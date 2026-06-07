# SteamHibernate Plan 1 — Core + Manual 模式 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 交付一个 Steam 专用、跨平台可测的核心库 + Avalonia GUI,玩家可手动把不玩的 Steam 游戏极致压缩成归档包并随时还原,Steam 还原后秒认、不重下。

**Architecture:** 纯逻辑放 `SteamHibernate.Core` 类库(无 UI、无 ProjFS、无注册表硬依赖),通过 `ISteamLocator` / `IArchiveEngine` 接口隔离平台与外部 exe;`SteamHibernate.App`(Avalonia)只做展示与命令转发。压缩走外部 7-Zip(LZMA2 solid),precomp 作为可选装饰器叠加。所有状态切换 commit-on-success,绝不先删后压。

**Tech Stack:** .NET 8、C#、Avalonia 11、xUnit、外部 `7z`/`7zz`(LZMA2),后续可选 `precomp`。

---

## File Structure

```
SteamHibernate.sln
src/
  SteamHibernate.Core/
    SteamHibernate.Core.csproj
    Vdf/VdfParser.cs                 # Valve KeyValues 文本解析
    Vdf/VdfNode.cs                   # 解析结果树
    Steam/ISteamLocator.cs           # 定位 Steam 与库目录(接口)
    Steam/WindowsSteamLocator.cs     # 注册表实现(仅 Windows 运行)
    Steam/ConfigSteamLocator.cs      # 用配置指定路径(测试/无注册表)
    Steam/SteamLibrary.cs            # 库目录值对象
    Steam/GameScanner.cs             # 扫描 appmanifest + lastplayed
    Steam/InstalledGame.cs           # 游戏值对象
    Engine/IArchiveEngine.cs         # 压缩/解压/校验/虚拟清单 接口
    Engine/ArchiveProgress.cs        # 进度回调载体
    Engine/SevenZipEngine.cs         # 7-Zip LZMA2 solid 实现
    Engine/PrecompDecorator.cs       # 可选 precomp 前处理装饰器
    Engine/ExternalTool.cs           # 外部进程调用封装
    Package/FileEntry.cs             # 目录快照条目(名/大小/相对路径)
    Package/DirectoryManifest.cs     # 目录快照(供 Steam 占位/校验)
    Package/GamePackage.cs           # 归档包读写(数据+acf+manifest+头)
    Package/PackageHeader.cs         # 元数据头(appid/原始大小/压后大小/时间)
    Metadata/ArchiveRecord.cs        # 单条归档记录
    Metadata/MetadataStore.cs        # JSON 索引读写
    Tiering/ManualTieringService.cs  # 手动 Compress/Restore 编排 + 安全
    Tiering/TieringResult.cs         # 操作结果(成功/失败原因)
    Config/AppConfig.cs              # 归档目录/等级/引擎路径/srep 开关
    Config/ConfigStore.cs            # 配置读写
src/
  SteamHibernate.App/
    SteamHibernate.App.csproj
    Program.cs
    App.axaml / App.axaml.cs
    ViewModels/MainViewModel.cs
    ViewModels/GameRowViewModel.cs
    ViewModels/SettingsViewModel.cs
    Views/MainWindow.axaml / .cs
    Views/SettingsView.axaml / .cs
tests/
  SteamHibernate.Core.Tests/
    SteamHibernate.Core.Tests.csproj
    Vdf/VdfParserTests.cs
    Steam/GameScannerTests.cs
    Engine/SevenZipEngineTests.cs
    Package/GamePackageTests.cs
    Metadata/MetadataStoreTests.cs
    Tiering/ManualTieringServiceTests.cs
    Config/ConfigStoreTests.cs
    Fakes/FakeArchiveEngine.cs
    TestData/                        # 样本 acf / vdf
```

---

### Task 1: Solution 与项目骨架

**Files:**
- Create: `SteamHibernate.sln`
- Create: `src/SteamHibernate.Core/SteamHibernate.Core.csproj`
- Create: `src/SteamHibernate.App/SteamHibernate.App.csproj`
- Create: `tests/SteamHibernate.Core.Tests/SteamHibernate.Core.Tests.csproj`

- [ ] **Step 1: 创建解决方案与三个项目**

```bash
cd steam-hibernate
dotnet new sln -n SteamHibernate
dotnet new classlib -n SteamHibernate.Core -o src/SteamHibernate.Core -f net8.0
dotnet new xunit -n SteamHibernate.Core.Tests -o tests/SteamHibernate.Core.Tests -f net8.0
dotnet new install Avalonia.Templates 2>/dev/null || true
dotnet new avalonia.app -n SteamHibernate.App -o src/SteamHibernate.App -f net8.0 2>/dev/null \
  || dotnet new console -n SteamHibernate.App -o src/SteamHibernate.App -f net8.0
dotnet sln add src/SteamHibernate.Core src/SteamHibernate.App tests/SteamHibernate.Core.Tests
dotnet add tests/SteamHibernate.Core.Tests reference src/SteamHibernate.Core
dotnet add src/SteamHibernate.App reference src/SteamHibernate.Core
```

- [ ] **Step 2: 删掉模板默认占位类**

```bash
rm -f src/SteamHibernate.Core/Class1.cs tests/SteamHibernate.Core.Tests/UnitTest1.cs
```

- [ ] **Step 3: 构建验证空骨架**

Run: `dotnet build`
Expected: Build succeeded(0 Error)。Avalonia 模板未装则 App 为 console,后续 Task 9 再换。

- [ ] **Step 4: Commit**

```bash
git add -A
git commit -m "chore: scaffold solution (Core/App/Tests)"
```

---

### Task 2: VdfParser(Valve KeyValues 解析)

**Files:**
- Create: `src/SteamHibernate.Core/Vdf/VdfNode.cs`
- Create: `src/SteamHibernate.Core/Vdf/VdfParser.cs`
- Test: `tests/SteamHibernate.Core.Tests/Vdf/VdfParserTests.cs`

- [ ] **Step 1: 写失败测试**

```csharp
// tests/.../Vdf/VdfParserTests.cs
using SteamHibernate.Core.Vdf;
using Xunit;

public class VdfParserTests
{
    [Fact]
    public void Parses_nested_keyvalues_with_quoted_strings()
    {
        var text = """
        "AppState"
        {
            "appid"     "1091500"
            "name"      "Cyberpunk 2077"
            "UserConfig"
            {
                "language"  "schinese"
            }
        }
        """;

        var root = VdfParser.Parse(text);

        Assert.Equal("1091500", root["AppState"]["appid"].Value);
        Assert.Equal("Cyberpunk 2077", root["AppState"]["name"].Value);
        Assert.Equal("schinese", root["AppState"]["UserConfig"]["language"].Value);
    }

    [Fact]
    public void Missing_key_returns_empty_node_not_throw()
    {
        var root = VdfParser.Parse("\"A\" { \"b\" \"1\" }");
        Assert.Null(root["A"]["zzz"].Value);
        Assert.True(root["A"]["zzz"].IsEmpty);
    }
}
```

- [ ] **Step 2: 跑测试确认失败**

Run: `dotnet test tests/SteamHibernate.Core.Tests --filter VdfParserTests`
Expected: FAIL(VdfParser/VdfNode 不存在,编译错误)。

- [ ] **Step 3: 实现 VdfNode**

```csharp
// src/SteamHibernate.Core/Vdf/VdfNode.cs
namespace SteamHibernate.Core.Vdf;

public sealed class VdfNode
{
    public static readonly VdfNode Empty = new();

    private readonly Dictionary<string, VdfNode> _children =
        new(StringComparer.OrdinalIgnoreCase);

    public string? Value { get; init; }
    public bool IsEmpty => Value is null && _children.Count == 0;

    public IReadOnlyDictionary<string, VdfNode> Children => _children;

    public VdfNode this[string key] =>
        _children.TryGetValue(key, out var n) ? n : Empty;

    internal void Add(string key, VdfNode node) => _children[key] = node;
}
```

- [ ] **Step 4: 实现 VdfParser**

```csharp
// src/SteamHibernate.Core/Vdf/VdfParser.cs
using System.Text;

namespace SteamHibernate.Core.Vdf;

public static class VdfParser
{
    public static VdfNode Parse(string text)
    {
        int pos = 0;
        var root = new VdfNode();
        ParseBody(text, ref pos, root);
        return root;
    }

    private static void ParseBody(string s, ref int pos, VdfNode parent)
    {
        while (true)
        {
            SkipWhitespace(s, ref pos);
            if (pos >= s.Length || s[pos] == '}') return;

            string key = ReadToken(s, ref pos);
            SkipWhitespace(s, ref pos);
            if (pos >= s.Length) return;

            if (s[pos] == '{')
            {
                pos++; // consume {
                var child = new VdfNode();
                ParseBody(s, ref pos, child);
                if (pos < s.Length && s[pos] == '}') pos++; // consume }
                parent.Add(key, child);
            }
            else
            {
                string value = ReadToken(s, ref pos);
                parent.Add(key, new VdfNode { Value = value });
            }
        }
    }

    private static void SkipWhitespace(string s, ref int pos)
    {
        while (pos < s.Length && char.IsWhiteSpace(s[pos])) pos++;
    }

    private static string ReadToken(string s, ref int pos)
    {
        SkipWhitespace(s, ref pos);
        if (pos >= s.Length) return string.Empty;

        if (s[pos] == '"')
        {
            pos++; // opening quote
            var sb = new StringBuilder();
            while (pos < s.Length && s[pos] != '"')
            {
                if (s[pos] == '\\' && pos + 1 < s.Length) pos++; // escape
                sb.Append(s[pos++]);
            }
            if (pos < s.Length) pos++; // closing quote
            return sb.ToString();
        }

        int start = pos;
        while (pos < s.Length && !char.IsWhiteSpace(s[pos]) && s[pos] != '{' && s[pos] != '}')
            pos++;
        return s[start..pos];
    }
}
```

- [ ] **Step 5: 跑测试确认通过**

Run: `dotnet test tests/SteamHibernate.Core.Tests --filter VdfParserTests`
Expected: PASS(2 passed)。

- [ ] **Step 6: Commit**

```bash
git add -A && git commit -m "feat(vdf): KeyValues parser with nested + quoted tokens"
```

---

### Task 3: Steam 定位抽象与值对象

**Files:**
- Create: `src/SteamHibernate.Core/Steam/SteamLibrary.cs`
- Create: `src/SteamHibernate.Core/Steam/ISteamLocator.cs`
- Create: `src/SteamHibernate.Core/Steam/ConfigSteamLocator.cs`
- Create: `src/SteamHibernate.Core/Steam/WindowsSteamLocator.cs`

> 说明:`WindowsSteamLocator` 用注册表,仅在 Windows 实机运行;Core 测试一律用 `ConfigSteamLocator`,故本任务无单测,正确性由 Task 4 的 GameScanner 测试覆盖。

- [ ] **Step 1: 实现 SteamLibrary 值对象**

```csharp
// src/SteamHibernate.Core/Steam/SteamLibrary.cs
namespace SteamHibernate.Core.Steam;

public sealed record SteamLibrary(string RootPath)
{
    public string SteamAppsPath => Path.Combine(RootPath, "steamapps");
    public string CommonPath => Path.Combine(SteamAppsPath, "common");
    public string AppManifestPath(string appId) =>
        Path.Combine(SteamAppsPath, $"appmanifest_{appId}.acf");
}
```

- [ ] **Step 2: 实现接口**

```csharp
// src/SteamHibernate.Core/Steam/ISteamLocator.cs
namespace SteamHibernate.Core.Steam;

public interface ISteamLocator
{
    string SteamRoot { get; }
    IReadOnlyList<SteamLibrary> GetLibraries();
    IReadOnlyList<string> GetUserConfigPaths(); // localconfig.vdf 路径集合
}
```

- [ ] **Step 3: 实现 ConfigSteamLocator(解析 libraryfolders.vdf)**

```csharp
// src/SteamHibernate.Core/Steam/ConfigSteamLocator.cs
using SteamHibernate.Core.Vdf;

namespace SteamHibernate.Core.Steam;

public sealed class ConfigSteamLocator : ISteamLocator
{
    public string SteamRoot { get; }

    public ConfigSteamLocator(string steamRoot) => SteamRoot = steamRoot;

    public IReadOnlyList<SteamLibrary> GetLibraries()
    {
        var result = new List<SteamLibrary> { new(SteamRoot) };
        var file = Path.Combine(SteamRoot, "steamapps", "libraryfolders.vdf");
        if (!File.Exists(file)) return result;

        var root = VdfParser.Parse(File.ReadAllText(file));
        var folders = root["libraryfolders"];
        foreach (var (_, node) in folders.Children)
        {
            var path = node["path"].Value;
            if (!string.IsNullOrWhiteSpace(path) &&
                !result.Any(l => string.Equals(l.RootPath, path, StringComparison.OrdinalIgnoreCase)))
            {
                result.Add(new SteamLibrary(path));
            }
        }
        return result;
    }

    public IReadOnlyList<string> GetUserConfigPaths()
    {
        var userdata = Path.Combine(SteamRoot, "userdata");
        if (!Directory.Exists(userdata)) return Array.Empty<string>();
        return Directory.GetDirectories(userdata)
            .Select(d => Path.Combine(d, "config", "localconfig.vdf"))
            .Where(File.Exists)
            .ToList();
    }
}
```

- [ ] **Step 4: 实现 WindowsSteamLocator(注册表 + 委托 ConfigSteamLocator)**

```csharp
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
```

- [ ] **Step 5: 给 Core 加 Windows 注册表包引用**

```bash
dotnet add src/SteamHibernate.Core package Microsoft.Win32.Registry
dotnet build
```
Expected: Build succeeded。

- [ ] **Step 6: Commit**

```bash
git add -A && git commit -m "feat(steam): locator abstraction + libraryfolders parsing"
```

---

### Task 4: GameScanner(扫描已安装游戏 + 最后游玩时间)

**Files:**
- Create: `src/SteamHibernate.Core/Steam/InstalledGame.cs`
- Create: `src/SteamHibernate.Core/Steam/GameScanner.cs`
- Test: `tests/SteamHibernate.Core.Tests/Steam/GameScannerTests.cs`
- TestData: `tests/SteamHibernate.Core.Tests/TestData/` (运行时写入)

- [ ] **Step 1: 写失败测试(用临时目录搭一个假 Steam 库)**

```csharp
// tests/.../Steam/GameScannerTests.cs
using SteamHibernate.Core.Steam;
using Xunit;

public class GameScannerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "shtest_" + Guid.NewGuid().ToString("N"));

    public GameScannerTests()
    {
        var apps = Path.Combine(_root, "steamapps");
        Directory.CreateDirectory(Path.Combine(apps, "common", "Half-Life"));
        File.WriteAllText(Path.Combine(apps, "appmanifest_70.acf"), """
        "AppState"
        {
            "appid"      "70"
            "name"       "Half-Life"
            "installdir" "Half-Life"
            "SizeOnDisk" "4194304"
        }
        """);
        var userCfg = Path.Combine(_root, "userdata", "111", "config");
        Directory.CreateDirectory(userCfg);
        File.WriteAllText(Path.Combine(userCfg, "localconfig.vdf"), """
        "UserLocalConfigStore"
        {
            "Software" { "Valve" { "Steam" { "apps"
            {
                "70" { "LastPlayed" "1700000000" }
            } } } }
        }
        """);
    }

    [Fact]
    public void Scans_installed_game_with_size_and_lastplayed()
    {
        var scanner = new GameScanner(new ConfigSteamLocator(_root));
        var games = scanner.Scan();

        var g = Assert.Single(games);
        Assert.Equal("70", g.AppId);
        Assert.Equal("Half-Life", g.Name);
        Assert.Equal(4194304, g.SizeOnDisk);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1700000000), g.LastPlayed);
        Assert.True(Directory.Exists(g.InstallDir));
    }

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
```

- [ ] **Step 2: 跑测试确认失败**

Run: `dotnet test tests/SteamHibernate.Core.Tests --filter GameScannerTests`
Expected: FAIL(GameScanner/InstalledGame 不存在)。

- [ ] **Step 3: 实现 InstalledGame**

```csharp
// src/SteamHibernate.Core/Steam/InstalledGame.cs
namespace SteamHibernate.Core.Steam;

public sealed record InstalledGame(
    string AppId,
    string Name,
    string InstallDir,
    long SizeOnDisk,
    DateTimeOffset? LastPlayed,
    SteamLibrary Library);
```

- [ ] **Step 4: 实现 GameScanner**

```csharp
// src/SteamHibernate.Core/Steam/GameScanner.cs
using SteamHibernate.Core.Vdf;

namespace SteamHibernate.Core.Steam;

public sealed class GameScanner
{
    private readonly ISteamLocator _locator;
    public GameScanner(ISteamLocator locator) => _locator = locator;

    public IReadOnlyList<InstalledGame> Scan()
    {
        var lastPlayed = ReadLastPlayed();
        var games = new List<InstalledGame>();

        foreach (var lib in _locator.GetLibraries())
        {
            if (!Directory.Exists(lib.SteamAppsPath)) continue;
            foreach (var acf in Directory.GetFiles(lib.SteamAppsPath, "appmanifest_*.acf"))
            {
                var state = VdfParser.Parse(File.ReadAllText(acf))["AppState"];
                var appId = state["appid"].Value;
                var installDir = state["installdir"].Value;
                if (string.IsNullOrEmpty(appId) || string.IsNullOrEmpty(installDir)) continue;

                long.TryParse(state["SizeOnDisk"].Value, out var size);
                games.Add(new InstalledGame(
                    AppId: appId,
                    Name: state["name"].Value ?? installDir,
                    InstallDir: Path.Combine(lib.CommonPath, installDir),
                    SizeOnDisk: size,
                    LastPlayed: lastPlayed.TryGetValue(appId, out var lp) ? lp : null,
                    Library: lib));
            }
        }
        return games;
    }

    private Dictionary<string, DateTimeOffset> ReadLastPlayed()
    {
        var result = new Dictionary<string, DateTimeOffset>();
        foreach (var cfg in _locator.GetUserConfigPaths())
        {
            var apps = VdfParser.Parse(File.ReadAllText(cfg))
                ["UserLocalConfigStore"]["Software"]["Valve"]["Steam"]["apps"];
            foreach (var (appId, node) in apps.Children)
            {
                if (long.TryParse(node["LastPlayed"].Value, out var unix))
                {
                    var ts = DateTimeOffset.FromUnixTimeSeconds(unix);
                    if (!result.TryGetValue(appId, out var existing) || ts > existing)
                        result[appId] = ts;
                }
            }
        }
        return result;
    }
}
```

- [ ] **Step 5: 跑测试确认通过**

Run: `dotnet test tests/SteamHibernate.Core.Tests --filter GameScannerTests`
Expected: PASS。

- [ ] **Step 6: Commit**

```bash
git add -A && git commit -m "feat(steam): scan installed games + lastplayed from localconfig"
```

---

### Task 5: 目录快照(DirectoryManifest)

**Files:**
- Create: `src/SteamHibernate.Core/Package/FileEntry.cs`
- Create: `src/SteamHibernate.Core/Package/DirectoryManifest.cs`
- Test: `tests/SteamHibernate.Core.Tests/Package/GamePackageTests.cs`(本任务先加 manifest 部分)

- [ ] **Step 1: 写失败测试**

```csharp
// tests/.../Package/GamePackageTests.cs
using SteamHibernate.Core.Package;
using Xunit;

public class GamePackageTests : IDisposable
{
    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "shpkg_" + Guid.NewGuid().ToString("N"));
    public GamePackageTests() => Directory.CreateDirectory(_tmp);
    public void Dispose() { if (Directory.Exists(_tmp)) Directory.Delete(_tmp, true); }

    [Fact]
    public void Manifest_captures_relative_paths_and_sizes()
    {
        var game = Path.Combine(_tmp, "game");
        Directory.CreateDirectory(Path.Combine(game, "bin"));
        File.WriteAllText(Path.Combine(game, "bin", "a.txt"), "hello");

        var manifest = DirectoryManifest.Capture(game);

        var entry = Assert.Single(manifest.Files);
        Assert.Equal(Path.Combine("bin", "a.txt"), entry.RelativePath);
        Assert.Equal(5, entry.Size);
    }
}
```

- [ ] **Step 2: 跑测试确认失败**

Run: `dotnet test tests/SteamHibernate.Core.Tests --filter "GamePackageTests.Manifest"`
Expected: FAIL(类型不存在)。

- [ ] **Step 3: 实现 FileEntry / DirectoryManifest**

```csharp
// src/SteamHibernate.Core/Package/FileEntry.cs
namespace SteamHibernate.Core.Package;

public sealed record FileEntry(string RelativePath, long Size);
```

```csharp
// src/SteamHibernate.Core/Package/DirectoryManifest.cs
namespace SteamHibernate.Core.Package;

public sealed class DirectoryManifest
{
    public required List<FileEntry> Files { get; init; }
    public long TotalSize => Files.Sum(f => f.Size);

    public static DirectoryManifest Capture(string root)
    {
        var files = new List<FileEntry>();
        foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(root, path);
            files.Add(new FileEntry(rel, new FileInfo(path).Length));
        }
        return new DirectoryManifest { Files = files };
    }
}
```

- [ ] **Step 4: 跑测试确认通过**

Run: `dotnet test tests/SteamHibernate.Core.Tests --filter "GamePackageTests.Manifest"`
Expected: PASS。

- [ ] **Step 5: Commit**

```bash
git add -A && git commit -m "feat(package): directory manifest capture"
```

---

### Task 6: 引擎接口 + 7-Zip 实现

**Files:**
- Create: `src/SteamHibernate.Core/Engine/ArchiveProgress.cs`
- Create: `src/SteamHibernate.Core/Engine/IArchiveEngine.cs`
- Create: `src/SteamHibernate.Core/Engine/ExternalTool.cs`
- Create: `src/SteamHibernate.Core/Engine/SevenZipEngine.cs`
- Test: `tests/SteamHibernate.Core.Tests/Engine/SevenZipEngineTests.cs`

> 集成测试需要本机存在 `7z`/`7zz`/`7za`。容器内安装:`sudo apt-get install -y p7zip-full` 或下载 7-Zip-linux。测试在找不到时 `Skip`。

- [ ] **Step 1: 写失败测试(真实 round-trip,缺二进制则跳过)**

```csharp
// tests/.../Engine/SevenZipEngineTests.cs
using SteamHibernate.Core.Engine;
using Xunit;

public class SevenZipEngineTests : IDisposable
{
    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "sh7z_" + Guid.NewGuid().ToString("N"));
    public SevenZipEngineTests() => Directory.CreateDirectory(_tmp);
    public void Dispose() { if (Directory.Exists(_tmp)) Directory.Delete(_tmp, true); }

    [SkippableFact]
    public void Compress_then_extract_roundtrips_bytes()
    {
        var exe = SevenZipEngine.FindBinary();
        Skip.If(exe is null, "7-Zip binary not found");

        var src = Path.Combine(_tmp, "src");
        Directory.CreateDirectory(Path.Combine(src, "sub"));
        File.WriteAllText(Path.Combine(src, "a.bin"), new string('x', 10000));
        File.WriteAllText(Path.Combine(src, "sub", "b.bin"), "content-b");

        var engine = new SevenZipEngine(exe!);
        var archive = Path.Combine(_tmp, "out.7z");
        engine.Compress(src, archive, level: 9, _ => { });

        Assert.True(File.Exists(archive));
        Assert.True(engine.VerifyIntegrity(archive));

        var dst = Path.Combine(_tmp, "dst");
        engine.Extract(archive, dst, _ => { });

        Assert.Equal(new string('x', 10000), File.ReadAllText(Path.Combine(dst, "a.bin")));
        Assert.Equal("content-b", File.ReadAllText(Path.Combine(dst, "sub", "b.bin")));
    }
}
```

- [ ] **Step 2: 加 Skip 支持包并跑测试确认失败**

```bash
dotnet add tests/SteamHibernate.Core.Tests package Xunit.SkippableFact
dotnet test tests/SteamHibernate.Core.Tests --filter SevenZipEngineTests
```
Expected: FAIL(SevenZipEngine 不存在;编译错误)。

- [ ] **Step 3: 实现 ArchiveProgress 与接口**

```csharp
// src/SteamHibernate.Core/Engine/ArchiveProgress.cs
namespace SteamHibernate.Core.Engine;

public sealed record ArchiveProgress(string Stage, double Fraction);
```

```csharp
// src/SteamHibernate.Core/Engine/IArchiveEngine.cs
namespace SteamHibernate.Core.Engine;

public interface IArchiveEngine
{
    string ArchiveExtension { get; }
    void Compress(string srcDir, string archivePath, int level, Action<ArchiveProgress> progress);
    void Extract(string archivePath, string dstDir, Action<ArchiveProgress> progress);
    bool VerifyIntegrity(string archivePath);
}
```

- [ ] **Step 4: 实现 ExternalTool(进程封装)**

```csharp
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
        p.OutputDataReceived += (_, e) => { if (e.Data != null) onLine?.Invoke(e.Data); };
        p.ErrorDataReceived += (_, e) => { if (e.Data != null) onLine?.Invoke(e.Data); };
        p.Start();
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();
        p.WaitForExit();
        return p.ExitCode;
    }
}
```

- [ ] **Step 5: 实现 SevenZipEngine**

```csharp
// src/SteamHibernate.Core/Engine/SevenZipEngine.cs
namespace SteamHibernate.Core.Engine;

public sealed class SevenZipEngine : IArchiveEngine
{
    private readonly string _exe;
    public SevenZipEngine(string sevenZipExePath) => _exe = sevenZipExePath;

    public string ArchiveExtension => ".7z";

    public static string? FindBinary()
    {
        foreach (var name in new[] { "7zz", "7z", "7za", "7z.exe", "7za.exe" })
        {
            var path = ResolveOnPath(name);
            if (path != null) return path;
        }
        var win = @"C:\Program Files\7-Zip\7z.exe";
        return File.Exists(win) ? win : null;
    }

    private static string? ResolveOnPath(string name)
    {
        var dirs = (Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
        foreach (var d in dirs)
        {
            var full = Path.Combine(d, name);
            if (File.Exists(full)) return full;
        }
        return null;
    }

    public void Compress(string srcDir, string archivePath, int level, Action<ArchiveProgress> progress)
    {
        progress(new ArchiveProgress("Compressing", 0));
        // -t7z LZMA2 solid; -mx level; -bsp1 输出进度到 stdout
        var code = ExternalTool.Run(_exe, new[]
        {
            "a", "-t7z", $"-mx={level}", "-m0=lzma2", "-ms=on", "-bsp1", "-y",
            archivePath, Path.Combine(srcDir, "*")
        }, line => TryReportPercent(line, "Compressing", progress));
        if (code != 0) throw new IOException($"7z compress failed (exit {code}).");
        progress(new ArchiveProgress("Compressing", 1));
    }

    public void Extract(string archivePath, string dstDir, Action<ArchiveProgress> progress)
    {
        Directory.CreateDirectory(dstDir);
        progress(new ArchiveProgress("Extracting", 0));
        var code = ExternalTool.Run(_exe, new[]
        {
            "x", "-bsp1", "-y", $"-o{dstDir}", archivePath
        }, line => TryReportPercent(line, "Extracting", progress));
        if (code != 0) throw new IOException($"7z extract failed (exit {code}).");
        progress(new ArchiveProgress("Extracting", 1));
    }

    public bool VerifyIntegrity(string archivePath)
        => ExternalTool.Run(_exe, new[] { "t", "-y", archivePath }) == 0;

    private static void TryReportPercent(string line, string stage, Action<ArchiveProgress> progress)
    {
        // 7z -bsp1 行形如 " 42% ..."
        var idx = line.IndexOf('%');
        if (idx <= 0) return;
        int start = idx - 1;
        while (start >= 0 && char.IsDigit(line[start])) start--;
        if (int.TryParse(line.AsSpan(start + 1, idx - start - 1), out var pct))
            progress(new ArchiveProgress(stage, Math.Clamp(pct / 100.0, 0, 1)));
    }
}
```

- [ ] **Step 6: 跑测试确认通过(或在缺二进制时跳过)**

```bash
sudo apt-get install -y p7zip-full || true
dotnet test tests/SteamHibernate.Core.Tests --filter SevenZipEngineTests
```
Expected: PASS(或 Skipped: "7-Zip binary not found")。

- [ ] **Step 7: Commit**

```bash
git add -A && git commit -m "feat(engine): IArchiveEngine + 7-Zip LZMA2 solid implementation"
```

---

### Task 7: 归档包格式(GamePackage)

**Files:**
- Create: `src/SteamHibernate.Core/Package/PackageHeader.cs`
- Create: `src/SteamHibernate.Core/Package/GamePackage.cs`
- Test: `tests/SteamHibernate.Core.Tests/Package/GamePackageTests.cs`(追加)

> 归档包 = 一个目录:`data{engine.ext}`(游戏数据压缩档)+ `appmanifest_<appid>.acf`(原样副本)+ `manifest.json`(目录快照)+ `header.json`(元数据头)。这样 Steam 还原所需三要素齐全。

- [ ] **Step 1: 追加失败测试**

```csharp
// 追加到 tests/.../Package/GamePackageTests.cs
using SteamHibernate.Core.Engine;
using SteamHibernate.Core.Package;

public partial class GamePackageRoundTrip
{
}

// 在 GamePackageTests 类内追加:
    [SkippableFact]
    public void Pack_then_unpack_restores_game_and_acf()
    {
        var exe = SevenZipEngine.FindBinary();
        Skip.If(exe is null, "7-Zip binary not found");
        var engine = new SevenZipEngine(exe!);

        var game = Path.Combine(_tmp, "common", "MyGame");
        Directory.CreateDirectory(game);
        File.WriteAllText(Path.Combine(game, "game.dat"), "payload");
        var acf = Path.Combine(_tmp, "appmanifest_999.acf");
        File.WriteAllText(acf, "\"AppState\" { \"appid\" \"999\" }");

        var pkgDir = Path.Combine(_tmp, "pkg");
        GamePackage.Pack(engine, appId: "999", gameDir: game, acfPath: acf,
            packageDir: pkgDir, level: 5, _ => { });

        var header = GamePackage.ReadHeader(pkgDir);
        Assert.Equal("999", header.AppId);
        Assert.True(header.CompressedSize > 0);

        var restoreGame = Path.Combine(_tmp, "restored", "MyGame");
        var restoreAcf = Path.Combine(_tmp, "restored", "appmanifest_999.acf");
        GamePackage.Unpack(engine, pkgDir, restoreGame, restoreAcf, _ => { });

        Assert.Equal("payload", File.ReadAllText(Path.Combine(restoreGame, "game.dat")));
        Assert.True(File.Exists(restoreAcf));
    }
```

- [ ] **Step 2: 跑测试确认失败**

Run: `dotnet test tests/SteamHibernate.Core.Tests --filter "GamePackageTests.Pack"`
Expected: FAIL(GamePackage/PackageHeader 不存在)。

- [ ] **Step 3: 实现 PackageHeader**

```csharp
// src/SteamHibernate.Core/Package/PackageHeader.cs
namespace SteamHibernate.Core.Package;

public sealed record PackageHeader(
    string AppId,
    string GameName,
    string InstallDirName,
    long OriginalSize,
    long CompressedSize,
    string EngineExtension,
    DateTimeOffset CreatedUtc);
```

- [ ] **Step 4: 实现 GamePackage**

```csharp
// src/SteamHibernate.Core/Package/GamePackage.cs
using System.Text.Json;
using SteamHibernate.Core.Engine;

namespace SteamHibernate.Core.Package;

public static class GamePackage
{
    private const string ManifestFile = "manifest.json";
    private const string HeaderFile = "header.json";
    private static string DataFile(IArchiveEngine e) => "data" + e.ArchiveExtension;

    public static PackageHeader Pack(
        IArchiveEngine engine, string appId, string gameDir, string acfPath,
        string packageDir, int level, Action<ArchiveProgress> progress)
    {
        Directory.CreateDirectory(packageDir);

        var manifest = DirectoryManifest.Capture(gameDir);
        File.WriteAllText(Path.Combine(packageDir, ManifestFile),
            JsonSerializer.Serialize(manifest));

        if (File.Exists(acfPath))
            File.Copy(acfPath, Path.Combine(packageDir, Path.GetFileName(acfPath)), overwrite: true);

        var dataPath = Path.Combine(packageDir, DataFile(engine));
        engine.Compress(gameDir, dataPath, level, progress);

        var header = new PackageHeader(
            AppId: appId,
            GameName: Path.GetFileName(gameDir),
            InstallDirName: Path.GetFileName(gameDir),
            OriginalSize: manifest.TotalSize,
            CompressedSize: new FileInfo(dataPath).Length,
            EngineExtension: engine.ArchiveExtension,
            CreatedUtc: DateTimeOffset.UtcNow);
        File.WriteAllText(Path.Combine(packageDir, HeaderFile), JsonSerializer.Serialize(header));
        return header;
    }

    public static PackageHeader ReadHeader(string packageDir) =>
        JsonSerializer.Deserialize<PackageHeader>(
            File.ReadAllText(Path.Combine(packageDir, HeaderFile)))
        ?? throw new InvalidDataException("Invalid package header.");

    public static void Unpack(
        IArchiveEngine engine, string packageDir, string gameDir, string acfPath,
        Action<ArchiveProgress> progress)
    {
        var dataPath = Path.Combine(packageDir, DataFile(engine));
        if (!engine.VerifyIntegrity(dataPath))
            throw new InvalidDataException("Package data failed integrity check.");

        engine.Extract(dataPath, gameDir, progress);

        var acfInPkg = Directory.GetFiles(packageDir, "appmanifest_*.acf").FirstOrDefault();
        if (acfInPkg != null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(acfPath)!);
            File.Copy(acfInPkg, acfPath, overwrite: true);
        }
    }
}
```

- [ ] **Step 5: 跑测试确认通过**

Run: `dotnet test tests/SteamHibernate.Core.Tests --filter "GamePackageTests.Pack"`
Expected: PASS(或 Skipped)。

- [ ] **Step 6: Commit**

```bash
git add -A && git commit -m "feat(package): GamePackage pack/unpack with acf + manifest + header"
```

---

### Task 8: MetadataStore(归档索引)

**Files:**
- Create: `src/SteamHibernate.Core/Metadata/ArchiveRecord.cs`
- Create: `src/SteamHibernate.Core/Metadata/MetadataStore.cs`
- Test: `tests/SteamHibernate.Core.Tests/Metadata/MetadataStoreTests.cs`

- [ ] **Step 1: 写失败测试**

```csharp
// tests/.../Metadata/MetadataStoreTests.cs
using SteamHibernate.Core.Metadata;
using Xunit;

public class MetadataStoreTests : IDisposable
{
    private readonly string _file = Path.Combine(Path.GetTempPath(), "shmeta_" + Guid.NewGuid().ToString("N") + ".json");
    public void Dispose() { if (File.Exists(_file)) File.Delete(_file); }

    [Fact]
    public void Upsert_and_reload_persists_records()
    {
        var store = new MetadataStore(_file);
        store.Upsert(new ArchiveRecord("70", "Half-Life", "/pkg/70",
            OriginalSize: 4_000_000, CompressedSize: 1_000_000, DateTimeOffset.UnixEpoch));
        store.Save();

        var reloaded = new MetadataStore(_file);
        var rec = reloaded.Get("70");
        Assert.NotNull(rec);
        Assert.Equal("Half-Life", rec!.GameName);
        Assert.Equal(1_000_000, rec.CompressedSize);
    }

    [Fact]
    public void Remove_deletes_record()
    {
        var store = new MetadataStore(_file);
        store.Upsert(new ArchiveRecord("1", "G", "/p", 10, 5, DateTimeOffset.UnixEpoch));
        store.Remove("1");
        Assert.Null(store.Get("1"));
    }
}
```

- [ ] **Step 2: 跑测试确认失败**

Run: `dotnet test tests/SteamHibernate.Core.Tests --filter MetadataStoreTests`
Expected: FAIL。

- [ ] **Step 3: 实现 ArchiveRecord**

```csharp
// src/SteamHibernate.Core/Metadata/ArchiveRecord.cs
namespace SteamHibernate.Core.Metadata;

public sealed record ArchiveRecord(
    string AppId,
    string GameName,
    string PackageDir,
    long OriginalSize,
    long CompressedSize,
    DateTimeOffset ArchivedUtc);
```

- [ ] **Step 4: 实现 MetadataStore**

```csharp
// src/SteamHibernate.Core/Metadata/MetadataStore.cs
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
        File.Move(tmp, _path, overwrite: true); // 原子替换
    }
}
```

- [ ] **Step 5: 跑测试确认通过**

Run: `dotnet test tests/SteamHibernate.Core.Tests --filter MetadataStoreTests`
Expected: PASS(2 passed)。

- [ ] **Step 6: Commit**

```bash
git add -A && git commit -m "feat(metadata): JSON archive index with atomic save"
```

---

### Task 9: ManualTieringService(手动 Compress/Restore + 安全)

**Files:**
- Create: `src/SteamHibernate.Core/Tiering/TieringResult.cs`
- Create: `src/SteamHibernate.Core/Tiering/ManualTieringService.cs`
- Create: `tests/SteamHibernate.Core.Tests/Fakes/FakeArchiveEngine.cs`
- Test: `tests/SteamHibernate.Core.Tests/Tiering/ManualTieringServiceTests.cs`

> 安全语义(commit-on-success):Compress = 压到 packageDir → 校验 → **校验通过才删原游戏目录**;失败抛错且原目录不动。Restore = 解到临时目录 → 移动就位 + acf 就位 → 成功后才删 packageDir(可选保留)。

- [ ] **Step 1: 写 FakeArchiveEngine(无需 7z 也能测编排与安全)**

```csharp
// tests/.../Fakes/FakeArchiveEngine.cs
using SteamHibernate.Core.Engine;

public sealed class FakeArchiveEngine : IArchiveEngine
{
    public bool FailVerify { get; set; }
    public string ArchiveExtension => ".fake";

    public void Compress(string srcDir, string archivePath, int level, Action<ArchiveProgress> progress)
    {
        // 把目录序列化成一行行 "relpath\tcontent" 模拟压缩
        var lines = Directory.EnumerateFiles(srcDir, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(srcDir, f) + "\t" + File.ReadAllText(f));
        File.WriteAllLines(archivePath, lines);
        progress(new ArchiveProgress("Compressing", 1));
    }

    public void Extract(string archivePath, string dstDir, Action<ArchiveProgress> progress)
    {
        Directory.CreateDirectory(dstDir);
        foreach (var line in File.ReadAllLines(archivePath))
        {
            var i = line.IndexOf('\t');
            var rel = line[..i];
            var full = Path.Combine(dstDir, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, line[(i + 1)..]);
        }
        progress(new ArchiveProgress("Extracting", 1));
    }

    public bool VerifyIntegrity(string archivePath) => !FailVerify && File.Exists(archivePath);
}
```

- [ ] **Step 2: 写失败测试(成功路径 + 校验失败不删原目录)**

```csharp
// tests/.../Tiering/ManualTieringServiceTests.cs
using SteamHibernate.Core.Metadata;
using SteamHibernate.Core.Steam;
using SteamHibernate.Core.Tiering;
using Xunit;

public class ManualTieringServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "shtier_" + Guid.NewGuid().ToString("N"));
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }

    private (ManualTieringService svc, InstalledGame game, MetadataStore store) Setup()
    {
        var lib = new SteamLibrary(Path.Combine(_root, "lib"));
        Directory.CreateDirectory(lib.CommonPath);
        var gameDir = Path.Combine(lib.CommonPath, "MyGame");
        Directory.CreateDirectory(gameDir);
        File.WriteAllText(Path.Combine(gameDir, "g.dat"), "hello");
        File.WriteAllText(lib.AppManifestPath("999"), "\"AppState\" { \"appid\" \"999\" }");

        var game = new InstalledGame("999", "MyGame", gameDir, 5, null, lib);
        var store = new MetadataStore(Path.Combine(_root, "meta.json"));
        var archiveRoot = Path.Combine(_root, "archives");
        var svc = new ManualTieringService(new FakeArchiveEngine(), store, archiveRoot, level: 5);
        return (svc, game, store);
    }

    [Fact]
    public void Compress_removes_game_dir_and_records_archive()
    {
        var (svc, game, store) = Setup();
        var result = svc.Compress(game, _ => { });

        Assert.True(result.Success);
        Assert.False(Directory.Exists(game.InstallDir)); // 原目录已删
        Assert.NotNull(store.Get("999"));
    }

    [Fact]
    public void Compress_failed_verify_keeps_game_dir()
    {
        var (_, game, store) = Setup();
        var engine = new FakeArchiveEngine { FailVerify = true };
        var svc = new ManualTieringService(engine, store, Path.Combine(_root, "a2"), 5);

        var result = svc.Compress(game, _ => { });

        Assert.False(result.Success);
        Assert.True(Directory.Exists(game.InstallDir)); // 安全:原目录保留
        Assert.Null(store.Get("999"));
    }

    [Fact]
    public void Restore_brings_back_game_and_acf_then_clears_record()
    {
        var (svc, game, store) = Setup();
        Assert.True(svc.Compress(game, _ => { }).Success);

        var result = svc.Restore("999", _ => { });

        Assert.True(result.Success);
        Assert.Equal("hello", File.ReadAllText(Path.Combine(game.InstallDir, "g.dat")));
        Assert.True(File.Exists(game.Library.AppManifestPath("999")));
        Assert.Null(store.Get("999"));
    }
}
```

- [ ] **Step 3: 跑测试确认失败**

Run: `dotnet test tests/SteamHibernate.Core.Tests --filter ManualTieringServiceTests`
Expected: FAIL(类型不存在)。

- [ ] **Step 4: 实现 TieringResult**

```csharp
// src/SteamHibernate.Core/Tiering/TieringResult.cs
namespace SteamHibernate.Core.Tiering;

public sealed record TieringResult(bool Success, string? Error = null)
{
    public static TieringResult Ok() => new(true);
    public static TieringResult Fail(string error) => new(false, error);
}
```

- [ ] **Step 5: 实现 ManualTieringService**

```csharp
// src/SteamHibernate.Core/Tiering/ManualTieringService.cs
using SteamHibernate.Core.Engine;
using SteamHibernate.Core.Metadata;
using SteamHibernate.Core.Package;
using SteamHibernate.Core.Steam;

namespace SteamHibernate.Core.Tiering;

public sealed class ManualTieringService
{
    private readonly IArchiveEngine _engine;
    private readonly MetadataStore _store;
    private readonly string _archiveRoot;
    private readonly int _level;

    // libraries:用于 Restore 时把游戏放回原库。Plan1 用 game 记录的 Library;
    // Restore 时从 store 取不到 Library,故 Compress 时把库根写进 record 的 PackageDir 同级。
    private readonly Func<string, SteamLibrary>? _resolveLibrary;

    public ManualTieringService(IArchiveEngine engine, MetadataStore store,
        string archiveRoot, int level, Func<string, SteamLibrary>? resolveLibrary = null)
    {
        _engine = engine; _store = store; _archiveRoot = archiveRoot;
        _level = level; _resolveLibrary = resolveLibrary;
    }

    public TieringResult Compress(InstalledGame game, Action<ArchiveProgress> progress)
    {
        if (!Directory.Exists(game.InstallDir))
            return TieringResult.Fail("Game directory not found.");

        var pkgDir = Path.Combine(_archiveRoot, game.AppId);
        try
        {
            if (Directory.Exists(pkgDir)) Directory.Delete(pkgDir, true);

            var header = GamePackage.Pack(_engine, game.AppId, game.InstallDir,
                game.Library.AppManifestPath(game.AppId), pkgDir, _level, progress);

            // 校验:确认数据档可完整校验通过,再动原文件(commit-on-success)
            var dataPath = Path.Combine(pkgDir, "data" + _engine.ArchiveExtension);
            if (!_engine.VerifyIntegrity(dataPath))
                throw new InvalidDataException("Integrity check failed after compression.");

            // 也把库根记进一个小文件,Restore 时能定位回去
            File.WriteAllText(Path.Combine(pkgDir, "library.txt"), game.Library.RootPath);

            Directory.Delete(game.InstallDir, true); // 仅在校验通过后删除
            _store.Upsert(new ArchiveRecord(game.AppId, game.Name, pkgDir,
                header.OriginalSize, header.CompressedSize, header.CreatedUtc));
            return TieringResult.Ok();
        }
        catch (Exception ex)
        {
            if (Directory.Exists(pkgDir)) Directory.Delete(pkgDir, true); // 清理半成品
            return TieringResult.Fail(ex.Message);
        }
    }

    public TieringResult Restore(string appId, Action<ArchiveProgress> progress)
    {
        var rec = _store.Get(appId);
        if (rec is null) return TieringResult.Fail("No archive record for app " + appId);

        var libraryRoot = File.ReadAllText(Path.Combine(rec.PackageDir, "library.txt")).Trim();
        var lib = _resolveLibrary?.Invoke(appId) ?? new SteamLibrary(libraryRoot);
        var header = GamePackage.ReadHeader(rec.PackageDir);

        var finalGameDir = Path.Combine(lib.CommonPath, header.InstallDirName);
        var tmpDir = finalGameDir + ".restoring";
        try
        {
            if (Directory.Exists(tmpDir)) Directory.Delete(tmpDir, true);
            GamePackage.Unpack(_engine, rec.PackageDir, tmpDir,
                lib.AppManifestPath(appId), progress);

            if (Directory.Exists(finalGameDir)) Directory.Delete(finalGameDir, true);
            Directory.Move(tmpDir, finalGameDir); // 原子就位
            _store.Remove(appId);
            return TieringResult.Ok();
        }
        catch (Exception ex)
        {
            if (Directory.Exists(tmpDir)) Directory.Delete(tmpDir, true);
            return TieringResult.Fail(ex.Message);
        }
    }
}
```

- [ ] **Step 6: 跑测试确认通过**

Run: `dotnet test tests/SteamHibernate.Core.Tests --filter ManualTieringServiceTests`
Expected: PASS(3 passed)。

- [ ] **Step 7: Commit**

```bash
git add -A && git commit -m "feat(tiering): manual compress/restore with commit-on-success safety"
```

---

### Task 10: 配置(AppConfig / ConfigStore)

**Files:**
- Create: `src/SteamHibernate.Core/Config/AppConfig.cs`
- Create: `src/SteamHibernate.Core/Config/ConfigStore.cs`
- Test: `tests/SteamHibernate.Core.Tests/Config/ConfigStoreTests.cs`

- [ ] **Step 1: 写失败测试**

```csharp
// tests/.../Config/ConfigStoreTests.cs
using SteamHibernate.Core.Config;
using Xunit;

public class ConfigStoreTests : IDisposable
{
    private readonly string _file = Path.Combine(Path.GetTempPath(), "shcfg_" + Guid.NewGuid().ToString("N") + ".json");
    public void Dispose() { if (File.Exists(_file)) File.Delete(_file); }

    [Fact]
    public void Load_returns_defaults_when_missing_then_saves_roundtrip()
    {
        var store = new ConfigStore(_file);
        var cfg = store.Load();
        Assert.Equal(9, cfg.CompressionLevel); // 默认最高

        cfg = cfg with { ArchiveRoot = "/data/archives", CompressionLevel = 5 };
        store.Save(cfg);

        var reloaded = new ConfigStore(_file).Load();
        Assert.Equal("/data/archives", reloaded.ArchiveRoot);
        Assert.Equal(5, reloaded.CompressionLevel);
    }
}
```

- [ ] **Step 2: 跑测试确认失败**

Run: `dotnet test tests/SteamHibernate.Core.Tests --filter ConfigStoreTests`
Expected: FAIL。

- [ ] **Step 3: 实现 AppConfig**

```csharp
// src/SteamHibernate.Core/Config/AppConfig.cs
namespace SteamHibernate.Core.Config;

public sealed record AppConfig
{
    public string ArchiveRoot { get; init; } = "";
    public int CompressionLevel { get; init; } = 9;
    public string? SevenZipPath { get; init; }
    public bool EnableSrep { get; init; } = false;
    public int IdleDays { get; init; } = 30; // 供 Plan2 自动模式使用
    public string DefaultMode { get; init; } = "Manual"; // Manual | Auto
}
```

- [ ] **Step 4: 实现 ConfigStore**

```csharp
// src/SteamHibernate.Core/Config/ConfigStore.cs
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
```

- [ ] **Step 5: 跑测试确认通过**

Run: `dotnet test tests/SteamHibernate.Core.Tests --filter ConfigStoreTests`
Expected: PASS。

- [ ] **Step 6: 全量测试 + Commit**

```bash
dotnet test
git add -A && git commit -m "feat(config): AppConfig + ConfigStore with defaults"
```

---

### Task 11: GUI — ViewModels(可单测的展示逻辑)

**Files:**
- 确保 App 为 Avalonia 项目(Task 1 若回退为 console,此处重建):
  - Modify/Create: `src/SteamHibernate.App/*`
- Create: `src/SteamHibernate.App/ViewModels/GameRowViewModel.cs`
- Create: `src/SteamHibernate.App/ViewModels/MainViewModel.cs`
- Test: 新建 `tests/SteamHibernate.App.Tests/` 或并入 Core.Tests 引用 App

> ViewModel 不直接 new 服务,通过构造注入,便于测试。GUI 视图层(axaml)正确性靠 Task 12 手动冒烟。

- [ ] **Step 1: 确保 Avalonia + MVVM 依赖**

```bash
# 若 Task1 回退成 console,先转 Avalonia:
dotnet new install Avalonia.Templates
rm -rf src/SteamHibernate.App && dotnet new avalonia.mvvm -n SteamHibernate.App -o src/SteamHibernate.App -f net8.0
dotnet sln add src/SteamHibernate.App
dotnet add src/SteamHibernate.App reference src/SteamHibernate.Core
dotnet add src/SteamHibernate.App package CommunityToolkit.Mvvm
dotnet build
```
Expected: Build succeeded。

- [ ] **Step 2: 建 App 测试项目并写失败测试**

```bash
dotnet new xunit -n SteamHibernate.App.Tests -o tests/SteamHibernate.App.Tests -f net8.0
dotnet sln add tests/SteamHibernate.App.Tests
dotnet add tests/SteamHibernate.App.Tests reference src/SteamHibernate.App src/SteamHibernate.Core
```

```csharp
// tests/SteamHibernate.App.Tests/MainViewModelTests.cs
using SteamHibernate.App.ViewModels;
using SteamHibernate.Core.Steam;
using Xunit;

public class MainViewModelTests
{
    [Fact]
    public void Rows_reflect_installed_and_archived_state()
    {
        var lib = new SteamLibrary("/root");
        var installed = new List<InstalledGame>
        {
            new("70", "Half-Life", "/root/steamapps/common/Half-Life", 5, null, lib),
        };
        var archivedIds = new HashSet<string> { "999" };

        var vm = new MainViewModel();
        vm.LoadRows(installed, archivedIds,
            archivedNames: new Dictionary<string,string> { ["999"] = "Old Game" });

        Assert.Contains(vm.Games, r => r.AppId == "70" && r.Status == "Installed");
        Assert.Contains(vm.Games, r => r.AppId == "999" && r.Status == "Archived");
    }
}
```

- [ ] **Step 3: 跑测试确认失败**

Run: `dotnet test tests/SteamHibernate.App.Tests`
Expected: FAIL(MainViewModel/GameRowViewModel 不存在)。

- [ ] **Step 4: 实现 GameRowViewModel**

```csharp
// src/SteamHibernate.App/ViewModels/GameRowViewModel.cs
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
```

- [ ] **Step 5: 实现 MainViewModel(本步只做 LoadRows,命令在 Task 12 接服务)**

```csharp
// src/SteamHibernate.App/ViewModels/MainViewModel.cs
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using SteamHibernate.Core.Steam;

namespace SteamHibernate.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    public ObservableCollection<GameRowViewModel> Games { get; } = new();

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
}
```

- [ ] **Step 6: 跑测试确认通过**

Run: `dotnet test tests/SteamHibernate.App.Tests`
Expected: PASS。

- [ ] **Step 7: Commit**

```bash
git add -A && git commit -m "feat(ui): MainViewModel/GameRowViewModel with testable LoadRows"
```

---

### Task 12: GUI — 视图 + 接线 + 手动验证

**Files:**
- Modify: `src/SteamHibernate.App/ViewModels/MainViewModel.cs`(加 Compress/Restore 命令 + 后台扫描)
- Create/Modify: `src/SteamHibernate.App/Views/MainWindow.axaml` / `.axaml.cs`
- Modify: `src/SteamHibernate.App/App.axaml.cs`(组装依赖)

- [ ] **Step 1: 在 MainViewModel 加命令(注入服务)**

```csharp
// 追加到 MainViewModel.cs(类内)
using CommunityToolkit.Mvvm.Input;
using SteamHibernate.Core.Engine;
using SteamHibernate.Core.Tiering;

    private GameScanner? _scanner;
    private ManualTieringService? _tiering;
    private SteamHibernate.Core.Metadata.MetadataStore? _store;

    public void Wire(GameScanner scanner, ManualTieringService tiering,
                     SteamHibernate.Core.Metadata.MetadataStore store)
    { _scanner = scanner; _tiering = tiering; _store = store; }

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
        var game = _scanner.Scan().FirstOrDefault(g => g.AppId == row.AppId);
        if (game is null) return;
        row.Busy = true; row.Status = "Compressing";
        var result = await Task.Run(() => _tiering.Compress(game,
            p => row.Progress = p.Fraction));
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
            p => row.Progress = p.Fraction));
        row.Busy = false;
        row.Status = result.Success ? "Installed" : "Error";
        Refresh();
    }
```

- [ ] **Step 2: 写 MainWindow.axaml(列表 + 两个按钮)**

```xml
<!-- src/SteamHibernate.App/Views/MainWindow.axaml -->
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:vm="clr-namespace:SteamHibernate.App.ViewModels"
        x:Class="SteamHibernate.App.Views.MainWindow"
        x:DataType="vm:MainViewModel" Width="820" Height="560"
        Title="SteamHibernate">
  <DockPanel Margin="12">
    <Button DockPanel.Dock="Top" Content="Refresh" Command="{Binding RefreshCommand}" Margin="0,0,0,8"/>
    <DataGrid ItemsSource="{Binding Games}" AutoGenerateColumns="False" IsReadOnly="True">
      <DataGrid.Columns>
        <DataGridTextColumn Header="Game" Binding="{Binding Name}" Width="*"/>
        <DataGridTextColumn Header="Size" Binding="{Binding SizeDisplay}" Width="90"/>
        <DataGridTextColumn Header="Status" Binding="{Binding Status}" Width="110"/>
        <DataGridTemplateColumn Header="Actions" Width="200">
          <DataGridTemplateColumn.CellTemplate>
            <DataTemplate x:DataType="vm:GameRowViewModel">
              <StackPanel Orientation="Horizontal" Spacing="6">
                <Button Content="Compress"
                        Command="{Binding $parent[DataGrid].((vm:MainViewModel)DataContext).CompressCommand}"
                        CommandParameter="{Binding}" IsEnabled="{Binding !Busy}"/>
                <Button Content="Restore"
                        Command="{Binding $parent[DataGrid].((vm:MainViewModel)DataContext).RestoreCommand}"
                        CommandParameter="{Binding}" IsEnabled="{Binding !Busy}"/>
              </StackPanel>
            </DataTemplate>
          </DataGridTemplateColumn.CellTemplate>
        </DataGridTemplateColumn>
      </DataGrid.Columns>
    </DataGrid>
  </DockPanel>
</Window>
```

- [ ] **Step 3: 加 DataGrid 包 + 组装依赖**

```bash
dotnet add src/SteamHibernate.App package Avalonia.Controls.DataGrid
```

```csharp
// src/SteamHibernate.App/App.axaml.cs — OnFrameworkInitializationCompleted 内组装
// using ...;
var cfgStore = new SteamHibernate.Core.Config.ConfigStore(
    SteamHibernate.Core.Config.ConfigStore.DefaultPath());
var cfg = cfgStore.Load();

var locator = OperatingSystem.IsWindows()
    ? (SteamHibernate.Core.Steam.ISteamLocator)new SteamHibernate.Core.Steam.WindowsSteamLocator()
    : new SteamHibernate.Core.Steam.ConfigSteamLocator(cfg.ArchiveRoot); // 非 Windows 仅用于跑起来

var scanner = new SteamHibernate.Core.Steam.GameScanner(locator);
var exe = cfg.SevenZipPath ?? SteamHibernate.Core.Engine.SevenZipEngine.FindBinary()
          ?? throw new InvalidOperationException("7-Zip not found; set it in settings.");
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
// desktop.MainWindow = new Views.MainWindow { DataContext = mainVm };
```

- [ ] **Step 4: 构建 + ViewModel 测试回归**

```bash
dotnet build
dotnet test
```
Expected: Build succeeded;所有测试 PASS/Skipped。

- [ ] **Step 5: 手动冒烟(在 Windows GPU 机)**

1. 把仓库 git archive 到 Windows(参见项目同步约定),`dotnet run --project src/SteamHibernate.App`。
2. 设置归档目录为另一块大盘;选一个**小体积非反作弊**游戏,点 Compress。
3. 确认:游戏目录消失、归档包生成、Steam 该游戏显示"未安装"。
4. 在工具里点 Restore;确认 Steam **直接显示"运行"、无需重下/校验**,游戏可启动。
5. 记录原始/压后大小与压缩比。

- [ ] **Step 6: Commit**

```bash
git add -A && git commit -m "feat(ui): wire manual compress/restore + DataGrid view"
```

---

## Self-Review

**Spec coverage(对照 spec 各节):**
- §1 G1/G2/G3(Steam 透明/自动 hydrate/闲置自动)→ 属 Auto 模式,**Plan 2**(本计划范围外,已在顶部声明)。
- §1 G4 极致压缩 → Task 6(7-Zip LZMA2 solid),precomp 增强见下「后续」。
- §1 G5 绝不丢游戏 → Task 9 commit-on-success + 测试覆盖校验失败不删原目录 ✅
- §1 G6 内核开源可插拔 → Task 6 `IArchiveEngine`;srep 留配置开关(AppConfig.EnableSrep)✅
- §1 G7 手动一等公民 → Task 9 + Task 11/12 ✅
- §5 Steam 集成(定位/库/appmanifest/lastplayed/秒认)→ Task 3/4 + Task 9 Restore 放回 acf ✅
- §6 压缩链 → Task 6 默认 7-Zip;precomp 装饰器在「后续工作」列出(Plan1 不阻塞)。
- §10 安全机制 → Task 9 ✅;崩溃恢复(过渡态扫描)属 Auto 后台,Plan 2 处理。
- §12 GUI(列表/手动操作/进度/设置)→ Task 11/12;设置页 SettingsView 见「后续」(Plan1 用配置文件 + 首启即可,完整设置 UI 可挪 Plan 1.1)。
- §14 测试 → 各 Task TDD + Task 12 端到端手动冒烟 ✅

**Placeholder scan:** 无 TODO/TBD;每个代码步骤含完整代码。`SettingsView` 与 `precomp 装饰器` 显式列为「后续工作」而非计划内占位。

**Type consistency:** `IArchiveEngine`(Compress/Extract/VerifyIntegrity/ArchiveExtension)在 Task 6 定义,Task 7/9/Fake 一致使用;`InstalledGame`/`SteamLibrary`/`ArchiveRecord`/`PackageHeader` 字段在引用处一致;`TieringResult.Success` 一致。

## 后续工作(Plan 1 之后,非本计划阻塞项)
- **precomp 装饰器引擎**:`PrecompDecorator : IArchiveEngine`,在 Compress 前对目录做 precomp 预处理、Extract 后还原,叠加在 SevenZipEngine 上;srep 检测到则插入中间级。
- **SettingsView**:归档目录/等级/引擎路径/srep 开关的完整设置 UI(Plan1 暂用 config.json + 首启提示)。
- **Plan 2 — Auto 模式(ProjFS)**:ProjfsProvider / LaunchWatcher / IdlePolicy / AntiCheatRegistry / 崩溃恢复 / 首启启用 ProjFS 向导。**前置:R1 spike 验证 ProjFS 占位能让 Steam 稳定显示 Play 不触发重下。**
