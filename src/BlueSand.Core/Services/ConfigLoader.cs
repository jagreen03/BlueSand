using System.Text;

using BlueSand.Core.Config;
using BlueSand.Core.Models;

using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace BlueSand.Core.Services
{
	public static class ConfigLoader
	{
		// Purpose-built exception so callers can catch one thing.
		public sealed class ConfigLoadException : Exception
		{
			public ConfigLoadException(string message, Exception? inner = null) : base(message, inner) { }
		}

		/// <summary>
		/// Async load: reads YAML, deserializes to ScanConfigRaw, normalizes to ScanConfigEssentials.
		/// Throws ConfigLoadException on any error (missing file, YAML error, validation fail).
		/// </summary>
		public static async Task<ScanEssentials> LoadAsync(
			string path,
			CancellationToken cancellationToken = default)
		{
			if(string.IsNullOrWhiteSpace(path))
				throw new ConfigLoadException("Config path is empty.");

			if(!File.Exists(path))
				throw new ConfigLoadException($"Config file not found: {path}");

			try
			{
				using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
				using var sr = new StreamReader(fs, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
				var yaml = await sr.ReadToEndAsync(cancellationToken).ConfigureAwait(false);

				var deserializer = new DeserializerBuilder()
					.WithNamingConvention(UnderscoredNamingConvention.Instance)
					.IgnoreUnmatchedProperties()
					.Build();

				var raw = deserializer.Deserialize<ScanConfigRaw>(yaml) ?? new ScanConfigRaw();

				// Normalize → runtime config
				var cfg = ScanEssentials.FromRaw(raw);

				// Minimal validation (tune as you like)
				Validate(cfg, path);

				return cfg;
			}
			catch(YamlException yx)
			{
				// YamlDotNet can give line/col; surface them if available
				var loc = (yx.Start.Line, yx.Start.Column);
				var where = loc.Line > 0 ? $" (at line {loc.Line}, col {loc.Column})" : string.Empty;
				throw new ConfigLoadException($"YAML parse error in {path}{where}: {yx.Message}", yx);
			}
			catch(Exception ex) when(ex is IOException or UnauthorizedAccessException)
			{
				throw new ConfigLoadException($"Could not read config file {path}: {ex.Message}", ex);
			}
			catch(Exception ex)
			{
				throw new ConfigLoadException($"Unexpected error loading {path}: {ex.Message}", ex);
			}
		}

		/// <summary>
		/// Sync wrapper for convenience.
		/// </summary>
		public static ScanEssentials Load(string path)
			=> LoadAsync(path).GetAwaiter().GetResult();

		/// <summary>
		/// Non-throwing variant. Returns false and an error message on failure.
		/// </summary>
		public static bool TryLoad(string path, out ScanEssentials? config, out string? error)
		{
			try
			{
				config = Load(path);
				error = null;
				return true;
			}
			catch(ConfigLoadException cle)
			{
				config = null;
				error = cle.Message;
				return false;
			}
			catch(Exception ex)
			{
				config = null;
				error = ex.Message;
				return false;
			}
		}

		private static void Validate(ScanEssentials cfg, string path)
		{
			if(cfg.IncludePaths.Count == 0)
				throw new ConfigLoadException($"Config {path} has no include_paths.");
			if(cfg.Extensions.Count == 0)
				throw new ConfigLoadException($"Config {path} has no extensions.");
			if(cfg.AnchorTerms.Count == 0)
				throw new ConfigLoadException($"Config {path} has no anchor_terms.");
			if(cfg.CrestThreshold <= 0 || cfg.CrestThreshold > 1)
				throw new ConfigLoadException($"crest_threshold must be in (0,1]: {cfg.CrestThreshold}");
			if(cfg.SlopesThreshold < 0 || cfg.SlopesThreshold >= cfg.CrestThreshold)
				throw new ConfigLoadException($"slopes_threshold must be >= 0 and < crest_threshold ({cfg.CrestThreshold}).");
		}
	}
}