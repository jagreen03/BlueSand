using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BlueSand.Core
{
	public static class RepoHeuristics
	{
		public static string FromFullPath(string fullPath, IReadOnlyList<string> roots)
		{
			foreach(var r in roots)
			{
				if(fullPath.StartsWith(r, StringComparison.OrdinalIgnoreCase))
				{
					var rel = fullPath.Substring(r.Length).TrimStart('\\', '/');
					if(rel.Length > 0) return rel.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[0];
				}
			}
			var parts = fullPath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
			return parts.Length >= 3 ? parts[2] : parts[^1];
		}
	}
}
