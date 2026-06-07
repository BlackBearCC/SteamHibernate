# SteamHibernate 设计文档

> 代号 **SteamHibernate**(暂定,可改)。一个 **Steam 专用**的桌面 GUI 工具:把长期不玩的游戏极致压缩冷藏,在 Steam 里照常点 Play 时自动唤醒,全程不干预 Steam 正常操作。

> **压缩引擎实测要点(2026-06-07,真机)**:默认引擎是纯 7-Zip LZMA2。precomp 引擎(`.pc7z`)是**可选、默认关闭**的增强,只对 zlib/deflate 打包的游戏有用;对已压缩素材的游戏(Unity LZ4/LZMA bundle、UE5 Oodle、Forza 等)**反而更大更慢**——实测 Overcooked! 2(Unity)plain 5.4 GB/4 分钟,precomp 5.9 GB/10 分钟。故 precomp 必须 opt-in 逐游戏判断,绝不能默认开。两条引擎的 round-trip 均经真机验证字节完好(Steam 秒认不重下)。

- 日期:2026-06-07
- 平台:Windows 10 1809+ / Windows 11(NTFS)
- 技术栈:C# / .NET 8 + Avalonia(GUI),Microsoft.Windows.ProjFS(虚拟化),外部压缩引擎(precomp / 7-Zip-LZMA2,srep 可选)
- 界面与对外文档:英文;本设计文档:中文(便于评审)

---

## 1. 定位与目标

### 要解决的问题
Steam 库占满系统盘,用户被迫"删游戏 → 重下新游戏"的循环往复。重下耗带宽、耗时间,还可能遇到游戏更新/下架。

### 核心思路
把**长期不玩**的游戏用实体压缩(solid `precomp → srep? → LZMA2`)榨到极致冷藏,腾出空间;但对 Steam **完全透明** —— 通过 ProjFS 让被冷藏的游戏目录在 Steam 眼里"看着就像装着",显示 Play、校验照过。用户点 Play 的瞬间自动解压唤醒,游戏正常启动;闲置久了后台自动再冷藏。

### Goals
- G1:被冷藏的游戏在 Steam 里仍显示"已安装 / 可运行",不触发重下、不触发完整性校验失败。
- G2:用户点 Play 自动唤醒(hydrate),无需手动操作。
- G3:闲置 N 天的游戏后台自动冷藏(dehydrate)。
- G4:极致压缩,优先 ratio(可接受首次唤醒的解压等待)。
- G5:**绝不丢游戏** —— 任何失败都不损坏原始可玩状态。
- G6:压缩内核全开源、可插拔;srep 为可选增强。
- G7:**手动模式为一等公民** —— GUI 支持玩家完全手动地压缩/解压,不依赖自动分层,也不强制 ProjFS。

### Non-Goals
- 不做 Steam 之外的平台(Epic/GOG/独立程序)—— 本项目**专门针对 Steam**。
- 不做"压完直接从压缩包运行"(物理不可行,见 §3)。
- 不做透明 NTFS/LZX 压缩(那是另一条路,ratio 不够)。
- 不做云同步/远程归档(首版只管本机 + 用户指定目录)。

---

## 2. 一条必须写在最前面的物理铁律

**极致(solid)压缩的包没法被直接运行。** precomp/srep/LZMA2 是实体流压缩,读其中任一文件都需先解开一大块乃至整包。因此"压到极致"与"从压缩包直接启动"二者不可兼得。

本项目的取舍:**坚持极致压缩,代价是首次唤醒需要一次完整解压**(对用户表现为:冷藏后第一次点 Play 有解压等待,之后再玩即时)。ProjFS 负责让这件事对 Steam 透明、对用户自动。

---

## 3. 核心概念:自动分层(Dehydration / Hydration)

每个游戏在三种物理状态之间流转,但**对 Steam 始终呈现"已安装"**:

```
        闲置 N 天 / 用户手动
  HYDRATED ───────────────────────▶ DEHYDRATED
 (真实文件,                          (ProjFS 占位目录 +
  可直接玩)  ◀───────────────────────  极致压缩归档包)
        点 Play 触发 / 用户手动
                (hydrate)
```

- **HYDRATED**:`steamapps/common/<游戏>/` 是真实文件,游戏可直接运行。归档包可保留(便于下次秒冷藏),也可在确认后删除以省冷存储。
- **DEHYDRATED**:真实文件已删除,目录被 ProjFS 占位接管;真身是归档目录里的压缩包。Steam 看到完整文件清单 → 显示 Play、校验通过。
- **HYDRATING / DEHYDRATING**:过渡态,带进度,期间 UI 锁定该游戏的操作。

状态记录在 MetadataStore;ProjFS 占位的"虚拟文件清单"来自归档时保存的目录快照(manifest)。

### 两种运行模式(玩家可全局选,亦可按游戏覆盖)

| | **Auto 模式(ProjFS 自动分层)** | **Manual 模式(纯手动归档/还原)** |
|---|---|---|
| Steam 显示 | 始终 Play(透明) | 冷藏后显示"未安装",还原后才 Play |
| 唤醒触发 | 点 Play 自动 hydrate | 玩家在本工具里手动点 Restore |
| 冷藏触发 | 闲置 N 天自动 / 也可手动 | 完全玩家手动点 Compress |
| 依赖 ProjFS | 是 | **否**(ProjFS 不可用也能跑) |
| 适合 | 想全自动、无感的玩家 | 反作弊游戏 / 不想要后台魔法 / 旧系统 |

- 两种模式**共用同一套压缩内核(ArchiveEngine)与归档包格式(PackageFormat)** —— 区别只在"是否建立 ProjFS 占位 + 是否自动触发"。
- Manual 模式下,Compress = 压缩+校验+删真实文件(Steam 转为未安装);Restore = 解包+把 appmanifest 与真实文件就位(Steam 秒认)。**不建占位、不挂 ProjFS。**
- 即使全局选了 Auto,GUI 也始终提供对单个游戏的手动 Compress/Restore 操作。手动是底座,自动是其上的便利层。

---

## 4. 架构与模块边界

每个模块单一职责、接口清晰、可独立测试。

| 模块 | 职责 | 依赖 |
|---|---|---|
| **SteamLocator** | 从注册表定位 Steam;解析 `libraryfolders.vdf` 得到所有库目录 | 注册表、文件系统 |
| **GameScanner** | 遍历各库 `appmanifest_*.acf` → (appid/名称/安装目录/体积);从 `localconfig.vdf` 读最后游玩时间;判定冷/热 | SteamLocator、VdfParser |
| **VdfParser** | 解析 Valve 的 VDF(KeyValues)文本格式 | 无 |
| **ArchiveEngine**(接口) | 输入目录 → 输出单个压缩包 + 目录 manifest;支持进度回调、完整性校验、可逆解包 | 外部 exe |
| └ `PrecompLzmaEngine` | 默认实现:`precomp` 预处理 → (srep 可选)→ `LZMA2` solid 打包 | precomp/7z/srep |
| **PackageFormat** | 归档包结构读写:游戏数据 + `appmanifest_<appid>.acf` + 目录 manifest + 元数据头 | ArchiveEngine |
| **ProjfsProvider** | 注册/管理 ProjFS 虚拟化根;响应枚举/读取回调;触发 hydration | ProjFS API、Orchestrator |
| **HydrationOrchestrator** | 状态机核心:dehydrate / hydrate 的完整流程编排 + 安全保证 | ArchiveEngine、ProjfsProvider、MetadataStore、SteamLocator |
| **LaunchWatcher** | 检测游戏即将/正在启动(ProjFS 首次访问回调为主信号),驱动按需 hydrate | ProjfsProvider |
| **IdlePolicy** | 后台扫描最后游玩时间,按策略触发自动 dehydrate | GameScanner、HydrationOrchestrator |
| **MetadataStore** | JSON 索引:每个游戏的状态、归档路径、原始/压后大小、目录 manifest、时间戳 | 文件系统 |
| **AntiCheatRegistry** | 已知带强反作弊(EAC/BattlEye 等)游戏的排除清单 + 用户自定排除 | 内置数据 + 配置 |
| **Config** | 归档目录、压缩等级、引擎路径、srep 开关、闲置阈值 N、自动开关 | 文件系统 |
| **GUI (Avalonia ViewModels)** | 游戏列表、状态/进度展示、手动操作入口、设置页 | 上述全部 |

---

## 5. Steam 集成细节(本项目的护城河)

- **定位 Steam**:注册表 `HKCU\Software\Valve\Steam\SteamPath`(回退 `HKLM\...\WOW6432Node\Valve\Steam`)。
- **枚举库**:`<steam>/steamapps/libraryfolders.vdf` → 各库根。
- **枚举游戏**:每个库 `steamapps/appmanifest_<appid>.acf` → `appid` / `name` / `installdir` / `SizeOnDisk`。
- **最后游玩时间**:`<steam>/userdata/<id>/config/localconfig.vdf` 里每个 app 的 `LastPlayed`(Unix 秒)。多用户取最大值。
- **"秒认"机制**:Steam 判定"已安装"只依赖 `appmanifest_<appid>.acf` + 安装目录的文件存在性。dehydrated 态下 ProjFS 提供完整虚拟文件清单 → Steam 认为完好。还原时把真实文件 + 原 appmanifest 就位即可,**不重下、不重新校验**。
- **appmanifest 一并归档**:归档包内含该游戏的 `.acf` 副本,作为还原与状态判定的事实来源。

---

## 6. 压缩引擎(可插拔、全开源默认链)

`ArchiveEngine` 接口:
```
Compress(srcDir, outPackage, level, progress) -> manifest + sizes
Extract(package, dstDir, progress)            // 完整解包
VerifyIntegrity(package) -> bool              // 解包前/压缩后校验
BuildVirtualManifest(package) -> FileEntry[]  // 供 ProjFS 占位用(文件名/大小/属性)
```

默认实现 `PrecompLzmaEngine`:
1. `precomp` —— 无损还原文件内部的 zlib/Deflate 流(开源,Apache/LGPL 系),为后续重压创造空间。
2. `srep`(**可选**)—— 超长距离去重;检测到用户机器上存在才启用。⚠️ srep 为闭源 freeware,**不随仓库分发**,仅作运行时可选增强。
3. `LZMA2`(7-Zip / xz,开源)—— solid 极致压缩成单包。

许可证洁癖:仓库默认只依赖 precomp + 7-Zip/xz(全开源),srep 永不内置。

> 与 ProjFS 的协同:solid 压缩本就需要整包解压,正好与"首次访问时整目录 hydrate"的策略一致 —— 不追求 per-file 随机读取,故可用最强 solid ratio。

---

## 7. ProjFS Provider 设计

- 用 **Microsoft.Windows.ProjFS** 托管封装。首次运行检测并(经用户同意)启用 "Windows Projected File System" 可选特性。
- **占位来源**:dehydrate 时保存的目录 manifest(完整文件名/大小/目录结构/属性)。ProjFS 枚举回调据此返回,使 Steam 看到一个"完整安装"。
- **读取回调(Get File Data)**:任何对占位目录内文件的实际读取 = 游戏正在启动的信号 → 触发 **整目录 hydrate**(见 §8),而非逐文件懒加载(因 solid 压缩无法廉价随机读)。
- hydrate 完成后,真实文件落地,ProjFS 虚拟化对该目录**退场**,游戏直接读真实文件,零额外开销。

---

## 8. 唤醒流程(Hydrate,点 Play 自动触发)

1. ProjFS 读取回调触发 → `LaunchWatcher` 通知 `HydrationOrchestrator`。
2. 弹出**解压进度浮层**(告知用户"正在唤醒 <游戏>,首次启动需解压")。
3. `ArchiveEngine.VerifyIntegrity(package)` 校验归档包完好。
4. 解包到**临时目录**(同盘),完成后再原子改名/移动到 `common/<游戏>/`(**commit-on-success**)。
5. ProjFS 虚拟化退场;`MetadataStore` 状态置 HYDRATED。
6. 放行,游戏正常启动。
7. 归档包默认保留(下次冷藏可秒做);可在设置里选"唤醒后删除归档省冷存储"。

**首次唤醒等待**是实体压缩的必然代价(100G 约数分钟,仍远快于重下)。

---

## 9. 冷藏流程(Dehydrate,闲置自动 / 手动)

1. `IdlePolicy` 发现某 HYDRATED 游戏最后游玩 > N 天(或用户手动),且进程未运行、不在反作弊排除名单。
2. 扫描目录 → 生成 manifest(供日后占位)。
3. `ArchiveEngine.Compress(...)` → 归档包(含 appmanifest 副本 + manifest + 元数据)。
4. `VerifyIntegrity` **校验归档包可完整还原**(round-trip 抽检)。
5. **校验通过后**才删除真实文件,并用 manifest 建立 ProjFS 占位(**commit-on-success,绝不先删后压**)。
6. `MetadataStore` 状态置 DEHYDRATED。

---

## 10. 安全机制(G5:绝不丢游戏)

- **commit-on-success,不做 try-then-rollback**:冷藏先压+校验再删;唤醒先解到临时目录+校验再就位。任一步失败,原状态保持不变,游戏始终可玩或可重唤醒。
- **不写 fallback 兜底路径**:失败显式报错并停在安全态,绝不"猜一个降级方案"继续。
- **进程占用检测**:游戏运行中禁止 dehydrate。
- **磁盘空间预检**:hydrate 前确认目标盘容得下解压结果,否则提前报错。
- **崩溃恢复**:启动时扫描 MetadataStore 中的过渡态(HYDRATING/DEHYDRATING),据临时目录残留判定并回到最近的安全态。

---

## 11. 反作弊处理

- 内置 `AntiCheatRegistry`:已知带 EAC / BattlEye / Vanguard 等强反作弊的 appid 默认**排除**(不冷藏),因虚拟化/过滤型文件访问可能触发封禁或启动失败。
- 用户可手动加入/移除排除项,并在 UI 明确警示风险。
- 首版策略:宁可不压,也不冒封号风险。

---

## 12. GUI 设计(Avalonia,英文界面)

- **主窗口 — 游戏列表**:每行 = 游戏名 / 占用大小 / 最后游玩 / 当前模式(Auto·Manual)/ 状态徽标(Installed · Hydrated · Dehydrated/Archived · 过渡态进度)。冷游戏(超阈值)标黄。
- **行内手动操作(始终可用,G7)**:对任一游戏一键 **Compress / Restore**(Manual 语义)或 **Dehydrate / Hydrate**(Auto 语义);Dehydrated/Archived 行显示"已省 X GB"。多选批量压缩/还原。
- **进度浮层**:压缩/解压分阶段进度 + 实时压缩比 + 可取消(取消即停在安全态)。
- **设置页**:**默认模式(Auto / Manual)**、归档目录(用户自定)、压缩等级、外部引擎路径、srep 开关、闲置阈值 N、自动冷藏开关、反作弊排除清单。
- **按游戏覆盖**:右键单个游戏可把它固定为 Auto 或 Manual,覆盖全局默认。
- **首启向导**:选默认模式、(若选 Auto)检测/启用 ProjFS、定位 Steam、设归档目录、引擎自检。
- 关系:**手动压缩/解压是底座,任何时候都能用;自动分层是其上的便利层**(需 ProjFS)。ProjFS 不可用时整个工具仍以 Manual 模式完整可用。

---

## 13. 错误处理

- 所有外部 exe 调用捕获退出码与 stderr,失败显式上抛并在 UI 呈现。
- 归档/解包失败 → 保持安全态 + 明确错误,不静默降级。
- ProjFS 不可用(系统过旧/特性未启用)→ 引导启用;无法启用则禁用自动分层、仅留手动归档/还原(此时回退为显式 archive/restore 模式)。

---

## 14. 测试策略

- **单元**:VdfParser 喂真实 acf/vdf 样本;MetadataStore 读写;AntiCheatRegistry 命中。
- **引擎 round-trip**:小目录 Compress→Extract→逐字节比对一致(核心正确性)。
- **状态机**:HydrationOrchestrator 在各失败注入点都回到安全态(模拟磁盘满、校验失败、进程占用)。
- **ProjFS 集成**:小型虚拟目录的枚举/读取触发 hydrate(需 Windows 环境)。
- **端到端**:取一个小体积 Steam 游戏,完整跑 dehydrate → Steam 仍显示 Play → 点 Play 自动 hydrate → 正常启动。

---

## 15. 技术栈与分发

- C# / .NET 8;Avalonia UI;Microsoft.Windows.ProjFS。
- 外部引擎随发布附带 precomp + 7-Zip(均开源);srep 留用户自备。
- 分发:GitHub Release,自包含构建(尽量单目录绿色版)。
- 开发在容器(dotnet 跨平台编译),ProjFS 相关功能必须在 Windows GPU 机上实测。

---

## 16. 风险与待定

- **R1 ProjFS 与 Steam 校验的真实行为**:需在 Windows 上实测 —— 占位目录能否稳定让 Steam 显示 Play 且不触发"缺失文件 → 校验/重下"。这是项目成立的前提,应作为第一个验证里程碑(spike)。
- **R2 反作弊兼容面**:实际可安全冷藏的游戏范围需逐步摸清。
- **R3 hydrate 期间游戏被强行启动**的竞态处理。
- **R4 Steam 在游戏更新时**对 dehydrated 占位目录的写入行为(更新应先 hydrate)。
- **待定**:正式项目名(代号 SteamHibernate 暂用)。
