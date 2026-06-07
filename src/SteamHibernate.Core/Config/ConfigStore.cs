using System.Text.Json;

namespace SteamHibernate.Core.Config;

public sealed class ConfigStore
{
    private readonly string _path;
    public ConfigStore(string path) => _path = path;

    public AppConfig Load() =>
        File.Exists(_path)
            ? JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(_path)) ?? new AppConfig()
            : new AppConfig();

    public void Save(AppConfig config)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var tmp = _path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(config,
            new JsonSerializerOptions { WriteIndented = true }));
        File.Move(tmp, _path, overwrite: true);
    }

    public static string DefaultPath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "SteamHibernate", "config.json");
}
