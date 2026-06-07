// tests/.../Package/GamePackageTests.cs
using SteamHibernate.Core.Engine;
using SteamHibernate.Core.Package;
using Xunit;

public class GamePackageTests : IDisposable
{
    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "shpkg_" + Guid.NewGuid().ToString("N"));
    public GamePackageTests() => Directory.CreateDirectory(_tmp);
    public void Dispose() { if (Directory.Exists(_tmp)) Directory.Delete(_tmp, true); }

    [Fact]
    public void Manifest_captures_relative_paths_and_sizes()
    {
        var game = Path.Combine(_tmp, "game");
        Directory.CreateDirectory(Path.Combine(game, "bin"));
        File.WriteAllText(Path.Combine(game, "bin", "a.txt"), "hello");

        var manifest = DirectoryManifest.Capture(game);

        var entry = Assert.Single(manifest.Files);
        Assert.Equal(Path.Combine("bin", "a.txt"), entry.RelativePath);
        Assert.Equal(5, entry.Size);
    }

    [Fact]
    public void Manifest_of_empty_directory_is_empty()
    {
        var empty = Path.Combine(_tmp, "empty");
        Directory.CreateDirectory(empty);
        var m = DirectoryManifest.Capture(empty);
        Assert.Empty(m.Files);
        Assert.Equal(0, m.TotalSize);
    }

    [SkippableFact]
    public void Pack_then_unpack_restores_game_and_acf()
    {
        var exe = SevenZipEngine.FindBinary();
        Skip.If(exe is null, "7-Zip binary not found");
        var engine = new SevenZipEngine(exe!);

        var game = Path.Combine(_tmp, "common", "MyGame");
        Directory.CreateDirectory(game);
        File.WriteAllText(Path.Combine(game, "game.dat"), "payload");
        var acf = Path.Combine(_tmp, "appmanifest_999.acf");
        File.WriteAllText(acf, "\"AppState\" { \"appid\" \"999\" }");

        var pkgDir = Path.Combine(_tmp, "pkg");
        GamePackage.Pack(engine, appId: "999", gameDir: game, acfPath: acf,
            packageDir: pkgDir, level: 5, _ => { });

        var header = GamePackage.ReadHeader(pkgDir);
        Assert.Equal("999", header.AppId);
        Assert.True(header.CompressedSize > 0);

        var restoreGame = Path.Combine(_tmp, "restored", "MyGame");
        var restoreAcf = Path.Combine(_tmp, "restored", "appmanifest_999.acf");
        GamePackage.Unpack(engine, pkgDir, restoreGame, restoreAcf, _ => { });

        Assert.Equal("payload", File.ReadAllText(Path.Combine(restoreGame, "game.dat")));
        Assert.True(File.Exists(restoreAcf));
        Assert.Equal(File.ReadAllText(acf), File.ReadAllText(restoreAcf));
    }

    [SkippableFact]
    public void VerifyIntegrity_returns_false_on_corrupted_archive()
    {
        var exe = SevenZipEngine.FindBinary();
        Skip.If(exe is null, "7-Zip binary not found");
        var engine = new SevenZipEngine(exe!);
        var archive = Path.Combine(_tmp, "bad.7z");
        File.WriteAllBytes(archive, new byte[] { 0x00, 0x01, 0x02, 0x03 });
        Assert.False(engine.VerifyIntegrity(archive));
    }
}
