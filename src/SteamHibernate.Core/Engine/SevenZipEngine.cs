// src/SteamHibernate.Core/Engine/SevenZipEngine.cs
namespace SteamHibernate.Core.Engine;

public sealed class SevenZipEngine : IArchiveEngine
{
    private readonly string _exe;
    public SevenZipEngine(string sevenZipExePath) => _exe = sevenZipExePath;

    public string ArchiveExtension => ".7z";

    public static string? FindBinary()
    {
        foreach (var name in new[] { "7zz", "7z", "7za", "7z.exe", "7za.exe" })
        {
            var path = ResolveOnPath(name);
            if (path != null) return path;
        }
        var win = @"C:\Program Files\7-Zip\7z.exe";
        return File.Exists(win) ? win : null;
    }

    private static string? ResolveOnPath(string name)
    {
        var dirs = (Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
        foreach (var d in dirs)
        {
            var full = Path.Combine(d, name);
            if (File.Exists(full)) return full;
        }
        return null;
    }

    public void Compress(string srcDir, string archivePath, int level, Action<ArchiveProgress> progress)
    {
        progress(new ArchiveProgress("Compressing", 0));
        // -t7z LZMA2 solid; -mx level; -bsp1 outputs progress to stdout
        var code = ExternalTool.Run(_exe, new[]
        {
            "a", "-t7z", $"-mx={level}", "-m0=lzma2", "-ms=on", "-bsp1", "-y",
            // Path.Combine(srcDir, "*") => archive contents WITHOUT a top-level folder wrapper; 7z expands the glob itself (no shell).
            archivePath, Path.Combine(srcDir, "*")
        }, line => TryReportPercent(line, "Compressing", progress));
        if (code != 0) throw new IOException($"7z compress failed (exit {code}).");
        progress(new ArchiveProgress("Compressing", 1));
    }

    public void Extract(string archivePath, string dstDir, Action<ArchiveProgress> progress)
    {
        Directory.CreateDirectory(dstDir);
        progress(new ArchiveProgress("Extracting", 0));
        var code = ExternalTool.Run(_exe, new[]
        {
            // 7z -o flag: output dir is concatenated directly to -o with no space (7z-specific).
            "x", "-bsp1", "-y", $"-o{dstDir}", archivePath
        }, line => TryReportPercent(line, "Extracting", progress));
        if (code != 0) throw new IOException($"7z extract failed (exit {code}).");
        progress(new ArchiveProgress("Extracting", 1));
    }

    public bool VerifyIntegrity(string archivePath)
        => ExternalTool.Run(_exe, new[] { "t", "-y", archivePath }) == 0;

    private static void TryReportPercent(string line, string stage, Action<ArchiveProgress> progress)
    {
        // 7z -bsp1 lines look like " 42% ..."
        var idx = line.IndexOf('%');
        if (idx <= 0) return;
        int start = idx - 1;
        while (start >= 0 && char.IsDigit(line[start])) start--;
        if (int.TryParse(line.AsSpan(start + 1, idx - start - 1), out var pct))
            progress(new ArchiveProgress(stage, Math.Clamp(pct / 100.0, 0, 1)));
    }
}
