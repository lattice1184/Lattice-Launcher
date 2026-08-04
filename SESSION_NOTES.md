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
