// tests/.../Engine/SevenZipEngineTests.cs
using SteamHibernate.Core.Engine;
using Xunit;

public class SevenZipEngineTests : IDisposable
{
    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "sh7z_" + Guid.NewGuid().ToString("N"));
    public SevenZipEngineTests() => Directory.CreateDirectory(_tmp);
    public void Dispose() { if (Directory.Exists(_tmp)) Directory.Delete(_tmp, true); }

    [SkippableFact]
    public void Compress_then_extract_roundtrips_bytes()
    {
        var exe = SevenZipEngine.FindBinary();
        Skip.If(exe is null, "7-Zip binary not found");

        var src = Path.Combine(_tmp, "src");
        Directory.CreateDirectory(Path.Combine(src, "sub"));
        File.WriteAllText(Path.Combine(src, "a.bin"), new string('x', 10000));
        File.WriteAllText(Path.Combine(src, "sub", "b.bin"), "content-b");

        var engine = new SevenZipEngine(exe!);
        var archive = Path.Combine(_tmp, "out.7z");
        engine.Compress(src, archive, level: 9, _ => { });

        Assert.True(File.Exists(archive));
        Assert.True(engine.VerifyIntegrity(archive));

        var dst = Path.Combine(_tmp, "dst");
        engine.Extract(archive, dst, _ => { });

        Assert.Equal(new string('x', 10000), File.ReadAllText(Path.Combine(dst, "a.bin")));
        Assert.Equal("content-b", File.ReadAllText(Path.Combine(dst, "sub", "b.bin")));
    }
}
