// src/SteamHibernate.Core/Engine/IArchiveEngine.cs
namespace SteamHibernate.Core.Engine;

public interface IArchiveEngine
{
    string ArchiveExtension { get; }
    void Compress(string srcDir, string archivePath, int level, Action<ArchiveProgress> progress);
    void Extract(string archivePath, string dstDir, Action<ArchiveProgress> progress);
    bool VerifyIntegrity(string archivePath);
}
