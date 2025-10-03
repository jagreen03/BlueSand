using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using CsvHelper;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

record Config(
    List<string> include_paths,
    string exclude_dir_regex,
    List<string> extensions,
    List<string> anchor_terms,
    string planned_hints_regex,
    string code_hints_regex,
    double crest_threshold,
    double slopes_threshold
);

record Row(
    string Term,
    string Repo,
    string File,
    string Ext,
    string Bucket,
    int    Frequency,
    string Context
);

static class Program
{
    static int Main(string[] args)
    {
        var configPath = args.FirstOrDefault(a => a.StartsWith("--config="))?.Split('=')[1] ?? "config/bluesand.yaml";
        var outDir     = args.FirstOrDefault(a => a.StartsWith("--out="))?.Split('=')[1]     ?? "docs";

        // 1) Load YAML (ASCII-safe; env expansion on $env:…)
        var cfgText = File.ReadAllText(configPath, Encoding.UTF8);
        cfgText = ExpandEnv(cfgText); // handle $env:USERPROFILE\… style
        var cfg = new DeserializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .Build()
            .Deserialize<Config>(cfgText)
            ?? throw new Exception("Failed to parse config");

        if (cfg.include_paths is null || cfg.include_paths.Count == 0)
            throw new Exception("'include_paths' is empty");

        Directory.CreateDirectory(outDir);

        // 2) Prepare filters/regex (compile for speed)
        var excludeRe   = new Regex(cfg.exclude_dir_regex, RegexOptions.IgnoreCase | RegexOptions.Compiled);
        var plannedRe   = new Regex(cfg.planned_hints_regex, RegexOptions.IgnoreCase | RegexOptions.Compiled);
        var codeRe      = new Regex(cfg.code_hints_regex,    RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // Anchor terms → compile each as a literal regex (or build one big alternation)
        var termRegexes = cfg.anchor_terms
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(t => (term: t, re: new Regex(Regex.Escape(t), RegexOptions.IgnoreCase | RegexOptions.Compiled)))
            .ToArray();

        // 3) Gather files (fast enumeration)
        var allFiles = new List<string>(capacity: 1 << 16);
        foreach (var root in cfg.include_paths)
        {
            var rootExpanded = ExpandEnv(root);
            if (!Directory.Exists(rootExpanded)) continue;

            var enumOpts = new EnumerationOptions {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.ReparsePoint
            };

            foreach (var path in Directory.EnumerateFiles(rootExpanded, "*", enumOpts))
            {
                var ext = Path.GetExtension(path).ToLowerInvariant();
                if (!cfg.extensions.Contains($"*{ext}", StringComparer.OrdinalIgnoreCase)) continue;
                if (excludeRe.IsMatch(path)) continue;
                allFiles.Add(path);
            }
        }

        if (allFiles.Count == 0)
        {
            Console.WriteLine("No files matched include_paths/extensions.");
            return 0;
        }

        // 4) Scan quickly (parallel)
        var rows = new ConcurrentBag<Row>();
        var total = allFiles.Count;
        var processed = 0;
        var sw = System.Diagnostics.Stopwatch.StartNew();

        Parallel.ForEach(
            allFiles,
            new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount },
            file =>
            {
                string text;
                try { text = File.ReadAllText(file, Encoding.UTF8); }
                catch { return; }

                var repo = GetRepoName(file, cfg.include_paths);
                var ext = Path.GetExtension(file).ToLowerInvariant();

                // contextual “bucket” once per file
                var isPlanned = plannedRe.IsMatch(text);
                var isCode    = codeRe.IsMatch(text);
                var bucket = (isPlanned, isCode) switch {
                    (true, true) => "Overlap",
                    (true, false) => "Planned",
                    (false, true) => "Code",
                    _ => "Unknown"
                };

                foreach (var (term, re) in termRegexes)
                {
                    var matches = re.Matches(text);
                    if (matches.Count == 0) continue;

                    // grab first line as context
                    var firstIdx = matches[0].Index;
                    var line = GetLineSnippet(text, firstIdx, maxLen: 240);

                    rows.Add(new Row(term, repo, file, ext, bucket, matches.Count, line));
                }

                var done = Interlocked.Increment(ref processed);
                if (done % 500 == 0)
                {
                    var pct = (int)(done * 100.0 / total);
                    Console.WriteLine($"Scanning… {done}/{total} ({pct}%)");
                }
            });

        sw.Stop();
        Console.WriteLine($"Scan complete in {sw.Elapsed.TotalSeconds:F1}s. Hits: {rows.Count}");

        if (rows.IsEmpty)
        {
            Console.WriteLine("No anchor term matches found.");
            return 0;
        }

        // 5) Aggregate → tiers
        var byTerm = rows
            .GroupBy(r => r.Term, StringComparer.OrdinalIgnoreCase)
            .Select(g => new { Term = g.Key, Total = g.Sum(r => r.Frequency) })
            .OrderByDescending(x => x.Total)
            .ToList();

        var max = byTerm.First().Total;
        var crest = cfg.crest_threshold;
        var slopes = cfg.slopes_threshold;

        var withTier = byTerm.Select(x =>
        {
            var score = max > 0 ? (double)x.Total / max : 0.0;
            var tier = score >= crest ? "Crest" : score >= slopes ? "Slopes" : "Base";
            return new { x.Term, x.Total, Score = Math.Round(score, 3), Tier = tier };
        }).ToList();

        // 6) Write WORDMAP_TABLE.md
        var mdPath = Path.Combine(outDir, "WORDMAP_TABLE.md");
        using (var md = new StreamWriter(mdPath, false, new UTF8Encoding(false)))
        {
            md.WriteLine("# BlueSand Word Map");
            md.WriteLine();
            md.WriteLine("| Term | Tier | Total | Top Example |");
            md.WriteLine("|---|---:|---:|---|");
            foreach (var r in withTier)
            {
                var topCtx = rows
                    .Where(x => x.Term.Equals(r.Term, StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(x => x.Frequency)
                    .Select(x => x.Context.Replace("|", "\\|"))
                    .FirstOrDefault() ?? "";
                md.WriteLine($"| {r.Term} | {r.Tier} | {r.Total} | {topCtx} |");
            }
        }

        // 7) Write RAW CSV
        var csvPath = Path.Combine(outDir, "WORDMAP_RAW.csv");
        using (var writer = new StreamWriter(csvPath, false, new UTF8Encoding(false)))
        using (var csv = new CsvWriter(writer, CultureInfo.InvariantCulture))
        {
            csv.WriteRecords(rows);
        }

        // 8) Summary
        var bucketSummary = rows.GroupBy(r => r.Bucket).Select(g => new { Bucket = g.Key, Items = g.Count() }).OrderByDescending(x => x.Items);
        var tierSummary   = withTier.GroupBy(r => r.Tier).Select(g => new { Tier = g.Key, Terms = g.Count() }).OrderByDescending(x => x.Terms);
        var topRepos      = rows.GroupBy(r => r.Repo).Select(g => new { Repo = g.Key, Items = g.Count() }).OrderByDescending(x => x.Items).Take(10);
        var topTerms      = withTier.OrderByDescending(x => x.Total).Take(15);

        var sumPath = Path.Combine(outDir, "SUMMARY.md");
        using (var sum = new StreamWriter(sumPath, false, new UTF8Encoding(false)))
        {
            sum.WriteLine("# BlueSand Summary\n");
            sum.WriteLine("## Bucket Distribution\n");
            sum.WriteLine("| Bucket | Items |");
            sum.WriteLine("|---|---:|");
            foreach (var b in bucketSummary) sum.WriteLine($"| {b.Bucket} | {b.Items} |");
            sum.WriteLine("\n## Tier Distribution (per term)\n");
            sum.WriteLine("| Tier | Terms |");
            sum.WriteLine("|---|---:|");
            foreach (var t in tierSummary) sum.WriteLine($"| {t.Tier} | {t.Terms} |");
            sum.WriteLine("\n## Top Repos (by occurrences)\n");
            sum.WriteLine("| Repo | Items |");
            sum.WriteLine("|---|---:|");
            foreach (var r in topRepos) sum.WriteLine($"| {r.Repo} | {r.Items} |");
            sum.WriteLine("\n## Top Terms\n");
            sum.WriteLine("| Term | Tier | Total | Score |");
            sum.WriteLine("|---|---|---:|---:|");
            foreach (var t in topTerms) sum.WriteLine($"| {t.Term} | {t.Tier} | {t.Total} | {t.Score:F3} |");
        }

        Console.WriteLine($"Wrote {mdPath}, {csvPath}, {sumPath}");
        return 0;
    }

    static string GetRepoName(string fullPath, IEnumerable<string> roots)
    {
        foreach (var r in roots)
        {
            var root = ExpandEnv(r);
            if (fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                var rel = fullPath.Substring(root.Length).TrimStart('\\','/');
                if (rel.Length == 0) break;
                return rel.Split(new[]{'\\','/'}, 2)[0];
            }
        }
        // fallback (C:\root\repo\… -> "repo")
        var parts = fullPath.Split('\\','/');
        return parts.Length > 2 ? parts[2] : parts.Last();
    }

    static string ExpandEnv(string s)
    {
        // Support $env:USERPROFILE and %USERPROFILE%
        var expanded = Environment.ExpandEnvironmentVariables(s);
        // Replace $env:NAME with actual env var if present
        return Regex.Replace(expanded, @"\$env:([A-Za-z_][A-Za-z0-9_]*)", m =>
        {
            var key = m.Groups[1].Value;
            return Environment.GetEnvironmentVariable(key) ?? m.Value;
        });
    }

    static string GetLineSnippet(string text, int index, int maxLen)
    {
        int start = text.LastIndexOf('\n', index);
        int end   = text.IndexOf('\n', index);
        if (start < 0) start = 0; else start++;
        if (end   < 0) end   = Math.Min(text.Length, start + maxLen);
        var line = text.Substring(start, Math.Min(end - start, maxLen));
        return Regex.Replace(line, @"\s+", " ").Trim();
    }
}
