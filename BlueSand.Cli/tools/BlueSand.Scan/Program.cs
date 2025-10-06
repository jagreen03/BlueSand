using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

internal sealed class BlueSandConfig
{
    public List<string> IncludePaths { get; init; } = new();
    public string ExcludeDirRegex { get; init; } = "";
    public List<string> Extensions { get; init; } = new();
    public List<string> AnchorTerms { get; init; } = new();
    public string PlannedHintsRegex { get; init; } = "";
    public string CodeHintsRegex { get; init; } = "";
    public string? ExcludeFileRegex { get; init; }
    public double CrestThreshold { get; init; } = 0.90;
    public double SlopesThreshold { get; init; } = 0.60;
    public int MaxFileMb { get; init; } = 0;
}

internal sealed class HitRow
{
    public string Term { get; init; } = "";
    public string Repo { get; init; } = "";
    public string File { get; init; } = "";
    public string Ext { get; init; } = "";
    public string Bucket { get; init; } = "";
    public int Frequency { get; init; }
    public string Context { get; init; } = "";
}

internal static class Program
{
    static int Main(string[] args)
    {
        // Args: --config <path> --outdir <path> --dop <int>
        string repoRoot = FindRepoRoot(); // not critical, just for nicer messages
        string configPath = GetArg(args, "--config") ?? Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "config", "bluesand.yaml");
        string outDir = GetArg(args, "--outdir") ?? Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "docs");
        int degree = int.TryParse(GetArg(args, "--dop"), out var dop) && dop > 0 ? dop : Environment.ProcessorCount;

        Console.WriteLine($"BlueSand.Scan");
        Console.WriteLine($"  config: {Path.GetFullPath(configPath)}");
        Console.WriteLine($"  outDir: {Path.GetFullPath(outDir)}");
        Console.WriteLine($"  dop:    {degree}");

        if (!File.Exists(configPath))
        {
            Console.Error.WriteLine("ERROR: config file not found.");
            return 2;
        }

        BlueSandConfig cfg;
        try
        {
            cfg = LoadYaml(configPath);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"ERROR: YAML parse failed: {ex.Message}");
            return 3;
        }

        if (cfg.IncludePaths.Count == 0)
        {
            Console.Error.WriteLine("ERROR: include_paths is empty.");
            return 4;
        }
        Directory.CreateDirectory(outDir);

        var excludeDirRe = SafeRegex(cfg.ExcludeDirRegex, RegexOptions.IgnoreCase);
        Regex? excludeFileRe = null;
        if (!string.IsNullOrWhiteSpace(cfg.ExcludeFileRegex))
            excludeFileRe = SafeRegex(cfg.ExcludeFileRegex!, RegexOptions.IgnoreCase);

        var plannedRe = SafeRegex(cfg.PlannedHintsRegex, RegexOptions.IgnoreCase | RegexOptions.Compiled);
        var codeRe    = SafeRegex(cfg.CodeHintsRegex,    RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // compile extension set to lower, with leading "*."
        var extSet = new HashSet<string>(cfg.Extensions.Select(e => e.Trim().ToLowerInvariant()), StringComparer.OrdinalIgnoreCase);

        // compile terms
        var termRegexMap = new Dictionary<string, Regex>(StringComparer.OrdinalIgnoreCase);
        foreach (var term in cfg.AnchorTerms)
        {
            if (string.IsNullOrWhiteSpace(term)) continue;
            var r = new Regex(Regex.Escape(term), RegexOptions.IgnoreCase | RegexOptions.Compiled);
            termRegexMap[term] = r;
        }

        // expand env in include paths
        var includeRoots = cfg.IncludePaths
            .Select(ExpandEnv)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .ToArray();

        // enumerate files
        var allFiles = new List<FileInfo>();
        foreach (var root in includeRoots)
        {
            if (!Directory.Exists(root)) continue;

            var dirEnum = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories);
            foreach (var f in dirEnum)
            {
                // dir exclusions
                if (excludeDirRe.IsMatch(f)) continue;

                var fi = new FileInfo(f);
                var ext = fi.Extension.ToLowerInvariant();
                var glob = $"*{ext}";
                if (!extSet.Contains(glob)) continue;

                if (excludeFileRe != null && excludeFileRe.IsMatch(fi.FullName)) continue;

                if (cfg.MaxFileMb > 0)
                {
                    long maxBytes = (long)cfg.MaxFileMb * 1024 * 1024;
                    if (fi.Length > maxBytes) continue;
                }

                allFiles.Add(fi);
            }
        }

        Console.WriteLine($"Files to scan: {allFiles.Count:N0}");

        var hits = new ConcurrentBag<HitRow>();

        var po = new ParallelOptions { MaxDegreeOfParallelism = degree };
        Parallel.ForEach(allFiles, po, fi =>
        {
            string text;
            try
            {
                // UTF8 read; if it fails, skip
                text = File.ReadAllText(fi.FullName, Encoding.UTF8);
            }
            catch
            {
                return;
            }

            bool isPlanned = plannedRe.IsMatch(text);
            bool isCode = codeRe.IsMatch(text);
            string bucket = isPlanned && isCode ? "Overlap"
                           : isPlanned ? "Planned"
                           : isCode    ? "Code"
                           : "Unknown";

            foreach (var kvp in termRegexMap)
            {
                var re = kvp.Value;
                var mc = re.Matches(text);
                if (mc.Count == 0) continue;

                int index = mc[0].Index;
                string context = GetLineContext(text, index, 240);

                hits.Add(new HitRow
                {
                    Term = kvp.Key,
                    Repo = GetRepoName(fi.FullName, includeRoots),
                    File = fi.FullName,
                    Ext = fi.Extension.ToLowerInvariant(),
                    Bucket = bucket,
                    Frequency = mc.Count,
                    Context = context
                });
            }
        });

        if (hits.IsEmpty)
        {
            Console.WriteLine("No anchor term matches found.");
            return 0;
        }

        // aggregate per-term totals
        var totals = hits
            .GroupBy(h => h.Term, StringComparer.OrdinalIgnoreCase)
            .Select(g => new { Term = g.Key, Total = g.Sum(r => r.Frequency) })
            .OrderByDescending(x => x.Total)
            .ToList();

        double max = totals.Count > 0 ? totals[0].Total : 0.0;
        var tiers = totals.Select(t =>
        {
            double score = max > 0 ? t.Total / max : 0.0;
            string tier = score >= cfg.CrestThreshold ? "Crest"
                      : score >= cfg.SlopesThreshold ? "Slopes"
                      : "Base";
            return new { t.Term, t.Total, Score = Math.Round(score, 3), Tier = tier };
        }).ToList();

        // write WORDMAP_TABLE.md
        var md = new StringBuilder();
        md.AppendLine("# BlueSand Word Map");
        md.AppendLine();
        md.AppendLine("| Term | Tier | Total | Top Example |");
        md.AppendLine("|---|---:|---:|---|");
        foreach (var t in tiers)
        {
            string example = hits
                .Where(h => string.Equals(h.Term, t.Term, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(h => h.Frequency)
                .Select(h => h.Context)
                .FirstOrDefault() ?? "";

            example = example.Replace("|", "\\|");
            md.AppendLine($"| {t.Term} | {t.Tier} | {t.Total} | {example} |");
        }
        File.WriteAllText(Path.Combine(outDir, "WORDMAP_TABLE.md"), md.ToString(), new UTF8Encoding(false));

        // write WORDMAP_RAW.csv
        var csv = new StringBuilder();
        csv.AppendLine("Term,Repo,File,Ext,Bucket,Frequency,Context");
        foreach (var h in hits.OrderBy(h => h.Term).ThenBy(h => h.Repo))
        {
            csv.AppendLine(string.Join(",",
                Csv(h.Term),
                Csv(h.Repo),
                Csv(h.File),
                Csv(h.Ext),
                Csv(h.Bucket),
                h.Frequency.ToString(CultureInfo.InvariantCulture),
                Csv(h.Context)
            ));
        }
        File.WriteAllText(Path.Combine(outDir, "WORDMAP_RAW.csv"), csv.ToString(), new UTF8Encoding(false));

        // write SUMMARY.md
        var sum = new StringBuilder();
        sum.AppendLine("# BlueSand Summary");
        sum.AppendLine();
        sum.AppendLine("## Bucket Distribution");
        sum.AppendLine();
        sum.AppendLine("| Bucket | Items |");
        sum.AppendLine("|---|---:|");
        foreach (var b in hits.GroupBy(h => h.Bucket).OrderByDescending(g => g.Count()))
            sum.AppendLine($"| {b.Key} | {b.Count()} |");

        sum.AppendLine();
        sum.AppendLine("## Tier Distribution (per term)");
        sum.AppendLine();
        sum.AppendLine("| Tier | Terms |");
        sum.AppendLine("|---|---:|");
        foreach (var g in tiers.GroupBy(x => x.Tier).OrderByDescending(g => g.Count()))
            sum.AppendLine($"| {g.Key} | {g.Count()} |");

        sum.AppendLine();
        sum.AppendLine("## Top Repos (by occurrences)");
        sum.AppendLine();
        sum.AppendLine("| Repo | Items |");
        sum.AppendLine("|---|---:|");
        foreach (var r in hits.GroupBy(h => h.Repo).OrderByDescending(g => g.Count()).Take(10))
            sum.AppendLine($"| {r.Key} | {r.Count()} |");

        sum.AppendLine();
        sum.AppendLine("## Top Terms");
        sum.AppendLine();
        sum.AppendLine("| Term | Tier | Total | Score |");
        sum.AppendLine("|---|---|---:|---:|");
        foreach (var t in tiers.OrderByDescending(t => t.Total).Take(15))
            sum.AppendLine($"| {t.Term} | {t.Tier} | {t.Total} | {t.Score.ToString("0.###", CultureInfo.InvariantCulture)} |");

        File.WriteAllText(Path.Combine(outDir, "SUMMARY.md"), sum.ToString(), new UTF8Encoding(false));

        Console.WriteLine("Wrote docs/WORDMAP_TABLE.md, docs/WORDMAP_RAW.csv, docs/SUMMARY.md");
        return 0;
    }

    // ---- helpers ------------------------------------------------------------

    static string? GetArg(string[] args, string name)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        }
        return null;
    }

    static string ExpandEnv(string path)
        => Environment.ExpandEnvironmentVariables(path ?? "");

    static Regex SafeRegex(string pattern, RegexOptions opts)
    {
        try
        {
            return new Regex(pattern ?? "", opts);
        }
        catch
        {
            // Fallback to a match-nothing regex if pattern is bad
            return new Regex("(?!x)x", opts);
        }
    }

    static string GetLineContext(string text, int index, int maxLen)
    {
        if (string.IsNullOrEmpty(text) || index < 0) return "";
        int start = text.LastIndexOf('\n', Math.Min(index, text.Length - 1));
        int end   = text.IndexOf('\n', index);
        if (start < 0) start = 0; else start++;
        if (end   < 0) end = Math.Min(text.Length, start + maxLen);
        var slice = text.Substring(start, Math.Min(end - start, maxLen));
        return Regex.Replace(slice, "\\s+", " ").Trim();
    }

    static string GetRepoName(string fullPath, string[] roots)
    {
        foreach (var r in roots)
        {
            if (fullPath.StartsWith(r, StringComparison.OrdinalIgnoreCase))
            {
                var rel = fullPath.Substring(r.Length).TrimStart('\\', '/');
                if (!string.IsNullOrEmpty(rel))
                {
                    var first = rel.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[0];
                    return first;
                }
            }
        }
        // fallback: C:\X\Y\Z -> Y (index 2)
        var parts = fullPath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (parts.Length >= 3) return parts[2];
        return parts[^1];
    }

    static string Csv(string s)
    {
        if (s == null) return "";
        bool mustQuote = s.Contains(',') || s.Contains('"') || s.Contains('\n') || s.Contains('\r');
        if (!mustQuote) return s;
        return "\"" + s.Replace("\"", "\"\"") + "\"";
    }

    static BlueSandConfig LoadYaml(string path)
    {
        // Minimal YAML: key: value OR key: [a,b] OR:
        // key:
        //   - item
        //   - item
        var cfg = new BlueSandConfig();
        var lines = File.ReadAllLines(path, Encoding.UTF8);
        string? currentKey = null;

        for (int i = 0; i < lines.Length; i++)
        {
            var raw = lines[i];
            var t = raw.Trim();
            if (t.Length == 0 || t.StartsWith("#")) continue;

            var mKey = Regex.Match(t, @"^([A-Za-z0-9_]+):\s*(.*)$");
            if (mKey.Success)
            {
                currentKey = mKey.Groups[1].Value;
                var rest = mKey.Groups[2].Value.Trim();

                if (rest.StartsWith("[") && rest.EndsWith("]"))
                {
                    var inner = rest.Substring(1, rest.Length - 2);
                    var items = inner.Split(',')
                        .Select(x => x.Trim().Trim('"', '\''))
                        .Where(x => x.Length > 0)
                        .ToList();
                    SetList(cfg, currentKey, items);
                }
                else if (rest.Length > 0)
                {
                    SetScalar(cfg, currentKey, rest.Trim('"', '\''));
                }
                else
                {
                    // expect dash-list
                    SetList(cfg, currentKey, new List<string>());
                }
                continue;
            }

            // dash item for current list
            var mItem = Regex.Match(t, @"^-\s*(.*)$");
            if (mItem.Success && currentKey != null)
            {
                var val = mItem.Groups[1].Value.Trim().Trim('"', '\'');
                AppendList(cfg, currentKey, val);
                continue;
            }

            throw new InvalidOperationException($"YAML parse error at line {i + 1}: {t}");
        }

        return cfg;
    }

    static void SetList(BlueSandConfig cfg, string key, List<string> items)
    {
        switch (key)
        {
            case "include_paths": cfg.IncludePaths = items; break;
            case "extensions": cfg.Extensions = items; break;
            case "anchor_terms": cfg.AnchorTerms = items; break;
            default:
                // ignore unknown lists
                break;
        }
    }

    static void AppendList(BlueSandConfig cfg, string key, string value)
    {
        switch (key)
        {
            case "include_paths": cfg.IncludePaths.Add(value); break;
            case "extensions": cfg.Extensions.Add(value); break;
            case "anchor_terms": cfg.AnchorTerms.Add(value); break;
            default:
                break;
        }
    }

    static void SetScalar(BlueSandConfig cfg, string key, string value)
    {
        switch (key)
        {
            case "exclude_dir_regex": cfg.ExcludeDirRegex = value; break;
            case "planned_hints_regex": cfg.PlannedHintsRegex = value; break;
            case "code_hints_regex": cfg.CodeHintsRegex = value; break;
            case "crest_threshold": cfg.CrestThreshold = ParseDouble(value, 0.90); break;
            case "slopes_threshold": cfg.SlopesThreshold = ParseDouble(value, 0.60); break;
            case "exclude_file_regex": cfg.ExcludeFileRegex = value; break;
            case "max_file_mb": cfg.MaxFileMb = ParseInt(value, 0); break;
            default:
                // ignore unknown scalars
                break;
        }
    }

    static double ParseDouble(string s, double @default)
        => double.TryParse(StripComment(s), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : @default;

    static int ParseInt(string s, int @default)
        => int.TryParse(StripComment(s), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : @default;

    static string StripComment(string s)
        => (s ?? "").Split('#')[0].Trim();

    static string FindRepoRoot()
    {
        try
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                if (Directory.Exists(Path.Combine(dir.FullName, ".git"))) return dir.FullName;
                dir = dir.Parent;
            }
        }
        catch { }
        return AppContext.BaseDirectory;
    }
}
