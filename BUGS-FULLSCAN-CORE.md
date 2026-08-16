# BUGS-FULLSCAN-CORE — Launcher.Core + PCL.Core 逻辑缺陷排查（2026-08-16）

范围：下载引擎 / 版本安装链 / 生态服务 / 账号 / DPAPI 存储 / 配置存储 / 任务系统。
只报逻辑缺陷（竞态、边界、泄漏、状态不一致、静默失败、死锁风险、并发安全），不含风格/命名/性能微优化。

## 严重

- PCL.Core/App/Configuration/Storage/CommonFileProvider.cs:17-30 + JsonFileProvider.cs:59-88 + YamlFileProvider.cs:72-97 + ConfigStorage.cs:64-73 — 配置内存树无锁并发访问：后台写线程 `Sync()` 序列化 `_rootElement`（JsonObject/YamlMappingNode）的同时，任意调用线程可并发 `Get`/`Set` 直接读写同一棵树（Set 还可能在 Get 失败回退时写树）；并发枚举+修改抛出的 `InvalidOperationException` 不在 FileConfigStorage.Get 的 catch 白名单（FileConfigStorage.cs:110-116）里，一路逃到 `ConfigStorage.Access` 的 catch → `Lifecycle.ForceShutdown(-2)` 整个应用退出 — 复现：下载/安装进行中（后台线程写配置，如切下载限速档）同时 UI 线程读某配置项，进程直接退出；即使不抛异常，Sync 也可能序列化出被并发改坏的树 → 配置落盘错乱。

## 中等

- src/Launcher.Core/Download/LoaderService.cs:383-385 — 组任务路径下「加载器配置」子任务失败后 `version` 仍为 null，`DownloadVersionAsync(version!, ...)` 抛 NullReferenceException，把真实错误（meta 拉取失败）吞成「未将对象引用设置到对象的实例」— 复现：Fabric/Quilt 安装时 meta.fabricmc.net 拉 profile json 失败（国内常见超时），组任务最终报 NRE，错误原因与自动修复提示全部丢失（子任务 `Completion` 永不抛是根因，同文件 434 行注释已承认该语义但本路径漏查状态）。

- src/Launcher.Core/Download/ModpackInstaller.cs:247-253 与 328-332 — 子任务失败被计为安装成功：`await ctx.AddChild(...).Completion.WaitAsync(ct)` 在子任务失败时正常返回（Completion 不抛），随后 `Interlocked.Increment(ref installed)` 照常执行，且失败从未进入 `skipped` 列表 — 复现：mrpack/CF 整合包某模组下载 404/超时，导入报告仍显示「已装 N 个模组」全绿，用户以为装齐了，进游戏才发现缺 mod；CF 兜底路径（328-332）同病。

- src/Launcher.Core/Download/DownloadService.cs:610-632 + 458-460 — 竞速源任务（RaceOneAsync）只过滤 HttpRequestException/InvalidDataException/OCE，其余异常（IOException 共享冲突、UnauthorizedAccessException 等）从 `await (Task<...>)done` 处直接重抛，逃出整轮重试循环 → 整个文件下载立即失败，不换源不重试 — 复现：AbandonDoomed 摘除的挂死源无视取消仍持有 `.race*.parts` 文件（FileShare.None），下一轮同 URL 复用同 key 片集时（345 行注释明说的跨轮复用场景）chunk 打开文件抛 IOException → 本轮竞速主循环崩掉，单连接回退（1203-1210 的 catch）也到不了。

- src/Launcher.Core/Download/LoaderService.cs:424-441 — Forge/NeoForge 安装中用户取消（或暂停），`runChild.TerminalState` 为 Cancelled（≠Failed）不拦截，流程继续 `FindNewestVersionDir()` + 后续 `VerifyInstalledVersionAsync` + `InstallMarker.Mark` — 复现：取消正在跑的 forge 安装器，`FindNewestVersionDir` 可能命中原版父版本目录（或旧的已装目录），把从未被本启动器安装的版本打上 `.yanla-installed` 标记，版本页/安装状态被污染；若命中半成品目录则 verify 报「缺文件」，错误原因与「用户取消了」无关，同样误导。

- src/Launcher.Core/Launch/InstallerProcess.cs:30 — 取消不杀子进程：`await process.WaitForExitAsync(ct)` 抛 OCE 后仅 `using` 释放 Process 对象，java 安装器进程继续运行 — 复现：Forge 安装中点取消，安装器仍在后台写 versions/ 目录；用户立刻重试或装别的版本 → 两个安装器并发写同一目录，装出损坏版本。

- src/Launcher.Core/Download/DownloadTask.cs:341-347 + 434-446 + 417-431 — 组任务 Suspend/Resume（及 Retry）造成新旧执行并发 + 旧执行永久挂起泄漏：Suspend 后子任务 `_suspendRequested=true` → 子任务 finally 跳过 `TrySetResult`（247 行）→ 旧 RunGroupAsync 永远卡在 266 行 `Task.WhenAny`（无任何超时/取消响应）；Resume/Retry 只 `Children.Clear()` 后重跑新的 RunGroupAsync，旧执行的状态机（ctx+旧子任务+事件订阅）永久泄漏，新旧两个组同时下载同一批文件 — 复现：下载中心对进行中的版本安装点「全部暂停」再「全部继续」，此后每次暂停/恢复泄漏一份完整任务图，且新旧执行重复下同一版本。

- src/Launcher.Core/Download/DownloadTask.cs:262-276 — 「首败早退」实际不生效：组任务先 `await groupWork(ctx, _cts.Token)`（262 行），而 groupWork（VersionDownloadPipeline.RunAsync 137 行 `await Task.WhenAll(tasks)`）等的是全部子任务的 Completion，失败子任务的 Completion 会正常完成 → groupWork 必然等所有兄弟（含 2000+ assets）全部跑完才返回；266 行的 WhenAny/FirstFailure 与 272-276 行的级联取消是死代码路径，失败后其余子任务继续白白下载 — 复现：版本下载中一个库 404，其余库与全部 assets 仍继续下载数分钟，组任务最后才转 Failed（注释声称的「不再等 2000 个 assets 全部下完才报错」与实现不符）。

- PCL.Core/App/Essentials/StartupService.cs:144-151 + 71-83 — `_UnhandledCommandMap` 单实例 RPC 回调线程无锁写（Task.Run 内 `_UnhandledCommandMap[command] = model`）与 `TryHandleCommand` 锁内遍历、`UnhandledCommands` 无锁读并发 — 复现：第二个实例启动向主实例 RPC 传参（SingleInstanceService._TryRpc → "REQ cli"）时，若主实例正在 UI 线程处理命令，普通 Dictionary 并发改/读抛 InvalidOperationException → RPC 失败、命令行参数丢失（如更新后跳转、文件关联打开）。

- PCL.Core/App/Configuration/Storage/DynamicCacheConfigStorage.cs:9-35 — `_cache`/`_nullContextCache` 普通 Dictionary 无锁并发访问：任意线程首次访问实例配置都会调 `StorageFactory` 并写 `_cache[context]`，与另一线程的读/`InvalidateCache` 并发 → 字典损坏或异常 → 沿 ConfigStorage.Access 的 catch 触发 ForceShutdown(-2) — 复现：启动游戏（后台线程读实例配置）同时 UI 线程改实例配置/切换实例。

## 轻微

- src/Launcher.Core/Account/AccountService.cs:156-161 — `Logout()` 只清 `Current` 不清 `MicrosoftSession`：登出后 `MicrosoftSession` 仍持有 access/refresh token，HomeViewModel:516 的启动链以它为缓存凭据 — 复现：正版账号登出后切换窗口再切回（Current 非空路径）或任何读 `MicrosoftSession` 的路径，拿到的是已登出账号的 token；删除正版账号同样残留。

- src/Launcher.Core/Download/DownloadService.cs:362 + 554 — 每个候选源的 `srcCts = CancellationTokenSource.CreateLinkedTokenSource(raceCts.Token)` 从不 Dispose：每次竞速每源泄漏一个 CTS 及其对父 token 的注册回调（父 raceCts 已 `using` 释放，子 CTS 使父无法回收），逐文件逐轮累积 — 复现：下载 2000+ assets（每个文件 1-8 个候选源 × 2 轮）后进程内存/句柄持续增长。

- src/Launcher.Core/Download/DownloadService.cs:471-477 — 赢家出现后的后台清扫任务对 straggler 执行无超时 `p.Task.Wait()`：被取消但无视取消的挂死源（watchdog 注释自己承认这种任务存在）使该线程池线程永久占用，且其 `.race*` 残留（CleanupRaceSweep 跳过 straggler 键）永远不会被清 → 磁盘垃圾累积 — 复现：竞速中一个源静默断流且不响应取消，每次下载留下一个僵尸线程 + 10MB 级 .race 残留。

- src/Launcher.Core/Download/DownloadService.cs:21 + 236 — 静态 `FileLocks` 字典只增不减：每个去重 destPath 永久留一个 SemaphoreSlim — 复现：下载中心生命周期内下载过多少不同路径的文件就泄漏多少 SemaphoreSlim（资产安装数千条目）。

- PCL.Core/App/Configuration/ConfigService.cs:199 — `if (!File.Exists(dir))` 用「目录」做条件判断（应为 `!File.Exists(configPath)`）：迁移条件恒真，每次访问实例配置都尝试迁移 `PCL/setup.ini`（目录不存在时从不存在路径读文件，异常被 `_TryMigrate` 吞掉）— 复现：任意实例配置首次访问，日志出现无谓的迁移尝试告警；实例目录确实不存在时行为错误（本应报配置初始化失败却静默继续）。

- src/Launcher.Core/Account/MicrosoftAuth.cs:191-196 — Minecraft profile 请求不检查 `resp.IsSuccessStatusCode`：401/500 返回的 JSON 无 "id" 字段时直接 `profile.GetProperty("error").GetString()`，若错误体无 "error" 键则抛 KeyNotFoundException（错误信息变成「字典中不存在给定键」）；非 JSON 响应（HTML 错误页）直接 JsonException — 复现：正版 token 被微软吊销后点启动，用户看到的是 KeyNotFoundException 而非「请重新登录」。

- PCL.Core/App/Tasks/TaskCenter.cs:39-52 + 92 — 任务状态/进度事件直接在后台线程写 UI 绑定的 `TaskModel.State/Progress/StateMessage`，且 `Register` 的 catch 分支也在 Task.Run 内写 `model.State` — 复现：后台长任务状态变化时，Avalonia 绑定属性跨线程更新（无 Dispatcher 封送），快速连续变化时可能抛线程关联异常或 UI 刷新错乱。

- PCL.Core/App/Configuration/Storage/EncryptedFileConfigStorage.cs:47-52 — DPAPI 解密失败（换 Windows 账户/数据损坏）时 `OnAccess(Get)` 返回 false，调用方一律按「未设置」处理：所有加密配置静默回退默认值，下次 Set 又用新账户密钥覆写 — 复现：换 Windows 用户登录后启动启动器，CF key、GitHub token 等加密设置全部静默消失（账号切换则丢失旧密钥下数据），无任何提示。

- src/Launcher.Core/Ecosystem/EcosystemDependencyAdapter.cs:17-35 + 86-99 — ProjectResolver 用 `.GetAwaiter().GetResult()` 同步阻塞等待网络（Modrinth/CF 请求，15s 超时 × 每依赖）：`ResolveDependencyNamesAsync` 若在 UI 线程调用（依赖提示路径），UI 冻结数秒到数十秒 — 复现：安装含 3-5 个前置的模组、Modrinth 网络慢时，依赖名称提示/安装编排阶段 UI 卡死。
