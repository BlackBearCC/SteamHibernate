// src/SteamHibernate.Core/Engine/PrecompLzmaEngine.cs
namespace SteamHibernate.Core.Engine;

/// <summary>
/// IArchiveEngine that pipelines: STORE-7z → precomp → LZMA2-7z.
/// The outer archive extension is ".pc7z", distinct from SevenZipEngine's ".7z",
/// so packages created by this engine are self-identifying on restore.
///
/// Compress dry-run-restores the PCF before returning, so "Compress succeeds +
/// VerifyIntegrity passes" implies the archive is restorable (the safety model relies on this).
/// Peak temp usage is ~2x the source size (store container + pcf transiently).
/// NOTE: precomp v0.4.7 is labelled a DEVELOPMENT build; the dry-run is the safety net for that.
/// </summary>
public sealed class PrecompLzmaEngine : IArchiveEngine
{
    private readonly string _sevenZip;
    private readonly string _precomp;

    public PrecompLzmaEngine(string sevenZipPath, string precompPath)
    {
        _sevenZip = sevenZipPath;
        _precomp  = precompPath;
    }

    public string ArchiveExtension => ".pc7z";

    public static string? FindBinary()
    {
        var names = new[] { "precomp", "precomp.exe" };
        // Prefer a copy shipped next to the app (the installer bundles precomp.exe).
        foreach (var name in names)
        {
            var local = Path.Combine(AppContext.BaseDirectory, name);
            if (File.Exists(local)) return local;
        }
        foreach (var name in names)
        {
            var path = ResolveOnPath(name);
            if (path != null) return path;
        }
        var win = @"C:\Program Files\precomp\precomp.exe";
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
        var workDir = archivePath + ".work";
        try
        {
            Directory.CreateDirectory(workDir);
            var storePath = Path.Combine(workDir, "store.7z");
            var pcfPath   = Path.Combine(workDir, "store.pcf");

            // Step 1 — STORE all source files into an uncompressed 7z container.
            progress(new ArchiveProgress("Packing", 0));
            var storeCode = ExternalTool.Run(_sevenZip, new[]
            {
                "a", "-t7z", "-m0=copy", "-y",
                storePath, Path.Combine(srcDir, "*")
            }, null);
            if (storeCode != 0)
                throw new IOException($"7z store failed (exit {storeCode}).");
            progress(new ArchiveProgress("Packing", 1));

            // Step 2 — precomp: pre-process deflate streams in the container.
            // precomp exits 0 = success (made smaller), 2 = success (wrote anyway), 1 = error.
            progress(new ArchiveProgress("Precompressing", 0));
            var pcCode = ExternalTool.Run(_precomp, new[]
            {
                $"-o{pcfPath}", storePath
            }, line => TryReportPrecomp(line, "Precompressing", progress));
            if (pcCode != 0 && pcCode != 2)
                throw new IOException($"precomp failed (exit {pcCode}).");
            if (!File.Exists(pcfPath))
                throw new IOException("precomp did not produce output file.");

            // The .pcf is self-contained — precomp -r reconstructs store.7z from it alone.
            // Delete store.7z now to keep peak temp usage at ~2x source size (matters for big games).
            File.Delete(storePath);

            // Integrity gate: the system's safety model deletes the original game once Compress
            // succeeds + VerifyIntegrity passes. VerifyIntegrity only checks the outer LZMA layer,
            // which can't prove the PCF is restorable. So dry-run precomp restore HERE, before the
            // original is ever touched. If precomp produced an unrestorable PCF, fail loudly now.
            var verifyPath = Path.Combine(workDir, "verify.7z");
            var dryRun = ExternalTool.Run(_precomp, new[] { "-r", $"-o{verifyPath}", pcfPath }, null);
            if (dryRun != 0)
                throw new IOException($"precomp produced an unrestorable PCF (restore dry-run exit {dryRun}).");
            File.Delete(verifyPath);
            progress(new ArchiveProgress("Precompressing", 1));

            // Step 3 — LZMA2-compress the pcf into the final archive.
            // 7z stores only the basename ("store.pcf") when given an absolute file path,
            // so Extract can reliably locate workDir/store.pcf after expanding the outer archive.
            progress(new ArchiveProgress("Compressing", 0));
            var lzmaCode = ExternalTool.Run(_sevenZip, new[]
            {
                "a", "-t7z", $"-mx={level}", "-m0=lzma2", "-ms=on", "-bsp1", "-y",
                archivePath, pcfPath
            }, line => TryReportPercent(line, "Compressing", progress));
            if (lzmaCode != 0)
                throw new IOException($"7z LZMA2 compress failed (exit {lzmaCode}).");
            progress(new ArchiveProgress("Compressing", 1));
        }
        finally
        {
            if (Directory.Exists(workDir))
                try { Directory.Delete(workDir, true); } catch { /* best-effort */ }
        }
    }

    public void Extract(string archivePath, string dstDir, Action<ArchiveProgress> progress)
    {
        var workDir = archivePath + ".work";
        try
        {
            Directory.CreateDirectory(workDir);
            var pcfPath   = Path.Combine(workDir, "store.pcf");
            var storePath = Path.Combine(workDir, "store.7z");

            // Step 1 — expand the outer LZMA2 archive to get store.pcf.
            progress(new ArchiveProgress("Extracting", 0));
            var outerCode = ExternalTool.Run(_sevenZip, new[]
            {
                "x", "-bsp1", "-y", $"-o{workDir}", archivePath
            }, line => TryReportPercent(line, "Extracting", progress));
            if (outerCode != 0)
                throw new IOException($"7z extract (outer) failed (exit {outerCode}).");
            if (!File.Exists(pcfPath))
                throw new IOException($"Expected 'store.pcf' inside archive but it was not found in {workDir}.");
            progress(new ArchiveProgress("Extracting", 1));

            // Step 2 — precomp restore: reconstruct the bit-exact STORE container.
            progress(new ArchiveProgress("Restoring", 0));
            var rcCode = ExternalTool.Run(_precomp, new[]
            {
                "-r", $"-o{storePath}", pcfPath
            }, null);
            if (rcCode != 0)
                throw new IOException($"precomp restore failed (exit {rcCode}).");
            progress(new ArchiveProgress("Restoring", 1));

            // Step 3 — extract the inner STORE container into dstDir.
            Directory.CreateDirectory(dstDir);
            progress(new ArchiveProgress("Unpacking", 0));
            var innerCode = ExternalTool.Run(_sevenZip, new[]
            {
                "x", "-bsp1", "-y", $"-o{dstDir}", storePath
            }, line => TryReportPercent(line, "Unpacking", progress));
            if (innerCode != 0)
                throw new IOException($"7z extract (inner) failed (exit {innerCode}).");
            progress(new ArchiveProgress("Unpacking", 1));
        }
        finally
        {
            if (Directory.Exists(workDir))
                try { Directory.Delete(workDir, true); } catch { /* best-effort */ }
        }
    }

    /// <summary>Verifies the outer LZMA2 container (precomp restore is bit-exact by design).</summary>
    public bool VerifyIntegrity(string archivePath)
        => ExternalTool.Run(_sevenZip, new[] { "t", "-y", archivePath }) == 0;

    private static void TryReportPercent(string line, string stage, Action<ArchiveProgress> progress)
    {
        var idx = line.IndexOf('%');
        if (idx <= 0) return;
        int start = idx - 1;
        while (start >= 0 && char.IsDigit(line[start])) start--;
        if (int.TryParse(line.AsSpan(start + 1, idx - start - 1), out var pct))
        {
            try { progress(new ArchiveProgress(stage, Math.Clamp(pct / 100.0, 0, 1))); }
            catch { /* progress is best-effort */ }
        }
    }

    private static void TryReportPrecomp(string line, string stage, Action<ArchiveProgress> progress)
    {
        // precomp outputs lines like "  42.00% lzma ..." or "  100.00% - New size: ..."
        var idx = line.IndexOf('%');
        if (idx <= 0) return;
        int start = idx - 1;
        while (start >= 0 && (char.IsDigit(line[start]) || line[start] == '.')) start--;
        if (double.TryParse(line.AsSpan(start + 1, idx - start - 1),
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var pct))
        {
            try { progress(new ArchiveProgress(stage, Math.Clamp(pct / 100.0, 0, 1))); }
            catch { /* progress is best-effort */ }
        }
    }
}
