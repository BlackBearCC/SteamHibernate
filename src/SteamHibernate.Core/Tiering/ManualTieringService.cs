using SteamHibernate.Core.Engine;
using SteamHibernate.Core.Metadata;
using SteamHibernate.Core.Package;
using SteamHibernate.Core.Steam;

namespace SteamHibernate.Core.Tiering;

public sealed class ManualTieringService
{
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
            return TieringResult.Fail("Game directory not found.");

        var pkgDir = Path.Combine(_archiveRoot, game.AppId);
        try
        {
            if (Directory.Exists(pkgDir)) Directory.Delete(pkgDir, true);

            var header = GamePackage.Pack(_engine, game.AppId, game.InstallDir,
                game.Library.AppManifestPath(game.AppId), pkgDir, _level, progress);

            // Verify integrity before touching the original — commit-on-success
            var dataPath = Path.Combine(pkgDir, "data" + _engine.ArchiveExtension);
            if (!_engine.VerifyIntegrity(dataPath))
                throw new InvalidDataException("Integrity check failed after compression.");

            // Write library root so Restore can locate the target library
            File.WriteAllText(Path.Combine(pkgDir, "library.txt"), game.Library.RootPath);

            Directory.Delete(game.InstallDir, true); // only deleted after successful verify
            _store.Upsert(new ArchiveRecord(game.AppId, game.Name, pkgDir,
                header.OriginalSize, header.CompressedSize, header.CreatedUtc));
            return TieringResult.Ok();
        }
        catch (Exception ex)
        {
            if (Directory.Exists(pkgDir)) Directory.Delete(pkgDir, true); // clean up partial
            return TieringResult.Fail(ex.Message);
        }
    }

    public TieringResult Restore(string appId, Action<ArchiveProgress> progress)
    {
        var rec = _store.Get(appId);
        if (rec is null) return TieringResult.Fail("No archive record for app " + appId);

        var libraryRoot = File.ReadAllText(Path.Combine(rec.PackageDir, "library.txt")).Trim();
        var lib = _resolveLibrary?.Invoke(appId) ?? new SteamLibrary(libraryRoot);
        var header = GamePackage.ReadHeader(rec.PackageDir);

        var finalGameDir = Path.Combine(lib.CommonPath, header.InstallDirName);
        var tmpDir = finalGameDir + ".restoring";
        try
        {
            if (Directory.Exists(tmpDir)) Directory.Delete(tmpDir, true);
            GamePackage.Unpack(_engine, rec.PackageDir, tmpDir,
                lib.AppManifestPath(appId), progress);

            if (Directory.Exists(finalGameDir)) Directory.Delete(finalGameDir, true);
            Directory.Move(tmpDir, finalGameDir); // atomic placement
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
