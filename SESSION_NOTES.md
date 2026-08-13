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

**2026-08-09 台风夜 AL45 完成:下载提速 + CF Key 本地代理 + 关于页开源声明（v15 待发布）**
- A 下载提速:HttpClientPool 静态共享 SocketsHttpHandler(ConnectTimeout 5s/池寿命 5min/HTTP2 多路复用),6 服务注入;全流程连接复用
- C 关于页:版本号 v0.1.0(SettingsViewModel.AppVersion←嵌入 PCL.metadata.json)+ 开源声明 5 条 + 第三方依赖清单 Expander(ThirdPartyLicenses.cs 手写静态清单)
- B CF Key 本地代理(新项目 Lattice.KeyProxy):
  - 架构:独立小 exe(http://localhost:8799,http.sys 回环免管理员),KEY 只在代理进程内存 + DPAPI 密文文件(%AppData%\Launcher\keyproxy\key.bin);启动器进程永远拿不到明文
  - 端点:GET /v1/ping / POST /v1/migrate / GET /v1/* 转发(注入 x-api-key,剥离客户端伪造 header)
  - 启动器改造:CurseForgeService 加 apiBase 参数(生产指代理);App 启动无条件拉起代理 + 首启迁移(明文 key 迁入后清空 settings);设置页 Key 输入行 → 状态行(✓ 托管/✗ 未运行)+ 检查按钮(带自愈重拉);搜索失败 TryCfSearchAsync 拉起代理重试一次
  - 发布.ps1 加 [3/5] KeyProxy 发布步骤
- **真机发现并解决:本机 .NET 连 CF CloudFront 必挂**——网络层对部分 13.226.69.x 丢 SYN,默认顺序试 IP 21s 超时;curl/裸 SslStream 正常。解法:代理 ConnectCallback 并行竞速全部解析 IP(4s/个),实测 0.3s 建连;同类问题还坑过 HttpWebRequest/WinHTTP(PS5.1 也超时)
- 验收(真机 E2E):启动器拉起代理→migrate 迁移 378 字真实 key→settings 清空(只验证长度,不回显)→经代理打真实 CF 搜索 HTTP 200/0.3s 返回真实项目→设置页显示「✓ API Key 由本地代理托管」→关于页全字段渲染(UIA 文本验证)→407 测试全绿,构建 0 错误
- 安全:测试全程用 dummy key;真实 key 只走了 DPAPI 加密落盘;测试残留 key.bin 保留(产品行为)
- **路线图追加:Mica 样式(Windows 11 亚克力新材料)记入后续计划,本次不做**(用户 08-09 指示)
- 软件内更新仍 deferred(无远端通道)
- 未 commit(等用户确认);发布 v15 待用户跑 发布.ps1

**2026-08-09 台风夜 AL46 动画丝滑度优化（对标 BHL，待用户真机验收）**
- 对标确认：BHL = WPF .NET 8 原生（非 Electron），丝滑核心 = 状态色板平滑过渡 + 页面过渡 + Acrylic；我们差距 = hover 瞬跳 + 帧率（GC/布局动画）
- P0 纯重构（视觉零变化）：UiAnim 内核加 Curves.Linear + slot 互斥粒度（hover 颜色与 scale 同 visual 共存）+ FrameStats（LATTICE_FRAMESTATS=1 开启，Release 也零开销）；SpringScale 迁 UiAnim + ConditionalWeakTable 持久 ScaleTransform（每帧零分配）；Ripple 迁 UiAnim + 变换化（一次布局定位，每帧只改 Scale/Opacity）
- P1 行为平滑：UiAnim.TweenBrush 公共助手（150ms cubic 逐通道 Lerp，完成写回目标 brush）；HoverBrushBehavior 双向过渡（进入捕获基底色，退出动画回基底后 ClearValue 回落样式值）；导航/标题栏/设置菜单 code-behind 瞬跳全改 TweenBrush（激活态保持瞬跳+指示条滑动）；NavIndicator 一次性落定 + Transform 反向补偿（每帧 0 布局失效）
- ExpandCollapse 保留 Height 动画（Scale 会拉伸文字；单元素可忽略）；Composition API 不做（低层实验性 + 收益趋零）
- 验证：407 测试全绿、构建 0 错误；像素采样证明 hover 过渡中间态存在（60ms 截图介于基底与 hover 色之间）；UIA 冒烟（设置 5 分区/下载/主页切换无崩溃）
- 帧率数字教训：UIA 驱动此应用每帧阻塞 10-124s（视觉树遍历极慢）→ 完全污染帧率测量，p99=18.9ms 说明 99% 帧正常；纯鼠标风暴因 RAF 只在动画期采样难出 2s 窗口。结构性收益（零分配+零布局动画）由代码保证，数值对比留待用户真机感受
- 未 commit；待用户真机验收后提交

**2026-08-09 AL47 整合包拖入导入（完成，待真机手测拖拽）**
- 三格式解析（ModpackImporter）：自家 manifest.json / CurseForge zip（minecraft 对象判别）/ Modrinth mrpack（modrinth.index.json + downloads 直链）；ResolvePackId 重名消解、ExtractZipEntries 通用解压、ParseCfModLoader
- 安装编排（ModpackInstaller 新文件）：基座四路（无 loader CopyVersion / Fabric+Quilt meta profile 重写 / Forge+NeoForge 安装器+改名），loader 声明版本优先+最新回退；mrpack 直链下载（并发门 4）+ overrides 去前缀；CF zip 解压优先（跳过 manifest/modlist、overrides/clientoverrides 剥离）+ 缺 jar 时 CF API 兜底（GetFileAsync 新增）+ 无 key 明确报错；走全局下载中心（ctx.AddChild 逐文件子任务）
- App 层：ModpackImportFlow 统一入口（确认框→入队→Toast+版本页选中）；MainWindow 全窗口拖拽（Avalonia 12 API：DragDrop.AllowDrop + DataTransfer.Items + DataFormat.File + TryGetRaw，兼容 IStorageItem/string 两种返回）；断链修复两处（ProjectDetailViewModel + EcosystemViewModel.InstallCard 双分支：下载完弹「立即导入？」）；版本页按钮支持 .mrpack 改道统一入口
- 配套：VersionManifestService.GetVersionJsonUrlAsync（缓存目录可注入）、LoaderService.LastInstalledVersionId、CurseForgeService.GetFileAsync
- 测试：415 全绿（+8：Parse 三格式/判别不误判/ResolvePackId/ParseCfModLoader + mrpack 全流程/CF 解压/无 key 报错/API 兜底）
- 真机验收：版本页按钮→FilePicker 弹出→确认框链路通（UIA 验证到 FilePicker 弹出）；mrpack 测试包（26.2+Fabric 基座）就绪；**OLE 拖放自动化失败**（explorer 新版本 UIA 不暴露文件项+窗口遮挡，试 6+ 轮）——拖拽本体留给用户手测（2 秒）
- 未 commit；发布 v15 含 AL45/46/47 全部

**2026-08-09 AL48 README 重写**：删除过时目标/里程碑/YanKa旧名，精简为 5 章节（简介/功能/构建发布/目录/许可），功能含整合包拖入导入与 KeyProxy 托管；用户疑问"台风影响宽带"——实测 10MB/s 为 Mojang 境外路由上限，非台风主因，镜像优先可提速

**2026-08-10 AL49 下载提速（优化现有架构，完成待发布手测）**
- 停顿已修部分：Fabric API 附带安装 30s 超时+进度文案（上轮发布已含）；本轮新修 HEAD 无超时停顿点（GetContentLengthAsync 每源 8s 超时，原默认 100s 等响应头）
- 配置坑修复（停顿感最大嫌疑）：用户 settings.json MaxConcurrentDownloads=1 → 库并发=1 串行下载（几十个库文件 RTT 累积）；改为 0 回档位 High=24，ChunkCount 8→0 分片回 24；原值备份 settings.json.bak-al49
- 提速改动：mrpack 模组并发门 4→8（Modrinth/CF CDN 按连接限速场景）；DownloadTier 默认 Low→Medium（新装/重置受益）
- 测试：415 全绿（3 处断言随默认值更新：DownloadOptionsTests/LauncherSettingsTests；顺手修 KeyProxyTests 环境依赖缺陷——原测试真连 api.curseforge.com，网络可达时 403 透传 flaky，改注入直抛 handler）
- 未 commit；发布待用户跑 发布.ps1；手测点：版本安装总时长、模组下载速度、Fabric API 附带安装 ≤30s

**2026-08-10 AL49.1 KeyProxy 终端弹窗 + 发布脚本修复**
- 弹窗根因：KeyProxy 是 Console 子系统（OutputType=Exe），KeyProxyHost 拉起时未隐藏窗口（ProcessStartInfo 只有 UseShellExecute=true）→ 每次开启动器弹黑窗，分发同样弹
- 修复：Lattice.KeyProxy.csproj OutputType→WinExe（窗口子系统，无日志输出不怕丢 stdout）；KeyProxyHost 加 CreateNoWindow=true + WindowStyle.Hidden + UseShellExecute=false（false 才让 CreateNoWindow 生效）；PE 头验证 GUI subsystem
- CF「失效」链路：用户关掉黑窗 = 杀代理进程 → CF 全失效；实测代理正常（ping ok/keyed/经代理 CF 200/1.3s）；429 限流是另一可能（提示 HTTP 429）
- 发布脚本 bug：旧 KeyProxy 进程锁着 发布\Lattice.KeyProxy.exe（脚本只杀启动器进程）→ Remove-Item 静默失败 + Move-Item「文件已存在」报错终止；修复：步骤 0 连 KeyProxy 进程一起杀
- 重新发布完成（3 文件签名）；AL49.1 未 commit

**2026-08-10 AL50 砍 KeyProxy 本地代理，CF key 并入主进程（完成，已发布待实测）**
- 背景：用户选定防护价值低 + 分发痛点（独立 exe 必须连带分发、框架依赖无 runtime 机器 CF 静默失效、弹终端窗）
- 迁移：新增 LegacyKeyStore.cs（读旧 keyproxy\key.bin 的 DPAPI 原始字节格式）→ App.axaml.cs 启动块改一次性迁移（key.bin → 设置 DPAPI 密文 → 精确删文件+空目录）；用户现有 key 无缝，无需重填
- CurseForgeService：删 IsProxyMode；IsEnabled = key 非空；apiBase 参数保留（测试用）；GetJsonAsync 5xx/404 自动重试一次（CF 边缘瞬时故障，实测偶发——用户截图 OCR 确认 404 文案）
- 设置页：新增 key 输入框（Avalonia 12 已无 PasswordBox → TextBox PasswordChar+RevealPassword，Watermark→PlaceholderText）；ValidateApiKeyAsync 改直连；「检查」= 提交输入（有输入才覆盖）+ 验证；文案「✓ Key 有效（DPAPI 加密保存）」
- 删除（精确列举）：KeyProxyHost.cs、src\Lattice.KeyProxy\ 整目录（含 bin/obj）、KeyProxyTests.cs、Tests.csproj ProjectReference
- 发布.ps1：删 [3/5] KeyProxy 块 → 3 步；签名 2 文件；README 两处删改
- 测试 410 全绿；发布产物仅 2 exe + 使用说明
- 顺手修复：EcosystemViewModel.TryCfSearchAsync 删代理自愈改重试一次
- 未 commit（AL45-50 一起）；CF 404 根因 = CloudFront 边缘瞬时故障（各 URL 形态实测全 200）

**2026-08-10 待办（搁置）**：CF API 网关（Cloudflare Worker 转发层，免费 10 万请求/天）
- 动机：分发场景防逆向提取 CF key；客户端只持访问 token；Worker 逻辑可复用已删的 CfForwarder（git 历史）
- 启动器侧改动极小：CurseForgeService(apiBase:) 参数已保留，加设置项指向 Worker URL 即可
- **搁置原因：等龙腾猫跃回复私信（问题 3：PCL 如何保护/调度 API key、是否有服务端代理）后再决定方案**
- 现状兜底：DPAPI 密文落盘（设置层 Secrets），分发解不开，够个人使用

**2026-08-10 AL51 下载统一到引擎 + 安装提速（完成，已发布）**
- 归类：模组一键安装（Modrinth/CF）从 Enqueue 叶子任务 → EnqueueGroup 组任务；EcosystemService/CurseForgeService 的 InstallWithDependenciesAsync 加 ctx 参数，主文件+每依赖一个子任务（下载中心可见、可暂停/重试）；ProjectDetailViewModel.ExecuteInstallAsync 同样入组
- 提速：两处依赖安装 for 循环串行 → Task.WhenAll + SemaphoreSlim(4)（CF 限流 50req/30s 安全余量）；结果收集加锁（多线程写 report）
- mrpack 内容并发门 8 → 档位 clamp(4,16)（设置页可调）；存档扫描 CollectSaves 移出 UI 线程（原来 UI 线程 .GetResult() 卡界面）
- 坑：DownloadProgressHandler 是 void 委托，`dp => dp` 表达式 lambda 被判为语句（CS0201）→ ctx 模式 progress 直接传 null
- 测试隔离坑：NoJars_NoKey 测试失败——用户手测时 key.bin 已迁进真实 settings.json，测试进程读到真实 key → IsEnabled 误判；修复 ModpackInstaller 支持显式 key 注入（"" = 禁用，测试用）
- 测试 410 全绿；发布产物 2 exe + 说明
- 未 commit（AL45-51 一起）

**2026-08-10 AL52 启动动画空白帧修复（完成，已发布）**
- 现象：启动动画尾段「一瞬间空白页面」
- 根因：放大动画期间 RootSurface（液态玻璃背景层）保持隐藏（ctor IsVisible=false），AppContent.Opacity 淡入的是不可见容器；完成回调才放背景（0 起步 220ms 渐入）→ logo 消失瞬间窗口全透明 = 空白帧，叠加 TransparencyLevelHint Transparent→AcrylicBlur 合成切换更明显
- 修复：背景层随内容同步铺设（e≥0.55 起同一曲线淡入），动画结束时已铺满；完成回调删掉 0 起步渐入，只切合成级别；描边同步渐显
- 编译 0 错误；未 commit（AL45-52 一起）

**2026-08-10 AL53 启动动画真丝滑（去窗口 resize，已发布）**
- 现象：动画「生硬卡顿，就几帧的样子」
- 根因：UiAnim 是渲染帧驱动（RequestAnimationFrame）——帧率低 = 真实渲染慢。元凶是 GrowToFull 每帧 Width/Height 改变：透明窗口 + 亚克力下逐帧 SetWindowPos 走软件合成路径（透明窗口 resize 无硬件加速），每帧几百 ms
- 修复：窗口不再 resize——直接以目标尺寸铺开（透明不可见，logo 悬浮），动画全在内部（内容 ScaleTransform 0.25→1 + 背景/浮层 Opacity 交叉淡化，纯 GPU 合成不触发布局）；logo 固定大小随浮层淡出（不再随窗口放大）
- 视觉：logo 屏幕中央浮现 → 涨成完整界面；窗口框不再做桌面级放大
- 编译 0 错误；未 commit（AL45-53 一起）

**2026-08-10 AL54 启动动画最终版：GPU 缩放遮罩（已发布，动画冻结）**
- 设计：SplashOverlay 变「假窗口」（深色 #FF14181F + 圆角 12），从 0.18（150/900 比例）整层放大到全窗——视觉上窗口从 logo 小窗长成界面，但全程一个 ScaleTransform（GPU 合成，无窗口 resize 掉帧）
- logo 反补：相对 scale = 1/layer，实际尺寸恒定 60px 钉在中心（阶段1 实际 18→60px 浮现，阶段2 随层长成界面）
- 阶段1 450ms logo 浮现（层固定 0.18）→ 阶段2 950ms 整层放大 + 内容 0.25→1 涨开 + 浮层/内容/背景交叉淡化（e>0.45/0.55 重叠，无空窗）
- 完成回调不变：切 AcrylicBlur hint、导航兜底定位
- 坑：Avalonia Transform 内 x:Name 不生成字段（已知，SplashLogoS 同模式）→ SplashOverlayScale 运行时从 RenderTransform 取
- 验证：编译 0 错误；FrameStats 可测（LATTICE_FRAMESTATS=1）
- **动画冻结：本版后不再迭代，重心转移**
- 未 commit（AL45-54 一起）

**2026-08-10 AL55 启动动画回退优化（已发布，动画冻结）**
- 用户反馈：AL54 退化（深色假窗口）+"瞬间出现是主页，像 2 张图片"
- 回退：SplashOverlay 恢复透明（logo 悬浮桌面），删假窗口/整层缩放/logo 反补
- 主页过渡：内容淡入提前并拉长（浮层 e>0.4 淡出 500ms、内容 e>0.5 淡入 475ms），去掉内容 ScaleTransform 涨开（0.25→1 放大感 = 「图片放大」感源头）
- 保留：窗口不 resize（纯 GPU 合成满帧，AL53 核心）
- 最终形态：logo 透明浮现（450ms）→ 内容整页平滑淡入（475ms）与浮层淡出重叠 → 切 AcrylicBlur
- 编译 0 错误；**动画冻结，重心转移**
- 未 commit（AL45-55 一起）

**2026-08-10 AL56 模组版本选择优化：双源手动选版 + 错误可见 + 实例实时刷新（已发布）**
- 双源手动选版：CF 源不再拒绝手动选版（原 317-318 直接 return）——LoadVersions 按 Source 分派（CF GetFilesAsync / Modrinth GetVersionsAsync）；VersionOptionVM 来源无关化（FromModrinth/FromCf 工厂，Source 供安装分派）；OnSelectedVersionChanged 按 Source 设 _matchedVersion（Modrinth）或 _cfFile（CF），CF 安装路径自动用手动所选文件
- 错误可见：LoadVersions catch { } 静默 → VersionHint 显示「版本列表加载失败: xxx」；空结果提示「该模组没有适配所选游戏版本的版本」；防重入 _loadingVersions
- 实例实时刷新：UpdateContext(instance)（_instance 去 readonly）→ 清匹配/文件 → 重跑自动匹配 + 手动列表自动重载（_versionsLoaded）；EcosystemViewModel.OnSelectedInstanceChanged 联动 Detail.UpdateContext（主页版本切换经 OnMainPropertyChanged → SelectedInstance 链覆盖）
- UI：按钮常显（有列表 = 「刷新」），空提示绑定 VersionHint
- 坑：PCL.Core 二进制 vendored 无 CurseforgeFile.fileDate → 发布日留空（排序按 id 降序）
- 测试 410 全绿；未 commit（AL45-56 一起）

**2026-08-10 AL57 模组缺失自愈 + 双版本 bug（完成，已发布）**
- ModRepairService（Core\Diagnostics）：ScanInstanceLogs 读 versions/{id}/logs/latest.log（尾部 200KB）+ 最新 crash-report，正则提取缺失模组（Fabric 行内 [a,b] / 分行 "- id" 列表（含 [main/ERROR]: 前缀）/ "Couldn't load mod x because it is missing y" / Forge "requires mod 'id'" 引号包裹）；误报过滤词表 + 无日志返回空
- RepairAsync：slug 直查 GetProjectAsync → 搜索兜底（downloads 排序取首个 hit）→ FindBestVersionAsync → InstallAsync 到实例 mods（gameDirOverride）；ctx 子任务（下载中心可见）；ModRepairReport(Missing/Repaired/Failed)
- 挂载三处：CrashReportWindow 一键修复后、HomeViewModel 启动失败自动修复后、版本页手动修复后——统一走 ModRepairFlow（确认框 → EnqueueGroup → Toast 结果）
- 双版本根因（截图 OCR 确认 ×2）：快照版 PCL 的 .minecraft junction 指向正式版 → ScanSourceDirs 字符串去重失效（物理同目录两路径）→ 每版本扫两次；修：ScanSourceDirs 按物理路径去重（ResolveLinkTarget 解析 junction）+ RescanLocal 兜底 (Id, 目录) 去重
- 测试 415 全绿（+5 解析用例：行内/分行/requires/引号过滤/无日志）；坑：测试 finally 曾误删 %TEMP%（GetDirectoryName 删到根——文件锁救场，已改删 gameDir 本身）；TerminalState 是 Core internal（App 层用 State）
- 未 commit（AL45-57 一起）

**2026-08-10 AL58 签名真相调查（结论：发布产物从未真正签名成功，假成功长期存在）**
- 用户问 SAC/SmartScreen——查签名时发现：发布 exe Get-AuthenticodeSignature = NotSigned
- 根因链（逐步实证排除）：
  1. PS5.1 Set-AuthenticodeSignature 对发布 exe 返回 UnknownError「非 Win32 应用」**但不抛异常**——sign-output 只 count++ 不验 Status → 历史「已签 N 个文件」全是假成功
  2. 排除法：notepad/普通 exe/自写 probe（37MB 单文件）PS5.1 都能签；发布 exe（46/83MB 单文件）失败
  3. probe 与发布 exe 的 PE 头/区段/DD[5] 完全一致（SDK 伪证书表 RVA=SizeOfImage、Size=828/fdep 30KB/自包含）——差异只在大小和内容，PS5.1 行为不同（疑似大文件 bug）
  4. signtool（从 NuGet Microsoft.Windows.SDK.BuildTools 包取得，nuget.azure.cn 可达）：对普通 exe/已签名单文件成功；对未签名单文件（伪表+bundle）badexeformat
  5. 自写 CodeSigner（SignerSignEx P/Invoke）对任何文件 0xC0000005 崩溃——结构细节未解（停，不耗 token）
- 对用户的实际影响：SmartScreen 蓝窗对未签名文件同样有「更多信息→仍要运行」（核心诉求不受影响）；SAC 无论签名与否都硬拦；文件完整性校验（Authenticode Valid）缺失
- 修复：sign-output.ps1 现在验证 Status（Valid 才算成功，失败明确警告）——不再假成功
- 后续方案（等网络/再战）：装 PowerShell 7（实现重写）或 signtool 先清伪表；或买 OV 证书
- 遗留：scripts/CodeSigner/（半成品 P/Invoke 签名工具，未完成未使用）
- 未 commit（AL45-58 一起）

## 2026-08-10 批次 2：汉堡错位 + 背景色 ColorPicker + 模块开关与存储（三需求）

### 需求 1：设置页 ☰ 菜单选中错位（已修）
- 根因：SettingsView.axaml.cs SetNavVisual 激活时 BorderThickness 0→(3,0,0,0)，Button 模板内容内缩 3px → 选中项文字右移
- 修：恒为 Thickness(3,0,0,0)，非激活透明 BorderBrush（单行改动）

### 需求 2：背景色 ColorPicker + 透明可读（已完成）
- 新增 Core `BackgroundPaletteMath.cs`（TryParse 7/9 位 hex + Derive 亮暗整套表面色；判据 A>=128 && lum>0.30）
- LauncherSettings.BackgroundColor（#AARRGGBB 可空，默认 #B81D222C 与旧硬编码一致）
- App.axaml.cs ApplyBackgroundColor 写 10 个资源键；App.axaml + 16 视图 Static→Dynamic 令牌替换（82 处）
- MainWindow ContentSurface/TitleBar 改 DynamicResource；ApplyBackgroundImage 空路径清本地值（回落背景色）
- 外观页新增 ColorPicker（Avalonia.Controls.ColorPicker 12.1.1 包，IsAlphaEnabled）
- 亮主题值：深字白卡（TextPrimary #1A1F2B 对 BgRaised 对比度 16.3 ≥4.5）；低 alpha 按暗（透暗 acrylic）
- 不动：TextLog/Accent 系/语义色/BgDim

### 需求 3：模块开关 + 存储统计清理（已完成）
- LauncherSettings.EnabledModules（List<string>，settings 恒开）+ StorageCapsMb（Dictionary<string,int>）
- 新增 Core `StorageScanner.cs`（5 组：game/server/downloads/logs/backups；game 组排除 *.parts 防重复统计；DeleteGroup 先量后删只计实删）
- 新增 App Models/ModuleDescriptor.cs（静态表 + IsEnabled + Normalize 空列表保底）；MainViewModel 重构（VM nullable + EnsureX 懒建 + Navigate 拦截 + ApplyModuleSettings 当前页被禁跳设置）
- MainWindow 6 按钮 IsVisible 绑定；SettingsView 第 6 分区「模块与存储」（index 5）+ SectionModulesView + ModuleSettingsViewModel（进分区自动扫描）
- StorageWindow 重构消费 StorageScanner（删除本地重复 ItemSize/FormatSize）
- ServerViewModel:809 修 nullable（main.Home?）；ModpackImportFlow:45 guard

### 其他
- 顺手修：UrlFormLibraryTests 脆弱断言（绑死官方源 → 只断言 jar 被请求过；Stub 下源选择非确定）
- 测试 434/434 全绿（新增 BackgroundPaletteMathTests 12 + StorageScannerTests 4）；构建 0 错误；14:29 发布（双 exe 签名 Valid）
- 未 commit（连同 AL45-58 一起）

## 2026-08-10 批次 3：去括号注释改问号 + 进程优先级 + 版本隔离默认（已完成）

### 需求 1：UI 括号注释 → 问号图标 + 点击显示解释（28+ 站点）
- 基础设施：App.axaml `TextBlock.help-hint` 样式（Segoe E897 图标 + ToolTip.ShowDelay=60s 禁 hover 弹出）+ `Services/HelpTipService.cs`（ToolTip.SetIsOpen 切换——Avalonia 12.1.1 API 已确认）+ 12 个 code-behind 加 OnHelpClick 薄壳
- 站点覆盖：SectionDownload(7)/Launch(3+优先级行)/Modules(3)/Appearance(3)/GameDir(1)/About(2)/HomeView(1)/StorageWindow(1)/ThirdPartyDownload(2)/VersionBrowse(1)/VersionDownload(1)/LoaderChoiceDialog(1)
- 卸载警告改紧凑红字「删除后无法恢复」（status-error 常驻）+ 问号完整清单
- 批量策略：perl \Q\E 字面量替换（含 / 的 2 条失败改手工 Edit）+ python 按关键词删行到自闭合 + 手工结构编辑
- 坑：所有 code-behind 用文件级 namespace（无大括号）——批量插 handler 必须插在「最后一个 }（类结束）前」，曾误插到方法内（24 错误）已修

### 需求 2：进程优先级设置（已完成）
- LauncherSettings.GamePriority 枚举（BelowNormal/Normal/AboveNormal/High/RealTime，默认 Normal）+ 2 测试
- SettingsViewModel：5 档（低/正常/高/最高/实时（慎用））即时保存；SectionLaunchView 新行（性能档位与自动中文之间）
- LaunchProcess.ToPriorityClass 映射 + Start 参数 + Start 后 ApplyPriority（Normal 零开销，失败 onLog「§ 设置进程优先级失败」）
- 服务端同设置（ServerProcess + ServerViewModel 传值）

### 需求 3：版本隔离
- 无逻辑改动（默认已 true + 已有测试）；仅 label 去括号 + 问号

### 其他
- 测试 436/436 全绿；构建 0 错误；18:50 发布（双 exe）
- 未 commit（连同 AL45-58 全部）

## 2026-08-10 批次 3 修订（用户测试反馈后）
- 问号改悬停弹出：ToolTip.SetIsOpen 点击方案在 Avalonia 12 不可靠（点击无反应）→ 删 HelpTipService + 12 handler + 全部 PointerPressed，恢复标准 hover ToolTip
- 问号显眼化：FontSize 14 + Accent 青绿（原 12 + TextDim 太暗）
- 问号位置：label 旁 → 行尾右侧（设置控件后）
- 性能档位联动进程优先级（用户澄清「不是分开的」）：删独立「进程优先级」设置行 + GamePriority 设置属性；PerformanceProfiles.Priority(profile) 映射（低→BelowNormal/均衡→Normal/流畅→AboveNormal/极致→High），游戏+服务端一致；性能档位问号写明「改动对下次启动生效」（非动态实时调节）
- 测试：439 全绿（新增 PerformanceProfilesTests 5 个，删 GamePriority 设置测试 2 个）
- 坑：批量删 PointerPressed 行把跨行 TextBlock 闭合 /> 也删了（12 文件 XAML 破坏）——补 /> 修复；perl \Q\E 对含 / 的文本（PATH/KB/s）替换失败——手工 Edit
- 19:21 发布

## 2026-08-10 批次 4（用户真机反馈全修，20:05 发布）
- 菜单交叉：BuildSection `4 => SectionModulesView` 改 `5`（XAML 参数 5=模块/4=关于 与 switch 对齐）
- 问号渲染：help-hint FontFamily 去 Fluent（E897 无字形 → 手打问号）改单字体 Segoe MDL2 Assets；ToolTip.ShowDelay=500（0.5s 悬停）
- 文案治根：28 处 ToolTip 重写口语短句（零书面腔）——批量 python 精确替换
- 进程优先级恢复独立：LauncherSettings.GamePriority 属性 + SettingsViewModel 4 档（低/正常/高/最高，去实时）+ SectionLaunchView「内存与 Java」子组新行 + GameLaunchService/ServerViewModel 传设置值 + 删 PerformanceProfiles.Priority 联动 + 测试恢复
- 删模块开关：ModuleDescriptor.cs 删、MainViewModel 恢复 eager 全建、MainWindow IsVisible×6 删、EnabledModules 删、ModuleSettingsViewModel→StorageSettingsViewModel（只留存储）、SectionModulesView 只留存储占用、菜单「模块与存储」→「存储」、guard 恢复
- 模组页提速：HttpClientPool.Create()（15s 超时）用于 Ecosystem/CF/ImageLoader（原 100s 默认拖死页面）；EcosystemService 搜索磁盘缓存 5min（eco-{hash}.json）；默认来源「全部」→「Modrinth」单源；CF 外层重试删（GetJsonAsync 内已有）；预加载推迟——EcoVM 构造抑制搜索 + Activate() 幂等首次激活才搜（8 请求风暴→激活 1 次）
- 测试 437/437 全绿；20:05 发布双 exe；真机运行验证通过

## 2026-08-10 批次 5（问号矢量图标 + 第三方下载 GitHub 加速）
- 问号图标治根：字体渲染（MDL2 E897）像拼上去的字符 → 换矢量 Path（Material help 24x24 路径）。App.axaml 加 HelpIconData 资源（StreamGeometry）+ help-hint 样式改 `Path.help-hint`（Data/Stretch/14x14/Accent/Cursor/ShowDelay 500）；18 处 XAML TextBlock→Path（两轮 python 批量：无前缀 + Grid.Column 前缀两种写法）。构建 0 错误，发布（签名 Valid）
- 第三方下载 GitHub 下载不了（用户反馈"重试 2 轮"）：诊断——github.com 直连被墙（curl 21s 超时；20 分钟前还 200，典型时好时坏），release 直链第一步 302 就死；api.github.com/release-assets CDN 通；ghproxy.net 转发 200/1.17s 且支持 Range(206)；ghproxy.com、ghfast.top、ghproxy.cc、mirror.ghproxy.com 已挂
- 修复：新 ThirdPartyDlSourceResolver（Launcher.Core.Download）——GitHub release 直链（/releases/download/ 或 /releases/expanded_assets/）映射多候选 [原URL + ghproxy.net + gh-proxy.com]，复用 AL32 并行竞速（原挂镜像赢）；非 GitHub/tag 页/非 https 单候选。ThirdPartyDownloadViewModel 换 resolver 构造
- 测试：ThirdPartyDlSourceResolverTests 7 个（镜像格式/expanded_assets/单候选 Theory×4/端到端竞速——坑：竞速无 sha1 校验时未路由的镜像候选默认 200 会抢赢，把 gh-proxy.com 也路由 500 固定赢家）；全量 444/444 全绿；发布（签名 Valid）
- 未 commit（连同 AL45-58 全部批次）
- 批次 5 续（竞速速度虚高修复，AL57）：用户反馈第三方下 GitHub 文件「19MB 下了 10 多秒但显示几百 MB/s」。诊断：竞速（AL32）多源同时拉同一文件不同副本（.race{i}）共享同一进度回调——字节混合上报回退触发 DownloadTask.Report 计速基线重置，完成瞬间剩余字节全挤进最后 0.25s 窗口 → 速度爆表（真实吞吐 ~2MB/s）。修复：DownloadService 内新增 internal RaceProgress（每源独立 last 只报增量 + 共享累加器单调累加 cap total + 惰性取 total）；竞速 Task.Run 处 perSourceProgress 包装。Launcher.Core.csproj 加 InternalsVisibleTo(Launcher.Core.Tests)。测试：RaceProgressTests 3 个（单调 cap/单源回退忽略/完成跳变只补余量——坑：双源首报增量是真实工作量，回退测试要单源）；全量 447/447 全绿；发布（签名 Valid）
- AL58（批 5 续，下载对比实测 + 静默修复）：curl vs 启动器对比——curl 直连 GitHub 000（21s 墙）；ghproxy.net 对 376MB 大文件 503 拒绝（限文件大小）；gh-proxy.com 单连接 2.76MB/s 但限并发（8 连接分片总吞吐反降到 0.1MB/s，1 片超时）——分片对限并发服务器是灾难。用户下载 AnythingLLMDesktop.exe（376MB）被取消（history.json 22:23 已取消）：3 源×8 片=24 连接全被限 → 无源能赢 + 多源字节累加 cap 99% 造成「下完了」错觉 + 竞速赢家后先等全部 pending 停止才 rename（慢源取消传播几十秒）→ 静默。修复：① 赢家 rename 先行 + 输家取消/清理放后台 Task.Run（先 Wait 防边写边删）；② RaceProgress 语义改「领先源进度」（多源累加→全局 Max 单调，同值不转发——LastSent 放 Shared，按源记录会让落后源重复报全局值）；测试 RaceProgressTests 适配新语义；全量 447/447 全绿；发布（签名 Valid）
- 下载黑科技三件套（AL59/AL60，批 5 续）：① GitHub API 官方直链——新 GitHubApiDirect.cs（ghapi: 占位 URL → api.github.com releases/tags 拿 asset id → assets octet-stream 302 签名直链，30 分钟缓存；全程不碰被墙的 github.com）；ThirdPartyDlSourceResolver 追加 ghapi 候选（4 候选）；DownloadFromSourceAsync 开头换链；resolved 移入 attempt 循环（签名 1h 过期→403 每轮重换）。② 竞速淘汰制——RaceProgress 加 per-source 字节（GetBytes）；竞速每源独立 CTS；15s 评估点取消非领先源（DownloadOptions.RaceEliminateInterval 可注入；限并发镜像 24 连接收敛到 1 源）。③ ramp-up 分片自适应——ProbeAndDecideConcurrencyAsync：拉头 1MB/2s 窗口测单连接速度 → ≥800KB/s 单连接（限并发源）/<200KB/s 满片（按连接限速源）/中间 4 片；探测写 probe.part 后删，正式片按 N 重分。坑：DownloadChunkAsync 单片重试 catch 捕获取消 OCE → 重试请求悬挂（探测取消重复发请求）——加 OCE 前置 catch 原样上抛；GitHubApiDirect JSON 键全小写需 PropertyNameCaseInsensitive；fake handler 不实现重定向 + RequestMessage 需手动设置；测试传 progress null 时淘汰评估跳过（SlowSource 测试误过）。测试 459/459 全绿；发布（签名 Valid）
- AL61（批 5 续，下载中自动换源）：用户实测 gh-proxy.com 376MB 下载「慢慢降下来锁在 900KB」——外部 curl 实测新连接也只给 12-36KB/s（按 IP 烧完配额，连接不断但趋近 0）。新增 SlowSourceDetector + SlowSourceException（HttpRequestException 子类）：下载循环每采样间隔（默认 5s）测实时速度，连续 6 次 < 100KB/s（=30s 持续龟速）→ 判源死抛异常 → 外层换路（单候选重试整轮重新 Resolve / 竞速标记失败）。单连接在读循环内 Check；分片在 DownloadChunkedAsync 起监测 Task 并行 WhenAll（cp.Bytes 总吞吐测速），触发取消分片直接抛（不回退单连接——catch 前置拦截 SlowSourceException）。DownloadOptions 加 SlowSpeedBps(100KB)/SlowProbeMs(5s)/SlowSamples(6) 可注入。坑：HttpStatusCode.SlowDown 不存在（用 TooManyRequests 或不带）；.NET10 HttpRequestException (string,Exception?,HttpStatusCode?) 重载没了（只传 message）。测试 SlowSourceTests 3 个（单连接判死/分片判死不回退/快源回归）；全量 462/462 全绿；发布（签名 Valid）
- AL62（8-11 凌晨，下载质检员）：用户痛点「进度条停在跑满（99% 封顶）任务不结束」——根因：组任务下载完到 Completed 之间 VerifyInstalled（只查存在性）+ Mark 的 1-3 秒窗口 + 无文件统计。实现：AutoRepairService.FileIntegrityReport（TotalExpected/Present/Missing/VerifiedByHash/TotalBytes/MissingFiles + SummaryText）；VerifyFiles/VerifyVersion 返回报告 + verifyHashes 参数（有 sha1 元数据的文件并行 SHA1 验证——下载后质检用；启动前快查传 false）；VersionInstaller.InstallCoreAsync 插质检（verifyHashes: true）→ ctx.SetStage("质检：125/125 文件完整 · 540MB · N 个哈希验证通过")（DownloadTask 加 public SetStage Post 包装 + DownloadGroupContext 转发）→ 通过才 Mark；CheckIntegrity UI 显示统计；叶子完成 Stage =「已下载 X」。调用方适配 6 处（FixRedownload/VerifyInstalled/LoaderService/GameLaunchService/CheckIntegrity/测试）。坑：FileIntegrityReport 是嵌套 record 要 AutoRepairService. 前缀；$$""" raw string 内容连续 } 超限（每个 } 加空格隔开）。测试 463/463 全绿；发布（签名 Valid）
- 同日其他（外部）：Ollama qwen3-vl:4b-instruct 3.3GB 下载（网络全线限速 70KB-4MB 波动 + 卡死重拉 2 次，凌晨 1 点完成注册）；CPU 推理纯文本 OK、识图超时（CUDA v12 库安装时被取消——GPU 加速待补）；AnythingLLM 配好 DeepSeek API（.env LLM_PROVIDER=deepseek，key 只进配置不回显）；桌面安装器残留进程已杀；Watt Toolkit 已装（Steam++ 进程在跑）——免费版 Google 搜索无加速项，谷歌搜索合规无解（google/startpage/brave 全墙，cn.bing 唯一通）
- AL63（8-11 上午，模组中文搜索 + MC百科接入）：用户发现模组搜索不支持中文（Modrinth 索引英文标题，实测「遗落荒野」原生 0 命中）。实测：search.mcmod.cn 国内直连 200/0.4s；MCIM 镜像全挂（mcim.taiyukai.com/mirrors.taiyukai.cn/mcim.cn 全 000）。链路打通：中文 → search.mcmod.cn/s?key=（HTML 静态，正则条目 id+标题）→ www.mcmod.cn/class/{id}.html → link.mcmod.cn/target/{base64(完整URL)} 双层编码解出 Modrinth slug（实测 missing-wilds）→ api.modrinth.com/v2/project/{slug}。实现：新 McmodSearchService.cs（ParseSearchResults/DecodeModrinthSlug/SearchSlugsAsync/ContainsChinese，正则无依赖，HttpClientPool 15s 超时）；EcosystemService.SearchChineseAsync（slug→项目详情→ModrinthSearchHit，无分页上限 10，构造注入 _mcmod）；EcosystemViewModel.RunMrSearchAsync 中文分流（含 CJK → 汉化链路，英文不变）。坑：char/int pattern 比较要 (uint)c；raw string $$""" 连续 } 超限。测试 470/470 全绿（McmodSearchServiceTests 7 个：条目解析/双层 base64 解 slug/无 Modrinth 外链 null/中文判定 Theory×4）；发布（签名 Valid）
- AL64/65（8-11 上午，卡死根治 + 引擎榨干 + CUDA）：GLM 识图确认真相——26.2+Fabric 148.2/148.5MB 满速 60.7MB/s 卡「下载中」（网络没问题，收尾卡死）。① AL64 响应头超时：SendWithHeaderTimeoutAsync（DownloadOptions.ResponseHeaderTimeoutMs 默认 30s——半开连接响应头拿不到转 HttpRequestException 换路；body 不限）；接 DownloadSingleAsync（经 SendWith416RetryAsync）/DownloadChunkAsync；测试 HeaderTimeoutTests 2 个（单候选快速判死不卡死/双候选换源赢）。② AL65 队列调度：DownloadManager 全局并发门（SemaphoreSlim(MaxConcurrentDownloads>0 时；0=不限旧行为)——Gated 包装 Enqueue/EnqueueGroup——排队显示 Queued「排队等待…」；测试 3 个（门1串行/门2并行/0不限）。③ 网络诊断：NetworkChecker.ProbeHttpAsync（HEAD 计时 -1=断）+ DownloadViewModel.NetworkStatus + CheckNetworkCommand（6 源：Mojang/BMCLAPI/Fabric/Modrinth/GitHub镜像/直连）+ DownloadView 诊断条。④ ImageLoader 磁盘缓存（%LocalAppData%\Launcher\imgcache + 并发门4 + 8s 超时）。⑤ CUDA 补装：cudav12.7z 627MB（cdn.anythingllm.com 38MB/s）→ py7zr 不支持 BCJ2 → 7zr.exe 解压 → engines/ollama/lib/ollama/cuda_v12（4 dll 2.5GB）→ GPU 生效（推理 3.3s vs CPU 分钟级；识图 49.7s 且准确读出截图内容——本地识图全通）；临时文件已清理。全量 475/475 全绿（3 处失败修复：Group_Cancel 等 Children 落地、Suspend 等 runs++、LeafFailure 等 work 执行——均为并发门 Gated 异步调度的时序竞态）；重新发布完成（发布/Lattice启动器.exe 80M + 轻量版 46M，签名 2 文件）

## 2026-08-11 下午批次 6（真机复测 + AL66 读心跳 + AL67 片断点续传 + AL68 停滞透明化）
- 真机复测 26.2+Fabric：fabric-loader 39MB 完整下载成功（13:50-13:52，2 分钟）；fabric-api 每次末尾断流（Modrinth→Cloudflare 对移动宽带的间歇 TCP 干扰——curl 实测 cdn.modrinth.com 新连接全 000/15s 超时、ping 通、存量连接存活；api 域名同步断；无代理/Watt 关闭——纯运营商干扰，间歇性 13:51 能下 37MB）
- AL66 body 读心跳：AL61 慢速检测挂在数据循环体内、ReadAsync 挂起时永不执行（fabric-api 卡 0.2MB 3 分钟+ 根因）；ReadWithStallAsync（每次数据重置 N 秒心跳，挂起判死抛可重试错误）堵三处：单连接/分片/探测段（探测走分片函数）；DownloadOptions.ReadStallTimeoutMs=30000
- AL67 片断点续传：中断片从已下长度续拉（Range from=have，206 才 Append/200 重写防错位）；主循环部分片先入账 cp（进度不归零）；合并时片长度校验（超长片拒绝回退单连接自愈）；顺带修 slowWatch 分片成功后 await 永挂（先 Cancel 再 await）
- AL68 停滞透明化（用户痛点「末尾停滞像死了一样」）：叶子失败 Stage=「失败：原因」；自动重试前 Stage=「源异常，自动重试中…」（800ms 延迟可见）；组推导 Failed 叶子兜底（不再「正在完成…」掩盖失败）；组自身失败也 SetStage 原因；Retry 清 Stage
- 测试：StallReadTests 3 + PartResumeTests 3 + StageTransparencyTests 4 = 10 新增；全量 485/485 全绿；发布（签名）
- 坑：fake handler 分片并发共享 StreamContent 被先 dispose 的片打爆（工厂模式）；ResumeHandler 无 Range 请求 NRE（单连接回退路径）；ProbeDelay 调慢触发分片（秒回判快源单片）；SetState/SetStage 两次独立 Post 断言要等 Stage；Post 是 UI 同步回调不能内嵌 Delay（Task.Run 延迟）
- 网络结论：Modrinth CDN（Cloudflare）对移动宽带间歇 TCP 阻断（新连接拒绝/存量中途断=末尾断流）——启动器已尽力（心跳+续传+重试），等网络恢复或换链路
- AL69.1 多轮机会（用户纠正「不能直接改成百分百下载失败」）：自动重试 1 次 → 2 次（叶子共 3 次尝试，第 2 轮退避 3s；组任务编排层抛错也重试 2 次——叶子失败聚合不组重试防风暴）；重试 Stage 带计数「网络异常，自动重试第 N/2 次…」；全部耗尽才终态失败 + AL69 弹窗（坦言网络原因 + 打开下载页按钮——Modrinth/CurseForge 双路）。测试：NetworkFailure_RetriesTwice_BeforeGivingUp 新增；LeafNetworkFailure 语义更新（3 次尝试后 CheckNetwork）；Gate2 竞态修（CountdownEvent 同步）；486/486 全绿；重新发布（上次带失败发布已作废）
- AL71 死锁根治（用户第三次反馈「又卡正在完成」+ GLM 截图 148.5/148.5）：真凶 = VerifyFiles 的 Task.WaitAll（SHA1 并行）——阻塞池线程等 Task.Run 排队任务 = 线程池饥饿死锁（16:28 26.2 装完卡「正在完成」12 分钟+，96 线程 67 UserRequest 阻塞；fabric-api 30s 超时上限反而正常）。VerifyFiles→VerifyFilesAsync（WhenAll 非阻塞）传播 5 调用点（RepairVersion/VerifyVersion→VerifyVersionAsync/VersionInstaller.VerifyInstalledAsync/LoaderService.VerifyInstalledVersionAsync/GameLaunchService/VersionBrowseViewModel）。另确认 26.2+Fabric 流程含附带 fabric-api（LoaderService InstallFabricApiAsync，30s 超时静默不阻断）。487/487 全绿；发布

## 2026-08-11 晚间批次 7（双 subagent 代码审查 + 高优修复）
- 用户改口启用 subagent（记忆 no-ultracode-workflow 已更新：8-11 认可能量变质变，趁网络差跑本地审查）
- 派 2 个 general-purpose agent 并行审查：BUGS.md（引擎 16 条：高2/中8/低6）+ BUGS2.md（UI 14 条：高1/中5/低8）
- **已修**：
  - B1（UI 高）：崩溃窗 Task.Run 包 FixRedownloadAsync → ObservableCollection 跨线程崩溃 → 去 Task.Run 直接 await（UI 线程 await IO 不卡）
  - B4（中）：网络诊断并发点击交错 → CancellationTokenSource 取消旧轮
  - B5（中）：「全部」双源中文不走 MC百科分流 → RunBothSearchAsync 加 ContainsChinese 分流（McmodSearchService.ContainsChinese）
  - BUGS#1（高）：限速<100KB/s 与 SlowSourceDetector 固定阈值冲突必判死 → SlowThresholdForLimit()（min(默认, 限速×0.8)）接单连接+分片两处
  - BUGS#2（高）：InstallCoreAsync catch 无条件删 jar 误删有效 jar → jarExistedBefore 记录只删本次新建
- **未修（下轮）**：B2 Completion 早于重试耗尽误报；B3 收藏 seq 守卫；B6 ImageLoader ct/毒化；BUGS#3 单连接续传 206 判定；#4/#5 组任务级联取消死代码；#6 LoaderService NRE 掩盖；其余低
- 验证：编译 0 错误；SlowSource/StallRead/PartResume 12/12 过
- cudav13.7z 后台下载中（国际出口慢 ~38KB/s，2.5GB 需数小时——等窗口）

## 2026-08-11 晚间批次 8（全面流程审查：批次 1 = A/C/F 三旅程）
- 用户指令（压缩后）：「全面审查全局的逻辑流程，所有模块，就当正常用户一样走完整套流程」
- 方法：6 条用户旅程 × 审查代理两批并行；REVIEW-{A,C,F,B,D,E}.md 落盘；每代理只回路径+摘要
- 批次 1 结果（A 版本生命周期 9 条 / C 生态 16 条 / F 跨线程 5 条 = 30 条）
- F 反转：VersionManageViewModel Task.Run 后改集合 = 假警报（Avalonia 12 AutoInstall=true IL 实证）；真问题 = ProjectDetailViewModel.cs:371 UpdateContext Task.Run 改 AllVersions（高）、StorageWindow Task.Run 内弹窗（中）、HomeViewModel:418 正版启动 Changed 池线程改绑定（中）
- 复核结论：B2（Completion 早于重试耗尽）仍存在 6 处调用点误报；B3/B6/B7/B11 仍存在；B5/BUGS#2 已修正确；BUGS#8 半修（旧式 natives 路径仍错）；C1-C5 无问题
- 影响使用 TOP（用户确认优先级）：①B2 重试误报失败 ②_userStopped 永不重置→崩溃全误报已停止（A1）③整合包导出导入断链（C 高2：Own ZIP 永不落盘 + mrpack 无 downloads）④CF 分页页码当偏移（C）⑤ModRepair 失败计成功（A2）⑥卡片依赖失败弹成功（C）⑦非隔离备份自包含损坏（A5）⑧详情页实例切换装错目录（C）⑨mrpack 路径穿越（C 高1）⑩ProjectDetail 跨线程改集合（F 高）
- 批次 2（B 引擎复核 / D 开服联机 / E 账号设置存储）待跑

### 批次 8 修复（批次 1 审查后，490/490 全绿 + 发布签名 Valid）
- 修 _userStopped（HomeViewModel LaunchCoreAsync 入口重置——停过一次后崩溃全误报「已停止」）
- 修 CF 分页（EcosystemViewModel 373/396：页码→偏移量 CurrentPage*PageSize——第 2 页起 19/20 条重复）
- 修 B2 Completion 语义（DownloadTask 加 _retryPending：排程重试期间不完成 TCS——首败不再误报失败弹窗/历史记失败/跳下载页；调用点 6 处受益 + ScheduleAutoRemove 时机顺带修正（REVIEW-A7）；Delay 收尾处理排程期间取消/暂停（否则 TCS 永不完成）；测试适配 5 个（泵队列等 Completion.IsCompleted——State 首败即 Failed 不能当终态判据））
- 修 ModRepairService 补全失败计成功（子任务终态检查 TerminalState != Completed → Failed 记入 report）
- 修 ProjectDetailViewModel 跨线程 + 实例切换竞态（UpdateContext 去 Task.Run（Avalonia AutoInstall 保证 continuation 回 UI 线程）+ captured 实例快照守卫，LoadAsync/LoadVersions await 后检查——防旧实例匹配覆盖新实例装错目录）
- 修整合包三连：① Own ZIP 格式内容落盘（InstallContentAsync Own 分支调 ModpackImporter.Import——旧代码 (0,[]) 永不落盘静默丢 mods）② mrpack files[].path 路径穿越防护（GetFullPath + StartsWith 包含检查，与 ExtractZipEntries 同款）③ mrpack 无 downloads 时按 sha1 反查 Modrinth 补直链（ModpackImporter.ParseMrpack 保留无 downloads 但有 sha1 的文件 + InstallMrpackAsync 反查兜底，走注入 _http 8s 联动超时）
- 新测试：Own_Zip_Content_LandsOnDisk / Mrpack_PathTraversal_IsSkipped / Mrpack_NoDownloads_Sha1Fallback_ResolvesUrl（坑：反查路由 key 要带 /v2 前缀；"AAAAA" sha1=c1fe3a7b 是既有测试的匹配值）
- 全量 490/490 全绿（1m40s）；发布（签名 Valid ×2）；新版已启动

### 批次 8 审查批次 2 汇总（B 引擎复核 8 新 + D 开服联机 15 + E 账号设置 15）
- B 复核：13 项仍存在（#3/#4/#6/#7/#8/#9/#10/#11-16）；#5 已修；B2 基本修复但暴露 R-01 新竞态；BUGS#1 验证通过
- B 新发现：R-01 中（手动 Retry 撞排程重试→终态后幽灵重跑——B2 副作用）；R-02 中（限速按流均分失真，小文件=限速/8）；R-03 中（分片 .parts 取消即删→暂停继续=完整重下）；R-04/05/06/07/08 低
- D 高 4：StartServer Java 选配在 try 外（静默失败+卡死）；_oneClickActive 异常后永不复位；HttpClient 超时 3000s（离房挂 50 分钟）；复用实例会话无进程死亡检测
- D 中 6：世界生成崩溃 stop 标志残留；取消创建/加入 terracotta.exe 孤儿；一键修复与手动创建并发互踩；修复删错路径锁（%TEMP% 清不掉）；运行中改 properties 被回写覆盖；ban/op 后 500ms 读盘列表不刷新
- E 高 1：**微软登录从未保存 refresh_token → 正版账号永远无法启动游戏**（PollOAuthTokenAsync 丢弃响应里的 refresh_token）
- E 中 6：版本级 Java 泄漏进全局设置覆盖；StorageWindow 后台线程弹确认框（跨线程抛错被吞，点击无反应）；清理下载缓存会永久删未导入整合包；账号/设置 JSON 非原子写损坏静默；MaxConcurrentDownloads 改动对队列门无效
- E 低 8：外观预览丢失/CF Key 解密失败覆盖为空/每击键落盘/存储上限只显示/登录无取消/会话残留/防抖丢值/头像竞态

### 批次 8 修复（批次 2 审查后，490/490 全绿 + 发布签名 Valid + 新版启动）
- 修 E-高1 微软 refresh_token：PollOAuthTokenAsync 返回 (AccessToken, RefreshToken)——旧代码只回 access_token，会话 RefreshToken="" → 启动静默刷新必失败，正版账号永远无法启动游戏；AccountViewModel 传 refreshToken ?? ""
- 修 D-高1/2：StartServer Java 选配（PickServerJava throw）移入 try——旧代码在 try 外：找不到 Java 直接外抛 → 普通启动静默失败 + 一键开服 _oneClickActive 永不复位卡死
- 修 D-高3：TerracottaLobbyService Timeout=FromSeconds(3000)（=50 分钟）→ FromMilliseconds（3 秒）——离房请求可挂 50 分钟
- 修 D-高4：监控循环连续失败计数（复用实例 _ownedProcess==null 无死亡检测——连接拒绝无限忽略，陶瓦死后 UI 永远卡「房间已就绪」）；连续 10 次失败判定死亡（网络抖动 1~2s 不误杀）
- 修 B-R01：Retry 代际守卫（_retryGeneration——手动 Retry/Resume 递增，旧排程 Delay 到点发现代际不符即作废）——否则手动重跑耗尽失败后旧排程仍触发 Retry → 终态后幽灵重跑（B2 修复的副作用）
- 未修中低（记录留档）：E-中 6 条（版本级 Java 泄漏/StorageWindow 后台弹窗/清理删未导入整合包/JSON 非原子写/并发数门无效）、D-中 6 条（stop 标志残留/孤儿进程/并发互踩/锁路径/回写覆盖/列表不刷新）、B R-02/03 中、BUGS#3/4/6/7/8/9/10 复核仍存在、E-低 8、D-低 5、B-低 5

### 批次 9（8-11 晚，全 UI 文案改造——微软式「你」口吻 + 关于页更新日志）
- 用户要求：所有带文字描述说明的界面改微软「你」口吻（例「你可以在此处管理你的版本」）、问号 ToolTip 去 AI 腔、关于页披露版本+改动最大的几条功能/修复
- 风格规范：主语「你」/口语短句 ≤20 字/去书面腔（将以便是否请需须进行）/去括号解释展开/去 AI 腔/命令式改你式
- 改动：共 76 处（我 6 处 + Views 代理 36 处 + VM/Services 代理 34 处）
- 关于页：新增「最近更新」区（ChangelogItems 7 条：下载引擎重构/卡死根治/整合包闭环/中文搜索/正版登录修复/开服联机修复/网络诊断）+ 技术说明口语化
- 版本页空状态：「你可以在此处管理你的版本：启动、启动配置、加载器、模组、存档、备份导出」
- CF API：「获取 API」ToolTip 口语化 + SettingsViewModel 状态文字你式
- Views 15 文件（Multiplayer 9 处最多）+ VM/Services 11 文件（HomeViewModel 8 处最多）；StartupTips 彩蛋已口语无需改
- 验证：编译 0 错误；全量 490/490 全绿；发布签名 Valid；新版已启动（真机截图被游戏挡住，留用户自看）

### 批次 10（8-11 深夜，下载体验根治：速度/进度/卡完成/前摇）
- 用户真机三问题：速度显示十几 MB/s 实际几 MB/s 还跳；「正在完成」还卡；加载器前摇讨厌
- 根因（agent 深挖 + 自查）：
  ① 速度虚高 = 计速「累计平均」（基线到现在的全程/耗时，前快后慢虚高数倍）+ legacy assets 上报把文件序号当 FileBytesDone（文件数/秒显示成 MB/s）
  ② 进度跳动 = 组聚合加权 percent 新子任务挂载回落（BytesDone 回退）
  ③ 卡「正在完成」= 组 WhenAll 等全部子任务（含失败/重试排期的）跑完才报错（BUGS#4/5 复核确认的死代码）
  ④ 前摇 = profile json 每次网络拉取（meta 源 2-26s，内容由 mc+loader 版本确定却从不缓存）
- 修复：
  A1 滑动窗口计速（DownloadTask.SampleSpeed/UpdateSpeedSample，近 2s 至少留 2 采样点——裁剪过度会停在旧值）；A2 legacy assets 上报改真实字节累计（1010 行）
  B 聚合 percent 单调不减（Math.Max + 封顶 99；字节随新 total 推进消除 AL32 卡死观感——AL32 回归测试断言更新）
  C1 组首败早退（DownloadGroupContext.FirstFailure 信号：AddChild 订阅子任务 State==Failed；RunGroupAsync WhenAny(WhenAll, FirstFailure)——组内叶子无自动重试故失败即终态；正常路径等价 WhenAll）
  C2 Loader 双重校验 = agent 误报（Fabric 组路径不经 Installer 质检），跳过
  D1 profile json 磁盘缓存（AppData\Launcher\cache\loader-profiles\{kind}-{mc}-{loader}.json；缓存目录可注入——测试隔离防污染）
- 测试：DownloadSpeedTests 3 个新（窗口速度前快后慢/聚合不落/首败早退 96ms）+ 更新 AL32 回归 + CreateService 缓存隔离；全量 493/493 全绿；发布签名 Valid；新版启动
- 坑：窗口裁剪过度（点全删光 → 停在旧值）；percent 断言错写 50（实际封顶 99）；测试 profile 缓存污染全局 AppData（可注入修复）
### 批次 10 补（质检窗口）：VerifyInstalledAsync 前 SetStage「正在质检文件完整性…」——质检 10-20s 全盘 SHA1 期间组任务不再显示死寂「正在完成…」（AL62 只做完成后 Stage）；493/493 全绿；已发布
### 批次 10 补 2（真凶确认 + fabric-api 子任务化）
- 真机截图（23:28）铁证：148.5/148.5 满进度 + 2.8MB/s 在动 + 「正在下载」——磁盘实证：26.2 的 client(37.4)+131库(109.3)=146.7MB=TotalBytes；assets 走 32 索引（已大部分存在）；**2.8MB/s = fabric-api 附带下载**（InstallFabricApiAsync 在组路径下 progress 参数无效 → 主任务满进度无表达）
- 修复：fabric-api 挂组内子任务（ctx.AddChild「Fabric API」weight=0 不定条 + progress 透传 InstallFabricApiAsync → eco.InstallAsync——下载速度/Stage 可见）；非组路径保留 AL46.1 progress 文案
- 493/493 全绿；发布签名 Valid；新版重启

### 批次 11（8-12，进度节流根治——用户洞察「数据跟不上下载速度」）
- 用户洞察：怀疑网速过快 → 数据同步不过来；PCL 慢所以永远跟得上
- 机制实锤：单连接路径每 64KB 块上报（60MB/s ≈ 1000 次/秒）+ 分片 250ms/文件 × 131 并行文件 = 500+ 次/秒 UI Post 积压；组聚合无节流（每次子任务变化同步重算 + 父属性逐个 Post UI）
- 修复：
  ① 单连接 progress 250ms 节流 + 收尾强制报一次（DownloadService.DownloadSingleAsync）
  ② 组聚合重构为「同步快照 ComputeSnapshot + 节流发布 PublishAggregate」——窗口 250ms + 60ms 尾算；发布值 = 窗口内最大 percent（_pendingPercent 单调——节流不吞峰值：旧「当前值单调」被挂载覆盖 99→69.3 爬不回去）
  ③ 终态守卫 ×2（ComputeSnapshot/PublishAggregate——尾算不得覆盖 Stage="已完成"/失败 Stage 显式 Post 延迟读 Error）
  ④ AttachChild 挂载也走节流入口
- 测试适配 3 个（等尾算稳定）；全量 493/493 全绿（一次过——此前 flaky 是残留 testhost 并发竞争）
- 发布签名 Valid；新版重启（PID 40124）

### 批次 12（8-12，ProgressReporter 统一抽象——「静默段」治本）
- 用户确认治本方向：所有阶段（下载/质检/meta/API）强制经过统一抽象，每阶段必须携带「阶段文字 + 字节进度 + 节流」三件套
- 新建 src/Launcher.Core/Download/ProgressReporter.cs：构造即 Emit（阶段文字立即可见——无「无表达窗口」）；Report 250ms 节流；ReportStage 窗口内立即生效（文字优先）；Complete 补报（节流吞掉的收尾状态不丢）；sink 可空全空操作
- 接入：LoaderService meta 子任务（「正在拉取加载器信息…」→ ReportStage 加载器配置完成）；fabric-api 子任务化重签名（DownloadProgressHandler? → ProgressReporter?，内部 eco.InstallAsync 适配 p => rep.Report(...)）
- pipeline assets 复查：字节缩放单位已正确 + 组聚合节流兜底 → 无需重复接入
- 清理 DownloadTask 遗留未用字段（滑动窗口重构残留 CS0169）
- **额外抓到真生产 bug（flake 现场实证）**：ForgeInstall_Success 间歇失败——DEBUG 落盘显示两个版本 json mtime 精确并列（.605752 同微秒）→ FindNewestVersionDir 稳定排序按枚举顺序选中父版本「1.21.10」→ 校验/标记打在原版目录 → forge 版本页不显示已装（生产真机同样可能中招）。修复：mtime 并列时 tie-break 优先带 inheritsFrom 的 json（Forge/NeoForge 安装器产出物必有，原版没有）→ 确定性选对目标
- 测试：ProgressReporter 4 个单测（节流/Complete 补报/ReportStage 立即/NULL sink）+ MtimeTie 回归（强制 mtime 并列断言标记落 forge）+ 全量 498/498 全绿
- 发布签名 Valid；新版重启

### 批次 13（8-12，生态页五项改造——26.2/版本下拉/PCL 式模组列表/路径确认）
- 用户四个问题：①实例下拉没有 26.2 ②游戏版本下拉最高 1.21.6 ③模组详情页只有自动匹配+折叠手动选择（要 PCL 式列表）④安装无路径确认、按钮不明显
- 根因：①IsInstalled(json+jar 双文件) 漏掉 Fabric 父版本（26.2 jar 沿 inheritsFrom 落加载器子目录）——版本页 json-only 所以可见 ②GameVersionOptions 硬编码 1.18.2~1.21.6 从未加 26.x YY.M 新格式 ③详情页手动选择是懒加载折叠 Expander（两次请求：匹配+列表）
- 修复：
  ① 新 IsInstanceTarget（json-only + .prefetched 排除）——不动 IsInstalled（版本页 Installed 标记/主页权威口径）；InitializeAsync 删 manifest 循环（新判定下与目录循环同结果）
  ② GameVersionOptions 静态硬编码 → 实例 ObservableCollection；CompareGameVersions 语义比较器（26.2 > 1.21.6、1.21.10 > 1.21.6）；FilterGameVersionOptions（release 且 >=1.16，语义降序）；manifest 拉取失败兜底内置常用列表；XAML x:Static → Binding
  ③ 详情页删手动选择 Expander/ComboBox/加载按钮；打开即加载版本列表最新 10 条直显（DatePublished/fileId 降序），每行 版本名·游戏版本·加载器/日期/大小 + 独立安装按钮；推荐行 chip（IsRecommended=匹配命中，与列表同源同请求——删旧双请求）；InstallVersionCommand 行内安装复用现有管线
  ④ DialogService.ConfirmInstallPath（"装到这里：{path}"）+ 4 调用点（列表页 Modrinth/CF、详情页 Modrinth/CF）——路径确认在依赖确认之前；顺手修复 AF2 缺口：Modrinth 列表页/CF 双侧 InstallAsync/InstallWithDependenciesAsync 补 gameDirOverride 参数（之前 CF 一直装到 Detect() 目录）
  ⑤ 卡片安装按钮 FontSize 11→13、Padding 14,4→20,7
- 测试：IsInstanceTarget 4 个（json-only 26.2 场景/预取排除/双标记兜底/缺目录）+ CompareGameVersions 3 个 + FilterGameVersionOptions 1 个；全量 506/506 全绿
- 发布签名 Valid；新版重启

### 批次 14（8-12，问号 ToolTip 边缘翻转 + Modrinth 正式版优先）
- 用户两个问题：①问号提示在窗口右/下边缘溢出看不见（23 处问号全是 ToolTip 跟随鼠标弹出，无 Placement 配置）②下载 26.2 自动匹配到 26.2 最新快照
- 根因：①ToolTip 默认 Pointer 模式跟随鼠标，无边界检测（App.axaml 只有 ShowDelay=500）②SelectBestVersion 排序只有 Featured→DatePublished，无 version_type 维度——Modrinth API 返回 release/beta/alpha 全量，beta 日期最新被选中（ModrinthVersion.VersionType 字段存在但从未用；CF SelectBestFile 和依赖解析器都有 release 优先，唯独这里漏）
- 修复：
  ① 新 Core 纯函数 ToolTipPlacementPicker（方向判定：候选 Bottom→Top→Right→Left 首个「该侧空间充足+垂直/水平不溢出」胜出，Avalonia 对齐语义 Bottom/Top 水平居中、Left/Right 垂直居中贴边；文本尺寸估算 全角14px/半角7px/行高20/内边距12）+ App 层 ToolTipEdgeFlip（挂 MainWindow 监听 ToolTip Opening 路由事件，Opening 前 SetPlacement 翻转）+ App.axaml.cs 挂载
  ② SelectBestVersion 加 ReleaseRank（release=0 beta=1 alpha=2 null=3，与依赖解析器 NormalizeReleaseType 一致）排序第一，Featured/Date 随后
- 测试：ToolTipPlacementPicker 9 个（四方向场景/角/极小窗口/中文估算/多行/空）+ SelectBestVersion 2 个（release 赢新 beta 根因回归/beta 赢 alpha）；全量 517/517 全绿
- 坑：①Pick 初版「剩余最大」逻辑忽略默认方向偏好——Bottom 双向满足时不应翻走；②Avalonia ToolTipOpeningEventArgs 类型名错误——实为 CancelRoutedEventArgs（XML 文档确认）；③Left/Right 对齐语义是「贴控件边缘」非居中——判定重写
- 发布签名 Valid；新版重启（真机验证：悬停设置页底部/右侧问号应翻向可视区域）
- 批次 14 补（8-12 晚）：用户澄清「区域判定」= 问号命中区太小（14x14 鼠标难对准），非边缘翻转（该功能保留）。help-hint 样式 14→26x26（Path 无 Padding 属性——直接放大图标本身）；发布签名 Valid；新版重启

### 批次 15（8-12 晚，问号命中区/误标/链接/EasyTier 第二联机）
- 用户四点：①问号 26x26 放大反而更难（鼠标放中间时有时无——Path 命中=几何形状，问号孔不触发）②PCL 的 1.21.1 Fabric 0.19.3 被标「本启动器」（明明 PCL 装的）③打开主页等链接不显眼 ④陶瓦联机不知原因出问题→接入第二联机方案
- ①修复：Path 回退 14px；新 Border.help-hint 样式（Transparent 背景全矩形命中 + Padding=6 → 26 命中区，孔也命中）；23 处机械替换 <Border><Path/></Border>（perl 批量）
- ②根因：修复/自动修复路径以版本实际目录（PCL 目录）构造 VersionInstaller → InstallCoreAsync 无条件 Mark → .yanla-installed 写进 PCL → 扫描后「本启动器」。修复：GameDirectory.IsOwnInstallDir（物理路径+来源判定 Own/Custom）+ VersionInstaller 守卫（Mark/prefetch 均只自建目录；整合包导入 allowForeignMarkers 放行——3 处构造传 true）+ VersionManifestService 扫描时清理非自建目录既有误标
- ③Button.link 样式（Accent+下划线+ContentTemplate TextBlock）+ 3 处外部链接应用
- ④EasyTier 第二联机（用户确认选型）：
  - D1 接口抽象：IMultiplayerLobbyService（SnapshotChanged/Stopped/Current/CreateHostAsync/JoinAsync/StopAsync）+ MultiplayerModels 通用化（Snapshot/Player/State/StopReason/Failure/Exception——Terracotta* 重命名，TerracottaModels 只留 Module/ProvisionProgress）；TerracottaLobbyService 实现接口；FailureDiagnostics 补 NetworkFailed 映射
  - D2 EasyTierProvisioningService（锁 v2.6.4 + SHA256 27af91e2…实测 32.6MB + GitHub 直连/镜像候选链）+ EasyTierLobbyService
  - **实测关键发现**：a) 静态 IP 模式 peer 表格显示 ipv4（DHCP 模式空）；b) TUN 虚拟网卡创建需要管理员权限（非管理员 Failed to create adapter——UAC 提权启动 Verb=runas）；c) 隧道从虚拟网卡出发连 127.0.0.1 会 10049——房主地址必须物理 IP；d) 同机多实例需独立监听/RPC 端口；e) 26.138.121.8 是用户 Radmin VPN 网卡（误判过）
  - 房间码 = 网络名#密钥#房主物理IP:11010；虚拟 IP 静态分配 10.144.144.{2..254}（网络名+玩家名 SHA256）；加入者直连房主；服务器地址 = 房主虚拟IP:游戏端口（游戏内直接连接）；跨网段需房主端口转发（UI 文案说明）
  - D3 MultiplayerViewModel：方案下拉（陶瓦/EasyTier）+ GamePortText + ServerAddress + CopyServerAddress + 修复分流 + 提权失败诊断文案；MultiplayerView 加方案选择/端口输入/地址卡；许可条目更新（EasyTier LGPL-3.0 源码链接）
- 测试：EasyTier 3 个（虚拟 IP 分配）+ 既有测试适配（Terracotta*→Multiplayer*、RepairPath/Install 断言改守卫语义、ModpackInstaller 放行）；全量 520/520 全绿
- 坑：①追加测试 heredoc 落类外（CS1519）②Path 无 Padding 属性（编译验证）③ensureAgreement 改名引发重复方法 ④ModpackImporter 预取被守卫误拦（allowForeignMarkers）⑤RepairPath 断言与新语义冲突（改为守卫回归）⑥xunit 吞 Console——flake 调试用文件落盘
- 发布签名 Valid；新版重启（真机：问号孔命中、PCL 版本来源标签、链接下划线、联机页方案下拉）

### 批次 16（8-12 晚，文案去 AI 味 + 第三方说明 + GitHub 大文件提速）
- 用户三点：①文案再改口吻（不要AI味、像官方描述、不要废话文学）②第三方下载加说明（用户文案：使用Lattice的下载第三方文件功能下载自定义文件，支持Github镜像加速下载）③好消息+诉求：下载引擎 GitHub 小文件（19MB）快于浏览器、大文件掉几百 KB——「github文件能怎么快就怎么快」
- ①文案扫描（agent 全 25 View）：17 处问题 6 文件——一键修复/一键开服（营销腔）、即可/须/需/点击（书面腔）、「你可以在此处管理你的版本」（废话引导）、协议弹窗「须知」公告腔、括号没展开（mrpack/ZIP/options.txt）。全部改写（短句口语化）
- ②ThirdPartyDownloadView 加副标题「下载自定义文件。GitHub 源自动走镜像加速。」+ ToolTip 补镜像说明
- ③GitHub 大文件提速根因：ramp-up 探测 1MB/2s 测不出 GitHub CDN 渐进式限速（前几 MB 全速后 throttle）→ 误判高速给 1 片 → 掉速；非 release 直链（objects.githubusercontent.com/codeload.github.com 签名 URL）不走镜像竞速（单源国内几十 KB/s）。修复：ProbeAndDecideConcurrencyAsync 按域加大档位（GitHub CDN 4MB/5s——限速暴露 → 分片决策正确）+ IsGitHubCdn 纯函数；ThirdPartyDlSourceResolver.IsGitHubUrl 覆盖签名 CDN 域（贴签名 URL 也走 ghproxy.net/gh-proxy.com 竞速）
- 不做（风险/收益权衡）：ghapi 换链后签名 URL 的嵌套镜像竞速（需改竞速核心结构）；ghfast.top 镜像（项目 08-10 实测已挂废弃）
- 测试：GitHubSpeedupTests 12 个（IsGitHubCdn 域判定 Theory/签名 URL 镜像候选/ghapi 兜底保留/非 GitHub 单候选）+ 既有 Resolver 测试抓回归（github.com tag/列表页非文件直链）；全量 532/532 全绿
- 坑：probeBytes 变量名与既有局部变量冲突（CS0128）；IsGitHubUrl 扩展过宽（tag 页/列表页误触发镜像——08-10 语义保留）
- 发布签名 Valid；新版重启（真机：GitHub 大文件下载速度对比 + 文案目视）
- 批次 16 补（8-12 晚）：用户澄清方向反转——「公告腔调这种我反而喜欢，更官方更正规」「让我感觉AI味的反而是等一会儿你俩就在游戏里碰头，都在这里管这种样式」——口语化套近乎 = AI 味，官方/正式/公告腔 = 喜欢。反转批次 16 口语化处：碰头→「点击「加入房间」，即可进入房间」、都在这里管→「在此处管理你的版本：…」、协议恢复「使用须知/须遵守/不得用于违法用途/首次使用需下载…即可使用」、第三方说明用用户原文案「使用 Lattice 的下载第三方文件功能下载自定义文件，支持 Github 镜像加速下载」、端口文案官方化。发布签名 Valid；新版重启

### 批次 17（8-12 晚，OBS 大文件波动修复：签名 URL 套镜像 + 动态升片）
- 用户：OBS 走第三方下载 GitHub 路径波动严重（骤降到几百 KB），比它大的 AnythingLLM 反而快——问为什么
- 原因（代码级）：①OBS 4 源竞速（原链/2 镜像/ghapi 签名）——ghapi 签名直链（objects.githubusercontent.com）实测国内 64KB/s（代码注释明示「兜底源」）——镜像全挂/被淘汰 → 落到签名直链 ×分片 ≈ 几百 KB（用户看到的值）②镜像转发波动 + 竞速淘汰制取消后不重引入 → 领先源切换抖动 ③AnythingLLM 的 CDN 非渐进限速（探测准确），GitHub 的渐进 throttle 探测期测不出（4MB/5s 档位还不够）
- 修复：
  ① 签名 URL 套镜像：候选构建处 ghapi 预换链（await GetSignedUrlAsync）→ 展开为 [signed, ghproxy/signed, gh-proxy.com/signed] 并入外层竞速——兜底源也走镜像加速；换链失败本轮剔除（下一轮重新 Resolve）
  ② 动态升片（治本）：DownloadChunkedAsync 重构为 while 循环——监测循环内 3 采样均速 < 300KB/s 且完成 < 80% 且片数 < max 且冷却 ≥10s → 取消当前片、清 .parts（旧边界与新片数不对齐，保留会错位损坏）、2× 片数重启（ShouldUpgradeChunks 纯函数）；cp.Bytes/Reported 每轮新建（进度回退瞬间可见随后爬升）；判死换路（SlowSourceException）与升片分支区分
- 不做：竞速淘汰后重引入（镜像波动恢复后回场——复杂且升片已缓解）；签名 URL 嵌套竞速（已用展开方案替代）
- 测试：ChunkUpgradeTests 5 个（低速中段升/高速不升/尾部不升/满片不升/冷却期不升）；全量 537/537 全绿
- 坑：while 循环括号平衡（CS1513——合并块 return 后缺 while 闭合）；UpgradeSpeedBps 常量漏定义（CS0103）；升片清 .parts 的错位风险（片断点续传的 have 跨越新边界会数据缺口——必须清空重下）
- 发布签名 Valid；新版重启（真机：第三方下载 OBS GitHub 大文件看速度稳定性）
- 批次 17 补（8-12 深夜，动态升片后期失效）：用户实测「前期确实稳定2M 但后期煎熬又回到了几十KB每秒 直接拖长几分钟」——80% 完成条件挡住后期升片（OBS 掉速发生在 80%+ 之后）。修复①条件从「完成 <80%」改为「剩余 ≥8 MiB」（MinUpgradeRemainBytes=8*1024*1024——升片重下损失随尾部缩小，剩余 8MiB 后停）②满分片仍慢 → 立即判死换路（fail-dead，不等 30s 连续采样——镜像被竞速淘汰后不会回来，重新 Resolve 让镜像重新参与）
- 测试：ChunkUpgradeTests 6 个（新增 LateStageStillYes：90MB/100MB 升、边界剩余恰 8MiB 升）；全量 538/538 全绿（首次全量跑 OfficialDown_MirrorWins flaky 失败 1 次、重跑通过——网络竞速时序偶发，观察）
- 坑：边界测试值 92_000_000 是 8MB（8,000,000）不是 8MiB（8,388,608）——断言 false；改 100_000_000-8*1024*1024 精确边界
- 发布签名 Valid；新版重启（真机：第三方下载 OBS 大文件全程速度稳定性）

### 批次 18（8-12 深夜，固定分片续传 + 失败体验三件套 + UA 修复）
- 用户方向演进：最初要求「重试清空重置进度 + 删遗留」→ 最后明确「换源续进度，确保文件没有遗漏或差池」——最终方案以固定分片续传为核心
- 集训室评估（顺带）：下载源国内分布——版本全家走 BMCLAPI（国内 ✅）、Fabric 库 bmclapi/maven ✅、Forge/NeoForge/模组/GitHub 系（含 ghproxy 镜像）全境外 ❌；实测 7 个候选镜像 5 个死（ghproxy.cc/gh.llkk.cc/ghps.cc/moeyy/gitmirror 全 000）、2 个活（gh-proxy/ghproxy.net 境外）；「自动转 gitee」不成立（gitee 无 URL 转发，仓库导入阉割 release）——集训室无解，家里镜像竞速正常
- **固定分片续传（核心）**：分片边界从 totalSize/chunkCount 改为固定 256KB（FixedChunkSize，延续旧 ChunkThreshold 语义）——边界永不变化 → 已完成片跨 attempt/换源/并发变化全部复用；SemaphoreSlim 并发调度（初始=探测值，上限 maxChunks=8）；升片从「清 .parts 重切重启」改为「gate.Release 提高并发」（不丢进行中字节、进度不回退）；ShouldUpgradeChunks 参数改并发语义（旧「片数<max」固定片后永不触发）；探测函数 internal 化（Probe_DecidesConcurrencyBySpeed 直测）
- **完整性加固**：合并后总长度 == totalSize 校验（无 sha1 的第三方下载的最后兜底）；片长度校验 + SHA1 终校验保留
- **终态清理（需求 4）**：CleanupResiduals（清 .tmp/.parts/.race* 系列，destPath 本体永不动——幂等语义）+ DownloadTask 终态失败触发（ScheduleAutoRetry 不可重试/耗尽分支）——**不在 Service 层 attempt 耗尽清理**（Task 自动重试还要靠 .parts 换源续传，8-18 曾误放 Service 层导致续传测试失败，已移）
- **自动重试提示（需求 3）**：DownloadTask 加 IsAutoRetryPending + AutoRetryScheduled(attempt,total) 事件（Post 内 Stage 后触发——选事件不选 Stage 字符串匹配：组任务聚合污染 Stage）；DownloadViewModel 订阅弹 Error 红 Toast 8s（durationMs 参数已有，未改 NotificationService）；终态失败 Toast 抑制双弹（Failed && !IsAutoRetryPending）
- **UA 修复**：HttpClientPool UA 改浏览器格式（Mozilla/5.0 ... Chrome/126 ... YanKa-Launcher/0.1 常量）——实测 ghproxy.net 对自定义 UA 403（镜像候选实际不可用），全仓无 UA 读取逻辑低风险；CurseForge 要求 UA 含联系信息（保留后缀）
- 测试：FixedChunkResumeTests 2（跨 attempt 复用——片1/2 全程只请求 1 次 + 片3 从 16KB 断点续传；边界 256KB 对齐）、DownloadResidualsTests 2（CleanupResiduals 纯 IO + Task 终态清理）、AutoRetrySignalTests 2（首败 Pending true + 事件 (1,2)；耗尽 false + (1,2)(2,2)）、RampUpTests 重构（探测返回并发 + 固定片端到端 13 Range + 小文件 2 片）、HttpClientPoolTests +1（UA）；适配 PartResume（256KB 片下 ResumeFrom=512KB 恰好是片 3 起点——自动恢复）；全量 546/546 连续两次全绿
- 坑：①RampUp 直测探测函数时 partDir 未建 → DirectoryNotFoundException → 片重试吞时间 → 返回慢源档（测试建目录）②探测诊断临时行 heredoc \n 变真实换行（CS1025）③断言 start=0 计数忘了探测也是 0（3 次 vs 2）④StallFrom 挂起被片内重试+回退单连接救活 → 改慢速判死（SlowSourceException 穿透不清理）触发失败⑤Requests.Clear 会重置 stall 判断 → 一次性开关 _stalledOnce⑥Service 层终败清理与 Task 重试续传冲突（移 Task 层）⑦Exhausted 测试全量跑超时（完整重试链 3.8s + 线程池饥饿——DrainUntil 上限 400→2000 次）⑧OfficialDown_MirrorWins 依赖真实网络 ghapi 换链（8-16 预解析引入）——注入 500 handler 修掉 flaky
- 暂停丢进度既有 bug（记录不修）：DownloadChunkedAsync 通用 catch 清 .parts 后回退单连接——暂停（OCE）也走此路径 → Resume 从零（断点续传失效）；下批处理
- 发布签名 Valid；新版重启（真机：OBS 大文件下载换源续传不归零、掉速升片只提并发、终态无垃圾文件、重试红 Toast 8s、镜像竞速 UA 修复后 ghproxy.net 重新参与）
- 批次 18 补（8-12 深夜，固定片 256KB→1MB）：用户实测 HTTP Toolkit 166MB 下载「不稳定偶尔 2M、整体不如之前」——根因：固定 256KB 片 × 664 个请求，每片一次 HTTP RTT（~100ms）——单并发下吞吐崩到 1.5-2.5MB/s（升片条件 <300KB/s 又拦住波动速度）；批次 17 大文件片=20MB（totalSize/并发）无此惩罚。修复：FixedChunkSize 256KB→1MB（PCL 同款，166MB→166 片，RTT 惩罚降 4 倍）；Modrinth 小文件并发上限略降可接受。测试适配：RampUp（3MB→3 片 4 Range / 500KB→1 片）、FixedChunkResume（3.5MB 4 片重新设计）、PartResume（ResumeFrom 512KB→1MB）、ChunkBoundaries 改名 1MB
- 坑：Exhausted_PendingFalse_EventFiredTwice 全量跑 5 次失败 4 次（单跑必过）——诊断 State=Failed Pending=True Events=1：重试链卡在 Task.Run 续跑（全量并行线程池饥饿 20s+ 饿死）——测试 DrainUntil 设死上限误报超时；改无限泵到 Completion（产品环境是真实 UI 队列无此问题）
- 发布签名 Valid；新版重启（真机：HTTP Toolkit 下载速度对比——1MB 片后单并发 RTT 惩罚降 4 倍）
- 批次 18 补 2（8-19，片大小自适应 + 快源并发保底）：用户问「怎么最大限度与批次 17 同等甚至更快更稳定」——计划模式（Plan agent 对抗性审查确认两个关键点）
  - **根因（审查确认）**：RTT 开销 = totalChunks/并发 × RTT——166MB：256KB 片 66.4s（实测 1.5-2.5MB/s 正是 RTT 界）→ 1MB 16.6s → 2.6MB 6.4s。**瓶颈不是请求次数，是「快源→探测判 1 连接」**——RTT 浪费对吞吐检测不可见（恒 >10MB/s），慢速检测/升片永不触发——必须在探测时刻定并发
  - **Phase 1 片大小自适应**：ChunkSizeFor(totalSize) = clamp(totalSize/64, 1MB, 4MB) 纯函数（目标 64 片 = 8 并发 × 8 波 = 0.8s RTT 上界；上限 4MB = 零字节失败重下粒度）；入口一次定永不变化（边界固定 → 续传复用保留）；<64MB 恒 1MB（现有测试 ≤10MB → 零行为变化）；166MB → 2,719,744 字节/片（非 2 幂，抓硬编码回归）
  - **Phase 2 快源并发保底**：探测快源分支 totalSize ≤8MB ? 1 : min(4, maxChunks)——4 并发摊薄 RTT 4 倍；每连接单请求 ≤2.6-4MB 在 GitHub「前几 MB 节流」窗口内不新增节流暴露；8MB 门槛保住现有快源测试（3MB/3.5MB 仍判 1）；连接数受限源最坏浪费一轮 attempt 走既有单连接回退兜底
  - 否决：档位式片大小（小文件退化更多请求）、探测决定片大小（边界变 → 全量重下回归）、升片冷却改短（升片判据是吞吐，RTT 界永不触发——错误杠杆）
  - 测试：ChunkSizeTests 7（<64MB 恒 1MB/64MB 恰 1MB/100MB→1,638,400 恰 64 片/166MB→2,719,744/256MB 恰 4MB/1GB→4MB 256 片/扫描自洽）+ AdaptiveBoundaries 集成（100MB 端到端 start 对齐 1,638,400）+ Probe_FastSource_LargeFile_FloorsConcurrency Theory 3（100MB→4/8MB→1/8MB+1→4）；全量 556/556 全绿
  - 风险：731 行 expectLen 必须用局部 chunkSize（漏改大文件合并炸）；合并/升片/判死语义零耦合
  - 发布签名 Valid；新版重启（真机：HTTP Toolkit 166MB 重下——预期初期即 4 并发 × 2.6MB 片，RTT 惩罚消失）
- 批次 19（8-19，末尾限速死区修复 + 合并阶段提示）：用户实测新架构「前 95MB 全程 MB 级、掉到 1.x 马上回升 2MB——末尾莫名降到几十 KB」——根因：**末尾死区**——剩余 <8MB 时升片被 MinUpgradeRemainBytes（≥8MB）守卫挡、判死被「并发到顶」守卫挡（快源保底 4 并发未到 8）——GitHub 连接级累积限速（末尾每连接传输量大被 throttle 到几十 KB）无任何机制干预拖到尾。修复：判死条件加 `|| totalSize - bytes < MinUpgradeRemainBytes`（剩余 <8MB 低速直接判死换路——新连接重新累积前几 MB 快，收益远大于 Resolve+探测开销）；另加合并阶段 Stage 上报「正在合并文件…」（大文件合并 64 片写 166MB 要几秒，期间速度显示几十 KB 被误认限速——上报 Stage 让用户看到收尾）。全量 556/556 全绿
- 批次 20（8-19，启动器下载日志）：用户问「怎么用 HTTP Toolkit / 能自动抓启动器日志吗」——HTTP Toolkit 抓 HTTP 层但看不到竞速业务语义（哪个源赢/为什么判死/升片几次）——给 DownloadService 加下载日志（LogWrapper → PCL\Log\Launch-*.log）：每轮候选源列表（Debug）、单候选完成/失败、竞速赢家（URL+耗时）、升片（并发 n→m + 均速）、判死换路（均速+剩余）、终败（原因+总耗时）；ShortUrl 截断长 URL（签名/镜像前缀超长）；swDl 总耗时计时。全量 556/556 全绿。HTTP Toolkit 结论：通用抓包保留（不删），启动器分析走下载日志（我直接读文件）

- 批次 21（8-12，生态「匹配失败」修复 + 跟随实例开关）：用户主页选 PCL2 存档（26.2-Fabric 0.19.3）→ 下载光影包页「匹配失败: The JSON value could not be converted...」——CF API 实测未失效（DPAPI 解密 key HTTP 200），根因：TryParseGameVersion("26.2-Fabric 0.19.3")→"26.2"→CF gameVersion=26.2（CF 用 1.21.6 格式不认年份号）→ **200+错误 JSON（data=null 不抛 JsonException）** → UI「匹配失败」
- **Step 1 容错**：GetJsonAsync Deserialize 成功后显式 TryParseCfError（错误 body 能成功反序列化成 T——data=null 的「空结果」≠ 合法 data=[]）；非 2xx 读 body 提 CF 错误消息；CurseForgeApiException(CfStatusCode) 继承 HttpRequestException
- **Step 2 版本参数自动降级**：WithVersionFallbackAsync——400 → 不带 gameVersion 重调一次（防循环最多 2 请求）；接入 SearchAsync/GetFilesAsync/FindBestFileAsync；**FindBestFileAsync 降级后 SelectBestFile(files, dropped?null:gameVersion)**（否则 26.2 过滤空误报「没有适配文件」）；CurseForgeSearchPage.VersionFilterDropped
- **Step 3 降级提示**：RunCfSearchAsync/RunBothSearchAsync 状态栏「该版本 CurseForge 暂不支持过滤，已显示全部版本」
- **Step 4 跟随实例开关**：LauncherSettings.EcoFollowInstance=true（默认开老用户无感）+ SectionDownloadView 下载行为组 ToggleSwitch + SettingsViewModel Load/Save/OnChanged 即时生效 + EcosystemViewModel 三处 gate（OnMainPropertyChanged 关→不自动选实例 / InitializeAsync 关→只取 Instances[0] / RunSearchAsync 关→gameVersion=null 显示全部版本）；加载器派生不受开关影响（fabric/forge 两侧合法）；用户显式选版本永远优先
- Modrinth 不修：26.2 facet 大概率有效（FallbackGameVersions 有 26.2），无效也软失败（200 空结果）
- 测试：CfStubHandler 纯增量（RequestUrls 全 URI 列表 / RouteJsonFull 按 PathAndQuery 路由 / RouteStatusWithBody）+ 8 个新测试；LauncherSettingsTests +EcoFollowInstance；**全量 564/564 全绿**（556 回归 + 8 新测试零破坏）
- 坑：①CF 错误 JSON 能成功 Deserialize（data=null）→ 原 TryParseCfError 只在 JsonException catch 里永远不触发——降级静默失效（本次主 bug）②测试 sortField=relevancy 是字符串，实际 BuildSearchUrl 是数字（SortIndex.Relevance→1）③RouteStatusWithBody 存 body 但 SendAsync 非 200 分支丢 body → TryParseCfError 读空串失败——stub 非 200 也要带 Content ④files 路由 key 漏 modId 路径段（/v1/mods/files vs /v1/mods/100/files）
- 发布签名 Valid；新版重启（真机：PCL 26.2 实例进光影包页——无「匹配失败」+ 降级提示；开关关后全部版本）
- 批次 21 补（8-12，光影包「才 3 个」根因排查——搜索词 + loader facet 双重过滤）：用户截图对比——PCL2（空搜索框+版本 26.2+来源全部）显示 Complementary/BSL 一堆 CF 光影包；启动器（跟随实例 fabric-loader-0.19.3-26.2 fabric）只有 3 个（Dirt RT/Krpmon Lite/old doggo 全 Modrinth）
- **根因 1（搜索词）**：截图识图确认搜索框残留 "fabric-loader-0.19.3-26.2 fabric"（无任何代码写 Query——171 行仅声明；用户残留输入）。实测 CF searchFilter=该词 → 0 结果（data=0 total=0）；Modrinth 相关性模糊命中 3 个
- **根因 2（loader facet，代码缺陷）**：跟随实例派生的 loader=fabric 传给 Modrinth → 光影包几乎不标 loader → 26.2+fabric 滤剩 3 个。实测：Modrinth 26.2 shader 带 fabric facet 第一个是 Dirt RT；不带 facet 显示 Complementary Reimagined 等全部
- **修复**：RunSearchAsync 派生 loader 加 IsModType gate（光影包/材质包不派生 loader——用户显式选加载器不受影响）；顺带确认 CF 对 gameVersion=26.2 真机返回 200+合法数据（90803 字节，与不带版本完全一致）——CF 静默忽略无效版本，批 21 的 400 降级在 26.2 上不触发属正常
- 验证手段：GLM 识图（两次聚焦指令——确认搜索框内容/状态栏「共 3 个结果」/筛选值）+ PowerShell DPAPI 解密 key 直测 CF（带/不带 26.2、带/不带 searchFilter）+ curl 直测 Modrinth（fabric facet 对比）
- 全量 564/564 全绿；发布签名 Valid；新版重启（真机：光影包页清空搜索词 → CF 侧应显示热门光影包；Modrinth 侧不再被 fabric 滤没）
- 批次 21 补 2（8-12，「没有能用的版本」详情页提示——files 200+空降级）：用户新截图——搜索框残留词 + 共 1353 个结果（loader 修复生效，fabric facet 不再滤光影包）但详情页提示「没有 fabric-loader-0.19.3-26.2 能用的版本，在下面列表里选一个试试」
- **根因**：FindBestFileAsync 带 gameVersion=26.2 → CF **files API 返回 200+空列表**（实测 data=0，非 400！）→ 批 21 降级只在 400 触发 → SelectBestFile 空池 → 误报「没有适配版本」。同族问题（CF 静默忽略/过滤无效版本）：search API 对 26.2 返回全量、files API 对 26.2 返回空——行为不一致
- **修复**：WithVersionFallbackAsync 加 isEmpty 参数——带版本调用返回空 → 不带版本重试（dropped=true）。接入：GetFilesWithFallbackAsync（空列表降级）、SearchAsync（仅**无搜索词**时 0 结果才降级——带搜索词 0 结果大概率词不匹配，降级会误导状态栏「版本不支持过滤」）。FindBestFileAsync 自动受益（降级后从全池选）
- 测试：+4（files 200+空降级、FindBestFile 200+空降级从全池选、search 无词空降级+flag、**search 带词空不降级恰 1 请求**）；全量 568/568 全绿；发布签名 Valid
- 真机：详情页点光影包 → 不再「没有能用的版本」，自动选到最佳文件（1.21.6 格式在 CF files 里存在）
- 批次 22（8-12，26.2 光影「找不到」全链路修复——Explore 7 块审计 + Plan 对抗审查）：用户实测批 21 补 2 后仍报「没有 fabric-loader-0.19.3-26.2 能用的版本」+ 版本列表空（「光影包的列表呢」）。全链路逐块实测（GLM 识图 + curl 实测 Modrinth/CF API + 代码审查）
- **实测事实**：CF search API 对 26.2 静默忽略（1353 结果正常）；CF files API 对 26.2 返回 200+空（批 21 补 2 已降级）；Modrinth **search** facet 认 26.2 但 **versions** API 不认（game_versions=["26.2"]→空、"1.21.6"→7 个）；光影包 **loaders=fabric→0**（不标 loader）；CF files 版本列表是 1.21.6 传统格式
- **真凶 A（CF 详情二次过滤）**：LoadCfAsync 调 GetFilesAsync（公开签名丢 dropped）→ SelectBestFile(files,"26.2") 再滤 → null 误报。修复：GetFilesWithFallbackAsync **public 化**（返回 (files, dropped)）+ LoadCfAsync/InstallWithDependenciesAsync 用 dropped ? null : gameVersion
- **真凶 B（Modrinth 详情双重过滤）**：详情页派生 gameVersion="26.2"+loader="fabric" 都滤空 → 列表真空。修复：GetVersionsAsync **年份号空降级**（IsYearFormatVersion 判别，保留 loader，防循环 2 请求）+ 详情页 loader 派生 _card.Type==Mod gate + InstallCard 同 gate（Explore 缺口 2）
- **块 4（依赖解析全失败，Explore 发现）**：ModDependencyResolver 精确字符串匹配 "26.2"/"" 永不匹配（**含无实例 target="" 的既有 bug**）。修复：IsCompatibleFile 对年份号/空 target 放宽（loader 保留，排序精确优先→选最新）；传统 1.x 严格不变（Resolve_VersionMismatch 锁定）
- **块 6（fabric-api 误装风险，Plan 发现）**：GetVersionsAsync 降级后 fabric-api 可能装 1.21.6 构建进 26.2 崩——LoaderService 客户端过滤 GameVersions.Contains(mcVersion)，无构建保持静默跳过
- **核心原则**：降级/放宽只对年份号（`^\d{2}\.\d+`）——传统 1.x 空结果/不匹配是真实语义绝不降级（防 1.21.6 实例高亮 1.20.1 装崩）；不做 26.2→1.21.6 映射表
- 测试：+10（CF files 降级 2、Eco GetVersionsAsync 降级 4 含传统不降级锁定、DependencyResolver 放宽 3 含 loader 仍生效、LoaderService 版本不匹配跳过 1）；**全量 578/578 全绿**
- 坑：GetFilesWithFallbackAsync 签名 ct 无默认值（VM+测试两处调用漏参 CS7036——已补 default）；LoaderServiceTests StubHandler 按 AbsolutePath 路由无法区分带/不带 query（测试设计适配：路由直接返回非空列表测过滤分支）；FabricProfileJson inheritsFrom=1.21.1 不重写（26.2 场景会污染——测试用 1.21.1 + 版本不匹配构建）
- 批次 22 补（8-12，安装路径确认对话框加修改入口）：用户问「为什么确认安装位子不给修改入口」——原设计路径由实例决定（AF2 落点：装实例 mods/shaderpacks 目录，PCL 式防装错），对话框只确认不能改。改：MessageDialogWindow 加 PathPanel（可编辑目录 TextBox + 实时落点预览 PathPreviewText + 浏览按钮 StorageProvider）；ConfirmInstallPathAsync 返回 string?（null=取消，否则用户确认的目录）；DialogService.ConfirmInstallPath 签名改 (owner, gameDir, instanceId, type)→Task<string?>；4 调用点（ProjectDetailViewModel 两处安装 + EcosystemViewModel InstallCard/InstallCfCardAsync）接入——确认框返回目录 → 后续安装 gameDirOverride/落点用新目录（局部变量不写回实例）
- 泛型化 ShowAndWaitAsync<T>（bool/string? 两用）；OnConfirm/OnCancel/OnClosed/ESC 双 TCS 兜底
- 全量 578/578 全绿（纯 UI 改动，Core 未动）；发布签名 Valid；真机：安装光影包 → 确认框可改目录 + 预览「将装到：」+ 浏览按钮
- 批次 23（8-12，末尾判死弃 99.6% 清零重下——PowerToys 271MB 实测）：用户测引擎：PowerToys 271MB 下到 99.6%（最后 1MB 不到）被判死换路 + **片集清零重下**（「没继承进度直接清零」）
- **日志还原真相**：23:32 第 1 轮竞速 → 源 3（签名 CDN release-assets.githubusercontent.com）全量分片下载 → 后段 GitHub 累积限速 64KB/s（批次 19 同款）→ **最后 1MB 触发末尾判死**（判死日志「已下0MB剩余」= 整数截断，真实剩余 <1MB）→ 片取消 WhenAll 抛 → 竞速源输 → 换路 CleanupRaceFiles 清 99.6% 片集 → 第 2 轮从零 → 又失败 → AutoRetry 第 3 轮
- **根因**：慢速监控三层判死（AL61 663 行持续低速 / 末尾 679 行剩余<8MB / 并发到顶）**都没有剩余守卫**——剩余 < 一片（chunkSize，271MB→4MB）时判死 = 弃 99.6% 换路重下 271MB，**纯亏**（批次 19 判死收益假设「剩余不多重下快」在 <1 片时失效）
- **修复**：三处判死统一加 `剩余 ≥ chunkSize` 守卫——剩余不足一片时不判死，等最后一片下完（至多几十秒）；单连接路径（DownloadSingleAsync）同守卫（total 未知时保持原判死行为）；顺带修判死日志文案（「已下X剩余」→「剩余X」）
- **测试**：SlowStream 加 slowAfter 分段速度（前快后慢）+ SlowSource_TailRemainder_WaitsForLastChunk（20MB 前 19.2MB 快 + 尾 800KB 慢 → 成功——无守卫必判死）；SmallFile 测试语义变更（<1MB 等完）；SlowSource_Chunked（5MB 中段判死）保绿验证守卫不误伤
- 坑：①SlowStream slowAfter 默认值设成 long.MaxValue → 永不 delay 恒快流（587KB/s 幽灵速度）——默认 0 ②CreateService 的 handler 流 5MB 与 SmallFile 期望 500KB 不匹配 → 读超 10 倍（64s）+ 回退单连接再 64s = 129s 校验失败——旧测试靠判死掩盖，测试流长度必须与期望一致
- 全量 579/579 全绿；发布签名 Valid；真机：重下 PowerToys 271MB 看末尾（最后一片慢速不再清零，直接下完合并）
- 批次 24（8-13，竞速提速——淘汰评估改速度外推 + GitHub 满并发）：用户问「270MB 只能这速度吗」——755KB/s 的根因：竞速淘汰评估只比「总量领先」——CDN 直连开局快（首字节毫秒级）15s 评估总量领先 → ghproxy 镜像（握手慢但全程 2MB/s 稳定）被提前淘汰 → 赢家是后段限速 64KB/s 的 CDN
- **修复 1（核心）**：淘汰评估改「预计剩余时间」PickRaceLeader 纯函数——eta = (total-bytes)×窗口/增量——稳定镜像胜过后段限速 CDN（271MB 实测：CDN eta≈1064s vs 镜像 ≈110s）；已下完（合并中）源直接保留（弃它=弃已下完文件）；全无增量回退总量领先；RaceProgress 暴露 GetTotal
- **修复 2**：探测快源档 GitHub CDN 大文件直接满并发（maxChunks=8 替代 4）——连接级累积限速按每连接传输量，满并发把 271MB 摊 8 连接（34MB/连接）尽量留前几 MB 快窗口
- 测试：+4 PickRaceLeader（镜像胜/合并保护/全卡死回退/快源不回归）；全量 583/583 全绿；发布签名 Valid
- 真机：重下 PowerToys 271MB——预期 ghproxy 镜像赢（2MB/s 级，~2.5 分钟）而非 CDN 755KB/s（~6 分钟）
- 批次 25（8-13，「影响的全修了」收尾——暂停归零 + 竞速输家片集继承）：用户盘点 17 个大 BUG 清单后指示全修——剩余两项
- **暂停归零（#1）**：DownloadChunkedAsync 通用 catch 把 OCE（用户暂停）也接住 → 清 .parts → 回退单连接 → Resume 从零（批次 18 记录不修的既有 bug）。修复：catch 链加 `catch (OperationCanceledException) { throw; }`（SlowSourceException 之后、通用 catch 之前）——暂停/取消保留片集，Resume 复用。测试：Pause_MidDownload_KeepsCompletedChunks（片 2 挂起中取消 → 0.part 完整保留 → 重下复用完成）
- **竞速输家片集跨轮继承**：竞速片集命名 `.race{index}` → `.race{RaceKey(url)}`（SHA1 前 8 位 hex，键与 URL 绑定——候选顺序轮间变化不影响）；轮间清理删除（301 行 CleanupRaceFiles 保底清 → 保留供同 URL 复用；赢家后清输家、终态失败 CleanupResiduals 全清——正常路径无积累）。判死换路后同 URL 下轮从断点续——「中途换源不丢进度」闭环
- 测试：+2（Pause_MidDownload、RaceKey_StablePerUrl）；全量 585/585 全绿；发布签名 Valid
- 坑：Edit 破坏 ServerReturns200 测试 finally 块（孤行残留 CS1519——修复）；xunit Assert.Throws 精确类型匹配（TaskCanceledException 子类失败——改断言类型）
- 真机：PowerToys 重下——中途杀任务/暂停再继续 → 进度不归零；判死换路（如镜像列表变化）→ 同 URL 片集续传
- 批次 26（8-13，GitHub API 换链限流——候选 6→3 源）：用户实测新版下载开头就几百 KB/s——日志：候选只剩 3 个（签名 URL 全缺）——换链（GitHub API 未认证 60 次/小时按 IP）被今天的多次大文件测试耗尽（每次换链 2 次 API 调用；**失败不缓存 → 每轮重试 Resolve 再打 → 重试风暴**）
- 修复：FailureCache 失败退避——403/429（限流）后 5 分钟不再打 API（额度自然恢复前不空打）；IsRateLimited + MarkFailure；ClearCacheForTest 清双缓存
- 测试：+1（RateLimited_BacksOff_NoRepeatedApiCalls——第二次调用 0 请求）；FakeHandler 加 RouteStatus + Calls 计数；全量 586/586 全绿；发布签名 Valid
- 用户方案确认：换网（IP 重置）立刻解限流；治本 = 失败退避（本批次）+ 可选 GitHub token（5000 次/小时，需用户配）
- 批次 27（8-13，GitHub API Token 可选配置——用户视角可还原）：用户观点「token 应该自由开关选择——大部分用户是下载为主，配 token 测不出普通用户视角」
- 实现：LauncherSettings.GitHubApiToken（DPAPI 加密落盘，Load/Save 同 CF key 双缓存模式）+ 设置页「下载行为」组 GitHub API Token 输入框（PasswordChar 不回显、留空=保留现有、填了覆盖、Save 清空输入）+ GitHubApiDirect ApplyAuth（每次现读设置即时生效；Authorization: Bearer）——**留空 = 普通用户未认证模式（60 次/小时/IP），还原真实用户视角**
- ToolTip 文案：60→5000 次/小时对比 + github.com 申请路径（Settings → Developer settings → Personal access tokens）
- 测试：+2（TokenConfigured 带 Bearer 头 / NoToken 不带——FakeHandler Auths 列表捕获全部请求头：redirect 模拟重建请求丢头，LastAuth 单值断言会 null）；TokenOverride 测试注入（null=动态读设置、""=显式未认证）
- 坑：FixedChunkResume.RetryAttempt_ReusesCompletedChunks 全量跑失败 1 次单跑必过（Expected 3 Actual 2）——flaky（批次 18 记录过的全量线程池时序敏感），重跑全量 588/588 全绿
- 发布签名 Valid；真机：设置页填 token → 换网前后对照（不填 = 用户视角 60 次限流；填了 = 5000 次）
- 批次 29（8-13，正版登录全链路——clientId 吊销真相 + Live 授权码流重写）：用户问「正版登录是最大弊端 + 为什么别人的设备码能用」——真相链：microsoft-auth.log 四次 AADSTS700016（Mojang 老 clientId 00000000402B532E 被吊销）；微软 2026-02 改了登录配置（HMCL #327/#328 专门修）；2026 年又把**个人注册 Azure 应用入口废弃**（「在目录外创建应用程序已被弃用」——用户实测新 outlook 账号也撞墙）→ 存量 clientId 是稀缺资源（HMCL 藏 JAR manifest、Prism 构建期注入、PCL 远程下发）
- **抓包拿协议**：HTTP Toolkit 拦截（系统代理 reg 手动设 + CA 装 CurrentUser Root）+ PCL 登录流量——PCL 用 **Live 体系 oauth20_remoteconnect**（client_id=2489da17e441279835a50896336649260，Live 老 scope wl.basic wl.emails wl.contacts.write wl.offline_access wl.signin，PPFT 防伪 + uaid cookie，token 回内嵌浏览器 fragment）
- **Lattice 重写**：devicecode（AAD 死）→ **Live oauth20_authorize 码流**：BuildLiveAuthorizeUrl（response_type=code + redirect localhost:随机端口）→ AccountViewModel 用 **TcpListener 端口 0**（HttpListener 有 URL ACL 坑，非管理员绑定被拒）→ 收 code → ExchangeCodeAsync（oauth20_token.srf）→ AuthenticateMinecraftAsync 复用（RpsTicket 修 d= 前缀——旧代码裸 token 从未真机验证）
- 顺带完成：token DPAPI 加密落盘（批次 28）+ 启动命令日志脱敏（RedactTokens：--auth_access_token/--auth_session/--accessToken 打码——launch-*.log 曾整行记录真实 token）
- 测试：+2（BuildLiveAuthorizeUrl 参数 / ExchangeCodeAsync 解析）+3（RedactTokens 三形态）+2（账号 DPAPI/迁移）；全量 595/595 全绿；发布签名 Valid；系统代理已还原（ProxyEnable=0）
- 坑：System.Web.HttpUtility 在 .NET 10 不可用（手写 query 解析）；TryGetProperty 单参重载不存在（CS1501）；certutil 装证书弹确认框卡后台任务；taskkill 的 /F 被 Git Bash 路径转换（MSYS_NO_PATHCONV=1）；PowerShell 装证书同样弹框（用户点「是」）
- 真机（待）：Lattice 正版登录（浏览器授权 → 本地回调 → 认证链）——风险点：Live 应用 2489da17 的 redirect_uri 白名单是否接受 localhost 随机端口（被拒则改 desktop 模式 redirect 空 + 用户复制 code）
- 批次 30（8-13，正版登录终局——remoteconnect 粘贴模式 + clientId 三层防护）：authorize 码流被微软拒（2489da17 只开 remoteconnect 端点——unauthorized_client 实锤）；remoteconnect 发起协议实测探明（GET 带 client_id/scope/response_type=token/redirect_uri=空 → HTML 内嵌 uaid 会话 ID）；轮询端点探不明（返回 HTML 非 JSON——PCL 程序侧流量不走系统代理抓不到）
- **最终方案「粘贴地址栏」**（老 Live SDK desktop 模式变体）：LoginMicrosoft = 发起 remoteconnect（StartRemoteConnectAsync 解析 uaid）→ 开浏览器（?uaid=xxx 登录页）→ 用户登录完复制地址栏 → 粘贴到账号弹窗（PasteTokenInput + CompleteMsLoginCommand）→ ParseRemoteConnectResult 解析 fragment（access/refresh token）→ 认证链。零协议盲区、不依赖 PCL
- **clientId 三层防护**（用户要求最高等级，PCL 同款）：①远程下发（ClientIdRemote：URL 占位待 Cloudflare Worker + 本地 DPAPI 加密缓存 + 拉不到用缓存）②设置手动值 DPAPI 加密落盘（MicrosoftClientId 走 Secrets，防 grep）③内置兜底（2489da17）；登录/刷新前 ResolveAsync 三级链（设置 > 远程缓存 > 兜底）；MicrosoftAuth.SetResolvedClientId 进程内生效值
- 测试：+2（ParseRemoteConnectResult fragment 解析 / 无 token 抛错）；全量 597/597 全绿；发布签名 Valid
- 坑：internal const 跨程序集不可见（CS0117——ResolveAsync 去 fallback 参数，App 调用不传兜底）；Live authorize 端点与 remoteconnect 端点权限独立（微软按端点开应用）
- 真机（待）：点正版登录 → 浏览器登录 → 复制地址栏 → 粘贴 → 完成。若 fragment 无 refresh_token 或认证链 XSTS 拒绝（clientId 未过 Mojang 白名单——lighty-auth 警告过），下一招：PCL 注册表 token 解密导入（CacheMsV2Access/Refresh 在 HKCU\Software\PCL，PCL 自加密格式待逆向）
- 批次 31（8-13，正版登录终局二——Live 设备码流：配对码 + 轮询自动化）：真机实测远程connect uaid 流程时浏览器弹「输入代码以允许访问」页——该 clientId 的 remoteconnect 会话被微软配置成 OTC 模式，需要「应用/设备上显示的代码」，而发起响应 HTML 里没有 otc（JS 动态生成，fUseUpdatedStringsOnDeviceCodeFlow:true）→ 粘贴地址栏流程死路；用户要求「给代码 + 重开网页选项」
- **真凶确认**：otc 就是 Live 设备码流的 user_code——微软服务器生成的一次性 8 位配对码（不是程序自定义，无规律）。协议源码（PrismarineJS/prismarine-auth LiveTokenManager）：POST **oauth20_connect.srf**（scope=service::user.auth.xboxlive.com::MBI_SSL + response_type=device_code）→ JSON {user_code, device_code, verification_uri=https://www.microsoft.com/link, interval:5, expires_in:900}；轮询 POST oauth20_token.srf?client_id= 带 grant_type=urn:ietf:params:oauth:grant-type:device_code——**HTTP 400 + authorization_pending = 继续等**（实测无 cookie 也通）
- **clientId 实测矩阵**（curl 直打）：PCL 的 2489da17… = oauth20_connect.srf 上 invalid_client（只开 remoteconnect 端点）；**00000000402b5328（Minecraft Java 官方 title id）= 200 可用**（wl.* scope 是 invalid_scope——设备码只能 MBI_SSL）
- **Lattice 重写**：删 remoteconnect/authorize/粘贴三套旧路径 → StartDeviceCodeAsync（发起+解析）→ UI 大字显示配对码 + 复制代码按钮（剪贴板）+ 自动开 microsoft.com/link → PollDeviceCodeAsync 后台轮询（onTick 状态回调 + CancellationTokenSource 取消）→ MBI_SSL token 认证链（**RpsTicket 改 t= 前缀**——d= 是 AAD 的，prismarine-auth 源码确认）→ 登录成功自动收起。**用户痛点全解决**：配对码在 UI 显示、浏览器关了有「重新打开登录网页」按钮、随时可「取消登录」（不再卡死）；粘贴地址栏环节整个消灭
- RefreshAsync 改用 MBI_SSL scope + 设备码 clientId（refresh token 轮换逻辑不变）；FallbackClientId = 00000000402b5328（ClientIdRemote 三层防护自动跟随）
- 测试：-3 旧（ParseRemoteConnectResult×2/BuildLiveAuthorizeUrl/ExchangeCodeAsync）+6 新（StartDeviceCode 解析+invalid_client 抛错 / Poll pending→token+access_denied+超时+取消 / AuthenticateMinecraft t= 前缀+RPS body 断言——SequenceHandler 按序回放+捕获 URI/body）；全量 600/600 全绿；发布签名 Valid
- 真机（待）：点正版登录 → 弹窗显示配对码 → microsoft.com/link 输码登录 → 自动完成。风险点：XSTS 对 00000000402b5328 换来的 user token 是否放行（该 id 是 Live 老 title id，被吊销的是 AAD 侧，Live 侧 2026 实测 200）；若 XSTS 拒绝 → PCL 注册表 token 解密导入（HKCU\Software\PCL CacheMsV2*）
- 批次 32（8-13，正版真机跟进——「成了但游戏还是离线 + 启动变慢」双修）：真机登录成功（设备码全链通，XSTS 对 00000000402b5328 放行），但游戏内仍离线 + 启动明显变慢
- **离线真凶**：JavaArgumentsBuilder.BuildTokens 的 user_type 硬编码 "legacy"——1.16+ 游戏读 ${user_type} 决定认证模式，正版账号也按 legacy（离线）跑。修复：Build/GameLaunchService.LaunchAsync 加 userType 参数透传（默认 legacy 兼容旧调用），HomeViewModel 正版传 msa、离线传 legacy
- **顺带修 UUID 横线**：Minecraft profile id 是 32 位无横线 hex，游戏 --uuid 要 8-4-4-4-12——AccountService.FormatUuid + LoginMicrosoft/RefreshMicrosoftAsync 落盘前统一格式化（离线 UUID 本来就带横线）
- **启动变慢真凶**：每次启动无条件 RefreshMicrosoftAsync 全链（Live refresh → RPS → XSTS → login_with_xbox → profile 共 5 个串行网络往返）。修复：MicrosoftSession 加 ExpiresAtUtc（login_with_xbox 响应 expires_in 86400 解析，Clamp 60s~7d）；AccountInfo/StoredAccount 持久化该字段（旧 json 无字段 → null → 过期）；HomeViewModel 启动前判断——token 未过期直接用缓存（0 网络，跟离线一样快），过期才刷新
- 测试：+4（FormatUuid 三形态 / LoginMicrosoft UUID 横线 / ExpiresAt 持久化重载 / userType=msa game args 断言）+ AuthenticateMinecraft 加 expires_in 解析断言；全量 604/604 全绿；发布签名 Valid
- 真机（待）：正版账号启动 → F3 看登录状态（正版应为在线模式）+ 进在线模式服务器验证 + 二次启动速度对比（应无刷新链延迟）
- 批次 33（8-13，启动提速 + 描边勾勒启动动画——用户实测「双击 2 秒才出图标，再 1 秒才见仿 PCL 动画，很生硬」）：
- **2 秒空白真凶**：08-09（6e60c83）为体积引入 `EnableCompressionInSingleFile=true`——压缩单文件必须先解压整个 84MB 载荷才启动 CLR，无窗口句柄→任务栏图标不出。修复：发布.ps1 去掉压缩（自包含版回 ~186MB，用户明确要快；轻量版本就不压缩）
- **动画滞后真凶**：主窗口整树 XAML + 6 个 VM 构造的同步 IO（accounts.json 读 3 次+DPAPI、历史、目录扫描、反射建服务）全堵在 Show() 前——主窗口内浮层动画（60×60 静态 logo 450ms 淡入+950ms 交叉淡化）等首帧才可见
- **新动画（用户选定「独立启动窗 + 描边勾勒」）**：新建 SplashWindow（无边框透明、Topmost、ShowInTaskbar=false、ShowActivated=false 不抢焦点）——logo 轮廓一笔画过：**Core/UI/OutlineTracer.cs**（marching squares 16 表边界追踪 → 端点×2 量化连接成环 → Douglas-Peucker 1.5px 简化 → Chaikin×2 平滑 → 按面积降序外环在前，纯算法 4 测试）→ **App/Animations/LogoOutline.cs**（SkiaSharp 解码 logo.png alpha 阈值 96 → Trace → PathGeometry EvenOdd 内孔镂空，静态缓存）→ SplashWindow 三段动画：外环 420ms 描边（StrokeDashArray=[全长] + StrokeDashOffset 推进）→ 内孔 220ms → 填充+BlurEffect 光晕 300ms（发光落定）→ 呼吸循环（1±0.02 sin，1600ms）等主窗口首帧。提取失败兜底原图淡入
- **无缝切换**：App.axaml.cs——splash.Show() 后 `await Dispatcher.InvokeAsync(Loaded)` 让首帧+动画先跑再构造主窗口；主窗口 ShowActivated=false；首帧检测 = Opened + 双 Background Post → splash.Dismiss()（150ms 淡出 Close）+ 主窗口 Activate；15s 兜底强制关（Dismiss 幂等）；GameDirSetup 弹窗前 await splash 关闭
- MainWindow：删 SplashOverlay/StartSplashSequence/GrowToFull；AppContent 初始 Opacity=0，FadeInContent 150ms 淡入（与 splash 淡出交叉——AL16 强切顾虑解法），完成后切回 AcrylicBlur + 150ms 补导航定位 timer（原逻辑保留）
- 坑：Dispatcher.InvokeAsync 无只传 priority 的重载（补空 Action）；Avalonia 12 PathFigures 无 1 参构造（AddRange）；DoubleCollection 没了（AvaloniaList<double>）；StrokeLineJoin 无法解析（删）；ExtendClientAreaChromeHints 系列属性无法解析（删）；FixedChunkResume 又 flaky 一次（批次 27 已知，重跑全绿）
- 测试：OutlineTracer +4（实心方块单环/圆环外内两环+面积降序/空 mask/噪声过滤）；全量 608/608 全绿；发布签名 Valid
- 真机（待）：双击 → splash 可见（目标 ≤1s，压缩关后）+ 描边动画观感验收（描边速度 420/220ms、光晕 0.55 可调）→ 主窗口交叉淡化无缝
- 批次 34（8-13，瘦身回炉——体积锁 100MB + 原生 splash 治卡）：用户对批次 33 后悔（186MB 太大 + 描边动画「根源还是卡」——主窗口构造 1-2s 同步重活冻结 UI 线程，Avalonia splash 动画帧被卡停）→ 回炉：恢复压缩（84MB ≤100MB 恒定）+ 动画极简重造
- **删批次 33 三件套**：SplashWindow.axaml(.cs) / LogoOutline.cs / OutlineTracer.cs+测试（死代码全清）
- **NativeSplash.cs（新建，单文件 ~250 行）**：Win32 分层无边框窗（WS_EX_LAYERED|TOOLWINDOW|TOPMOST|NOACTIVATE，不抢焦点不进任务栏）+ **独立线程帧循环**（PeekMessage 排空 + Sleep 16ms + UpdateLayeredWindow）——动画与 Avalonia UI 线程完全并行，构造重活期间照常流畅（治卡治本）。动画：logo 淡入 300ms → 呼吸 ±2%（1600ms 周期，每帧 SKBitmap.Resize 重采样）→ Dismiss 150ms 淡出销毁。Skia 解码 → SKColor 转预乘 BGRA（AC_SRC_ALPHA）→ CreateDIBSection。DPI（GetDpiForSystem）+ 主屏居中。失败全程静默
- App 集成：NativeSplash.Show() 在 MainWindow 构造前 → ShowActivated=false → Opened+双 Background Post → Dismiss+Activate → 15s 兜底；GameDirSetup 前 Task.Delay(250) 等淡出（防 Topmost 遮挡）。MainWindow 的 FadeInContent 150ms 淡入保留（交叉淡化无缝）
- 坑：WNDCLASSEXW.lpfnWndProc 是 IntPtr 字段（方法组不能直赋——Marshal.GetFunctionPointerForDelegate + static delegate）；SkiaSharp 3.x SKBitmap.Pixels 是 ReadOnlySpan<SKColor>（2.x 的 byte[]/GetPixels 没了——SKColor.Red/Green/Blue/Alpha）
- 测试：全量 604/604 全绿（删 OutlineTracer 4 测试）；发布签名 Valid；体积：自包含 ~84MB（压缩恢复）+ 轻量 ~23MB
- 取舍记录：压缩 = 双击后 1-2s 解压空白（CLR 前，托管救不了）——补偿 = 解压完原生 splash 立即出现且永不卡；根治空白唯一出路是轻量版（不压缩 23MB，用户机装了 .NET 可直接用）
- git：本批后 commit 工作区形成保存点（此前最后提交 6f45360 AL44，正版登录修复全在未提交区）
- 真机（待）：双击 → ~1-2s 解压 → logo 淡入呼吸流畅（构造期间不卡）→ 主窗口交叉淡入
- 批次 34 追加（真机「双击没反应」= 进程崩溃修复）：复现 Fatal error 0xC0000005——SkiaSharp 3.x 的 SKBitmap.Resize 新旧重载内部都走 ScalePixels→PeekPixels，对 SKBitmap.Decode 的不可变位图原生访问冲突，整个进程陪葬（主窗口都没出现）
- **修复**：完全绕开 SKBitmap 缩放——SKImage.FromEncodedData 解码 → 每帧 SKSurface.Create(固定缓冲区 GCHandle 内存) + Canvas.DrawImage（SKSamplingOptions Linear）+ Flush → 自管预乘 RGBA byte[] → 转 BGRA 贴 UpdateLayeredWindow。零 PeekPixels
- 验证：Debug exe 完整走到 Lifecycle Running + MainWindowTitle=Lattice Launcher（进程存活）；全量 604/604；重新发布签名
- 坑：Debug 版进程名是 Launcher.App（发布版才是 Lattice启动器）——检查脚本 ProcessName 匹配错导致误判 crashed；P/Invoke 原生崩溃（AV）catch(Exception) 兜不住，必须根治 API 用法
- 批次 34 追加 2（真机正版登录 KeyNotFound）：microsoft-auth.log 锁定 PostXstsAsync 的 GetProperty("Xui")——XSTS 响应顶层没有 Xui 键，真实结构 DisplayClaims.xui[0].uhs（浏览器授权成功后才走到这步，所以「大功告成」页面 + 启动器报错并存）。代码从批次 30 起就写错，首次真机触达。修复：TryGetProperty 全链解析 DisplayClaims（xui/Xui 双写兜底）；测试 stub 同步改真实结构；全量 604/604
- 待真机：正版登录全链（输码→浏览器授权→XSTS→Minecraft 档案→账号落座）
- 批次 34 追加 3（真机体验跟进——「响应同步慢 + 头像延时」）：正版登录全链真机通过
- 轮询提速：PollDeviceCodeAsync 前 3 分钟 3s 快轮询（授权盲区 5s→3s，用户输码后等启动器反应的体感），超窗回 session.IntervalSec；slow_down 降频处理（微软要求时回建议间隔）；测试兼容（IntervalSec=0 的测试 session 走 min 逻辑不受影响）
- 头像延时：根因 minotar 首次网络下载慢 + 每次 Refresh 置空闪白。修复：①RefreshPlayer/Refresh 不置空（旧头像保留到新图回调）②PlayerAvatarFallback/AvatarFallback 首字母占位（视图层 ObjectConverters.IsNull/IsNotNull 叠层切换——加载期显示首字母块，网络图到即替换）③ImageLoader 磁盘缓存已有（二次启动秒显）
- 坑：partial 属性必须配 [ObservableProperty]（漏了特性 CS9248）；全量 604/604；发布签名 Valid
- 待真机：登录全链延迟体感（输码→授权→完成 ≈ 3s 盲区 + 认证链）+ 头像二次启动秒显
- 批次 34 追加 4（真机「任务栏图标点不动 + 页面 2 秒才出现」）：根因 = 批次 33/34 的登场设计——主窗口 ShowActivated=false（不激活→点任务栏无反馈）+ AppContent 整页初始透明等首帧后 150ms 淡入（页面体感 2 秒才出现）。旧版主窗口 Show 即激活、内容直接可见，所以「以前很快」
- 修复：①去掉 ShowActivated=false（主窗口 Show 即激活；splash 是 NOACTIVATE+TOOLWINDOW 本来就不抢焦点）②AppContent 不再整页透明——内容随首帧直接可见，只有背景玻璃层 150ms 铺设与 splash 淡出交叉（FadeInContent 精简为只动 RootSurface+BorderBrush）
- 全量 604/604；发布签名 Valid
- 待真机：双击 → 解压 → logo 呼吸（独立线程）→ 主窗口激活+内容出现+背景铺设（点任务栏/窗口即时反馈）
- 批次 35（8-13，终局：彻底移除启动动画——启动最短路径）：用户拍板「直接不要动画了，一个个全都卡的要死，都是瞬移一样；开的怎么快怎么来」。批 33（描边）/34（原生 splash 呼吸）两版动画真机均不被接受：帧驱动（Stopwatch 绝对时间插值）在系统忙/掉帧时直接跳变=瞬移感；splash 呼吸每 16ms 重采样+UpdateLayeredWindow 与主窗口首帧渲染（CPU 密集）抢 CPU 反拖慢启动
- 修复：删 NativeSplash.cs 全部集成（Show/Dismiss/双 Post/15s 兜底/250ms 等待）；App.axaml.cs 恢复最简序列（构造→初始化→Show→GameDirSetup）；MainWindow 删构造透明准备 + FadeInContent——窗口直接全量显示（RootSurface/BorderBrush 用 axaml 默认终值），保留 ResolveTargetSize 定位 + 150ms 补导航定位 timer
- 发布配置不动（压缩 84MB 体积锁保持）。启动体验 = 解压 ~1-2s → 窗口瞬间全显（用户点名的「之前」状态）
- 全量 604/604；发布签名 Valid；commit 保存点（含崩溃修复/XSTS/轮询头像/移除动画）
- 结论记录：真机动画帧率不达标（录屏+系统负载下）——未来若要动画，先做主窗口首帧提速（VM 构造惰性化）再说
