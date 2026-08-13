# REVIEW-D：开服 + 联机（陶瓦）用户旅程审查

范围：开服（ServerViewModel / ServerInstaller / ServerProcess / ServerProperties / ServerOpsFile / ServerBannedFile / SuggestionPresets）+ 联机（TerracottaProvisioningService / TerracottaLobbyService / TerracottaRepairService / TerracottaAgreementDialogViewModel / MultiplayerViewModel）。全新范围，无历史审查。

级别：高 4 / 中 6 / 低 5，共 15 条。

---

## 高

### 1. ServerViewModel.cs:667-673 | 高 | StartServer 的 Java 选配抛异常在 try 外——静默失败 + 崩溃 + 状态卡死三连
`PickServerJava`（找不到匹配 Java 时抛 InvalidOperationException）在 `try { _process.Start }`（:673）**之外**执行。三条用户路径全部受损：
- 直接点「启动服务端」按钮：异常被 AsyncRelayCommand 吞掉 → 界面无任何提示，按钮"点了没反应"，Status 不变。
- 服务端异常退出后的「自动修复并重新启动」：Exited 处理器是 `Dispatcher.UIThread.Post(async () => ...)`（:206-260）的 async void → `await StartServer()`（:248）抛出的异常逃逸 → 未处理异常直接崩应用。
- 一键开服/生成世界/下载后自动启动（:615、:1001、:1053、:1066）：任务静默 fault，流程标志卡死（见 #2、#3）。
复现：机器上没装 Java 25（或设置页未指定路径）→ 选 1.21.6 版本 → 点启动服务端：无任何提示；或服务端崩溃后点「自动修复并重新启动」：应用崩溃。

### 2. ServerViewModel.cs:1032,1053,1066 | 高 | 一键开服中途异常 → _oneClickActive 永不复位，功能永久禁用
`_oneClickActive = true` 只在两个失败分支（:1042、:1058）显式复位；`await StartServer()`（:1053、:1066）若抛异常（Java 缺失、下载异常），`_oneClickActive` 永远为 true → 之后每次点一键开服都直接"服务端运行中或流程进行中，先停止再一键开服"，重启应用才能恢复。同时 `_autoJoinOnReady`（:1064）残留，下次手动启动服务端在 Done 时自动拉起客户端。
复现：未装 Java → 点一键开服 → 失败 → 装好 Java 后再点一键开服 → 永远提示"流程进行中"。

### 3. TerracottaLobbyService.cs:48-51 | 高 | HttpClient.Timeout 用秒当毫秒（3000 秒=50 分钟），停止房间可挂死
`Timeout = TimeSpan.FromSeconds(RequestTimeoutMs)`（:50），RequestTimeoutMs=3000 本是毫秒意图（:275 的 `CancelAfter(RequestTimeoutMs)` 就是 3 秒）。**未走 GetAsync 封装的直连调用全部落在 50 分钟超时上**：`MetaIsValidAsync`（:290）、StopRuntimeAsync 的 `/state/ide`（:464）、`/panic?peaceful=true`（:483，用的还是 CancellationToken.None）、GetOrStartEndpointAsync finally 里的 panic + `.Wait()`（:198-200，同步阻塞，卡 UI 线程）、CleanupFailedCreationAsync 的 panic（:526）。
复现：陶瓦进程挂起（如崩溃前处于死循环）→ 点「离开房间」→ StopRuntimeAsync 的 /panic 等 50 分钟才超时，期间 UI 卡死/房间无法退出；修复进程后端口无响应时也一样。

### 4. TerracottaLobbyService.cs:408-415,421-422 | 高 | 复用现役实例的会话无进程死亡检测，UI 永远卡在"房间已就绪"
监控循环只在 `_ownedProcess != null` 时检测进程退出；复用已有实例（`_ownedProcess == null`，:173）的会话直接跳过该检查，`/state` 连不上（连接拒绝）被 `catch { continue; }`（:422）当"网络抖动"无限忽略 → 陶瓦进程死后房间状态永远 Active，玩家列表冻结，用户只能手动离开。
复现：取消过一次创建（残留孤儿进程，见 #6）→ 再创建房间复用该实例 → 在任务管理器结束 terracotta.exe → 联机页没有任何反应，永远显示房间就绪。

---

## 中

### 5. ServerViewModel.cs:1005-1022 | 中 | _autoStopOnReady / _autoJoinOnReady 在服务端崩溃（未到 Done）后残留
两个标志只在日志命中 `Done (...s)` 时清除。若服务端在 Done 之前崩溃/被杀（jar 损坏、内存不足、用户强停），标志保持 true → 用户下次手动启动服务端，Done 一到就自动 stop（或自动拉起客户端进服），与用户意图不符。
复现：点「生成世界」→ 服务端启动即崩（未到 Done）→ 再手动点「启动服务端」→ 服务端就绪瞬间被自动停止，日志出现"世界生成完成，自动停止…"。

### 6. TerracottaLobbyService.cs:67,87 + MultiplayerViewModel.cs:396-412 | 中 | 取消创建/加入 → terracotta.exe 孤儿进程永久残留
`CreateHostAsync`/`JoinAsync` 的 catch 过滤 `ex is not OperationCanceledException` → 用户取消时 `CleanupFailedCreationAsync`（panic+kill+清理句柄）完全不执行；VM 侧 `Reset()` 的 `_lobby.Dispose()`（:613-621）只 `Process.Dispose()` 释放句柄**不杀进程** → 被拉起的 terracotta.exe 在 scanning/connecting 状态永久存活，占住端口与 %TEMP% 锁文件，后续会话只能"复用"这个状态不定的孤儿（也是 #4 的直接来源；每个取消都积累一个残留进程，只能靠一键修复清理）。
复现：创建房间 → 点「取消」→ 任务管理器可见 terracotta.exe 仍在运行且永不退出。

### 7. MultiplayerViewModel.cs:151-173,352-391 | 中 | 无并发守卫：失败后「创建」与「一键修复」自动重试可双会话互踩
`RepairNow` 直接调用 `CreateRoom()`/`JoinRoom()` 方法（非命令，绕过命令级并发禁用），与用户同时点「创建房间」按钮 → 两次 `StartSession` 都 `new TerracottaLobbyService` 并替换 `_lobby` 字段 → 两个 CreateHostAsync 并发跑在各自实例上，先失败/先完成的一方 catch 里 `Reset()` 会 Dispose 掉对方的 `_lobby`（HttpClient 已释放）→ 后起会话报"创建房间失败：The HttpClient instance has already been disposed"这类莫名错误。
复现：创建房间失败（如 20 秒无世界）→ 错误条出现「一键修复」按钮 → 同时点「一键修复」和「创建房间」→ 其中一途必然失败且文案误导。

### 8. TerracottaRepairService.cs:13-15 vs TerracottaLobbyService.cs:114 | 中 | 一键修复删错路径的锁文件，%TEMP% 残留锁永远清不掉
修复删 `%LOCALAPPDATA%\terracotta\terracotta.lock`，而会话读取/真正写入的锁在 `%TEMP%\terracotta\terracotta.lock`（测试也以 %TEMP% 为准）。一键修复里"删锁文件"实际删了个不存在的文件，注释声称的根因（残留锁文件）并未被修复；杀进程是唯一有效部分。两个路径并存本身就是隐患：修复后 %TEMP% 锁仍指向死端口，下次会话照样要先 meta 探测失败再拉新进程。
复现：Busy 失败 → 一键修复 → 检查 %TEMP%\terracotta\terracotta.lock 依然存在。

### 9. ServerProperties.cs:43-59 + ServerViewModel.cs:1153-1164 | 中 | 运行中保存 server.properties 会被服务端停止时回写覆盖，编辑静默丢失
`SaveProperties` 在服务端运行中也可保存（无 IsRunning 检查），而原版服务端在停止/保存时会把内存中的属性整文件回写 → 用户运行中改的端口/视距等被服务端旧值覆盖，界面却提示"已保存"。且 `File.WriteAllText` 直写无原子性（临时文件+Move 更稳），崩溃/断电可写坏配置。
复现：服务端运行中 → 改 server-port 保存 → 停止服务端 → 再启动：端口是旧值，用户的修改消失。

### 10. ServerViewModel.cs:852-862,866-874 | 中 | ban/op 后固定 500ms 读盘，服务端写盘慢时列表不刷新
发送 ban/op 命令后 `Task.Delay(500)` 就刷新 ops.json/banned-players.json —— 服务端异步写盘（存档线程繁忙时可能 >500ms）→ 列表仍是旧数据且无任何重试，用户看到"已封禁"但列表里没有，点解封入口也无处可点（运行中走 pardon 命令路径，仅提示层问题）。
复现：玩家多/存档大的服务器上 ban 一个玩家 → 封禁列表 500ms 后刷新失败 → 列表空白，需手动刷新。

---

## 低

### 11. TerracottaLobbyService.cs:448-492 | 低 | StopAsync 后 _controllerPort 不归零，同实例二次会话复用死端口
`StopRuntimeAsync` 只清 `_ownedProcess`（:491），`_controllerPort` 保留 >0 → 同实例再次 `GetOrStartEndpointAsync`（:111 的 `if (_controllerPort > 0) return;`）直接跳过握手复用已死的端口。当前 VM 每次会话 new LobbyService 恰好规避，但属于未上锁的地雷：任何会话复用（如未来 UI 复用实例）即得"联机模块响应超时"。
复现（服务层单测场景）：同一实例 CreateHost → StopAsync → 再 CreateHost → 直接对死端口发请求失败。

### 12. TerracottaAgreementDialog.axaml.cs:18-24 + TerracottaAgreementDialogViewModel.cs:79-130 | 低 | 协议窗下载中关窗：模块照装、无提示、不可取消
用户点 X 关窗后 `ShowDialog` 返回 null 走 Declined 分支，但 `EnsureAvailableAsync` 后台任务继续跑完并完成安装（无取消路径、无"安装已完成"提示）。之后重新打开协议窗 → 模块已就绪直接通过 → 相当于未明确同意也装上了模块；`_finish(true)` 对已关窗口调 `Close(true)` 也被 catch 吞成"下载失败"文案（若 Avalonia 对已关窗口 Close 抛异常）。
复现：弹协议窗 → 点同意 → 立即关窗 → 等下载完成 → 重进联机页：模块已装、无需同意。

### 13. TerracottaProvisioningService.cs:96-101 | 低 | ReinstallAsync 删除失败被吞：可能"假装重装成功"或报误导文案
`Directory.Delete(ModuleRoot)` 失败（exe 被其他进程占用，如别的启动器实例）被 catch 静默吞掉 → 若旧模块仍能通过校验，`EnsureAvailableAsync` 直接返回旧模块，用户以为重装完成；若校验不过则走安装，`Directory.Move(targetDir, backup)`（:244）在 exe 被占用时抛 IOException → 用户看到"陶瓦模块下载失败"，与"重装"语义不符，且无"有进程占用"提示。
复现：另一个启动器正跑着 terracotta.exe → 本启动器联机失败 → 一键修复（ReinstallModule）→ 报"下载失败"或实际没重装。

### 14. ServerViewModel.cs:675,1099-1124 | 低 | 重启服务端后在线玩家列表残留旧玩家
`StartServer` 里 `Logs.Clear()`（:675）不清 `OnlinePlayers`；新会话没有 joined 事件前，旧玩家名一直显示在列表（含踢出/封禁按钮），点踢出会对已下线的玩家名发命令（无效命令进日志）。
复现：A、B 两个玩家在线 → 停止服务端 → 重启 → 玩家列表仍显示 A、B（实际无人）。

### 15. ServerOpsFile.cs:38-54 / ServerBannedFile.cs:39-55 | 低 | 文件级 OP/解封写失败静默吞掉，UI 却提示"已移除"
`Remove`/`Unban` 的 `catch { }` 吞掉所有写失败（文件被占用/无权限/损坏），调用方（ServerViewModel.cs:905-907、:946-948）无条件提示"已移除/已解封"——用户看到成功提示但服务端重启后 OP 还在。
复现：停止服务端 → ops.json 被编辑器占用 → 移除 OP → 提示成功 → 重启服务端：OP 仍在。
