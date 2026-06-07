using System.Text.Json;

namespace SteamHibernate.Core.Metadata;

public sealed class MetadataStore
{
    private readonly string _path;
    private readonly Dictionary<string, ArchiveRecord> _records;

    public MetadataStore(string path)
    {
        _path = path;
        _records = File.Exists(path)
            ? (JsonSerializer.Deserialize<List<ArchiveRecord>>(File.ReadAllText(path)) ?? new())
                .ToDictionary(r => r.AppId)
            : new();
    }

    public ArchiveRecord? Get(string appId) =>
        _records.TryGetValue(appId, out var r) ? r : null;

    public IReadOnlyCollection<ArchiveRecord> All => _records.Values;

    public void Upsert(ArchiveRecord record) { _records[record.AppId] = record; Save(); }
    public void Remove(string appId) { if (_records.Remove(appId)) Save(); }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var tmp = _path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(_records.Values.ToList()));
        File.Move(tmp, _path, overwrite: true); // atomic replace
    }
}
