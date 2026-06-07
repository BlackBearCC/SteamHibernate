using SteamHibernate.Core.Engine;

public sealed class FakeArchiveEngine : IArchiveEngine
{
    public bool FailVerify { get; set; }
    public string ArchiveExtension => ".fake";

    public void Compress(string srcDir, string archivePath, int level, Action<ArchiveProgress> progress)
    {
        // Serialize directory as lines of "relpath\tcontent" to simulate compression
        var lines = Directory.EnumerateFiles(srcDir, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(srcDir, f) + "\t" + File.ReadAllText(f));
        File.WriteAllLines(archivePath, lines);
        progress(new ArchiveProgress("Compressing", 1));
    }

    public void Extract(string archivePath, string dstDir, Action<ArchiveProgress> progress)
    {
        Directory.CreateDirectory(dstDir);
        foreach (var line in File.ReadAllLines(archivePath))
        {
            var i = line.IndexOf('\t');
            var rel = line[..i];
            var full = Path.Combine(dstDir, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, line[(i + 1)..]);
        }
        progress(new ArchiveProgress("Extracting", 1));
    }

    public bool VerifyIntegrity(string archivePath) => !FailVerify && File.Exists(archivePath);
}
