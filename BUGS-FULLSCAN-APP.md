# Launcher.App 逻辑缺陷全量扫描（2026-08-16）

范围：src/Launcher.App（ViewModel / Service / View code-behind / XAML 绑定）+ 其依赖的
Launcher.Core 关键路径（DownloadTask / DownloadManager / ServerProcess / LittleSkin / Account）。
只报逻辑缺陷，不报风格/命名/性能微优化。严重度：崩溃 > 数据损坏 > 静默失败 > 功能异常。

## 严重

- src/Launcher.App/ViewModels/HomeViewModel.cs:271（同款 src/Launcher.App/ViewModels/AccountViewModel.cs:80）— 空账号名触发 `acc.Name[..1]` 索引越界崩溃 — accounts.json 被手工编辑或含空 Name 账号（或 Littleskin 认证返回空角色名）时，启动/登录后 RefreshPlayer 在 UI 线程抛 IndexOutOfRangeException，每次启动都弹崩溃窗，须手删文件才能恢复。
- src/Launcher.App/ViewModels/VersionManageViewModel.cs:407-428 — 非隔离版本备份把输出 zip 包进源目录，zip 自包含损坏或直接失败 — 版本隔离关闭（默认）时 RootDir=_gameDir，`ZipFile.CreateFromDirectory(_gameDir, _gameDir\backups\xx.zip)`：backups 目录创建于打包前，枚举会把正在写入的 zip 自身（及全部 versions/ 目录，体积爆炸）打进归档 → 要么 IOException 备份失败，要么产出损坏/巨型 zip，且备份路径永远不可恢复。

## 中等

- src/Launcher.App/Views/StorageWindow.axaml.cs:83-90 — 删除操作在 Task.Run 后台线程里调 DialogService.Confirm（ShowDialog 必须 UI 线程）→ Avalonia 抛跨线程异常被吞 → 确认框永不出现、删除永远不执行（静默失败）— 复现：存储窗口点任意「删除」→ 无对话框、无删除、无提示。
- src/Launcher.Core/Server/ServerProcess.cs:112 + src/Launcher.App/Views/MainWindow.axaml.cs:101 — 启动器退出不杀服务端进程（无窗口关闭清理，ServerProcess.Dispose 从未被调用）— 复现：开服运行中直接关启动器 → 孤儿 java 进程残留占用端口；且 stdout 重定向管道无读者，java 写满 64KB 缓冲后挂起。
- src/Launcher.App/ViewModels/SkinLibraryViewModel.cs:306-317 — 泛型 WithRefreshAsync 401 自愈把原操作执行两次（RefreshAndRetryAsync 内部已 retry 一次，返回后第 315 行又执行一次）— 复现：token 过期后点「应用皮肤」→ ApplySkin PUT 发两次、衣柜页加载两遍。
- src/Launcher.App/ViewModels/SkinLibraryViewModel.cs:28,322-335 — `_tokenRefreshed` 成功后永不复位，整个窗口会话只允许一次 401 自愈 — 复现：连接后 token 第二次过期（LittleSkin access token 有效期短）→ 不刷新而是直接 Disconnect 清 token，用户被登出。
- src/Launcher.App/ViewModels/ServerViewModel.cs:1062-1065 / 1115-1117 / 1128-1130 — `_autoStopOnReady`/`_autoJoinOnReady` 在启动失败（java 缺失/取消）后不回置，状态泄漏到下一次手动开服 — 复现：一键开服/生成世界时 Java 选配失败 → 之后手动启动服务端 → 就绪（Done）时被自动 stop 或自动拉起客户端进服。
- src/Launcher.App/ViewModels/DownloadViewModel.cs:34-37,172-176 — `_returnTo` 单槽位竞态：多个并发下载任务共用一个跳回目标，先完成的任务消费后一个任务的跳回页 — 复现：先入队「下载服务端」（returnTo=server）再入队「装 MOD」（returnTo=download:mod），服务端先完成 → 跳到 server（错）；或 MOD 先完成 → 跳到 download:mod（对），服务端完成后不再跳。
- src/Launcher.Core/Download/DownloadTask.cs:615-619 + src/Launcher.App/Views/DownloadView.axaml:29-34 — Paused 任务无取消/移除途径：Cancel() 对已暂停任务为空操作（运行循环已退出），UI 只显示「继续」，ClearFinished 跳过 Paused — 复现：暂停全部（SuspendAll 含排队任务）→ 某任务断点文件/源已失效 → 该任务永久滞留队列，无法删除。
- src/Launcher.App/ViewModels/LoaderPickerViewModel.cs:69-97 — `if (IsLoadingVersions) return` 早退：加载器切换竞态 — 复现：点 Fabric（请求在途）后立即点 Quilt → Quilt 的版本列表永不加载，UI 显示选中 Quilt 但列表是 Fabric 的。
- src/Launcher.App/ViewModels/HomeViewModel.cs:508 — 版本级 Java 覆盖全局 `LauncherSettings.Current.JavaPath` 内存值且不写盘不回写 — 复现：版本 A 配了版本级 Java 启动后 → 开服页/配置摘要读到的是版本 A 的 Java；随后用户改全局设置并保存才恢复。
- src/Launcher.App/ViewModels/SettingsViewModel.cs:463-471 — ApplyCustomAccent 只比对静态 AccentPresets，重复应用同一自定义色时「自定义 #HEX」项无限累积 — 复现：选色器连续两次确认同一非预设色 → 下拉出现多个重复「自定义 #HEX」条目。
- src/Launcher.App/ViewModels/EcosystemViewModel.cs:540-577 — LoadFavoritesAsync 的 catch{} 吞掉 OperationCanceledException（ct 检查在 try 外仅一次），取消语义只在收藏项间生效 — 复现：收藏模式搜索中切换筛选 → 旧收藏加载会继续拉完当前项目才被 seq 丢弃（取消延迟，慢源时多等 1 次请求）。

## 轻微

- src/Launcher.App/ViewModels/ProjectDetailViewModel.cs:365 — 行内安装 `Install(default)` 绕过 RelayCommand 生成的取消 CTS → InstallCancelCommand 失效 — 复现：点版本行安装 → 点「取消」按钮 → 无反应，安装继续。
- src/Launcher.App/ViewModels/ProjectDetailViewModel.cs:251-305 — LoadCfAsync 无实例切换守卫（Modrinth 路径有 ReferenceEquals 检查，CF 路径没有）— 复现：详情页打开后切换实例 → 旧实例的 CF 版本列表/匹配结果晚到覆盖新实例 UI。
- src/Launcher.Core/Download/DownloadTask.cs:501-509 — PublishAggregate 在 threadpool 线程直接写 ProgressPercent/Stage（未走 Post），与 UI 完成 Post 的先后无 FIFO 保证 — 复现：组任务子任务全完成后 60ms 尾算与完成 Post 交错 → 界面短暂/持久回退到 99% 或旧 Stage。
- src/Launcher.App/Services/ImageLoader.cs:30-53 — ct 参数从未使用；失败 URL 永久缓存 null（直到超过 128 条整体清空）→ 断网恢复后坏图标不再重试 — 复现：离线启动加载图标全失败 → 联网后图标仍不显示。
- src/Launcher.App/ViewModels/ThirdPartyDownloadViewModel.cs:20-24 — static Downloader 以 options:null 构造，第三方下载永远不应用用户限速/分片/并发设置 — 复现：设置限速后第三方下载不生效（版本下载路径已用 FromSettings 修复，此路径漏了）。
- src/Launcher.App/ViewModels/VersionDownloadViewModel.cs:172-175 — RefreshInstalled 只置 true 不置 false — 复现：下载页选中某版本 → 去版本页删除该版本 → 回下载页详情仍显示「已安装」。
- src/Launcher.App/Views/SectionDownloadView.axaml.cs:18-21 — DataContextChanged 订阅不取消，SettingsViewModel 单例持有每次重建的 SectionDownloadView — 复现：设置页反复切换分区 → 旧视图无法 GC，内存持续增长（状态泄漏）。
- src/Launcher.App/ViewModels/SettingsViewModel.cs:513-523 — DebouncedSave 是 async void，Save() 写盘异常无捕获（只靠全局 UnhandledException 兜底弹崩溃窗）— 复现：settings.json 被占用/磁盘满时拖动并发滑块 → 弹「未捕获异常」窗。
- src/Launcher.App/ViewModels/EcosystemViewModel.cs:35-36 — 构造期 SelectedSort/SelectedGameVersion 赋值（_suppressSearch 只挡源切换）触发两次网络搜索 — 复现：进下载页预加载 5 个 tab → 10 个无效搜索请求打到 Modrinth/CF，用户不开 tab 也浪费流量与限流配额。
- src/Launcher.App/ViewModels/SkinLibraryViewModel.cs:86-132 — ConnectAsync 不先取消旧 _connectCts，连点两次连接会并行两轮设备码轮询 — 复现：连接请求在途时再次点连接 → 两个轮询循环并发，重复 Save token。
- src/Launcher.App/ViewModels/ServerViewModel.cs:232-258 — Exited 自修复在 300ms 延迟后读当前 SelectedVersion，而非退出时的版本 — 复现：服务端 A 崩溃后 300ms 内切换选中 B → 自动重下/重启的是 B 而非崩溃的 A。
- src/Launcher.App/ViewModels/DownloadViewModel.cs:139-147 + 31 — `_recorded` HashSet 永久持有全部终态任务引用、任务 PropertyChanged 订阅从不解除 — 长会话内存线性增长（量小但无界）。
- src/Launcher.App/ViewModels/VersionBrowseViewModel.cs:162-171 — `_syncCts` 只 Cancel 不 Dispose — 每次磁盘事件泄漏一个 CTS（低频率，量小）。
- src/Launcher.App/ViewModels/HomeViewModel.cs:690-715 — 每条游戏日志在 UI 线程同步 AppendAllText 写文件 — 复现：游戏刷屏日志时界面卡顿（每条日志一次 UI 线程磁盘 IO）。
- src/Launcher.App/Services/ImageLoader.cs:39-43 — decodeWidth>96 时绕过缓存直接二次下载同一 URL（与 96px 缓存任务并发）— 复现：详情页大图与列表小图同 URL 并行下载两次。
