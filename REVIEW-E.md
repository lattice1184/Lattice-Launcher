# REVIEW-E：账号 + 设置 + 存储用户旅程完整走查

范围：AccountService / MicrosoftAuth / AccountViewModel / SettingsViewModel / LauncherSettings / Secrets / StorageScanner / StorageWindow / GameDirSetupWindow / GameDirectory / HomeViewModel 启动链路 / DownloadManager。共 15 条。

---

## 高

### 1. src/Launcher.App/ViewModels/AccountViewModel.cs:153-157 | 高 | 微软登录从未保存 refresh_token → 正版账号永远无法启动游戏
设备码轮询 `PollOAuthTokenAsync`（MicrosoftAuth.cs:86-87）只提取 `access_token`，丢弃了同响应里的 `refresh_token`（scope 含 offline_access 必返回）；`AuthenticateMinecraftAsync(http, oauthToken, "", ...)` 把空串当 refresh token 存入账号。而启动链路（HomeViewModel.cs:417-431）对正版账号无条件走 `RefreshMicrosoftAsync` → 用空 token 调 token 端点 → invalid_request → 抛「刷新登录已失效，请重新登录」→ 启动中止。**结果：微软账号登录后一次都启动不了游戏**，「静默刷新」功能整体失效（AccountService.cs:69-84 的轮换保存逻辑永远跑不到成功分支）。
复现：账号页「微软登录」完成授权 → 回首页点启动 → 每次必然失败提示重新登录，且重新登录也无效。

---

## 中

### 2. src/Launcher.App/ViewModels/HomeViewModel.cs:417-431 | 中 | 启动无条件全链刷新：断网/微软故障时正版账号无法离线启动
每次启动都强制走 refresh_token→Xbox→XSTS→Minecraft 4 段网络链，从不检查已保存的 AccessToken 是否仍有效（即便有效也刷新）。断网或微软端点不可用时，即使游戏文件齐全、正版会话未过期也无法启动；且 `RefreshMicrosoftAsync` 内 `CancellationToken.None` + HttpClient 默认 100s 超时，网络黑洞时启动可挂数分钟无取消路径。
复现：正版账号正常启动一次（假设能成功）→ 断网 → 再启动 → 直接失败「正版登录已失效」。

### 3. src/Launcher.App/ViewModels/HomeViewModel.cs:413-414 | 中 | 版本级 Java 路径泄漏进全局设置并落盘
`if (!string.IsNullOrEmpty(javaCfg)) s.JavaPath = javaCfg;` 把版本级配置写进全局单例 `LauncherSettings.Current`，此后任意一次 Save（设置页改任何项、外观保存、150ms 防抖滑块）都会把该版本的 Java 路径持久化为全局设置，静默覆盖用户原本的全局 Java 选择（GameLaunchService.cs:65 读的就是全局字段）。
复现：全局 Java 设为 A → 启动一个版本级 Java 为 B 的版本 → 回设置页改个开关 → 检查 settings.json，JavaPath 已是 B → 其它版本也跟着用 B。

### 4. src/Launcher.App/Views/StorageWindow.axaml.cs:83-105 | 中 | 逐项删除在后台线程弹确认框：Avalonia 跨线程抛错被吞，点击删除无任何反应
`_ = Task.Run(async () => { ... await DialogService.Confirm(owner, ...) ... })` —— `MessageDialogWindow.Confirm`（MessageDialogWindow.axaml.cs:57-64）在后台线程构造窗口并 `ShowDialog/Show`，Avalonia 要求 UI 线程 → 第一处异常被 catch 吞掉后 `win.Show()` 再抛 → 逃出 Confirm → 任务静默 fault。结果：**不弹确认框、不删除、无任何提示**。设置页的 `CleanGroup`（ModuleSettingsViewModel.cs:111-131）在 UI 线程弹窗是对的，唯独此窗口走错线程。
复现：存储窗口点任意「可删」行删除按钮 → 什么都没有发生。

### 5. src/Launcher.Core/Utils/StorageScanner.cs:53 | 中 | 「清理下载缓存」会永久删除 downloads/modpacks 下未导入的用户整合包
`downloads/modpacks` 是生态页下载整合包的落点（ProjectDetailViewModel.cs:613「整合包已保存至 downloads/modpacks，可在版本页导入创建实例」、EcosystemService.cs:320），属于用户主动下载、尚未导入的资产，却被归入「下载缓存」组整体可删。删除前只有「删除后不可恢复」确认，无内容提示。
复现：下载一个整合包（未导入）→ 设置→存储→「下载缓存」→清理 → 整合包文件消失且无法恢复。

### 6. src/Launcher.Core/Account/AccountService.cs:131,141-152；src/Launcher.Core/Utils/LauncherSettings.cs:132,153 | 中 | 账号/设置 JSON 非原子写 + 损坏静默吞 → 数据无声丢失
`File.WriteAllText` 直接覆盖目标文件，中途崩溃/断电即损坏；`Load` 对坏文件 catch 后静默回退（账号全空、设置全默认），`Save` 失败也静默。用户重启后只见「未登录」+ 默认设置，无任何提示，且旧数据已不可恢复（无 .bak）。
复现：登录 3 个离线账号 → 强杀进程（写盘瞬间）→ 重启 → 全部账号消失、无提示。

### 7. src/Launcher.Core/Download/DownloadManager.cs:31-39 | 中 | 「最大并发下载数」改动对全局队列门无效：Instance 构造时固化
`DownloadManager.Instance` 静态实例在首次访问时用当时的 `MaxConcurrentDownloads` 构造 `SemaphoreSlim` 门，之后设置页改值不重建。`DownloadOptions.FromSettings`（DownloadOptions.cs:63-75）每个任务按新值生成（分片并发确实即时生效），但**同时运行的任务数上限永远停留在旧值**——设置 UI 与实际行为不一致。
复现：启动器开着时把「最大并发下载数」从 8 改到 2 → 连开 5 个下载任务 → 5 个同时跑，不排队。

---

## 低

### 8. src/Launcher.App/Views/MainWindow.axaml.cs:111 | 低 | 外观预览未保存直接关窗：改动静默丢失，无未保存提示
外观改动仅预览（SettingsViewModel.cs:363-365），需点「保存并应用」才落盘；但窗口 Closing 只存窗口尺寸，不检测未保存的外观变更。其它 5 个分区全部即时保存，唯独外观需要手动按钮——用户改完直接关窗即静默丢失。
复现：设置→外观→拖透明度/换强调色→直接关启动器→重启，改动全无。

### 9. src/Launcher.Core/Utils/LauncherSettings.cs:127；src/Launcher.Core/Utils/Secrets.cs:27-35 | 低 | CF Key DPAPI 解密失败 → 下次任意保存把 key 永久覆盖为空
`Secrets.Read` 解密失败返回 null → Load 得到 ""；此后用户改任何设置触发 Save，把加密密文整体替换成空串，key 永久丢失（仅表现为 CF 源不可用，无警告）。
复现：手动把 settings.json 里 `dpapi:` 密文改坏一个字节 → 启动 → 设置页改个开关 → key 从此为空。

### 10. src/Launcher.App/ViewModels/SettingsViewModel.cs:341-342,360,308-312 | 低 | Java 路径/额外参数/CDN 前缀每击键整文件落盘（含 DPAPI 加密 key），且输入中的半截 CF key 被顺手保存
`OnJavaPathTextChanged/OnExtraJvmArgsTextChanged/OnCurseForgeCdnPrefixTextChanged` 全部即时 `Save()`——每敲一个字符都全量序列化 settings.json 并对 CF key 做一次 DPAPI 加密；且 `Save()` 内「输入框非空即覆盖 key」（308-312），在 CF Key 框里输入到一半、转去改别的设置，半截 key 已落盘，之后「检查」只会验证半截 key。
复现：在 CF Key 框粘贴完整 key 后不点检查，先改 Java 路径框 → 半截 key 已写入。

### 11. src/Launcher.App/ViewModels/ModuleSettingsViewModel.cs:73-80,103-108 | 低 | 存储上限（StorageCapsMb）只显示不执行：无任何强制/自动清理
上限写入与超限红字标记齐全，但全库无一处消费 `StorageCapsMb` 做清理或拦截（grep 仅命中设置读写与显示）。用户以为设了「日志 200MB 上限」会被限制，实际只是标记。
复现：把「日志」上限设为 1MB → 日志照常无限增长，仅有红色超限标记。

### 12. src/Launcher.App/ViewModels/AccountViewModel.cs:147-157 | 低 | 微软登录流程无取消路径 + 完成后无条件覆盖用户当前的切换
轮询用 `CancellationToken.None` 且 UI 无取消按钮，15 分钟窗口内用户只能干等或关浏览器等 declined；期间用户在账号页切换到其它账号，授权完成后 `LoginMicrosoft` 又把 Current 覆盖回刚登录的账号。
复现：开始微软登录 → 轮询期间切换到离线账号 → 完成授权 → 当前账号被弹回正版账号。

### 13. src/Launcher.Core/Account/AccountService.cs:87-109,134-139 | 低 | 切换/删除/退出不清理 MicrosoftSession，残留旧账号会话
`SwitchTo/Delete/Logout` 只改 `Current` 与账号列表，`MicrosoftSession`（含 access/refresh token 内存态）原样残留。当前无消费方所以不炸，但任何后续读取该字段的代码都会拿到与 Current 不符的会话。
复现：正版登录 A → 切到离线账号 → 检查 `AccountService.Shared.MicrosoftSession` 仍是 A 的会话。

### 14. src/Launcher.App/ViewModels/SettingsViewModel.cs:434-444 | 低 | 150ms 防抖保存窗口内关窗/强退 → 滑块值丢失
防抖只存在于内存，无关闭时 flush。拖动并发/限速/分片滑块后 150ms 内关掉启动器，最后值不落盘。
复现：拖动「下载限速」滑块 → 立即关窗 → 重启，限速回旧值。

### 15. src/Launcher.App/ViewModels/AccountViewModel.cs:67-70 | 低 | 头像加载 fire-and-forget 竞态：快速切换账号显示旧账号头像
`ImageLoader.LoadAsync` 无取消/序号校验，慢网下先请求的旧头像后返回时直接覆盖新头像。HomeViewModel.RefreshPlayer（HomeViewModel.cs:271-296）同样问题。
复现：慢网环境 A/B 账号快速来回切换 → 头像与当前账号名不一致。
