using System;
using System.IO;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace SteamHibernate.App;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var cfgStore = new SteamHibernate.Core.Config.ConfigStore(
                SteamHibernate.Core.Config.ConfigStore.DefaultPath());
            var cfg = cfgStore.Load();

            var locator = OperatingSystem.IsWindows()
                ? (SteamHibernate.Core.Steam.ISteamLocator)new SteamHibernate.Core.Steam.WindowsSteamLocator()
                : new SteamHibernate.Core.Steam.ConfigSteamLocator(cfg.ArchiveRoot); // non-Windows: use configured path

            var scanner = new SteamHibernate.Core.Steam.GameScanner(locator);
            var (engine, restoreEngines) = SteamHibernate.Core.Engine.EngineFactory.Build(cfg);
            var store = new SteamHibernate.Core.Metadata.MetadataStore(
                Path.Combine(Path.GetDirectoryName(SteamHibernate.Core.Config.ConfigStore.DefaultPath())!, "archives.json"));
            // Empty ArchiveRoot => store archives inside each game's own Steam library (same drive).
            var tiering = new SteamHibernate.Core.Tiering.ManualTieringService(engine, store, cfg.ArchiveRoot, cfg.CompressionLevel, restoreEngines: restoreEngines);

            var mainVm = new SteamHibernate.App.ViewModels.MainViewModel();
            mainVm.Wire(scanner, tiering, store);
            mainVm.Refresh();

            desktop.MainWindow = new Views.MainWindow { DataContext = mainVm };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
