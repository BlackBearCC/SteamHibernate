// src/SteamHibernate.Core/Package/DirectoryManifest.cs
namespace SteamHibernate.Core.Package;

public sealed class DirectoryManifest
{
    public required IReadOnlyList<FileEntry> Files { get; init; }
    public long TotalSize => Files.Sum(f => f.Size);

    public static DirectoryManifest Capture(string root)
    {
        var files = new List<FileEntry>();
        foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(root, path);
            files.Add(new FileEntry(rel, new FileInfo(path).Length));
        }
        return new DirectoryManifest { Files = files };
    }
}
