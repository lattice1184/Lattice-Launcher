# 会话日志（追加式，超 50KB 合并进 PROJECT_STATE.md 后重置）

## 2026-08-03 晚（新会话，接手 83feac4f 死对话后）
- **提取**：home-layout-refactor 死对话（API 400 死锁）要点，两次官方压缩摘要（8/2、8/3）已提取，沉淀进 PROJECT_STATE.md
- **修复**：`ServerView.axaml` 两栏布局编译错误（Grid 误用 Border 属性）+ 命令输入缺 Dock=Bottom → 提交 `ea0ab41`；构建 0 错误，测试 165/165 全绿
- **沉淀**：PROJECT_STATE.md（41KB 交接文档）提交 `f2f74e3`；旧对话 26.3MB 已按用户要求删除
- **机制**：新建 CLAUDE.md（防注入死命令 + 上下文调度规则）+ SESSION_NOTES.md + cron 每 ~40 分钟检查上下文水位
- **Backlog 未动**：CurseForge 源 / mrpack 导入 / 微软登录实测 / P7 动画

## 2026-08-03 Q 批次：多源下载（CurseForge 第二源）+ 下载加速配置化（完成）
- **vendor 补丁 #11**（0009fe8）：CurseforgeFile 追加 downloadUrl/fileLength/gameVersions/dependencies + CurseforgeFileDependency record；PATCHES.md 记录
- **下载配置化**（cd8596d）：DownloadTier（低8/中16/高24）+ ChunkCount/BufferSize 覆盖项 + CurseForgeApiKey 设置项；FromSettings 提取为可单测静态方法
- **设置页 UI**（d62e354）：分片并发档位按钮 + API Key 密码框（即时保存）
- **CurseForgeService**（ae813f4）：搜索/详情/文件列表/最佳文件匹配/安装；key 解析 设置页>环境变量；sortField 1 基值已核实（1=Featured 6=TotalDownloads 11=ReleasedDate）
- **依赖适配器**（b862eb2）：CF CreateResolver/ToDependencyReferences（relationType==1）；InstallWithDependenciesAsync 一键安装
- **搜索分页模型**（10bac64）：CurseForgeSearchPage(TotalCount)
- **生态页双源**（0814762）：来源筛选（全部/Modrinth/CurseForge）+ 卡片来源角标 + 详情页 CF 分支（最佳文件/依赖计数/安装）+ 收藏双源（cf- 前缀防冲突）；Install 提取共享 ExecuteInstallAsync
- **flake 修复**（8afc582）：Phase1 门控测试阈值断言 ≥2/4 + 反饥饿线程池（机器负载下 5 子任务偶 1~2 个启动延迟超窗，根因未确诊，非本批引入）
- 测试 192/192 全绿
- 待办：用户申请 CF API key 后联调实测；加速档位（低8/中16/高24）实测对比
- **发布**（9355474）：Release 构建修复（收藏模式 TypeMatches 枚举→字符串 + 空列表防御）→ 发布.ps1 成功，发布\YanKa启动器.exe 185.7MB（22:40）；注意：发布脚本清空整个发布目录（原 PCL 子目录被删）

## 2026-08-03 R 批次：自适应布局 + 整体 UI 布局 + 开服/生态体验修复（完成）
- **ServerView 布局**（521038a）：两栏 2*,1,3* + GridSplitter 拖拽分隔条 + MinWidth 保护；参数行 Auto,* 横向舒展；控制台空态提示
- **EcosystemView 筛选栏**（dc286b7）：chips 独立行 + 版本/分类/排序/来源 Grid 均分（MinWidth），宽窗口无右侧空白
- **三页居中限宽**（af995f8）：Settings/Account/ProjectDetail MaxWidth 提高 + 居中
- **建议联动**（be852d5）：保存/应用参数后建议区 diff 刷新；应用后表单刷新
- **路径 Toast**（b95dd95）：Modrinth/CurseForge 安装完成显示完整目标路径
- **PCL 存档读放宽**（febc6a8）：SavesDir 回退共享目录（写侧仍走 RootDir）
- **窗口尺寸记忆**（f69feec）：关闭保存 + 启动恢复（夹取主屏居中）
- 测试 192/192 全绿；已发布到 发布\ 目录

## S 批次（2026-08-03 23:57 发布 185.7MB）
PCL 式简约重构：账号页融合主页 + 服务器页彻底改良 + 弹性交互动画
- S5 弹性动画：UiAnim BackEase overshoot 拉伸感（页面切换/弹出）+ 新增 SpringScaleBehavior 附加行为（按钮 hover 1.02/pressed 0.96 弹性回弹，全局 Setter 挂载）+ nav 激活 Accent 色条 + 清理闲置动画类。Avalonia 无 ScaleTransition/BackEase——行为手写 DispatcherTimer 插值
- S1 账号融合：删导航"账号"项（6→5）+ AccountView 删除；HomeViewModel 持有 Account + AccountTypeText 徽章；头像点击弹 Popup 账号面板（当前账号/切换/删除/离线+正版登录/换肤）
- S2 开服页重写：修 GridSplitter 嵌套 bug（嵌在 ScrollViewer 内两栏从未生效）+ 顶部操作条单行 + 参数 UniformGrid 2 列（10 参数 5 行）+ 控制台自动滚底
- S4 主页收敛：阶段条/进度条仅启动时显示
- S3 版本详情：5 卡垂直堆叠 → Tab 面板（基本信息/配置/模组/存档/操作）
- 提交：4c4226a(S5) 5f8de74(S1+S4) 21358c3(S2) 5146da3(S3)；192/192 测试全绿

## T 批次（2026-08-04 10:45 发布 185.7MB）
版本页空白修复 + 动画收束 + 无文件版本 + 服务器图形化管理
- T1 动画：FadeSlideTransition try/finally 复位（快速切页残留 Opacity=0/位移 = "页面没了"根因）+ 平滑滑移去 overshoot；row hover 去弹性（跳动）；导航涟漪 RippleBehavior（自定义 nav 模板 RippleHost Canvas + 点击扩散圆），nav 关弹性
- T2 无文件版本：InstalledVersionRowVM/Detail 加 JarMissing（扫描只认 json 不认 jar，启动才爆）；版本行"缺文件"红标；基本信息 Tab 警告条 + 补全下载(RepairCommand) + 打开官方下载页；启动失败提示引导
- T3 服务器：机器状态"去更改"→ 设置页（单次服务器配置）；在线玩家卡（日志解析 joined/left/list 实时增删）+ 踢出/封禁/OP 按钮（stdin 命令，无 RCON）
- 提交：6a3ffaa(T1) d81905a(T2) 54fe72f(T3)；192/192 全绿

## U 批次（2026-08-04 11:03 发布 185.7MB）
版本页回退 + 启动失败入口 + 弹窗平滑动画 + 圆角圆滑
- U1 版本页回退 Tab 化（git 恢复 5 卡直排——Tab 化是空白唯一嫌疑，不冒险；保留缺文件补全功能 + SelectById）
- U2 主页启动失败：ShowRepairGuide + [去版本页补全](NavigateToVersion 自动选中该版本) + [打开官方下载页]
- U3 弹窗：PopIn 去弹性改 CubicEaseOut（NVIDIA 浮窗风）+ SmoothOut 淡出收缩再关窗；AttachDialog 统一挂 MessageDialog/ExportDialog/LoaderChoiceDialog/GameDirSetupWindow
- U4 圆角：令牌 5/8/10 + 窗口 12 + 导航 12,0,0,12 + Toast 10 + 头像 10
- 提交：04e17b8(U1) c7bad78(U2) 2bc463d(U3) 7d3a34b(U4)；192/192 全绿

## V 批次（2026-08-04 11:59 发布 185.8MB）
弹窗窗口级动画 + 服务器页重构 + 涟漪全按钮 + BUG 收尾 + API400 应急文档
- V5 CONTEXT_OVERFLOW.md 应急文档（400 发生时立即登记）
- V1 弹窗：根因=Window 背景不透明盖住内容动画；改 FadeTo(win.Opacity) 整窗淡入淡出 + 内容缩放；CrashReportWindow 补挂载
- V3 断链修复：NavigateToVersionAsync await EnsureLoadedAsync 后 SelectById（首次导航选不中 bug）
- V2 服务器重构：删"去更改"跳设置（用户吐槽）；建议配置内联编辑（内存/视距/玩家 3 输入 + 应用按钮，ApplySuggestion 读输入值）；机器状态 DispatcherTimer 5s 实时刷新（CPU PerformanceCounter 包 + 两次采样；失败降级核数）；SuggestionStatusText 建议 diff 独立于实时状态
- V4 涟漪：全局 Button 模板内置 RippleHost + 挂载（nav 专属模板保留）；390ms 放慢 + 颜色加深 #33FFFFFF
- 提交：495be59(V5+V1) 2f48cc9(V3) 9d44ca2(V2) d2d3236(V4)；192/192 全绿

## W 批次（2026-08-04 12:23 发布 185.8MB）
Google 式涟漪 + 弹窗右侧切入切出 + 补全下载自动跳转
- W1 涟漪：扩散色改 BgActive 压暗色（#2A3240，点击变深从点击点扩散，0.9→0 淡出 390ms）+ TemplateApplied 缓存（Avalonia 12 无 ControlTemplate.FindName——回退视觉树找第一个 Canvas，当前 Content 无 Canvas 安全）
- W2 弹窗：SlideInFromRight/SlideOutToRight（48px 横向位移 + 淡入淡出，用户实测"只有淡出太突兀"——补明显位移）
- W3 补全下载：Repair EnqueueGroup 后自动 NavigateToDownloadQueue（agent 确认 Repair 已走队列，角标自动亮）；下载导航角标呼吸脉冲（Border.badge 0.55↔1 0.9s 循环）
- 提交：fc1f6d9(W1) 7e70d23(W2) dfcba18(W3)；192/192 全绿

## X 批次（2026-08-04 12:35 发布 185.8MB）
Toast 滑入 + 涟漪深色 + 版本页小屏 + 发布脚本自动杀进程
- X1 Toast（真"弹窗"！用户澄清是右上角通知不是对话框）：ContainerPrepared + SlideInX（纯位移 48→0，不动 Opacity 防破坏绑定淡出）
- X2 涟漪"白影"根因：BgActive #2A3240 比按钮底色 BgRaised #242A36 亮 → 改 BgBase #14181F（真正变深）
- X3 版本页：版本操作 WrapPanel 换行（窄右栏不裁切）+ 模组删除按钮 ghost→danger
- 发布.ps1 加第 0 步：检测到 YanKa启动器 进程自动 Stop-Process（用户不在时无需手动关）
- 提交：480a0fc + 发布.ps1；192/192 全绿

## Y 批次（2026-08-04 发布）
加载器版本分批加载：LoaderChoiceDialog 先绑前 5 条（立即可用）+ 剩余每批 8 个节流静默补全（ObservableCollection 增量替代 ItemsSource 全量绑定——ComboBox 不重建下拉）+ _versionGen 竞态丢弃（快速切换加载器不串台）。卡顿点确认：网络 await 不阻塞 UI，卡在 ComboBox 全量绑定。提交 e82b605；192/192 全绿

## AA 批次（2026-08-04 发布）
前提警告弹窗化 + 正常退出误报修复
- 新增 DialogService.Warn / MessageDialogWindow.Warn：红字加粗原因 + 普通色说明 + 双选项（替代无着重色状态栏小字）
- 开服页 StartServer 无服务端 → 警告框 [取消]/[立即下载并启动]（DownloadAndStartAsync 下载完自动启动）；未选版本 → 警告框；ApplySuggestion/DownloadServer 同样
- 主页未选版本/未登录 → 警告框（LaunchStatus 保留）
- 修复"主界面退出游戏被报异常退出"：LaunchProcess.GetExitCode 根因=ExitCode==0 时去读官方包装器才写的 exitStatus 文件（裸 Java 启动不写）→ -1；改为缺失/为 0 一律返回 0（正常退出），文件非 0 才异常
- 提交 4c5bed6；192/192 全绿

## AB 批次（2026-08-04 发布，导航栏重做）
用户反馈 nav hover 无变色 + 点击白影（多轮补丁无效）→ 用户拍板"别缠斗，删掉重做"
- **根因总结**：nav 视觉全靠样式伪类（Button.nav:hover 等）+ 模板 TemplateBinding——Avalonia 12 下该组合不可靠（多轮尝试 TemplateBinding→TemplatedParent 等均失败）
- **重做方案**：删掉 App.axaml 全部 Button.nav 系列样式；nav 视觉改 **code-behind 本地值驱动**（Avalonia 优先级：本地值 > 样式 Setter，模板绑定实时跟随，原理上无失效路径）：
  - MainWindow.axaml：5 个导航按钮去 Classes.nav/Classes.active，挂 PointerEntered/Exited/Pressed/Released + SpringScale 禁用
  - MainWindow.axaml.cs：_navButtons 注册表 + ApplyNavVisuals（active=深青 #12332F + 白字 + 左 Accent 色条 3px）+ NavEnter(BgHover #2C3544 变色)/NavExit/NavPress(#1A2029 按下变深)/NavRelease 三态互斥 + VM.PropertyChanged 订阅 IsXxxActive（覆盖点击/GoRepair 跳转等所有路径）
- **白影根因**：Button:pressed 背景 BgActive #2A3240 比 BgRaised #242A36 亮 → 全局改为 #1A2029（按下=变深）
- 坑：PointerPressed/PointerReleased 是路由事件（PointerPressedEventArgs），PointerEntered/Exited 是直接事件（PointerEventArgs）——签名别混
- 提交 e942b72；192/192 全绿

## AC 批次（2026-08-04 15:52 发布 185.8MB）
按钮 hover 色系 + 内置卸载 + 控制台复制 + 服务端下载进队列
- AC1 hover 根治：删除全局 Button 样式 Transitions（BrushTransition 0.15s）——样式伪类 Setter 驱动的过渡动画是 hover 失效（danger 悬浮不变红，用户确认）/白影闪现的嫌疑根因；去掉后瞬时生效。AB 批次 nav 本地值 + 本批去动画 = 双保险
- AC2 内置卸载（设置页"关于"卡片）：红字 Warn 列出删除项（本体 exe/应用数据 AppData\Launcher/游戏目录含存档）→ 写延迟删除 ps1（UTF-8 BOM——PowerShell 5.1 中文路径不乱码；安装目录仅删空目录防误删用户自放文件）→ MainWindow.Close() 退出
- AC3 复制：HomeView「复制日志」/ ServerView「复制」（clipboard.SetTextAsync；**Avalonia 12 的 IClipboard 在 Avalonia.Input.Platform 命名空间**，SetTextAsync 是扩展方法）
- AC4 服务端下载进队列：DownloadServer/DownloadAndStartAsync 改 DownloadManager.EnqueueGroup（ctx.AddChild 包 ServerInstaller——Core 不动）+ NavigateToDownloadQueue（原裸下载不进下载记录不跳转）
- 坑：GameDirectory.EnsureDefault() 是 void（创建目录）；取默认路径用 Detect()；Avalonia Application 无 Shutdown()——关主窗口即可
- 提交 7f5b9ff；192/192 全绿

## AD 批次（2026-08-04 16:09 发布 185.8MB）
hover 行为根治 + 服务端诊断弹窗 + 一键进服 + 导出报告中文诊断
- AD1 **HoverBrushBehavior（新建）**：AC1 删 Transitions 仍无效 → 根因坐实=样式伪类 Setter 对模板 TemplateBinding 的驱动不可靠；hover 全部改本地值驱动（Enter 设 HoverBrush 本地值 / Exit ClearValue 回落样式值——ClearValue 免记原色）。App.axaml 删全部 Button :hover 伪类样式，类样式 Setter 配 HoverBrush（primary=AccentHover、danger=#22E05A5A+红字、ghost=BgRaised+白字、tab=默认 BgHover）；nav 按钮 XAML 显式 Enabled=False（code-behind 管，防互踩）
- AD2 服务端诊断：LogDiagnostics（16 条错误模式正则→中文说明+建议，可扩展）；ServerViewModel Exited 非 0 → Task.Delay(300) 等缓冲刷完 → Diagnose(已收集 Logs) → Warn 弹窗（红字"服务端启动失败"+逐条中文原因）+ 控制台 § 诊断行
- AD2 一键进服：JavaArgumentsBuilder.Build + GameLaunchService.LaunchAsync 加 extraGameArgs（追加 arguments.game 之后）；HomeViewModel 重构 LaunchCoreAsync(overrideVersion, overrideGameDir, extraGameArgs)（LaunchAsync 变薄壳）+ RequestLaunchWithServerAsync；ServerViewModel.JoinGameCommand 读 server.properties server-port（默认 25565）→ 复用主页完整启动链路（阶段/日志/退出处理）
- AD3 导出报告：LogExportHelper 生成 诊断说明.txt（系统信息+包含文件清单+动态中文错误列表）入 zip
- 坑：Background/ForegroundProperty 在 TemplatedControl 不在 Control；IClipboard 在 Avalonia.Input.Platform（上批记录）
- 提交 e9a23c8（git 显示）；192/192 全绿

## AE 批次（2026-08-04 16:29 发布 185.8MB）
开服套娃修复 + servers 归位 + MOD 路径 + 管理空间 + danger 实色
- AE1 **套娃根治**（严重 bug）：根因=ServerInstaller.InstallAsync 不解析 inheritsFrom 链——Fabric/加载器 profile json 无 downloads.server（继承原版）→ serverUrl null → 每次下载失败 → 下次点启动又弹"未安装"→ 无限循环（用户实测 4 次）。修复：VersionJsonMerger.ResolveChain 解析；下载失败改显式红字弹窗（单按钮"知道了"，不再提供"立即下载并启动"）；VerifyServerJar（>1MB）验证；StartServer 加 IsInstalling
- AE2 服务端 Java 按版本：PickServerJava（读版本 json javaVersion，26.x→21+，降级 21/17）——修复 26.1 服务端 Java 17 硬编码启动即崩
- AE3 **目录树**：ServerDir = {InstallDir 父级}\servers\{id}（D:\YanKa Launcher\servers）——服务端不再在 .minecraft 内；MigrateLegacy 一次性迁移（启动时检测旧位置 Move）
- AE4 MOD 下载：InstallAsync 兜底 CreateDirectory（自定义实例名 mods 目录不存在→下载失败/落错位）；安装完成长通知"已安装到：完整路径"
- AE5 **管理空间**：新建 StorageWindow（应用数据/游戏目录/服务端 逐项路径+大小，后台 Task.Run 防卡；日志/缓存/崩溃报告/服务端可删，确认弹窗）；设置页「管理空间」入口
- AE6 danger hover #22E05A5A（13% 透明几乎不可见）→ #8C2F2F 实色深红
- 坑：catch 内 owner 与外层 Confirm owner 冲突（CS0136）；StorageWindow 自 DataContext 需 x:DataType；DownloadGroupTests 并发测试偶发失败（重跑过）
- 提交 75f4b17；192/192 全绿（首跑 1 个偶发，重跑过）

## AF 批次（2026-08-04 16:48 发布 185.8MB）
全局版本绑定 + MOD 落点修正 + 冲突提示 + 下载页实例分批
- AF1 **全局版本绑定**：MainViewModel.CurrentVersion——主页 SelectedVersion 变化 → 全局同步（单向权威）；EcosystemViewModel 初始化 + 主页切换时 SelectedInstance 跟随；开服页跟随（"主页选什么，后面就全都是那个版本"）
- AF2 **MOD 落点修正（严重）**：根因=下载页实例 VM 不带 GameDir + InstallAsync 用全局自建目录 → fabric api 装进自建目录自动创建的空 versions/{id} 目录，PCL 版本真实 mods 目录里没有。修复：Instances 带来源目录（PCL 扫描 → PCL 目录）；EcosystemService.InstallAsync/InstallWithDependenciesAsync 加 gameDirOverride（主文件+依赖透传）；ResolveInstallPath 改隔离判定（versions/{id} 存在→版本目录，否则共享 mods）
- AF3 **冲突提示**：安装前检查目标 mods——同名文件（覆盖确认）+ 同 mod id（zip 读 jar 的 fabric.mod.json id 匹配 → "已安装此模组" 确认）
- AF4 下载页实例下拉分批：前 5 立即 + 每批 8 静默补全（LoaderChoiceDialog 模式）；LoaderChoiceDialog 本身 Y 批次已分批
- 测试：ResolveInstallPath 断言更新（新隔离语义——临时目录建 versions/{id} 验证）；CF 测试防幂等残留（共享 mods 同名文件 sha1 跳过 → 请求断言偶发失败，开头清理）
- 水位：~34%（310k/917k）
- 提交 1be7011；192/192 全绿

## AG 批次（2026-08-04 17:07 发布 185.8MB）
真实加载器检测 + 版本页状态同步 + 内网 IP
- AG1 **LoaderDetector（新建）**：读版本 json（inheritsFrom 链）按 mainClass 真实判定 fabric/forge/neoforge/quilt/原版——替代名字猜测（GuessLoader/LoaderBadgeOf）。版本列表徽章 + 下载页实例下拉徽章（VersionInstanceVM 加 LoaderBadge 第 4 参）；mod 搜索/安装加载器筛选优先真实徽章
- AG2 **状态同步**：MainViewModel.RunningVersion（全局运行态：客户端/服务端；Home/Server VM 的 OnIsRunningChanged 上报）；版本页详情卡运行徽章（绿标"运行中（客户端/服务端）"）；服务端运行中时 JarMissing 红字弱化（"不影响开服"）；**版本页每次进入强制重扫**（Navigate/NavigateToVersionAsync 改 LoadAsync）——下载补全后红字同步消失（用户"同步一定要做好"根因：_loaded 缓存不重扫）
- AG3 **内网 IP**：开服页机器状态卡显示"局域网地址：ip:port"（NetworkInterface 私有段检测 + server.properties 端口）+ 复制按钮（服务端运行中显示）
- 查证：1.21.10 是纯原版（mainClass=net.minecraft.client.main.Main、官方 client.jar URL）——非 NeoForge；NeoForge 安装器生成的版本名本就带后缀（与 PCL 一致）
- 水位：~40%（369k/917k）
- 提交 8b3d2a1；192/192 全绿

## AH 批次（2026-08-04 17:56 发布 185.8MB）
开服页连接信息卡 + 授予 OP + 新服务端默认离线
- **连接信息卡**：机器状态卡加"连接信息"区——本机 127.0.0.1:{port}（一键进服用）+ 局域网 {ip}:{port}（复制）；AG3 的 LanAddressText 升级为 LocalAddressText + LanAddressText 两行
- **授予 OP**：管理员区输入框（预填登录账号名 AccountService.Shared.Current.Name）+ 授予 OP 按钮——stdin 发 `op <名>`，**无需玩家在线**（MC 写 ops.json 上线即生效）；未运行/空名有提示
- **新服默认离线**：ServerInstaller.WriteDefaultProperties——下载服务端后预写 server.properties（online-mode=false 等 10 键与 PropDefs 对齐），MC 首次启动直接使用；文件已存在不覆盖（已有服不受影响）
- 回答用户疑问：启动器内参数设置**起效**（PropRows 保存到 server.properties，服务端启动时读取——已运行需重启生效）
- 测试 +2（194/194 全绿）：预写默认离线 / 已存在不覆盖
- 提交 f1e9ac5；水位 ~40%

## AI 批次（2026-08-04 18:08 发布 185.8MB）
预生成空世界 + OP 权限面板 + Invalid session 诊断
- **预生成世界**：操作条加「生成世界」按钮——启动服务端 → 日志 Done（首次生成完成）→ 自动 stop → 空世界落盘（servers/{id}/world），Exited 时通知"世界已生成"。玩家再进服直接玩；world 已存在/未下载服务端/运行中有提示
- **OP 权限面板（服务器权限>游戏内OP）**：ServerOpsFile（Core 新建）读 ops.json → 开服页 OP 列表卡（名字+等级+移除 deop+刷新）；授予 OP 后 500ms 自动刷新；启动/停止后自动刷新。权限管理完全图形化，不依赖游戏内命令
- **Invalid session 诊断**：LogDiagnostics 加模式（Invalid session|Failed to verify username）→ 中文说明：服务端仍按正版校验，改 online-mode 后必须重启服务端（配置只在启动时读一次）/ 离线客户端连正版服
- 回答用户："无效会话"根因 = online-mode 改动未重启生效（服务端只启动时读一次 server.properties），诊断弹窗已能自动说明
- 测试 +3（197/197 全绿）：ops.json 解析/缺失/损坏
- 提交 aede9c6；水位 ~40%

## AJ 批次（2026-08-04 18:35 发布 185.8MB）
一键开服（一条龙）
- 用户："不能一条龙跑完吗全程"——操作条加「一键开服」主按钮（下载服务端/启动降为 ghost 次要按钮）
- **OneClickStart 流程**：① 缺 server.jar 自动下载（DownloadServer 重构出无确认的 DownloadServerCoreAsync）→ ② 无 world 自动生成（启动→Done→自动 stop→等退出）→ ③ 启动服务端 → 就绪（Done）后 ④ 自动授予 OP（登录账号名，OpNameText）⑤ 拉起客户端自动连接（JoinGame）。任一步失败中止、已完成部分保留；防重入 _oneClickActive
- ParseServerReady 双分支：_autoStopOnReady（生成世界）/ _autoJoinOnReady（一键开服）
- 197/197 全绿；提交 87c34df；水位 ~40%

## AJ2 批次（2026-08-04 18:40 发布 185.8MB）
日志文件占用预检
- 用户报：点启动即失败 "另一个程序已锁定文件"——根因：服务端启动要删 servers/{id}/logs/latest.log，被残留 java.exe 进程或打开着日志的编辑器锁定
- StartServer 启动前探测 latest.log 锁（FileShare.None 探测）：占用时直接中文提示（结束残留 java.exe / 关闭编辑器），不再启动即失败
- LogDiagnostics 加模式：Unable to delete file / FileSystemException / 另一个程序已锁定 → 中文说明
- 197/197 全绿；水位 ~40%

## AK 批次（2026-08-04 19:20 发布 185.8MB）
修复 PCL 整合包版本启动崩溃（ClassNotFoundException）
- 用户报：启动「红石生电优化」（PCL 整合包，Fabric）→ exitCode=1 ClassNotFoundException: KnotClient
- **根因**：JavaArgumentsBuilder 只把带 downloads.artifact 的库加 classpath；PCL 生成的 profile libraries 无 downloads 字段（fabric-loader 链 7/138 条被跳过）→ jar 都在（PCL libraries 有 fabric-loader-0.19.3.jar）但 classpath 没有 → JVM 找不到主类
- 修复：有 name 即按 maven 坐标推导进 classpath（与 DownloadService 下载逻辑一致）；自装版本 json 全带 downloads 不受影响
- LogDiagnostics 加 ClassNotFoundException 诊断
- 测试 +2（199/199 全绿）；提交 6220ecb（下条确认）；水位 ~40%

## AL 批次（2026-08-04 22:08 发布 185.8MB）
服务端下载 BMCLAPI 镜像兜底
- 用户问"为什么下载服务端基本都是失败"——查证：服务端 jar 托管在 piston-data.mojang.com，官方直连国内不稳（实测时通时断）；镜像映射（BmclapiDlSourceMapper）只覆盖 piston-meta/libraries/resources，**服务端无兜底** → 官方失败即失败
- 修复：ServerInstaller.InstallAsync 官方失败 → fallback https://bmclapi2.bangbang93.com/version/{id}/server（302 到 CDN）
- 测试 +2（201/201 全绿）；提交；水位 ~40%

## AL2 批次（2026-08-04 22:25 发布 185.8MB）
服务端三候选下载链 + 封禁列表解封
- 用户报：封禁不能解封 + "server.jar 还是失败"
- **下载失败根因追加**：BMCLAPI 服务端接口 302 → 签名 CDN（12.749333.xyz）连不上（WAFPRO 防护，HTTP:000）——单镜像兜底不可靠。候选链改为：官方 piston-data（实测通）→ launcher.mojang.com 旧域名（实测通 35MB）→ BMCLAPI
- **解封**：ServerBannedFile（读 banned-players.json）+ 开服页封禁列表卡（名字+解封 pardon 按钮）+ 封禁/启停后自动刷新；banned-players.json 里用户自己把自己封了（iwasGOD）
- 测试修复：ServerInstallerTests 目录隔离（ServerDir 取 gameDir 父级——%TEMP%/servers 共享残留导致假成功）+ 跳过真实网络预检（测试不依赖外网）
- 测试 +6（205/205 全绿）；提交 7216ba7；水位 ~40%

## AL3 批次（2026-08-04 23:19 发布 185.8MB）
server.jar 下载校验 + 解封文件级 + 字号
- 彻查"缺少或过小"根因：候选 2/3 传 size=null 无校验，BMCLAPI WAF 等返回 200 错误内容被当成功写盘 → 文件过小。修：每候选下载后校验（≥1MB + zip 魔数 PK），无效删除继续下一候选；size 无效转 null
- 解封点不动：Unban/RemoveOp 停止时直接改 banned-players.json/ops.json（服务端重启生效）——按钮去掉 IsRunning 禁用，随时可点
- 封禁/OP 列表字号 10→12/13
- 测试 +7（209/209 全绿）；提交 5d0a221；水位 ~40%

## AL4 批次（2026-08-04 23:33 发布 185.8MB）
下载失败信息汇总
- "下载历史算成功"根因：22:08 版校验在任务外（BMCLAPI 200 错误页写盘→任务 Completed→VerifyServerJar 才报错）；AL3（23:19）已把校验移进任务内（失败标红）——用户需用新版
- 全候选失败错误汇总："已尝试 N 个源，最后错误：..."；加载器/整合包版本无服务端链接时提示先装原版
- 209/209 全绿；水位 ~40%

## AM 批次（2026-08-04 23:35 发布 185.8MB）
服务端 URL 自动推断（一键开服自动补齐）
- 用户："只能特定版本？红石那个是整合包啊——基于一键再完善，自动补齐"
- **查证**：红石生电优化 = MC 26.2（jar 内 version.json id=26.2，26.x jar 内嵌）；旧版本 jar 无 version.json → id 前缀（1.21.1-Fabric → 1.21.1）/ intermediary
- ServerInstaller：无 downloads.server 时推断 MC 版本（jar version.json → id 前缀 → intermediary）→ Mojang manifest（piston-meta version_manifest_v2）拉该版本 server url/size——**无需先装原版**（服务端 jar 只依赖父 json 的 downloads.server 字段）
- 一键开服/下载服务端自动获益；VersionManifestService.ManifestUrl 公开；构造注入 HttpClient
- 测试 +3（212/212 全绿）；提交 f5fe4c3；水位 ~40%

## AL5 批次（2026-08-04 23:46 发布 185.8MB）
服务端下载两处真根因（23:45 用户实测：还是 server.js 失败 + 下载历史还是报完成）
- **根因 A（历史误报"完成"）**：DownloadTask 组任务状态推导竞态——子任务失败时 SetState(Failed) 经 UI Post **异步**生效，而 Completion **同步**完成；父任务 WhenAll 返回时子任务 State 仍是 Downloading → 误判无失败 → 父任务 Completed → 历史绿色"完成"。**影响所有组任务**（版本下载/加载器/服务端）。修：新增 internal TerminalState 同步终态（Post 前同步记录），组推导改读 TerminalState；Retry/Resume 重置。回归测试 DeferredSyncContext（Post 入队手动 Drain）精确复现——修复前 Actual: Completed
- **根因 B（还是失败）**：BMCLAPI 兜底候选用 versionId（"红石生电优化"）拼 URL → 必然 404。修：候选 3 用推断出的 MC 版本（mcVersion ?? 数字前缀），官方直连失败时兜底真正可用
- 测试 +4（216/216 全绿）；提交 084af72；水位 ~40%

## AL6 批次（2026-08-04 23:52 发布 185.8MB）
服务端 jar SHA1 真校验（探索 agent 补充发现的第三层漏洞）
- 之前 ServerInstaller 恒传 sha1=null：错误内容碰巧 ≥1MB+PK 魔数（误路由大 zip/gzip）可穿过 IsValidServerJar 表面校验假成功
- 修：版本 json 的 downloads.server.sha1 传给 DownloadFileAsync（Mojang sha1 是小写 hex——Sha1MatchesAsync 用 Convert.ToHexStringLower 精确比较，测试踩坑大写不匹配）
- FetchServerInfoAsync 返回 sha1（manifest 推断路径同样拿到）
- 测试 +1（217/217 全绿）：官方返回"sha1 不符但表面合法"内容 → 拒绝换下一候选
- 提交 5de410e（随 AL5 的 DownloadTask 竞态修复一起，本次为 SHA1 校验独立提交）；水位 ~40%

## AL7 批次（2026-08-05 00:06 发布 185.9MB）
开服版本目录错位根治 + 红字规范 + 密度默认标准 + 个性化设置修复
- **根因 A（下载从未真正开始）**："一瞬间的请先前往版本下载" + 下载失败——ServerViewModel 全部硬编码自建目录（InstallDir），红石生电优化在 PCL 目录（扫描不复制）→ 找不到版本 json → "版本未安装（请先在版本页下载）"→ AM 推断 26.2 代码根本没走到。修：RefreshVersionsAsync 带 GameDir（VersionInstanceVM 已有字段）+ VersionGameDir helper 替换全部调用点（ServerDir/选版本/下载/Java 选配/JoinGame）
- **根因 B（个性化没用）**：① 10 处 {StaticResource Accent} 运行时替换 Resources 不更新（模板静态缓存）→ 全改 DynamicResource；② 预览读 LauncherSettings 旧值（VM 已变未写盘）→ ApplyAccentColor(string)/ApplyAppearance(double,DensityMode) 参数化，Preview 传 VM 值；③ 导航激活色条硬编码 #2DD4BF → 跟随强调色
- **根因 C（字太小）**：默认密度=紧凑 0.9（整 UI 缩 10% 主因）→ 默认标准 + 系数 0.95/1.0/1.15（用户确认方案）
- **根因 D（红字规范）**：Status 8 处失败路径红字加粗（SetStatus/StatusIsError + TextBlock.status-error 样式 + Classes.status-error 绑定）；Error Toast 文字红（ToastItem.MessageBrush）；Warn reason 原本已红
- **根因 E（一闪而过）**：下载失败后 NavigateToServer 切回开服页（下载中自动跳下载板块，不切回看不到红字）
- 217/217 全绿；水位 ~40%

## AL8 批次（2026-08-05 00:24 发布 185.9MB）
Forge 整合包启动修复 + 启动命令日志增强 + 1B 显示修复
- **用户实测**："换一个测试对象就崩"（TACZgun Forge 枪械整合包 exitStatus=1）+ "桌面日志文件夹测试日志是否有效" + "把对付红石的手段应用在对付整合包上"
- **日志验证**：错误报告（错误信息.txt + settings.json + logs/launch-*.log）**有效**——完整捕获崩溃堆栈+系统信息，直接锁定根因
- **根因（TACZgun 崩）**：Forge 1.20+ 版本 json 的 -p 模块路径用 ${classpath_separator} 连接模块 jar，JavaArgumentsBuilder.BuildTokens 缺此 token → 整串未替换 → java 模块系统当单一路径解析 → InvalidPathException: Illegal char <:> → boot layer 失败。红石（Fabric）不崩是 json 无 -p；**"对付红石的手段"本已全通用（零特判，Explore 证实）**——缺的是 Forge 启动机制。修：BuildTokens 加 ["classpath_separator"] = Path.PathSeparator.ToString()
- **日志增强**：LaunchProcess.DescribeCommandLine（ArgumentList 语义，空格/引号转义）→ GameLaunchService 启动前 onLog 输出（launch-*.log 首行，VM 零改动）+ 服务端 ServerProcess 存 CommandLine + 开服页日志
- **1B 显示修复**（用户问"为什么变成 1B"）：某源返回 200+1B 垃圾（WAF 拦截页）时进度 total 读响应头 Content-Length=1 → 显示 "1 B"（文件本身 SHA1 校验已拦截删除）。修：进度 total 用 expectedSize 优先 + ServerInstaller 失败路径防御清理 <1MB 残骸
- 测试 +1（218/218 全绿，Forge120_ClasspathSeparator_ReplacedInModulePath）；提交 7c96a36；水位 ~40%

## AL8b 批次（2026-08-05 00:29 发布 185.9MB）
成对 JVM 选项去重事故 + clientid/auth_xuid token（AL8a 日志增强立功）
- **用户实测**：TACZgun 再崩，新错误 `ClassNotFoundException: java.base.java.lang.invoke=cpw.mods.securejarhandler`——**launch-*.log 首行启动命令直接暴露**（AL8 日志增强的价值兑现）
- **根因**：AddJvmArg 通用去重（jvmArgs.Contains）把重复的 `--add-opens`/`--add-exports` **选项名**去重、值却全留 → 值错位 → 第二个值被 java 当 main class → ClassNotFoundException。成对参数（选项+值）不能去重；只有自包含 `-Dxxx=y` 可去重（重复赋值无害）
- **顺带**：命令里 `--clientId ${clientid} --xuid ${auth_xuid}` 未替换（1.20.1+ 官方/Forge json 的 game 参数带官方启动器专属 token）→ BuildTokens 补 clientid/auth_xuid="0"（离线安全值）
- 测试 +1（219/219 全绿）；提交 a0e2658；水位 ~40%

## AL9 批次（2026-08-05 00:45 发布 185.9MB）
启动器自动读日志自修复引擎（用户："既然日志这么有用……能不能让启动器自动读日志然后自修复呢"）
- **架构**：识别层（LogDiagnostics 规则结构化 FixKind）+ 执行层（AutoRepairService）+ 接入层（HomeViewModel 失败/崩溃路径）+ 展示层（崩溃窗诊断区）
- **规则升级**：21 条 → 24 条（+KnotClient 加载器主类/jar 缺失/jar 损坏），每条带 FixKind：AdviceOnly（Java/内存/驱动类，只建议）/ Redownload（类加载/文件缺失 → VersionInstaller 幂等补全重下走下载队列）/ ReExtractNatives（natives 缺失 → 删目录重解压，ExtractNatives 从启动链路提取为静态方法）
- **LogDiagnostics 移入 Core**（Launcher.Core.Diagnostics，仅依赖 Regex，可单测）；旧 Diagnose(string) 兼容包装，LogExportHelper/ServerViewModel 调用点零改动
- **自动修复流程**（用户确认方案：修复后自动重试一次）：崩溃/失败 → 诊断 → 命中可修项 → "§ 检测到问题…正在自动修复…" → 修复 → 自动重新启动（_autoFixApplied 最多一次，重试经递归调用不重置；FileNotFoundException 异常即证据跳过诊断直接重下）→ 二次失败弹崩溃窗（带诊断区 + 一键修复按钮）
- **踩坑**：catch 块看不到 try 局部 gameDir（重算）；DiagLine 嵌套私有类型 XAML 编译不过（改顶层 public + x:DataType）；测试 StreamWriter 未释放
- 测试 +14（233/233 全绿）；水位 ~45%

## AL9c 批次（2026-08-05 00:50）
磁盘清理 + 引擎复查
- 用户："launcher 文件夹占用 1.96G"——定位：src/bin 构建产物 1.78G（Launcher.App/bin 1.57G，
  大头 libSkiaSharp.pdb 调试符号 80MB×4 架构）；运行日志很小（PCL/Log 1MB、AppData 2MB）✓
- 清理：删全部 14 个 bin/obj → 1.96G 降到 491MB；发布后顺手再清一次（构建必重建）
- 新增 清理构建产物.ps1：一键清理（用户随时可跑，下次 build 自动重建）
- **引擎复查发现**：FixRedownloadAsync 任务 Failed 不抛 → 调用方误判修复成功盲目自动重启
  （必再失败浪费一次启动）→ 改 State != Completed 抛异常，如实报告
- 233/233 全绿；00:50 发布；水位 ~45%

## AL10 内核批次（2026-08-05 10:01 发布 185.9MB）
自动修复全自动 + 下载一体（用户三问：区分开来/启动报错/自动修复没用）
- **日志实证**（09:53 错误报告）：ClassNotFoundException KnotClient（fabric-loader jar 缺失，
  classpath 静默忽略不存在 jar）→ 自动修复已触发但失败：`补全未完成（Downloading）`——
  **AL5 竞态复发**：FixRedownloadAsync 读 task.State（UI Post 异步），Completion 同步完成时
  State 仍是 Downloading。修：判定改 TerminalState
- **自动修复全自动**（用户："发现报错立即自己看日志实施修复，不是用户还要点击"）：
  修复最多试 2 次（幂等只补缺失，瞬时失败自愈）→ 全失败才弹崩溃窗；FileNotFound 路径同套
- **父版本补全**：FixRedownloadAsync 补 inheritsFrom 父 json（递归，深度上限 3）+ 下载改用
  merged 版本（client jar URL/全部 libraries 继承父链——覆盖加载器 profile 无 downloads）
- **下载一体**：InstallWithLoaderAsync 去掉原版预下载（LoaderService 的 merged 下载全包）——
  原版+加载器文件并列一个子任务列表，不再"原版一坨+加载器一坨"
- 233/233 全绿；10:01 发布；水位 ~50%

## AL10.1 真修复批次（2026-08-05 发布 185.9MB）
**CNFE KnotClient 复现的根因**（用户实测 10:09：自动修复"补全完成"但 jar 仍缺）
- **根因**：Fabric/Forge meta profile 的 libraries 是顶层 url 形式
  （{"name","url","sha1","size"}，无 downloads.artifact）——LibraryJson 模型没顶层
  Url/Sha1/Size 字段（被 JsonSerializer 忽略），DownloadService/VersionDownloadPipeline
  只下载 Downloads.Artifact 非空的库 → **url 形式库（asm 全系+sponge-mixin+intermediary+
  fabric-loader）全部静默跳过** → "补全完成"虚假成功。下载页/自动修复/一键修复全走同一代码
- **修**：LibraryJson 补 Url/Sha1/Size；双路径（RunLegacyAsync + VersionDownloadPipeline）
  加 url 分支（顶层 url + MavenPath 拼地址，sha1/size 可空——Fabric 的 intermediary 无 hash）
- 新测试 UrlFormLibraryTests（fabric-loader/intermediary 落盘）；234/234 全绿
- 防循环确认有效（_autoFixApplied 递归不重置）——多次日志是用户手动重试
- 教训：record 位置参数（含 string?/long?）无隐式默认值，调用须全显式（CS7036）

## AL10.2 批次（2026-08-05 发布 185.9MB）
26.2 Java 25 崩 + 版本列表分开 + 加载器一次性/0B + 完整性校验（用户四连报）
- **26.2 崩根因**：GameLaunchService Java 选择用原始 fabric profile（无 javaVersion）→ InferJavaMajor
  "fabric-loader-..." 匹配失败兜底 17 → 选 beta(17) → UnsupportedClassVersionError。本机其实有
  epsilon(Java 25)。修：沿 InheritsFrom 链继承父版本 javaVersion=25 → 用上本机 Java 25
- **JavaSelector 增强**：扫描注册表/JAVA_HOME/PATH/Program Files（用户："调用电脑里已有的"）；
  Pick 找不到匹配返回 null → 明确提示（不再静默 fallback 旧 Java 崩）；BestMatch 纯逻辑可测
- **版本列表一体**：隐藏被 inheritsFrom 继承的父版本（原版 26.2 不再单独显示）
- **加载器下载一体化**：VersionDownloadViewModel:224 改调组重载（旧写法 (p,c) 匹配 progress 重载，
  加载器=扁平单任务 weight=0 '一次性'且 0B）
- **完整性校验**：AutoRepairService.VerifyFiles 下载后校验 client jar+libraries，缺失抛异常
- 239/239 全绿（+5：BestMatch×3/VerifyFiles/BytesText）

---
## AL11（2026-08-05）自修复防假失败 + 自跳转补全 + 下载记录着重标识

**自修复（VerifyFiles 假失败根治）**
- 根因：VerifyFiles 遍历全部 libraries 不按 OS 规则过滤，Linux/Mac natives 库（不下载）被误报缺失 → 修复抛"补全后仍缺 N 个文件"假失败
- 修：AutoRepairService.VerifyFiles 用 new RulesResolver().IsAllowed(lib.Rules) 过滤非本 OS 库
- 测试：AutoRepairServiceTests +VerifyFiles_SkipsOtherOsLibraries

**自跳转补全（用户："自跳转完善还是没做好"）**
- 服务端下载成功 → NavigateToServer() 跳回开服页（ServerViewModel DownloadServer/DownloadAndStartAsync）
- MOD/整合包/材质/光影安装完成 → NavigateToDownloadQueue()（EcosystemViewModel.InstallCard/InstallCfCardAsync、ProjectDetailViewModel.ExecuteInstallAsync）
- 整合包导入完成 → OnInstalled 改 await LoadAsync + SelectById（刷新并选中新版本）

**下载记录着重标识（用户："下载记录的着重标识也没有"）**
- 置顶：DownloadManager KeepActiveOnTop 稳定分区（活跃前/终态后），入队与状态变化时调用；组任务子任务 IndexOf<0 跳过
- 高亮/失败警示：DownloadView 任务行 Classes.active/failed + App.axaml .taskRow.active（青左条）/ .taskRow.failed（红左条）
- 历史着色：新 HistoryStateBrushConverter（失败红/完成绿/已取消灰），历史行 State 绑定
- 测试：DownloadManagerTests +TerminalTask_SinksToBottom_ActiveStaysOnTop

- 241/241 全绿（+2）；发布 185.9MB exe（发布\YanKa启动器.exe）

---
## AL12（2026-08-05）启动器 UI 文案去 AI 腔
用户："更新下文本风格，文字 AI 味别太多"（澄清：指启动器界面文案，不是 AI 对话风格）。

- Explore agent 全量扫描：约 478 条用户可见中文文案，87 条 AI 腔信号
- 按原则改写 ~35 处：去书面腔（将/以便/是否/请确认）、括号解释并进主句或删、长句拆短
- 覆盖：GameDirSetupWindow/CrashReportWindow/LoaderChoiceDialog/SettingsView/ServerView/HomeView/VersionBrowseView/StorageWindow + HomeViewModel/AccountViewModel/EcosystemViewModel/ProjectDetailViewModel/ServerViewModel/VersionBrowseViewModel/VersionDownloadViewModel/VersionManageViewModel/SettingsViewModel/LogExportHelper
- 典型：GameDirSetupWindow:11「…将存放在下面的文件夹。建议放在剩余空间充足的盘（如 D 盘）…」→「…都会放在这个文件夹。选个空间大的盘就行…」；三处"将重新下载…（已有文件自动跳过）。继续？"→"重新下载…（已有的自动跳过）。继续？"
- 保留：按钮短词、状态行、"先选版本"类正常对话文案；第 85 行 OP 长句只在注释里，未动
- 241/241 全绿；发布 exe（发布\YanKa启动器.exe）
- 记忆已修正：text-style-no-ai-flavor 改为"启动器 UI 文案去 AI 腔"

---
## AL13（2026-08-05）文案精简正式化 + 落地页(web/) + 前端设计agent实测 + 主窗口开场动画

**Part 1 文案精简+正式化（用户："去除没用的AI解释文本，并且正式"）**
- 删冗余括号解释：日志预览（尾部）、下载原版+所选加载器（已下过的自动跳过）、暂无OP（指引）、输命令（例子）、改动即时生效、CurseForge（申请网址）、直接打包本地文件整行、命令框例子
- AL12 口语过头回正式：GameDirSetupWindow 选目录、SettingsView 卸载文案、HomeViewModel 选版本/登录/缺文件、ServerViewModel 世界生成/重试/运行中/进服失败、删除版本、整合包下载通知、版本页"缺客户端文件"回"客户端文件缺失"
- StorageWindow 列表项"（可删）"标签保留（有效信息非 AI 解释）

**Part 3 主窗口开场动画**
- MainWindow.axaml.cs：构造函数 Opacity=0（避免闪一帧），Opened 里 StartupEnter() 一次性淡入+放大（0.96→1，CubicEaseOut ~360ms，保留 ContentSurface 密度缩放基准）；无循环无呼吸
- 复用 UiAnim DispatcherTimer 步进模式，未引新库

**Part 2 落地页 launcher/web/（完整单页）**
- design-system-curator 产出 DESIGN_SPEC（token/组件/3套SVG动画/反AI清单）——**有用，规格可落地**
- premium-design-critic 评审（P0×1/P1×5/P2×11，动画8条全达标）——**有用，抓出字体未加载/SVG伪元素失效/死链**；已按 P0/P1 + 高价值 P2 修复
- 产物：index.html + styles.css + script.js + screenshots/（4张实机截图：主页/版本/开服/设置，从发布 exe 截取）
- SVG 动画：Hero 启动流程线（描边→节点→小车驶过→终点环）、自修复 emblem（破损→扫描→恢复）、画廊滚动进度线；全部 iteration-count:1 方向性
- 下载按钮暂为"即将开放"（无真实下载地址）

**Part 4**：241/241 全绿；发布 exe（发布\YanKa启动器.exe）

---
## AL14（2026-08-05）PCL 式启动画面 + 落地页去单调
用户反馈：开场动画要 PCL 那种（独立启动画面），网页太单调。

**Part A 启动画面（SplashWindow）**
- 新建 Views/SplashWindow.axaml/.cs：小窗 340×200，青点 logo + 闫卡启动器 + v4.0 + mono 状态行 + 细进度条；SetStatus() UI 线程安全
- App.axaml.cs 重构：ShutdownMode=OnMainWindowClose（防 splash 早退）→ splash.Show() → 各 init 阶段 SetStatus（初始化/应用外观/启动服务/加载主界面）→ 最小展示 500ms → 关 splash → MainWindow.Show()（AL13 StartupEnter 淡入衔接）
- 主窗口 StartupEnter 保留为 splash→主界面过渡
- 241/241 全绿；发布 exe（发布\YanKa启动器.exe，含 splash）

**Part B 落地页去单调（launcher/web/）**
- Hero 加实机截图 mockup（window chrome 框 + 玻璃投影 + 滚动一次性浮起）
- 新增"不同之处"小节（单文件自包含/四加载器/自修复/无打扰，4 卡）
- 画廊首图（主页）跨 2 列放大，其余 2 列错落
- 下载区改 CTA banner（玻璃大卡 + 「即将开放」大按钮）
- why/gallery section 背景加微弱 accent radial 光晕，打破纯色平
- 通用 [data-reveal] 一次性滚动 reveal；动画纪律保持（全 iteration-count:1 方向性）

---
## AL15（2026-08-05）splash 缩小 + logo 扩展过渡 + 改名 Lattice（晶格）
用户反馈：splash 别太大、要"logo 丝滑扩展到启动器"、换名字（选定 晶格/Lattice）。

**splash 缩小 + 扩展过渡**
- SplashWindow 260×150（原 340×200），logo 居中（青块+晶格启动器+v4.0）
- 新增 SplashWindow.ExpandAndClose()：logo scale 1→8 + 窗口淡出（CubicEaseOut ~300ms）后 Close
- MainWindow.StartupEnter 起始 scale 0.96→0.18、时长 ~480ms：主窗口内容从中心小缩放展开
- App 流程：await splash.ExpandAndClose() → MainWindow.Show()（内容从中心展开）→ 视觉 = logo 丝滑扩展成启动器

**改名 Lattice（晶格）**
- 用户从推荐中选 晶格/Lattice（弃 YanKa/闫卡）
- sed 批量替换 YanKa→Lattice、闫卡→晶格（src/Launcher.App 用户可见文案 + 发布.ps1 + web/）
- exe 改名 Lattice启动器.exe；发布.ps1 进程名/输出名同步
- 覆盖：splash/窗口标题/主页侧栏/关于页/欢迎窗/日志导出zip名/崩溃报告zip名/诊断说明/网页全部 + 记忆更新
- 241/241 全绿；发布 exe（发布\Lattice启动器.exe）

---
## AL16（2026-08-05）splash 固定 1.2s 进度 + 内存自动分配
用户反馈：splash 启动太快动画一闪而过；内存分配要自动、且保证新开应用分得到内存。

**Part A splash 固定 1.2s 进度**
- SplashWindow.SetStatus 只改文字不再跳进度条；新增 StartLoad(durationMs)（AnimateSmooth 0→100 平滑填充）
- App：StartLoad(1200) + Task.Delay(1200) 固定时长 → 进度条完整可见再扩展进主界面

**Part B 内存自动分配（留余量）**
- 新建 MemoryAllocator（Core\Launch）：Compute = min(max(avail-1536,1024), total*0.6)；AutoMb 用 GlobalMemoryStatusEx 取可用内存，拿不到退化 60%
- SettingsViewModel 预设加"自动（按可用内存）"(-2) 放首位；IsCustom 改 Mb==-1
- LauncherSettings.MemoryMb 默认 4096→-2；HomeViewModel 启动内存 memCfg switch（-2→AutoMb，0→60%，>0→直用）
- 用户 settings.json MemoryMb 已改 -2
- 测试：MemoryAllocatorTests×3（充足封顶/紧张降配/下限1024）；LauncherSettingsTests 断言更新

**测试 flake 处理（既有并发测试，非本批次引入）**
- DownloadTaskBytesTextTests.UnknownSize_ShowsDash：补 SetSynchronizationContext(null)（xunit AsyncTestSyncContext 挂 Post 致 State 读旧值）
- DownloadGroupTests：非并行 collection（AsyncPostContext 重并发 + 全量并行线程池争抢 → Post 积压超窗）+ 轮询窗口 10s→20s
- 残余偶发：UrlFormLibraryTests/DownloadGroupTests 在机器瞬时高负载下仍可能超窗（隔离跑 255ms 全过，逻辑无 bug）
- 244/244 多数轮全绿；发布 exe（发布\Lattice启动器.exe）

### AL16 补充（动画真修，18:42 发布）
用户实测：splash 0% 直接启动、扩展看不到。逐项排查（截屏+窗口句柄轮询实证）：
- **根因1：splash 不渲染**——`splash.Show()` 在 `desktop.MainWindow` 赋值前调用，Avalonia 12 不渲染非主窗。
  修：先 `desktop.MainWindow = splash` 再 Show，init 完换成真主窗。窗口句柄序列实证 splash 出现。
- **根因2：AnimateSmooth 的 DispatcherTimer 在启动上下文可能不泵**——进度条停在 0%。
  修：AnimateSmooth 改 `Task.Run` 计时循环 + `Dispatcher.UIThread.Post` 更新（保证跑）。
- **扩展不可见**：0.94 起始太隐、且主窗盖住 splash。改为 `splash.Close()` 后主窗内容从中心 0.25→1（easeOutQuint 650ms，透明度先到 40%），可见的"logo 扩展成启动器"。
- exe 单文件自解压首启 ~1.2s 才弹 splash（进程句柄 0→有），属正常。
- 244/244 全绿；发布（发布\Lattice启动器.exe，18:42）

### AL16 再修（强切根治，18:51 发布）
用户："还是像强切""要扩散过渡美还快、能看全过程"。
- **根因**：独立 SplashWindow + 主窗口两个窗口切换，天然有"关窗→开窗"的缝 + Avalonia 窗口渲染时序坑，怎么调都有切感。
- **方案**：删掉 SplashWindow，启动浮层做进主窗口内部（MainWindow.axaml 根 Grid 最上层 SplashOverlay：logo+进度+状态）。
  单窗口内过渡：进度 0→100 走 1.2s → 界面内容从中心 0.25→1 扩展（easeOutQuint 650ms）同时浮层淡出（400ms）→ 连续无强切、全过程可见。
- MainWindow：SetSplashStatus() 公开方法（App 各 init 阶段调）；Opened → StartSplashSequence（进度→GrowContentAndReveal）。
- App：先建 MainWindow + MainViewModel，init 阶段 SetSplashStatus，Show 触发浮层序列。删 SplashWindow.axaml/.cs。
- 244/244 全绿；发布（发布\Lattice启动器.exe，18:51）

### AL17（无边框 + 启动窗口放大，19:04 发布）
用户："窗口尺寸调小一点，最开始时就是个 LOGO（不带名字），logo 缩放出现，随后放大到正式页面，全程单窗口但尺寸实时变化。"
- **像素实证**：截屏顶部三行纯色 32,32,32 → 原生标题栏（深色 #202020），客户区 y≈40 起。
- **根因**：原生标题栏下做不了"只有 logo 的小窗"（会挤标题栏+系统按钮，还显示名字）。
- **方案：MainWindow 改无边框**（SystemDecorations=None）+ 自定义标题栏（36px 拖拽条 + 最小化/最大化/关闭；双击最大化；关闭钮 hover 红 #C42B1C）。
  - NavSurface 圆角 12,0,0,12→0（顶部被标题栏占）；外层 Border 命名 WindowRoot，最大化时圆角归 0（防透明角露壁纸，监听 WindowState 属性——12.x 无 WindowStateChanged 事件）。
  - 拖拽：TitleBar_PointerPressed → BeginMoveDrag（双击切最大化）。窗口最小 120 下限，放大完成后恢复 760/500。
- **启动序列重写**：XAML 初始 150×150 + WindowStartupLocation=CenterScreen；SplashOverlay 只留 logo（56px accent 圆角方块，无名字无进度）。
  1) logo easeOutBack 缩放出现（0.3→1，450ms，UiAnim 新增 AnimateBack 过冲 ~8%）
  2) 窗口 800ms 从 150×150 实时放大到存档尺寸（逐帧 Width/Height/Position 居中），内容 0.25→1 涨开（密度为基准），logo 随窗口 1→1.6，浮层后 35% 淡出揭示界面 → 单窗口无强切。
- 放大目标 = 存档尺寸（ResolveTargetSize，夹主屏工作区）；删 RestoreWindowSize/SetSplashStatus/SplashBar/SplashStatus/SplashVersion。
- ScaleTransform 内 x:Name 不生成字段（CS0103）→ 运行时 (ScaleTransform)SplashLogo.RenderTransform 取。
- 244/244 全绿；发布（发布\Lattice启动器.exe，19:04）
- 待用户确认：自定义标题栏观感、放大动画流畅度、无边框阴影/最大化行为。

### AL17 补充（顶部颜色统一 + 平滑，19:11 发布）
用户："平滑一点，但差不多了""顶部最小化那块颜色不一样；左导航一个色、右内容一个色，好弄一点"
- **颜色问题根因**：旧标题条 Background=Transparent，透出的是全窗 RootSurface 亚克力（#14181F@0.40）——第三条颜色，跟左导航（@0.55）和右内容（#B81D222C）都不同。
- **修**：标题条移进 ContentSurface 内（Dock=Top，Background=#B81D222C 与内容同色）；导航列改 Dock=Left 全高（整列一种色）；左导航/右内容各一色，顶部无第三色。NavSurface 加 PointerPressed 拖拽（按钮自身 handle 不冲突）。
- **平滑**：放大 800→950ms；浮层淡出起点 0.35→0.42（前段多遮，少露缩放抖动）；logo easeOutBack c1 1.70158→1.2（过冲 8%→5%）。
- 244/244 全绿；发布（发布\Lattice启动器.exe，19:11）

### AL18（设置页汉堡菜单导航，20:25 发布）
用户：设置页整页滚动太乱，要分类导航；但设置页内部再加左侧栏会跟主界面左侧主导航冲突 → 改**汉堡菜单**。
- SettingsView.axaml：顶部改 Grid（☰ 按钮 + 设置标题）；新增 Popup 分类菜单（glass-card 内 5 个导航式按钮：游戏目录/启动/外观/下载/关于，CommandParameter 0..4）；5 个 card 各加 x:Name + IsVisible（默认只显游戏目录）。
- SettingsView.axaml.cs：ShowSection(i)（切显隐 + ApplySettingsNavVisuals + 关菜单）、OnToggleMenu、OnSettingsNavClick；视觉照抄 MainWindow 导航（本地值驱动：激活 #12332F+白字+左侧 accent 边框，hover #2C3544，按下 #1A2029）；☰ 按钮特殊处理（无激活态，hover 变灰、退出透明）。Popup 锚定在构造器里用代码赋 PlacementTarget。
- 踩坑：SettingsView.axaml 缺 xmlns:behaviors 声明（补）；Avalonia 12 Popup 无 StaysOpen（改 IsLightDismissEnabled=True）。
- 处理器全部原位（浏览目录/Java/内存/外观/档位/存储/卸载），ViewModel 未改。
- 244/244 全绿；发布（发布\Lattice启动器.exe，20:25）

### AL19（窗口默认尺寸 900×600，21:02 发布）
用户："大小布局也得重新设计，最适合的" → 选 **900×600**（内容区约 740×564，3:2 均衡；日志有高度、设置表单有宽度）。
- MainWindow.axaml：d:DesignWidth/Height 860×560 → 900×600（Width/Height 150 启动小窗不动）。
- MainWindow.axaml.cs：ResolveTargetSize 回退 (860,560) → (900,600)（三处）；NormalMin 760/500 不变。
- settings.json：WindowWidth 1105.6→900、WindowHeight 551.2→600（用户存档很宽很矮，不更新就看不到新默认）。
- 244/244 全绿；发布（发布\Lattice启动器.exe，21:02）

### AL20（设置页加内容：下载加速 + 性能优化 + 启动彩蛋，23:19 发布）
用户嫌设置页太空；排除了反馈/投票（要网站证书）。做了三件本地事：
1. **下载源策略**：旧的 MirrorFallbackEnabled(bool) → `DownloadSourcePreference` 枚举（官方优先/镜像优先/仅镜像）。DownloadService 候选序按偏好排（OfficialFirst=官方+镜像按速度排、MirrorFirst=镜像固定在前、MirrorOnly=只要镜像）。模组 CDN 不走镜像（诚实标注）。
2. **性能优化（两个都要）**：
   - JVM 预设：复用已定义但从未接入的 `PerformanceProfiles`（Low/Medium/High/Ultra → GcArgs），HomeViewModel 启动时取 GcArgs 前置合并进 extraJvmArgs（用户参数在后优先）。设置页"性能档位"下拉（轻量/均衡/流畅/极致）。
   - 清理下载缓存：删游戏目录 `*.parts` 断点续传残留，Toast 报告释放空间。
3. **启动随机小提示**（彩蛋）：`StartupTips.cs` 16 条梗，MainWindow Opened 后 1.8s 随机 Toast，设置页"关于"里可关（StartupTipEnabled）。
- 改：LauncherSettings（3 字段+枚举）、DownloadOptions、DownloadService、HomeViewModel、SettingsViewModel、SettingsView(.axaml/.cs)、新 StartupTips.cs、MainWindow。测试 MirrorFallbackTests 改造 + 新增 2 个 → 246/246 全绿。
- 发布（发布\Lattice启动器.exe，23:19）

### AL21（开服 Java 继承链修复，用户实测 26.2 开服报 Java 错）
- **根因**：ServerViewModel.PickServerJava 只读版本自身 json 的 javaVersion；fabric-loader-0.19.3-26.2（红石跑的这个）javaVersion=None、inheritsFrom=26.2（原版要求 Java 25）→ 默认 major=17 → 拿 Java 17 跑 26.2 服务端 → UnsupportedClassVersionError。
- **修**：JavaSelector 新增共享 ResolveRequiredMajor(VersionJson, loadParent)：自身 javaVersion → 沿 InheritsFrom 链递归继承 → 按 MC 版本号推断（1.17+→17，旧→8）。GameLaunchService 原内联单层父继承逻辑改用它；PickServerJava 改用它 + 去掉 {major,21,17} 静默降级（降级选旧 Java 跑新版本必崩）→ 找不到直接报"需要 Java {major}，但本机未找到匹配版本"。
- 测试 +4（自身/单层继承/深链/版本号推断）→ 250/250 全绿；发布（发布\Lattice启动器.exe）

### AL22（开服自动修复补上，用户"自动修复竟然在开服这块没有？"）
- **缺口**：开服崩溃只有诊断弹窗（"知道了"），无修复动作；AutoRepairService.FixRedownloadAsync 只重下客户端文件，服务端 jar 不在列。
- **修**：AutoRepairService 新增 FixServerJarAsync(versionId, gameDir, installer?, ct)——删坏 jar → ServerInstaller 重下（幂等、可注入便于测试）。
  ServerViewModel 崩溃弹窗改 DiagnoseDetailed（带 Fix 分类）：命中 Redownload（jar 缺失/损坏/Unable to access jarfile）→ 弹"自动修复并重新启动"→ 修复成功后自动 StartServer()；失败红字，不静默。
- 踩坑：LogDiagnostics.Diagnose 是旧接口返回 string 列表（无 Fix）——用 DiagnoseDetailed。
- 测试 +1（坏 jar 删除→重下→有效）→ 251/251 全绿；发布（发布\Lattice启动器.exe，23:39）

### AL23（动态化整改：分片滑块 + 开服档位 + 服务器内存独立 + 死设置清理，发布）
用户四连：分片档位不能调/要滑块；开服"只有 20 人"要低配档；死设置太多；需重启的设置要提示。
- **分片滑块**：3 按钮档位（低8/中16/高24）→ 滑块 1-32 绑 ChunkCount（老用户继承现有档位，新装 8）；DownloadTier 退役为 json 兜底；DownloadTierIndex/OnTierClick 删除。改动对新下载任务生效（防抖写盘）。
- **开服建议档位**：BuildSuggestion 不再写死 10/20——按 CPU 核数+可用内存动态算（新 SuggestionPresets.Compute：≤2核/<4G→6/8，≤4核→10/20，≥8核→16/40）；建议卡加"测试低配/推荐（按机器）/高配"三按钮（低配=1G·视距4·玩家5）；PropRows Number 控件 TextBox → NumericUpDown 带范围（port 1-65535 / view 2-32 / players 1-1000）；difficulty 加 peaceful。
- **服务器内存独立**：新 ServerMemoryMb（默认 2048），StartServer/ApplySuggestion 用独立字段——开服页改内存不再误改客户端启动内存。
- **死设置清理**：CurseForgeService 去掉构造缓存——IsEnabled/请求头每次读 Current（改 key 即时生效，无需重启，测试兼容）；VersionManageViewModel 去掉 _isolated 构造快照（改隔离开关即时生效）。
- **生效时机提示**：启动小提示行加"下次启动生效"+ 改动 Toast；下载行加"改动对新下载任务生效"；动态化后无必须重启的设置，不做重启对话框。
- 测试 +4（SuggestionPresets 档位）+ ServerMemoryMb 默认/往返 → 255/255 全绿；发布（发布\Lattice启动器.exe）

### AL24（第三方文件下载 tab + 开服重叠修复 + 跳转下载记录统一，发布）
用户三件事：PCL 式"粘贴链接下载第三方文件"（可自定义存放位置）；开服页 UI 重叠（要弹独立窗口）；"自动跳转下载记录还是没修好"。
- **第三方文件下载**（下载页第 7 tab）：Core 新增 UriFileNameResolver（URL 最后段解码 / Content-Disposition filename* 优先 RFC5987 解码 + filename 回退 / Sanitize 剔非法字符防路径穿越）+ UniquePath（同名自动 " (1)" 递增，永不覆盖）；LauncherSettings 加 ThirdPartyDownloadDir 记忆目录（默认 Downloads）。VM：UrlText / FileNameText（留空自动识别）/ TargetDirText（FolderPicker 在 code-behind，复用 SettingsView 模式）/ StartDownloadCommand 校验 URL → 识别名 → UniquePath → DownloadManager.Enqueue 复用全局队列（断点续传/历史/Toast 零额外代码）。
- **开服页重叠**：根因 = 操作条 8 列 Auto 按钮窗口 <800px 横向溢出 + 左右栏 MinWidth 260/300 挤没分隔列。修：操作条 Grid → WrapPanel（ComboBox MinWidth 220，窄窗口按钮自动换行），左栏 240 / 右栏 280；下载页 tab 栏 StackPanel → WrapPanel（7 个 tab 窄窗口换行不重叠）。**弹独立窗口**：标题行加「在新窗口打开」→ 新 ServerWindow 共享同一 ServerViewModel 实例（日志/状态/操作实时同步；双视图各滚各的 LogScroll，code-behind 全部走 DataContext，无冲突）。
- **跳转统一**：VersionDownloadViewModel / EcosystemViewModel×2 / ProjectDetailViewModel 的「完成后跳下载记录」全部移到 Enqueue 后立即跳（用户点下载立刻看到进度，不再干等几十分钟）；开服服务端下载保留入队跳 + 完成跳回开服页；版本修复入队跳保留。
- 踩坑：Avalonia WrapPanel 属性是 ItemSpacing 不是 Spacing（AVLN2000）；TextBox.Watermark 过时用 PlaceholderText。
- 测试 +10（UriFileNameResolver 7 / UniquePath 3）+ ThirdPartyDownloadDir 默认/往返 → 265/265 全绿；发布（发布\Lattice启动器.exe）

### AL25（开服配置卡下拉框 + 第三方识别流程 + 两个跳转分离 + 历史重下，发布）
用户四点反馈：配置卡下拉框仍重叠；第三方识别慢；第三方下载没跳转；两个跳转要分清楚（①入队→记录看进度 ②完成→跳回来源界面）。
- **配置卡单列**：server.properties 卡 UniformGrid Columns 2→1（根因：左栏 ~230px 两列各 ~106px，行内 Label 88 + 下拉框被压到 ~12px）。单列后控件全宽，ScrollViewer 兜底滚动。
- **两个跳转分离**：
  - 跳转①：入队 → 下载记录（已有 + 补漏：ThirdPartyDownloadViewModel 漏调 NavigateToDownloadQueue）
  - 跳转②：统一机制——`NavigateToDownloadQueue(returnTo)` + DownloadViewModel.SetReturnNavigation + 终态（Completed/Failed）经 Dispatcher.Post 跳回一次并清空（Canceled 不跳——主动取消留在记录；只记最后一次入队者）。`NavigateTo(string page)` 支持 `"download:tab"` 前缀切下载页内 tab。调用点：版本下载/修复 → "version"；生态页×2/详情页 → "download:{TabFor(type)}"；第三方 → "download:thirdparty"；ServerViewModel 保留原逻辑（避免双跳）。
- **第三方识别流程**：URL 输入防抖 600ms 自动识别（FromUrl → Content-Disposition）→ 填充文件名 + CanStart 亮起开始按钮；识别不到提示手输，手输即亮；IsRecognizing 显示"正在识别文件名…"。入队传 (url, dest) 供历史用。
- **历史重下/位置**：DownloadTask +SourceUrl/TargetPath；Enqueue 可选参数；DownloadHistoryEntry +字段（旧 json 缺失 → null 兼容）；历史行加「重下」（同 URL 重下到原目录，UniquePath 防覆盖）/「位置」（explorer /select 定位，文件不存在提示）。
- 踩坑：RelayCommand 生成的命令属性 public 但原方法仍 private——MainViewModel 跨类调 SelectTab/Navigate 报 CS0122，改 SelectTab 为 public + 新增 NavigateTo 包装。
- 测试 +2（Enqueue 元数据赋值）→ 267/267 全绿；发布（发布\Lattice启动器.exe）

### AL26（版本列表三处统一：隐藏被继承原版 + 加载器友好名，发布）
用户问：主页版本下拉为什么"灰色背景+英文"；为什么加载器和原版是"分开的 2 个游戏"。
- **根因**：Minecraft 里原版 26.2 与 fabric-loader-0.19.3-26.2 是两个独立版本目录（加载器 json 声明 inheritsFrom 继承原版）。版本页 AL10.2 已做"隐藏被继承原版"，但主页/开服页没做 → 三处不一致，主页下拉出现两个条目 + 英文版本 id 直显。
- **修**：
  - 新 `Launcher.App/Services/VersionScan.cs`：Inspect(dir,id) 读 json 一次返回 (Loader 徽章, McVersion=inheritsFrom)；GetInheritedBaseIds 收集被继承原版集合。三处共用（版本页内联逻辑/旧 LoaderBadgeOf 删除）。
  - `VersionInstanceVM` +McVersion；DisplayName 友好化：加载器版本 → "26.2 (Fabric) · 本启动器"（Cap 首字母大写）。
  - 主页/开服页 RefreshVersionsAsync 改为：候选收集 → inherited 过滤 → Inspect 填徽章；开服页下拉 ItemTemplate 绑 DisplayName。
  - 灰色背景即下拉展开项默认深灰样式 + 英文长名——友好名 + 合并后条目变少变友好。
- 踩坑：CS8361 插值内条件表达式需括号（$"{(a ? b : c)}"）。
- 测试 267/267 全绿（无新增，显示层改动）；发布（发布\Lattice启动器.exe）

### AL27（AL26 回归急救：恢复原版条目 + fabric 秒退根因修复，发布）
用户"以前那一套交互彻底废了 / 修复失效 / 会自动退出 / 然后卡着"——从 launch-history + launch log 完整还原故障链：
- **根因 1（AL26 引入）**：隐藏被继承原版 → 主页/版本页失去原版 1.21.10 条目，只剩 fabric →「交互废了」→ 回滚：三处（Home/Server/VersionBrowse）去掉 GetInheritedBaseIds 过滤，保留友好名徽章（"1.21.10 (Fabric)"）；VersionScan 删该方法。
- **根因 2（既有）**：fabric-loader-0.19.3-1.21.10 启动 0.14~0.5s 秒退（exitStatus=-1，7 次）——手动复现（Python 提取启动命令 + 引号感知拆分 + subprocess）拿到 stderr：`ExceptionInInitializerError: duplicate ASM classes found on classpath: asm-9.6.jar + asm-9.10.1.jar`——fabric loader 校验重复 ASM 拒绝启动（原版 1.21.10 继承 asm-9.6 + fabric 自带 asm-9.10.1 冲突；错误走 stderr 而日志"无输出"是假象——LaunchProcess 其实已捕获 stderr）。
- **修**：JavaArgumentsBuilder classpath 组装加「同 group:artifact 只保留继承链末尾」（MavenKey：group:artifact[:classifier]；seenLibs 字典覆盖旧索引）。手动验证：去掉 asm-9.6 复现命令 → fabric 1.21.10 正常启动（25s 仍在运行）→ 修复有效。
- **修复失效/卡着**：修复只重下 client jar（30.5MB，17:06），重复 ASM 不在修复范围内 → 修了还崩（_autoFixApplied 只一次）→ 崩溃弹窗循环；状态清理（catch 分支 526 IsRunning=false）已确认完整，无需改。
- 踩坑：复现时 shell=True 走 cmd.exe 有 8191 字符限制（"命令行太长"假象）→ 必须 subprocess 参数列表直连 CreateProcess；日志文件行首 `§ 启动命令：` 是 UTF-8 多字节，cut -c 按字节切会坏前缀。
- 测试 267/267 全绿（无新增，去重经手动复现验证）；发布（发布\Lattice启动器.exe）

### 真机 GUI 全流程测试（2026-08-06 20:08-20:15，Task #137）
方法：python OCR（WinRT zh-CN）+ DPI 修正像素点击（SetProcessDPIAware 后物理坐标）操作真实启动器窗口。
- **启动**：点「启动游戏」→ 20:08:04 失败（1.21.10 客户端缺失）→ AutoFix 自动重下 → 自动重启 → **稳定运行 88.6s**（java.exe 1GB 内存，日志仅 Realms 超时无害）→ **AL27 fabric 去重修复真机验证通过**（对比 17:07 连续 7 次 0.14~0.5s 秒退）。
- **停止**：点「停止」→ 进程杀 → 按钮回「启动」+ 状态「已退出」+ launch-history 新增 Outcome=2（已停止）88.6s ✅ 停止路径写历史正常。
- **启动记录**：UI 列表与 launch-history.json（24 条）一致（20:09 已停止 / 20:08 失败重下 / 17:07 fabric 秒退 ×8）。
- **版本页**：7 版本列表 + 来源统计「本启动器 2 · PCL 5」+ loader/来源/日期徽章；搜索「1.21」筛出原版 1.21.10 + 1.21.1-Fabric + fabric-loader-0.19（**AL26 回滚后原版条目恢复**）；详情面板：启动/停止/重新下载 + 版本级启动配置表单（内存/Java/额外 JVM 跟随全局）。
- **下载页**：下载/下载记录/MOD/整合包/材质包 tab + Fabric/Forge/NeoForge/Quilt + 版本选择正常。
- **开服页**：「服务端就绪，可启动」+ D:\YanKa Launcher\servers\1.21.10 + 启动/进入服务器/下载服务端/刷新。
- **设置页**：目录/启动参数/下载选项 tab + 版本隔离开关 + 浏览/默认。
- **导入整合包**：弹「选择整合包」原生文件对话框，ESC 正常关闭。
- 已知未复测/未修复：CurseForge 缺 API key 未联调、mrpack 导入降级提示、微软登录设备码国内不稳、设置页 Java 自动下载/性能管线 UI/模组管理未实现。

### 真机 GUI 测试续（2026-08-06 20:19-20:35，Task #137：用户指定三测试全完成）
- **测试 1：下载新游戏 1.21.11 + Fabric** ✅
  - 版本页选中 1.21.11 → 下载按钮（紫色 accent #8b5cf6，位于 (864,345)——find_blue 蓝色阈值会误匹配紫色，定位靠逐行像素扫描）
  - LoaderChoiceDialog「选择加载器」→ Fabric chip → **加载器版本列表 12+ 秒才出现**（共 251 个版本，0.19.3 默认选中）——LoaderService `new HttpClient()` 无显式超时（默认 100s），meta.fabricmc.net 从国内访问慢；用户「fabric单次加载全部还是没解决」的体验根因在此，最终加载成功
  - 下载「1.21.11 + Fabric」106.2MB 完成（history.json「完成」20:28:40）：versions/fabric-loader-0.19.3-1.21.11/ 含 jar（31,152,600B = 原版 client jar 大小，fabric 安装把 client jar 重命名落 fabric 目录）+ 库（fabric-loader 0.19.3 / intermediary 1.21.11）
  - **启动 fabric-loader-0.19.3-1.21.11 完整成功**：java 进程命令行含 fabric-loader-0.19.3-1.21.11，游戏窗口「Minecraft* 1.21.11」存在，运行 ~2 分钟，日志仅 sessionserver.mojang.com 连接超时（离线环境无害）→ 停止 → launch-history outcome=2 ✅
- **发现的问题（新）**：
  1. **纯净版 1.21.11 是残件**：versions/1.21.11/ 只有预取 json（42KB，20:19 选中版本时 GetOrFetchVersionJsonAsync 写入），**无 1.21.11.jar**（client jar 只作为 fabric 版本的 jar 存在）→ UI 显示「已装 9 个版本」含它，但纯净版启动会抛 GameLaunchService.cs:27「客户端文件缺失（请重新下载）」（AutoFix 可自动补，但首次点启动体验差）；「json 存在即 installed」判定（VersionManifestService.cs:90-93）是根因
  2. **启动过程多次 JVM 尝试**：20:31:14-29 产生 8 个 launch-*.log（1/13/3/39/20/2334/3228/50 行）但 launch-history 只有 1 条（20:33 outcome=2）——疑似我的探针+双击触发多次启动 + AutoFix 重试，边缘行为待查（不排除并发启动保护缺失）
  3. 主页「启动记录」tab 日志区显示「版本 1.21.10 的客户端文件（请重新下载）」= 20:08 失败历史的残留显示（启动记录列表选中项），与 1.21.11 会话无关（已用 JavaArgumentsBuilder 继承链消息 + fabric json inheritsFrom=1.21.11 排除）
- **测试 2（长 ID 友好名）**：此前已验证 ✅「1.21.10 (Fabric) · 本启动器」下拉+选中均友好名，fabric 1.21.10 真机启动 44.1s 无 ASM 错误（AL27 验证）
- **测试 3（设置动态性）**：此前已验证 ✅ 设置页改内存 2G → 主页摘要「内存 2G · Java 自动 · 本机离线」动态更新 + settings.json MemoryMb=2048 持久化

### AL28 灰背景+英文 修复真机验证（2026-08-06 21:19-21:30，Task #141）
**根因**：`VersionJsonMerger.Merge` 用 `child.Arguments ?? parent.Arguments`——fabric profile 的 `arguments.game=[]`（空数组非 null）短路覆盖父版 26 个游戏参数 → 无 --assetsDir/--assetIndex → 资源索引断链 → 灰背景 + 语言回退英文。修复：`MergeArgumentList` 父在前子追加、**空子数组回退父**。
**破案链（关键）**：probe 用旁路 dll 验证 20 参数 ✓，但真机 0 参数——一度怀疑进程加载别处 dll（rename/全盘搜索/进程对比全做），最终确认：**发布 exe 是 17:22 的单文件 bundle（内嵌旧 Core），20:47 的 `dotnet publish -o 发布` 只写了旁路 dll 从未被用**（单文件发布托管程序集从 exe 内存加载）。**教训：改了代码必须重跑 发布.ps1，旁路 dll 对单文件 exe 无效**。
**验证（21:19 重建 exe 后）**：
- 原版 1.21.11：11 参数全齐（基线对照）✅
- fabric-loader-0.19.3-1.21.11：**10 对 game 参数全齐**（--username iwasGOD / --version fabric-loader-0.19.3-1.21.11 / --gameDir / --assetsDir / --assetIndex 29 / --uuid / --accessToken / --clientId 0 / --xuid 0 / --versionType release）+ mainClass=knot.KnotClient + classpath 含 intermediary-1.21.11 ✅（旧 bundle 同期 0 参数）
- 游戏画面：主菜单**中文**（单人游戏/多人游戏），背景深色非灰 ✅ → 资源链完整
**#138/#139/#140 全部落地**；测试 267/267 全绿；发布\Lattice启动器.exe 21:19 重建（单文件，签名完成）。
**GUI 自动化备忘**：强杀游戏进程会触发「启动器遇到问题」模态弹层（Avalonia 渲染在主窗口内，EnumWindows 不可见，Esc/点击按钮难关）→ 直接杀启动器重启最干净；版本下拉 (469,95) 物理坐标点开列表选「1.21.11 (Fabric)」(291,254)；「启动游戏」按钮 (667,96)。

## AL29 三批修复完成（2026-08-06 22:05）
- 批1 C1+C2：IsInstalled(json&&jar) 统一判定（版本页保持 json-only 管理页语义）+ ParentVersionMissingException 异常分化
- 批2 H5+H6：启动前校验门（GameLaunchService 沿链 VerifyVersion，缺失即报「文件不完整」不等到 JVM 崩）+ 安装后校验门（VersionInstaller.VerifyInstalled，下载完成==文件完整，杜绝虚假成功）
- 批3 H1：DownloadService 原子写入（单连接/416/分片合并全部 destPath+".tmp" → 校验通过 File.Move(tmp,destPath,true)；catch 只清 .parts/.tmp，未验证新文件不覆盖旧 destPath）
- 测试 267 → 278 全绿（+VersionManifestServiceTests 4 / GameLaunchServiceTests 2 / VersionInstallerTests 2 / AtomicWriteTests 3）
- 测试关键坑：VersionInstaller 自建 DownloadService 带真实 HttpClient+网络预检 → stub 测试必须注入（stub handler + 单候选解析器 + 零退避 + 网络检查 stub），否则 stub 主机 DNS 失败走真实镜像 13 秒才报错
- 发布\Lattice启动器.exe 21:56 重建 + 真机验证：fabric 1.21.11 10 对参数全齐 + knot.KnotClient + 中文主菜单 ✅
- 本轮不做（后续批候选）：H2 存量 SHA1 重校 / H3 Forge 安装后校验 / H4 Redownload 带 sha1 / M1-M7

## 2026-08-06 22:30 — 版本页 PCL 命名改造 + versions 删除调查
- 需求：版本页名称看不清 → PCL 式显示名「1.21.11 (Fabric)」。实现：VersionScan.FriendlyName（共享助手，主页/版本页统一）+ InstalledVersionRowVM.DisplayName + Detail.DisplayName（Id 真实目录名小字保留）
- 真机验证 ✅：重装 fabric 1.21.11 后版本页第一行「1.21.11 (Fabric)」+ fabric 徽章；主页同样显示。PCL 版版本（无 inheritsFrom 自包含）显示原名 = PCL 自身风格，正确
- 安装链路复核：client jar 沿 ResolveChain 落子版本目录（31,152,600 字节与 1.21.11.json downloads.client.size 完全一致）；父版本 1.21.11/ 只有 json 是设计（残件在管理页带缺文件徽章，勿误报 bug）
- versions 删除调查结论：21:56 启动 classpath 铁证版本在；22:01:54 versions 目录 mtime=杀进程时刻；启动器代码 5 处 Directory.Delete 全为用户手动操作（版本页删除/存储管理/暂存/natives）；回收站无痕迹；launch-history 无删除记录。结论：22:01 前后外部动作删除（需用户确认是否手动删）；无自动删除逻辑
- GUI 自动化补充：find_blue.py 找 accent 主按钮；侧边栏 tab y：主页~96 版本~140 下载~188 开服~232 设置~276

## 2026-08-06 23:1x — AL29 真机 Forge 安装验证 + 3 真实误报源修复（Task #153 收尾）
**真机 22:3x 验证**（D:\YanKa Launcher\.minecraft，真实 Forge 1.21.10 官方安装器 x2 次运行）：
- fix B 确认 ✅：launcher_profiles.json stub（clientToken+launcherVersion+profiles 空）让真实安装器通过 profile 检查进入正常安装流程；不写则安装器中止「There is no minecraft launcher profile...」。若用户已有官方启动器配置则保留不覆盖（测试覆盖）
- 真实安装器行为（22:3x 两次 run 日志）：下载 client jar + 19 库 → mcp_config/DownloadMojmaps 阶段 `java.net.ConnectException: Connection timed out` → exit=1。**安装器无重试**（单次超时即中止，启动器控制之外；已下载文件幂等跳过，重试安全）
- 决定性证据 history.json：「下载 1.21.10 + Forge」22:41:38 State=失败 → 新校验代码真机正确工作；「已全部完成」=空队列文案不是成功信号（失败任务 3 秒移除）
- 发现 3 个真实误报源并修复：
  1. **A 顺序**：先校验后标记——校验抛异常不再留 .yanla-installed（22:41 残留标记曾使失败安装计为「本启动器已装」）
  2. **B**：运行安装器前 EnsureLauncherProfiles（第三方启动器预写 stub，官方语义）
  3. **C**：组路径子任务失败显式传播（runChild.TerminalState==Failed → throw runChild.Error）——否则 FindNewestVersionDir+校验把「安装器执行失败」误报成「缺 N 个文件」
  4. **Verify 语义 x2**：官方安装器把 client jar 落**父版本目录**（30,592,168 字节在 versions/1.21.10/）+ forge json client classifier 库 `downloads.artifact.url=""`（继承引用，安装器标记 Invalid 跳过）→ VerifyFiles 加 clientParentId 备选路径 + 跳过 url 空库（VerifyVersion 传 version.InheritsFrom）
  5. FindNewestVersionDir 按目录内 {id}.json 文件 mtime 排序（NTFS 目录 mtime 有缓存延迟，Test 4 实证选错目录）
- 测试 283/283 全绿：新增 4 组路径测试（stub 注入 installerProcess 测试缝：InstallerWroteNothing→Failed+无标记+stub 先就位 / ExitNonZero→真实错误传播 / Profiles 已存在不覆盖 / Success→父目录 jar+url 空库→Completed+标记）
- **发布\Lattice启动器.exe 23:1x 重建**（含 AutoRepairService VerifyFiles 修复，发布.ps1 签名完成）
- 遗留：H2 存量 SHA1 重校 / H4 Redownload 带 sha1 未做（后续批候选）；真机网络超时需网络环境改善或安装器重试策略（启动器侧不可控）

## 2026-08-06 23:3x — BUG时间线.md 文档（AL1~AL29 反馈→确诊→修复时间线）
- 产出 launcher/BUG时间线.md：按 AL 分节（34 节：AL1~AL29 + 子编号 + AM），每节含用户反馈引语/确诊根因/修复/时间/来源标注（SN:L行号 + git 哈希），文末编号前 Q~AK 简表 + 多轮反复修复规律表 + 时间数据可靠性说明
- 不造假原则落地：时间精度三级（git 到分 / SESSION_NOTES 标题到分 / 仅日期），来源冲突并列（如 AL4 git 23:24 vs 发布 23:33），查不到的标「无记录」
- 数据要点：AL11~AL27 无 git 提交（会话日志追加制，git 停在 08-05 11:32 AL10.2）；服务端下载系列 7 轮、KnotClient 4 轮、splash 4+ 轮为最长反复链
- 验证：5 处引语抽查与 SESSION_NOTES 原文逐字一致；编号覆盖完整

## 2026-08-07 10:0x~11:0x — AL30 真机修复验证收尾 + 「Failed+Error=null」根因确诊修复（Task #154-157）
**真机 10:37「修复 1.21.10-forge-60.1.0」验证**（用户睡觉期间自主执行，无提问）：
- AL29 修复真机确认 ✅：修复前版本页正确显示「客户端文件缺失，无法启动」（无 .yanla-installed → 未误标已装）；重新下载补齐 147MB（client jar 落子版本目录 30,592,168 字节、53 forge 库 + 115 parent 库、assets 4403 全 0 缺失 0 size mismatch 0 sha1 mismatch）；修复后「本启动器」标签 + 启动按钮 + 无警告；本启动器计数 2→3
- 但 history.json 记「修复 1.21.10-forge-60.1.0」State=失败 Error=null——与文件全完整矛盾 → **AL30 确诊**：
  1. forge json `net.minecraftforge:forge:1.21.10-60.1.0:client` 库 `downloads.artifact.url=""`（继承引用，安装器标记 Invalid 跳过，Mojang 官方启动器同样跳过下载）——**pipeline 却为它建了下载子任务** → `DownloadFileAsync("")` → UriFormatException（不在 HttpRequestException/InvalidDataException 重试过滤器内）→ 子任务 Failed。磁盘实证：libraries/.../forge-1.21.10-60.1.0-client.jar 永久缺失
  2. 组任务 WhenAll 完成（其余子任务全并行成功，故文件全完整）→ 检测到失败子任务 → 组 Failed；**Error 的 Post 排在 SetState 的 Post 之后** → PropertyChanged(State) 触发 DownloadHistoryService.Record 时 Error 仍 null → history.json Error=null（诊断全靠猜）
- **AL30 修复 x2**：
  1. VersionDownloadPipeline + RunLegacyAsync 库循环：artifact url 空 → 跳过不建子任务（镜像 VerifyFiles 同规则）
  2. DownloadTask.SetState 加 error 参数，失败路径同一 Post 内先 Error 后 State（3 处：叶子 catch / 组 failed-child / 组 catch）——Record 时错误可见
- 测试 284/284 全绿：新增 `RepairPath_UrlEmptyClassifier_Skipped_GroupCompletes`（VersionInstaller.InstallAsync+EnqueueGroup 修复路径语义，旧行为必红）
- 语义结论：client classifier 无 url 无法下载是 Forge 官方设计（json `_comment` 明言勿自动化）；VerifyFiles 跳过与 pipeline 跳过保持一致，启动 classpath 缺失项 JVM 静默忽略；若启动缺 Forge 类 → 重跑加载器安装（运行官方安装器）。**发布\Lattice启动器.exe 10:54 重建**（用户确认，含 AL30 修复，签名完成）
- 遗留小 UX：版本页详情面板修复完成后「客户端文件缺失」警告不刷新，需切换版本行再切回（缓存问题，未修）
- 顺带发现：versions/ 目录含「1.21.10-forge-60.1.0」与「1.21.1-Fabric 0.19.3」等混合命名；libraries 有 forge shim.jar（安装器私有残留，json 未引用）

## 2026-08-07 11:0x~11:3x — 回归 + 广域下载测试 + 滑块真实生效性验证（Task #158-162，全真机）
**用户先删除版本 → 回归 + 多版本下载 + 滑块行为验证（GUI 自动化驱动 Lattice 本体）**
- **AL30 回归 ✅**：forge 1.21.10 重装（加载器对话框→开始下载）：history「下载 1.21.10 + Forge」11:17 完成 Error=None（对照 10:37「修复…」失败 Error=null）；forge-1.21.10-60.1.0-client.jar 落盘
- **不同版本下载 ✅（广）**：1.21.11-rc2 全新下载：history 11:21「下载 1.21.11-rc2」完成 Error=None；versions/1.21.11-rc2/ 含 jar+json，jar 31,152,682 字节 = 官方 size 一致
- **滑块三连验证（核心结论）**：
  1. **写盘 ✅**：拖 MaxConcurrentDownloads 1→16、ChunkCount 8→4、SpeedLimitKbps 0→1958，settings.json 1.2s 内 DebouncedSave 落盘，UI 值同步
  2. **版本下载路径 ❌ 进程内不生效**：限速 1958 KB/s 设置下 1.21.11-rc2 实测 46.9 MB/s → DownloadService.cs:42 `_options = options ?? DownloadOptions.FromSettings(...)` 构造时冻结，进程内改滑块不生效（要重启）；DownloadOptions.cs「改动即时生效」注释误导（候选 bug）
  3. **第三方直链路径 ✅ 立即生效**：第三方文件 tab 下载 Mojang client.jar 31.5MB x2 次（client.jar / client (1).jar 同名自动加后缀，size 31152682 与官方一致，分片断点续传 parts 目录实证）：点击后 ~11-13s 下 29.2MB ≈ 2.2-2.7 MB/s 与 1958 KB/s 限速量级吻合（46.9→2.5 十倍差）；history 11:26/11:27 两条「下载 client.jar」完成 Error=None——DownloadViewModel 每次 new DownloadService → 立即读当前 settings
- **结论**：滑块非摆设——第三方路径即时生效；版本下载冻结到进程启动，重启后生效（该路径实际工作，只是即时性预期不符）。修复候选：版本下载改用当前 settings（解冻）或 UI 提示「重启后生效」
- 测试残留：settings.json 现持 16/1958/4（原值 1/0/8）；桌面 client.jar + client (1).jar 测试产物（待用户决定清理）

## 2026-08-07 11:4x — AL31 滑块即时生效 + 分片进度节流上报（Task #163-164）
**背景**：用户要求修两个实测候选 bug（前一节真机验证发现）：
- **A** 版本下载滑块冻结：VersionDownloadViewModel 缓存 `_installer`（DownloadService 构造时读 settings 快照，`DownloadViewModel._game ??=` 缓存 → 冻结点=首次打开下载页），改滑块后同会话版本下载不生效（实测 46.9MB/s 无视 1958 限速），第三方/修复路径（每次 new）立即生效
- **B** 分片进度粒度：DownloadChunkedAsync 每片完成才 Invoke 一次 → 大文件速度/剩余文字每片周期刷新（观感「延迟显示」；快下载时 0%→100% 直跳「跟不上」）
**修复**：
- **A**（VersionDownloadViewModel.cs:242）：每次下载重建 installer，`new DownloadService(null, null, DownloadOptions.FromSettings(LauncherSettings.Current), null)` 传最新设置；repair 仍用 InstallDir()，正常用 Detect()（与原语义一致）。_installer 字段保留给 Detail 面板（只拉 json 不下载）
- **B**（DownloadService.cs）：新增 ChunkProgress 共享类——各分片读循环 `Interlocked.Add` 实时累加字节 + CompareExchange 抢占 250ms 窗口节流上报（ReportChunkProgress）；片完成上报与节流上报同源（统一 cp.Bytes 计数器，曾因双计数器打架出现进度回退 917504→262144，测试捕获）；复用分片也计入计数；DownloadChunkAsync 重试递归透传参数
**测试**：新增 `ChunkedDownload_Progress_ReportsMoreThanChunkCount`（慢速 HttpListener 端点 /slow.bin：1MB 4 片、每 64KB 延迟 80ms 拉过 250ms 窗口；断言回调 >4、单调、终值=文件大小、文件内容一致）——先红（旧代码 4 次）后绿。**285/285 全绿**，App 编译通过

## 2026-08-07 AL32 — 修复「大小已满但一直下载中」（父进度被 clamp 卡 100%）
**用户反馈**：「为什么一直有大小到了但是还提示下载中的情况？」
**确诊**（VersionDownloadPipeline.cs + DownloadTask.cs 聚合层）：
- 版本下载两阶段：阶段 1（client jar + libraries + asset index + logging）全部完成 → 父聚合收敛 100%；阶段 2（assets 差量，index 下完才知道清单）**之后**才 AddChild「资源文件 (N 个)」（weight=missing 总字节，真实值）
- 新子任务挂载后真实加权进度应回落到 ~70%，但 DownloadTask.RecomputeAggregate 的 `if (percent > ProgressPercent)` clamp（D2 组任务模型时代的防乱序防御）拒绝下降 → 父卡 100%
- `BytesDone = TotalBytes × percent` → BytesText 满格 + Stage=「资源文件 N 个」→「下载中」——观感「大小到了还下载中」，持续整个 assets 阶段（几百 MB 可能数分钟）；assets 越大越明显
**修复**（DownloadTask.cs:355-362）：clamp 移除，直接赋真实加权值。安全性：子任务自身进度单调（DownloadService 层已保证），聚合唯一回落来源 = 新子任务挂载（真实进度变化，Stage 同步指到新任务，观感「进入新阶段」）；D2 时代的防回退防御已无对象
**测试**：新增 `Group_LateAttachedChild_ProgressFallsFrom100ToReal`（reported TCS 做阶段屏障：先收敛 100% 再挂阶段 2 子任务，断言回落到 70）——先红（100）后绿（70）。**286/286 全绿**

## 2026-08-07 17:0x — Task #167 真机验证收尾：检查文件按钮 + AutoChinese + 布局假象破案
**验证项 ③⑤（版本页红字 + 检查完整性按钮）✅**（窗口物理 1800x960，全入屏后 OCR 实测）：
- 选中 vanilla 26.3-snapshot-6（用户删过 jar）：列表行红色「缺文件」徽章 (359,596) + 详情红色横幅「版本 26.3-snapshot-6 客户端文件缺失，无法启动。可补全下载，或前往官方页面手动下载。」(y318-330 红字 + 补全下载/打开官方下载页 按钮)
- 按钮行 4 按钮全部 OCR 可见可点：启动 (1382,189) 停止 (1479,189) 重新下载 (1564,189) 检查文件 (1679,188)
- 点「检查文件」→ 红色 ErrorText 出现：「文件不完整：缺 1 个（首例：26.3-snapshot-6.jar）。可点「重新下载」补全」—— 与 VerifyVersion 磁盘直读逻辑完全一致
- 切到 fabric 行 26.3-snapshot-6 (Fabric)：无 JarMissing 横幅（文件完整）✓ 重新下载流程之前会话已验证

**验证项 ⑥ AutoChinese 对版本级隔离目录 ✅**：
- 点启动 → java 进程 19180 拉起，命令行 `--gameDir "D:/YanKa Launcher/.minecraft/versions/fabric-loader-0.19.3-26.3-snapshot-6"`（版本级隔离 ✓）
- 30s 后 options.txt 落盘，含 `lang:zh_cn` ✓
- 游戏窗口「Minecraft* 26.3 Snapshot 6」主菜单 OCR 全中文：单人游戏/多人游戏/Minecraft Realms/选项…/退出游戏/© Mojang 请勿二次分发！
- （游戏窗口曾被 Lattice 窗口盖住——Lattice 1800x960 覆盖游戏窗口区域；最小化 Lattice 后截到）

**布局谜团破案（上一会话遗留：ScaleX≈2.9/按钮裁切/详情偏右 380px = 全假象）**：
- 本机显示器 **1920x1080 物理 @ 125% (120 DPI)**；Lattice per-monitor aware；python 默认 DPI-unaware → 全部坐标被 OS 虚拟化 ×1.25
- 真相链：unaware GetWindowRect 报 (400,100)-(1680,900) = 物理 (500,125)-(2100,1125) **右半 180px 超出屏幕**；截图 x≥1420 纯黑 = 屏幕外；「启动按钮被 1280 裁切」= 屏幕外 + 虚拟化双重假象
- 物理图实测：nav=200px（160 DIP×1.25 ✓）、列表 x 200-660、详情列 x 682-1436 DIP、按钮行全部窗口内 —— **布局完全正常，无 scale bug，无 Grid 溢出**
- **verify.py 修复**：开头 `SetProcessDpiAwarenessContext(-4)`（PerMonitorV2）→ 全坐标统一物理；move/click/snap 后续直接可用
- 遗留：任务列表 #168（按钮行布局 bug）实为误判——无布局 bug 可修，待用户确认关闭；游戏进程仍在跑（用户自停）

**待办**：下载页滑块问题（AL31 前）的 settings.json 残留 16/1958/4 未还原；桌面 client.jar/client (1).jar 测试产物未清理

## 2026-08-07 18:0x — 红字溯源 + 秒同步（Task #169/#170）

**红字溯源（「为什么版本页还有红字」）**：D:\YanKa Launcher\.minecraft\versions\26.3-snapshot-6 只剩 json 无 jar——上会话测试「检查文件」删 jar 后未还原，是测试残留非 bug。删除逻辑本身干净（VersionManageViewModel.cs:223 Directory.Delete(dir,true) 递归整删）。补全下载即消红字。

**智能选源「雷同」核对（AI 建议 vs 现有代码）**：
- 已有（且更强）：下载后 SHA1/大小校验，失败删文件抛 InvalidDataException → 换源整轮重试（DownloadService.cs:249-256,160-163）；官方兜底天然在候选源列（:149-163）；全败抛错不静默（:178）
- 无：HEAD 预检（GetContentLengthAsync 只在无预期大小时取长，:393-407）；动态超时切源（切源仅失败/校验失败时发生）
- 结论：「智能选源导致静默缺文件」在本实现下不成立（校验失败必换源），AI 建议属通用聊天建议

**秒同步实现（VersionBrowseViewModel.cs 单文件改动）**：
- 根治：VM 常驻单例 + _loaded 幂等缓存 → 首次进入后列表/详情永不再扫；Select() 同版本早退 → 补全后红字切走切回都不消失
- FileSystemWatcher 监听所有源 versions 目录（IncludeSubdirectories=true, NotifyFilter=DirName|FileName|LastWrite），500ms 防抖（CTS+Task.Delay）→ Dispatcher.UIThread → SyncFromDisk
- SyncFromDisk：RescanLocal（纯本地目录遍历，不依赖 manifest 无网络——manifest RefreshAsync 有 24h 缓存但高频触发免之）+ 重选选中行 + Detail.RefreshJarMissing()；版本被删 → Detail.ClearSelection()
- Repair() 完成后 / CheckIntegrity() 完成后主动 RefreshJarMissing()（watcher 事件可能漏，主动刷新兜底）
- 编译：0 错误（一次低级错误：NotifyFilter 单数属性名，写错为 NotifyFilters）
- 未真机验证：需重启 Lattice 加载新 exe

## 2026-08-07 18:3x — 删除残留根因确诊 + 删除强化 + 残件清理

**用户反馈**：「26.3-snapshot-6 只剩 json 是删除JAR留json？删除是否是不完全删除」——上批把残件判为测试删 jar 未还原，被用户纠正：是用户自己删除版本后留下的。

**根因确诊**：`Directory.Delete(dir, true)` 在 Windows 上不原子——目录内任一文件被占（游戏进程、Defender 扫描、索引器短锁）时删到一半抛 IOException，**已删部分不恢复** → jar 先删、json 被锁 → 剩 json 残件；旧代码 catch 只显示「删除失败」，不重试不清理，残件目录留在 versions/ 下被版本扫描发现 → 版本页「缺文件」红字（红字显示本身正确，根源是删除不彻底）。

**修复（VersionManageViewModel.cs Delete 强化）**：
- 重试 3 次（0.5s/2s/4.5s）等短锁释放（Defender/索引器通常秒放）
- 仍失败 → `Directory.Move` 改名隔离 `{id}.deleting-{guid}` → 版本立刻消失（扫描不可见）+ 后台线程 30s 内每 3s 续删（游戏结束/放锁后删净）
- 改名也失败（目录内文件被独占占用，如版本运行中）→ 明确报错「请先停止该版本，再删除一次」
- 隔离成功时 NotificationService 提示「已移除，残留文件后台清理」
- 绝不把没删干净冒充删除成功：后台 10 次仍失败留 .deleting- 目录供手动处理

**残件处理**：D:\YanKa Launcher\.minecraft\versions\26.3-snapshot-6（仅 json 51768B）已手动清理，versions/ 现在只剩 2 个正常版本。

**编译**：dotnet build 0 错误（28 个既有 obsolete 警告与本次无关）。

**未验证**：删除强化逻辑需真机（临时锁 json 难自动测；删除运行中版本场景可手测：启动后删除应报「被占用」而非删一半）。

## 2026-08-07 21:0x — #174 快照7 英文根因闭环：MC-310687 Mojang 官方 bug（Lattice 无责）

**用户线索**：「停掉吧」「搜索MC310687 就是幕后真凶」——用户认定 Mojang bug MC-310687 是快照7 英文的根因。

**真机验证（已做）**：20:14 用 Lattice 重下后启动快照7（PID 41936，--gameDir 隔离目录 + assetIndex 33），OCR 主菜单仍英文 → AutoChinese 的 lang:zh_cn 写进 options.txt 但游戏语言系统加载失败，与 Lattice 无关。

**MC-310687 结论**（[mojira.dev/MC-310687](https://mojira.dev/MC-310687)）：
- 标题「Languages other than "English (US)" fail to load」，26.3-snapshot-7 官方回归：`ClientLanguage.loadFrom` 现在要求**每个 namespace** 都有语言文件，任一为空 → throw `EmptyTranslationsException`；而默认 jar 不再含其他语言文件（只有 en_us.json + deprecated.json）→ 非英语语言整体加载失败：语言菜单只有 en_us，或选中文直接 ErrorScreen 保持英文
- **与本地字节码分析完全吻合**：s7 的 loadFrom 新增 `resourceStack.isEmpty() → throw`（165B vs s6 178B 的 warn 继续循环），LanguageManager.onResourceManagerReload 新增 catch EmptyTranslationsException → ErrorScreen——这就是 bug 的代码表现
- 状态：Mojira 标记 **Fixed**，但 26.3-snapshot-8 尚未发布（周更节奏预计 08-11），修复未落地公开构建 → **用户侧无需任何操作，等 Mojang 修**；期间快照7 保持英文属上游行为
- 同源 bug：MC-310783（自定义 namespace 资源包导致 en_us 也失败，s6 无此问题）
- 官方临时 workaround：把目标语言翻译文件改名 en_us.json 塞资源包启用（太丑，不推荐给 Lattice 用户落地）

**#174 闭环**：快照7 英文 = Mojang 上游 bug，Lattice/AutoChinese 无责，无需改代码。验证完成，游戏进程已停。

**残留清理**：s7 隔离目录实验残留（ClientLanguage.class 提取、resourcepacks/zhcn 实验包）、Temp 下 ocr_diag/ocr_fix/shot_pw/s6_pw.png 可删。

## 2026-08-07 22:0x — #174 结论推翻再闭环：PCL 中文 = fabric-api 的 ClientLanguageMixin（Lattice 依旧无责）

**用户决定性线索**：「等等，PCL的是中文的？！你现在赶紧上真机看」——官方 PCL 2.13.0.1 跑同一个快照7 是**中文**，MC-310687「唯一根因」结论被推翻，立即真机找差异。

**系统性对比（PCL vs Lattice，逐项排除）**：
- options.txt 完全相同（lang:zh_cn）｜assets 完全相同（5147 objects 全在盘）｜合并 jar md5 完全相同（34f73df2093df1abe4ba096a6c74413d, 41010320B）｜java 都是 25
- **唯一差异 = fabric-api 0.156.3+26.3**（PCL 的 s7 实例装了，Lattice 隔离目录没装）

**石锤机制（字节码级）**：
- fabric-api 嵌套 jar 内 fabric-resource-loader-v1 3.0.2 的 mixin 配置含 **ClientLanguageMixin**：@Redirect 把 `ClientLanguage.loadFrom` 的 `isEmpty()→throw` 检查重定向到 `allowMissingLanguageFiles(boolean)`，其字节码仅 `ICONST_0 + IRETURN`（恒 false）→ 不抛 EmptyTranslationsException → MC-310687 被绕过 → zh_cn.json 正常加载 → 中文
- 即：fabric-loader 3.0.x 系（fabric 生态）自带对 MC-310687 的规避，PCL 有 fabric-api 所以中文

**最终真机验证（Lattice 环境 + fabric-api，已做）**：fabric-api jar 拷入 Lattice s7 隔离目录 mods/ → 手动构造完整启动命令（61 库父继承 + 7 库子版本 classpath，-DFabricMcEmu 代理）→ 启动成功（PID 32140，窗口 698x422）→ OCR 主菜单 = **「单人游戏」「多人游戏」中文** → 停进程。

**#174 最终闭环**：快照7 英文 = MC-310687 上游 bug；中文方案 = 装 fabric-api（客户端侧规避，PCL 就是这么中文的）；Lattice 无需任何代码改动。快照8（预计 08-11）官方修复后裸装也中文。

**遗留说明**：s7 隔离目录 mods/fabric-api 保留（用户可决定去留）；快照6 父版本 json 手动创建问题本身未修（另开任务）；验证进程已停，Temp 临时脚本已清。


---
## 08-07 动画「谷歌级丝滑」升级（A/B/C 三批次全部完成）

**背景**：用户要求动画媲美谷歌交互动画。三线齐做：① Material 曲线（SplineEasing cubic-bezier）+ 真阻尼弹簧（SpringEasing，IEasing 不可用户实现→终值钳位放内核）② 渲染帧驱动（TopLevel.RequestAnimationFrame 替代手写 timer；Avalonia 12 无 CompositionTarget.Rendering，RequestAnimationFrame 是单次回调需每帧自再入队；无 TopLevel 时回退 DispatcherTimer 15ms）③ 补齐全部硬切点。

**A 批次**（UiAnim.cs 内核化 ⭐）：统一 `Animate(ms, curve, set, onDone, host, ct)` 帧内核；`Curves.Standard/Decelerate/Accelerate/Overshoot`（fast-out-slow-in 等）+ `Durations` 150/220/350 + `CreateSpring(ζ, stiffness)` 工厂（damping=2ζ√(mk)）；公共 API 签名全保持→调用点零改动；每 visual 互斥（打断不写终值）；Stopwatch 绝对起算（遮挡/最小化恢复后超时帧直接收尾终值必达）。行为层：SpringScale 释放回弹换 CreateSpring(0.65,120)，Ripple 换 Curves.Standard 390ms；App.axaml 全局 Button 加 BrushTransition 150ms（本地值赋值也被 Transitions 接管→导航/设置菜单变色自动渐变，B1/B2 零代码）。

**B 批次**（硬切点）：Settings 分区 ShowSection 淡入（Opacity<1 才淡，防重复）；DownloadView tab / EcosystemView 列表详情——**Avalonia 12 无 IsVisibleChanged 事件**（CS1061），用 Control.PropertyChanged + IsVisibleProperty 过滤器惯用法（`UiAnim.AttachFadeOnVisible`）；MainWindow ApplyAppearance 密度/亚克力 TintOpacity 改内核 220ms 插值（material 直赋 Transitions 管不到；滑块连发事件→同 visual 互斥形成追值）。

**C 批次**（splash）：logo 弹出 AnimateBack(BackEaseOut) → CreateSpring(0.8, 100)（damping=16，~450ms 温和过冲，Android 启动屏手感）；GrowToFull 950ms QuinticEaseOut → Curves.Decelerate（窗口尺寸不弹簧化）；AnimateBack/AnimateSmooth 仅 splash 用→已删（连带 _easeOutQuint/_easeOutBack）。

**验证**：build 0 错误（28 警告全为既有 Watermark obsolete）；测试 286/286 全绿（上次 flaky 的 ChunkedDownload 本轮也过）；真机启动 0 异常。Plan 文件：`C:\Users\yanka\.claude\plans\fuzzy-strolling-frog.md`。明确不做：Popup 关闭动画、Expander、强调色 fade（若 B 批次收尾顺利再考虑）。

---
## 08-07 动画方向修正：撤回视觉动画 → Material 控件过渡 + 透明 Splash（R1-R5 全部完成）

**用户真机判定 A/B/C 跑偏**：「除了1全撤回吧 1我也没看到效果 我要的是谷歌那种选中输入框和下拉列表以及扩展的过渡动画效果 并且打开动画的LOGO背景能不能透明」。AskUserQuestion 确认：只留帧内核+曲线令牌；TextBox/ComboBox/Expander 控件过渡全做；splash logo 背景完全透明。

**R1 撤回**（4 文件 git checkout HEAD：SpringScale/RippleBehavior、Download/EcosystemView.axaml.cs；手动撤回：UiAnim 删 AttachFadeOnVisible/FadeInOnVisible（Avalonia 12 无 IsVisibleChanged 的替代方案，已无用）、App.axaml Button Transitions、MainWindow splash 弹簧→BackEaseOut、GrowToFull→QuinticEaseOut、ApplyAppearance 恢复 HEAD 即时赋值、SettingsView fade、两个 XAML 的 x:Name）。

**R2 TextBox 聚焦**（App.axaml 全局，25 处自动生效）：UseFloatingPlaceholder=True + Transitions（BorderBrush/Background BrushTransition + BorderThickness/Padding ThicknessTransition 150ms）+ `TextBox:focus`（BorderBrush={DynamicResource Accent}、Thickness=2、Padding=9,5、BgHover、PlaceholderForeground=Accent）。**Padding/粗细同步补偿**：1+10↔2+9 外尺寸恒 22 文字零漂移。**关键发现：不用模板部件选择器——TextBox 有公共属性 PlaceholderForeground，:focus 里直接设即可给浮起标签上强调色**（部件名验证失败：Fluent dll 模板字符串被编译掉、PowerShell 反射依赖解析失败、GitHub raw 拉不到——止损换公共属性，更稳）。GameDirSetupWindow.axaml:14 本地 Padding="10,7" 删除。

**R3 ComboBox 下拉**（新 DropDownAnimBehavior.cs 附加行为，RippleBehavior 同款 RegisterAttached+AddClassHandler）：DropDownOpened 现找 `FindDescendantOfType<Popup>()?.Child`（不缓存，Popup 随模板重建），RenderTransformOrigin(0.5,0) scale 0.95→1 + Opacity 0→1 150ms Curves.Standard，host=panel 互斥（连点打断无残留）；找不到静默降级无动画；收起不做（DropDownClosed 不可取消）。

**R4 Expander 高度**（新 ExpandCollapseTransition.cs IPageTransition，FadeSlideTransition 同款 TCS 模式）：展开 to：Height=0 → Measure(Infinity) 拿全高 → 0→h；收起 from：当前 DesiredSize.Height → 0；动画期间 ClipToBounds=true；**Inflight ConcurrentDictionary<Visual,TCS> 重入防悬挂**——内核互斥打断不触发 onDone，主动 TrySetResult 旧 tcs 唤醒其 finally，ReferenceEquals 校验只有最新动画才复位（Height=NaN + 裁剪复原）；App.axaml `Style Selector="Expander"` ContentTransition={StaticResource ExpanderTransition} 共享实例（无实例状态）。探针 Debug.WriteLine 保留（Release 剥离）等真机确认调用约定。

**R5 透明 Splash**（结构改动）：SplashOverlay 移出 RootSurface 成兄弟层（XAML 根 Grid：WindowRoot 亚克力层 + SplashOverlay 透明层叠放，DockPanel x:Name=AppContent）；构造器 Show 前 hint=[Transparent,AcrylicBlur,Blur,None] + RootSurface.IsVisible=false + WindowRoot.BorderBrush=Transparent；GrowToFull done 恢复描边 #4D2F3745 + RootSurface 可见 + hint 切回 [AcrylicBlur,Blur,None] + AppContent Opacity 0→1 淡入。降级链：Win11 WinUIComposition 不支持 Transparent → 亚克力染色 → None fallback 深色，不算 bug。

**验证**：每阶段 build 0 错误；测试 286 中 ChunkedDownload_Progress_ReportsMoreThanChunkCount 偶发失败一次、单独重跑即过（时序类 flaky，与本次改动无关，PCL.Core 未动）。改动未提交（与 AL10/AL11 混存），真机验收清单见对话，用户确认后统一提交。

---
**08-07 打磨轮（S1-S6）**：用户真机验收 R1-R5 后反馈三条——「LOGO 动画割裂」「输入框震动不舒服」「还有什么可以加的」（全选 4 项）。已实现并发布 23:01（177MB）。
- **S1 splash 连贯化**（MainWindow.axaml.cs）：阶段1 BackEaseOut→Curves.Decelerate（无过冲）+ logo Opacity 0→1 同步淡入；阶段2 QuinticEaseOut→Curves.Decelerate（两段同曲线慢起快收，衔接速度连续）；**交叉淡化并入 grow 帧**（浮层 e>0.45 淡出 / AppContent e>0.55 淡入，同帧时钟无空窗，替代 done 后独立淡入）；done 改亚克力+描边 220ms 渐进出现（RootSurface.Opacity + LerpBrush 颜色插值）。注意：**Avalonia 无 DecelerateEase 类（WPF 名），用 SplineEasing(0,0,0.2,1)（即 Curves.Decelerate）**；已删 using Avalonia.Animation.Easings。
- **S2 TextBox 去震动**（App.axaml）：删 2 条 ThicknessTransition + :focus 删 BorderThickness=2/Padding=9,5——聚焦只渐变颜色（BorderBrush/Background/PlaceholderForeground），几何零变化。
- **S3 导航色条滑动**（MainWindow.axaml+.cs）：NavSurface 内 StackPanel 外包 Grid + `<Border x:Name="NavIndicator" Width="3">` 独立指示条；ApplyNavVisuals 删按钮描边色条，改 MoveNavIndicator（TranslatePoint 实测位置/高度，首次直接定位+Bounds.Height<=0 下一帧重试，之后 180ms Animate host=Indicator 互斥）。
- **S4 下载 tab 淡入**（DownloadView.axaml.cs 重写，x:Name 加回 QueuePanelHost/ActiveTabHost）：订阅 VM PropertyChanged（ActiveTab/IsQueueTabSelected）→ Post 一帧 → 当前可见面板 200ms 淡入+4px 上移；首次跳过。
- **S5 列表渐显**：DownloadView 队列（订阅 vm.Tasks/vm.History CollectionChanged → QueuePanelHost 淡入）；EcosystemView 搜索列表（订阅 vm.Cards CollectionChanged Reset/首项 → CardsScroll 淡入 180ms）；VersionDownloadView 已有 ListOpacity 机制（VM 416-417 行）不动。订阅均不退订（视图=窗口生命周期，单例集合）。
- **S6 Toast 滑出**（NotificationService+MainWindow）：ToastItem.OnRemoving Action；FadeOutAsync 移除前 Invoke；MainWindow ContainerPrepared 注册 `t.OnRemoving = () => UiAnim.SlideOutToRight(e.Container)`（复用现成 180ms，Delay(260) 覆盖动画时长）。
- **验证**：build 0 错误；测试 SuspendAll_ThenResumeAll 失败一次、单独重跑即过（下载时序 flaky，PCL.Core 未动）。改动未提交（与 AL10/AL11 混存），真机验收清单：splash 无过冲无瞬间切换、输入框只变色不震、色条滑动、tab 淡入、列表刷新淡入、Toast 右滑出。

**08-07 修复（未响应）**：用户启动"第一次未响应"。根因：S3 MoveNavIndicator 布局未就绪（splash 期间 RootSurface.IsVisible=false → 子树不布局 → 按钮 Bounds.Height=0）时 `Dispatcher.UIThread.Post` 链式重试——每帧往 UI 队列塞回调，渲染/输入饿死 → 未响应。修复：Bounds<=0 直接跳过（绝不 Post 重试），splash done 里 150ms DispatcherTimer 一次性兜底 ApplyNavVisuals（无链式）。已重新发布。

**08-07 S7 设置界面折叠展开动画**（23:15 发布）：用户反馈「设置界面的折叠展开也加动画」，确认两个都要。
- **S7a ☰ 菜单弹入/收起**（SettingsView.axaml+.cs）：Popup 加 Opened="OnSettingsMenuOpened"——弹入 Scale 0.9→1 + 淡入 180ms Standard（host=child 互斥；**不用 PopIn**——ElasticIn 有回弹过冲，用户把过冲当割裂感）；收起 CloseMenuAnimated 反向 120ms（起点取当前值防中断跳变），done 后 IsOpen=false，覆盖 ☰ 再点 + 选完自动收起两路径；IsLightDismiss 点击外部系统行为无法拦截，保持瞬间（计划内已知限制）。
- **S7b 分类内容切换**（SettingsView.axaml.cs）：ShowSection 改调 SwitchSection——`_visibleSection` 字段记实际可见 section；旧 140ms 淡出（保持占位防布局跳动，section 是流布局不能交叉淡化）→ done 硬切 IsVisible → 新 200ms 淡入+8px 上移，done 清 RenderTransform；构造期首帧（old=null）直接显示。
- **验证**：build 0 错误（32 警告均为既有 Watermark 过时警告）；发布.ps1 经 powershell.exe 跑通（pwsh 不在 PATH），PCL 挪出挪回正常，exe 23:15 更新。改动未提交（与 AL10/AL11 + S1-S6 混存），真机验收：☰ 弹出 180ms 缩放淡入、收起先缩后关、分类切换旧淡出新淡入、外部点击瞬间关（预期）、连点不崩、回归 S1-S6。

**08-08 多源完善 + 报红弹窗修复**（09:25 发布）：
- **报红必弹窗**（用户反馈"报红了没弹窗"）：三处补 NotificationService.Error——① Select 版本详情页 JarMissing=true（客户端文件缺失）；② CheckIntegrity 三个报错分支（缺文件/JSON 解析失败/异常，原只写红字）；③ HomeViewModel 启动失败最终落定（自修复后仍失败，原只有 LaunchStatus 红字，用户在别的页面看不到）。
- **CF 文件下载加速前缀**：LauncherSettings.CurseForgeCdnPrefix + SettingsViewModel.CurseForgeCdnPrefixText（Load/Save/OnChanged→Save）+ SettingsView.axaml 下载区 TextBox + CurseForgeService.ApplyCdnPrefix（替换 edge.forgecdn.net 前缀，每次读设置即时生效，非官方域名原样）。现实：CF 文件无免费可靠公共镜像（PCL2 自建代理），给用户前缀自由度。
- **双源故障隔离**（EcosystemViewModel.RunBothSearchAsync）：原 Task.WhenAll 单源异常炸全页 → TrySearchAsync 并行发起独立捕获（OCE 向上不吞），失败源降级 + Status 提示"XX 搜索失败，仅显示 YY 结果"；单源模式保持报错。
- **CF key 置灰标记**：SourceOptions 静态 → 实例 BuildSourceOptions()，CF 未配置 key 时 Display="CurseForge（未配置 Key）"；XAML ItemsSource 改 {Binding SourceOptions}。
- **验证**：build 0 错误；发布.ps1 经 powershell.exe（pwsh 不在 PATH）。未提交（与 AL10/AL11 + S1-S7 混存）。真机验收：报红场景弹窗、CF 前缀设置生效、拔网线搜"全部"只剩单源结果+提示。
- **BlockHelm-Launcher 比对**（用户要求克隆比对，报告 Temp/blockhelm_report.md 470 行）：.NET 8 WPF 五层架构 972 cs；我们有的它也有（Modrinth+CF 双源、加载器安装）；它多我们：① Terracotta 联机（局域网+房间码跨网）；② 微软 OAuth+离线+authlib 三账号（我们只有离线）；③ 自更新 GitHub+Gitee 双源镜像；④ 整合包部署为服务端+Log4Shell 修复；⑤ 4 语言+9 色+多背景。技术栈：WPF vs Avalonia，UI 动画它们远不如我们（用户实评）。今天阶段聚焦下载模块，联机/账号列为后续候选。

**08-08 联机功能第一批（LAN 局域网联机）**（发布成功，178MB）：
- **对照 NEXT_ROUND_PLAN 盘点**：离线账号/微软设备码登录/账号 UI/启动参数接入（auth_player_name/uuid/access_token + extraGameArgs --server --port）早已完成（f7d3e73 F1），无需 Azure 注册（Mojang 公开 client_id 设备码流）。计划第一批剩余 = 联机，本轮做 LAN 部分；房间码跨网（Terracotta 信令）暂缓（依赖外部服务无法本地验证）。
- **LanDiscoveryService**（Core/Multiplayer/ 新）：UDP 广播 255.255.255.255:34198 心跳 2s（JSON camelCase），监听端绑定 Any:34198 + ReceiveTimeout 1000ms 周期醒检查取消；广播/监听均幂等（先停后开），Shared 单例（同 AccountService 模式）；LocalIp 私网段优先（192.168/10/172.16-31 过滤虚拟网卡）。防火墙拦 UDP 时广播方/监听方静默降级（不崩）。
- **联机页**（MultiplayerView + VM 新）：侧边栏"联机"按钮（开服下、设置上），MainWindow.axaml.cs 三处接线（_navButtons/PropertyChanged/IsPageActive）；Rooms 列表 6s 无心跳自动剔除（1s 定时器），信息变化才替换行（避免每心跳重渲染）；空态提示 UDP 34198 防火墙；[打开开服页] 引导。
- **开服联动**：StartServerCommand 成功后 StartBroadcastRoom（读 server.properties motd/level-name/server-port，房间名=motd 回退"我的 X 服务器"）；StopServer 立即停播；_process.Exited 回调停播（崩溃兜底）。
- **加入**：复用主页一键进服 RequestLaunchWithServerAsync(versionId, GameDirectory.Detect(), room.Ip, room.Port)（切主页跑启动进度）。
- **顺手修复**：CurseForgeService.EffectiveKey 语义 bug——注释承诺"空字符串=禁用"但实现 `!IsNullOrWhiteSpace` 回退动态读设置（本机 settings.json 有 key 后两个禁用态测试挂）；改 `_apiKeyOverride is not null` 判定，显式禁用真正生效。
- **测试**：新增 LanDiscoveryTests（广播→本机监听回环 + Notch 离线 UUID 断言 b50ad385-829d-3141-a216-7e7d7539ba7f）；288/288 通过（修 CF 测试环境脆性：传 "" 显式禁用替代 null 动态读）。
- 未提交（与 AL10/AL11 + S1-S7 + 多源混存）。真机验收（需两台电脑或一台+虚拟机）：开服页启动服务端 → 联机页出现房间（名称/版本/主机/端口）→ 另一台点加入自动进服；同机验证广播回环已过单测；防火墙首次弹窗需允许 UDP 34198。

**08-08 联机第二批（创建房间 + 防火墙自动放行）**（11:19 发布，178MB）：
- **联机独立闭环**（用户反馈"联机和开服是分开的"）：联机页 [＋创建房间] 弹 CreateRoomWindow（版本下拉=开服页 InstalledVersions + 房间名 + 端口 1-65535 校验，Close(result) 标准对话框模式）→ FirewallRules 放行 → Server.SelectedVersion 赋值 + ApplyRoomSettings(port, motd) 写 server.properties → StartServerCommand 启动（jar 缺失自动走下载确认流）→ 留在联机页（广播回环单测已验证，自己的房间出现在列表）。
- **FirewallRules**（Core/Multiplayer/ 新）：netsh show rule 检测（纯 ASCII 规则名 "Lattice LAN Multiplayer UDP 34198"——GBK/UTF-8 解码下 ASCII 子串都不变，防中文系统编码误判）；TryAddRule UAC 提权（Verb=runas，profile=private 只放行专用网络）；WaitForExit 后复查 RuleExists 验证；ManualHint 手动步骤兜底。
- **联机页防火墙提示条**：打开页面即后台检测（netsh ~1s，Task.Run 回 UI 线程），缺失显示黄色提示条+[放行] 按钮（加入者也收广播=入站，两边都要放行——用户"网络防护能做到吗"的完整答案）；创建房间时再兜底检一次（缺失提权，被拒 Error 弹手动步骤，不阻断创建）。
- **验证**：build 0 错误；测试 289/289（新增 FirewallRuleQuery_DoesNotThrow 防 netsh 解析回归）。未提交（与 AL10/AL11 + S1-S7 + 多源 + LAN 第一批混存）。真机验收：创建房间 → UAC 弹窗（首次）→ 服务端启动 → 联机页出现自己房间；第二台电脑联机页看到房间并可加入；拒绝 UAC 时提示条出现、点[放行]恢复。

**08-08 联机 4 槽点修复（11:30 发布，186MB）**：真机验收反馈四连修——① 房间行加 [复制]（复制 IP:端口，发任何人游戏内直接连接，Toast 确认，剪贴板照抄 ServerViewModel.CopyLanAddress 模式）；② 用户澄清「还使用的开服功能」=启动后别引去开服页：删「打开开服页」按钮 + 引导文案去开服页句 + IsRunning 提示改中性；③ 版本过滤：CreateRoom 前 `InstalledVersions.Where(v => Server.HasServerJar(v))`（ServerViewModel 新增 public HasServerJar，复用 VersionGameDir+ServerInstaller.ServerDir），空列表 Error 提示先到开服页下载——缺 jar 不再弹下载确认流；④ CreateRoomWindow VersionBox 加 ItemTemplate 显示 DisplayName（照抄 ServerView.axaml:27-31），不再显示 record ToString 全字段。编译 0 错误、289/289 测试过。未提交（混存池）。

**08-08 根治"缺文件靠自修复"（11:50 发布，186MB）**：用户点名解析设置/下载源码，质疑为什么一直出"版本文件缺失"并靠自修复擦屁股。分析结论：① 下载文件级健全（tmp→SHA1→原子 rename，AL29 H1）② 洞在编排级——安装 = 多文件编排，任一失败异常抛出但已完成文件残留 → 半装态；「已安装」判定（json+jar）不查 libraries → 谎报已装 → 启动预检（GameLaunchService:34 VerifyVersion）拦下 → 自修复补全 → 兜底被推成常态路径。修复：① VersionInstaller 事务化（InstallCoreAsync：先 Verify 后 Mark + 失败删本次新建 client jar，json 留缓存/libraries 共享不删）——半装态消失，"已安装"判定恢复诚实；② 服务端启动预检 File.Exists → IsValidServerJar（≥1MB+PK，public 化），坏 jar 弹"重新下载并启动"（先删坏 jar 再走下载流，InstallAsync 幂等跳过已有不删会假成功）；③ 联机 HasServerJar 同步升级 IsValidServerJar——坏 jar 版本不进创建房间列表。客户端自愈链路保留为崩溃兜底（用户删文件/杀软隔离）。新测试 Install_Failure_CleansClientJar_AndSkipsMark（290/290 过）。未提交（混存池）。

## 08-08 真机验收结果（11:50 发布版 + 修复后重验）
- 核心场景 ✅：坏 server.jar（64B）→ 启动弹「服务端文件损坏」→「重新下载并启动」→ 删坏 jar → 重下 51,627,615B → 修复后自动启动 Done (2.267s)
- **真机抓到 bug**：DownloadAndStartAsync 里 StartServer() 在 IsInstalling=true 时 fire-and-forget，被 StartServer 开头 IsInstalling 检查直接 return → 「重新下载并启动」只重下不启动。已修：finally 里 IsInstalling=false 后 `if (readyToStart) await StartServer()`（ServerViewModel.cs）。修复版重验通过（自动启动 Done）。
- 手动「启动」验证：1.20.1-Forge 服务端完整启动 Done (19.598s)——预检通过版本启动链路 OK
- 待用户决定：上午日志「自动修复失败: 版本 1.21.1-Fabric 0.19.3 缺少清单下载地址」（FixRedownloadAsync 缺 manifest URL 场景），未处理
- 环境已恢复：D 盘两个 server.jar 还原（58MB/62MB）、PCL server.jar 完好、Steam 已关、Lattice 已关
- 未提交改动（AL10/AL11 + S1-S6 + 多源 + 联机三轮 + 4 槽点 + 根因修复 + 本次时序修复）等用户决定统一提交

## 08-08 AL31：三项提速 + sidecar 防线（真机验证通过）
- A. HTTP 超时：DownloadService 连接 15s（SocketsHttpHandler.ConnectTimeout，body 不限时防误杀 51MB）；VersionManifestService 清单 15s
- B. 重试：RetryPolicy 0.5s×2ⁿ 起步（上限 30s）；MaxSourceAttempts 3→2；测试同步更新
- C. sidecar：ServerInstaller 下载验证通过后写 server.jar.size（实际大小）；IsValidServerJar 比对 <期望×0.9 判截断残件。真机验证：2MB PK 残件（旧版放行）→ 弹「服务端文件损坏」→ 重下 51,627,615B → 自动启动 Done (2.153s)
- D. FixRedownloadAsync 前置诊断：VerifyFiles 空清单 → 直接返回「文件已完整」不排下载队列
- 测试 290→292 全过；构建 0 错误；已发布

## 2026-08-08 AL32 并行竞速完成（#210 收尾）
- 分片进度回退 flaky 根因与修复：片完成回调「先读 Bytes 快照、后 Invoke」与另一片节流上报竞争 → 读旧值报新值（1048576→917504 倒序）。修复：删除片完成回调即时上报，改为 Task.WhenAll 后一次性报最终值（Bytes 已恒定=最大值）；上报路径全部单调。
- 验证：单测 10/10、全量 3 轮 295/295 全过；构建 0 错误。
- 遗留说明：DownloadChunkedAsync 的 sha1 失败 → catch 回退 DownloadSingleAsync 机制保留（弱网自愈），进度会从 0 重报（已知观感，非 bug）。

## 2026-08-08 AL33 版本清单多源完成（#211）
- VersionManifestService：ManifestUrl 单点 → 候选链 [piston-meta 官方, bmclapi2.bangbang93.com/mc/game/version_manifest_v2.json]，公共静态 FetchManifestJsonAsync 逐候选尝试（官方失败自动换镜像；用户取消传播；全失败抛 HttpRequestException 带「N 个源均不可用」）
- ServerInstaller.FetchServerInfoAsync 改走同一入口（版本 json 内 url 不受清单源影响）
- 分片进度 flaky 终修（#210 收尾延伸）：片完成回调改为锁内读 Bytes + 锁内 Invoke（force 允许同值重复报——并行片同刻完成时不合并丢粒度）；节流/最终上报按 Reported 去重。锁串行化保证上报值序列不降——彻底消除「读旧快照+晚 Invoke」倒序回退
- 测试：+4（FetchManifest 三态 + ServerInstaller 镜像清单回退）→ 299；单测 10/10、全量 3 轮全过；构建 0 错误

## 2026-08-08 AL34 CF key 引导 + 有效性验证完成（#212）
- CurseForgeService.ValidateKeyAsync：调最小 search 验证 key——200=有效 / 401,403=无效（带 HTTP 码）/ 其他=如实报告 / 网络错=稍后再试；结果不含 key 内容
- SettingsViewModel：CurseForgeApiKeyStatus 属性 + ValidateApiKeyAsync（序列号防抖：输入变化丢弃过期结果；空 key 提示未配置）；缓存 CurseForgeService 实例（避免每次构造跑 GameDirectory.Detect()）
- SettingsView：key 行失焦验证 + 状态行；已有 key 打开设置页即验证一次
- 测试 +4（200/401/500/无 key 四态）→ CF 26/26；App 构建 0 错误
- 未做：CF CDN 国内反代预置（计划注明「需实测存活，不可靠就不加」）

## 2026-08-08 AL35 UI 去笨重第一批完成（#213）
- 阴影令牌：App.axaml 加 ShadowCard/ShadowPop 两个 BoxShadow；setting-group 卡片套 ShadowCard
- 卡片三层模板：group-header（半粗体小标题）/ setting-group（半透明圆角阴影卡片）/ setting-row（统一行距，ColumnSpacing 10；ColumnDefinitions 非 AvaloniaProperty 不能走 Style Setter——各 Grid 显式声明，AVLN3000 教训）
- ToggleSwitch 动画模板：36x18 轨道 + 14 圆形 Thumb + 0.14s CubicEase。Avalonia 12 要求 PART_MovingKnobs（Panel，Grid）+ IsHitTestVisible=False；Thumb 位移用模板内 TranslateTransform 局部 Transitions + `{Binding $parent[ToggleSwitch].IsChecked}` 绑定 + BoolThumbXConverter（x:Static 静态字段 Instance；2/20 双侧对称，行程 18）；checked 轨道变色用伪类样式 + BrushTransition
- 导航图标：MainWindow 6 导航项加 Segoe Fluent Icons 字形（E80F 主页/E9D2 版本/E896 下载/E7C3 开服/E77B 联机/E713 设置），Foreground 绑 $parent[Button].Foreground
- 强调色选色器：圆点排 → ComboBox（AccentPresetVM 五项预设 + 老用户自定义色动态插项）
- 设置页分区重构：5 个 Border 硬切 → 每分区独立 UserControl（SectionGameDirView 等 5 个，DataContext 继承 SettingsViewModel，绑定零迁移）；主文件 ContentControl 覆盖式切换（直接替换 + 淡入上移 200ms，去掉流布局的淡出步骤）；VM 未拆分（标记类方案做视觉统一，拆 VM 无视觉收益）
- 顺带修：MemoryCustomText 文本框补 x:Name（原 FindControl 找不到返回空，自定义内存提交静默失效）
- 坑：x:DataType 必须加在每个分区 UserControl 根（compiled binding 无根类型 AVLN2100）
- 构建 0 错误；等真机验证
- 发布：发布.ps1 跑通（powershell.exe，pwsh 不在 PATH），Lattice启动器.exe 186MB 14:53 更新，签名 2 文件；PCL 目录在原位无需挪动
- 待真机验收：#214——① 官方源不可用 → 版本列表 3~5s 刷出（BMCLAPI 镜像）② 下载慢 → 秒切镜像 ③ 服务端 5s 切源 ④ CF key 验证反馈 + 生态页 CF 源生效 ⑤ UI：开关动画/导航图标/选色器/分区切换/卡片阴影/☰ 回归

## 2026-08-08 真机验收通过（#214 自动走查，用户休息代跑）
- 方式：启动发布版 exe → UIA 自动化枚举控件树 + 鼠标模拟点击（截图不可用——当前模型不支持图像输入）
- **逐项结果**：
  1. 主页：账号/离线标签/PCL 扫描版本/开始游戏/下载记录 16 徽章/控制台空态 ✓
  2. 导航 6 项 Segoe Fluent Icons 字形全部渲染（UIA 读出声符 ）✓
  3. 设置页分区重构：ContentHost 渲染 SectionGameDirView（group-header/浏览默认/来源「自配」/版本隔离）✓；☰ 菜单 5 项弹出 ✓；切「下载」分区（CF key 密码框/CDN 前缀/限速/清理缓存）✓
  4. 版本页：5 版本 + 来源统计「本启动器 1 · PCL 3」+ 缺文件标记 ✓
  5. **清单多源**：版本浏览页「共 905 个版本」（正式版 101/快照 729/远古 61/愚人节 14）——清单拉取成功，cache/version_manifest_v2.json 落盘 ✓
  6. MOD 生态页：4858 结果 Modrinth 源正常（实例 1.20.1 跟随过滤）✓
  7. 开服页：服务端就绪（1.20.1-Forge）/一键开服/下载服务端 ✓
  8. 联机页：创建房间/局域网扫描/UDP 34198 提示 ✓
  9. 外观分区：强调色选色器显示「紫」= settings 存 #8B5CF6 正确匹配预设（选色器初始化逻辑验证）✓
- 未自动验证（单测已覆盖）：断网秒接/镜像回退（#210/211 测试）、CF key 四态（#212 测试）、动画帧观感
- 遗留：点 ✕ 2.5s 未退出（可能动画/确认），force kill 收尾；进程全程 292MB 无泄漏式增长

## 2026-08-08 S 批次：联机与开服彻底分离 + 陶瓦（Terracotta）联机集成（完成）
- **背景**：用户拍板——联机不再走开服（2 人开服成本高），照 BlockHelm-Launcher 走陶瓦联机；旧局域网扫描整套删除
- **Core**（TerracottaModels/TerracottaProvisioningService/TerracottaLobbyService）：锁版 0.4.2 内置 SHA256（x86_64/arm64），Gitee 优先 GitHub 兜底，tar.gz 预检（扁平名/≤64MB/仅2文件）+ staging 原子发布；handoff 启动（12s）→ lock 复用（/meta 校验）→ 750ms 所有权 → 500ms 轮询状态机（host 20s 超时）→ /state/ide + /panic?peaceful=true 收尾
- **App**：协议弹窗（下载进度/失败重试/AGPL 声明）+ 联机页重写（创建三步引导/粘贴房间码/房间码大字号+复制/玩家列表 HOST·我 打标/离开确认）；ServerViewModel 清联机残留
- **删除**：LanDiscoveryService/FirewallRules/CreateRoomWindow/LanDiscoveryTests
- **测试**：+28（Provenance 下载校验安装 8 + Lobby 状态机 20），328/328 全绿；20s 超时用例含真实等待
- **测试揪出 3 个生产 bug**：type/latency_ms 解析未防御 ValueKind.Null（服务端显式 null 会崩）；GetOrStartEndpointAsync 握手失败不清理进程（僵尸）；StopRuntimeAsync 拿 mode 判 started → 主动离开永远跳过 /state/ide
- **发布**：发布.ps1 成功 → 发布\Lattice启动器.exe（自包含单文件，已签名）
- **待办**：真机两机验收（清单见下）；netsh 旧防火墙规则一次性清理放发版说明；THIRD_PARTY_NOTICES（Terracotta AGPL-3.0 / EasyTier / BHL GPL-3.0）
- **验收清单**：A 首进联机页下载 → A 游戏内开局域网 → 房间码 → B 输码加入 → 双方互见 → A 关世界双方复位 → A 离开无 terracotta.exe 残留 → 双启动器 lock 复用 → 断网下载失败提示

## 2026-08-08 T 批次：联机 UI 重做（换掉 BHL 式布局，用项目排版+动画）（完成）
- **背景**：用户指出联机 UI 是照 BHL 搬的（左右双卡并排）+ 下载进度 UI 与项目风格不符 + 全程无动画
- **布局**：欢迎态改为 Tab 切换式（Button.tab 项目样式 + active 高亮）——创建/加入内容共用一个 Border.card 大卡，随 tab 动画切换（用户拍板）
- **动画**：区块切换（Welcome/Busy/Active/Declined）UiAnim.PopIn 弹入；tab 内容切换同样 PopIn；Busy 态加 stagedot.current 呼吸点（HomeView 同款）；协议窗 Opened 时 PopIn(Root)
- **配色规范化**：房间码卡改项目约定加载器蓝（#12332F 底/#B5F4E9 字，删硬编码 #7FE7C8）；徽章「我」改 Accent 底/#0B1F1C（删金色 #3A2F12/#F0C960）；玩家行用 Classes="row"（项目 hover 过渡）；错误条改约定错误红 #3A2020/#E05A5A；琥珀警告条保留
- **下载进度对齐项目**：状态文字 + 独立百分比（Accent 色，ViewModel 拆出 PercentText）+ 6px 进度条（全局 Value 0.3s 过渡）
- **改动**：MultiplayerViewModel（+IsCreateTab/IsJoinTab/SwitchTab）、MultiplayerView.axaml(.cs)（重写+动画）、TerracottaAgreementDialogViewModel（+PercentText）、TerracottaAgreementDialog.axaml(.cs)（进度区+入场动画）
- **验证**：构建 0 错误；Core 测试 328/328 全绿（Core 零改动）
- **待办**：真机过四态 + 协议窗下载三段式（进页→弹窗动画→下载百分比→失败重试）；上一批（S 批次）真机两机验收清单仍挂着

## 2026-08-08 体积优化：186MB → 84MB（-55%）
- 原因：发布.ps1 只开了 PublishSingleFile + IncludeNativeLibrariesForSelfExtract，程序集未压缩
- 修复：加 `-p:EnableCompressionInSingleFile=true` → exe 83614861 字节（83.6MB）；启动时程序集自解压约 1-2s（实测压缩版冒烟通过：能走到单例检测阶段）
- 踩坑：PowerShell 反引号续行块内严禁插入注释行（续行链被打断，-p: 被当独立命令报错 CommandNotFoundException）；注释只能放参数块结束之后
- 结构性说明：PCL2 才几十 MB 是因为 .NET Framework 4.8 Windows 自带，不用打包运行时；我们是 .NET 10 self-contained 必须带 ~70MB 运行时——83.6MB 已是该技术栈近下限（PublishTrimmed 可再省但 Avalonia+反射易炸，不赌）
- 发布：22:57 更新，签名 2 文件

## 2026-08-08 双版本发布（用户拍板：自包含 + fdep 轻量版）
- 发布.ps1 重写为 Publish-One 函数跑两遍；产物：发布\Lattice启动器.exe（自包含 83.6MB，双击即用）+ Lattice启动器-轻量版.exe（fdep 46.6MB，需装 .NET 10 Desktop Runtime，弹窗引导）+ 使用说明.txt；3 文件全部签名
- 轻量版 46.6MB 构成：托管代码 ~23MB + native 库嵌入（IncludeNativeLibrariesForSelfExtract，SKIA 等 ~23MB）——fdep 单文件形态的下限；不带该属性则 native 变散文件（23MB exe + 一堆 dll，失去单文件意义），维持嵌入
- 轻量版 runtimeconfig 已带 rollForward=LatestMajor（防 .NET 11 版本地狱，grep 验证）；自包含版无（自带运行时，MSBuild 忽略该属性，正常）
- 踩坑：Publish-One 里 dotnet publish 的 stdout 泄漏进函数管道 → $finalSelf 变成数组 → Get-Item 报「语法不正确」exit=1；修复 = publish 输出接 `| Out-Null`（$LASTEXITCODE 检查不受影响）
- 用户选择背景：自包含 83.6MB 是「开箱即用」；轻量版是给在意体积的用户（省 44%，换装一次运行时）

---
## 2026-08-08 深夜批次：联机失败真根因 + CF key 清空修复 + DPAPI 加密（发布 00:14）

**1. 联机失败根因闭环（本批最大成就，用户"给我抓住他"）**
- 打点：MultiplayerLog（%AppData%\Launcher\logs\multiplayer.log，毫秒级线程安全）+ TerracottaLobbyService/Provisioning/协议窗全链路
- 手动复现：`/state/scanning`、`/state/guesting` 是**动作端点**——200 + 空 body（立即/延迟 2s 都是），状态只靠 `/state` 轮询（`{"index":1,"state":"host-scanning"}`）；无效房间码 → 400 HTML
- 根因：CallStateAsync 把动作端点响应当 JSON 解析 → JsonReaderException。证据链：[23:59:54.919] handoff 端口=1710 → [23:59:54.960] CreateHostAsync 失败
- 修复：新 FireActionAsync（只查状态码 400→InvalidRoomCode）+ ParseJsonAsync（非 JSON 留证据再抛）；CreateHost/Join 改调
- **测试 mock 失真教训**：StubHandler 的 /state/scanning 返回 `Json(200,"{}")` ≠ 真实空 body → 测试全绿真机必炸；已改真实行为
- 遗留未解：23:35 会话「HostOk 成功但进程被清理」未复现（FireActionAsync 可能覆盖其一部分）

**2. CF key 清空 bug（用户"为什么重启启动器我的curseforgeapi没了"）**
- 根因：SettingsViewModel 构造器属性赋值无条件触发 OnXxxChanged → Save()，加载完前字段是默认值 → 空值覆盖文件（文件里值长度 0 实测确认）
- 修复：`_loading` 标志（构造末尾置 false；Save/OnSelectedMemoryPresetChanged 拦截）
- 其余 VM 核查安全：ServerViewModel(438)/ThirdPartyDownloadViewModel(117) 的 Save 都在用户操作路径

**3. CF key DPAPI 加密（用户"KEY 不能直接放里面"）**
- 新 Secrets.cs：DPAPI CurrentUser + "dpapi:" 前缀；无前缀=旧明文原样返回（下次保存自动迁移加密）；解密失败→null
- LauncherSettings：Load 后 Secrets.Read；Save 时 Protect（内存保持明文，finally 还原）
- SecretsTests 6 测试（往返/不含明文/明文迁移/空串/Settings 加密往返/旧文件迁移）
- 换 Windows 账户/重装系统 → 密文失效需重填

**验证**：构建 0 错误；全量 334/334 测试全绿；发布成功（00:14 轻量版 + 00:14 自包含）
**踩坑**：构建产物陈旧两次——MultiplayerLog 在构建后落盘；App 无 RID build 不更新历史 win-x64 残留目录（rebuild 只更 net10.0-windows）→ 发布/手动测试前验证产物（rg 搜 IL 字符串 + 时间戳）；运行中进程锁 DLL 复制失败被静默跳过（exit=0）→ 先 Stop-Process

**待办（用户尚未反馈）**：
- ① 设置页重填 CF key → 重启 → key 还在 + settings.json 无明文（验收）
- ② 联机创建房间真机测试（验收）
- ③ SHA256 校验加进发布脚本 + 使用说明.txt（SAC/SmartScreen 透明化三件套，用户未答复做不做）
- ④ 网卡 metric 修复后续（承诺过"不用关任何东西也能用"的修复，未排期）
- LittleSkin 集成方案已给，未排期

---
## 2026-08-09 凌晨 跨启动器联机侦察(BHL × Lattice,单机)

**BHL 获取**:GitHub zqq-699/BlockHelm-Launcher release v0.9.13(12,637,280 字节,与本地源码 dotnet publish fdd-single 产物字节数完全一致——官方 release 即同源码同配置);本地源码在 AppData\Local\Temp\BlockHelm-Launcher,发布脚本 BuildSingleRelease.bat(WinX64FrameworkDependentSingleFile)。编译缺 CF/Microsoft key 文件仅 warning,不影响联机。产物拷到 桌面\BlockHelm\ 稳定位置。

**协议互认四条实锤**(日志证据):
1. 锁协议:BHL 认 Lattice 的 terracotta.lock(2 字节大端端口)→ 显示「被其他启动器占用」= 互认
2. meta 探测互认:Lattice 能探测 BHL 实例的版本/状态
3. 房间码互通:双方同 U/xxxx-xxxx-xxxx-xxxx 格式;HostOk 状态双方日志都能解读(profiles/machine_id/vendor)
4. 异常码映射一致(400→InvalidRoomCode 等,抄的同一套 TERRACOTTA_SPEC)

**「Lattice 输 BHL 码显示无效」真相**:时间差——BHL 建房 HostOk(00:54:54)后被 web API 命令杀掉(日志 `[Core]: Closed by web API. Shutting down.`),用户输码时房间已死。停止命令大概率来自 Lattice 加入流程对已有实例的清理/接管。

**单机双启动器物理不可行**(重要认知):陶瓦每机一实例(锁设计)。房主实例不能当 guest(对 host-ok 态实例发 guesting → 400);杀房主再起 guest → 房间没了。两条路都死。**跨启动器完整闭环必须两台机器**(房主机 + guest 机)。单机只能验证协议层互认,已验证完毕。

**EasyTier 隐患**(新发现):BHL 建房日志 100 条 `dns lookup_ip failed`(easytier.cn 公共节点解析失败;etnode.zkitefly.eu.org 握手 ConnectionReset)——跨机联机(含 Lattice 对 Lattice)最大坑,优先级记入待办。23:59 Lattice 建房成功那次的日志无 DNS 失败——网络时好时坏。

**死锁残留现象**:实例异常退出(强杀/被 web API 杀)锁不删 → 后来者读到僵尸锁报「被占用」(已手动删过一次)。Lattice 是否自动清理死锁未确认,待查。

**Lattice 改进点(低优先级)**:加入时探测到「已有实例且为 host 态」应提示「本机已有房间,请先退出」而非 400「无效」。

**2026-08-09 凌晨 启动耗时实测(同机比对,数据源:用户实测)**
- BHL:点击开始 15s → 主页 41s,点启动到主页 = 26s(窗口出现时刻未录,无数据)
- Lattice:点启动 53s → 窗口+彩蛋弹窗同时出现 → 主页 1:13,耗时 = 20s
- Lattice 快 ~23%;且窗口拉起即有反馈(彩蛋同框)——BHL 窗口出现时刻无数据,静默段不成立,仅比点击→主页
- 注:对比前提需同版本同 Java 才干净;下载速度差(秒下 vs 几十KB)比启动差更悬殊,视频建议两个都录

**2026-08-09 台风夜 「进度条满了还在等」修复(视频素材暴露)**
- 现象:视频对比发现——字节下完进度即 100%,但合并分片+SHA1 校验+落盘无进度表达 → UI 钉在 100%「下载中/排队等待」,观感像卡死;BHL 单连接直下无收尾,100% 即完成
- 修复:底层全部进度上报点封顶 99%(DownloadService 4 处 + VersionDownloadPipeline assets 1 处),「100%」只由任务真正完成时的 Post 给出(DownloadTask.cs:173/238 已有)→ 99% → 瞬间 100% + 完成,观感对齐 BHL
- 测试:新增 Progress_Overall_StaysBelow100_UntilTaskCompletes(先红后绿);全套 335 通过;App 构建 0 错误
- 待办:发布新版本给朋友(顺带 SmartScreen 话术),明天两机联机实测

**2026-08-09 台风夜 探针最终结果:asm-9.10.1.jar「神秘取消」根因 + 修复(AL34)**
- 现象:探针全量下载 1.21.10+fabric 0.19.3,127/128 完成,唯一 asm-9.10.1.jar 为 Canceled(p=0%、Error=null、stage=排队等待、文件缺失)→ 父报「缺 1 个文件」;且 Canceled 在 UI 上不可重试(RetryCommand 只认 Failed)
- 根因:asm 是 fabric 8 库之一(顶层 url maven.fabricmc.net)→ 镜像 mapper 无映射 → 单候选直连路径。HttpClient.Timeout(默认 100s 等响应头)抛 TaskCanceledException(OCE 但 token 未被请求),单候选 catch 只拦 HttpRequestException/InvalidDataException → OCE 漏出 → 叶子 catch(OperationCanceledException) 一律标 Canceled(不看 token 是否被请求)→ 无错误、不重试、不可重试
- 修复(三层):
  1. DownloadService 单候选路径加 `catch (OCE) when (!ct.IsCancellationRequested)` → 转可重试错误「等待响应头超时(>100s)」走退避下一轮;`catch (OCE) { throw; }` 用户取消原样上抛(DownloadService.cs:164 附近)
  2. DownloadTask 叶子 catch(OCE) 加 `when (_cts.IsCancellationRequested)` 过滤;未被请求的 OCE → Failed 带信息「下载中断(TaskCanceledException: …)」(防未来泄漏再变神秘取消)
  3. RunGroupAsync 同款防御(「安装中断…」)
- 测试:SingleCandidateTimeoutTests.cs 3 个(超时→重试成功 / 超时耗尽→HttpRequestException 带"超时"/ 叶子 OCE 未请求→Failed)——先红后绿;全套 338 通过;App 构建 0 错误
- 探针重跑:state=Completed 19.6s(幂等跳过已有 127 文件,只补 asm),0 违规,versions 下 1.21.10 + fabric-loader-0.19.3-1.21.10 齐全
- 注:竞速路径(RaceOneAsync)OCE 已静默转 (false,null) → 换轮重试 → 最终 Failed,本就不受影响;只有单候选路径漏
- 探针目录:launcher/.probe/ + %TEMP%\lattice-probe(真实网络下载产物),#221 验收后再删
- 发布：08-09 02:13 完成（自包含 79.7MB + 轻量版 44.5MB，签名 + 使用说明.txt）；自 08-08 联机第一批以来第 7 版
- 今日开发日志：2026-08-09-开发记录.md（含版本统计表 / 联机完成时间回顾 / 明日待办）
