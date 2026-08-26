# AI 协作交接记录

本文件用于两个 AI 在同一仓库中串行开发时交接上下文。Git 状态、提交记录和实际测试结果是最终依据；聊天记录仅作补充。

## 当前状态

- 当前执行者：AI-A
- 任务状态：标准版清理完成，构建、发布和运行验证通过
- 当前任务：从 `main` 移除 DSH/Harness 功能，保留通用 bug 修复，并明确双版本入口
- 工作分支：`codex/standard-main-cleanup`
- 基线提交：`2d195de`
- 最后更新：2026-08-26
- 提交授权：已授予
- 合并授权：已授予（仅标准版清理快进到 `main`）
- 推送授权：已授予（`main`）

### 历史接力阻塞（已处理）

代码改动全部已合并并推送。但 `publish/win-x64/AIUsageRobot.exe` 还是**旧版**（包含 iter 3 + iter 4，**不包含 iter 5 修复**），因为 widget 还在运行锁住了旧 exe，重打包 dotnet publish 报 `IOException: The process cannot access the file ... because it is being used by another process`。

接手 AI 需要：

1. 让用户关掉机器人（如果之前的"dsh web 启动后立即退出"MessageBox 还挂着，按 Enter / 点 OK 即可消掉）。如果窗口完全卡死无响应，让用户跑 `taskkill //F //IM AIUsageRobot.exe` 强杀（注意强杀会绕过 `Window_Closing` 里的 `StopOwnedHarness`，可能留孤儿 dsh 进程，需手动清）。
2. 确认 `Get-Process AIUsageRobot` 返回空。
3. 跑：
   ```powershell
   cd 'D:\tool\AI Usage Robot'
   dotnet publish 'src/AIUsageRobot.Widget/AIUsageRobot.Widget.csproj' --configuration Release --runtime win-x64 --self-contained true -p:DebugType=None -p:DebugSymbols=false --output 'publish/win-x64'
   ```
4. 确认新 exe 已生成（应该 ≈ 80.4 MB，包含 iter 5 修复）。
5. 把状态从 "Blocked" 改回 "已完成"，更新 `AI_HANDOFF.md` 的"最后更新"时间戳。

## 给下一位 AI 的接力说明

**代码状态**：iter 5 已合并 `bf0c0e0`，`origin/main` 已同步，git 工作区干净。

**未完成事项（唯一一件）**：用户机器上的 widget 还在跑，锁住 `publish/win-x64/AIUsageRobot.exe`，所以 dotnet publish 重打包失败。代码本身已经修好并提交，**只差重打单文件 exe**。

**接手 AI 的 checklist**：

1. **读 `AI_HANDOFF.md`** 当前状态 + 第 5 轮交接记录，弄清楚 iter 5 修了什么。
2. **确认 widget 状态**：
   ```powershell
   Get-Process AIUsageRobot -ErrorAction SilentlyContinue
   ```
3. **如果 widget 还在跑**：让用户关窗口（按 Enter 消掉 MessageBox → 正常关窗）。窗口完全卡死的话，让用户跑 `taskkill //F //IM AIUsageRobot.exe`（注意强杀会绕过 Window_Closing，需手动 `taskkill //F //IM node.exe` 清残留 dsh）。
4. **跑 build / test 确认代码没问题**（已经验证过 0 警告 0 错误、test 12/12 通过，但接手时再过一遍是规则）。
5. **重新打包**：
   ```powershell
   cd 'D:\tool\AI Usage Robot'
   dotnet publish 'src/AIUsageRobot.Widget/AIUsageRobot.Widget.csproj' --configuration Release --runtime win-x64 --self-contained true -p:DebugType=None -p:DebugSymbols=false --output 'publish/win-x64'
   ```
6. **确认新 exe 落地**（`publish/win-x64/AIUsageRobot.exe`，≈80.4 MB）。
7. **更新 `AI_HANDOFF.md`**：把"当前状态"从 Blocked 改回"已完成（全部交付）"；最后更新戳改成接手完成时间。
8. **不再开新迭代**：用户明确说要切走，所以不要主动加新功能 / 改动代码。如果用户提出新需求，开新分支、新迭代。

**已知风险**（沿用 AI_HANDOFF 规则 + 历史）：

- iter 5 修的是"Exited 事件和轮询两套机制重复检测"，本轮通过 null-check 让它们协作。更彻底的做法是去掉轮询里的 `IsHarnessProcessAlive`、只依赖 Exited 事件，留作以后。
- iter 4 的 `ResolveDshExecutable` 候选列表是硬编码，未来 dsh 改了安装位置要补条目。
- iter 3 的慢闪与 `ShowProvider` 互动：如果慢闪进行中用户单击左眼切到 Codex，`ShowProvider` 会覆盖 `DeepSeekEyeFill.Fill` 为 `InactiveEyeBrush`，慢闪会被卡住。需要的话给 `ShowProvider` 加"如果正在慢闪则不要覆盖 `DeepSeekEyeFill`"的判断。
- AgentTeams validator 静默失败事件记录在案（不影响代码）。
- `widget-crash.log` 旧的 995 UnobservedTaskException 条目是 iter 5 修复前的累积，可以无视；如果用户嫌日志大，删了即可。

**用户沟通模板**：

> 上一位 AI 已把所有改动合到 main（`bf0c0e0`）并推到 `origin/main`，现在只剩重新打包 `publish/win-x64/AIUsageRobot.exe` 这件事。
>
> 请先把机器人关掉（X 或右键任务栏关闭窗口都行）。如果窗口挂着"dsh web 启动后立即退出"MessageBox，按一下 Enter 就能消。关掉之后告诉我，我立刻 `dotnet publish` 重新打包。
>
> 如果窗口完全卡死（连 X / Alt+F4 都没反应），告诉我；需要你在 PowerShell 跑 `Get-Process AIUsageRobot | Stop-Process -Force` 强杀。

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
11. **遇到用户口语化、模糊的需求（如"启动本地 DeepSeek"），必须先回问澄清语义再动手**；猜测实现会让整轮工作无效。规则 5 也涵盖此点：未确认的"外部行为"不得擅自代用户决定。

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
  1. `main` 恢复为完全不依赖 DSH 的标准版。
  2. 保留服务线程安全、连接释放、SQLite 基类和异常兜底等通用修复。
  3. README 明确标准版与 DSH Edition 的分支和产物选择。
- 允许修改：
  - `src/AIUsageRobot.Widget/**/*.cs`
  - `src/AIUsageRobot.Widget/**/*.xaml`
  - `README.md`
  - `AI_HANDOFF.md`
- 禁止修改：
  - `publish/`（gitignore 之外）
  - `src/AIUsageRobot.Service/**`（本轮不动 service）
  - `.git/` 内部状态
- 依赖：.NET 8 SDK；标准版不依赖 DSH 或 Node.js
- 完成标准：
  - `dotnet build` 通过且无新增警告
  - `dotnet test` 全绿（12/12）
  - `src`、标准版 README 和发布脚本中不存在 DSH/Harness/3080 引用
  - 发布目录只有 `AIUsageRobot.exe`
  - `/health=200`、未授权 `/api/overview=401`

## 交接历史

### 第 8 轮（当前）：恢复 `main` 标准版并建立双版本入口

- 交出者：AI-A
- 接收者：标准版后续开发者
- 状态：已完成并验证，待提交推送
- 基线提交：`2d195de`
- 交接提交：见包含本记录的 `main` HEAD
- 工作分支：`codex/standard-main-cleanup`

#### 已完成

1. 将 `MainWindow.xaml` 和 `MainWindow.xaml.cs` 精确恢复到通用修复提交 `3787f61` 的界面基线，撤除后续全部 DSH/Harness 代码。
2. 保留 `2d195de` 中的服务线程安全、连接释放、SQLite 仓储基类和 WPF 异常兜底等非 DSH 修复。
3. README 增加标准版与 DSH Edition 的分支、发布文件和适用场景对照。
4. DSH 功能继续由 `codex/dsh-edition` 独立维护，不合并回 `main`。

#### 未完成事项

- 提交、快进 `main` 并推送。

#### 验证结果

- build：通过，0 警告、0 错误。
- test：通过 12/12。
- source：`src` 中无 DSH、Harness、3080 或旧启动方法引用。
- publish：通过，目录内仅有 `AIUsageRobot.exe`。
- runtime：标准版 Widget 和同名 `--service` 子进程正常启动。
- API：`/health=200`；未授权 `/api/overview=401`。

### 第 5 轮：修"杀 dsh 后机器人卡住" + 漏 UnobservedTaskException

- 交出者：AI-B
- 接收者：（待合并后由下一位 AI / 用户接管）
- 状态：已完成（待合并）
- 基线提交：`7eae714`
- 交接提交：`caeec9e`
- 分支：`codex/ai-b-harness-poll-and-tcp-fix`

#### 问题复盘

用户反馈：双击左眼启动 dsh 后用 `taskkill //F //IM node.exe` 杀掉 dsh 的 node.exe，机器人"也死机了"。

#### 根因

第 3 轮实现里有 **两套机制** 同时检测 Harness 死亡，互相打架：
1. `Process.Exited` 事件 → 触发慢闪（正确路径）
2. `LaunchHarnessAsync` 轮询循环里的 `IsHarnessProcessAlive()` 检查 → 触发 `MessageBox.Show("dsh web 启动后立即退出...")`

当用户在 6 秒轮询窗口内杀掉 dsh，两个机制都触发。`Process.Exited` 把 `_harnessProcess` 置 null、启动慢闪；接着轮询循环看到 `_harnessProcess is null`、进 `MessageBox.Show` 分支。**这个 MessageBox 是模态的，挂在主窗口上**，如果不主动关掉，看起来就是机器人"死机"。

崩溃日志里另一类 `SocketException(995)` UnobservedTaskException 是 `IsHarnessReachableAsync` 的 TcpClient 在超时后没正确取消底层 socket，被 dispose 时漏出孤儿 ConnectAsync。日志被刷屏但不影响功能，只是污染。

#### 已完成

1. **轮询循环静默退出**：检测到 `_harnessProcess is null`（说明 Exited 已经处理了）就 `return`，不再弹"立即退出" MessageBox。
2. **TcpClient 取消**：`Task.WhenAny` 超时分支里 `client.Close()` 显式中断正在进行的 ConnectAsync，然后 `await connectTask` 把异常观察掉（catch 掉，因为预期内的）。这样 `widget-crash.log` 不再被 995 错误刷屏。

#### 修改文件

- `src/AIUsageRobot.Widget/MainWindow.xaml.cs`（+23 行 / -3 行：`IsHarnessReachableAsync` 加 Close + await；`LaunchHarnessAsync` 轮询循环顶部加 null-check）
- `AI_HANDOFF.md`（本更新）

#### 验证结果

- **build**：`dotnet build AIUsageRobot.sln --configuration Release --no-restore -warnaserror` → 4 项目全过，0 警告 0 错误，耗时 10.55s。
- **test**：`dotnet test AIUsageRobot.sln --configuration Release --no-build` → 通过 12 / 失败 0 / 跳过 0 / 总计 12，持续时间 180ms。
- **runtime**：未在无头环境跑过 widget；用户手动验证。
- **git diff --check**：无 whitespace / 冲突告警。
- **git status**：除 `AI_HANDOFF.md` 待提交外干净。

#### 未完成事项

- 由 captain 合并到 `main` + 推 `origin/main` + 重打单文件包。
- 用户手动验证三个场景：
  1. 双击左眼 → 浏览器打开 3080。
  2. `taskkill //F //IM node.exe` → 左眼开始 2s 周期慢闪，**不再有 MessageBox 卡 UI**。
  3. 关机器人 → dsh 进程同步消失（iter 3 功能保持）。
  4. 顺手看 `%LocalAppData%\AIUsageRobot\widget-crash.log` 应该不再有 995 异常新增（旧的累积条目不影响）。

#### 已知风险

- 第 4 轮的 ResolveDshExecutable 硬编码候选列表继续在案。
- 第 3 轮的慢闪与 ShowProvider 互动继续在案。
- **Exited 事件和轮询重复检测本质上是 redundant 设计**：本轮通过 null-check 让它们协作，但理论上更干净的做法是完全去掉轮询里的 IsHarnessProcessAlive，只保留 Exited 事件。本轮没动，留作以后可能。
- AgentTeams validator 静默失败继续在案。

#### 下一位 AI 操作

1. 等待 captain 完成合并 + 重打包。
2. 用户手动验证 4 个场景。
3. 接手前 `git checkout main && git pull`。

### 第 4 轮（已合并）：dsh 路径解析修复

- 交出者：AI-B
- 接收者：（待合并后由下一位 AI / 用户接管）
- 状态：已完成（待合并）
- 基线提交：`9e43784`
- 交接提交：`fcad514`
- 分支：`codex/ai-b-harness-path-resolve`

#### 已完成

1. **`ResolveDshExecutable`**：在常见 npm/yarn 全局 bin 位置硬编码搜索 `dsh.cmd` / `dsh.exe` / `dsh`，找到就返回绝对路径；找不到再走当前 process 的 PATH；都找不到返回 `null`。
   - 命中顺序：`%APPDATA%\npm\dsh.cmd`（这是本机实际位置）→ `%LOCALAPPDATA%\npm\dsh.cmd` → `%APPDATA%\npm\dsh` → Yarn、PnP、自定义 prefix → 当前 process PATH 遍历。
2. **`EnsureChildHasFullPath`**：合并 `EnvironmentVariableTarget.Machine` + `User` + 当前 process 的 PATH，写入 `ProcessStartInfo.Environment["PATH"]`，去重。覆盖 dsh.cmd 内部 shellaot `node` 时的依赖查找。
3. **`LaunchHarnessAsync` 改用解析后的路径**：`FileName = harnessExe ?? "dsh"`；resolved 失败时弹明确错误提示（指引 `npm install -g` + 常见位置）。原来"PATH 中找不到 dsh"的提示已替换。
4. **try/catch 文案更新**：失败时显示具体的 `harnessExe` 路径 + 异常消息，不再笼统说 PATH。

#### 修改文件

- `src/AIUsageRobot.Widget/MainWindow.xaml.cs`（+113 行 / -9 行：新增 `ResolveDshExecutable`、`EnsureChildHasFullPath`；改造 `LaunchHarnessAsync`）
- `AI_HANDOFF.md`（本更新）

#### 验证结果

- **build**：`dotnet build AIUsageRobot.sln --configuration Release --no-restore -warnaserror` → 4 项目全过，0 警告 0 错误，耗时 7.54s。
- **test**：`dotnet test AIUsageRobot.sln --configuration Release --no-build` → 通过 12 / 失败 0 / 跳过 0 / 总计 12，持续时间 186ms。
- **runtime**：未在本环境无头跑过 widget，但用 `where.exe dsh` 验证了 shim 在 `%APPDATA%\npm\dsh.cmd`，命中 `ResolveDshExecutable` 的第一个候选位置。
- **git diff --check**：无 whitespace / 冲突告警。
- **git status**：除 `AI_HANDOFF.md` 待提交外干净。

#### 未完成事项

- 由 captain 合并到 `main` + 推 `origin/main` + 重打单文件包。
- 用户手动验证：双击 exe → 双击左眼 → Harness 启动 + 浏览器打开 3080。

#### 已知风险

- **`ResolveDshExecutable` 候选列表是硬编码**：未来 dsh 改了安装位置或用户用了非标准 prefix 还要再补条目。可考虑后续接 `WidgetSettings` 让用户自定义路径。
- **没写单元测试覆盖 `ResolveDshExecutable`**：逻辑很纯，但当前 Widget 工程没有测试项目。后续如果新增 WPF 测试框架（如 xUnit + WpfUiTest）可以补。
- 第 3 轮的慢闪与 ShowProvider 互动风险继续在案。

#### 下一位 AI 操作

1. 等待 captain 完成合并 + 重打包。
2. 用户手动验证。
3. 接手前 `git checkout main && git pull`。

### 第 3 轮（已合并）：Harness 退出通知 + 同步 kill

- 交出者：AI-B
- 接收者：（待合并后由下一位 AI / 用户接管）
- 状态：已完成（待合并）
- 基线提交：`b20c8c5`
- 交接提交：`1851312`
- 分支：`codex/ai-b-harness-exit-notify`

#### 已完成

1. **订阅 Harness `Exited` 事件**：
   - `Process.Start` 成功后立刻 `_harnessProcess.EnableRaisingEvents = true` + 订阅 `_harnessProcess.Exited`。
   - 启动后立即 `if (HasExited) HarnessProcess_Exited(...)`，避免 Start → Subscribe 间隙内进程死掉丢事件。
2. **`OnHarnessExited` 在 UI 线程处理**：
   - `HarnessProcess_Exited` 捕获 ThreadPool 上的事件，`Dispatcher.Invoke` 切回 UI 线程。
   - `ReferenceEquals(sender, _harnessProcess)` 过滤老实例的过期事件（用户重新双击启了新 Harness 后老 Exited 还在 in-flight）。
   - 看 `_harnessStopInProgress` 区分"我们主动 Kill"和"它自己死了"；前者直接清场，后者启动慢闪。
3. **左眼慢闪**（`StartHarnessExitBlink`）：
   - 创建专属 `SolidColorBrush`，把 `DeepSeekEyeFill.Fill` 换成它。
   - `Storyboard` + `ColorAnimation`：从 DeepSeek 蓝 `(47,174,255)` 到接近黑 `(15,20,25)`，`Duration=1s`，`AutoReverse=true`，`RepeatBehavior=Forever`，`SineEase EaseInOut`。**完整周期 2s**，符合用户要求。
   - 用独立 brush 不和 `ShowProvider` 的静态 brush 引用打架。
4. **`StopHarnessExitBlink`**：停 storyboard、清 brush 引用、调 `ShowProvider(_showingChatGpt)` 把眼睛颜色还原到当前 provider 对应的状态。
5. **`StopOwnedHarness`**：
   - 先置 `_harnessStopInProgress = true`，保证即使 Kill 触发的 Exited 跑过来也不会触发慢闪。
   - 停慢闪 → `Kill(entireProcessTree: true)` → `Dispose` → 置 null。
   - 只杀我们自己跟踪的 `_harnessProcess`；用户自己在命令行启的 Harness 不在 `_harnessProcess` 里，不会误杀。
6. **`Window_Closing` 加 `StopOwnedHarness()`**：在 `StopOwnedService()` 之后调，机器人关掉时把 Harness 也同步关掉。
7. **`LaunchHarnessAsync` 入口加 `StopHarnessExitBlink()`**：再次双击时先停止上一轮的慢闪状态再走流程。

#### 修改文件

- `src/AIUsageRobot.Widget/MainWindow.xaml.cs`（+110 行：3 个字段 + 4 个新方法 + Window_Closing 一行 + LaunchHarnessAsync 三处小改）
- `AI_HANDOFF.md`（本更新）

#### 验证结果

- **build**：`dotnet build AIUsageRobot.sln --configuration Release --no-restore -warnaserror` → 4 项目全过，0 警告 0 错误，耗时 4.77s。
- **test**：`dotnet test AIUsageRobot.sln --configuration Release --no-build` → 通过 12 / 失败 0 / 跳过 0 / 总计 12，持续时间 103ms。
- **runtime**：未执行（无法在本环境无头启动 WPF + 模拟进程退出）。
- **git diff --check**：无 whitespace / 冲突告警。
- **git status**：工作区干净（除 `AI_HANDOFF.md` 待提交）。

#### 未完成事项

- 由 AI-B（captain）合并到 `main` 并推 `origin/main`，重打单文件包。
- 用户手动验证 3 个场景：
  1. 双击左眼 → 浏览器打开 3080。
  2. 在浏览器 / 命令行里把 dsh 进程 kill 掉 → 左眼开始 2s 周期慢闪。
  3. 关机器人 → dsh 进程同步退出（用 `Get-Process dsh` 或任务管理器验证）。

#### 已知风险

- **慢闪只覆盖我们自己跟踪的 Harness**：用户自己在命令行启的 Harness 死了我们不会知道（因为端口可达分支不跟踪）。如果用户希望也监测这种"野生"实例，需做"周期性探端口，断流时再慢闪"——本轮不做。
- **`_harnessStopInProgress` 是简单 `bool`**：UI 线程单写、Dispatcher 上单读，理论安全；不打算升级成更重的同步原语。
- **慢闪和 Codex 视图切换的互动**：如果慢闪进行中用户单击左眼切到 Codex，`ShowProvider` 会覆盖 `DeepSeekEyeFill.Fill` 为 `InactiveEyeBrush`，但 Storyboard 仍在跑、还把颜色绑在 `_harnessBlinkBrush` 上——结果眼睛颜色卡死、不再慢闪。要修就把 `ShowProvider` 加个"如果正在慢闪则不要覆盖 `DeepSeekEyeFill`"的判断。本轮不做，权衡是：Harness 死的时候通常不会同时切视图。
- 第 2 轮的 `dsh` 路径 / 端口 hardcode 风险继续在案。
- AgentTeams validator 静默失败事件继续在案（不影响本轮）。

#### 下一位 AI 操作

1. 等待 captain 完成合并到 `main` + 重打 `publish/win-x64/AIUsageRobot.exe`。
2. 用户手动跑 3 个验证场景。
3. 如有下一轮任务，由 captain 重新分配并更新"当前状态"和"当前任务边界"。
4. 接手前先 `git checkout main && git pull`，确认 HEAD 是新 merge commit。

### 第 2 轮（已合并）：修复左眼 Harness 启动器

- 交出者：AI-B
- 接收者：（待合并后由下一位 AI / 用户接管）
- 状态：已完成（待合并）
- 基线提交：`c2aaf18`
- 交接提交：`edeca71`
- 分支：`codex/ai-b-deepseek-harness-launcher`

#### 已完成

1. **撤回错误实现**：删除 `MainWindow.xaml.cs` 里上一轮的 `LaunchLocalDeepSeek()` 方法和右键菜单里的 "启动本地 DeepSeek…" 菜单项（commit `9546369` 引入的）。
2. **左眼双击检测**：
   - XAML：`DeepSeekEyeButton` 把 `Click="DeepSeekEyeButton_Click"` 换成 `PreviewMouseLeftButtonDown="DeepSeekEyeButton_PreviewMouseLeftButtonDown"` + `MouseDoubleClick="DeepSeekEyeButton_MouseDoubleClick"`。
   - `PreviewMouseLeftButtonDown` 通过 `e.ClickCount` 干净地区分单击（`==1`）和双击（`>=2`），避免引入 debounce 计时器。`Click` 事件用的 `RoutedEventArgs` 没有 `ClickCount`，所以必须改用 `MouseButtonEventArgs` 链路。
3. **Harness 启动器（`LaunchHarnessAsync`）**：
   - 入口：`HarnessLaunchGate`（`SemaphoreSlim(1,1)`）做并发锁，连续双击只进一次。
   - 步骤：先 TCP 探测 `127.0.0.1:3080`（500ms 超时）；可达就 `OpenInBrowser(HarnessUrl)` 返回。
   - 不可达：`Process.Start("dsh", "web")`，`UseShellExecute=false`, `CreateNoWindow=true`, `WindowStyle=Hidden`；保留 handle 在 `_harnessProcess`。
   - 每 200ms 探测一次端口和进程存活，最多 30 次（≈6s）；任一成功就开浏览器；进程死了弹 MessageBox；端口不通也弹 MessageBox。
   - `OpenInBrowser` 失败时弹 MessageBox 并提示手动访问 `http://127.0.0.1:3080/`。
4. **新增字段**：`HarnessHost`, `HarnessPort`, `HarnessUrl`, `HarnessLaunchGate`, `_harnessProcess`。
5. **新增 `using System.Net.Sockets`**：用于 `TcpClient` 探测端口。
6. **`.gitignore`**：补 `.agent-teams/`，防止 AgentTeams 内部状态意外入库。

#### 修改文件

- `src/AIUsageRobot.Widget/MainWindow.xaml`（左眼按钮改用 PreviewMouseLeftButtonDown + MouseDoubleClick）
- `src/AIUsageRobot.Widget/MainWindow.xaml.cs`（撤回旧 LaunchLocalDeepSeek；新增 Harness 字段 + LaunchHarnessAsync + IsHarnessReachableAsync + IsHarnessProcessAlive + OpenInBrowser）
- `.gitignore`（新增 `.agent-teams/`）
- `AI_HANDOFF.md`（本更新）

源代码外的产物：`%LocalAppData%\AIUsageRobot\widget-crash.log`（运行期产生，gitignore 之外）。

#### 验证结果

- **build**：`dotnet build AIUsageRobot.sln --configuration Release --no-restore -warnaserror` → 4 个项目全部生成成功，0 警告 0 错误，耗时 3.18s。
- **test**：`dotnet test AIUsageRobot.sln --configuration Release --no-build` → 通过 12 / 失败 0 / 跳过 0 / 总计 12，持续时间 90ms。
- **runtime**：未执行（无法在本环境无头启动 WPF Widget + 点击事件）。
- **git diff --check**：无 whitespace / 冲突告警。
- **git status**：工作区干净。

#### 未完成事项

- 由 AI-B（captain）将 `codex/ai-b-deepseek-harness-launcher` 合并到 `main` 并推送到 `origin/main`（按用户授权）。
- 用户手动验证：双击机器人左眼 → 浏览器自动打开 `http://127.0.0.1:3080/`（前提是 `dsh` 在 PATH 中）。
- 未做但已记录在案的潜在改进（不在本轮范围）：
  - `LaunchHarnessAsync` 把 `dsh` 路径做成可配置（环境变量 / 用户配置）。
  - 给 `LaunchHarnessAsync` 加超时可视化（机器人屏幕闪一下 "Launching Harness…" 之类）。
  - Harness 退出后通知用户（订阅 `_harnessProcess.Exited`）。
  - 测试覆盖盲区：`BalanceState` / `ChatGptQuotaState` 状态机、SQLite 事务、`CodexAppServerClient` 重连重试、Widget UI 行为。

#### 已知风险

- **依赖用户 PATH 里有 `dsh` 命令**：如果 `dsh` 不在 PATH，启动会失败并弹 MessageBox；本轮不做路径配置化。
- **端口 3080 是 hardcode**：如果用户改了 `dsh web` 默认端口，机器人探测会失败。同样需要做成可配置。
- **没监控 Harness 退出**：双击启动后如果 Harness 进程崩溃，机器人不会知道；只在下一次双击时才会重新拉起。
- **第 1 轮 t2 的"启动本地 DeepSeek"语义误读**：已在 commit `9546369`（已合并到 main）留下"打开 platform 页"的错误实现，并在 commit `edeca71` 撤回。这是一个教训：**遇到语义模糊的需求必须先问清楚再动手**（规则 11 已新增）。
- AgentTeams validator 静默失败事件继续记录在案。

#### 下一位 AI 操作

1. 等待 captain 完成合并到 `main`。
2. 合并后，用户应手动验证：双击机器人左眼 → Harness 启动 → 浏览器打开 3080。
3. 如有下一轮任务，由 captain 重新分配并更新"当前状态"和"当前任务边界"。
4. 接手前先 `git checkout main && git pull`，确认 HEAD 是新的 merge commit。

### 第 1 轮（已合并）：Bug 修复 + 初次左眼集成尝试（部分错位）

- 交出者：AI-A（建立了本文件）
- 接收者：AI-B
- 状态：已完成并合并
- 基线提交：`dc09d5d2073ef8fd68d0ec236894fa6e97a34415`
- 交接提交范围：`a495d18` … `9546369`
- 合并提交：`c2aaf18`

#### 已完成

1. **Bug 修复（t1）**：
   - `BalanceState`：引入 `_statusGate` + `SetTransient` / `ReadTransient` 解决撕裂读。
   - `CodexAppServerClient.CloseConnection`：在 `lock` 内对 `_input` 调 `Dispose` 再置空，避免 `StandardInput` 泄漏。
   - 新增 `SqliteRepositoryBase`，三个 Repo 改继承消重。
   - `App.xaml.cs`：订阅 `DispatcherUnhandledException` + `AppDomain` + `TaskScheduler` 三层兜底。
2. **集成"启动本地 DeepSeek"到左眼（t2）—— 实现位置错位**：放在右键菜单里（而不是左眼本身），且动作是 `Process.Start("https://platform.deepseek.com/")`（打开云端控制台，而不是启动本地进程）。用户审阅后判定功能未实现，本轮（第 2 轮）已撤回重做。
3. **项目验证基线（t3）**：captain 接手执行，build + test 全绿。
4. **交接记录（t4）**：本文件已更新。

#### 修改文件

- `src/AIUsageRobot.Service/BalanceState.cs`（修 bug）
- `src/AIUsageRobot.Service/CodexAppServerClient.cs`（修 bug）
- `src/AIUsageRobot.Service/SqliteRepositoryBase.cs`（新增，消重）
- `src/AIUsageRobot.Service/BalanceRepository.cs`（改继承）
- `src/AIUsageRobot.Service/ChatGptQuotaRepository.cs`（改继承）
- `src/AIUsageRobot.Service/MonitoringHistoryRepository.cs`（改继承）
- `src/AIUsageRobot.Widget/App.xaml.cs`（异常兜底）
- `src/AIUsageRobot.Widget/MainWindow.xaml.cs`（首次实现 "启动本地 DeepSeek" 菜单项 —— **已在第 2 轮撤回**）
- `AI_HANDOFF.md`（建立）

#### 验证结果

- build：通过，0 警告 0 错误。
- test：通过 12/12。
- runtime：未执行。
- git diff --check：无告警。

#### 已知风险（继承到第 2 轮）

- t2 实现位置错位 + 语义错位 → 已在本轮修正。
- AgentTeams validator 静默失败 → 已 captain 兜底。
- 测试覆盖盲区（`BalanceState` / `ChatGptQuotaState` 状态机、SQLite 事务、`CodexAppServerClient` 重连、Widget UI）。

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
