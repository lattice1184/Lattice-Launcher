# 陶瓦联机（Terracotta）移植规格
源：BlockHelm-Launcher（GPL-3.0）。行号引用缩略：P=TerracottaProvisioningService.cs，L=MultiplayerLobbyService.cs，V=MultiplayerPageViewModel.cs，I=ITerracottaProvisioningService.cs。

## 1. 下载/安装（P）
- **release API**（并行请求，Gitee 优先）：Gitee `https://gitee.com/api/v5/repos/burningtnt/Terracotta/releases/latest`（P25）、GitHub `https://api.github.com/repos/burningtnt/Terracotta/releases/latest`（P27）。请求头 User-Agent=`BlockHelm-Launcher/1.0`（P221）；重定向 MaxRedirects=10、ResponseHeadersTimeout=20s（P76-77），最终 URI 必须 HTTPS（P765）。两者皆失败且已装模块→直接用旧模块（P96-102）。
- **响应 JSON 字段**：`tag_name`（去前导 v，仅允许字母数字./-，≤64）、`prerelease` 必须 false、`draft`（仅 GitHub）必须 false、`assets[]`：`name`、`browser_download_url`、可选 `digest`（"sha256:"前缀，剥前缀后须 64 位 hex，P742-753）。元数据 ≤2MB（P30）。
- **资产名**：`terracotta-{version}-windows-{arch}-pkg.tar.gz`（P717）；arch=`x86_64`/`arm64`（P720-725）。
- **SHA256 校验**：优先 GitHub 同版本资产的 digest；否则查硬编码表 `{version}/{arch}`（P38-43）：`0.4.2/x86_64`=`07ebe139e3ca5f74576e58b1a96efe59abdfbe148d3f1a49bfdca8b6f70745f0`、`0.4.2/arm64`=`acfab0a87a02dedc6dab7c05303186c8907f56f815548b693fb3324358da7d14`；都没有→跳过校验仅记日志（P194-200）。
- **下载源顺序**：Gitee 资产 URL → GitHub 同版本资产 URL → 兜底 `https://github.com/burningtnt/Terracotta/releases/download/v{version}/{assetName}`（P182-192）。单源失败换下一源（P352-384）。归档限 0<大小≤64MB（P31,403）。
- **tar.gz 解析**（P440-508 抽取 + P510-555 预检）：仅 RegularFile/V7RegularFile；目录跳过；文件名扁平（含 '/' 或 "." ".." 即拒）；只允许两个文件：`terracotta-{version}-windows-{arch}.exe`（改名为 `terracotta.exe`）与 `VCRUNTIME140.DLL`；禁重复；单项 ≤64MB；最终恰好 2 个文件。
- **流程**：下载→SHA256 比对→预检→解压到 staging→写 manifest→发布（旧目录挪为 backup，失败回滚，P690-712）→ValidateInstallation。
- **安装目录**：`{DefaultDataDirectory}\tools\terracotta\{version}\terracotta-windows-{arch}`（P57,714-715）。
- **manifest**：`.blockhelm-module.json`（P33），JSON 字段：`Version`、`Architecture`、`ArchiveSha256`、`PublisherDigestVerified`、`Files`（字典 文件名→{`Size`,`Sha256`}，P870-877）；先写 `.blockhelm-module.json.{guid}.tmp` 再 Move 覆盖（P636-659）。已装同版本且新 digest 通过→回写 PublisherDigestVerified=true（P661-680）。
- **TryGetAvailable**（P557-583）：扫 moduleRoot 子目录 `{dir}\terracotta-windows-{arch}`，逐目录 ValidateInstallation（manifest 存在、arch 匹配、目录名==manifest.Version、2 个文件 Size+SHA256 全匹配，P585-634），取版本最高者。版本比较按 '-' 前段 ParseVersion（P774-778）。
- **进度**：LauncherProgress(Stage,Message,Percent)，阶段字面量 `terracotta-download`（百分比=total*90/ContentLength 夹 0..90）、`terracotta-extract`（92）、`terracotta-ready`（100）（P310,329,357,428-431）。
- **并发**：SemaphoreSlim(1,1) 串行（P49）。同版本但 digest 与线上不符→重装（P128-132）；已装版本更高→不更新（P107）。

## 2. 进程与握手（L）
- **启动参数**：`terracotta.exe --hmcl2 {handoffPath}`（L365-366）；WorkingDirectory=模块目录；隐藏窗口；stdout/stderr 重定向并排空（L353-377）。
- **handoff 文件**：`%TEMP%\blockhelm-terracotta-{Guid:N}.json`（L302-304）；JSON 字段 `port`（int，0<port≤65535）（L405-410）；50ms 轮询、超时 12s（L22,379-423）；IOException 时延迟 50ms 重读；进程先退→失败；结束时删除 handoff 及 `.tmp` 变体（L346-350）。
- **锁文件**：`%TEMP%\terracotta\terracotta.lock`（L25-28），内容=2 字节大端端口号（L977-996）；FileShare.ReadWrite|Delete 读。已有实例：读端口→GET `/meta` 校验（requireExactVersion=false）通过则直接复用，OwnedProcess=null（L293-300）。
- **所有权判定 ClassifyHandoffProcessOwnershipAsync**（L425-444）：进程已退出→非拥有者；否则 WaitForExitAsync 竞争 750ms：期间退出→非拥有者；超时且仍存活→拥有者。随后 `/meta` 用 requireExactVersion=ownsProcess 再校验（L316-320）；非拥有者→dispose 进程对象继续用现役实例（L327-332）。
- **/meta 校验字段**（L446-485）：`version` 非空、`target_os`=="windows"（忽略大小写）、`target_arch`：模块 x86_64→"x86_64"；arm64→"aarch64" 或 "arm64"（L968-975）；仅拥有进程时要求 version 与模块版本一致。
- **退出监视**（L608-662）：PeriodicTimer 1s 轮询 `/state`；process.HasExited→TerracottaExited。玩家变更按签名 `machine_id\x1fname\x1fkind` 以 '\n' 拼接比对。

## 3. HTTP API（L）
- **客户端**：`http://127.0.0.1:{port}{path}`（L815-816）；AllowAutoRedirect=false、UseProxy=false（L42-46）；单请求超时 3s（L23）；响应 ≤1MB（L20）；期望 200。
- **端点**：`GET /meta`；`GET /state`；`GET /state/scanning?player={UrlEncode}`（L905-906，host 建房间）；`GET /state/guesting?room={UrlEncode}&player={UrlEncode}`（L908-910，guest 加入，**400→InvalidRoomCode**，L805-810）；`GET /state/ide`（L705，回 waiting）；`GET /panic?peaceful=true`（L725）。
- **/state 结构**（L839-903）：`state` 字符串枚举：`waiting`/`host-scanning`/`host-starting`/`host-ok`/`guest-connecting`/`guest-starting`/`guest-ok`/`exception`/其他→Other；`room` 字符串（≤64）；`type` 整型异常码（可缺省）；`profiles[]` 元素：`machine_id`（≤128，重复跳过）、`name`（≤64，空→"Player"）、`vendor`（≤128，空→"Terracotta"）、`kind`（"HOST"→Host，"LOCAL"→IsLocal；host-ok 状态的 HOST 也标 IsLocal）。文本规整：Trim、去控制字符、超长截断、空填 fallback（L922-931）。
- **状态机**（轮询间隔 500ms=PollInterval L24；启动超时 20s L21）：
  - Host（L487-547）：等 `host-ok`→Active（room 空→协议错误）；`exception`→按 type 映射；`waiting`/guest 态/other→协议错误。超时：最后态=host-scanning→MinecraftWorldUnavailable，否则 TerracottaStartupFailed。
  - Guest（L549-606）：等 `guest-ok`→Active；guest-connecting/starting 且 room 非空→用 state 的 room 作规范房间码（L566-570）；`exception`→按 type 映射；其余态→协议错误；超时→RoomConnectionFailed。
  - Monitor（L608-662）：`exception`→按 type 停；非期望态（Host→host-ok、Guest→guest-ok）→TerracottaServiceFailed。
- **异常码 type 映射**（L933-966）：host：3→TerracottaStartupFailed（EasyTier host 停止）、4→MinecraftWorldUnavailable（LAN 世界消失）、其他→协议错误；guest：0/1→RoomConnectionFailed（连不到房主）、2→RoomConnectionFailed（EasyTier guest 停止）、其他→协议错误；停止原因：3→TerracottaExited、4→MinecraftWorldClosed、其他→ServiceFailed。

## 4. UI（V + XAML + Strings.zh-Hans.resx）
- **创建页三步**（resx 58-60）："第一步：进入要联机的游戏世界。" / "第二步：在游戏菜单中点击"创建局域网世界"。" / "第三步：返回启动器，点击"创建房间"。" 按钮="创建房间"（L51）。
- **检测弹窗**：创建时开 IsLanWorldDetectionDialogOpen；可取消（CancelLobbyDetectionCommand，L213-218）。成功→IsLobbyHost=true、进入 Lobby 步骤（V186-190）。
- **加入页**（JoinLobbyView）：两步文案 "第一步：输入由房主提供的房间代码。" / "第二步：点击"加入房间"。"；房间码输入框 MaxLength=256，占位 "请输入房间代码"，按钮 "粘贴"/"加入房间"（加入中→"正在加入…"）；状态文本 JoinLobbyStatus 展示错误。
- **协议弹窗**（MainWindow.xaml:1276-1329，DialogWidth=500）：标题 "联机功能使用须知"；正文（许可证声明）"联机功能由 Terracotta | 陶瓦联机提供，底层基于 EasyTier。使用联机功能时，您必须遵守中国大陆相关法律法规，不得用于违法用途。"；项目链接 "Terracotta | 陶瓦联机项目"→`https://github.com/burningtnt/Terracotta`（I13；EasyTier 分支 `https://github.com/burningtnt/EasyTier/tree/v2.5.0-terracotta.2`，I19）；按钮 "不同意"/"同意"；下载中显示状态+进度条（0-100）。VM（TerracottaAgreementDialogViewModel）：EnsureReadyAsync 在 TryGetAvailable()==null 时弹窗并 await 决策；状态文案：准备下载…/正在下载…/正在安装…（stage=="terracotta-extract"）/联机模块已就绪。/下载失败，您可以重试或暂不启用联机功能。
- **房间页**（CreateLobbyView.xaml:57-152）：标题 `{0}的游戏房间`（{0}=房主名，否则 "玩家"）；副标题 "联机服务由 Terracotta | 陶瓦联机提供"；危险按钮 房主"退出并解散"/客人"离开房间"；"房间代码" 只读框+复制按钮（复制成功 toast "已复制房间代码"）；"玩家列表"：每行 DisplayName+Subtitle(vendor)+LatencyText（"{0} ms"，未知 "—"）+RoleTags（"房主"/"玩家"，房主标签强调色）+LocalTags（本机 "我"）。
- **离开确认**：房主："退出并解散房间？" / "退出后房间将被解散，其他玩家也会断开连接。是否确认退出？" / "退出"；客人："离开房间？" / "离开后将断开与房间的连接。" / "离开房间"。
- **错误文案**：InvalidRoomCode→"房间代码格式无效，请检查后重试。"；TerracottaUnavailable→"Terracotta 联机模块不可用…"；MinecraftWorldUnavailable→"未检测到可用的局域网世界…"；TerracottaBusy→"Terracotta 正被其他启动器使用…"；ProtocolFailed→"Terracotta 联机服务异常，请关闭其他 Terracotta 实例后重试。"；WorldClosed→"局域网世界已关闭，房间已自动解散。"；TerracottaExited→"Terracotta 联机模块已停止，房间已自动解散。"；ServiceFailed→"Terracotta 联机服务异常，房间已自动解散。"；创建失败→"创建房间失败，请稍后重试。"；加入失败→"加入房间失败，请确认房间仍然有效并检查网络后重试。"；剪贴板空→"剪贴板中没有可用的房间代码。"

## 5. 收尾（L）
- **StopAsync**：RequestStop→快照 Stopping→StopRuntimeAsync（L265-287,698-740）：若 ControllerStateStarted→GET `/state/ide` 后等 waiting（5s 超时、100ms 轮询，L742-753）；有 OwnedProcess 且未退出→GET `/panic?peaceful=true`（CancellationToken.None，失败仅日志）；再 StopOwnedProcessAsync：先等 3s，超时 Kill(entireProcessTree:true)，再等 3s（L1023-1051）。
- **异常终止**：StopUnexpectedlyAsync 加锁、置 runtime=null、停进程、清 current，然后触发 `Stopped` 事件(MultiplayerLobbyStopped{Reason,Exception})（L664-687）。UI 侧 OnLobbyStopped→dispatcher Post ResetLobbyView+按 Reason 显示错误文案（V380-395）；SnapshotChanged→dispatcher Post ApplyLobbySnapshot（V375-378）。创建失败路径 CleanupFailedCreationAsync 同样停进程但**不发 Stopped**（L689-696）。

## 6. 可复用常量清单
URL：Gitee release API、GitHub release API、GitHub 兜底下载模板、Terracotta 仓库 `https://github.com/burningtnt/Terracotta`、EasyTier `https://github.com/burningtnt/EasyTier/tree/v2.5.0-terracotta.2`。版本：Terracotta 引用 `0.4.2`（I14）、EasyTier `v2.5.0-terracotta.2`（I20）、KnownDigests 仅 0.4.2 双架构（P41-42）。超时：StartupTimeout 20s、HandoffTimeout 12s、RequestTimeout 3s、WaitForWaiting 5s、进程退出等待 3s、所有权宽限 750ms、handoff 轮询 50ms、状态轮询 500ms、Monitor 1s、WaitForWaiting 轮询 100ms。大小：响应 1MB、元数据 2MB、归档 64MB、单项 64MB、缓冲 81920。文件：`terracotta.exe`、`VCRUNTIME140.DLL`、`.blockhelm-module.json`、`terracotta.lock`、handoff 模板 `blockhelm-terracotta-{Guid:N}.json`。文件名模板：资产 `terracotta-{v}-windows-{arch}-pkg.tar.gz`、exe `terracotta-{v}-windows-{arch}.exe`、目录 `{v}\terracotta-windows-{arch}`、阶段 `terracotta-download`/`terracotta-extract`/`terracotta-ready`（92/100）。
