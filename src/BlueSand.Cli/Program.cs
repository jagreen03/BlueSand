using System;
using System.Threading.Tasks;
using BlueSand.Core.Scan;
using BlueSand.Core.Config;

namespace BlueSand.Cli
{
	public static class Program
	{
		public static async Task<int> Main(string[] args)
		{
			string configPath = GetArg(args, "--config") ?? @"config\bluesand.yaml";
			string outputDir = GetArg(args, "--outdir") ?? @"docs";
			bool showProgress = !HasFlag(args, "--no-progress");
			int? maxDegree = TryParseInt(GetArg(args, "--maxdeg"));

			if(HasFlag(args, "--help") || HasFlag(args, "-h"))
			{
				PrintHelp();
				return 0;
			}

			try
			{
				var scanner = new CorpusScanner();
				var options = new ApplicationOptions(configPath, outputDir, showProgress, maxDegree);
				return await scanner.RunAsync(options).ConfigureAwait(false);
			}
			catch(Exception ex)
			{
				Console.Error.WriteLine($"BlueSand fatal error: {ex.Message}");
				return 2;
			}
		}

		private static string? GetArg(string[] args, string name)
		{
			for(int i = 0; i < args.Length; i++)
			{
				var a = args[i];
				if(a.Equals(name, StringComparison.OrdinalIgnoreCase))
					return (i + 1 < args.Length && !args[i + 1].StartsWith("-")) ? args[i + 1] : null;

				var prefix = name + "=";
				if(a.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
					return a.Substring(prefix.Length);
			}
			return null;
		}

		private static bool HasFlag(string[] args, string flag) =>
			Array.Exists(args, a => a.Equals(flag, StringComparison.OrdinalIgnoreCase));

		private static int? TryParseInt(string? s) =>
			int.TryParse(s, out var n) ? n : (int?)null;

		private static void PrintHelp()
		{
			Console.WriteLine(
@"BlueSand CLI
  --config <path>   Path to bluesand.yaml (default: config\bluesand.yaml)
  --outdir <dir>    Output directory (default: docs)
  --maxdeg <N>      Max degree of parallelism
  --no-progress     Suppress progress output
  -h, --help        Show help");
		}
	}
}
