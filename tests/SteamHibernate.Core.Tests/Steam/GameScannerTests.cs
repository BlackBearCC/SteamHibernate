// tests/.../Steam/GameScannerTests.cs
using SteamHibernate.Core.Steam;
using Xunit;

public class GameScannerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "shtest_" + Guid.NewGuid().ToString("N"));

    public GameScannerTests()
    {
        var apps = Path.Combine(_root, "steamapps");
        Directory.CreateDirectory(Path.Combine(apps, "common", "Half-Life"));
        File.WriteAllText(Path.Combine(apps, "appmanifest_70.acf"), """
        "AppState"
        {
            "appid"      "70"
            "name"       "Half-Life"
            "installdir" "Half-Life"
            "SizeOnDisk" "4194304"
        }
        """);
        var userCfg = Path.Combine(_root, "userdata", "111", "config");
        Directory.CreateDirectory(userCfg);
        File.WriteAllText(Path.Combine(userCfg, "localconfig.vdf"), """
        "UserLocalConfigStore"
        {
            "Software" { "Valve" { "Steam" { "apps"
            {
                "70" { "LastPlayed" "1700000000" }
            } } } }
        }
        """);
    }

    [Fact]
    public void Scans_installed_game_with_size_and_lastplayed()
    {
        var scanner = new GameScanner(new ConfigSteamLocator(_root));
        var games = scanner.Scan();

        var g = Assert.Single(games);
        Assert.Equal("70", g.AppId);
        Assert.Equal("Half-Life", g.Name);
        Assert.Equal(4194304, g.SizeOnDisk);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1700000000), g.LastPlayed);
        Assert.True(Directory.Exists(g.InstallDir));
    }

    [Fact]
    public void Scan_without_userdata_returns_game_with_null_lastplayed()
    {
        var root2 = Path.Combine(Path.GetTempPath(), "shtest2_" + Guid.NewGuid().ToString("N"));
        try
        {
            var apps = Path.Combine(root2, "steamapps");
            Directory.CreateDirectory(Path.Combine(apps, "common", "Half-Life"));
            File.WriteAllText(Path.Combine(apps, "appmanifest_70.acf"), """
            "AppState"
            {
                "appid"      "70"
                "name"       "Half-Life"
                "installdir" "Half-Life"
                "SizeOnDisk" "4194304"
            }
            """);
            // deliberately NO userdata directory

            var scanner = new GameScanner(new ConfigSteamLocator(root2));
            var games = scanner.Scan();

            var g = Assert.Single(games);
            Assert.Equal("70", g.AppId);
            Assert.Null(g.LastPlayed);
        }
        finally
        {
            if (Directory.Exists(root2)) Directory.Delete(root2, true);
        }
    }

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
