namespace SteamHibernate.Core.Config;

public sealed record AppConfig
{
    public string ArchiveRoot { get; init; } = "";
    public int CompressionLevel { get; init; } = 9;
    public string? SevenZipPath { get; init; }
    public bool EnableSrep { get; init; } = false;
    public bool EnablePrecomp { get; init; } = false;
    public string? PrecompPath { get; init; }
    public int IdleDays { get; init; } = 30; // for Plan2 auto mode
    public string DefaultMode { get; init; } = "Manual"; // Manual | Auto
}
