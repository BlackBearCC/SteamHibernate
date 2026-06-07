// src/SteamHibernate.Core/Steam/ConfigSteamLocator.cs
using SteamHibernate.Core.Vdf;

namespace SteamHibernate.Core.Steam;

public sealed class ConfigSteamLocator : ISteamLocator
{
    public string SteamRoot { get; }

    public ConfigSteamLocator(string steamRoot) => SteamRoot = steamRoot;

    public IReadOnlyList<SteamLibrary> GetLibraries()
    {
        var result = new List<SteamLibrary> { new(SteamRoot) };
        var file = Path.Combine(SteamRoot, "steamapps", "libraryfolders.vdf");
        if (!File.Exists(file)) return result;

        var root = VdfParser.Parse(File.ReadAllText(file));
        var folders = root["libraryfolders"];
        foreach (var (_, node) in folders.Children)
        {
            var path = node["path"].Value;
            if (!string.IsNullOrWhiteSpace(path) &&
                !result.Any(l => string.Equals(l.RootPath, path, StringComparison.OrdinalIgnoreCase)))
            {
                result.Add(new SteamLibrary(path));
            }
        }
        return result;
    }

    public IReadOnlyList<string> GetUserConfigPaths()
    {
        var userdata = Path.Combine(SteamRoot, "userdata");
        if (!Directory.Exists(userdata)) return Array.Empty<string>();
        return Directory.GetDirectories(userdata)
            .Select(d => Path.Combine(d, "config", "localconfig.vdf"))
            .Where(File.Exists)
            .ToList();
    }
}
