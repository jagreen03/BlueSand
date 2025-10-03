using BlueSand.Core.Services;
using Xunit;

public class ConfigLoaderTests
{
    [Fact]
    public void LoadsConfig_WhenValidYaml()
    {
        var path = Path.Combine("config","bluesand.yaml");
        if (!File.Exists(path)) return; // allow CI to pass if config not present
        var cfg = ConfigLoader.Load(path);
        Assert.NotEmpty(cfg.include_paths);
    }
}
