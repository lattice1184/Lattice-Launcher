# REVIEW-C：生态/资源用户旅程代码走查

走查范围：下载页 MOD tab（EcosystemViewModel）→ 搜索（英文/中文双链路、双源合并）→ 三级筛选/防抖/收藏 → 详情页 → 一键安装 → 整合包导入/导出 → 第三方下载。
日期：2026-08-11。

## 已复核清单

- B3（LoadFavoritesAsync 循环内 Cards.Add 无 seq 守卫，EcosystemViewModel.cs:479-516）：**仍存在**，且比原描述多一个变体——循环进行中关闭收藏模式，旧循环继续把已拉取的收藏卡片混入新搜索结果（见 M2）。
- B5（「全部」双源中文分流）：**已修且修复正确**——RunBothSearchAsync（EcosystemViewModel.cs:389-397）中文 query 的 Modrinth 侧走 SearchChineseAsync，CF 侧原样（中文 0 命中属预期降级，状态栏有提示逻辑）。
- B6（ImageLoader 忽略 CancellationToken + 大图失败毒化小图缓存，ImageLoader.cs:26-47）：**仍存在**——ct 参数声明后从未使用；失败在 44 行写 `Cache[url]=null` 永久毒化；另磁盘缓存无 TTL/无失效，URL 图片更新后永远显示旧图。
- B7（McmodSearchService 详情页顺序拉取无单条超时预算 + slug 切片不截断查询串）：**仍存在**——SearchSlugsAsync（McmodSearchService.cs:53-77）最多 10 条顺序 GetStringAsync，每条约 15s 上限，最坏 ~150s；DecodeModrinthSlug（47 行）`url[(idx+5)..]` 不截断查询串，带参 URL 404 整条跳过（见 M4）。
- B13（ghapi 签名直链进 SourceStats 统计表膨胀）：**部分仍存在，机制与描述有出入**——DownloadService.cs:404 换链后 415 行 RecordSuccess 记录的确实是签名 URL，但 SourceStats 按 host 键控（SourceStats.cs:45），签名 URL host 恒定，实际不膨胀；真正后果是 ghapi 占位源在 Rank 中 host 为空、永远无历史数据、无法被速度排序加权（见 M6）。
- B14（GitHubApiDirect 缓存无逐出 + 302 链响应体不读）：**仍存在但影响轻微**——缓存读时有过期检查（GitHubApiDirect.cs:44）但过期条目永不删除（字典无界增长，量小）；302 响应 ResponseHeadersRead 后不读体，连接不复用（见 M6）。

---

## 发现

**ModpackInstaller.cs:219-228 | 高 | mrpack 导入路径穿越：files[].path 未做包含性校验**
mrpack 在线下载路径 `Path.Combine(versionDir, f.Path)` 无 GetFullPath/StartsWith 包含检查，且 `Directory.CreateDirectory(Path.GetDirectoryName(target)!)` 会逐级建目录；`f.Path` 为绝对路径（`C:\...`）时 Path.Combine 直接返回第二参数，可写盘任意位置（游戏目录内可覆盖 versions/{id}/{id}.json 启动配置、options.txt、其他版本 mods 等）。zip 解压侧（ExtractZipEntries:223-225）有目录穿越防护，唯独这条下载路径没有。
复现：导入一个构造的 mrpack，modrinth.index.json 中 files[].path 填 `../options.txt` 或 `../../versions/1.21.1/1.21.1.json` → 下载直写越界文件，无任何拦截。

**ModpackInstaller.cs:195-202 | 高 | 自家 ZIP 格式整合包导入：内容永不落盘（静默数据丢失）**
ModpackImportFlow（版本页按钮 / 窗口拖拽 / 下载完成三入口全走它）→ ModpackInstaller.ImportAsync → InstallContentAsync 对 ModpackFormat.Own 返回 `(0, [])`，注释写「自家格式：Import 已解压」但该流程从未调用 ModpackImporter.Import——全库对 `ModpackImporter.Import(` 的调用只剩测试（ModpackImporterTests.cs:181/203）。结果：导入「导出整合包 (ZIP)」产生的包，只装出原版/加载器基座，mods/config/saves 全部静默丢失，Toast 还报成功。
复现：版本页「导出整合包 (ZIP)」→ 再导入该 zip → 确认框显示文件数正确 → 实例创建成功但 mods 目录为空。

**MrpackExporter.cs:39-46 + ModpackInstaller.cs:215-218 | 高 | mrpack 导出 files 无 downloads 直链 → 导入/第三方全部跳过模组**
导出 mrpack 的 files[] 只写 path/hashes/env/fileSize，downloads 恒为空数组；导入侧 InstallMrpackAsync 对 `string.IsNullOrEmpty(f.Url)` 一律 skipped「无下载地址」——自己导出的包自己导回，所有模组全丢；PCL/HMCL/Modrinth App 同样因 mrpack 规范要求 downloads 而无法安装。另：BuildDependencies（MrpackExporter.cs:87-104）只从版本目录名正则取 mc/loader，自定义名实例（如中文名）导出的包 dependencies 为空 → 本启动器 ResolveMcVersion 返回 null 直接「无法解析整合包的 Minecraft 版本」，第三方拒收。
复现：版本页「导出整合包 (mrpack)」→ 导入该 mrpack → 全部模组被跳过，仅 overrides 内容（config 等）落地。

**EcosystemViewModel.cs:373,396 + CurseForgeService.cs:306 | 中 | CurseForge 分页把页码当偏移传：第 2 页起结果与第 1 页几乎重复**
CF API `index` 语义是「响应第一条的偏移量」（offset），VM 却传 CurrentPage（0 基页码，未乘 PageSize）：第 2 页 index=1&pageSize=20 返回 items[1..21)，与第 1 页 items[0..20) 有 19 条重复，翻页只推进 1 条；分页栏按 totalCount 算出页数，用户逐页翻会看到大量重复卡片。「全部」双源模式（396 行）同样受影响；Modrinth 侧 offset=CurrentPage*PageSize（356 行）正确，两侧不对称。
复现：来源选 CurseForge（或「全部」）→ 搜索结果 ≥21 条 → 点第 2 页 → 列表几乎与第 1 页相同。

**EcosystemViewModel.cs:483-520 | 中 | 收藏模式竞态（B3 复核 + 变体）**
原问题仍存在：循环内逐条 Cards.Add（500/507 行）无 seq 检查，只在循环末尾 512 行检查一次。变体更糟：收藏循环进行中关闭收藏模式或改筛选 → 新搜索（ct=default，不取消旧循环的 cts）先 Cards.Clear 再填充正常结果，旧循环继续把已拉取的收藏卡片 Add 进来混入结果；且旧循环的网络请求会全部跑完（每条约 15s 超时），期间收藏模式不可取消。
复现：收藏 10+ 个项目 → 打开「只看收藏」→ 等 1 秒立刻关掉 → 结果列表混入收藏卡片，且后续几次筛选都会被迟到卡片污染。

**ProjectDetailViewModel.cs:364-376 | 中 | 详情页实例切换无竞态守卫：旧实例匹配结果覆盖新实例**
UpdateContext 每次实例切换 `_ = Task.Run(LoadAsync + LoadVersions)` 并发重跑，无序号/取消；快速连续切换时，旧实例的 FindBestVersionAsync 响应晚到会覆盖新实例的 _matchedVersion/VersionHint/CanInstall。用户此时点安装：版本来自旧实例响应、instanceName 读的是新 _instance → 版本-实例错配（可能把 A 实例匹配的版本装进 B 实例目录）。
复现：详情页开着 → 顶部快速切换两次目标实例 → 匹配版本行可能仍是第一次实例的结果，点安装装到新实例。

**McmodSearchService.cs:53-77 | 中 | 中文搜索链路最坏 ~150s 无整体预算（B7 复核）**
SearchSlugsAsync 对最多 10 个 mcmod class 详情页顺序 GetStringAsync（每条约 15s 超时），任何一条挂起都拖慢整链；无整体预算、无并发、无进度提示，UI 全程「搜索中」。43-49 行 slug 切片不截断查询串（`?tab=…` 之类跟进去 → 404 → 该条目静默消失）。
复现：中文搜索一个冷门词 → mcmod 有多条结果但其中一条详情页慢 → 界面转圈数十秒。

**EcosystemViewModel.cs:572-591 | 中 | 卡片一键安装对依赖失败只弹「安装完成」**
InstallCard 在 task.Completed 后只看 report.Installed[0] 弹成功 Toast，report.Failed 非空（依赖没装上）完全不提示；对比详情页路径 ProjectDetailViewModel.ExecuteInstallAsync:604-610 有「部分安装失败：xxx」分支。同一动作两条入口反馈不一致，用户选了「全部安装」却不知道前置缺失。
复现：卡片安装一个带依赖的 mod，网络/API 故障致部分依赖失败 → Toast「安装完成」但游戏里缺前置。

**DownloadService.cs:398-421 + GitHubApiDirect.cs:44,62 | 中 | ghapi 换链统计错位 + 缓存无逐出（B13/B14 复核）**
B13：`url = signed` 后 RecordSuccess(url) 记录的是带 30 分钟签名过期的 CDN URL；SourceStats 按 host 键控不膨胀，但 ghapi 占位源在 Rank 中 host 为空永远无历史数据、速度排序对它失效——「官方兜底源」即使实测最快也排不进前面。B14：缓存读时有过期检查但过期条目永不删除（无界增长，量小）；302 响应 ResponseHeadersRead 后不读体，连接不复用。
复现：同一 GitHub 资产重复下载多次 → 统计里只有 release-assets.githubusercontent.com 一条死数据，镜像全挂时 ghapi 兜底源仍按默认顺序排在最后。

**EcosystemViewModel.cs:34-40 | 中 | 构造期赋值先于 _suppressSearch：预加载每个 tab 白发 2 次网络搜索**
SelectedSort/SelectedGameVersion 赋值发生在 `_suppressSearch = true`（38 行）之前，OnSelectedSortChanged/OnSelectedGameVersionChanged 直接触发 RunSearchAsync——注释声称「构造期不搜」，实际 4 个 tab 预加载各发 2 次空查询搜索（启动共 8 次浪费请求），并与 InitializeAsync 之后的实例搜索竞态，首屏可能出现短暂的空筛选结果闪屏/列表跳动。
复现：启动进下载页 → 观察网络请求，4 个 tab 构造即各发 2 次 search 请求。

**ThirdPartyDownloadViewModel.cs:79-109 | 中 | 文件名识别结果覆盖手动填写值**
识别防抖后 HEAD 请求最长 15s；期间用户手动填文件名（OnFileNameTextChanged 置 CanStart=true）→ 识别完成回调（94 行）无条件 `FileNameText = name` 覆盖手动值 → 下载落盘用错文件名。识别没有「用户已手动填写则放弃」的判断，也没有取消入口。
复现：粘贴响应慢的直链 → 立即手动填「my.zip」→ 等 5 秒文件名被替换成服务器 Content-Disposition 值 → 下载文件名与预期不符。

**CurseForgeService.cs:127-134,229-231 | 低 | CF 文件列表 pageSize=50 无分页截断**
GetFilesAsync 只取最新 50 个文件；老 mod（>50 文件）手动版本列表被截断无提示；依赖解析 `FirstOrDefault(f.id == dep.File.Id) ?? SelectBestFile` 在窗口外找不到目标文件时静默回退「最佳文件」——可能装到不兼容版本（依赖侧）。
复现：安装一个文件数 >50 的老 mod → 手动版本列表只有最新 50 条；某依赖的目标文件不在窗口内 → 静默装错版本。

**ProjectDetailViewModel.cs:502-524 | 低 | 安装前冲突扫描在 UI 线程同步读全部 jar**
EnsureNoConflictAsync 对 mods 目录每个 jar 同步 ZipFile.OpenRead + 读 fabric.mod.json（UI 线程、await 之前）；数百 jar 的大目录下点「安装」按钮到弹窗出现之间有可见卡顿。
复现：mods 目录 200+ jar → 详情页点安装 → 界面卡顿 1~3 秒。

**MrpackExporter.cs:24 + VersionManageViewModel.cs:349-351 | 低 | 导出 zip/mrpack 同名静默覆盖**
ZipArchiveMode.Create / ZipFile.CreateFromDirectory 对已存在的同名包直接覆盖，无确认；用户重复导出同名包（默认名即实例名）旧包被无提示替换，无备份。
复现：同一实例导出两次 → 第一次的包被覆盖。

**ProjectDetailViewModel.cs:78-106 | 低 | 截图画廊快速切换无次序守卫：旧图覆盖新图**
Prev/Next 连续点按触发两个 ImageLoader.LoadAsync，回调乱序时旧请求先到则新图被旧图覆盖（B6 同根因：无取消/无序号）；详情页从卡片打开时画廊载入与用户操作并发同样受影响。
复现：详情页截图 ≥2 张 → 快速来回点左/右箭头 → 偶发显示与页码不符的图。

**EcosystemViewModel.cs:483-511 | 低 | 收藏模式逐条拉取无独立超时且中途不可取消**
每收藏顺序拉详情（MR GetProjectAsync / CF GetProjectAsync），网络挂起时每条吃满 15s；失效收藏多时「只看收藏」长时间转圈，且如 M2 所述切换筛选不会取消旧循环（cts 不联动）。
复现：收藏里有多条已失效/慢项目 → 开「只看收藏」→ 长转圈且无法通过操作中断。
