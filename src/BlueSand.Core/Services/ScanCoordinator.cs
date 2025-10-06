using System.Collections.Concurrent;
using System.Text;
using System.Text.RegularExpressions;

using BlueSand.Core.Config;

namespace BlueSand.Core.Scan
{

	/// <summary>
	/// Orchestrates a full BlueSand scan:
	///   - Enumerates files per config (include_paths, extensions, excludes, size)
	///   - Scans concurrently for anchor terms + planned/code buckets
	///   - Aggregates totals and assigns tiers (Crest/Slopes/Base)
	/// Returns a ScanResult; writing/printing is handled elsewhere.
	/// </summary>
	public sealed class ScanCoordinator
	{
		public sealed record ScanProgress(int FilesProcessed, int TotalFiles);

		public async Task<ScanResult> ExecuteAsync(
			ScanEssentials cfg,
			ProcessingOptions options,
			IProgress<ScanProgress>? progress = null,
			CancellationToken cancellationToken = default)
		{
			ArgumentNullException.ThrowIfNull(cfg);

			// 1) Precompute filters/regex
			var includeRoots = cfg.IncludePaths
				.Where(p => !string.IsNullOrWhiteSpace(p))
				.Select(p => Environment.ExpandEnvironmentVariables(p).TrimEnd('\\', '/'))
				.Where(Directory.Exists)
				.ToArray();

			var allowedExtensions = NormalizeExtensions(cfg.Extensions);   // e.g. [".md",".cs",...]

			var termRegex = new Dictionary<string, Regex>(StringComparer.OrdinalIgnoreCase);
			foreach(var t in cfg.AnchorTerms.Where(t => !string.IsNullOrWhiteSpace(t)))
			{
				termRegex[t] = new Regex(Regex.Escape(t), RegexOptions.IgnoreCase | RegexOptions.Compiled);
			}

			// 2) Enumerate files
			var files = EnumerateFiles(includeRoots, allowedExtensions, cfg.ExcludeDirRegex, cfg.ExcludeFileRegex, cfg.MaxFileMb);
			var fileList = files.ToList(); // materialize to know total
			var total = fileList.Count;

			// 3) Scan concurrently
			var rows = new ConcurrentBag<WordMapRow>();
			var processed = 0;

			var parallelOptions = new ParallelOptions
			{
				CancellationToken = cancellationToken,
				MaxDegreeOfParallelism = options.MaxDegreeOfParallelism ?? Environment.ProcessorCount
			};

			await Task.Run(() =>
			{
				Parallel.ForEach(fileList, parallelOptions, fileInfo =>
				{
					try
					{
						var text = ReadAllTextUtf8(fileInfo.FullName);
						if(string.IsNullOrEmpty(text)) return;

						// Bucket detection (once per file)
						var isPlanned =cfg.PlannedHintsRegex.IsMatch(text);
						var isCode = cfg.CodeHintsRegex.IsMatch(text);
						var bucket = (isPlanned, isCode) switch
						{
							(true, true) => "Overlap",
							(true, false) => "Planned",
							(false, true) => "Code",
							_ => "Unknown"
						};

						foreach(var kvp in termRegex)
						{
							var term = kvp.Key;
							var re = kvp.Value;
							var matches = re.Matches(text);
							if(matches.Count == 0) continue;

							var firstIdx = matches[0].Index;
							var context = GetLineContext(text, firstIdx, 240);
							var repo = GuessRepo(fileInfo.FullName, includeRoots);

							rows.Add(new WordMapRow(
								Term: term,
								Repo: repo,
								File: fileInfo.FullName,
								Ext: fileInfo.Extension.ToLowerInvariant(),
								Bucket: bucket,
								Frequency: matches.Count,
								Context: context
							));
						}
					}
					catch
					{
						// Swallow file-specific issues; keep the scan moving.
					}
					finally
					{
						var done = Interlocked.Increment(ref processed);
						if(options.ShowProgress && (done % 250 == 0))
							progress?.Report(new ScanProgress(done, total));
					}
				});
			}, cancellationToken).ConfigureAwait(false);

			// 4) Aggregate → tiers
			var tiers = ComputeTiers(rows, cfg.CrestThreshold, cfg.SlopesThreshold);

			// 5) Summaries (for CLI/reporting)
			var summaries = BuildSummaries(rows, tiers);

			return new ScanResult(rows.ToArray(), tiers, summaries);
		}

		// ---------- helpers ----------

		private static IEnumerable<FileInfo> EnumerateFiles(
			IReadOnlyList<string> roots,
			HashSet<string> allowedExts,
			Regex excludeDirRe,
			Regex? excludeFileRe,
			int maxFileMb)
		{
			foreach(var root in roots)
			{
				// Non-recursive stack walk to cheaply skip excluded dirs
				var dirs = new Stack<DirectoryInfo>();
				dirs.Push(new DirectoryInfo(root));

				while(dirs.Count > 0)
				{
					DirectoryInfo dir;
					try { dir = dirs.Pop(); }
					catch { continue; }

					// Skip excluded dirs by full path
					if(excludeDirRe.IsMatch(dir.FullName))
						continue;

					// Push subdirs
					DirectoryInfo[] subDirs;
					try { subDirs = dir.GetDirectories(); }
					catch { continue; }

					foreach(var sd in subDirs)
					{
						if(!excludeDirRe.IsMatch(sd.FullName))
							dirs.Push(sd);
					}

					// Files
					FileInfo[] files;
					try { files = dir.GetFiles(); }
					catch { continue; }

					foreach(var f in files)
					{
						// extension filter
						var ext = f.Extension.ToLowerInvariant();
						if(!allowedExts.Contains(ext)) continue;

						// file regex exclude
						if(excludeFileRe is not null && excludeFileRe.IsMatch(f.FullName))
							continue;

						// size filter
						if(maxFileMb > 0 && f.Length > (long)maxFileMb * 1024L * 1024L)
							continue;

						yield return f;
					}
				}
			}
		}

		private static HashSet<string> NormalizeExtensions(IEnumerable<string> patterns)
		{
			var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			foreach(var p in patterns)
			{
				if(string.IsNullOrWhiteSpace(p)) continue;
				// accept "*.md" or ".md"
				var ext = p.Trim();
				if(ext.StartsWith("*.", StringComparison.Ordinal))
					ext = ext[1..]; // drop leading '*'
				if(!ext.StartsWith(".", StringComparison.Ordinal))
					ext = "." + ext.Trim('*');

				set.Add(ext.ToLowerInvariant());
			}
			return set;
		}

		private static string ReadAllTextUtf8(string path)
		{
			try
			{
				return File.ReadAllText(path, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false));
			}
			catch
			{
				// As a fallback, try default encoding (rarely helps, but cheap)
				try { return File.ReadAllText(path); } catch { return string.Empty; }
			}
		}

		private static string GetLineContext(string text, int index, int maxLen)
		{
			if(index < 0 || string.IsNullOrEmpty(text)) return string.Empty;
			var start = text.LastIndexOf('\n', Math.Min(index, text.Length - 1));
			var end = text.IndexOf('\n', index);
			if(start < 0) start = 0; else start += 1;
			if(end < 0) end = Math.Min(text.Length, start + maxLen);
			var len = Math.Min(end - start, maxLen);
			var slice = (start >= 0 && start + len <= text.Length) ? text.Substring(start, len) : string.Empty;
			return Regex.Replace(slice, @"\s+", " ").Trim();
		}

		private static string GuessRepo(string fullPath, IReadOnlyList<string> roots)
		{
			foreach(var r in roots)
			{
				if(fullPath.StartsWith(r, StringComparison.OrdinalIgnoreCase))
				{
					var rel = fullPath.Substring(r.Length).TrimStart('\\', '/');
					if(rel.Length == 0) break;
					var first = rel.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[0];
					return string.IsNullOrWhiteSpace(first) ? "(root)" : first;
				}
			}
			// fallback: C:\A\B\C\file -> B
			var parts = fullPath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
			return parts.Length >= 3 ? parts[2] : parts[^1];
		}

		private static IReadOnlyList<TermTier> ComputeTiers(IEnumerable<WordMapRow> rows, double crest, double slopes)
		{
			var totals = rows
				.GroupBy(r => r.Term, StringComparer.OrdinalIgnoreCase)
				.Select(g => new { Term = g.Key, Total = g.Sum(x => x.Frequency) })
				.OrderByDescending(x => x.Total)
				.ToList();

			if(totals.Count == 0) return Array.Empty<TermTier>();

			var max = (double)totals[0].Total;
			var list = new List<TermTier>(totals.Count);
			foreach(var t in totals)
			{
				var score = max > 0 ? t.Total / max : 0.0;
				var tier = score >= crest ? "Crest"
						 : score >= slopes ? "Slopes"
						 : "Base";
				list.Add(new TermTier(t.Term, t.Total, Math.Round(score, 3), tier));
			}
			return list;
		}

		private static ScanSummaries BuildSummaries(IReadOnlyCollection<WordMapRow> rows, IReadOnlyCollection<TermTier> tiers)
		{
			var buckets = rows
				.GroupBy(r => r.Bucket, StringComparer.OrdinalIgnoreCase)
				.Select(g => new BucketSummary(g.Key, g.Count()))
				.OrderByDescending(b => b.Items)
				.ToArray();

			var tierCounts = tiers
				.GroupBy(t => t.Tier, StringComparer.OrdinalIgnoreCase)
				.Select(g => new TierCount(g.Key, g.Count()))
				.OrderByDescending(t => t.Terms)
				.ToArray();

			var topRepos = rows
				.GroupBy(r => r.Repo, StringComparer.OrdinalIgnoreCase)
				.Select(g => new RepoCount(g.Key, g.Count()))
				.OrderByDescending(r => r.Items)
				.Take(10)
				.ToArray();

			var topTerms = tiers
				.OrderByDescending(t => t.Total)
				.Take(15)
				.ToArray();

			return new ScanSummaries(buckets, tierCounts, topRepos, topTerms);
		}
	}

	// ---------- Result & small DTOs ----------

	public sealed record WordMapRow(
		string Term,
		string Repo,
		string File,
		string Ext,
		string Bucket,
		int Frequency,
		string Context);

	public sealed record TermTier(
		string Term,
		int Total,
		double Score,
		string Tier);

	public sealed record BucketSummary(string Bucket, int Items);
	public sealed record TierCount(string Tier, int Terms);
	public sealed record RepoCount(string Repo, int Items);

	public sealed record ScanSummaries(
		IReadOnlyList<BucketSummary> Buckets,
		IReadOnlyList<TierCount> TierDistribution,
		IReadOnlyList<RepoCount> TopRepos,
		IReadOnlyList<TermTier> TopTerms);

	public sealed record ScanResult(
		IReadOnlyList<WordMapRow> Rows,
		IReadOnlyList<TermTier> Tiers,
		ScanSummaries Summaries);
}