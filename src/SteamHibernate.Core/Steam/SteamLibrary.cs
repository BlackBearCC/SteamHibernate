// src/SteamHibernate.Core/Steam/SteamLibrary.cs
namespace SteamHibernate.Core.Steam;

public sealed record SteamLibrary(string RootPath)
{
    public string SteamAppsPath => Path.Combine(RootPath, "steamapps");
    public string CommonPath => Path.Combine(SteamAppsPath, "common");
    public string AppManifestPath(string appId) =>
        Path.Combine(SteamAppsPath, $"appmanifest_{appId}.acf");
}
