# CurseForge API 代理（Cloudflare Worker 免费档）

> 用途：api.curseforge.com 直连在国内网络不稳时（h2 挂起/抖动/超时），用 Cloudflare Worker
> 做一层转发。**不是必须**——HTTP/1.1 修复后官方直连大多数时候已稳定，这是备胎方案。

## 原理

```
启动器 → Cloudflare Worker（你的域名）→ api.curseforge.com（官方）
              ↑ 国内可达            ↑ 海外节点间（稳）
```

启动器把 API 请求发到你的 Worker，Worker 原样转发给官方并回传结果。
你的 key 仍然只存在启动器本地（DPAPI 加密），Worker **不存 key**（透传请求头）。

## 免费档限制（够用）

| 项 | 免费档 |
|---|---|
| 请求数 | 10 万次/天（个人自用绰绰有余） |
| CPU | 10ms/请求（纯转发够） |
| 费用 | 0 元 |

## 前提：一个域名（必须）

`*.workers.dev` 子域**国内大概率不可达**——Worker 必须绑**自定义域名**才在国内稳定可用：

- 买一个便宜的域名（.xyz / .top 之类，10-50 元/年）
- 域名注册商处把 DNS 改到 Cloudflare（添加站点 → 免费套餐 → 按提示改 NS 记录）
- 生效后域名由 Cloudflare 托管

## 部署步骤

1. 注册 Cloudflare 账号（邮箱即可）→ 把域名接入（如上）
2. 控制台 → **Workers & Pages** → **创建** → **Worker** → 起名（如 `cf-api`）
3. 粘贴下面代码 → **部署**

```js
// cf-api Worker：CurseForge API 转发代理（透传 X-Api-Key，不存 key）
const UPSTREAM = "https://api.curseforge.com";

export default {
  async fetch(request, env, ctx) {
    const url = new URL(request.url);
    // 只代理 /v1/* 路径，其余拒绝
    if (!url.pathname.startsWith("/v1/")) {
      return new Response("not found", { status: 404 });
    }
    // OPTIONS 预检（浏览器场景用；启动器走 HttpClient 不需要，留着无害）
    if (request.method === "OPTIONS") {
      return new Response(null, {
        headers: { "Access-Control-Allow-Origin": "*", "Access-Control-Allow-Headers": "*" },
      });
    }
    try {
      const upstream = new URL(url.pathname + url.search, UPSTREAM);
      const resp = await fetch(upstream, {
        method: request.method,
        headers: request.headers, // 含 X-Api-Key（透传）
        body: ["GET", "HEAD"].includes(request.method) ? null : request.body,
      });
      return new Response(resp.body, {
        status: resp.status,
        headers: { "Access-Control-Allow-Origin": "*", "Content-Type": resp.headers.get("Content-Type") ?? "application/json" },
      });
    } catch (err) {
      return new Response("upstream error: " + err.message, { status: 502 });
    }
  },
};
```

4. **绑域名**：Worker 详情 → **设置** → **域和路由** → **添加自定义域** → 填 `cf-api.你的域名.com` → 等 CF 自动签发证书（几分钟）
5. 验证：浏览器/命令行访问 `https://cf-api.你的域名.com/v1/mods/search?gameId=432&searchFilter=jei&pageSize=1`（不带 key 应返回 403 而不是超时——说明转发通了）

## 启动器填法

设置 → 下载 → **CF API 地址覆盖** → 填：

```
https://cf-api.你的域名.com/v1
```

保存即生效（动态读取，无需重启）→ 回到设置页点「检查」验证 200。

## 风险提示

- **别公开分享你的 Worker 地址**——别人能蹭你的免费额度（10 万次/天烧完就 429）
- key 不透传?——透传的，但 Worker 日志不记请求头；不放心可后续改成 key 注入式（Worker 里写死你的 key，启动器端留空）——不推荐（key 进 Worker 源码等于云端明文）
- Cloudflare 账号被风控/封号时 Worker 会失效——这是额外依赖，直连能用时优先直连
