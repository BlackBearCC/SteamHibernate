using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SteamHibernate.Core.Steam;
using SteamHibernate.Core.Tiering;

namespace SteamHibernate.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    public ObservableCollection<GameRowViewModel> Games { get; } = new();

    private GameScanner? _scanner;
    private ManualTieringService? _tiering;
    private SteamHibernate.Core.Metadata.MetadataStore? _store;

    public void Wire(GameScanner scanner, ManualTieringService tiering,
                     SteamHibernate.Core.Metadata.MetadataStore store)
    {
        _scanner = scanner;
        _tiering = tiering;
        _store = store;
    }

    public void LoadRows(
        IReadOnlyList<InstalledGame> installed,
        ISet<string> archivedIds,
        IReadOnlyDictionary<string, string> archivedNames)
    {
        Games.Clear();
        foreach (var g in installed)
            Games.Add(new GameRowViewModel { AppId = g.AppId, Name = g.Name, SizeOnDisk = g.SizeOnDisk, Status = "Installed" });
        foreach (var id in archivedIds)
            Games.Add(new GameRowViewModel { AppId = id, Name = archivedNames.GetValueOrDefault(id, id), Status = "Archived" });
    }

    [RelayCommand]
    public void Refresh()
    {
        if (_scanner is null || _store is null) return;
        var installed = _scanner.Scan();
        var archived = _store.All;
        LoadRows(installed,
            new HashSet<string>(archived.Select(a => a.AppId)),
            archived.ToDictionary(a => a.AppId, a => a.GameName));
    }

    [RelayCommand]
    public async Task CompressAsync(GameRowViewModel row)
    {
        if (_tiering is null || _scanner is null) return;
        row.Busy = true; row.Status = "Compressing";
        var result = await Task.Run(() =>
        {
            var game = _scanner.Scan().FirstOrDefault(g => g.AppId == row.AppId);
            if (game is null) return TieringResult.Fail($"Game {row.AppId} not found during scan.");
            return _tiering.Compress(game, p => Dispatcher.UIThread.Post(() => row.Progress = p.Fraction));
        });
        row.Busy = false;
        row.Status = result.Success ? "Archived" : "Error";
        Refresh();
    }

    [RelayCommand]
    public async Task RestoreAsync(GameRowViewModel row)
    {
        if (_tiering is null) return;
        row.Busy = true; row.Status = "Restoring";
        var result = await Task.Run(() => _tiering.Restore(row.AppId,
            p => Dispatcher.UIThread.Post(() => row.Progress = p.Fraction)));
        row.Busy = false;
        row.Status = result.Success ? "Installed" : "Error";
        Refresh();
    }
}
