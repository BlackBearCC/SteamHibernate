// src/SteamHibernate.Core/Steam/InstalledGame.cs
namespace SteamHibernate.Core.Steam;

public sealed record InstalledGame(
    string AppId,
    string Name,
    string InstallDir,
    long SizeOnDisk,
    DateTimeOffset? LastPlayed,
    SteamLibrary Library);
