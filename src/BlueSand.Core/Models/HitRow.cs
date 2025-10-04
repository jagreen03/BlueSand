namespace BlueSand.Core.Models;

public class HitRow
{
    public string Term { get; init; } = "";
    public string Repo { get; init; } = "";
    public string File { get; init; } = "";
    public string Ext { get; init; } = "";
    public string Bucket { get; init; } = "";
    public int Frequency { get; init; }
    public string Context { get; init; } = "";
}
