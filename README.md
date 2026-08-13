# Lattice Launcher（晶格启动器）

基于 [PCL-CE 的 PCL.Core](https://github.com/PCL-Community/PCL-CE)（Apache-2.0）+ Avalonia 的 Minecraft 启动器。

## 功能

- 版本安装与启动：原版 / Fabric / Quilt / Forge / NeoForge，多实例隔离
- 资源下载：Modrinth + CurseForge 双源搜索、一键装模组（含依赖）
- **整合包导入**：拖入 .zip / .mrpack 即自动建可启动实例（CF / Modrinth / 自家格式）
- 开服与联机：本地服务端可视化 + Terracotta 联机
- 外观主题：强调色 / 背景图 / 界面密度，动画丝滑
- CurseForge API Key 支持，DPAPI 加密落盘，明文不落磁盘

## 构建与发布

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
