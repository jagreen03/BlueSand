using System.Text.Json.Serialization;

using YamlDotNet.Serialization;

namespace BlueSand.Core.Models
{
	public record TermOccurrence(
		
		[property: JsonPropertyName("term"), YamlMember(Alias = "term")]
		string Term,
		
		[property: JsonPropertyName("repo"), YamlMember(Alias = "repo")]
		string Repository,
		
		[property: JsonPropertyName("file"), YamlMember(Alias = "file")]
		string FilePath,
		
		[property: JsonPropertyName("ext"), YamlMember(Alias = "ext")]
		string Extension,
		
		[property: JsonPropertyName("bucket"), YamlMember(Alias = "bucket")]
		BucketKind Bucket,
		
		[property: JsonPropertyName("frequency"), YamlMember(Alias = "frequency")]
		int Frequency,
		
		[property: JsonPropertyName("context"), YamlMember(Alias = "context")]
		string Context
	);

}
