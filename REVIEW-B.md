# REVIEW-B — 下载引擎验证性复核 + 深挖（2026-08-11）

复核范围：DownloadService / DownloadTask / DownloadManager / DownloadGroupContext / VersionDownloadPipeline / LoaderService / AutoRepairService / SlowSourceDetector / FailureDiagnostics / DownloadOptions。
方法：对 BUGS.md 16 条 + B2 + BUGS#1 逐条核对当前代码，再按 6 个用户实际使用方向深挖。

---

## 一、复核清单结论

| 条目 | 结论 | 一句话结论 |
|------|------|-----------|
| BUGS#3 单连接续传无 206/200 判定 | **仍存在** | 442/452 行仍无条件 Append；有 sha1/size 时自愈，无元数据直链静默错位落盘 |
| BUGS#4 级联取消失败兄弟死代码 | **仍存在** | 253-259 在 WhenAll(249) 之后 Cancel，子任务已全终态；库 404 后 assets 仍下完 |
| BUGS#5 编排抛错等全部子任务 | **已修（无法复现）** | groupWork(248) 抛错直接进 catch(296) 立即失败态，不再等子任务；残余问题见 B9 |
| BUGS#6 LoaderService 配置子任务失败 version=null NRE | **仍存在** | 307-324：子任务 Completion 不抛，version 保持 null → 324 行 NRE；且 ForDownload(NRE)=null，诊断卡也不出 |
| BUGS#7 Retry 双击竞态 | **仍存在** | 375 行守卫读 Post 异步生效的 State，连点两次双并发跑同一 work |
| BUGS#8 natives classifier 不参与完整性校验 | **仍存在** | AutoRepairService 102-110 只验 Artifact 路径；natives-windows classifier 缺失/损坏质检照过 |
| BUGS#9 竞速赢家落盘异常裸抛 | **仍存在** | 328 行 File.Move 无 try；失败时 raceCts.Cancel 与收尾都不执行，其余源变僵尸继续下整份文件 |
| BUGS#10 GetContentLengthAsync 重复 HEAD + 吞取消 | **仍存在** | 804-820：每源全量候选串行 HEAD；817 catch(Exception) 吞用户 OCE（取消仅延迟，无挂起） |
| BUGS#11 CTS 不 Dispose + 竞速残留 | **仍存在** | 287 srcCts 从不 Dispose；.race/.parts 只在轮首(274)清理，最后一轮失败后永久残留 |
| BUGS#12 416 泄漏 response | **仍存在** | 496-500 异常内 response 不可达不 Dispose，连接延迟回收 |
| BUGS#13 straggler 阻塞 Task.Wait | **仍存在** | 334 行后台线程池线程阻塞等输家 |
| BUGS#14 DownloadTask CTS/注册不释放 | **仍存在** | 29/379/394 换新 CTS 旧的不 Dispose；417 注册项只增不清 |
| BUGS#15 FileLocks 只增不减 | **仍存在** | 19/195 字典永不移除 |
| BUGS#16 分片跨轮 chunkSize 错位 | **仍存在（路径收窄）** | 仅 SlowSourceException 分支(662)绕过清理保留 .parts；探测判定变化后同序号 part 续传错位 |
| B2 完成延迟到重试耗尽 | **已修，但有新竞态** | 四条完成路径齐全；手动 Retry 撞排程自动重试时出现幽灵重跑（见 R-01） |
| BUGS#1 慢速阈值随限速联动 | **已修，验证通过** | 阈值=min(默认, 每流限速×0.8)，实测余量 ≥18%；限速=0 走默认不误伤；连带发现限速精度问题（见 R-02） |
| BUGS2-B9 编排抛错子任务不取消 | **仍存在** | 296-307 catch 分支无 Children 级联 Cancel；重试 Children.Clear 不摘 PropertyChanged 订阅（R-07） |

---

## 二、新发现问题（深挖）

### R-01 src/Launcher.Core/Download/DownloadTask.cs:344-369, 373-386 | 中 | 手动 Retry 撞自动重试排程 → 终态后幽灵重跑
- 问题: 自动重试排程期间（Task.Delay 800ms/3s）用户手动点 Retry：手动重跑快速再失败后，旧排程的 Delay 到期分支把 `_retryPending=false`（把**新排程**的待办标记也清掉），随后 `Post(() => Retry())` 通过 State==Failed 守卫启动新一轮下载——此时 TCS 可能已完成（旧排程清标记导致 finally 提前放行）、任务已被 ScheduleAutoRemove 移出队列 → 幽灵下载：文件在队列外后台下完，用户看到的却是「失败」、历史记失败。根因：重试待办是单一 bool，非按代（attempt）记账；旧排程可清掉新排程的标记。
- 复现: ① 任务失败（网络异常）→ 等待自动重试期间立即手动点「重试」→ ② 手动重试快速失败（<2s，如连接被拒）→ ③ 原排程 800ms 到期 → Retry 又跑一轮且成功/失败时，TCS 早已完成 → ②的第二次自动重试排程到期后 Post(Retry) 启动第 4 轮下载（队列里已无此任务）。修复建议：排程时捕获 `_cts` 实例，Delay 后比较 `ReferenceEquals(_cts, captured)` 再决定是否重跑；或 Retry 用 `Interlocked` 单飞标志。

### R-02 src/Launcher.Core/Download/DownloadService.cs:45-47, 466, 756, 689-720 | 中 | 限速按流均分 + 8KB/s 下限 → 限速值不精确（多数偏慢，小限速超限）
- 问题: `_limitPerStream = max(L/8, 8192)` 且限速只在每流累加器生效（非全局令牌桶）：① 单连接下载（<256KB 小文件、probe 判定单连接、单候选）实际速度恒 = **L/8**——用户设 2MB/s 限速，小文件只有 256KB/s（慢 8 倍）；② L≥6.4MB/s 时 probe 探测到「快源」→ 单连接 → 实际 = L/8；③ L∈[1.6MB, 6.4MB) 时 probe 判 4 片 → 实际 = L/2；④ L<64KB/s 时每流被 8KB 下限抬升，分片实际恒 64KB/s = **超限 1.3~6.4 倍**（设 30KB/s 实际跑 64KB/s），单连接则只有 8KB/s。附带：限速开启时 probe 测的是被限速后的速度，「按连接限速源/快源」判定失真。
- 复现: 设置下载限速 2MB/s → 下载一个 100KB 的库 → 观察速度恒 ~256KB/s；设限速 30KB/s → 下载 300MB 大文件 → 观察总吞吐 ~64KB/s（超过所限 2 倍）。修复建议：全局令牌桶（每服务一个累加器），去掉每流 8KB 下限或改为「总吞吐=min(L, 实际并发流×8KB)」语义。

### R-03 src/Launcher.Core/Download/DownloadService.cs:652, 662-670 | 中 | 分片 .parts 在取消/暂停时被删 → 暂停/继续大文件 = 完整重下
- 问题: DownloadChunkedAsync 的通用 catch(663-670) 无条件 `Directory.Delete(.parts)` + `File.Delete(.tmp)` 后再回退单连接；用户取消（含暂停）的 OCE 也走这条路径 → 分片大文件**暂停→继续**或**取消→稍后重试** = probe 重跑 + 全部片从 0 重下（376MB 客户端 jar 白下）。而单连接路径 .tmp 保留、跨任务断点续传正常（442 行 from=长度续传）——两条路径续传能力不一致，暂停/继续这个主用户路径对分片大文件等于没有。唯一幸存路径是 SlowSourceException(662) 的直抛，但也正是它造成 BUGS#16 的错位风险。
- 复现: 下载 300MB 大文件（分片）→ 暂停 → 继续 → 观察从头开始下载（速度条归零、probe 重跑）；同样操作单连接小文件（.tmp 保留）则续传。
- 修复建议: OCE/暂停路径保留 .parts，仅在「无 sha1 且合并失败」等确认损坏时删；续传前校验 part 起点与本次 chunk 边界（同 BUGS#16 建议）。

### R-04 src/Launcher.Core/Download/DownloadService.cs:330-336, 609-614 | 低 | 分片 straggler 的 orphan 分片任务不被外层 Wait 等待 → 清理与写句柄竞态残留
- 问题: 竞速输家为分片下载时，WhenAll 首错即抛（619-628），未 await 的兄弟分片 Task.Run 是孤儿；RaceOneAsync 提前返回 (false,null) 后外层 `p.Task.Wait()` 已返回 → CleanupRaceFiles 删除 .parts 时孤儿分片句柄仍打开（FileShare.None）→ 删除抛 IOException 被静默吞 → .parts 残留磁盘直到下次同路径下载轮首才清。另：慢取消窗口内孤儿分片继续写几个毫秒（共享 slowCts 已取消，实际影响极小）。
- 复现: 竞速中输家是分片下载且取消传播瞬间 → 观察 .race{i}.parts 目录残留（下一次下载同路径前一直存在）。
- 修复建议: DownloadChunkedAsync 在 WhenAll 失败路径先 await 未完成分片（或让 chunk 任务共享一个 inner WhenAll）再返回。

### R-05 src/Launcher.Core/Download/DownloadManager.cs:66-72 + DownloadGroupContext.cs:21-27 | 低 | 并发门只限顶层任务，组任务子任务/分片连接不限
- 问题: AL65 Gated 只包住 Enqueue/EnqueueGroup 的顶层 work；组任务内的子任务（ctx.AddChild 直接 new DownloadTask）与下载内部 8 分片×多源竞速连接全部绕过 _gate。设置「最大并发下载数=3」时，3 个版本下载组可同时开出几十上百条连接（每组长子任务并发 + 分片 + 竞速多源）——设置名与实际并发语义不符（对叶任务多、组任务少的场景（模组包）有效，对版本下载无效）。
- 复现: 并发下载数设 3 → 同时装 3 个版本 → 观察连接数远超 3（任务数 3 但连接几十条）。
- 附: 队列任务取消无信号量泄漏（WaitAsync 未获锁不 Release）✓；SuspendAll/ResumeAll 与门交互正确（排队中任务暂停=等待者 OCE，未占槽）✓。

### R-06 src/Launcher.Core/Download/DownloadTask.cs:253-264, 552-563 | 低 | 父组失败 Error 泛化为「子任务失败」——child.Error 经 UI Post 异步生效
- 问题: 父推导 `SetState(Failed, failed.Error ?? "子任务失败")` 读的是子任务 Error，而 Error 在 SetState 的 Post 内赋值（异步）；WhenAll 返回的线程池线程上 Post 大概率未执行 → 父 Error 几乎总是泛化文案「子任务失败」，真实原因只在子任务行的 Stage（AL68 兜底后 UI 勉强可见），历史记录/错误详情拿不到根因。与 BUGS#6 同族（错误信息在 TCS 完成时点上不可用）。
- 复现: 真机 UI 上下文下让组内某子任务失败 → 父任务 Error=「子任务失败」而非「连接被拒…」（测试环境 Post 同步直跑，测不出）。
- 修复建议: 子任务在 Post 前把 Error 同步写进 TerminalError 字段（与 TerminalState 同法）。

### R-07 src/Launcher.Core/Download/DownloadTask.cs:296-307, 381, 408-421 | 低 | 编排抛错子任务不取消（B9 复核）+ Children.Clear 不摘订阅
- 问题: RunGroupAsync 两个 catch（287-307）无 `Children` 级联 Cancel（只有 255-259 的 failed 分支做了，且是死代码）——组编排中途抛错（index 下载失败、NRE 等）时已创建的子任务继续下载到结束，浪费带宽/IO；重试时 Retry 的 `Children.Clear()` 只清集合，不摘 `child.PropertyChanged += OnChildPropertyChanged`（415）也不 Dispose `_externalCancellations` 注册（417）——旧子任务完成时仍触发父 RecomputeAggregate（空转）+ 注册链泄漏（BUGS2-B10 复核确认）。
- 复现: 版本下载编排在挂载部分子任务后抛错（如 index 下载失败）→ 观察已挂载子任务继续下载；重试后旧子任务事件仍触发父聚合。

### R-08 src/Launcher.Core/Diagnostics/FailureDiagnostics.cs:69-82 + DownloadService.cs:352-356 | 低 | 「网络不可达」异常无诊断映射（无修复卡片、无自动重试）
- 问题: 重试耗尽且全网不可达时抛 InvalidOperationException("网络不可达：…")，ForDownload 对 InvalidOperationException 返回 null → 无 Diagnosis 卡片（无「检查网络」建议按钮）、无自动重试；而同为耗尽路径的 HttpRequestException 却映射为 CheckNetwork。诊断能力不一致。
- 复现: 断网下载重试 2 轮后 → 任务失败仅显示错误文案，无 AL44 诊断卡/修复按钮。

---

## 三、深挖方向结论（模拟用户实际路径）

1. **竞速输家清理窗口**：AL58 修复后主窗口已封（先 Wait 停再删）；残留窗口仅 R-04（分片孤儿任务，Windows FileShare.None 下删除失败→静默残留，无「边写边删」损坏）。BUGS#9 的 Move 失败路径仍裸抛，僵尸源照旧。
2. **断点续传边界**：单连接 .tmp 跨任务续传成立（0 字节残留安全、416 自删自愈）；服务器忽略 Range 回 200 → 单连接 append 错位（BUGS#3，未修）；分片 .parts 取消/暂停即删 → 跨任务续传实际不存在（R-03）；SlowSourceException 路径保留 parts → BUGS#16 错位风险。
3. **限速+慢速判定**：BUGS#1 修复正确（阈值随每流限速下调，余量 ≥18%，限速=0 不误伤）；但每流均分+8KB 下限导致限速精度问题（R-02）——慢速判死已无假阳性，限速语义本身失真。
4. **取消传播**：单连接/分片/竞速/排队各路径均及时退出（OCE 直达任务 catch，无死循环、无继续写盘）；GetContentLengthAsync 吞用户取消（BUGS#10，仅毫秒级延迟，无挂起）；竞速 eval 循环取消时多转一圈后正常上抛。暂停大文件=重下（R-03）。
5. **并发门**：排队取消无信号量泄漏、与 SuspendAll/ResumeAll 交互正确；门只限顶层任务数（R-05，组任务内部连接不受限）。
6. **组任务时序**：TerminalState 同步先于 TCS ✓（AL5 成立）；残余两处：child.Error 异步 → 父 Error 泛化（R-06）；级联取消死代码（BUGS#4）→ 失败组在子任务还在下载时就已 Failed 并被 3s 后移出队列，残余下载在队列外不可见。
