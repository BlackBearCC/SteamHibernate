namespace SteamHibernate.Core.Metadata;

public sealed record ArchiveRecord(
    string AppId,
    string GameName,
    string PackageDir,
    long OriginalSize,
    long CompressedSize,
    DateTimeOffset ArchivedUtc);
