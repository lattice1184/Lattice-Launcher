# 下载引擎代码审查 BUGS（2026-08-11）

审查范围：DownloadService / DownloadOptions / DownloadManager / DownloadTask / VersionDownloadPipeline / VersionInstaller / LoaderService / SlowSourceDetector / AutoRepairService。

---

## 高

### 1. 全局限速与慢速判死冲突：限速 < 100KB/s 时所有下载必然失败
- 文件: src/Launcher.Core/Download/DownloadService.cs:457（单连接）、566（分片 slowWatch）+ src/Launcher.Core/Download/SlowSourceDetector.cs:60-64 + src/Launcher.Core/Download/DownloadOptions.cs:38
- 问题: SlowSourceDetector 阈值固定 100KB/s（SlowSpeedBps），而限速（BytesPerSecond）会把实际吞吐压到设定值。用户设置限速 < 100KB/s（如 50KB/s）时，测得的实时速度恒低于阈值 → 30 秒（6 采样×5s）后抛 SlowSourceException → 整轮重试后下载必然失败。分片路径同样中招（慢速监测的是合并后的 cp.Bytes 总吞吐 ≈ 限速值）。
- 复现: 设置下载限速 50KB/s，下载任意 >256KB 文件，30s 后报「源速度持续低于 100KB/s」并重试至失败；网络本身完全正常。限速恰好 100KB/s 时因抖动也处于临界。
- 严重级别: 高（功能必现，慢速用户全部下载失败；限速检测应考虑限速值，如阈值取 min(100KB/s, 限速×1.2)）

### 2. 安装失败清理误删有效 client jar：修复已装版本时把好文件删掉
- 文件: src/Launcher.Core/Download/VersionInstaller.cs:88-93
- 问题: InstallCoreAsync 的 catch 无条件删除 `versions/{version.Id}/{version.Id}.jar`，注释假设「安装前本不存在」。但修复路径（VersionBrowseViewModel.cs:579「修复」、VersionDownloadViewModel.Repair）对**已安装**版本调用：pipeline 幂等跳过已存在且 SHA1 匹配的 jar，只补缺失库；若补库失败，catch 删掉的却是原本有效的 jar → 修复把完好版本搞坏（json+标记还在、jar 没了）。
- 复现: 已装 1.21.10（jar 完好），手动删一个库文件 → 点「修复」→ 让其中一个库下载失败（断网/源挂）→ 修复报失败，原版 jar 被删，版本变成「缺文件」。
- 严重级别: 高（数据丢失，修复路径使状况恶化；应只删「本次实际下载且原本不存在」的 jar）

---

## 中

### 3. 单连接续传无 206/200 判定：服务器忽略 Range 时 append 拼出错位文件
- 文件: src/Launcher.Core/Download/DownloadService.cs:442 vs 730-734
- 问题: 分片路径显式处理了「服务器忽略 Range 返回 200」→ FileMode.Create 重写防错位；单连接路径（DownloadSingleAsync）无条件 `FileMode.Append`——残留 .tmp 存在时若服务器回 200 全量，会把整个 body 拼在已下内容后面 → 错位文件。有 sha1/size 时下一轮靠校验失败自愈（多浪费一轮），但 url 下载（第三方直链等）若 sha1 与 size 均无，错位文件会被当作成功落盘。
- 复现: 断网留下半截 .tmp → 换一个忽略 Range 的源重试（或代理剥离 Range）→ 文件拼接损坏且无校验时静默通过。
- 严重级别: 中（与分片路径行为不一致，属同源缺陷的半面）

### 4. 组任务「级联取消失败兄弟」是死代码：失败后其余文件仍全部下完
- 文件: src/Launcher.Core/Download/DownloadTask.cs:245-251
- 问题: RunGroupAsync 在 `await Task.WhenAll(ctx.Children.Select(c => c.Completion))`（241 行）之后才检查失败并 `_cts.Cancel()` + `c.Cancel()`——此时所有子任务**已经全部终态**，取消对仍在下载的兄弟无效（pipeline 内部的 WhenAll 同样等全部子任务完成）。注释声称「停止无效下载/写盘」不成立：版本下载中一个库 404，assets 批量（数百 MB、2000 文件）仍会完整下完才报失败。
- 复现: 下载版本时让一个库文件 404，观察 assets 仍在继续下载直到结束。
- 严重级别: 中（行为与注释意图不符，失败后浪费大量带宽与时间；应让 pipeline 监听首个子任务失败即级联取消）

### 5. 组任务编排抛错后要等全部子任务跑完才进入失败态
- 文件: src/Launcher.Core/Download/DownloadTask.cs:240-241
- 问题: groupWork 抛错（如父版本缺失、LoaderService 配置子任务失败）时，241 行仍 `await Task.WhenAll(Children.Completion)`——已创建的子任务（下载中的大文件）全部跑完（或自然失败）后组才亮失败，错误报告延迟数分钟。
- 复现: 组内配置阶段失败但已建 assets 子任务 → 组任务卡「下载中」直到 assets 下完。
- 严重级别: 中

### 6. LoaderService 组路径：配置子任务失败后 version=null，NRE 掩盖真实错误
- 文件: src/Launcher.Core/Download/LoaderService.cs:307-324
- 问题: `version` 在子任务 lambda 内赋值；子任务失败（meta.fabricmc.net 拉取失败等）时 Completion 照常完成、lambda 不抛，`version` 保持 null → 324 行 `DownloadVersionAsync(version!...)` 内部 `version.InheritsFrom` 抛 NullReferenceException → 组任务显示「NullReferenceException」而非真实网络错误，诊断信息丢失（AL68 停滞透明化被这层掩盖）。
- 复现: 断网状态下安装 Fabric，报错为 NRE 而不是网络失败。
- 严重级别: 中（错误掩盖；应在 Completion 后检查子任务 TerminalState 并抛其 Error）

### 7. Retry 双击竞态：State 经 Post 异步生效，两次 Retry 并发跑同一任务
- 文件: src/Launcher.Core/Download/DownloadTask.cs:349-360
- 问题: Retry() 的守卫 `if (State != DownloadTaskState.Failed) return;` 读的是异步 Post 生效的 State——快速双击（或失败瞬间连点）时两次都通过守卫 → 两个 RunAsync 并发执行同一 work：双倍网络请求、进度/速度互踩、两个 TerminalState 写入。Resume() 有 `_suspendRequested` 守卫无此问题，Retry 没有等价守卫（_autoRetryCount 也不保护）。
- 复现: 失败的任务快速双击「重试」，观察同文件被并发下载两次。
- 严重级别: 中

### 8. natives classifier 库不参与完整性校验（质检缺口）
- 文件: src/Launcher.Core/Diagnostics/AutoRepairService.cs:102-110
- 问题: VerifyFilesAsync 只按 `MavenPath.FullPath(lib.Name)` 校验 artifact 路径；natives（`lib.Downloads.Classifiers["natives-windows"]`，下载到 libraries/{dir}/{name}-natives.jar）缺失/损坏完全不查。下载与修复都下载 natives（DownloadService.cs:938-954、Pipeline 93-104），但「质检：N/N 文件完整」不含它们 → 质检通过但启动时解压 natives 失败。
- 复现: 删掉某版本 natives jar（libraries 下 -natives-*.jar）→ 修复报告「文件已完整，无需修复」→ 启动解压 natives 报错。
- 严重级别: 中

### 9. 竞速赢家落盘异常路径：不取消其余源、不清理、异常裸抛
- 文件: src/Launcher.Core/Download/DownloadService.cs:314-329
- 问题: `File.Move(race{idx} → destPath)`（318 行）若抛异常（杀软锁文件/目标被占用），直接跳出 while 循环上抛——`raceCts.Cancel()` 与 straggler 收尾都没执行，其余竞速源变成僵尸下载（继续把整份文件下到 .race{i}，直到用户取消整个任务）；且 `using var raceCts` 退出时 Dispose 掉已取消的源 token 之后，残留源再 CreateLinkedTokenSource 会抛 ObjectDisposedException（被各自 catch 吞掉，但下载已失控）。
- 复现: 竞速期间杀软/占用 destPath → 赢家 Move 失败 → 观察镜像源继续下载完整个文件。
- 严重级别: 中（失败路径缺统一收尾；Move 应在 try 内，失败也 cancel+清理）

### 10. GetContentLengthAsync 竞速下重复 HEAD 全源列表 + 吞用户取消
- 文件: src/Launcher.Core/Download/DownloadService.cs:792-808
- 问题: 每个竞速源内部都 `_resolver.Resolve(url)` 全量候选并逐个 HEAD（每源 8s 限时）；expectedSize 缺失的竞速场景下 N 个源各自串行 HEAD 全部候选。`catch (Exception)` 同时吞掉用户取消的 OCE（取消仅延迟：后续 SendAsync 立即抛）。
- 复现: 无 size 元数据的多源直链下载，竞速启动前每源最多等 N×8s。
- 严重级别: 中（性能/取消语义瑕疵，expectedSize 常有时无感）

---

## 低

### 11. per-source CancellationTokenSource 从不 Dispose + 竞速残留文件最后轮后永久滞留
- 文件: src/Launcher.Core/Download/DownloadService.cs:277、264
- 问题: pending 元组里的 srcCts 在任务完成后被丢弃，从不 Dispose（linked CTS 需 GC 兜底回收）；.race{i}/.parts 残留只在下一轮开头清理（264 行）——最后一轮失败后残留文件永久留在磁盘，同路径再次下载时才清。
- 严重级别: 低

### 12. 416 重试泄漏第一个 HttpResponseMessage（连接不回收）
- 文件: src/Launcher.Core/Download/DownloadService.cs:485-491
- 问题: 416 分支里第一个 response 只从异常取 StatusCode，response 对象不可达且未 Dispose——共享连接池的这条连接延迟回收（HTTP/2 连接数被占）。
- 严重级别: 低

### 13. 后台 straggler 收尾用阻塞 Task.Wait() 占池线程
- 文件: src/Launcher.Core/Download/DownloadService.cs:322-326
- 问题: fire-and-forget Task.Run 内 `p.Task.Wait()` 阻塞线程池线程直到输家停止；输家取消传播路径基本及时（throttle/读循环都观察 race 取消），但大文件慢取消时池线程被占，多文件竞速下可堆积。
- 严重级别: 低（应改 async await）

### 14. DownloadTask 的 CTS/注册永不释放
- 文件: src/Launcher.Core/Download/DownloadTask.cs:29、392
- 问题: `_cts` 从不 Dispose（每次 Retry/Resume 换新实例，旧的直接丢弃）；`_externalCancellations` 的 CancellationTokenRegistration 在 Children.Clear 后也不 Dispose——重试多次的组任务累积注册项（GC 可达但延迟回收）。
- 严重级别: 低

### 15. FileLocks 并发字典只增不减
- 文件: src/Launcher.Core/Download/DownloadService.cs:19、185
- 问题: 每个 destPath 一个 SemaphoreSlim，完成任务后不移除——模组包安装等大批量下载会话中字典持续增长（条目极小，无功能影响）。
- 严重级别: 低

### 16. 分片断点续传跨轮 chunkSize 变化时部分残留错位（sha1 兜底自愈）
- 文件: src/Launcher.Core/Download/DownloadService.cs:718-728、632-635
- 问题: {i}.part 残留按文件名续传，但 ramp-up 探测（AL60）每轮可能给出不同 chunkCount → 新旧 chunkSize 不同时同序号的 part 覆盖不同字节区间，续传拼接错位；合并长度校验查不出（长度碰巧对），只有终 SHA1 能拦——无 sha1 的文件会损坏落盘。
- 复现: 大文件首轮 8 片失败留部分 part → 换源后探测判定 4 片 → 续传错位。
- 严重级别: 低（sha1 普遍存在时自愈；建议续传前校验 part 起点与本次 chunk 边界一致）

---

## 已核查未发现问题的点（防重复审查）

- SemaphoreSlim 与取消组合：DownloadManager._gate / libGate / assetGate / fileLock 全部是「WaitAsync 在 try 外，Release 在 finally 内」——取消时未获锁不 Release，无信号量泄漏。
- 阻塞调用清理：VerifyFilesAsync 已 WhenAll 化；全 Core 项目仅剩 DownloadService.cs:324（后台任务，见 13）与 Ecosystem/Multiplayer 两处（不在本次改动范围）。
- 竞速淘汰评估（AL59）：evalDelay 与源任务在 WhenAny 上的判定、领先源重评、赢家唯一性均无竞态；用户取消时源任务全部观察 ct 及时退出，无死循环。
- 心跳（ReadWithStallAsync）与 throttle 的交互：throttle 每 64KB 块最大延迟（下限 8KB/s → 8s/块）远小于 30s 心跳窗口，无假阳性。
- [flaky 8-16] PaceRunnerTests.Takeover_OvertakerStableLead_DethronesWinner：全量并发跑时偶发「进度回退 145285→141762」（6s 挂）；单跑 12/12 稳定——250ms 陪跑节拍 + 段表脚本时序，机器负载抖动触发。疑似 RaceProgress.Wrap 单调转发断言对时钟敏感
- [flaky 8-16] FixedChunkResumeTests.RetryAttempt_ReusesCompletedChunks：全量并发跑时偶发「探测次数 Expected 3 / Actual 2」——判死窗口 100ms 级时序 + 全量机器负载抖动；单跑 2/2 稳定。与 PaceRunner flaky 同族（慢速判死时序敏感）

---

# 8-22 CF 匹配链路 + 下载引擎竞态定向扫描（git 71b01b8..HEAD）

审查范围：CurseForgeService / EcosystemDependencyAdapter / ProjectDetailViewModel / ProjectDetailView.axaml（今日改动）+ DownloadTask（R-01 代际）/ DownloadGroupContext / FileConfigStorage（已知风险点复核）。

## 高

### 17. Suspend→Resume 后旧执行泄漏：旧 RunAsync 的 catch/finally 未被代际守卫，OCE 迟达时完成 TCS 并幽灵重跑
- 文件: src/Launcher.Core/Download/DownloadTask.cs:218-227（旧 run 第二 catch）、247（叶子 finally）、333（组 finally）、341-347（Suspend）、434-446（Resume）
- 问题: R-01 代际守卫只保护「已排程的自动重试」（ScheduleAutoRetry 内 `_retryGeneration != gen` 作废），不保护**正在执行的旧 run**。Suspend() 取消 `_cts` 后，若用户迅速 Resume（`_suspendRequested=false`、`_cts` 换新、Run2 启动），旧 run 的 OCE 才到达 catch：`catch (OCE) when (_cts.IsCancellationRequested)` 读到的已是**新** cts（未取消）→ 落入第二 catch（AL34 分支）→ 误判「token 未被请求的中断」→ SetState(Failed) + ScheduleAutoRetry → 排程的 Retry 在 gen 校验后照常执行（gen 快照取的是 Resume 后的值）→ Run3 与 Run2 并发下载同一文件（双写）；同时旧 run finally 因 `_suspendRequested=false` 且（Run2 尚在跑时）`_retryPending` 未置位 → `_completionTcs.TrySetResult()` 提前完成 → 调用方（ExecuteInstallAsync `await task.Completion`、`child.Completion.WaitAsync`、自动移除/历史记录）在下载仍在进行时就收尾。组任务同理（333 行）：编排器用无 token 的 WhenAll 等子任务时（VersionDownloadPipeline 2000 assets 场景），Suspend 后组 run 永远挂起，Resume 后旧 run 变僵尸续延。
- 复现: 叶子下载中快速「暂停→继续」（OCE 传播晚于 Resume 即可，网络读块间隙/写盘期间最易触发）→ 状态被旧 run 覆写为「失败」且进度仍在走，或提前完成；组任务 2000 文件下载「暂停→继续」循环多次后出现悬挂的组任务。
- 严重级别: 高（与 R-01 要防的幽灵重跑同根因，代际守卫覆盖面不全；建议 run 开始时快照 gen，catch/finally 前校验 `gen != 当前` 则直接 return 不碰共享状态）

### 18. LoadCfAsync 无实例切换守卫（REVIEW-C 只修了 Modrinth 路径，CF 路径漏了）
- 文件: src/Launcher.App/ViewModels/ProjectDetailViewModel.cs:294（await GetFilesWithFallbackAsync 用 default ct 不可取消）、275-342（LoadCfAsync 整体）
- 问题: Modrinth 路径 LoadAsync:232 有 `if (!ReferenceEquals(_instance, captured)) return;` 守卫，CF 路径完全没有：LoadCfAsync 开头没捕获 `_instance`，请求也传 `default` 取消令牌。UpdateContext 切换实例后，旧实例的在途请求完成后照常写 `_cfFile`/`_cfGameVersion`/`VersionHint`/`CanInstall`/MatchedFileText → 与新的 `_instance` 错配 → 「安装」把旧实例的变体（fabric/neoforge、旧游戏版本）装进新实例目录，正是 REVIEW-C 要防的「装错目录」；且旧实例的 RefreshCfDependenciesAsync 也会以旧 gameVersion/loader 覆盖 DependencyHint。
- 复现: 详情页先选 Fabric 实例（加载中），切到 NeoForge 实例（加载中），若 Fabric 的 CF 响应后到 → 页面显示/可装 Fabric 变体而 _instance 是 NeoForge。
- 严重级别: 高（同类竞态已在 Modrinth 路径修过，CF 路径未对齐）

## 中

### 19. loader 参数在依赖解析主路径是死参数：GetFilesWithFallbackAsync 的 loader 从未使用，CF ToFile 置 Loaders=[]，解析器选中的变体绕过滤
- 文件: src/Launcher.Core/Services/CurseForgeService.cs:198-207（loader 形参未在函数体引用）、309-310（id 直查命中则 SelectBestFile(loader) 兜底不执行）；src/Launcher.Core/Ecosystem/EcosystemDependencyAdapter.cs:87、109（`Loaders = []`）
- 问题: 本次 commit 声称「依赖也要按加载器过滤」，实际链路是：resolver（CreateResolver→GetFilesWithFallbackAsync 返回**未过滤**文件列表）→ `ToFile` 置 `Loaders=[]`（resolver 的 IsCompatibleFile 对 Loaders.Count==0 直接放行）→ resolver 按 releaseType/ReleaseDate 选中变体 → InstallWithDependenciesAsync:309 `files.FirstOrDefault(f.id == dep.File.Id)` 必命中（同源同列表）→ loader 过滤的 SelectBestFile 只在 id 查不到时才执行。即双加载器依赖（malilib 等）从解析到安装全程无 loader 过滤，与 8-22 注释宣称相反。
- 复现: Fabric 实例装 tweakeroo（依赖 malilib）：malilib 的 neoforge 变体 fileId/release 较新时被 resolver 选中并安装进 fabric 实例 → 启动报错。
- 严重级别: 中（本轮修复的核心目标未生效；应在 resolver 侧过滤——如 ToFile 时按 loader 剔除敌对变体，或 GetFilesWithFallbackAsync 内先 SelectBestFile）

### 20. IsCompatibleWithLoader「forge」分支恒放过 neoforge 变体（子串自吞）
- 文件: src/Launcher.Core/Services/CurseForgeService.cs:496
- 问题: `"forge" => !hasFabric && !hasQuilt && (!hasNeo || hasForge)`——任何 neoforge 文件名都含子串 "forge"（hasForge 恒真）→ `(!hasNeo || hasForge)` 恒真 → forge 实例的过滤退化为只排 fabric/quilt。NameMentionsLoader（481 行）同样把 "neoforge" 名当 "forge" 排最前 → forge 实例会选中并安装 neoforge 变体（运行时必崩），与注释「先判长词」意图相反。测试只覆盖了 fabric/neoforge 目标，forge 分支无测试。
- 复现: Forge 实例打开 JEI（双加载器）：SelectBestFile 选中 jei-*-neoforge.jar 而非 forge 变体。
- 严重级别: 中（应与 neoforge 分支对称：`hasForge && !hasNeo` 才放行）

### 21. RefreshCfDependenciesAsync：catch 空实现使提示永久卡「正在查询前置依赖…」；m==-1（未知）也被当成「无需前置依赖」
- 文件: src/Launcher.App/ViewModels/ProjectDetailViewModel.cs:355-357
- 问题: ① catch 是空的，注释写「查询失败按无依赖处理」但什么都没做——GetFileAsync/网络异常时 DependencyHint 永远停在 311 行设的「正在查询前置依赖…」，后续安装确认弹窗（821-825 行）会把这个占位文案当依赖提示展示；② 355 行 `DependencyHint = m == 0 ? "无需前置依赖" : "无需前置依赖";` 是恒真式——CountModrinthRequiredDepsAsync 返回 -1（搜不到/网络失败=未知）时也显示「无需前置依赖」，用户被误导为确认无依赖。
- 复现: 打开任意 CF mod 详情页后断网 → 提示卡「正在查询前置依赖…」永不变；弱网下安装弹窗显示占位文案。
- 严重级别: 中（状态不一致 + 误导；catch 应复位为「依赖未知」文案，m<0 应单独处理）

### 22. 跨源兜底无法区分「依赖数据缺失」与「本就无依赖」：零依赖 CF mod 全部静默改走 Modrinth 源
- 文件: src/Launcher.App/ViewModels/ProjectDetailViewModel.cs:797-818
- 问题: 触发条件 `(file.dependencies ?? []).Count(d => d.relationType == 1) == 0` 对「CF 真没依赖数据」与「这个 mod 本来就没前置」不可区分 → 所有零依赖 CF mod（Sodium 等）安装时都会按标题搜 Modrinth 并装 Modrinth 版，弹出误导性通知「CurseForge 无依赖数据」；搜索按 `limit:1` 只取第一条且**无项目身份校验**（无 slug/id 匹配、无用户确认）——同名不同 mod（如 CF 独有或重名项目）会被直接装进实例，且绕过 includeDeps 依赖确认弹窗、绕过 CF 侧用户所选文件。
- 复现: 搜索页打开任意无前置的 CF mod 详情 → 安装 → 通知「已改用 Modrinth 源安装」；若 Modrinth 同名搜索第一位是重名 mod → 装错 mod。
- 严重级别: 中（建议：仅当 CF 单文件详情端点也返回空依赖**且** Modrinth 命中项目标题/作者强匹配时才兜底，或至少弹确认）

### 23. InstallVersion 选中版本行后，匹配文件块与 DependencyHint 不刷新（显示 A、下载 B）
- 文件: src/Launcher.App/ViewModels/ProjectDetailViewModel.cs:480-501（InstallVersion）、138-152（块属性）
- 问题: 用户点版本列表任一行后 `_cfFile`/`_matchedVersion` 被换成新选中项，但 MatchedFileText/HasMatchedFile/MatchedDownloadState 不更新（块仍显示自动匹配的旧文件），而 DownloadMatchedFile（382 行）读 `_cfFile`/`_matchedVersion` → 块显示文件 A、点「下载」实际下载 B；同时 RefreshCfDependenciesAsync 不会对新选择重跑 → DependencyHint（含安装确认弹窗）仍是旧文件的数据。
- 复现: 详情页匹配文件 A → 版本列表点选 B → 匹配文件块仍显示 A → 点「下载」→ 下载的是 B，状态文案与块内容互相矛盾。
- 严重级别: 中（UI 状态不一致；应在 InstallVersion 中同步刷新块文本并按需重跑依赖查询）

### 24. 搜索页 CF 一键安装（EcosystemViewModel）未接 loader 与依赖详情补查——本轮修复只覆盖详情页
- 文件: src/Launcher.App/ViewModels/EcosystemViewModel.cs:728（FindBestFileAsync 无 loader 参数）、744（InstallWithDependenciesAsync 传 loader=null）
- 问题: 同一「安装 JEI」动作，详情页今天修了 loader + 依赖补查，搜索页卡片路径全没修：① FindBestFileAsync 内 SelectBestFile 不带 loader → 双加载器 mod 从搜索页装仍可能选中 neoforge 变体（fabric 实例）；② depCount 取自列表响应的 `file.dependencies`（恒空数组，8-22 实测）→ 依赖确认弹窗永远不弹、InstallWithDependenciesAsync 的 resolver 输入 ToDependencyReferences(file) 为空 → malilib 等前置永远不会被解析安装。
- 复现: 搜索页直接点 CF 卡片的「安装」（不进详情页）→ 有前置依赖的 mod 装完缺前置、双加载器 mod 装错变体。
- 严重级别: 中（与详情页行为不一致，属本轮修复遗漏的调用点）

## 低

### 25. DownloadMatchedFile 的 Modrinth 分支 fileName 未过 Path.GetFileName（CF 分支有）
- 文件: src/Launcher.App/ViewModels/ProjectDetailViewModel.cs:385（CF 分支已 sanitize）vs 393（Modrinth 分支原样取 f.FileName）、399（Path.Combine）
- 问题: CF 分支 `Path.GetFileName(cf.fileName)` 剥掉了目录成分，Modrinth 分支却直接 `fileName = f?.FileName` → 文件名含路径分隔符或 `..` 时 `Path.Combine(InstallDir, "downloads", "mods", fileName)` 可逃逸到下载目录外覆盖任意文件（Modrinth 文件名虽通常受限，但不应依赖远端约束）；CF 分支文件名含 Windows 非法字符（`<>:"|?*`）时 Path.GetFileName 不剥离 → 下载直接 IOException「下载失败」，与 MR 分支行为不一致。
- 复现: 恶意/异常文件名（`..\..\evil.jar` 或 `a<b>.jar`）的版本文件点「下载」→ 路径逃逸或落盘失败。
- 严重级别: 低（加固项；两分支统一 sanitize：Path.GetFileName + 非法字符清洗）

---

## 已复核未发现问题的点（防重复审查）

- FileConfigStorage.cs（PCL.Core/App/Configuration/Storage/FileConfigStorage.cs）：Get 的清理分支已在读锁外（146-163 行先 ExitReadLock 再入队/写锁），读锁内无写树路径；写线程 Sync 与 Get 读锁互斥成立，无双锁死锁路径；Unbounded channel TryWrite 不失败，兜底写锁分支也无嵌套锁。复核结论：无死锁风险。
- DownloadGroupContext.cs FirstFailure：子任务 IsGroupChild=true 不自动重试 → 失败即终态，PropertyChanged(State==Failed) 与 TCS 同步完成双路径都能唤醒组任务；级联取消（RunGroupAsync 272-276）覆盖了「父已取消、子后创建」（AttachChild 的 externalCancellations 注册）时序。复核结论：首败早退完整。
- CF hashes 数组反序列化修复（CurseforgeFile.cs hashes → List?）与 `InstallAsync:236` 的 FirstOrDefault 取 SHA1：类型与用法一致，测试（FilesResponse_WithArrayHashes_Deserializes）覆盖；单对象旧格式的存量缓存无（响应不缓存）。
- GetJsonAsync 200+非 JSON 重试（CurseForgeService.cs:382-386）：attempt==0 时 500ms 后重试一次，`using var resp` 每轮释放，无泄漏；重试路径正确再抛。
- ProjectDetailView.axaml 新绑定：`!IsDownloadingMatched`（Avalonia 11 支持）、`DependencyHint.Length` 空串不可见——绑定目标存在，无 NRE 路径。
