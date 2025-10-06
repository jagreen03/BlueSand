
using BlueSand.Core.Config;
using BlueSand.Core.Scan;     // CorpusScanner (adjust if your namespace differs)

namespace BlueSand.Tests
{
	[TestClass]
	public class ScannerSmokeTests
	{
		[TestMethod]
		public async Task Can_Run_Scan_EndToEnd()
		{
			var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
			var configPath = Path.Combine(repoRoot, "config", "bluesand.yaml");
			Assert.IsTrue(File.Exists(configPath), "Missing config\\bluesand.yaml");

			var outDir = Path.Combine("docs", "_smoke");
			Directory.CreateDirectory(outDir);

			var options = new ApplicationOptions(
				configPath: configPath,
				outputDir: outDir,
				showProgress: false,
				maxDegreeOfParallelism: null);

			var scanner = new CorpusScanner();

			// Act
			int exit = await scanner.RunAsync(options);

			// Assert
			Assert.AreEqual(0, exit);
			Assert.IsTrue(File.Exists(Path.Combine(outDir, "WORDMAP_TABLE.md")));
			Assert.IsTrue(File.Exists(Path.Combine(outDir, "WORDMAP_RAW.csv")));
			Assert.IsTrue(File.Exists(Path.Combine(outDir, "SUMMARY.md")));
		}
	}
}
