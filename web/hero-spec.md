# Hero 区改版设计规格 — Lattice 启动器官网

状态：**规格文档**（结构/数值/节奏），供人工实现，不包含完整代码。
涉及文件：`index.html`（hero 结构替换）、`styles.css`（Hero 区块重写）、`script.js`（删一处选择器）。
实现后自检：900px 折叠、prefers-reduced-motion 降级、文字对比度、无新增循环动画泄漏到其他区块。

---

## 0. 设计立场

双栏官方排版 + 晶格脉络动画 + 透光背景。动效只发生在**右栏 SVG** 与**背景 ::before 层**；
左栏文案零动效（唯一例外：整体一次性 reveal）。所有节奏取慢取疏——动画是点睛，不是炫技。

---

## 1. 双栏 Grid 布局

### 桌面（≥901px）
- `.hero` 保留：`min-height: 100vh`、flex 垂直居中、`overflow: hidden`、`position: relative`
- `.hero-inner` 从 flex-column 改为 grid：
  - `grid-template-columns: repeat(12, 1fr)`
  - `gap: clamp(32px, 4vw, 64px)`
  - `align-items: center`
  - `.hero-text`（左栏）：`grid-column: 1 / span 6`
  - `.hero-lattice`（右栏）：`grid-column: 7 / span 6`
- 垂直对齐：两栏均随 hero 垂直居中；`text-align: center` 只作用于左栏内容

### 断点 900px（新增 `@media (max-width: 900px)`）
- grid 塌成 1 列：`.hero-text` 在前、`.hero-lattice` 在后（正常文档流）
- `.hero-lattice`：`max-width: 480px; margin: 56px auto 0`
- hero padding 由 `120px 24px 80px` → `112px 24px 64px`
- 其余既有断点（1024 / 640）不动

### 间距基准
- 栏间距 ≥ 48px（见 gap 值）；左栏内部垂直节奏 16 / 24 / 32 / 48（见 §2）

---

## 2. 左栏排版规格（官方化）

### 结构（自上而下，全部居中于栏轴）
`hero-badge → hero-title → hero-sub → hero-cta → hero-stats`

### 层级 / 字号（复用现有 token，新增 1 个）

| 元素 | 字号 | 字重/字体 | 行高 | 颜色 | 上间距 |
|---|---|---|---|---|---|
| badge | `--t-caption` (.75rem) | 600 mono | — | `--c-accent` | — |
| title | **新 token `--t-hero-l`: `clamp(2.25rem, 3.8vw, 3.5rem)`** | 700 display | 1.15 | `--c-text` | 24px |
| sub | `--t-hero-sub` (clamp(1rem, 2vw, 1.25rem)) | 400 body | 1.7 | `--c-text-2` | 16px |
| cta | `.btn` 现状（`--t-small` 600） | — | — | — | 32px |
| stats | 见下 | — | — | — | 48px |

- **title 不沿用 `--t-hero`（4.5rem 在 536px 栏内过满）**：14 字标题在 3.5rem 下折 2 行（7+7），单行约 392px，栏内有余量。`letter-spacing: -0.01em`。
- **字重 700 而非 900**：Space Grotesk 无 CJK 字形，回退 Noto Sans SC 时 900 为合成加粗，官方感取 700 更稳。
- sub：`max-width: 26em`，居中。
- **对称处理**：badge/title/sub/cta 全部居中；CTA 双按钮同尺寸同内边距；stats 以栏轴镜像排布（分隔线对称）。

### stats 排布（克制化）
- 结构保留 4 项 `.stat`（100+ / 4 / 24 / 185MB 不变）
- `.stat-num`：**从 `--t-data-lg` 降一级到 `--t-data` (1.25rem)**；mono 700；`--c-accent`；加 `font-variant-numeric: tabular-nums`（数字等宽对齐）
- `.stat-label`：`--t-caption`；`--c-text-3`
- 分隔线：`.stat + .stat { border-left: 1px solid var(--c-border); padding-left: 24px; }`，整行 `gap: 24px`
- ≤640px：分隔线移除、gap 降 16px、`flex-wrap` 保持现状

---

## 3. 晶格动画规格（右栏 SVG）

### 3.1 画布
- `viewBox: 0 0 560 520`；`aria-hidden="true"`
- 外层 `.hero-lattice` 挂 `[data-reveal]`（复用现有 IO 一次性上移淡入，`transition-delay: .15s`，不用新 JS）
- 尺寸：`width: 100%; height: auto`（右栏 536px 时渲染约 500×465）

### 3.2 节点分布（三角晶格 = 晶格语义）
- 参数：`sx = 56`，`sy = 48.5`（56·√3/2）；行 `j = 0..7`，`y = 270 + (j − 3.5) × 48.5` → 100.25 … 439.75
- 偶行（j 偶）：9 节点，`x = 56 + 56k`（56 … 504）
- 奇行（j 奇）：8 节点，`x = 84 + 56k`（84 … 476）
- **节点总数 68**：`r = 2.5`，`fill: rgba(45,212,191,.30)`，无描边
- **连线**：每节点连东邻 + 下排两个对角邻（三角铺砌），全图约 190 段，合成**单条 `<path>`**（M/L 段），`stroke: rgba(45,212,191,.14)`、width 1、**无动画**（静态基底）
- 实现提示：190 段坐标按公式一次性生成（任意脚本输出 path 字符串），不要手写

### 3.3 层级与 class 划分（SVG 内自下而上）
1. `.lat-grid` — 全部静态连线（单 path）
2. `.lat-pulses` — 3 条脉冲流动线
3. `.lat-nodes` — 77 个静态节点 circle
4. `.lat-core` — 核心节点 + 涟漪环
5. `.lat-hi` — 2 个高亮节点（各带涟漪环）

### 3.4 核心与高亮节点
- **核心 `.core`**：位置 (308, 245.75)（奇行 j=3 正中心），`r = 5`，`fill: var(--c-accent)`；底衬圆 `r = 10`，`fill: rgba(45,212,191,.10)`
- **高亮 `.hi`**：A (168, 100.25) 与 B (420, 439.75)（左上/右下对角呼应），`r = 3.5`，`fill: var(--c-accent)`
- **涟漪环 `.ripple`**：`r = 6`、`fill: none`、`stroke: var(--c-accent)`、width 1.5；动画 = `transform: scale(1 → 3.8)` + `opacity: .5 → 0`
- 硬性要求：`.ripple` 必须 `transform-box: fill-box; transform-origin: center`；**只动 transform/opacity，绝不动画 r/属性值**

### 3.5 脉冲路径（3 条，流向 = 核心 → 高亮）
全部 `pathLength="100"`、`stroke: var(--c-accent)`、width 1.5、`opacity: .55`、`dasharray: 8 92`；
动画 = `stroke-dashoffset: 100 → −100`（一条 dash 匀速行进，周期 100 无缝循环）

| 路径 | 走向 | 路径数据（参考值） |
|---|---|---|
| P1 | 水平贯穿 row j=2 | `M 56 197.25 L 504 197.25` |
| P2 | 核心 → 左上高亮 A | `M 308 245.75 L 280 197.25 L 224 197.25 L 196 148.75 L 168 100.25` |
| P3 | 核心 → 右下高亮 B | `M 308 245.75 L 336 294.25 L 392 294.25 L 420 342.75 L 448 391.25 L 420 439.75` |

（P2/P3 每段均为真实晶格键、端点落在节点上；脉冲终到的高亮点与涟漪呼应——"流向"语义）

### 3.6 节奏与延迟（仅此处允许循环，全站唯一例外）

| 动画 | 时长 | 缓动 | 延迟 | 说明 |
|---|---|---|---|---|
| P1 流动 | 4.5s | linear | 0 | dash 匀速过场 |
| P2 流动 | 5.5s | linear | 1.2s | 与 P1 相位错开 |
| P3 流动 | 6.5s | linear | 2.4s | 最长最慢 |
| 涟漪环（周期 4.2s，单程 3.6s） | 3.6s | ease-out | 核心 0 / 2.1s；A 1.4 / 3.5s；B 0.7 / 2.8s | 同周期内最多 3 环并发 |

- 入场（一次性）：`.hero-lattice[data-reveal]` 复用现有 `.6s var(--ease-out)` 上移淡入；循环动画无需等待入场（父级 opacity 0 期间不可见，无闪跳）
- **例外声明**：全站规则"iteration-count: 1、无循环呼吸"在本 SVG 的 `.lat-pulses` / `.ripple` 与背景层豁免（拍板的方向）；**任何其他元素不得新增循环动画**

### 3.7 prefers-reduced-motion 降级
现有全局块已压平所有动画，追加（写在该块内）：
- `.lat-pulses path`：`animation: none; stroke-dashoffset: 0`（静态虚线完整显示）
- `.ripple`：`display: none`
- 结果：静态晶格图（点 + 线 + 核心）原样呈现，零运动

---

## 4. 透光背景规格（::before 层 + 半透明面纱）

### 4.1 层级（自后向前）
`body canvas (#0D1017)` → `.hero` 自身 background（半透明面纱） → `.hero::before`（z-index 0：网格线 + 渐变光斑） → `.grain::after`（z-index 1，opacity .04 overlay，原样保留） → `.hero-inner`（z-index 2）

### 4.2 面纱（`.hero` background，替换现有两个静态 radial-gradient）
`linear-gradient(180deg, rgba(13,16,23,.62) 0%, rgba(13,16,23,.48) 45%, rgba(13,16,23,.62) 72%, rgba(13,16,23,.97) 100%)`
- 中段最透 → 光斑最亮处透出；底部 .97 收口 → 与 #features 之间无硬边，向下"溶入"页面
- 对比度：最亮光斑下合成底色约 #1A2A2E，`--c-text` (#E8EAF0) 对比 ≥ 9:1，达标

### 4.3 ::before 结构（网格 + 光斑一体，background 多层）
`background-image`（5 层，自上而下）：
1. 横网格：`linear-gradient(to right, rgba(255,255,255,.05) 1px, transparent 1px)`
2. 纵网格：`linear-gradient(to bottom, rgba(255,255,255,.05) 1px, transparent 1px)`
3. 主光斑（68% 30%，晶格背后）：`radial-gradient(ellipse 55% 45% at 68% 30%, rgba(45,212,191,.13) 0%, transparent 60%)`
4. 次光斑（22% 72%，左下）：`radial-gradient(ellipse 40% 35% at 22% 72%, rgba(45,212,191,.07) 0%, transparent 55%)`
5. 冷灰微光（84% 82%，右下，加深度）：`radial-gradient(ellipse 30% 22% at 84% 82%, rgba(148,163,184,.04) 0%, transparent 60%)`

`background-size: 56px 56px, 56px 56px, 100% 100%, 100% 100%, 100% 100%`
（网格 56px 与晶格 sx=56 同构，前后呼应；repeating 梯度平移天然无缝）

### 4.4 动画节奏（两条动画叠在同一 ::before，comma 合并）
- **漂移 `hero-drift`**：`background-position` 从 `0 0, 0 0, 68% 30%, 22% 72%, 84% 82%` → `24px 18px, 18px 24px, 71% 32%, 17% 67%, 88% 77%`；**20s** ease-in-out infinite alternate（整层含网格缓慢平移，光斑 ±3~6% 漂移）
- **呼吸 `hero-breathe`**：`opacity: 1 ↔ .8`；**9s** ease-in-out infinite（幅度 ±10%，以察觉不到"呼吸感"为界）
- reduce 降级：`animation: none`（停在 from 态，静态网格 + 光斑照常显示）

### 4.5 实现提示
- 网格如需更聚焦可加 `mask: radial-gradient(...)` 让线在四边渐隐——**默认不开**（mask 会连光斑一起遮，若开则光斑必须移到 `.hero` 自身 background 或独立元素）
- **勿用 blur 滤镜伪元素做光斑**（大区域 blur + background-position 动画 = 每帧重绘）；径向渐变自带软边即可

---

## 5. 现有元素去留清单

### 删除
1. `.launch-rail` 整套 — HTML 38–56 行、CSS 194–213、script.js 第 15 行选择器 `'.launch-rail'`、reduce 块 394 行的 `.launch-rail *` 规则
2. `.hero-mockup` + `.mock-chrome` / `.mock-dot` / `.mock-title` — HTML 58–64 行、CSS 174–192。**去向：直接删除**，截图已由 #gallery 第一张全宽大图（01-home.png）承载，无信息损失
3. `.hero` 现有两个静态 radial-gradient（CSS 133–136）— 由 §4 面纱 + ::before 替换
4. `.hero-inner` 的 flex-column 居中 + 统一 gap（CSS 143–147）— 改为 §1 grid

### 保留（零改动）
1. `.hero-badge` 样式原样
2. `.btn` / `.btn-primary` / `.btn-ghost` 全套
3. `.grain::after` 纹理（z-index 1 不变，叠在动效层之上做统一纹理）
4. `[data-reveal]` 一次性 reveal 系统 + script.js 的 IO（只删 `'.launch-rail'` 选择器，**不新增任何 JS**）
5. `.hero-title` / `.hero-sub` 的字体家族与颜色 token（仅字号调整）
6. `.hero-stats` 的 HTML 结构（仅数字降级 + 分隔线）
7. `prefers-reduced-motion` 全局降级块（追加 §3.7 / §4.4 规则）

### HTML 结构调整
`<header class="hero grain">` 内变为：
`.hero-inner` > 两个子元素（`.hero-text` 包 badge/title/sub/cta/stats + `.hero-lattice[data-reveal]` 包 svg）

---

## 6. 动画纪律（防炫技自检）

- 左栏任何元素不新增动画（唯一例外：整体 `[data-reveal]` 一次性入场）
- 循环动画仅存在于 `.hero-lattice` 内部与 `::before` 背景层，共 2 种语言：**dash 流动**（脉冲）与**涟漪扩散**（scale + fade）— 不用旋转、位移抖动、闪烁
- 所有循环时长 ≥ 3.6s（背景层 ≥ 9s）；**任何 < 1s 的循环动画一律禁止**
- 颜色只用现有 accent 家族（#2DD4BF 及其 .07/.10/.13/.14 透明度变体）+ 白 .05 网格线，不新增色相
- 动画只动 `transform` / `opacity` / `background-position` / `stroke-dashoffset` 四类属性
- 实现完成后走查：900px 折叠、640px stats 换行、reduce 模式三处（网格/涟漪/脉冲）均静态
