// src/SteamHibernate.Core/Package/PackageHeader.cs
namespace SteamHibernate.Core.Package;

public sealed record PackageHeader(
    string AppId,
    string GameName,
    string InstallDirName,
    long OriginalSize,
    long CompressedSize,
    string EngineExtension,
    DateTimeOffset CreatedUtc);
