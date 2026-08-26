# AI Usage Robot

AI Usage Robot 是一个 Windows 桌面常驻机器人组件，监控 ChatGPT Plus / Pro 账号的 Codex 配额与 DeepSeek API 余额。

> 当前 `main` 分支是**标准版**，不安装、不启动、也不要求本机存在 DSH。需要从机器人左眼启动 DeepSeek Harness 的用户，请使用 [`codex/dsh-edition`](https://github.com/hurricanepilot-ai/AI-Usage-Robot/tree/codex/dsh-edition)。

## 版本选择

| 版本 | Git 分支 | 发布文件 | 适用场景 |
| --- | --- | --- | --- |
| 标准版 | `main` | `AIUsageRobot.exe` | 监控 Codex 配额与 DeepSeek API 余额，不使用 DSH |
| DSH Edition | `codex/dsh-edition` | `AIUsageRobot-DSH.exe` | 需要启动和监控 DeepSeek Harness |

两个版本独立维护，DSH Edition 不合并回 `main`。

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
- Codex 同时保存 5 小时与 7 天额度窗口；机器人腹部优先显示 5 小时剩余量和对应重置时间，详情页保留两个窗口。
- 支持 Codex app-server `account/usage/read`：保存累计 token、峰值日、连续使用天数及最近 31 天每日用量；旧版 app-server 不支持时自动降级，不影响配额同步。
- Codex 与 DeepSeek 每次成功同步都会写入统一 SQLite 历史快照，可通过 `/api/history/codex` 与 `/api/history/deepseek` 查询。
- 配额数据按 `<15 分钟`、`15 分钟～24 小时`、`>24 小时` 分别显示 Fresh、Stale、Unavailable。
- Codex Service 启动时立即查询，收到 app-server 额度更新事件时只重新查询 `account/rateLimits/read`，并每 5 分钟完整校准额度与用量；账号切换后自动完整刷新。
- DeepSeek Service 启动时立即查询官方余额接口，之后按固定 5 分钟周期刷新；网络请求耗时不会累积到下一个周期。
- 点击左眼切换 DeepSeek 时立即刷新官方余额；点击右眼切换 Codex 时立即调用本机 app-server 查询额度。
- 系统托盘支持显示机器人、查看详情、同步全部和退出；不再设置开机自启动，重复启动时只保留一个 Widget 实例。
- Codex 任一周期剩余不高于 20%/10%，或 DeepSeek 余额不高于 10/5 时，通过 Windows 托盘发送分级提醒。
- 点击机器人腹部屏幕打开当前 Provider 的七日趋势面板：Codex 显示每日 Token 柱状图；DeepSeek 根据每 5 分钟余额快照的下降额汇总每日金额用量，充值造成的余额上升不会计为消费。
- 趋势面板、机器人右键菜单和系统托盘均提供“测试 Windows 额度预警”和“预警设置”；阈值保存于当前用户本地配置。
- 额度预警采用双通道：右下角应用预警卡片保证可见，同时尝试发送 Windows 托盘通知；系统关闭通知或启用专注助手时仍有应用内提示。
- 发布版只有一个 `AIUsageRobot.exe`。双击时显示机器人并以同一程序的隐藏服务模式承载本地 API；退出机器人时一并结束后台服务。

## 运行

生成单文件 Windows 应用：

```powershell
.\publish.ps1
```

完成后双击 `publish\win-x64\AIUsageRobot.exe` 即可启动，不会随 Windows 自动运行。

首次运行时右键机器人，选择“设置 DeepSeek API Key…”。

## Codex 要求

- 本机需要存在已经登录同一 ChatGPT Plus / Pro 账号的 Codex CLI。
- 如需指定独立 CLI，可设置当前用户环境变量 `CODEX_EXECUTABLE`。
- Microsoft Store 版 Codex 的内置 CLI 会在首次启动时复制到 `%LOCALAPPDATA%\AIUsageRobot\codex-runtime` 后运行，以绕过 WindowsApps 对外部进程的启动限制。
- 无需安装浏览器扩展，也不要求打开 ChatGPT 网页。

## 验证

```powershell
dotnet test AIUsageRobot.sln
```

> `account/rateLimits/read` 与 `account/usage/read` 属于本机 Codex app-server 协议，不是 OpenAI 公网 API。项目按当前本机 schema 做兼容解析，并在方法不可用时降级。

## 项目地址

https://github.com/hurricanepilot-ai/AI-Usage-Robot
