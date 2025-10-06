using System.Globalization;
using System.Text;
using BlueSand.Core.Models;

namespace BlueSand.Core.Services.Writers;

public static class MarkdownWriter
{
    public static void WriteWordMap(string path, IEnumerable<Analyzer.TierRow> tiers, IEnumerable<HitRow> hits)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# BlueSand Word Map");
        sb.AppendLine();
        sb.AppendLine("| Term | Tier | Total | Top Example |");
        sb.AppendLine("|---|---:|---:|---|");
        foreach (var t in tiers)
        {
            var example = hits
                .Where(h => h.Term.Equals(t.Term, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(h => h.Frequency)
                .Select(h => h.Context)
                .FirstOrDefault() ?? "";
            example = example.Replace("|", "\\|");
            sb.AppendLine($"| {t.Term} | {t.Tier} | {t.Total.ToString("0", CultureInfo.InvariantCulture)} | {example} |");
        }
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
    }

    public static void WriteSummary(string path, IEnumerable<Analyzer.TierRow> tiers, IEnumerable<HitRow> hits)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# BlueSand Summary");
        sb.AppendLine();
        sb.AppendLine("## Bucket Distribution");
        sb.AppendLine();
        sb.AppendLine("| Bucket | Items |");
        sb.AppendLine("|---|---:|");
        foreach (var g in hits.GroupBy(h => h.Bucket).OrderByDescending(g => g.Count()))
            sb.AppendLine($"| {g.Key} | {g.Count()} |");

        sb.AppendLine();
        sb.AppendLine("## Tier Distribution (per term)");
        sb.AppendLine();
        sb.AppendLine("| Tier | Terms |");
        sb.AppendLine("|---|---:|");
        foreach (var g in tiers.GroupBy(x => x.Tier).OrderByDescending(g => g.Count()))
            sb.AppendLine($"| {g.Key} | {g.Count()} |");

        sb.AppendLine();
        sb.AppendLine("## Top Repos (by occurrences)");
        sb.AppendLine();
        sb.AppendLine("| Repo | Items |");
        sb.AppendLine("|---|---:|");
        foreach (var g in hits.GroupBy(h => h.Repo).OrderByDescending(g => g.Count()).Take(10))
            sb.AppendLine($"| {g.Key} | {g.Count()} |");

        sb.AppendLine();
        sb.AppendLine("## Top Terms");
        sb.AppendLine();
        sb.AppendLine("| Term | Tier | Total | Score |");
        sb.AppendLine("|---|---|---:|---:|");
        foreach (var t in tiers.OrderByDescending(x => x.Total).Take(15))
            sb.AppendLine($"| {t.Term} | {t.Tier} | {t.Total.ToString("0", CultureInfo.InvariantCulture)} | {t.Score.ToString("0.###", CultureInfo.InvariantCulture)} |");

        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
    }
}
