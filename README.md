# AI Usage Robot

AI Usage Robot 是一个 Windows 桌面常驻机器人组件，监控 ChatGPT Plus / Pro 账号的 Codex 配额与 DeepSeek API 余额。

## 已实现

- `AIUsageRobot.Service`：仅监听 `127.0.0.1:17860` 的独立 ASP.NET Core 进程。
- API Key 仅保存于 Windows Credential Manager，不写入 SQLite、配置或日志。
- 使用 DeepSeek 官方 `GET /user/balance` 接口，每 5 分钟刷新，401/403 不重试。
- SQLite 只保存余额、币种、可用状态和 UTC 更新时间。
- 本地 `/api/*` 使用当前 Windows 用户 DPAPI 保护的随机 Bearer Token。
- `AIUsageRobot.Widget`：透明、无边框、可拖动、置顶的原创分层矢量 WPF 机器人；默认窗口约为 `193×216`。造型采用独立双目舱、分段颈、收腰机身、两段式手臂和机械履带。
- Widget 支持离线、Unknown、Fresh、Stale、Unavailable、AuthError 状态，右键可设置 API Key 或手动刷新。
- 位置会保存到 `%LOCALAPPDATA%\AIUsageRobot`，并在多显示器虚拟桌面范围内恢复。
- Service 维持本机 Codex app-server 长连接，通过 `account/rateLimits/read` 查询额度，并监听 `account/rateLimits/updated` 更新事件；不读取浏览器 Cookie、Session Token 或聊天内容。
- 腹部屏幕只保留 Codex / DeepSeek 两个视图；不自动轮播、不响应滚轮。左眼激活为蓝色并将左臂向上旋转 180°，显示 DeepSeek；右眼激活为红色并将右臂向上旋转 180°，显示 Codex；未激活眼睛为黑色。
- Codex 配额保存计划类型、剩余百分比、周期、重置时间、采集时间和数据源版本。
- 配额数据按 `<15 分钟`、`15 分钟～24 小时`、`>24 小时` 分别显示 Fresh、Stale、Unavailable。
- Codex Service 启动时立即查询，收到 app-server 更新事件时重新查询，并每 5 分钟主动校准一次。
- DeepSeek Service 启动时立即查询官方余额接口，之后按固定 5 分钟周期刷新；网络请求耗时不会累积到下一个周期。
- 点击左眼切换 DeepSeek 时立即刷新官方余额；点击右眼切换 Codex 时立即调用本机 app-server 查询额度。

## 运行

先启动服务，再启动 Widget：

```powershell
dotnet run --project src/AIUsageRobot.Service
dotnet run --project src/AIUsageRobot.Widget
```

首次运行时右键机器人，选择“设置 DeepSeek API Key…”。

## Codex 要求

- 本机需要存在已经登录同一 ChatGPT Plus / Pro 账号的 Codex CLI。
- 可在 `appsettings.json` 的 `Codex:ExecutablePath` 指定独立 CLI 路径。
- Microsoft Store 版 Codex 的内置 CLI 会在首次启动时复制到 `%LOCALAPPDATA%\AIUsageRobot\codex-runtime` 后运行，以绕过 WindowsApps 对外部进程的启动限制。
- 浏览器扩展已退出主同步链路，不再要求打开 ChatGPT 网页。

## 验证

```powershell
dotnet test AIUsageRobot.sln
```

后续阶段将按方案继续加入 DeepSeek Proxy/usage、Pricing、托盘与设置面板。
