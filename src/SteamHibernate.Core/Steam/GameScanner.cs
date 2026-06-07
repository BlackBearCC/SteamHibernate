// src/SteamHibernate.Core/Steam/GameScanner.cs
using System.IO;
using SteamHibernate.Core.Vdf;

namespace SteamHibernate.Core.Steam;

public sealed class GameScanner
{
    private readonly ISteamLocator _locator;
    public GameScanner(ISteamLocator locator) => _locator = locator;

    public IReadOnlyList<InstalledGame> Scan()
    {
        var lastPlayed = ReadLastPlayed();
        var games = new List<InstalledGame>();

        foreach (var lib in _locator.GetLibraries())
        {
            if (!Directory.Exists(lib.SteamAppsPath)) continue;
            foreach (var acf in Directory.GetFiles(lib.SteamAppsPath, "appmanifest_*.acf"))
            {
                try
                {
                    var state = VdfParser.Parse(File.ReadAllText(acf))["AppState"];
                    var appId = state["appid"].Value;
                    var installDir = state["installdir"].Value;
                    if (string.IsNullOrEmpty(appId) || string.IsNullOrEmpty(installDir)) continue;

                    long.TryParse(state["SizeOnDisk"].Value, out var size);
                    games.Add(new InstalledGame(
                        AppId: appId,
                        Name: state["name"].Value ?? installDir,
                        InstallDir: Path.Combine(lib.CommonPath, installDir),
                        SizeOnDisk: size,
                        LastPlayed: lastPlayed.TryGetValue(appId, out var lp) ? lp : null,
                        Library: lib));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    continue; // skip this one manifest, keep scanning
                }
            }
        }
        return games;
    }

    private Dictionary<string, DateTimeOffset> ReadLastPlayed()
    {
        var result = new Dictionary<string, DateTimeOffset>();
        foreach (var cfg in _locator.GetUserConfigPaths())
        {
            var apps = VdfParser.Parse(File.ReadAllText(cfg))
                ["UserLocalConfigStore"]["Software"]["Valve"]["Steam"]["apps"];
            foreach (var (appId, node) in apps.Children)
            {
                if (long.TryParse(node["LastPlayed"].Value, out var unix))
                {
                    var ts = DateTimeOffset.FromUnixTimeSeconds(unix);
                    if (!result.TryGetValue(appId, out var existing) || ts > existing)
                        result[appId] = ts;
                }
            }
        }
        return result;
    }
}
