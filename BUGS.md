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
