# 探索报告："粘贴下载链接 -> 下载到自定义目录"功能评估

## 1. 下载系统核心

| 文件 | 行号 | 类/方法 | 说明 |
|------|------|---------|------|
| `src/Launcher.App/Views/DownloadView.axaml` | :1-159 | DownloadView | 下载页 UI：队列面板+历史+进度条+暂停/继续/取消按钮 |
| `src/Launcher.App/Views/DownloadView.axaml.cs` | :5-9 | DownloadView | Code-behind（空壳，仅 InitializeComponent） |
| `src/Launcher.App/ViewModels/DownloadViewModel.cs` | :15-191 | DownloadViewModel | 下载板块 VM：tab 切换、队列绑定、历史记录、角标 |
| `src/Launcher.Core/Download/DownloadTask.cs` | :15-450 | DownloadTask | 任务模型：State(7态)/ProgressPercent/BytesDone/SpeedBps/Error；支持组任务(Children)+暂停/继续/重试 |
| `src/Launcher.Core/Download/DownloadOptions.cs` | :8-50 | DownloadOptions | 下载配置：ChunkCount(8)/LibraryConcurrency(8)/AssetConcurrency(16)/BufferSize(81920)/BytesPerSecond(限速0=不限) |
| `src/Launcher.Core/Download/DownloadProgress.cs` | :7-14 | DownloadProgress | 进度快照 record：Stage/CurrentFile/FileBytesDone/FileTotalBytes/OverallPercent |
| `src/Launcher.Core/Download/DownloadManager.cs` | :10-153 | DownloadManager | 全局单例：Enqueue(name,work)/EnqueueGroup/暂停全部/继续全部/终态 3 秒自动移除 |
| `src/Launcher.Core/Download/DownloadGroupContext.cs` | :7-28 | DownloadGroupContext | 组任务上下文：AddChild(name,weight,work) 创建子任务并挂载聚合 |

## 2. 任意 URL 下载能力

| 文件 | 行号 | 类/方法 | 说明 |
|------|------|---------|------|
| `src/Launcher.Core/Download/DownloadService.cs` | :88-103 | DownloadService.DownloadFileAsync(url, destPath, sha1?, size?, progress?, ct) | **核心通用下载函数**，参数 url/destPath/sha1/size/progress/ct，不限于 MC 资源，支持任意直链 |
| `src/Launcher.Core/Download/DownloadService.cs` | :77-81 | CreateClient() | HttpClient 封装：new HttpClient() + UserAgent "YanKa-Launcher/0.1" |
| `src/Launcher.Core/Download/DownloadService.cs` | :105-164 | DownloadFileCoreAsync | 幂等跳过(sha1/size匹配)→多候选源轮询→指数退避重试(3轮)→网络检查报告 |
| `src/Launcher.Core/Download/DownloadService.cs` | :189-239 | DownloadSingleAsync | 单连接断点续传+416防御+限速节流+sha1校验失败自动删除重下 |
| `src/Launcher.Core/Download/DownloadService.cs` | :257-321 | DownloadChunkedAsync | >256KB走多连接Range分片并发下载+合并+失败回退单连接 |
| `src/Launcher.Core/Services/EcosystemService.cs` | :86-99 | InstallAsync | 生态下载封装：调用 DownloadFileAsync 把文件下到 mods/resourcepacks/ 等目录 |

## 3. 保存路径/目录选择

| 文件 | 行号 | 类/方法 | 说明 |
|------|------|---------|------|
| `src/Launcher.Core/Utils/GameDirectory.cs` | :32-38 | OwnDefault() | 默认游戏目录：D:\YanKa Launcher\.minecraft（D盘优先），否则 C:\Users\...\Downloads\YanKa Launcher\.minecraft |
| `src/Launcher.Core/Utils/GameDirectory.cs` | :40-44 | InstallDir() | 安装目标：用户自配 LauncherSettings.GameDirectory ?? OwnDefault() |
| `src/Launcher.App/Views/SettingsView.axaml.cs` | :118-129 | OnBrowseGameDir / Picker | **可复用的 FolderPicker 模式**：`TopLevel.GetTopLevel(this)?.StorageProvider.OpenFolderPickerAsync(...)` |
| `src/Launcher.App/Views/HomeView.axaml.cs` | :48-52 | OnExportLogs | 另一个 FolderPicker 示例（选择日志保存位置） |
| `src/Launcher.Core/Services/EcosystemService.cs` | :234-244 | ResolveInstallPath | 安装路径解析：版本隔离→versions/{id}/mods，否则→共享根/mods |
| `src/Launcher.Core/Utils/LauncherSettings.cs` | :23 | GameDirectory | 用户自配游戏目录配置项（持久化到 settings.json） |
| - | - | - | **没有**针对单个下载任务选择保存目录的功能——硬编码固定在游戏目录结构下 |

## 4. 文件命名/扩展名/已有文件处理

| 文件 | 行号 | 类/方法 | 说明 |
|------|------|---------|------|
| `src/Launcher.Core/Download/DownloadService.cs` | :112-119 | DownloadFileCoreAsync | **幂等跳过**：文件存在且 sha1 匹配→跳过；sha1 为空时比大小相等→跳过 |
| `src/Launcher.Core/Download/DownloadService.cs` | :197-201 | DownloadSingleAsync | **416 防御**：残留文件长度>=目标→删除重下（坏文件不续传） |
| `src/Launcher.Core/Download/DownloadService.cs` | :230-238 | DownloadSingleAsync | **校验失败处理**：sha1/大小不匹配→删除文件抛 InvalidDataException（外层换源重试） |
| `src/Launcher.Core/Services/EcosystemService.cs` | :96 | InstallAsync | **文件命名**：`Path.GetFileName(file.FileName)` 直接取 Modrinth 文件名，无自定义命名逻辑 |
| `src/Launcher.App/ViewModels/ProjectDetailViewModel.cs` | :434-456 | EnsureNoConflictAsync | **冲突提示**：同名文件弹确认框(覆盖/取消)；mod id 匹配检测(fabric.mod.json) |
| - | - | - | 无 URL 解析扩展名逻辑——第三方直链需新增从 URL/Content-Disposition 提取文件名的代码 |

## 5. 下载历史记录/列表 UI

| 文件 | 行号 | 类/方法 | 说明 |
|------|------|---------|------|
| `src/Launcher.App/Services/DownloadHistoryService.cs` | :7-10 | DownloadHistoryEntry | 历史条目 record：(Name, State, Time, Error)，持久化 AppData\Launcher\history.json，最多 200 条 |
| `src/Launcher.App/Services/DownloadHistoryService.cs` | :31-38 | Record(DownloadTask) | 任务终态(完成/失败/取消)自动记录，去重(同一任务一次) |
| `src/Launcher.Core/Download/DownloadManager.cs` | :17 | Tasks | ObservableCollection<DownloadTask> — 活跃任务列表，UI 直接绑定 |
| `src/Launcher.App/ViewModels/DownloadViewModel.cs` | :23-26 | Tasks/History | **可直接复用**：Enqueue 的第三方下载任务会自动出现在现有队列+历史 UI |
| `src/Launcher.App/Views/DownloadView.axaml` | :36-130 | QueuePanel DataTemplate | 队列 UI：Expander 组任务+叶子任务(名称/状态徽标/进度条/速度/ETA/字节) |

## 6. 模组安装/文件复制

| 文件 | 行号 | 类/方法 | 说明 |
|------|------|---------|------|
| `src/Launcher.Core/Services/EcosystemService.cs` | :226-232 | ResolveSubDir(ProjectType) | 类型→子目录映射：Mod→"mods", Resourcepack→"resourcepacks", Shader→"shaderpacks" |
| `src/Launcher.Core/Services/EcosystemService.cs` | :234-244 | ResolveInstallPath(gameDir,instanceId,type) | 安装路径解析：整合包→downloads/modpacks；其他→版本隔离目录或共享根 |
| `src/Launcher.Core/Services/EcosystemService.cs` | :86-99 | InstallAsync | **下载即安装**：直接下到目标目录(创建目录)+幂等(sha1匹配跳过)，无"复制到"逻辑 |
| `src/Launcher.App/ViewModels/VersionManageViewModel.cs` | :117-130 | CollectMods() | MOD 扫描：RootDir/mods 下 *.jar* 文件(含 .disabled 禁用后缀) |
| `src/Launcher.App/ViewModels/VersionManageViewModel.cs` | :393-400 | CopyDir(src,dest) | **可复用**的目录复制工具方法(递归复制文件) |
| `src/Launcher.Core/Download/ModpackImporter.cs` | :73-94 | Import | 整合包导入：解析 zip→解压到 versions/{id}+防目录穿越+写安装标记 |
| - | - | - | 无独立的"下载文件→复制/移动到 mods"的中间步骤——文件直接下载到最终位置 |

## 小结

**可直接复用的**：
- `DownloadService.DownloadFileAsync(url, destPath, ...)` — 核心下载函数，支持任意 URL，自带断点续传/多源/限速/校验
- `DownloadManager.Instance.Enqueue(name, work)` — 入队即自动显示在下载队列+进度+历史
- SettingsView.axaml.cs 的 `StorageProvider.OpenFolderPickerAsync` — FolderPicker 模式
- `EcosystemService.ResolveInstallPath` — mods 目录路径解析

**需要新增的**：
- 下载页新增 TextBox+按钮("粘贴链接")，调用 `TopLevel.GetTopLevel().Clipboard.GetTextAsync()` 读取剪贴板
- URL→文件名提取（从 URL path 或 Content-Disposition header 解析）
- 可选保存目录的 FolderPicker（复用 SettingsView 的 Picker 模式）
- 默认目录逻辑（如 `LauncherSettings` 新增 `ThirdPartyDownloadPath` 默认 mods 文件夹）
- 第三方下载文件类型识别（.jar→mods, .zip→modpacks, .mrpack→提示）
