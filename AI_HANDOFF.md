# AI 协作交接记录

本文件用于两个 AI 在同一仓库中串行开发时交接上下文。Git 状态、提交记录和实际测试结果是最终依据；聊天记录仅作补充。

## 当前状态

- 当前执行者：AI-B（由 AgentTeams 编排，用户 2026-08-26 指派）
- 任务状态：执行中
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
  1. 修复 code review 识别的高优先级 bug（`BalanceState` 字段线程安全、`App` 缺 `DispatcherUnhandledException`、`CodexAppServerClient.CloseConnection` 漏 dispose 等）。
  2. 把"启动本地 DeepSeek"功能集成到机器人左眼 `DeepSeekEyeButton`，并保留切换视图、刷新、状态机的现有行为。
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
  - 左眼交互：单击切换 DeepSeek 视图并刷新；如集成"启动本地 DeepSeek"，应给出可观察行为（启动进程 / 打开网页 / 调起服务等）
  - 在"最近一次交接"中如实填写验证结果

## 最近一次交接

- 交出者：AI-A（之前一轮建立了本文件）
- 接收者：AI-B
- 状态：执行中
- 基线提交：`dc09d5d2073ef8fd68d0ec236894fa6e97a34415`
- 交接提交：待 4 步流程完成后填写

### 已完成

- AI-B 接手检查：HEAD 与基线一致，工作区仅含 staged 的 `AI_HANDOFF.md`。

### 修改文件

- `AI_HANDOFF.md`（本更新，标记当前执行者和任务）

### 验证结果

- 文档检查：通过，文件位于仓库根目录。
- 构建：未执行（仅新增文档）。
- 测试：未执行（仅新增文档）。
- 运行验证：未执行（仅新增文档）。

### 未完成事项

- 执行 4 步工作流：bug 修复 → 启动本地 DeepSeek 集成到左眼 → 测试 → 记录。
- 最终合并 `codex/ai-b-deepseek-eye-and-bugfix` 到 `main`。

### 已知风险

- "启动本地 DeepSeek"具体语义（启动服务进程 / 打开网页 / 调起本地模型）需要 worker 读代码上下文并选择最合理解释，必要时向用户确认。
- AgentTeams 中并行 worker 可能产生文件编辑冲突；任务依赖已串行化以规避。

### 下一位 AI 操作

1. 按依赖顺序领取任务：先 bug 修复，再 DeepSeek 左眼集成，再测试，最后更新本文件。
2. 所有代码变更必须先在 `codex/ai-b-deepseek-eye-and-bugfix` 分支上提交，再汇报完成。
3. 测试失败时不要吞错；在交接记录中如实填写失败用例和原因。
4. 完成全部 4 步后由 AI-B（captain）合并到 `main` 并推送。

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
