using System.Text.RegularExpressions;

using YamlDotNet.Serialization;

namespace BlueSand.Core.Models
{

	public class ScanConfigRaw
	{
		[YamlMember(Alias = "include_paths")] public List<string> IncludePaths { get; set; } = new();
		[YamlMember(Alias = "exclude_dir_regex")] public string? ExcludeDirRegex { get; set; }
		[YamlMember(Alias = "exclude_file_regex")] public string? ExcludeFileRegex { get; set; }
		[YamlMember(Alias = "exclusion_patterns")] public List<string>? ExclusionPatterns { get; set; }
		[YamlMember(Alias = "exclusion_files")] public List<string>? ExclusionFiles { get; set; }
		[YamlMember(Alias = "extensions")] public List<string> Extensions { get; set; } = new();
		[YamlMember(Alias = "anchor_terms")] public List<string> AnchorTerms { get; set; } = new();
		[YamlMember(Alias = "planned_hints_regex")] public string? PlannedHintsRegex { get; set; }
		[YamlMember(Alias = "code_hints_regex")] public string? CodeHintsRegex { get; set; }
		[YamlMember(Alias = "crest_threshold")] public double CrestThreshold { get; set; } = 0.90;
		[YamlMember(Alias = "slopes_threshold")] public double SlopesThreshold { get; set; } = 0.60;
		[YamlMember(Alias = "max_file_mb")] public int MaxFileMb { get; set; } = 0;
	}
}