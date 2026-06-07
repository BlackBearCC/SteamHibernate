using System.Linq;
using SteamHibernate.Core.Engine;
using SteamHibernate.Core.Metadata;
using SteamHibernate.Core.Steam;
using SteamHibernate.Core.Tiering;
using Xunit;

public class ManualTieringServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "shtier_" + Guid.NewGuid().ToString("N"));
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }

    private (ManualTieringService svc, InstalledGame game, MetadataStore store) Setup()
    {
        var lib = new SteamLibrary(Path.Combine(_root, "lib"));
        Directory.CreateDirectory(lib.CommonPath);
        var gameDir = Path.Combine(lib.CommonPath, "MyGame");
        Directory.CreateDirectory(gameDir);
        File.WriteAllText(Path.Combine(gameDir, "g.dat"), "hello");
        File.WriteAllText(lib.AppManifestPath("999"), "\"AppState\" { \"appid\" \"999\" }");

        var game = new InstalledGame("999", "MyGame", gameDir, 5, null, lib);
        var store = new MetadataStore(Path.Combine(_root, "meta.json"));
        var archiveRoot = Path.Combine(_root, "archives");
        var svc = new ManualTieringService(new FakeArchiveEngine(), store, archiveRoot, level: 5);
        return (svc, game, store);
    }

    [Fact]
    public void Compress_removes_game_dir_and_records_archive()
    {
        var (svc, game, store) = Setup();
        var result = svc.Compress(game, _ => { });

        Assert.True(result.Success);
        Assert.False(Directory.Exists(game.InstallDir)); // original dir deleted
        Assert.False(File.Exists(game.Library.AppManifestPath("999"))); // appmanifest removed -> Steam shows "not installed"
        Assert.NotNull(store.Get("999"));
    }

    [Fact]
    public void Empty_archiveRoot_stores_package_inside_game_library()
    {
        var (_, game, store) = Setup();
        // empty archive root => default to <library>/SteamHibernate/<appid>
        var svc = new ManualTieringService(new FakeArchiveEngine(), store, "", level: 5);

        Assert.True(svc.Compress(game, _ => { }).Success);

        var expected = Path.Combine(game.Library.RootPath, "SteamHibernate", "999");
        Assert.Equal(expected, store.Get("999")!.PackageDir);
        Assert.True(Directory.Exists(expected));
    }

    [Fact]
    public void Compress_failed_verify_keeps_game_dir()
    {
        var (_, game, store) = Setup();
        var engine = new FakeArchiveEngine { FailVerify = true };
        var svc = new ManualTieringService(engine, store, Path.Combine(_root, "a2"), 5);

        var result = svc.Compress(game, _ => { });

        Assert.False(result.Success);
        Assert.True(Directory.Exists(game.InstallDir)); // safety: original dir preserved
        Assert.Null(store.Get("999"));
    }

    [Fact]
    public void Restore_brings_back_game_and_acf_then_clears_record()
    {
        var (svc, game, store) = Setup();
        Assert.True(svc.Compress(game, _ => { }).Success);

        var result = svc.Restore("999", _ => { });

        Assert.True(result.Success);
        Assert.Equal("hello", File.ReadAllText(Path.Combine(game.InstallDir, "g.dat")));
        Assert.True(File.Exists(game.Library.AppManifestPath("999")));
        Assert.Null(store.Get("999"));
    }

    [Fact]
    public void Compress_twice_replaces_package_without_duplicate_metadata()
    {
        var (svc, game, store) = Setup();
        Assert.True(svc.Compress(game, _ => { }).Success);
        // restore so the game dir exists again, then re-compress
        Assert.True(svc.Restore("999", _ => { }).Success);
        var (svc2, game2, store2) = (svc, game, store); // same instances
        Assert.True(svc2.Compress(game2, _ => { }).Success);
        Assert.Single(store2.All, r => r.AppId == "999");
    }

    [Fact]
    public void Restore_failure_leaves_archive_and_record_intact()
    {
        // Reproduce Setup's library/game layout but keep a reference to the fake engine.
        var root = Path.Combine(_root, "fail");
        var lib = new SteamLibrary(Path.Combine(root, "lib"));
        Directory.CreateDirectory(lib.CommonPath);
        var gameDir = Path.Combine(lib.CommonPath, "MyGame");
        Directory.CreateDirectory(gameDir);
        File.WriteAllText(Path.Combine(gameDir, "g.dat"), "hello");
        File.WriteAllText(lib.AppManifestPath("999"), "\"AppState\" { \"appid\" \"999\" }");
        var game = new InstalledGame("999", "MyGame", gameDir, 5, null, lib);
        var store = new MetadataStore(Path.Combine(root, "meta.json"));
        var engine = new FakeArchiveEngine();
        var svc = new ManualTieringService(engine, store, Path.Combine(root, "archives"), 5);

        Assert.True(svc.Compress(game, _ => { }).Success);
        var rec = store.Get("999");
        Assert.NotNull(rec);

        engine.FailVerify = true;                 // make the pre-extract integrity check fail
        var result = svc.Restore("999", _ => { });

        Assert.False(result.Success);
        Assert.NotNull(store.Get("999"));         // record intact
        Assert.True(Directory.Exists(rec!.PackageDir)); // archive intact
    }
}
