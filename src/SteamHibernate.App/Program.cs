using Avalonia;
using System;
using System.IO;
using System.Linq;
using SteamHibernate.Core.Config;
using SteamHibernate.Core.Engine;
using SteamHibernate.Core.Metadata;
using SteamHibernate.Core.Steam;
using SteamHibernate.Core.Tiering;

namespace SteamHibernate.App;

class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static int Main(string[] args)
    {
        // Headless CLI mode (scriptable / no display): list | compress <appid> | restore <appid>.
        if (args.Length > 0 && (args[0] is "list" or "compress" or "restore"))
            return RunCli(args);

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        return 0;
    }

    private static int RunCli(string[] args)
    {
        var cfgPath = ConfigStore.DefaultPath();
        var cfg = new ConfigStore(cfgPath).Load();

        ISteamLocator locator = OperatingSystem.IsWindows()
            ? new WindowsSteamLocator()
            : new ConfigSteamLocator(cfg.ArchiveRoot);
        var scanner = new GameScanner(locator);

        var (engine, restoreEngines) = EngineFactory.Build(cfg);

        var configDir = Path.GetDirectoryName(cfgPath)!;
        var store = new MetadataStore(Path.Combine(configDir, "archives.json"));
        var archiveRoot = string.IsNullOrWhiteSpace(cfg.ArchiveRoot)
            ? Path.Combine(configDir, "archives")
            : cfg.ArchiveRoot;
        var tiering = new ManualTieringService(engine, store, archiveRoot, cfg.CompressionLevel, restoreEngines: restoreEngines);

        int lastPct = -1;
        void Progress(ArchiveProgress p)
        {
            int pct = (int)Math.Round(p.Fraction * 100);
            if (pct != lastPct) { lastPct = pct; Console.WriteLine($"  [{p.Stage}] {pct}%"); }
        }

        const double GB = 1024.0 * 1024 * 1024;

        switch (args[0])
        {
            case "list":
                foreach (var g in scanner.Scan().OrderByDescending(g => g.SizeOnDisk))
                    Console.WriteLine($"{g.AppId,-10} {g.SizeOnDisk / GB,7:F1} GB  installed  {g.Name}");
                foreach (var r in store.All)
                    Console.WriteLine($"{r.AppId,-10} {r.CompressedSize / GB,7:F1} GB  ARCHIVED (was {r.OriginalSize / GB:F1} GB)  {r.GameName}");
                return 0;

            case "compress":
            {
                if (args.Length < 2) { Console.Error.WriteLine("usage: compress <appid>"); return 2; }
                var appId = args[1];
                var game = scanner.Scan().FirstOrDefault(g => g.AppId == appId);
                if (game is null) { Console.Error.WriteLine($"Game {appId} is not installed."); return 2; }
                Console.WriteLine($"Compressing {game.Name} ({game.SizeOnDisk / GB:F1} GB) -> {archiveRoot}");
                lastPct = -1;
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var res = tiering.Compress(game, Progress);
                sw.Stop();
                if (res.Success)
                {
                    var rec = store.Get(appId);
                    Console.WriteLine($"OK in {sw.Elapsed:hh\\:mm\\:ss}. {game.SizeOnDisk / GB:F1} GB -> {rec!.CompressedSize / GB:F1} GB ({(double)rec.CompressedSize / game.SizeOnDisk:P0} of original)");
                    return 0;
                }
                Console.Error.WriteLine($"FAIL: {res.Error}");
                return 1;
            }

            case "restore":
            {
                if (args.Length < 2) { Console.Error.WriteLine("usage: restore <appid>"); return 2; }
                var appId = args[1];
                Console.WriteLine($"Restoring {appId} ...");
                lastPct = -1;
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var res = tiering.Restore(appId, Progress);
                sw.Stop();
                if (res.Success) { Console.WriteLine($"OK restored in {sw.Elapsed:hh\\:mm\\:ss}."); return 0; }
                Console.Error.WriteLine($"FAIL: {res.Error}");
                return 1;
            }
        }
        return 0;
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();
}
