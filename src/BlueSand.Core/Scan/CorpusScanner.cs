using System.Collections.Concurrent;
using System.Text;
using System.Text.RegularExpressions;

using BlueSand.Core.Config;
using BlueSand.Core.Models;

namespace BlueSand.Core.Scan
{
	public class CorpusScanner
	{
		public async Task<int> RunAsync(ApplicationOptions opt, CancellationToken ct = default)
		{
			Directory.CreateDirectory(opt.OutputDir);

			var cfg = ScanEssentials.Load(opt.ConfigPath);
			

			// normalize extensions like "*.md" -> ".md"
			var extSet = cfg.Extensions
				.Select(e => e.Trim().TrimStart('*'))
				.Select(e => e.StartsWith('.') ? e.ToLowerInvariant() : "." + e.ToLowerInvariant())
				.ToHashSet();

			// build friendly path-specs (if any)
			var pathSpecs = BuildPathSpecs(cfg, opt);    // can be null if none configured

			// local func to identify generated files to skip
			bool IsGenerated(string p)
			{
				var name = Path.GetFileName(p);
				return name.Equals("SUMMARY.md", StringComparison.OrdinalIgnoreCase)
					|| name.Equals("WORDMAP_RAW.csv", StringComparison.OrdinalIgnoreCase)
					|| name.Equals("WORDMAP_TABLE.md", StringComparison.OrdinalIgnoreCase);
			}
			// gather files
			var allFiles = new List<string>(capacity: 32_000);
			var outRoot = Path.GetFullPath(opt.OutputDir).TrimEnd('\\', '/');
			foreach(var theRoot in cfg.IncludePaths.Where(Directory.Exists))
			{
				foreach(var theFile in Directory.EnumerateFiles(theRoot, "*", SearchOption.AllDirectories))
				{
					if(ct.IsCancellationRequested) break;
					var full = Path.GetFullPath(theFile);
					if(full.StartsWith(outRoot, StringComparison.OrdinalIgnoreCase)) continue; // <-- new
					if(IsGenerated(full)) continue;                                           // <-- new
					if(full.StartsWith(outRoot, StringComparison.OrdinalIgnoreCase)) continue; // avoid self-scan
					var ext = Path.GetExtension(full).ToLowerInvariant();
					if(!extSet.Contains(ext)) continue;
					if(pathSpecs?.IsExcluded(full) == true) continue;   // friendly glob-style excludes
					if(cfg.ExcludeDirRegex is not null && cfg.ExcludeDirRegex.IsMatch(full)) continue;
					if(cfg.ExcludeFileRegex is not null && cfg.ExcludeFileRegex.IsMatch(full)) continue;
					allFiles.Add(full);
				}
			}

			// compile term regexes once
			var termRegex = cfg.AnchorTerms
				.Where(t => !string.IsNullOrWhiteSpace(t))
				.Distinct(StringComparer.OrdinalIgnoreCase)
				.ToDictionary(t => t, t => new Regex(Regex.Escape(t), RegexOptions.IgnoreCase | RegexOptions.Compiled));

			var rows = new ConcurrentBag<TermOccurrence>();

			// progress
			var processed = 0;
			var total = allFiles.Count;
			var progressStep = Math.Max(250, total / 200); // ~0.5% steps
			Task progressTask = Task.CompletedTask;
			if(opt.ShowProgress && total > 0)
			{
				progressTask = Task.Run(async () =>
				{
					while(!ct.IsCancellationRequested && processed < total)
					{
						Console.Write($"\rScanning {processed}/{total} ({processed * 100 / total}%)   ");
						await Task.Delay(250, ct).ConfigureAwait(false);
					}
					Console.WriteLine($"\rScanning {total}/{total} (100%)   ");
				}, ct);
			}

			var po = new ParallelOptions
			{
				CancellationToken = ct,
				MaxDegreeOfParallelism = opt.MaxDegreeOfParallelism  ?? Environment.ProcessorCount
			};

			Parallel.ForEach(allFiles, po, file =>
			{
				try
				{
					if(cfg.MaxFileMb > 0)
					{
						var len = new FileInfo(file).Length;
						if(len > (long)cfg.MaxFileMb * 1024 * 1024) { Interlocked.Increment(ref processed); return; }
					}

					string text;
					try
					{
						// BOM aware; fallback if needed
						text = File.ReadAllText(file, Encoding.UTF8);
					}
					catch
					{
						text = File.ReadAllText(file); // fallback to system default
					}

					var isPlanned = cfg.PlannedHintsRegex.IsMatch(text);
					var isCode = cfg.CodeHintsRegex.IsMatch(text);
					var bucket = isPlanned && isCode ? "Overlap" : isPlanned ? "Planned" : isCode ? "Code" : "Unknown";
					BucketKind bucketKind = BucketKind.Unknown;
					if(Enum.TryParse<BucketKind>(bucket, out var bk))
					{
						bucketKind = bk;
					}
					var repo = RepoHeuristics.FromFullPath(file, cfg.IncludePaths);

					foreach(var kvp in termRegex)
					{
						var m = kvp.Value.Matches(text);
						if(m.Count == 0) continue;

						var idx = m[0].Index;
						var context = SliceLine(text, idx, 240);

						rows.Add(new TermOccurrence(
							Term: kvp.Key,
							Repository: repo,
							FilePath: file,
							Extension: Path.GetExtension(file).ToLowerInvariant(),
							Bucket: bucketKind,
							Frequency: m.Count,
							Context: context));
					}
				}
				finally
				{
					Interlocked.Increment(ref processed);
				}
			});

			await progressTask.ConfigureAwait(false);

			if(rows.IsEmpty)
			{
				Console.WriteLine("No anchor term matches found.");
				return 0;
			}

			// Aggregate to tiers
			var grouped = rows.GroupBy(r => r.Term, StringComparer.OrdinalIgnoreCase)
							  .Select(g => new { Term = g.Key, Total = g.Sum(r => r.Frequency) })
							  .OrderByDescending(x => x.Total)
							  .ToList();

			var max = grouped.First().Total;
			var tiers = grouped.Select(x =>
			{
				var score = max > 0 ? (double)x.Total / max : 0d;
				var tier = score >= cfg.CrestThreshold ? "Crest"
						 : score >= cfg.SlopesThreshold ? "Slopes"
						 : "Base";
				return new TermTier(x.Term, x.Total, Math.Round(score, 3), tier);
			}).ToList();

			// Write outputs (MD + CSV + SUMMARY)
			WriteWordmapTable(opt.OutputDir, tiers, rows.ToList());
			WriteRawCsv(opt.OutputDir, rows.ToList());
			WriteSummary(opt.OutputDir, rows.ToList(), tiers);

			Console.WriteLine($"Wrote {Path.Combine(opt.OutputDir, "WORDMAP_TABLE.md")}, {Path.Combine(opt.OutputDir, "WORDMAP_RAW.csv")}, {Path.Combine(opt.OutputDir, "SUMMARY.md")}");
			return 0;
		}

		static string SliceLine(string text, int index, int maxLen)
		{
			if(index < 0 || index >= text.Length) return "";
			var start = text.LastIndexOf('\n', Math.Min(index, text.Length - 1));
			var end = text.IndexOf('\n', index);
			if(start < 0) start = 0; else start++;
			if(end < 0) end = Math.Min(text.Length, start + maxLen);
			var s = text.AsSpan(start, Math.Min(end - start, maxLen));
			return Regex.Replace(s.ToString(), @"\s+", " ").Trim();
		}

		static void WriteWordmapTable(string outDir, IList<TermTier> tiers, IList<TermOccurrence> rows)
		{
			var sb = new StringBuilder();
			sb.AppendLine("# BlueSand Word Map");
			sb.AppendLine();
			sb.AppendLine("| Term | Tier | Total | Top Example |");
			sb.AppendLine("|---|---:|---:|---|");
			foreach(var t in tiers)
			{
				var ex = rows.Where(r => r.Term.Equals(t.Term, StringComparison.OrdinalIgnoreCase))
							 .OrderByDescending(r => r.Frequency)
							 .Select(r => r.Context.Replace("|", @"\|"))
							 .FirstOrDefault() ?? "";
				sb.AppendLine($"| {t.Term} | {t.Tier} | {t.Total} | {ex} |");
			}
			File.WriteAllText(Path.Combine(outDir, "WORDMAP_TABLE.md"), sb.ToString(), Encoding.UTF8);
		}

		static void WriteRawCsv(string outDir, IList<TermOccurrence> rows)
		{
			static string Esc(string s) => $"\"{s.Replace("\"", "\"\"")}\"";
			var sb = new StringBuilder();
			sb.AppendLine("Term,Repo,File,Ext,Bucket,Frequency,Context");
			foreach(var r in rows)
				sb.AppendLine($"{Esc(r.Term)},{Esc(r.Repository)},{Esc(r.FilePath)},{Esc(r.Extension)},{Esc(r.Bucket.ToString())},{r.Frequency},{Esc(r.Context)}");
			File.WriteAllText(Path.Combine(outDir, "WORDMAP_RAW.csv"), sb.ToString(), Encoding.UTF8);
		}

		static void WriteSummary(string outDir, IList<TermOccurrence> rows, IList<TermTier> tiers)
		{
			var sb = new StringBuilder();
			sb.AppendLine("# BlueSand Summary\n");

			var buckets = rows.GroupBy(r => r.Bucket.ToString())
							  .Select(g => new { Bucket = g.Key, Items = g.Count() })
							  .OrderByDescending(x => x.Items);

			sb.AppendLine("## Bucket Distribution\n\n| Bucket | Items |\n|---|---:|");
			foreach(var b in buckets) sb.AppendLine($"| {b.Bucket} | {b.Items} |");
			sb.AppendLine();

			var tierCounts = tiers.GroupBy(t => t.Tier)
								  .Select(g => new { Tier = g.Key, Terms = g.Count() })
								  .OrderByDescending(x => x.Terms);

			sb.AppendLine("## Tier Distribution (per term)\n\n| Tier | Terms |\n|---|---:|");
			foreach(var t in tierCounts) sb.AppendLine($"| {t.Tier} | {t.Terms} |");
			sb.AppendLine();

			var topRepos = rows.GroupBy(r => r.Repository)
							   .Select(g => new { Repo = g.Key, Items = g.Count() })
							   .OrderByDescending(x => x.Items)
							   .Take(10);

			sb.AppendLine("## Top Repos (by occurrences)\n\n| Repo | Items |\n|---|---:|");
			foreach(var r in topRepos) sb.AppendLine($"| {r.Repo} | {r.Items} |");
			sb.AppendLine();

			var topTerms = tiers.OrderByDescending(t => t.Total).Take(15);
			sb.AppendLine("## Top Terms\n\n| Term | Tier | Total | Score |\n|---|---|---:|---:|");
			foreach(var t in topTerms) sb.AppendLine($"| {t.Term} | {t.Tier} | {t.Total} | {t.Score:0.###} |");

			File.WriteAllText(Path.Combine(outDir, "SUMMARY.md"), sb.ToString(), Encoding.UTF8);

			// local func to avoid field-capture overhead in linq above
			//string r_BUCKET(TermOccurrence r) => r.Bucket.ToString();
		}

		private static PathSpecSet? BuildPathSpecs(ScanEssentials cfg, ApplicationOptions opt)
		{
			var lines = new List<string>();
			if(cfg.ExclusionPatterns is { Count: > 0 }) lines.AddRange(cfg.ExclusionPatterns);

			// load any ignorefiles listed in YAML
			if(cfg.ExclusionFiles is { Count: > 0 })
			{
				foreach(var f in cfg.ExclusionFiles)
				{
					try
					{
						var p = Path.IsPathRooted(f) ? f : Path.Combine(AppContext.BaseDirectory, f);
						if(File.Exists(p)) lines.AddRange(File.ReadAllLines(p));
					}
					catch { /* ignore */ }
				}
			}

			// optional: also load “.bluesandignore” that sit in include roots
			foreach(var root in cfg.IncludePaths.Where(Directory.Exists))
			{
				var local = Path.Combine(root, ".bluesandignore");
				if(File.Exists(local)) lines.AddRange(File.ReadAllLines(local));
			}

			return lines.Count == 0 ? null : new PathSpecSet(lines);
		}
	}
}


