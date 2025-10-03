using BlueSand.Core.Models;
using BlueSand.Core.Services;
using BlueSand.Core.Services.Writers;

static string? Arg(string[] args, string name)
{
    for (int i = 0; i < args.Length - 1; i++)
        if (args[i].Equals(name, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
    return null;
}
static bool Has(string[] args, string name) => args.Any(a => a.Equals(name, StringComparison.OrdinalIgnoreCase));

var configPath = Arg(args, "--config") ?? Path.Combine("config", "bluesand.yaml");
var outDir     = Arg(args, "--outdir") ?? "docs";
var dop        = int.TryParse(Arg(args, "--dop"), out var p) && p > 0 ? p : Environment.ProcessorCount;
var csvOnly    = Has(args, "--csv-only");
var validate   = Has(args, "--validate");
var sample     = int.TryParse(Arg(args, "--sample"), out var s) ? Math.Max(0, s) : 0;

Console.WriteLine($"BlueSand.Cli  | config={Path.GetFullPath(configPath)}  outDir={Path.GetFullPath(outDir)}  dop={dop}");
Directory.CreateDirectory(outDir);

BlueSandConfig cfg;
try { cfg = ConfigLoader.Load(configPath); }
catch (Exception ex)
{
    Console.Error.WriteLine($"ERROR loading config: {ex.Message}");
    return 2;
}

if (validate)
{
    Console.WriteLine("Config OK.");
    return 0;
}

var files = Scanner.DiscoverFiles(cfg);
if (sample > 0 && files.Count > sample) files = files.Take(sample).ToList();
Console.WriteLine($"Files to scan: {files.Count:N0}");

var hits = Scanner.Scan(cfg, files, dop);
if (hits.Count == 0)
{
    Console.WriteLine("No anchor term matches found.");
    return 0;
}

var tiers = Analyzer.TierTerms(hits, cfg.crest_threshold, cfg.slopes_threshold);

// outputs
CsvWriter.WriteRaw(Path.Combine(outDir, "WORDMAP_RAW.csv"), hits);

if (!csvOnly)
{
    MarkdownWriter.WriteWordMap(Path.Combine(outDir, "WORDMAP_TABLE.md"), tiers, hits);
    MarkdownWriter.WriteSummary(Path.Combine(outDir, "SUMMARY.md"), tiers, hits);
}

Console.WriteLine("Done.");
return 0;
