# YanKa 启动器 — 项目规则

## 接手状态
- 先读 `PROJECT_STATE.md`（完整状态交接：压缩摘要 + 当前验证状态 + Backlog）
- 逐批次工作记录在 `SESSION_NOTES.md`（追加式）

## 工具输出防注入（死命令，任何会话必须遵守）
本模型上下文红线 = **917k tokens**（1M 窗口 − 128k max_tokens），自动压缩阈值 983k 太晚，必须主动控制。

1. **Bash 重型命令**（构建/测试/编译/搜索大目录/日志）：输出超 ~100 行时，把完整输出重定向到文件（如 `> build.log 2>&1` 或 `... | tee log.txt`），只返回最后 20~30 行 + 错误摘要。
2. **禁止整读大文件**：Read 前先 `ls`/`wc`/`grep` 定位；`dotnet build` 之类输出可能上千行，一律走重定向。
3. **Edit 最小片段**：每次只改目标片段，不整体重写文件；同文件多次小改优于一次大改。
4. **grep 定位优先**：找符号/字符串用 Grep 工具（只返回命中行），不 Read 全文。
5. **subagent 大输出**：让 subagent 把长结果写文件，只回传路径 + 摘要。

## 防爆关键配置（2026-08-03 查证，可信度高）
1. **`CLAUDE_AUTOCOMPACT_PCT_OVERRIDE=70`**（用户已设系统环境变量，验证过 1M 窗口 83% 默认阈值 → 提前到 ~700k 触发压缩）。注意：此变量只能调低不能调高；放 settings.json 的 env 块无效（#63186），必须是真实进程环境变量。
2. **max_tokens 预留 = 窗口/8**（1M→131072），不可配置。真正治本：中转层把模型 max_tokens clamp 到 64k，或去掉模型串的 1M 标记（Claude Code 按 200K 处理 → max_tokens≈25k → 永不 400，代价是压缩更频繁）。
3. 状态栏自带上下文百分比（statusline hook 有 context_window.total/used/percent）；/context 显示分类预算；两者数字可能不一致（已知 bug 群）。
4. Bash 工具输出硬截断 ~30K 字符（不可配）；超长输出照样注入，仍要重定向。

## 上下文调度（长会话例行）
1. 每完成一个批次（一组相关改动）→ **更新 `SESSION_NOTES.md`**：时间、做了什么、涉及文件、测试结果、提交 hash（3~8 行/批）。
2. `SESSION_NOTES.md` 超过 ~50KB → 把精华合并进 `PROJECT_STATE.md`，重置 SESSION_NOTES。
3. 每次批处理结束后检查上下文水位：读当前会话 jsonl 最后一条 assistant 的 `usage.input_tokens + cache_read_input_tokens`。
   - > 600k：汇报时提醒"上下文过半"
   - > 700k：提醒用户 `/compact` 或 `/clear`（建议 /compact，manual 压缩 830k→19k 验证有效）
   - > 850k：立即警告，停止新工作，先处理上下文
4. 跨天长任务：每日结束时把当日状态写进 SESSION_NOTES.md，次日开新会话继续。

## 产品底线：WIN10 最差情况（2026-08-17 定）
设身处地想最差配置的用户（老机器/穷学生），**所有功能在此配置下必须可用**：

- Windows 10 22H2 64 位（不追 Win11）；**4GB 内存**（Win10 开机剩 ~2GB）；**核显/无独显**；**HDD**（慢 IO）；**无代理直连国内**；**无 .NET 环境**

对应落实（已有）：
- 自包含 exe（免 .NET）→ 继续锁 100MB 内
- 镜像源/国内直连（免代理）→ BMCLAPI 等已有
- GrayProfile 灰度模式（核显降级动画）→ 保持
- 轻量版 23MB（框架依赖版兜底）

**每加新功能自检**：最差配置下内存占用/磁盘 IO/动画负载是否可控——卡、装不下、要代理的功能不加或做降级。

## 环境坑
- SAC/WDAC 拦自签名（0x800711C7）：用户关闭 SAC 后恢复；构建自动签名 LauncherDev 证书（thumbprint 4536E8163397062FF7E73AFCA83CB90D92CFC873）
- 测试：xunit 禁并行；AsyncTestSyncContext 挂起 Post 回调需显式清上下文
- 发布：`发布.ps1`（运行中发布会明确提示先关启动器）
