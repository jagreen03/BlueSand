using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BlueSand.Core.Config
{
	public class ProcessingOptions
	{
		public ProcessingOptions(string outputDir, bool showProgress = true, int? maxDegreeOfParallelism = null)
		{
			OutputDir = outputDir ?? "docs";
			ShowProgress = showProgress;
			MaxDegreeOfParallelism = maxDegreeOfParallelism;
		}

		/// <summary>Not used by coordinator directly (writers consume this), kept for parity with CLI flow.</summary>
		public string OutputDir { get; }
		public bool ShowProgress { get; }
		public int? MaxDegreeOfParallelism { get; }
	}
}
