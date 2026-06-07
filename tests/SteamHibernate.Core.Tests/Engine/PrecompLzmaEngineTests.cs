// tests/SteamHibernate.Core.Tests/Engine/PrecompLzmaEngineTests.cs
using System.Security.Cryptography;
using SteamHibernate.Core.Engine;
using SteamHibernate.Core.Metadata;
using SteamHibernate.Core.Package;
using SteamHibernate.Core.Steam;
using SteamHibernate.Core.Tiering;
using Xunit;

public class PrecompLzmaEngineTests : IDisposable
{
    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "shpc_" + Guid.NewGuid().ToString("N"));
    public PrecompLzmaEngineTests() => Directory.CreateDirectory(_tmp);
    public void Dispose() { if (Directory.Exists(_tmp)) Directory.Delete(_tmp, true); }

    // -------------------------------------------------------------------------
    // Helper: compute SHA-256 of a file
    // -------------------------------------------------------------------------
    private static string FileSha256(string path)
    {
        using var fs = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(fs));
    }

    // -------------------------------------------------------------------------
    // Test 1: full precomp+LZMA round-trip (skips if binaries not available)
    // -------------------------------------------------------------------------
    [SkippableFact]
    public void Compress_then_extract_roundtrips_bytes_exactly()
    {
        var precompPath = Environment.GetEnvironmentVariable("PRECOMP_PATH")
                          ?? PrecompLzmaEngine.FindBinary();
        var sevenZipPath = SevenZipEngine.FindBinary();
        Skip.If(precompPath is null, "precomp binary not found (set PRECOMP_PATH)");
        Skip.If(sevenZipPath is null, "7-Zip binary not found");

        // ---- Build source tree -----------------------------------------------
        var src = Path.Combine(_tmp, "src");
        var sub = Path.Combine(src, "sub");
        Directory.CreateDirectory(sub);

        // Compressible text content (helps see real ratio)
        var loremBase = "Lorem ipsum dolor sit amet, consectetur adipiscing elit. ";
        File.WriteAllText(Path.Combine(src, "readme.txt"),
            string.Concat(Enumerable.Repeat(loremBase, 200)));

        // Binary-ish content
        var rng = new Random(42);
        var binBytes = new byte[8192];
        rng.NextBytes(binBytes);
        File.WriteAllBytes(Path.Combine(src, "data.bin"), binBytes);

        // Subdir file
        File.WriteAllText(Path.Combine(sub, "config.ini"),
            "[section]\nkey=value\nanother=123\n" + new string('Z', 500));

        // ---- Collect source hashes before compression ------------------------
        var srcFiles = Directory.EnumerateFiles(src, "*", SearchOption.AllDirectories)
            .OrderBy(f => f)
            .ToList();
        var srcHashes = srcFiles.ToDictionary(
            f => Path.GetRelativePath(src, f),
            FileSha256);

        long srcTotalBytes = srcFiles.Sum(f => new FileInfo(f).Length);

        // ---- Compress -------------------------------------------------------
        var engine  = new PrecompLzmaEngine(sevenZipPath!, precompPath!);
        var archive = Path.Combine(_tmp, "out.pc7z");

        var stages = new List<string>();
        engine.Compress(src, archive, level: 5, p =>
        {
            if (!stages.Contains(p.Stage)) stages.Add(p.Stage);
        });

        Assert.True(File.Exists(archive), "archive file should exist after Compress");
        Assert.True(engine.VerifyIntegrity(archive), "VerifyIntegrity should return true for valid archive");

        long archiveBytes = new FileInfo(archive).Length;

        // ---- Extract -------------------------------------------------------
        var dst = Path.Combine(_tmp, "dst");
        engine.Extract(archive, dst, _ => { });

        // ---- Byte-exact comparison ------------------------------------------
        var dstFiles = Directory.EnumerateFiles(dst, "*", SearchOption.AllDirectories)
            .OrderBy(f => f)
            .ToList();
        var dstHashes = dstFiles.ToDictionary(
            f => Path.GetRelativePath(dst, f),
            FileSha256);

        Assert.Equal(srcHashes.Keys.OrderBy(k => k), dstHashes.Keys.OrderBy(k => k));
        foreach (var rel in srcHashes.Keys)
        {
            Assert.True(dstHashes.ContainsKey(rel), $"Missing file in output: {rel}");
            Assert.Equal(srcHashes[rel], dstHashes[rel]);
        }

        // ---- Compression ratio report (informational) -----------------------
        double ratio = archiveBytes > 0 ? (double)srcTotalBytes / archiveBytes : 0;
        // Not asserting a specific ratio — just ensure the archive is not obviously broken.
        Assert.True(archiveBytes > 0, $"archive is non-empty; ratio={ratio:F2}x (src={srcTotalBytes}B, arc={archiveBytes}B)");

        // ---- Stage names were emitted correctly -----------------------------
        Assert.Contains("Packing",        stages);
        Assert.Contains("Precompressing", stages);
        Assert.Contains("Compressing",    stages);
    }

    // -------------------------------------------------------------------------
    // Test 2: FindBinary searches PATH (deterministic, no external tools needed)
    // -------------------------------------------------------------------------
    [Fact]
    public void FindBinary_returns_null_when_not_on_path_and_not_windows_default()
    {
        // Override PATH to something empty so no binary is found.
        var original = Environment.GetEnvironmentVariable("PATH");
        try
        {
            Environment.SetEnvironmentVariable("PATH", "");
            // C:\Program Files\precomp\precomp.exe won't exist on Linux.
            var result = PrecompLzmaEngine.FindBinary();
            Assert.Null(result);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", original);
        }
    }

    // -------------------------------------------------------------------------
    // Test 3: ManualTieringService restore-engine selection
    //
    // Verifies that when restoreEngines is provided, Restore picks the engine
    // whose ArchiveExtension matches the package header's EngineExtension,
    // regardless of which engine was used to compress.
    // Uses two distinct fake engines (FakeArchiveEngine + FakePc7zEngine).
    // No external binaries required.
    // -------------------------------------------------------------------------
    [Fact]
    public void Restore_selects_engine_matching_package_header_extension()
    {
        // ---- Setup library + game dir --------------------------------------
        var lib = new SteamLibrary(Path.Combine(_tmp, "lib"));
        Directory.CreateDirectory(lib.CommonPath);
        var gameDir = Path.Combine(lib.CommonPath, "TestGame");
        Directory.CreateDirectory(gameDir);
        File.WriteAllText(Path.Combine(gameDir, "g.dat"), "restore-engine-test-content");
        File.WriteAllText(lib.AppManifestPath("777"), "\"AppState\" { \"appid\" \"777\" }");

        var game = new InstalledGame("777", "TestGame", gameDir, 5, null, lib);
        var store = new MetadataStore(Path.Combine(_tmp, "meta.json"));
        var archiveRoot = Path.Combine(_tmp, "archives");

        // The active compress engine is the ".fake" FakeArchiveEngine.
        var compressEngine = new FakeArchiveEngine();
        var svc = new ManualTieringService(compressEngine, store, archiveRoot, level: 5);
        Assert.True(svc.Compress(game, _ => { }).Success);

        var rec = store.Get("777");
        Assert.NotNull(rec);

        // Confirm the package header records ".fake" as the engine extension.
        var header = GamePackage.ReadHeader(rec!.PackageDir);
        Assert.Equal(".fake", header.EngineExtension);

        // ---- Build a restore-engines registry ------------------------------
        // We add a FakePc7zEngine keyed on ".pc7z" AND the compressEngine keyed on ".fake".
        // The svc2 default engine is a *different* (failing) engine so we can prove the
        // registry is used rather than the default.
        var wrongEngine  = new FakeArchiveEngine { FailVerify = true };  // would fail if chosen
        var correctEngine = new FakeArchiveEngine();                       // same format, should be chosen

        var restoreEngines = new Dictionary<string, IArchiveEngine>
        {
            [".fake"]  = correctEngine,
            [".pc7z"]  = new FakePc7zEngine(),  // irrelevant for this package but exercises the dict
        };

        var svc2 = new ManualTieringService(
            wrongEngine, store, archiveRoot, level: 5,
            resolveLibrary: null,
            restoreEngines: restoreEngines);

        // Restore should succeed because the registry maps ".fake" -> correctEngine (FailVerify=false).
        var result = svc2.Restore("777", _ => { });
        Assert.True(result.Success, $"Restore failed: {result.Error}");
        Assert.Equal("restore-engine-test-content",
            File.ReadAllText(Path.Combine(game.InstallDir, "g.dat")));
        Assert.Null(store.Get("777"));
    }

    // -------------------------------------------------------------------------
    // Test 4: restoreEngines = null falls back to _engine (backward compat)
    // -------------------------------------------------------------------------
    [Fact]
    public void Restore_without_restore_engines_uses_default_engine()
    {
        var lib = new SteamLibrary(Path.Combine(_tmp, "lib2"));
        Directory.CreateDirectory(lib.CommonPath);
        var gameDir = Path.Combine(lib.CommonPath, "MyGame2");
        Directory.CreateDirectory(gameDir);
        File.WriteAllText(Path.Combine(gameDir, "f.txt"), "hello2");
        File.WriteAllText(lib.AppManifestPath("888"), "\"AppState\" { \"appid\" \"888\" }");

        var game = new InstalledGame("888", "MyGame2", gameDir, 5, null, lib);
        var store = new MetadataStore(Path.Combine(_tmp, "meta2.json"));
        var archiveRoot = Path.Combine(_tmp, "archives2");

        var engine = new FakeArchiveEngine();
        // No restoreEngines param — backward compat path.
        var svc = new ManualTieringService(engine, store, archiveRoot, level: 5);

        Assert.True(svc.Compress(game, _ => { }).Success);
        var result = svc.Restore("888", _ => { });
        Assert.True(result.Success);
        Assert.Equal("hello2", File.ReadAllText(Path.Combine(game.InstallDir, "f.txt")));
    }

    // -------------------------------------------------------------------------
    // Minimal fake engine with ".pc7z" extension (for the restore-engine dict)
    // -------------------------------------------------------------------------
    private sealed class FakePc7zEngine : IArchiveEngine
    {
        public string ArchiveExtension => ".pc7z";

        public void Compress(string srcDir, string archivePath, int level, Action<ArchiveProgress> progress)
        {
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
                var i   = line.IndexOf('\t');
                var rel = line[..i];
                var full = Path.Combine(dstDir, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(full)!);
                File.WriteAllText(full, line[(i + 1)..]);
            }
            progress(new ArchiveProgress("Extracting", 1));
        }

        public bool VerifyIntegrity(string archivePath) => File.Exists(archivePath);
    }
}
