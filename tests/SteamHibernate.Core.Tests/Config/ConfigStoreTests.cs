using SteamHibernate.Core.Config;
using Xunit;

public class ConfigStoreTests : IDisposable
{
    private readonly string _file = Path.Combine(Path.GetTempPath(), "shcfg_" + Guid.NewGuid().ToString("N") + ".json");
    public void Dispose() { if (File.Exists(_file)) File.Delete(_file); }

    [Fact]
    public void Load_returns_defaults_when_missing_then_saves_roundtrip()
    {
        var store = new ConfigStore(_file);
        var cfg = store.Load();
        Assert.Equal(9, cfg.CompressionLevel); // default max

        cfg = cfg with { ArchiveRoot = "/data/archives", CompressionLevel = 5 };
        store.Save(cfg);

        var reloaded = new ConfigStore(_file).Load();
        Assert.Equal("/data/archives", reloaded.ArchiveRoot);
        Assert.Equal(5, reloaded.CompressionLevel);
    }
}
