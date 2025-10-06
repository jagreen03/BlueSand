using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BlueSand.Core.Config
{
	public class ApplicationOptions : ProcessingOptions
	{
		public ApplicationOptions(string configPath, string outputDir, bool showProgress = true, int? maxDegreeOfParallelism = null)
			: base(outputDir, showProgress, maxDegreeOfParallelism)
		{
			if(string.IsNullOrWhiteSpace(outputDir))
				throw new ArgumentException("Output directory cannot be empty.", nameof(outputDir));
			ConfigPath = configPath;
		}

		public string ConfigPath { get; set; }
	}
}
