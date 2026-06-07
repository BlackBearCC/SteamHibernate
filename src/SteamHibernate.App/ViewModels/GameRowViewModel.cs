using CommunityToolkit.Mvvm.ComponentModel;

namespace SteamHibernate.App.ViewModels;

public partial class GameRowViewModel : ObservableObject
{
    public required string AppId { get; init; }
    public required string Name { get; init; }
    public long SizeOnDisk { get; init; }

    [ObservableProperty] private string _status = "Installed";
    [ObservableProperty] private double _progress;       // 0..1
    [ObservableProperty] private bool _busy;

    public string SizeDisplay => $"{SizeOnDisk / 1024.0 / 1024 / 1024:F1} GB";
}
