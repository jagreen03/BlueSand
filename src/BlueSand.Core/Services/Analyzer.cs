using BlueSand.Core.Models;

namespace BlueSand.Core.Services;

public static class Analyzer
{
    public sealed record TierRow(string Term, double Total, double Score, string Tier);

    public static List<TierRow> TierTerms(IEnumerable<HitRow> hits, double crest, double slopes)
    {
        var totals = hits
            .GroupBy(h => h.Term, StringComparer.OrdinalIgnoreCase)
            .Select(g => new { Term = g.Key, Total = (double)g.Sum(x => x.Frequency) })
            .OrderByDescending(x => x.Total)
            .ToList();

        double max = totals.FirstOrDefault()?.Total ?? 0.0;

        return totals.Select(t =>
        {
            double score = max > 0 ? t.Total / max : 0.0;
            string tier = score >= crest ? "Crest" : score >= slopes ? "Slopes" : "Base";
            return new TierRow(t.Term, t.Total, Math.Round(score, 3), tier);
        }).ToList();
    }
}
