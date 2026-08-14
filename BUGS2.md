# BUGS2.md — UI/服务层代码审查发现（2026-08-11 改动文件）

## 高

### B1 崩溃窗「一键修复」在后台线程入队下载任务 → ObservableCollection 跨线程修改
- **文件**: src/Launcher.App/Views/CrashReportWindow.axaml.cs:187-193 → src/Launcher.Core/Diagnostics/AutoRepairService.cs:67 → src/Launcher.Core/Download/DownloadManager.cs:76-78
- **问题**: `OnRepair` 用 `await Task.Run(() => FixRedownloadAsync(...))` 把入队动作放到线程池线程。`FixRedownloadAsync` 内 `DownloadManager.Instance.EnqueueGroup` → `AddAndTrack` 直接 `Tasks.Add(task)` + `KeepActiveOnTop()`（`Tasks.Move` 且遍历枚举）——`Tasks` 是绑定到 UI 的 ObservableCollection，线程池线程增删/移动会触发 CollectionChanged 在非 UI 线程广播（Avalonia ItemsControl 绑定处理器直接访问可视树 → 抛 "Call from invalid thread" 或集合枚举竞态）。下载队列页绑定的就是这个集合，任何在 UI 线程上的并发操作（状态 Post、ScheduleAutoRemove）都会与之竞争。
- **复现**: 游戏崩溃 → 崩溃窗点「一键修复」（网络慢使下载持续）→ 同时打开下载页 → 列表刷新/应用崩溃或列表乱序。
- **级别**: 高

## 中

### B2 任务 Completion 在「首次失败」即完成，早于自动重试耗尽 → 安装失败弹窗/历史记录误报
- **文件**: src/Launcher.Core/Download/DownloadTask.cs:220-226（叶子 finally）、:300-304（组 finally）、:325-347（ScheduleAutoRetry）
- **问题**: `finally { if (!_suspendRequested) _completionTcs.TrySetResult(); }` 在失败后立即完成 TCS，而自动重试（800ms/3s 延迟后重跑）还在排期。等待 `task.Completion` 的调用方（EcosystemViewModel.InstallCard:567、VersionBrowseViewModel.Repair:591、ModRepairFlow:38）会在第一次失败时立刻返回，此时 `task.State == Failed` → 弹 AL69「安装失败 + 打开下载页」窗、Toast 失败、下载历史记失败、`_returnTo` 跳回——而自动重试几秒后成功，用户被无意义地引导去手动下载。附带：ScheduleAutoRemove（DownloadManager.cs:148-157）在 t0+3s 检查 State——若第二次失败恰在 3s 内且第 2 轮重试延迟 3s，任务会在重试开始前被移出队列，此后下载在队列不可见。
- **复现**: 网络抖动 → 叶子/组任务首次失败（可自动重试类）→ 弹「安装失败」窗同时任务自动重试成功。
- **级别**: 中

### B3 LoadFavoritesAsync 循环内 Cards 修改无 seq 守卫 → 旧收藏条目污染新搜索结果
- **文件**: src/Launcher.App/ViewModels/EcosystemViewModel.cs:479-516（Cards.Clear() 在 :482，循环内 Cards.Add 在 :496/:503，seq 检查只在 :508 末尾）
- **问题**: 收藏模式逐项目顺序拉详情（每个一次网络请求，慢）。期间用户切换筛选/来源/实例触发新搜索（新 RunSearchAsync seq 更大），新结果先 Cards.Clear+Add 完成；随后旧收藏循环仍继续 Add——旧卡片混进新结果列表，最终列表=新结果+残留旧收藏。
- **复现**: 收藏 5+ 个项目 → 进入收藏模式（加载中）→ 立即切到普通搜索 → 列表出现新旧混合卡片。
- **级别**: 中

### B4 CheckNetworkAsync 并发点击 → NetworkStatus 新旧结果交错/重复
- **文件**: src/Launcher.App/ViewModels/DownloadViewModel.cs:50-68
- **问题**: 命令无 CanExecute 守卫；两次点击后两个协程都从 UI 线程执行 Clear/Add，在 `await ProbeHttpAsync` 处交错——A.Clear → B.Clear → A.Add(mojang) → B.Add(mojang)…最终列表 6~12 条、新旧混合（旧请求结果覆盖新请求）。`CancellationToken.None` 也无法通过取消阻止旧结果。
- **复现**: 网络诊断进行中连点两次「检查网络」→ 列表出现重复/混合条目。
- **级别**: 中

### B5 「全部」双源模式下中文查询不走 MC百科链 → 中文搜索静默退化
- **文件**: src/Launcher.App/ViewModels/EcosystemViewModel.cs:384-412（RunBothSearchAsync 的 mrTask 直调 `_eco.SearchAsync`，无 ContainsChinese 分流；分流只存在于 :352 RunMrSearchAsync）
- **问题**: 中文 query（如「遗落荒野」）+ 来源=全部 → Modrinth 侧普通搜索 0 命中（索引是英文），只显示 CurseForge 结果；切到单源 Modrinth 才走中文链。行为与注释声称的中文分流不一致，中文用户被默认源（Modrinth 单源）外的手动切换迷惑。
- **复现**: 搜索框输入中文 → 来源选「全部」→ 无 Modrinth 结果（切单源却有）。
- **级别**: 中

### B6 ImageLoader 忽略调用方 CancellationToken + 大图失败毒化小图缓存
- **文件**: src/Launcher.App/Services/ImageLoader.cs:26-47（LoadAsync 的 ct 从未传入 DownloadAsync；:44 catch 无条件 `Cache[url] = null`）
- **问题**: ① 调用方取消（tab 切走、视图销毁）后下载仍继续跑满 8s 超时/完成，浪费连接与电量；缓存的 Task 由首个调用者的生命周期决定，后续调用者无法取消共享任务。② decodeWidth>96 分支（如 ProjectDetailViewModel:105 的 640px 画廊图）下载失败时，catch 把 96px 小图缓存也置 null——同一 URL 的小图标此后永远返回 null（直到重启），与「失败缓存 null」的设计意图（只毒化对应尺寸）不符。
- **复现**: ① 快速切换 tab 时后台仍发起图片请求；② 画廊 640px 图网络失败后返回列表，小图标同 URL 变空白且不再重试。
- **级别**: 中

## 低

### B7 MC百科搜索：详情页顺序拉取无单条超时预算 + slug 切片不截断查询串
- **文件**: src/Launcher.Core/Services/McmodSearchService.cs:53-77（SearchSlugsAsync）、:39-50（DecodeModrinthSlug）
- **问题**: ① 每条详情 `GetStringAsync` 用 HttpClientPool.Create() 的 15s 超时，10 条顺序拉取最坏 150s，中文搜索期间 UI 一直转圈；无单条快速失败。② `url[(idx+5)..]` 不截断 `?`/`#` 后缀（`/mod/slug?tab=files` 会得到 `slug?tab=files`），非法 slug 整条被跳过。
- **复现**: ① 某条详情页挂起 → 中文搜索卡 15s×N；② mcmod 外链带参数 → 该条静默丢失。
- **级别**: 低

### B8 ThirdPartyDlSourceResolver.ToGhapiPath 无界检查 → 畸形 URL 抛 IndexOutOfRange
- **文件**: src/Launcher.Core/Download/ThirdPartyDlSourceResolver.cs:33-40
- **问题**: `seg[4]`/`seg[1]` 直接下标。URL `https://github.com/o/r/releases/download/`（去空段后仅 4 段）→ IndexOutOfRangeException 从 Resolve() 抛出 → 该下载任务直接失败（错误文案「索引超出范围」），而不是换源/失败保底。第三方下载是自由文本输入 URL，用户可轻易粘出这种畸形串。
- **复现**: 第三方下载粘贴 `https://github.com/owner/repo/releases/download/` → 任务失败且无换源。
- **级别**: 低

### B9 组任务编排抛错时已创建的子任务不取消，继续后台下载
- **文件**: src/Launcher.Core/Download/DownloadTask.cs:288-299（RunGroupAsync catch 分支无 `Children` 级联 Cancel；只有 :245-251 的 failed 分支做了）
- **问题**: groupWork 中途抛错（如清单解析失败）时，之前 AddChild 的子任务仍在下载（FileLocks 同目标序列化避免写坏，但浪费带宽/IO），随后自动重试新建同名子任务继续下载，旧任务事件还挂在父级 PropertyChanged 上反复触发 RecomputeAggregate（空转）。重试后旧下载完成也会与历史记录/Toast 语义混淆。
- **复现**: 版本下载编排在挂载部分子任务后抛错 → 组失败/重试时旧子任务仍在跑。
- **级别**: 低

### B10 AttachChild 的取消注册永不释放 → 组任务/重试累积引用泄漏
- **文件**: src/Launcher.Core/Download/DownloadTask.cs:392（`child._externalCancellations.Add(_cts.Token.Register(child.Cancel))`，列表只增不清、Registration 不 Dispose）
- **问题**: 每个子任务持有父 _cts 的注册直到父被 GC；组任务 Retry/Resume 反复 Clear+新建子任务后，旧注册与旧 CTS 链无法提前回收。量小但无限累积。
- **级别**: 低

### B11 Repair 的 IsDownloading 守卫在 Confirm await 之后 → 双击弹双对话框、双下载
- **文件**: src/Launcher.App/ViewModels/VersionBrowseViewModel.cs:561-571
- **问题**: `if (IsDownloading) return;` 在 `await DialogService.Confirm` 之前；双击「重新下载」→ 两个 Confirm 都通过守卫 → 两个确认框 + 两个修复任务入队。同文件 CheckIntegrity（:620-623）先置 flag 无此问题。
- **复现**: 快速双击「重新下载」按钮。
- **级别**: 低

### B12 VerifyFilesAsync 的 FileLen 与 File.Exists 之间文件被删 → 质检抛错
- **文件**: src/Launcher.Core/Diagnostics/AutoRepairService.cs:130-134
- **问题**: `present` 列表在存在性检查时收集，随后 `FileLen(e.Path)` 读长度——期间文件被外部删除/移动 → FileNotFoundException 冒泡成「检查失败」（CheckIntegrity catch 显示），而实际只是竞态。
- **复现**: 质检运行中用户/杀软删掉某库文件。
- **级别**: 低

### B13 ghapi 签名直链进入 SourceStats → 统计表按 URL 膨胀、竞速排名失真
- **文件**: src/Launcher.Core/Download/DownloadService.cs:405（`_sourceStats.RecordSuccess(url, ...)` 记录的是换链后的签名 URL）
- **问题**: 签名 URL 每 30 分钟变化一次，同一文件多次下载会以不同 URL 反复登记成功/失败记录，SourceStats 表无限膨胀，且历史速度无法命中同源（换链后算「新源」）。
- **级别**: 低

### B14 GitHubApiDirect 缓存无逐出 + 302 链响应体不读直接 Dispose
- **文件**: src/Launcher.Core/Download/GitHubApiDirect.cs:30、:62-64
- **问题**: ① ConcurrentDictionary 缓存项无逐出/上限（长期运行内存增长，虽小）；② `HttpCompletionOption.ResponseHeadersRead` 拿到最终响应后不读 body 直接 `using` 释放——对 302（无体）无害，但若 GitHub 对某资产直接回 200 带文件体，则该体被丢弃且连接无法回池复用。
- **级别**: 低

---

## 已核查未发现问题（重点关注项）
- ImageLoader 磁盘缓存路径 = sha256(url) 十六进制，无路径注入；并发写盘冲突有 try/catch + 双检，不产生损坏文件。
- MessageDialogWindow.Confirm 在窗口关闭（X/Alt+F4/ESC/owner 关闭）时均通过 OnClosed → TrySetResult(false) 兜底，不会永久挂起；owner 不可见时 ShowDialog 抛异常有独立窗口兜底。
- EcosystemViewModel 常规搜索路径（RunMr/RunCf/RunBoth）的 Cards 修改均有 seq 守卫（除 B3 收藏路径）。
- GitHubApiDirect 30 分钟缓存 vs 签名 1 小时有效期一致；Split('/', 4) 保留含斜杠文件名；DownloadService 每轮重解占位 URL（AL58b）覆盖过期场景。
- DownloadGroupContext.Children 仅在编排线程访问，无跨线程竞态。
