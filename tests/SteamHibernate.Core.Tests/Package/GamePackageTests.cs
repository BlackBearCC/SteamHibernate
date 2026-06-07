// tests/.../Package/GamePackageTests.cs
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
}
