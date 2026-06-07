using SteamHibernate.Core.Metadata;
using Xunit;

public class MetadataStoreTests : IDisposable
{
    private readonly string _file = Path.Combine(Path.GetTempPath(), "shmeta_" + Guid.NewGuid().ToString("N") + ".json");
    public void Dispose() { if (File.Exists(_file)) File.Delete(_file); }

    [Fact]
    public void Upsert_and_reload_persists_records()
    {
        var store = new MetadataStore(_file);
        store.Upsert(new ArchiveRecord("70", "Half-Life", "/pkg/70",
            OriginalSize: 4_000_000, CompressedSize: 1_000_000, DateTimeOffset.UnixEpoch));
        store.Save();

        var reloaded = new MetadataStore(_file);
        var rec = reloaded.Get("70");
        Assert.NotNull(rec);
        Assert.Equal("Half-Life", rec!.GameName);
        Assert.Equal(1_000_000, rec.CompressedSize);
    }

    [Fact]
    public void Remove_deletes_record()
    {
        var store = new MetadataStore(_file);
        store.Upsert(new ArchiveRecord("1", "G", "/p", 10, 5, DateTimeOffset.UnixEpoch));
        store.Remove("1");
        Assert.Null(store.Get("1"));
    }
}
