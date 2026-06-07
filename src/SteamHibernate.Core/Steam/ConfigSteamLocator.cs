// src/SteamHibernate.Core/Steam/ConfigSteamLocator.cs
using SteamHibernate.Core.Vdf;

namespace SteamHibernate.Core.Steam;

public sealed class ConfigSteamLocator : ISteamLocator
{
    public string SteamRoot { get; }

    public ConfigSteamLocator(string steamRoot) => SteamRoot = steamRoot;

    public IReadOnlyList<SteamLibrary> GetLibraries()
    {
        var result = new List<SteamLibrary>();
        var seen = new HashSet<string>();
        void TryAdd(string root)
        {
            if (!string.IsNullOrWhiteSpace(root) && seen.Add(NormalizeKey(root)))
                result.Add(new SteamLibrary(root));
        }

        TryAdd(SteamRoot);
        var file = Path.Combine(SteamRoot, "steamapps", "libraryfolders.vdf");
        if (!File.Exists(file)) return result;

        var root = VdfParser.Parse(File.ReadAllText(file));
        foreach (var (_, node) in root["libraryfolders"].Children)
            TryAdd(node["path"].Value ?? string.Empty);
        return result;
    }

    // Steam stores library paths inconsistently: the registry SteamPath uses forward
    // slashes / lowercase (e.g. "d:/program files/steam") while libraryfolders.vdf uses
    // escaped backslashes / title case ("D:\\Program Files\\Steam"). Normalize separators,
    // trailing slash and case so the same physical folder is not scanned twice.
    private static string NormalizeKey(string path) =>
        path.Replace('\\', '/').TrimEnd('/').ToLowerInvariant();

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
