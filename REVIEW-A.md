# REVIEW-A：版本生命周期用户旅程走查（2026-08-11）

走查路径：主页（HomeViewModel）→ 版本页（VersionBrowseViewModel/VersionManageViewModel）→ 下载页（VersionDownloadViewModel）→ 加载器安装（LoaderService/InstallerProcess）→ 启动（GameLaunchService/JavaArgumentsBuilder/JavaSelector/LaunchProcess）→ 崩溃诊断（LogDiagnostics/CrashReportWindow/ModRepairFlow）→ 删除/备份/导出（MrpackExporter）。

## 已复核清单（各一句结论）

- **BUGS#2 修复：已验证正确**。VersionInstaller.InstallCoreAsync（VersionInstaller.cs:81-99）先记录 `jarExistedBefore`，catch 只删「本次新建」的 jar，修复路径（对已装版本补库失败）不再误删原本有效的 jar。
- **B2：仍存在**。DownloadTask 的 Completion（DownloadTask.cs:225/303 finally 无条件 TrySetResult）在自动重试**第一次失败时**就完成，早于重试耗尽；所有 `await task.Completion` 调用点（VersionDownloadViewModel.cs:262、VersionBrowseViewModel.cs:591、ModRepairFlow.cs:38、LoaderPickerViewModel.cs:120、AutoRepairService.cs:69、DownloadManager.cs:79-82 自动移除）都在首败时收尾：弹失败 Toast/红字，后台重试若成功则版本实际已装好但 UI 永远停在失败（且不跳回版本页、不刷新列表、无成功提示）。
- **B11：仍存在**。VersionBrowseViewModel.Repair 的守卫（VersionBrowseViewModel.cs:563）在 Confirm await 之前、IsDownloading 置位之前——双击会两次通过守卫、弹两个确认框，双确认后**两个并发修复组同时入队**（同一版本重复下载同一批文件）。DownloadDetailVM.Repair（VersionDownloadViewModel.cs:193）同构，但其 DownloadCoreAsync.cs:234 有二次守卫，后果仅是多弹一个框。
- **BUGS#8：部分修复，遗留面 = 旧式 natives**。VerifyFilesAsync 现在按 `MavenPath.FullPath(lib.Name)` 全量查库（含新式 4 段 `group:artifact:version:natives-windows` 条目，路径含 classifier，正确覆盖）；但旧式（≤1.12.2，`natives` 字段映射、name 3 段）条目校验的是**不含 classifier 的路径**，与实际落盘文件名不符 → 详见新报第 6 条。
- **C1-C5：已覆盖无问题**。FileNotFoundException → 修复指引 + 自动重下（HomeViewModel.cs:496-521）；ParentVersionMissingException → 自动重下（FixRedownloadAsync 递归补父）；Java 未配 → InvalidOperationException 提示去设置页（GameLaunchService.cs:68-69）；路径净化/rules 过滤/classpath 拼装已有防死守（探索代理复核过）。

## 新发现问题

### 1. **Launcher.App/ViewModels/HomeViewModel.cs:448,577 | 高 | `_userStopped` 永不重置：停过一次后，本次会话内后续崩溃全部被误报为「已停止」**

`StopGame()` 把 `_userStopped = true`（577 行），全文件/全工程无任何一处重置为 false。启动退出判断（448 行）`if (_userStopped)` 优先于 `code != 0` 崩溃分支——一旦本会话内用户停过一次游戏（或运行中误点停止），**之后任何一次启动的游戏崩溃都走「已停止游戏」分支**：不弹 CrashReportWindow、不触发自动修复、启动历史记 `Stopped` 而非 `Crashed`，用户完全看不到崩溃证据。

复现：启动游戏 → 点停止 → 再次启动（或先点停止再启动）→ 游戏崩溃退出 → 主页显示「已停止游戏」灰字，无崩溃弹窗无诊断。修复应在 `LaunchCoreAsync` 入口（或 `LaunchAsync`/`RequestLaunchWithServerAsync`）重置 `_userStopped = false`。

### 2. **Launcher.Core/Diagnostics/ModRepairService.cs:134-138 | 高 | 模组补全子任务失败仍计入 Repaired → 误报「已补全」成功**

ctx 路径下 `await child.Completion.WaitAsync(ct)` 后**不检查子任务终态**，无条件 `report.Repaired.Add(...)`。子任务下载失败（网络/404）时 Completion 同样完成（TCS 终态含失败），失败补全被记入 Repaired → ModRepairFlow 弹「已补全 N 个缺失前置」成功 Toast；且失败子任务无自动重试（IsGroupChild），错误完全静默。用户以为模组修好了，重进游戏照样崩。

复现：游戏因缺失前置模组崩溃 → 崩溃窗/启动失败触发自动修复 → 模组补全下载失败（断网/Modrinth 无此版本）→ 提示「已补全」成功，实际未补。修复：`if (child.TerminalState != DownloadTaskState.Completed) { report.Failed.Add(...); continue; }`。

### 3. **Launcher.Core/Download/LoaderService.cs:363-379 | 中 | Forge/NeoForge 安装器运行中被取消：不中止流程，继续 FindNewestVersionDir + 校验 + 打标记**

`runChild` 被取消时 TerminalState=Canceled，375 行只判 `== Failed` 才抛错——取消被放行，继续执行 379 行 `FindNewestVersionDir()`：若安装器已写出半截版本目录 → 校验抛「缺 N 个文件」把「用户取消」报成「安装失败」；若安装器还没写任何目录 → 取到最近修改的**旧版本目录**（如刚装的原版），校验通过后 `InstallMarker.Mark` 给无关版本打上「本启动器安装」标记，组任务终态 Canceled 但副作用已发生（版本标签被改写）。

复现：下载 1.21.10 原版 → 安装 Forge → 安装器运行中取消 → 返回版本页看最新修改的版本被标记为「本启动器」（若来自 PCL 目录则来源标签被改）。修复：`TerminalState != Completed` 即抛错中止。

### 4. **Launcher.Core/Download/LoaderService.cs:307-324 | 中 | Fabric/Quilt 配置子任务失败后 `version!` 空引用，NRE 掩盖真实失败原因**

组路径下 `version` 由配置子任务写入（307 行局部变量）；子任务失败（meta 源 404/断网）后 `await ...Completion` 正常返回但 `version` 仍为 null，324 行 `DownloadVersionAsync(version!, ...)` 抛 NullReferenceException → 组任务 catch 以「未将对象引用设置到对象的实例」为最终错误并触发组级自动重试（同样失败两次后仍报 NRE）。真实原因（子任务 Error：「连接被拒绝」等）被完全掩盖，用户与日志都看不到。

复现：断网安装 Fabric → 失败弹窗只显示 NRE 文案。修复：`if (version is null) throw new InvalidOperationException(runChild.Error ?? "加载器配置下载失败")`。

### 5. **Launcher.App/ViewModels/VersionManageViewModel.cs:302 | 中 | 非隔离（共享）模式备份：把整个 .minecraft 打进 zip，且 zip 落在被打包目录内部 → 自包含损坏/超大/磁盘爆满**

非隔离时 `RootDir == _gameDir`（整 .minecraft），备份 zip 又创建在 `_gameDir/backups/`（源树内部）——`ZipFile.CreateFromDirectory` 枚举时会读到自己正在写的 zip（FileShare.Read 允许），产出自包含损坏条目；同时把 libraries/assets/versions/其他版本全打包（数 GB~数十 GB，分钟级阻塞，磁盘可能写满），还容易被运行中游戏的锁定文件中断。

复现：设置关掉版本隔离 → 版本页点「备份」→ 备份几十 GB、zip 损坏或「备份失败」。修复：共享模式按版本目录（versions/{id} + mods/saves 等实例目录）打包，或先写临时目录再移入 backups。

### 6. **Launcher.Core/Diagnostics/AutoRepairService.cs:107 | 中 | 旧式 natives（≤1.12.2）校验路径不含 classifier → 远古版本安装/启动/修复/质检必报「缺文件」假失败**

VerifyFilesAsync 对每个库按 `MavenPath.FullPath(lib.Name)` 校验——旧式 natives 条目（`lib.Natives` 字段映射、name 三段，如 `org.lwjgl.lwjgl:lwjgl-platform:2.9.4-nightly-20150209`）实际落盘名带 classifier（`...-natives-windows.jar`，下载器 LoaderService/VersionDownloadPipeline 均按 classifier 路径写），校验却查不带 classifier 的路径 → 永远 Missing。后果：1.12.2 及以下（下载页「全部正式版」「远古」分类可见）安装完成校验必抛「安装完成但校验失败：缺 N 个文件」并删 jar；启动前校验同样拦截（自动重下也自愈不了）；CheckIntegrity 恒报缺文件。**这是 BUGS#8 的遗留面：新式 natives 已覆盖，旧式路径错。**

复现：下载 1.12.2（或任一 1.13 前版本）→ 下载完成 → 校验失败删 jar → 版本页红字「缺文件」→ 重新下载循环失败。修复：校验时对 `lib.Natives` 命中项改用 `lib.Name + ":" + classifierKey` 拼路径（与下载/解压口径一致）。

### 7. **Launcher.Core/Download/DownloadManager.cs:148-157 | 低 | 自动重试成功后的任务永不被自动移除，队列残留「完成」条目**

`ScheduleAutoRemove` 只在 `task.Completion`（首次终态）触发一次。B2 时序下：任务首败 → 3s 定时器启动 → 期间自动重试成功且状态变 Downloading → 3s 后检查发现非终态 → 跳过移除，**且不会再排下一次移除** → 重试成功（State=Completed）后任务永久留在队列里，只能靠「清除已结束」手动清。与「终态任务 3 秒后自动移除」的注释承诺不符。

复现：下载中发生一次瞬时网络错误（触发自动重试）→ 任务完成后仍在下载记录列表。修复：状态进入终态时重新调度移除，或在终态 Post 里排一次。

### 8. **Launcher.App/ViewModels/VersionBrowseViewModel.cs:441 | 低 | Select 早退按 Id 判重：跨目录同名版本（watcher 重扫路径）选中第二行时详情停留第一行，修复/删除指向错目录**

`if (HasSelection && Id == row.Id) return;` 只比 Id。LoadAsync 的目录补漏按 Id 去重（92 行）基本防住重复，但 watcher 驱动的 `RescanLocal`（202-205 行）按 (Id, 目录) 去重——两个目录各有同名版本时列表出现两行，点第二行被早退挡住，GameDir/详情仍是第一行（目录 A），点「重新下载/删除/模组管理」实际操作目录 A 的版本。

复现：自建目录与 PCL 目录各装一个 1.21.10 → 触发一次磁盘事件重扫 → 选中第二行 → 详情与操作指向第一行目录。修复：早退条件加上 GameDir 比较。

### 9. **Launcher.Core/Download/MrpackExporter.cs:44 | 低 | mrpack files 条目 downloads 为空数组：导入端无法解析模组下载**

modrinth.index.json 的 files 条目 `downloads = Array.Empty<string>()`——Modrinth 规范要求提供下载 URL（或 PCL/HMCL 导入时按 sha1 反查，通常不可用）。导出的 mrpack 在 PCL/HMCL 导入时模组条目无源可下载，整合包导入后 mods 缺失（overrides 又不含 mods）。另外 `versionId = options.Name`（51 行）语义也应是包版本号。

复现：导出含模组的 mrpack → 在 PCL 导入 → 模组全部解析失败/跳过。

---

## 汇总

| 级别 | 数量 |
|---|---|
| 高 | 2 |
| 中 | 4 |
| 低 | 3 |
| 合计 | 9 |
