# PCL2 改良版启动器

基于 [PCL-CE 的 PCL.Core](https://github.com/PCL-Community/PCL-CE)（Apache 2.0）+ Avalonia 全新 UI 的 Minecraft 启动器。

## 目标（当务之急）

- 重做 UI（Avalonia，低占用、流畅动画）
- 本地机器开服可视化（右侧实时控制台 + 机器状态自适应策略）
- 配置图形化（server.properties / 启动参数全 GUI）
- 模组安装一条龙（下载页弹性卡片列表）
- 版本管理、一键开关、备份

## 里程碑

- M0 环境搭建 ✅
- M1 PCL.Core vendor + WPF 解耦补丁 + 项目骨架（进行中）
- M2 版本列表 + 下载引擎
- M3 启动管道（JVM 参数组装 + 进程）
- M4 设置页 + 动画 + 发布

## 构建

```bash
dotnet build            # Debug
dotnet publish -c Release -r win-x64 --self-contained   # 单文件发布
```

## 许可

- `PCL.Core/`、`PCL.Core.SourceGenerators/`：Apache License 2.0（来自 PCL-CE，见 NOTICE）
- `src/`：本项目原创（Apache License 2.0）
- 不含原版 PCL 的任何代码（《PCL 分发有限许可》禁止衍生修改）
