# BlockHelm-Launcher UI 借鉴笔记（#213 UI 去笨重第一批）

来源：`C:\Users\yanka\Desktop\Temp\BlockHelm-Launcher`（zqq-699/BlockHelm-Launcher，克隆比对用）。
本文件记录可借鉴手法与落地方案，实现时不再重挖克隆。

## 5 项可借鉴手法

### 1. 设置页 DataTemplate 分区（SettingsPageView.xaml:30-71,95-98）
ContentControl 绑 CurrentSectionViewModel + 类型→View 映射（DataTemplate keyed by VM 类型），
替代我们 5 个 Border 的 IsVisible 硬切（SettingsView.axaml:64-269）。
→ 结构扁平化，省 ~50 行；每分区独立可滚动。
落地：SettingsView 重构为「左侧分区导航 + 右侧 ContentControl」。**本批最后做（依赖卡片模板）**。

### 2. 统一卡片三层模板（ControlStyles.Page.xaml:410-427）
- GroupHeader：半粗体分组标题
- SettingGroupCard：半透明圆角 + 阴影卡片
- SettingRow：`*,220` 两列行（标签 + 控件）
→ 抽成 App.axaml 三个 Style，SettingsView 全部卡片套用。

### 3. 开关滑块动画（ControlStyles.Buttons.xaml:683-808）
36x18 轨道 + 圆形 Thumb + 0.14s CubicEase 过渡。
→ Avalonia ToggleSwitch 自定义 Template（App.axaml 全局替换模板，所有 ToggleSwitch 生效）。

### 4. 强调色选色器（SettingsSectionResources.xaml:90-130）
ComboBox 每项 10x10 圆点 + 颜色名。
→ 升级现有 AccentPresets 一排圆点（SettingsView.axaml:157-173）为带名字的选择（新增预设 + 自定义？本批先只做「圆点+名字」的 ComboBox）。

### 5. AnimatedCollapse 条件展开（AnimatedCollapse.cs）
已有 ExpandCollapseTransition 可复用（App.axaml:47），**不新增**。

## 另外两刀（治「笨重」大面）

### 6. 导航加图标
Win11 自带 **Segoe Fluent Icons**（与 BlockHelm 用 Segoe MDL2 同思路，零资源成本）。
MainWindow.axaml:51-100 每个导航按钮图标+文字；160px 列可收窄。
字形参考（Segoe Fluent Icons codepoints）：
- 首页/仪表盘: \uEA8F（Home）或 \uE80F
- 版本: \uE9D2（AllApps）或 \uE823
- 生态: \uE8B7（Globe）/ \uEA86（WebSearch）
- 下载: \uE896（Download）
- 设置: \uE713（Settings）
- 服务器/联机: \uE7C3（Network）
- 控制台/日志: \uE756（Terminal）
实际渲染：FontFamily="Segoe Fluent Icons" 的 TextBlock，Text 为 codepoint。

### 7. 阴影令牌（App.axaml ResourceDictionary）
`<BoxShadows x:Key="ShadowCard">0 2 8 0 #14000000</BoxShadows>`（卡片级，轻）
`<BoxShadows x:Key="ShadowPop">0 8 24 0 #26000000</BoxShadows>`（浮层/弹窗级）
统一挂卡片/弹窗，全局一致。

## 实施顺序（依赖关系）
1. 阴影令牌 + 卡片三层模板（App.axaml 基础）→ 2. ToggleSwitch 动画模板 → 3. 导航图标
→ 4. 强调色选色器 → 5. SettingsView DataTemplate 分区重构
每步后跑 App 构建 0 错误（无测试覆盖 UI，真机验收在 #214）。
