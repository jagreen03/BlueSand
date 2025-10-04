using System.Text.RegularExpressions;

namespace BlueSand.Core.Scan;

public sealed record TermHit(
    string Term,
    string Repo,
    string FilePath,
    string Extension,
    string Bucket,     // Planned | Code | Overlap | Unknown
    int Frequency,
    string ContextOneLine
);

public sealed record TierRow(string Term, int Total, double Score, string Tier);

public sealed class CompiledConfig
{
    public required List<string> IncludePaths { get; init; }
    public required Regex ExcludeDirRe { get; init; }
    public required HashSet<string> Extensions { get; init; } // just suffixes like ".md", ".cs"

    public required List<string> AnchorTerms { get; init; }
    public required Regex PlannedRe { get; init; }
    public required Regex CodeRe { get; init; }

    public required double CrestThreshold { get; init; }
    public required double SlopesThreshold { get; init; }

    public Regex? ExcludeFileRe { get; init; }
    public long? MaxFileBytes { get; init; }
}
