using System.Text.RegularExpressions;

using BlueSand.Core.Models;

using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace BlueSand.Core
{
	public class ScanEssentials
	{
		[YamlMember(Alias = "include_paths")]
		public List<string> IncludePaths { get; set; } = new();

		[YamlMember(Alias = "exclude_dir_regex")]
		public Regex ExcludeDirRegex { get; init; } = new("", RegexOptions.Compiled | RegexOptions.IgnoreCase);

		[YamlMember(Alias = "exclude_file_regex")]
		public Regex? ExcludeFileRegex { get; init; } = new("", RegexOptions.Compiled | RegexOptions.IgnoreCase);

		[YamlMember(Alias = "exclusion_patterns")]
		public List<string> ExclusionPatterns { get; internal set; } = new();

		[YamlMember(Alias = "exclusion_files")]
		public List<string> ExclusionFiles { get; internal set; } = new();

		[YamlMember(Alias = "extensions")]
		public List<string> Extensions { get; init; } = new();

		[YamlMember(Alias = "anchor_terms")]
		public List<string> AnchorTerms { get; init; } = new();

		[YamlMember(Alias = "planned_hints_regex")]
		public Regex PlannedHintsRegex { get; init; } = new("", RegexOptions.Compiled | RegexOptions.IgnoreCase);

		[YamlMember(Alias = "code_hints_regex")]
		public Regex CodeHintsRegex { get; init; } = new("", RegexOptions.Compiled | RegexOptions.IgnoreCase);

		[YamlMember(Alias = "crest_threshold")]
		public double CrestThreshold { get; init; } = 0.90;

		[YamlMember(Alias = "slopes_threshold")]
		public double SlopesThreshold { get; init; } = 0.60;

		[YamlMember(Alias = "max_file_mb")]
		public int MaxFileMb { get; init; } = 0;

		public static ScanEssentials Load(string path)
		{
			var yaml = File.ReadAllText(path);

			// Fix escape issues only in quoted include_paths entries
			yaml = Regex.Replace(yaml,
				@"- \""([^""]*)\""",
				m => "- '" + m.Groups[1].Value.Replace("'", "''") + "'");

			var deserializer = new DeserializerBuilder()
				.WithNamingConvention(UnderscoredNamingConvention.Instance)
				.IgnoreUnmatchedProperties()
				.Build();

			var rawScanner = deserializer.Deserialize<ScanConfigRaw>(yaml) ?? new ScanConfigRaw();
			var cfg = ScanEssentials.FromRaw(rawScanner);

			// expand %ENV% in include paths
			cfg.IncludePaths = cfg.IncludePaths
				.Select(p => Environment.ExpandEnvironmentVariables(p))
				.ToList();
			return cfg;
		}

		/// <summary>
		/// Normalize a YAML-loaded ScanConfigRaw into a runtime-ready essentials config:
		/// - expands %ENV% in include paths
		/// - compiles regexes
		/// - normalizes extensions to lower case
		/// - enforces sane defaults
		/// </summary>
		public static ScanEssentials FromRaw(ScanConfigRaw raw)
		{
			var include = (raw.IncludePaths ?? new())
				.Select(p => Environment.ExpandEnvironmentVariables(p))
				.Where(p => !string.IsNullOrWhiteSpace(p))
				.ToList();

			var ext = (raw.Extensions ?? new())
				.Select(e => e?.Trim())
				.Where(e => !string.IsNullOrWhiteSpace(e))
				.Select(e => e!.ToLowerInvariant())
				.ToList();

			var terms = (raw.AnchorTerms ?? new())
				.Where(t => !string.IsNullOrWhiteSpace(t))
				.ToList();

			var excludeDir = string.IsNullOrWhiteSpace(raw.ExcludeDirRegex) ? "" : raw.ExcludeDirRegex!;
			var excludeFile = string.IsNullOrWhiteSpace(raw.ExcludeFileRegex) ? null : raw.ExcludeFileRegex;

			var planned = string.IsNullOrWhiteSpace(raw.PlannedHintsRegex) ? "" : raw.PlannedHintsRegex!;
			var code = string.IsNullOrWhiteSpace(raw.CodeHintsRegex) ? "" : raw.CodeHintsRegex!;

			return new ScanEssentials
			{
				IncludePaths = include,
				ExcludeDirRegex = new Regex(excludeDir, RegexOptions.Compiled | RegexOptions.IgnoreCase),
				ExcludeFileRegex = string.IsNullOrWhiteSpace(excludeFile) ? null : new Regex(excludeFile!, RegexOptions.Compiled | RegexOptions.IgnoreCase),
				Extensions = ext,
				AnchorTerms = terms,
				PlannedHintsRegex = new Regex(planned, RegexOptions.Compiled | RegexOptions.IgnoreCase),
				CodeHintsRegex = new Regex(code, RegexOptions.Compiled | RegexOptions.IgnoreCase),
				CrestThreshold = raw.CrestThreshold <= 0 ? 0.90 : raw.CrestThreshold,
				SlopesThreshold = raw.SlopesThreshold <= 0 ? 0.60 : raw.SlopesThreshold,
				MaxFileMb = raw.MaxFileMb < 0 ? 0 : raw.MaxFileMb
			};
		}
	}
}
