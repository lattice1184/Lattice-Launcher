# REVIEW-F：跨线程正确性专项审计（2026-08-11）

## 候选清单结论（前置确认）

**核心前提（本次审计的关键查证）**：Avalonia 12.1.1 在 UI 线程**自动安装** SynchronizationContext（`AvaloniaSynchronizationContext.AutoInstall = true`，已对 `~/.nuget/packages/avalonia/12.1.1` 的 Avalonia.Base.dll 做 IL 验证：`InstallIfNeeded() → SynchronizationContext.SetSynchronizationContext`，`Post → Dispatcher.Post`）。因此「从 UI 线程启动的 async 方法，其 await 之后的 continuation 会回到 UI 线程」成立；**从线程池启动的 async 方法（Task.Run 内、后台回调内）continuation 永远留在线程池**（SC.Current=null），这是全部风险的判别基准。App 侧无任何 `SetSynchronizationContext` 覆盖。

1. **VersionManageViewModel.LoadAsync（92-98 行）→ 假（安全）**：ctor 在 UI 线程被调用（VersionBrowseViewModel.Select:459 → UI 事件链），`await Task.Run` 的 continuation 回 UI 线程，Mods/Saves 的 Clear/Add 均在 UI 线程；92 行注释所述设计正确。
2. **EcosystemViewModel.InitializeAsync / LoadFavoritesAsync / RunSearchAsync → 安全**：所有入口（SelectTab / PreloadTabs / Activate / 筛选属性变更 / 命令）均在 UI 线程启动，continuation 回 UI，Cards/Instances 修改安全。
3. **DownloadViewModel.SelectTab / CreateAndLoad / PreloadTabs → 安全**：同上，fire-and-forget 的 load() 从 UI 线程启动。
4. **ServerViewModel 刷新路径 → 安全**：850/873 有 Post 守卫；RefreshVersionsAsync、StartServer（675 Logs.Clear）、ParsePlayerLine（1104-1121 OnlinePlayers）、LoadProperties（1132 PropRows）、AppendLog（1168-1170 守卫）全部收敛到 UI 线程。
5. **DownloadManager.Instance.Tasks → 安全但有前提**：Instance 首次访问在 MainViewModel ctor（UI 线程，App.axaml.cs:34 链）捕获 Avalonia SC，Tasks 增删（76/110/116/154/165）与 DownloadTask.Children（AttachChild 内 Post）全部封送；前提 = 所有 Enqueue 调用点在 UI 线程 —— 已逐一验证全部 16 个 Enqueue 调用点（命令/UI continuation）。见 LOW-5 的脆弱点。

## 发现

**C:\Users\yanka\Desktop\launcher\src\Launcher.App\ViewModels\ProjectDetailViewModel.cs:371 | 高 | UpdateContext 的 Task.Run 使 LoadVersions 在池线程改 AllVersions（ObservableCollection）**

`UpdateContext(instance)`（364-376 行）：`Files.Clear()` 在 UI 线程后，`_ = Task.Run(async () => { await LoadAsync(); if (_versionsLoaded) await LoadVersions(); })` —— Task.Run 内启动的 LoadAsync/LoadVersions **在池线程执行且无 SynchronizationContext**，所有 await continuation 留在池线程：
- `LoadVersions()`（320-361）：`AllVersions.Clear()`（333）+ 网络 await 后 `AllVersions.Add(...)`（339/345）→ **ObservableCollection 跨线程修改**，绑定它的 ComboBox/ListBox 在 UI 线程枚举 → Collection 竞态/跨线程异常。
- 同时 `LoadAsync()`/`LoadCfAsync()` 的 `VersionHint/CanInstall/Changelog/License/DependencyHint/Gallery*/LoadScreenshot(105)` 等**绑定属性也在池线程触发 INPC**（绑定更新无封送）。

触发条件：生态页打开任一项目详情（OpenDetail → ctor 的 `_ = LoadAsync()` 本身安全），然后在顶部实例下拉切换目标实例（EcosystemViewModel.OnSelectedInstanceChanged:223 → Detail.UpdateContext）—— 且用户此前展开过「加载版本列表」（_versionsLoaded=true）则必然触发 AllVersions 跨线程修改；即使未展开也会在池线程更新一堆绑定属性。

建议修复：去掉 Task.Run，直接 `await LoadAsync(); if (_versionsLoaded) await LoadVersions();`（方法内部本就是网络 await，不会阻塞 UI —— Task.Run 是多余且有害的）；或保留 Task.Run 时在 continuation 前用 `await Dispatcher.UIThread.InvokeAsync(...)` 包裹集合/属性写入。

---

**C:\Users\yanka\Desktop\launcher\src\Launcher.App\Views\StorageWindow.axaml.cs:83 | 中 | OnDeleteRequested 在 Task.Run 线程内弹确认对话框（Avalonia 窗口操作跨线程）**

`OnDeleteRequested`（81-106）：`_ = Task.Run(async () => { ... await DialogService.Confirm(owner, ...) ... })` —— `DialogService.Confirm → MessageDialogWindow.ShowAndWaitAsync:57` 的 `win.ShowDialog(owner)` / `win.Show()`（MessageDialogWindow.axaml.cs:50-66）从**线程池线程**调用。Avalonia 窗口 Show/ShowDialog 必须在 UI 线程；异常会冒泡出 ShowAndWaitAsync（其 catch 里再 Show 仍跨线程）→ 确认框不出现、删除永远不执行；Task.Run 是 fire-and-forget，异常成为未观察任务异常。

触发条件：存储窗口（StorageWindow）中任意可删项点「删除」。

建议修复：确认框提升到 UI 线程（在 Task.Run 之外 await），Task.Run 只包 `File.Delete/Directory.Delete` IO；删除后的 `Items.Remove` 已正确 Post（95-99 行）。

---

**C:\Users\yanka\Desktop\launcher\src\Launcher.App\ViewModels\HomeViewModel.cs:418（配合 AccountService.cs:82、HomeViewModel.cs:191） | 中 | 正版启动时 RefreshMicrosoftAsync 的 Changed 事件在池线程触发主页玩家区绑定更新**

HomeViewModel:418 `var session = await Task.Run(() => _accounts.RefreshMicrosoftAsync());` —— RefreshMicrosoftAsync 整体在池线程执行，其内部的 `Changed?.Invoke()`（AccountService.cs:82，await MicrosoftAuth.RefreshAsync 之后）在**池线程**触发。订阅者 HomeViewModel:191 `_accounts.Changed += RefreshPlayer` 在池线程执行 `RefreshPlayer`（269-285）→ 更新绑定属性 PlayerName / AccountTypeText / PlayerAvatar，且 `ImageLoader.LoadAsync`（284）从池线程启动 → onLoaded 回调也在池线程 → `PlayerAvatar = bmp` 仍在池线程。绑定属性跨线程更新：Avalonia 不封送，可能抛「Call from invalid thread」或渲染状态错乱。

触发条件：正版（Microsoft）账号点击「启动游戏」且 access token 过期（静默刷新路径）。

建议修复：RefreshPlayer 入口加 `Dispatcher.UIThread.CheckAccess()` 守卫（同 AppendLog 模式）或 Post 封送；或在 HomeViewModel:418 改为 Task.Run 只包网络调用、Changed 的消费在 UI continuation 完成。

---

**C:\Users\yanka\Desktop\launcher\src\Launcher.App\Views\StorageWindow.axaml.cs:66 | 低 | Task.Run 内直接写绑定属性 item.SizeText（池线程触发 INPC）**

`LoadAsync` 的第二个 Task.Run（66-70）：`foreach (var item in snap) item.SizeText = StorageScanner.FormatSize(...)` —— StorageItemVM.SizeText 是 `[ObservableProperty]`（StorageWindow.axaml.cs:20-21），ListBox 行绑定该属性；赋值发生在池线程 → 绑定更新无封送（同中-3 的性质，风险面小：仅一行文本）。

触发条件：存储窗口打开，后台逐项算大小时。

建议修复：Task.Run 内收集 (item, text) 结果，循环结束后 `Dispatcher.UIThread.Post` 统一赋值。

---

**C:\Users\yanka\Desktop\launcher\src\Launcher.Core\Download\DownloadManager.cs:27（配合 DownloadTask.cs:475-479） | 低 | 架构脆弱点：SC 捕获依赖「首次访问时机」，Cancel/Suspend 遍历 Children 无封送**

两处当前安全、但靠调用方纪律维持的脆弱点：
- `DownloadManager.Instance` 惰性构造时捕获 `SynchronizationContext.Current`（27 行）。当前首次访问 = MainViewModel ctor（UI 线程）→ _ui = Avalonia SC；**若未来任何后台路径先于 UI 触达 Instance（或测试/插件在后台 Enqueue），_ui=null → UiPost/Post 全部同步直跑 → Tasks/Children/PropertyChanged 全部裸奔跨线程**（UiPost:179 `if (_ui is null) action()`）。
- `DownloadTask.Cancel()/Suspend()`（475-479/315）直接 foreach 迭代 ObservableCollection Children，无 Post/无锁；当前调用方全在 UI 线程或仅遍历叶子（无 Children），暂未构成真实竞态，但 AttachChild 与 Cancel 并发时无保护。

建议：构造时改捕获 `Dispatcher.UIThread` 或对 _ui==null 加防御断言（开发期暴露时序违规）；Cancel/Suspend 的 Children 遍历收敛到同一 Post 内或加锁（与 AttachChild/RecomputeAggregate 的 _lock 一致）。

---

## 统计

- 高：1 条（ProjectDetailViewModel.UpdateContext）
- 中：2 条（StorageWindow 确认框 / 正版启动 Changed 跨线程）
- 低：2 条（StorageWindow SizeText / DownloadManager 架构脆弱点）
- 合计：5 条
