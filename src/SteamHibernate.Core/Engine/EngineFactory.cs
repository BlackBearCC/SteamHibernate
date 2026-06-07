using SteamHibernate.Core.Config;

namespace SteamHibernate.Core.Engine;

/// <summary>
/// Builds the active archive engine and the restore-engine registry from configuration.
/// The default engine is precomp+LZMA when EnablePrecomp is set (and precomp is available),
/// otherwise plain 7-Zip. Both engine extensions are always registered for restore so a
/// package made by either engine can be restored regardless of the current setting.
/// </summary>
public static class EngineFactory
{
    public static (IArchiveEngine engine, IReadOnlyDictionary<string, IArchiveEngine> restoreEngines) Build(AppConfig cfg)
    {
        var sevenZip = cfg.SevenZipPath ?? SevenZipEngine.FindBinary()
            ?? throw new InvalidOperationException("7-Zip not found; set SevenZipPath in config.");
        var seven = new SevenZipEngine(sevenZip);
        var restore = new Dictionary<string, IArchiveEngine> { [seven.ArchiveExtension] = seven };

        var precompPath = cfg.PrecompPath ?? PrecompLzmaEngine.FindBinary();
        PrecompLzmaEngine? precomp = precompPath is not null ? new PrecompLzmaEngine(sevenZip, precompPath) : null;
        if (precomp is not null) restore[precomp.ArchiveExtension] = precomp;

        if (cfg.EnablePrecomp && precomp is null)
            throw new InvalidOperationException("EnablePrecomp is set but precomp was not found; set PrecompPath.");

        IArchiveEngine engine = cfg.EnablePrecomp ? precomp! : seven;
        return (engine, restore);
    }
}
