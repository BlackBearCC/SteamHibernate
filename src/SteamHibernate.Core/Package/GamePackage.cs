// src/SteamHibernate.Core/Package/GamePackage.cs
using System.Linq;
using System.Text.Json;
using SteamHibernate.Core.Engine;

namespace SteamHibernate.Core.Package;

public static class GamePackage
{
    private const string ManifestFile = "manifest.json";
    private const string HeaderFile = "header.json";

    public static string DataPath(string packageDir, string engineExtension) =>
        Path.Combine(packageDir, "data" + engineExtension);

    public static PackageHeader Pack(
        IArchiveEngine engine, string appId, string gameDir, string acfPath,
        string packageDir, int level, Action<ArchiveProgress> progress)
    {
        // Note: on Compress failure a partial package dir may remain; the caller (tiering layer) is responsible for cleanup.
        Directory.CreateDirectory(packageDir);

        var manifest = DirectoryManifest.Capture(gameDir);
        File.WriteAllText(Path.Combine(packageDir, ManifestFile),
            JsonSerializer.Serialize(manifest));

        if (File.Exists(acfPath))
            File.Copy(acfPath, Path.Combine(packageDir, Path.GetFileName(acfPath)), overwrite: true);

        var dataPath = DataPath(packageDir, engine.ArchiveExtension);
        engine.Compress(gameDir, dataPath, level, progress);

        var header = new PackageHeader(
            AppId: appId,
            GameName: Path.GetFileName(gameDir),
            InstallDirName: Path.GetFileName(gameDir),
            OriginalSize: manifest.TotalSize,
            CompressedSize: new FileInfo(dataPath).Length,
            EngineExtension: engine.ArchiveExtension,
            CreatedUtc: DateTimeOffset.UtcNow);
        File.WriteAllText(Path.Combine(packageDir, HeaderFile), JsonSerializer.Serialize(header));
        return header;
    }

    public static PackageHeader ReadHeader(string packageDir) =>
        JsonSerializer.Deserialize<PackageHeader>(
            File.ReadAllText(Path.Combine(packageDir, HeaderFile)))
        ?? throw new InvalidDataException("Invalid package header.");

    public static void Unpack(IArchiveEngine engine, string packageDir, string gameDir, string acfPath,
        Action<ArchiveProgress> progress)
    {
        var header = ReadHeader(packageDir);
        var dataPath = DataPath(packageDir, header.EngineExtension);
        if (!engine.VerifyIntegrity(dataPath))
            throw new InvalidDataException("Package data failed integrity check.");
        engine.Extract(dataPath, gameDir, progress);

        var acfInPkg = Directory.GetFiles(packageDir, "appmanifest_*.acf").SingleOrDefault();
        if (acfInPkg != null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(acfPath)!);
            File.Copy(acfInPkg, acfPath, overwrite: true);
        }
    }
}
