// src/SteamHibernate.Core/Package/FileEntry.cs
namespace SteamHibernate.Core.Package;

public sealed record FileEntry(string RelativePath, long Size);
