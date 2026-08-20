# Lattice Launcher 安全自查报告（2026-08-20）

对照 VersePC2 0.8 逆向审计（84 条，见 Desktop\verse-BUGS.md）高危/中危清单，逐项自查 Lattice 同类面。全部本地静态核查。

## 结论：12 项核心对照全部安全，无同类高危

| # | Verse 问题 | Lattice 现状 | 依据 |
|---|---|---|---|
| 1 | 全局禁用 TLS 证书校验（下载可 MITM 投毒） | **零禁用**——所有 HttpClient 默认证书校验 | 全库 grep danger_accept_invalid_certs / ServerCertificateCustomValidationCallback 零命中 |
| 2 | 整合包 ZIP/mrpack 解压零路径防护（Zip Slip → RCE） | **三处全有防护** | ModpackImporter.cs:227-228、ModpackInstaller.cs:241-244、TerracottaProvisioningService.cs:212（GetFullPath + StartsWith 前缀校验，非法路径拒绝/跳过） |
| 3 | 微软 token 自造弱加密 SHA256(hostname+user+dir)（本机任意进程可解） | **DPAPI CurrentUser**（Windows 原生，仅当前账户可解） | Secrets.cs:12-31 ProtectedData；AccountService.cs:172-173 微软 refresh_token 持久化全走 Secrets；CF key/GitHub token 同 |
| 4 | 自更新链无签名 + 第三方镜像（供应链 RCE） | **无自更新功能**（无攻击面） | 全库 updater/UpdateCheck 零命中 |
| 5 | /api/resource-image 任意 URL 服务端拉取（SSRF） | **无用户可控 URL 服务端拉取** | 仅 3 处 GetStringAsync：Modrinth API / mcmod / 版本清单，URL 全为代码硬编码官方端点 |
| 6 | read_file_buffer 任意文件读 | **无任意路径文件读接口** | 全部 File.ReadAll 调用点使用服务内部固定路径（缓存/配置） |
| 7 | 日志/字符串 [..10000] 无 char_boundary（panic → 启动器崩溃） | **零切片截断** | grep `[..N]` 模式零命中 |
| 8 | 端口 as u16 静默截断（70000 → 4464） | **零截断** | grep (ushort)/as ushort 零命中 |
| 9 | 模组删除按子串模糊匹配（误删用户模组） | **无模糊删除** | 删除路径为精确文件操作，无 Contains 匹配 |
| 10 | mc_ping VarInt 无上限（OOM） | **无此功能** | 无服务器 ping VarInt 解析 |
| 11 | 前端 marked+innerHTML 无消毒（XSS → Tauri invoke → RCE） | **Avalonia TextBlock 纯文本渲染**（免疫） | 生态页/详情页描述 TextBlock 绑定；无 innerHTML 等价物 |
| 12 | base64 无限解码（内存耗尽） | **base64 仅用于解析服务端响应**（无用户上传无限解码入口） | 6 处 FromBase64String 全为服务端数据解析 |

## 额外确认（安全基线）

- **联机内核（Terracotta/EasyTier）**：锁版本 + SHA256 必校验（缺失即拒绝安装，EasyTier 口子 8-20 已堵，commit 2bd5ac7）
- **下载取消**：DownloadManager 真取消（会话级 CancellationToken 传播到下载循环），非 Verse 的假取消
- **CF key**：源码零明文（构建注入 AES-HMAC 加密，发布.ps1 注入后恢复占位）；用户自填 key 走 DPAPI——对比 Verse 公开仓库 4 处明文 const
- **下载镜像**：官方源优先 + 国内镜像降级，全部正常 TLS 校验

## 残留提示（非漏洞）

1. `Secrets.Read` 对无 `dpapi:` 前缀的旧数据按明文返回（历史版本迁移兼容）——新写入全走 Protect，仅影响旧数据
2. 镜像下载（ghfast/ghproxy）为第三方代理——与官方源同等 TLS 校验，但信任链多一跳（Verse 的更新链问题是「无校验+无签名」，我们校验哈希所以风险可控）
