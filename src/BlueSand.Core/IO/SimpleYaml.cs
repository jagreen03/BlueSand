using System.Text;
using System.Text.RegularExpressions;

namespace BlueSand.Core.IO;

/// <summary>
/// Minimal YAML reader for the restricted format you use:
/// key: value
/// key: [a, b, c]
/// key:
///   - a
///   - b
///   - c
/// Strings can be quoted or bare. Comments (#) and blank lines ignored.
/// This is intentionally small to avoid external deps.
/// </summary>
public static class SimpleYaml
{
    public static Dictionary<string, object> Load(string path)
    {
        var lines = File.ReadAllLines(path, Encoding.UTF8);
        var map = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        string? currentKey = null;

        for (int lineIndex = 0; lineIndex < lines.Length; lineIndex++)
        {
            var raw = lines[lineIndex];
            var trimmed = raw.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith("#")) continue;

            var kv = Regex.Match(trimmed, @"^\s*([A-Za-z0-9_]+):\s*(.*)$");
            if (kv.Success)
            {
                currentKey = kv.Groups[1].Value;
                var rest = kv.Groups[2].Value.Trim();

                if (rest.StartsWith("[") && rest.EndsWith("]"))
                {
                    var inner = rest[1..^1];
                    var arr = inner.Split(',')
                                   .Select(s => s.Trim().Trim('"', '\''))
                                   .Where(s => s.Length > 0)
                                   .ToList();
                    map[currentKey] = arr;
                }
                else if (!string.IsNullOrEmpty(rest))
                {
                    // single line scalar
                    map[currentKey] = rest.Trim('"', '\'');
                }
                else
                {
                    // expect a list to follow
                    map[currentKey] = new List<string>();
                }
                continue;
            }

            // list item?
            var li = Regex.Match(trimmed, @"^\s*-\s*(.*)$");
            if (li.Success && currentKey is not null)
            {
                if (!map.TryGetValue(currentKey, out var existing) || existing is not List<string> list)
                {
                    list = new List<string>();
                    map[currentKey] = list;
                }
                var val = li.Groups[1].Value.Trim().Trim('"', '\'');
                list.Add(val);
                continue;
            }

            throw new FormatException($"YAML parse error at line {lineIndex + 1}: {trimmed}");
        }

        return map;
    }
}
