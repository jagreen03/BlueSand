using BlueSand.Core.IO;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace BlueSand.Tests
{
	[TestClass]
	public class SimpleYamlTests
	{
		[TestMethod]
		public void CanParse_ListAndScalars()
		{
			var tmp = Path.GetTempFileName();
			File.WriteAllText(tmp, """
        include_paths:
          - "C:\\test\\"
          - "%USERPROFILE%\\Desktop\\"
        extensions: ["*.md","*.cs"]
        crest_threshold: 0.9
        """);

			var map = SimpleYaml.Load(tmp);
			Assert.IsTrue(map.ContainsKey("include_paths"));
			Assert.IsTrue(map.ContainsKey("extensions"));
			Assert.AreEqual("0.9", map["crest_threshold"]);

			File.Delete(tmp);
		}
	}
}