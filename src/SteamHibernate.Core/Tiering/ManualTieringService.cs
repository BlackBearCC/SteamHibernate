using System.IO;
using SteamHibernate.Core.Engine;
using SteamHibernate.Core.Metadata;
using SteamHibernate.Core.Package;
using SteamHibernate.Core.Steam;

namespace SteamHibernate.Core.Tiering;

public sealed class ManualTieringService
{
    private const string LibraryFile = "library.txt";
    private const string RestoringSuffix = ".restoring";

    private readonly IArchiveEngine _engine;
    private readonly MetadataStore _store;
    private readonly string _archiveRoot;
    private readonly int _level;
    private readonly Func<string, SteamLibrary>? _resolveLibrary;

    public ManualTieringService(IArchiveEngine engine, MetadataStore store,
        string archiveRoot, int level, Func<string, SteamLibrary>? resolveLibrary = null)
    {
        _engine = engine; _store = store; _archiveRoot = archiveRoot;
        _level = level; _resolveLibrary = resolveLibrary;
    }

    public TieringResult Compress(InstalledGame game, Action<ArchiveProgress> progress)
    {
        if (!Directory.Exists(game.InstallDir))
            return TieringResult.Fail($"Game directory not found: {game.InstallDir} (appId={game.AppId})");

        var pkgDir = Path.Combine(_archiveRoot, game.AppId);
        PackageHeader header;

        // Phase 1 — build + verify the package. Any failure here is safe: the original game dir is never touched.
        try
        {
            if (Directory.Exists(pkgDir)) Directory.Delete(pkgDir, true);
            header = GamePackage.Pack(_engine, game.AppId, game.InstallDir,
                game.Library.AppManifestPath(game.AppId), pkgDir, _level, progress);
            var dataPath = GamePackage.DataPath(pkgDir, _engine.ArchiveExtension);
            if (!_engine.VerifyIntegrity(dataPath))
                throw new InvalidDataException("Integrity check failed after compression.");
            File.WriteAllText(Path.Combine(pkgDir, LibraryFile), game.Library.RootPath);
        }
        catch (Exception ex)
        {
            if (Directory.Exists(pkgDir)) Directory.Delete(pkgDir, true); // original dir untouched in this phase
            return TieringResult.Fail($"Failed to archive {game.AppId}: {ex.Message}");
        }

        // Phase 2 — commit metadata BEFORE removing the only other copy. If this fails, original dir is intact; drop the package.
        try
        {
            _store.Upsert(new ArchiveRecord(game.AppId, game.Name, pkgDir,
                header.OriginalSize, header.CompressedSize, header.CreatedUtc));
        }
        catch (Exception ex)
        {
            if (Directory.Exists(pkgDir)) Directory.Delete(pkgDir, true);
            return TieringResult.Fail($"Failed to record archive metadata for {game.AppId}: {ex.Message}");
        }

        // Phase 3 — remove the original. If this fails, archive + metadata remain (recoverable). Do NOT delete the package here.
        try
        {
            Directory.Delete(game.InstallDir, true);
        }
        catch (Exception ex)
        {
            return TieringResult.Fail($"Archived {game.AppId} but failed to remove the original folder {game.InstallDir}: {ex.Message}. The archive is intact; remove the folder manually.");
        }

        return TieringResult.Ok();
    }

    public TieringResult Restore(string appId, Action<ArchiveProgress> progress)
    {
        var rec = _store.Get(appId);
        if (rec is null) return TieringResult.Fail("No archive record for app " + appId);

        var libraryRoot = File.ReadAllText(Path.Combine(rec.PackageDir, LibraryFile)).Trim();
        var lib = _resolveLibrary?.Invoke(appId) ?? new SteamLibrary(libraryRoot);
        var header = GamePackage.ReadHeader(rec.PackageDir);

        var finalGameDir = Path.Combine(lib.CommonPath, header.InstallDirName);
        var tmpDir = finalGameDir + RestoringSuffix;
        try
        {
            if (Directory.Exists(tmpDir)) Directory.Delete(tmpDir, true);
            GamePackage.Unpack(_engine, rec.PackageDir, tmpDir,
                lib.AppManifestPath(appId), progress);

            if (Directory.Exists(finalGameDir)) Directory.Delete(finalGameDir, true);
            Directory.Move(tmpDir, finalGameDir); // atomic placement
            // Package dir is intentionally retained after restore so re-archiving is instant; reclaiming it is a future enhancement.
            _store.Remove(appId);
            return TieringResult.Ok();
        }
        catch (Exception ex)
        {
            if (Directory.Exists(tmpDir)) Directory.Delete(tmpDir, true);
            return TieringResult.Fail(ex.Message);
        }
    }
}
