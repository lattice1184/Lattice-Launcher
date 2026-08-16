# Lattice 启动器（晶格）

> 本项目由 AI 协助开发（Claude Code 编写大量代码与本文档），作者校对与实测。

> **English TL;DR** — Lattice is a self-written Minecraft launcher for Windows (PCL-style UX) that puts its effort into download speed and login simplicity. A file is fetched from **6 sources simultaneously** (official direct link + 2 mirrors + CDN signed link + mirrors of the signed link) — first to finish wins, ranked by per-source speed history. Measured: a 159.8 MB installer in **19.9 s**. Microsoft device-code login (pairing code auto-copied, no Client ID needed), Littleskin one-click auth, and offline skins visible in-game via an auto-injected resource pack (no mods needed). Single-file portable build (~84 MB), double-click to run, no .NET required. Built with Avalonia on .NET 10, Apache-2.0. Issues welcome — Chinese or English.

自己写的 Minecraft 启动器。操作逻辑参考 PCL，下载和登录是花力气最多的两块。自用打磨阶段，功能以「自己用着顺手」为准，不是要复刻谁。

- 单文件 84MB，双击即用，不用装 .NET
- 双击秒开，没有启动动画拖时间
- Windows 10/11

## 下载

### 六源竞速：一个文件同时从六个源下载

GitHub 上的文件，启动器同时从这些源起跑：

- GitHub 官方直链
- ghproxy.net / gh-proxy.com 两个加速镜像
- GitHub API 换链出的 CDN 签名直链（国内可达）
- 签名直链再套两个镜像

谁先下完用谁，其余取消。候选顺序按各源历史速度自动排列；每一轮的结果都会被记住，下一轮排名跟着变。

**实测数据**（OBS 32.2.1 安装包，159.8MB，录屏环境下多次重测）：

| 轮次 | 赢家 | 耗时 | 均速 |
|---|---|---|---|
| 1 | CDN 签名直链 | 77.4s | 2.1MB/s |
| 2 | GitHub 官方直链 | 22.9s | 7.0MB/s |
| 3 | 第1轮全源失败自动重赛 → CDN 签名直链 | 163.4s（含 121s 网络抖动） | 3.7MB/s |
| 4 | CDN 签名直链 | 19.9s | 8.0MB/s |
| 5 | CDN 签名直链 | 21.9s | 7.3MB/s |

### 卡住不干等：三层兜底

- 分片断点续传：中断、换源、重试时已下分片复用，不从头下
- 陪跑：当前赢家掉速到峰值一半，新源在后台提前开跑，超过并稳定三拍才接手，期间主源不中断
- 卡死处理：低速自动换路（30 秒低于 100KB/s）、静默断流换路、唯一幸存源停滞兜底、下载全程 watchdog
- 完成后自动清理竞速临时文件

## 登录与皮肤

- **正版（Microsoft）**：设备码配对，点登录自动复制配对码，浏览器粘贴即完成；不用填任何 Client ID。多账户切换，登录后自动同步头像与皮肤
- **Littleskin**：一条龙登录，没创建过角色直接引导去创建；登录即同步皮肤
- **离线**：自定义用户名
- **皮肤**：拖 PNG/JPG 进窗口即换（自动校验 64×64 / 64×32）；重置正版账号皮肤 = 强制同步官方皮肤（不是清空）；**离线也能在游戏里看到皮肤**——启动器自动打包资源包注入，不用装任何模组

## 版本与整合包

- 版本安装与启动：原版 / Fabric / Quilt / Forge / NeoForge，多实例隔离
- Java 自动检测，装哪个版本用哪个；想手动指定也行，版本设置里下拉直接选
- 整合包导入：拖入 .zip / .mrpack 自动建可启动实例
- Forge 下载走国内镜像（BMCLAPI 等），官方断不裸奔

## 模组与资源

- Modrinth + CurseForge 双源搜索，一键装模组（含依赖），跟随实例版本和加载器
- 模组启停、检查更新（已装的模组版本落后会标出来）
- 资源包 / 光影管理

## 开服与联机

- 本地服务端可视化：一键开服、在线玩家列表、踢出 / 封禁 / 授予 OP 图形化操作（封禁名单和 OP 列表直接读写服务器文件，服务端停止时也生效）
- server.properties 图形化编辑（内存、视距、最大玩家数按机器自动推荐）
- 联机：Terracotta 免费 P2P 直连；也支持陶瓦 / EasyTier / 蓝盾这类虚拟局域网——开服页填个「对外地址」，朋友在虚拟网里直接复制连接
- 开服页可以弹成独立窗口，日志实时同步

## 外观

- 强调色预设（靛蓝等 8 色）、自定义背景图、界面密度（紧凑/标准/舒适）、窗口透明度
- 亚克力毛玻璃窗口，背景图片可透出

## 工程与安全

- 自动化测试 634 项（下载引擎、登录、JSON 解析、主题数学等，CI 可跑）
- 发布脚本一键签名（自签名；SmartScreen 提示「更多信息 → 仍要运行」属正常，见下）
- CurseForge API Key：构建时注入并混淆，源码仓库里没有明文；用户设置里也可以填自己的 Key（DPAPI 加密落盘，明文不落磁盘）
- GitHub Token：支持，同样 DPAPI 加密

## 安装

去 [Releases](../../releases) 下载，两个版本任选其一：

| | Lattice启动器.exe | 轻量版 |
|---|---|---|
| 体积 | 约 84MB | 约 47MB |
| 依赖 | 无 | .NET 10 Desktop Runtime（没装会弹窗引导） |
| 适合 | 图省事 | 在意体积/更新快 |

**Windows 拦截说明**：自签名发布者，SmartScreen 提示「更多信息 → 仍要运行」属正常；Win11 新装机的智能应用控制（SAC）会无提示阻止，需在 Windows 安全中心里关闭。

## 构建

> 网络较劲的备胎方案：国内连 api.curseforge.com 不稳时，可自建 Cloudflare Worker 免费代理
> （设置页「CF API 地址覆盖」填入即用），教程见 [docs/cf-proxy.md](docs/cf-proxy.md)。

```bash
dotnet build            # Debug 构建
```

发布（Windows）：`powershell -ExecutionPolicy Bypass -File 发布.ps1`

产物在 `发布\`：`Lattice启动器.exe`（自包含，双击即用）与 `Lattice启动器-轻量版.exe`（需 .NET 10 Runtime）。

## 目录结构

```
src/          # 源码（Launcher.App / Launcher.Core / Launcher.Animation / Tests）
PCL.Core/     # vendored PCL-CE 核心库（Apache-2.0，见 PATCHES.md）
发布/         # 一键发布产物（勿手改）
发布.ps1      # 一键发布脚本
scripts/      # 签名等辅助脚本
```

## 许可

- `PCL.Core/`：Apache License 2.0（来自 PCL-CE，见 NOTICE）
- `src/`：本项目原创，Apache License 2.0
- 第三方依赖清单见设置页「关于」

## 反馈

自用打磨阶段，联机这块还没经过大规模用户验证（陶瓦 / EasyTier 依赖第三方服务），下载和登录是测最多的部分。Issue 随便提，能改的都会改。
