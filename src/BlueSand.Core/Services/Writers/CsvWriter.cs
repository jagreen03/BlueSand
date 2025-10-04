using System.Globalization;
using System.Text;
using BlueSand.Core.Models;

namespace BlueSand.Core.Services.Writers;

public static class CsvWriter
{
    public static void WriteRaw(string path, IEnumerable<HitRow> hits)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Term,Repo,File,Ext,Bucket,Frequency,Context");
        foreach (var h in hits.OrderBy(h => h.Term).ThenBy(h => h.Repo))
        {
            sb.AppendLine(string.Join(",",
                Csv(h.Term), Csv(h.Repo), Csv(h.File), Csv(h.Ext),
                Csv(h.Bucket), h.Frequency.ToString(CultureInfo.InvariantCulture), Csv(h.Context)));
        }
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
    }

    static string Csv(string? s)
    {
        s ??= "";
        bool q = s.Contains(',') || s.Contains('"') || s.Contains('\n') || s.Contains('\r');
        return q ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
    }
}
