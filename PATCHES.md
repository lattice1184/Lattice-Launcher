# PCL.Core 补丁清单（vendor 后修改）

本仓库的 `PCL.Core/` 目录 vendor 自 [PCL-Community/PCL-CE](https://github.com/PCL-Community/PCL-CE) tag **v2.15.0**（Apache License 2.0）。
为使核心库脱离 WPF 应用容器运行（宿主改为 Avalonia），对以下文件打了补丁。

## 补丁规则

- 每个补丁记录：文件、上游行号（v2.15.0 为准）、改动内容、原因
- **re-vendor 流程**：下载新 tag → 覆盖 `PCL.Core/` → 按本清单逐条重打补丁 → `git diff` 对照校验

## 补丁清单（全部已执行，2026-08-01）

| # | 文件 | 上游位置 | 改动 | 原因 |
|---|---|---|---|---|
| 1 | `PCL.Core/App/IoC/LifecycleFlow.cs` | `OnInitialize()` 尾部 `Run()`、`OnLoading()` 的 `MainWindow.Show()` | 删除两处调用（消息循环与窗口显示由 Avalonia 宿主驱动） | 生命周期启动代码硬编码 WPF 消息循环 |
| 2 | `PCL.Core/App/Basics.cs` | `GetResourceStream()` pack URI | → `Assembly.GetManifestResourceStream` 回退（原名/程序集前缀名） | 无 WPF Application 实例时抛异常 |
| 3 | `PCL.Core/UI/Animation/Core/AnimationService.cs` | `_Initialize()` 中 `new WpfUIAccessProvider(...)` | 改为可注入 `UIAccessProviderFactory` 属性 + 内置 `DefaultUIAccessProvider`（直执行） | 动画引擎与 UI 框架解耦 |
| 4 | `PCL.Core/Logging/LogService.cs` | 致命错误 `MessageBox.Show` | 改为可注入 `FatalErrorReporter` 委托（默认 stderr） | 无 WPF 对话框可用 |
| 5 | `PCL.Core/App/Essentials/ApplicationService.cs` | `[LifecycleService(BeforeLoading)]` | **移除注册特性**（类保留） | WPF 应用容器服务，Avalonia 不用 |
| 6 | `PCL.Core/App/Essentials/MainWindowService.cs` | `[LifecycleService(WindowCreating)]` | **移除注册特性** | WPF 窗口服务，Avalonia 不用 |
| 7 | `PCL.Core/App/Localization/LocalizationService.cs` | `[LifecycleService(Loaded)]` | **移除注册特性** | PCL 本地化依赖 WPF 资源系统 |
| 8 | `PCL.Core/App/Essentials/RpcService.cs` | `[LifecycleService(Loaded)]` | **移除注册特性** | RPC 服务依赖 WPF 容器 |
| 9 | `PCL.Core/Link/Lobby/LobbyService.cs` | `[LifecycleService(Loaded)]` | **移除注册特性** | 联机大厅非本启动器需求 |
| 10 | `PCL.Core/UI/Theme/ThemeService.cs` | `[LifecycleService(WindowCreating)]` | **移除注册特性** | PCL 主题依赖 WPF 资源系统（CurrentApplication NRE） |
| 11 | `PCL.Core/Minecraft/ResourceProject/Curseforge/CurseforgeFile.cs` | `CurseforgeFile` record 尾部追加 4 个可选参数（downloadUrl/fileLength/gameVersions/dependencies）+ 新增 `CurseforgeFileDependency.cs` | v2.15.0 模型缺安装必需字段（下载 URL/文件大小/支持版本/依赖列表）；STJ 缺字段取默认值，向后兼容 |

> 说明：`UI/NColor.cs`、`UI/NRotateTransform.cs`、`Utils/WpfUtils.cs` 含 `System.Windows.Media` 类型引用，但 net10.0-windows 下类型存在即可编译运行，**不改**（仅 UI 辅助路径）。
> 配套：`src/Launcher.App/metadata.json` 以 `PCL.metadata.json` 逻辑名嵌入（`Basics.Metadata` 静态初始化依赖）。

## Vendor 记录（原样复制 + 命名空间调整，Apache 2.0）

| 资产 | 来源 | 落点 | 改动 |
|---|---|---|---|
| ModDependencyResolver（316 行） | PCL.Core/Minecraft/ResourceProject/ | src/Launcher.Core/Ecosystem/ | 仅命名空间 → Launcher.Core.Ecosystem，逻辑零改动 |
