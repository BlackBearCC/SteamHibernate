// src/SteamHibernate.Core/Engine/ExternalTool.cs
using System.Diagnostics;

namespace SteamHibernate.Core.Engine;

public static class ExternalTool
{
    public static int Run(string exe, IEnumerable<string> args, Action<string>? onLine = null)
    {
        var psi = new ProcessStartInfo
        {
            FileName = exe,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        using var p = new Process { StartInfo = psi };
        p.OutputDataReceived += (_, e) => { if (e.Data != null) { try { onLine?.Invoke(e.Data); } catch { /* progress is best-effort */ } } };
        p.ErrorDataReceived += (_, e) => { if (e.Data != null) { try { onLine?.Invoke(e.Data); } catch { /* progress is best-effort */ } } };
        if (!p.Start())
            throw new InvalidOperationException($"Failed to start process: {exe}");
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();
        p.WaitForExit();
        return p.ExitCode;
    }
}
