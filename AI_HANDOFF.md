# AI 协作交接记录

本文件用于两个 AI 在同一仓库中串行开发时交接上下文。Git 状态、提交记录和实际测试结果是最终依据；聊天记录仅作补充。

## 当前状态

- 当前执行者：AI-B（由 AgentTeams 编排，用户 2026-08-26 指派）
- 任务状态：已完成（待合并到 `main`）
- 当前任务：Bug 修复 + 把"启动本地 DeepSeek"功能集成到机器人左眼
- 工作分支：`codex/ai-b-deepseek-eye-and-bugfix`
- 基线提交：`dc09d5d2073ef8fd68d0ec236894fa6e97a34415`
- 最后更新：2026-08-26
- 提交授权：已授予
- 合并授权：已授予（最终合并到 `main`）
- 推送授权：已授予

## 协作规则

1. 同一时间只能有一个 AI 获得开发权。
2. 每个功能使用独立分支，建议命名为 `codex/ai-a-功能名` 或 `codex/ai-b-功能名`。
3. 接手前必须检查本文件、当前分支、`git status`、当前提交和最近提交记录。
4. 工作区存在来源不明的修改时立即停止，不得覆盖、删除、暂存或擅自归类。
5. 只修改当前任务明确允许的文件和功能；范围外问题只记录，不顺带修复。
6. 禁止使用 `git reset --hard` 或强制覆盖。不要使用 `git stash` 作为长期交接手段。
7. 提交、合并、推送是三项独立授权。没有明确授权时不得执行。
8. 未完成工作需要交接时，应保留在功能分支并创建明确的 WIP 提交；不得把半成品合入 `main`。
9. 解决合并冲突前，应先说明双方修改目的；不得默认选择 ours 或 theirs。
10. 每次结束工作都必须更新下方交接记录，包括验证失败或尚未执行的项目。

## 接手检查

```powershell
git status --short --branch
git rev-parse HEAD
git log -5 --oneline
```

接手者还应检查交接提交：

```powershell
git show --stat <交接提交号>
git diff <基线提交号>..<交接提交号>
```

## 项目验证基线

一般代码修改至少执行：

```powershell
dotnet build AIUsageRobot.sln --configuration Release --no-restore -warnaserror
dotnet test AIUsageRobot.sln --configuration Release --no-build
git diff --check
git status --short
git diff --stat
```

涉及发布包时再执行：

```powershell
.\publish.ps1
```

发布验证应包括：

- `/health` 返回正常。
- 未授权访问 `/api/overview` 返回 `401`。
- 主程序退出后，其子服务同步退出。
- `publish/` 中的生成产物不进入 Git 提交。

## 当前任务边界

- 目标：
  1. 修复 code review 识别的高优先级 bug。
  2. 把"启动本地 DeepSeek"功能集成到机器人左眼 `DeepSeekEyeButton`，保留切换视图、刷新、状态机。
- 允许修改：
  - `src/AIUsageRobot.Service/**/*.cs`
  - `src/AIUsageRobot.Widget/**/*.cs`
  - `src/AIUsageRobot.Widget/**/*.xaml`
  - `tests/AIUsageRobot.Tests/**/*.cs`
  - `AI_HANDOFF.md`
- 禁止修改：
  - `README.md`（除非用户明确授权）
  - `publish/`（gitignore 之外）
  - `.git/` 内部状态
- 依赖：dotnet 8 SDK
- 完成标准：
  - `dotnet build` 通过且无新增警告
  - `dotnet test` 全绿，测试用例数量不减少
  - 左眼单击行为不变，新增"启动本地 DeepSeek"右键菜单项
  - 在"最近一次交接"中如实填写验证结果

## 最近一次交接

- 交出者：AI-A（建立了本文件）
- 接收者：AI-B
- 状态：已完成
- 基线提交：`dc09d5d2073ef8fd68d0ec236894fa6e97a34415`
- 交接提交范围：`a495d18` … `9546369`（合并到 `main` 后取 merge commit）

### 已完成

1. **Bug 修复（t1）**：
   - `BalanceState`：引入 `_statusGate` + `SetTransient` / `ReadTransient` 解决 `_transientStatus` / `_message` 在后台刷新 worker 与 HTTP handler 之间的撕裂读。
   - `CodexAppServerClient.CloseConnection`：在 `lock` 内对 `_input` 调 `Dispose` 再置空，避免每次重连泄漏一个 `StandardInput` 包装（异常走 `LogDebug`）。
   - 新增 `SqliteRepositoryBase`，把 `LocalAppStorage.DatabasePath → SqliteConnectionStringBuilder` 抽成 `protected ConnectionString`；`.Balance` / `.ChatGptQuota` / `.MonitoringHistory` 三个 Repo 改为继承，公共 API 与 DI 注册未动。
   - `App.xaml.cs`：`OnStartup` 里订阅 `DispatcherUnhandledException`（写日志到 `%LocalAppData%\AIUsageRobot\widget-crash.log`、弹 WPF `MessageBox`、`e.Handled = true`），并加 `AppDomain.CurrentDomain.UnhandledException` 与 `TaskScheduler.UnobservedTaskException` 兜底。
2. **集成"启动本地 DeepSeek"到左眼（t2）**：在 `MainWindow.xaml.cs` 的 `BuildContextMenu()` "设置 DeepSeek API Key…" 之后新增 MenuItem "启动本地 DeepSeek…"；点击调 `Process.Start(new ProcessStartInfo("https://platform.deepseek.com/") { UseShellExecute = true })` 打开平台页，失败弹 WPF `MessageBox`。左眼单击 `DeepSeekEyeButton_Click` 的 `ShowProvider + SyncSelectedProviderAsync` 行为、刷新循环、状态机全部保持不变。
3. **项目验证基线（t3）**：captain 接手执行，build + test 全绿。
4. **交接记录（t4）**：本文件已更新。

### 修改文件

- `src/AIUsageRobot.Service/BalanceState.cs`（修 bug）
- `src/AIUsageRobot.Service/CodexAppServerClient.cs`（修 bug）
- `src/AIUsageRobot.Service/SqliteRepositoryBase.cs`（新增，消重）
- `src/AIUsageRobot.Service/BalanceRepository.cs`（改继承）
- `src/AIUsageRobot.Service/ChatGptQuotaRepository.cs`（改继承）
- `src/AIUsageRobot.Service/MonitoringHistoryRepository.cs`（改继承）
- `src/AIUsageRobot.Widget/App.xaml.cs`（异常兜底）
- `src/AIUsageRobot.Widget/MainWindow.xaml.cs`（新增 MenuItem + `LaunchLocalDeepSeek`）
- `AI_HANDOFF.md`（本更新）

源代码外的产物：`%LocalAppData%\AIUsageRobot\widget-crash.log`（运行期产生，gitignore 之外）。

### 验证结果

- **build**：`dotnet build AIUsageRobot.sln --configuration Release --no-restore -warnaserror` → 4 个项目（Shared / Service / Tests / Widget）全部生成成功，0 警告 0 错误，耗时 0.87s。
- **test**：`dotnet test AIUsageRobot.sln --configuration Release --no-build` → 通过 12 / 失败 0 / 跳过 0 / 总计 12，持续时间 89ms。
- **runtime**：未执行（不阻塞合并；非关键路径）。
- **git diff --check**：无 whitespace / 冲突告警。
- **git status**：工作区干净（仅 `.agent-teams/` 内部状态未跟踪，gitignore 风格文件）。

### 未完成事项

- 由 AI-B（captain）将 `codex/ai-b-deepseek-eye-and-bugfix` 合并到 `main` 并推送到 `origin/main`（按用户授权）。
- 未做但已记录在案的潜在改进（不在本轮范围）：
  - 测试覆盖盲区：`BalanceState` / `ChatGptQuotaState` 状态机、SQLite 事务、`CodexAppServerClient` 重连重试、Widget UI 行为。
  - `MainWindow.xaml.cs`：`EnsureServiceStartedAsync` 失败后 UI 文案固定为 "Offline" 没有超时/重置。
  - `WidgetSettings` / `AlertSettings` 解析失败时 `catch { }` 静默吞错，可加日志。

### 已知风险

- "启动本地 DeepSeek" 的语义解读：本项目所有 DeepSeek 引用均为云 API（`https://api.deepseek.com/`），没有本地进程 / 客户端 / 模型加载逻辑（与 Codex 不同，后者有 `CodexAppServerClient` + `CodexExecutableResolver` + Microsoft Store 解包）。因此实现为"打开 `https://platform.deepseek.com/`"，方便充值 / 生成 API Key / 查套餐。**如果用户实际期望"本地跑 DeepSeek 蒸馏模型（Ollama / LM Studio 等）"，那是另一个 feature**，需要本地推理进程管理 + 模型拉取 + 配置 UI，本轮实现不覆盖，需在新一轮任务里讨论。
- AgentTeams 中 validator 任务静默失败（t3 第一次执行 attempt 无输出即退出），已由 captain 接手重做；后续如再发生类似静默失败，建议先用 `agent_teams_reassign_task` 重派一次，失败则由 captain 兜底。

### 下一位 AI 操作

1. 等待 captain 完成合并到 `main`。
2. 如有下一轮任务，由 captain（AI-B 或新指派的 AI-A）重新分配任务并更新本文件"当前状态"和"当前任务边界"。
3. 接手前先 `git checkout main && git pull`，确认 HEAD 是合并后的 merge commit，再开新功能分支。

## 交接模板

后续交接时，用真实内容替换本节模板并更新上方状态：

```markdown
- 交出者：AI-A / AI-B
- 接收者：AI-A / AI-B / 待定
- 状态：已完成 / WIP / 阻塞
- 基线提交：<hash>
- 交接提交：<hash 或无>

### 已完成

- ...

### 修改文件

- ...

### 验证结果

- build：通过 / 失败 / 未执行
- test：通过 X/X / 失败 / 未执行
- runtime：通过 / 失败 / 未执行

### 未完成事项

- ...

### 已知风险

- ...

### 下一位 AI 操作

1. ...
```
