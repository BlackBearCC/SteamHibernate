// src/SteamHibernate.Core/Steam/WindowsSteamLocator.cs
using Microsoft.Win32;

namespace SteamHibernate.Core.Steam;

public sealed class WindowsSteamLocator : ISteamLocator
{
    private readonly ConfigSteamLocator _inner;

    public WindowsSteamLocator()
    {
        SteamRoot = ReadSteamPath()
            ?? throw new InvalidOperationException("Steam install path not found in registry.");
        _inner = new ConfigSteamLocator(SteamRoot);
    }

    public string SteamRoot { get; }
    public IReadOnlyList<SteamLibrary> GetLibraries() => _inner.GetLibraries();
    public IReadOnlyList<string> GetUserConfigPaths() => _inner.GetUserConfigPaths();

    private static string? ReadSteamPath()
    {
        if (!OperatingSystem.IsWindows()) return null;
        return Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string
            ?? Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath", null) as string;
    }
}
