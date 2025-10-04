using System.Text;
using System.Text.RegularExpressions;

namespace BlueSand.Core.Models
{
	public sealed class PathSpecSet
	{
		private readonly List<(Regex rx, bool neg)> _rules;

		public PathSpecSet(IEnumerable<string> lines)
		{
			_rules = new();
			foreach(var raw in lines)
			{
				var line = raw.Trim();
				if(line.Length == 0 || line.StartsWith("#")) continue;
				var neg = line.StartsWith("!");
				var pat = neg ? line[1..].Trim() : line;
				var rx = GlobToRegex(pat);
				_rules.Add((rx, neg));
			}
		}

		public bool IsExcluded(string fullPath)
		{
			// normalize slashes
			var p = fullPath.Replace('\\', '/');

			bool? state = null;
			foreach(var (rx, neg) in _rules)
			{
				if(rx.IsMatch(p)) state = !neg; // match → exclude unless negation flips it
			}
			return state == true;
		}

		private static Regex GlobToRegex(string glob)
		{
			// very small, safe converter:
			//   ** → .*      * → [^/]*      ? → [^/]
			//   escape regex meta
			var sb = new StringBuilder("^");
			for(int i = 0; i < glob.Length; i++)
			{
				var c = glob[i];
				switch(c)
				{
					case '*':
						if(i + 1 < glob.Length && glob[i + 1] == '*') { sb.Append(".*"); i++; }
						else sb.Append("[^/]*");
						break;
					case '?': sb.Append("[^/]"); break;
					case '.':
					case '+':
					case '(':
					case ')':
					case '$':
					case '^':
					case '{':
					case '}':
					case '[':
					case ']':
					case '|':
					case '\\':
						sb.Append('\\').Append(c); break;
					case '/':
						sb.Append('/'); break;
					default:
						sb.Append(c);
						break;
				}
			}
			sb.Append('$');
			return new Regex(sb.ToString(), RegexOptions.IgnoreCase | RegexOptions.Compiled);
		}
	}
}
