// src/SteamHibernate.Core/Steam/ISteamLocator.cs
namespace SteamHibernate.Core.Steam;

public interface ISteamLocator
{
    string SteamRoot { get; }
    IReadOnlyList<SteamLibrary> GetLibraries();
    IReadOnlyList<string> GetUserConfigPaths(); // localconfig.vdf 路径集合
}
