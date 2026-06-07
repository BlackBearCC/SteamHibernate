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
            var exe = cfg.SevenZipPath ?? SteamHibernate.Core.Engine.SevenZipEngine.FindBinary()
                      ?? throw new System.InvalidOperationException("7-Zip not found; set it in settings.");
            var engine = new SteamHibernate.Core.Engine.SevenZipEngine(exe);
            var store = new SteamHibernate.Core.Metadata.MetadataStore(
                Path.Combine(Path.GetDirectoryName(SteamHibernate.Core.Config.ConfigStore.DefaultPath())!, "archives.json"));
            var archiveRoot = string.IsNullOrWhiteSpace(cfg.ArchiveRoot)
                ? Path.Combine(Path.GetDirectoryName(SteamHibernate.Core.Config.ConfigStore.DefaultPath())!, "archives")
                : cfg.ArchiveRoot;
            var tiering = new SteamHibernate.Core.Tiering.ManualTieringService(engine, store, archiveRoot, cfg.CompressionLevel);

            var mainVm = new SteamHibernate.App.ViewModels.MainViewModel();
            mainVm.Wire(scanner, tiering, store);
            mainVm.Refresh();

            desktop.MainWindow = new Views.MainWindow { DataContext = mainVm };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
