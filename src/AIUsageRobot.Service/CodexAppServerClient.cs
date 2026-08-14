using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;

namespace AIUsageRobot.Service;

public sealed class CodexAppServerClient(
    CodexExecutableResolver executableResolver,
    ChatGptQuotaState quotaState,
    IConfiguration configuration,
    ILogger<CodexAppServerClient> logger) : BackgroundService
{
    private readonly ConcurrentDictionary<long, TaskCompletionSource<JsonElement>> _pending = new();
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private readonly object _connectionLock = new();
    private TaskCompletionSource<bool> _ready = NewSignal();
    private Process? _process;
    private StreamWriter? _input;
    private long _nextRequestId;

    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        Task readyTask;
        lock (_connectionLock) readyTask = _ready.Task;
        await readyTask.WaitAsync(TimeSpan.FromSeconds(20), cancellationToken);
        await RefreshConnectedAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var retryDelay = TimeSpan.FromSeconds(1);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunConnectionAsync(stoppingToken);
                retryDelay = TimeSpan.FromSeconds(1);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Codex app-server connection failed; retrying in {Delay}.", retryDelay);
                quotaState.ReportFailure(ToUserMessage(exception));
                try { await Task.Delay(retryDelay, stoppingToken); }
                catch (OperationCanceledException) { break; }
                retryDelay = TimeSpan.FromSeconds(Math.Min(15, retryDelay.TotalSeconds * 2));
            }
            finally
            {
                CloseConnection();
            }
        }
    }

    private async Task RunConnectionAsync(CancellationToken stoppingToken)
    {
        var executable = executableResolver.Resolve()
            ?? throw new FileNotFoundException("未找到可执行的 Codex CLI，请安装独立 CLI 或配置 Codex:ExecutablePath。");

        var startInfo = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
        };
        startInfo.ArgumentList.Add("app-server");
        startInfo.ArgumentList.Add("--listen");
        startInfo.ArgumentList.Add("stdio://");

        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        if (!process.Start()) throw new InvalidOperationException("Codex app-server 启动失败。");

        lock (_connectionLock)
        {
            _process = process;
            _input = process.StandardInput;
            _ready = NewSignal();
        }

        var outputTask = ReadOutputAsync(process, stoppingToken);
        var errorTask = DrainErrorsAsync(process, stoppingToken);

        try
        {
            await SendRequestAsync("initialize", new
            {
                clientInfo = new { name = "AIUsageRobot", version = "1.0" }
            }, stoppingToken);
            await SendNotificationAsync("initialized", null, stoppingToken);
            lock (_connectionLock) _ready.TrySetResult(true);

            await RefreshConnectedAsync(stoppingToken);

            var intervalMinutes = Math.Clamp(configuration.GetValue("Codex:RefreshIntervalMinutes", 5), 1, 60);
            using var timer = new PeriodicTimer(TimeSpan.FromMinutes(intervalMinutes));
            while (!stoppingToken.IsCancellationRequested)
            {
                var tickTask = timer.WaitForNextTickAsync(stoppingToken).AsTask();
                var exitTask = process.WaitForExitAsync(stoppingToken);
                var completed = await Task.WhenAny(tickTask, exitTask, outputTask);
                if (completed == outputTask) await outputTask;
                if (completed == exitTask || process.HasExited)
                    throw new InvalidOperationException("Codex app-server 已退出。");
                if (!await tickTask) break;
                await RefreshConnectedAsync(stoppingToken);
            }
        }
        finally
        {
            if (!process.HasExited)
            {
                try { process.Kill(true); }
                catch (Exception) { }
            }
            await Task.WhenAll(IgnoreCancellation(outputTask), IgnoreCancellation(errorTask));
            process.Dispose();
        }
    }

    private async Task RefreshConnectedAsync(CancellationToken cancellationToken)
    {
        if (!await _refreshGate.WaitAsync(0, cancellationToken)) return;
        try
        {
            var result = await SendRequestAsync("account/rateLimits/read", null, cancellationToken);
            if (!CodexRateLimitParser.TryParse(result, DateTimeOffset.UtcNow, out var quota) || quota is null)
                throw new InvalidDataException("Codex 返回的额度数据中没有可用窗口。");
            await quotaState.SaveAsync(quota, cancellationToken);
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    private async Task ReadOutputAsync(Process process, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && !process.HasExited)
        {
            var line = await process.StandardOutput.ReadLineAsync(cancellationToken);
            if (line is null) break;

            try
            {
                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;
                if (root.TryGetProperty("id", out var idElement) && idElement.TryGetInt64(out var id))
                {
                    if (!_pending.TryRemove(id, out var completion)) continue;
                    if (root.TryGetProperty("error", out var error))
                        completion.TrySetException(new InvalidOperationException(error.ToString()));
                    else if (root.TryGetProperty("result", out var result))
                        completion.TrySetResult(result.Clone());
                    else
                        completion.TrySetException(new InvalidDataException("Codex 响应缺少 result。"));
                    continue;
                }

                if (root.TryGetProperty("method", out var method) &&
                    method.GetString() == "account/rateLimits/updated")
                {
                    _ = Task.Run(async () =>
                    {
                        try { await RefreshConnectedAsync(cancellationToken); }
                        catch (Exception exception) { logger.LogDebug(exception, "Unable to refresh after a rate-limit event."); }
                    }, CancellationToken.None);
                }
            }
            catch (JsonException exception)
            {
                logger.LogDebug(exception, "Ignoring malformed Codex app-server output.");
            }
        }

        if (!cancellationToken.IsCancellationRequested)
            throw new EndOfStreamException("Codex app-server 输出流已关闭。");
    }

    private async Task DrainErrorsAsync(Process process, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && !process.HasExited)
        {
            var line = await process.StandardError.ReadLineAsync(cancellationToken);
            if (line is null) break;
            logger.LogDebug("Codex app-server: {Message}", line);
        }
    }

    private async Task<JsonElement> SendRequestAsync(string method, object? parameters, CancellationToken cancellationToken)
    {
        var id = Interlocked.Increment(ref _nextRequestId);
        var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_pending.TryAdd(id, completion)) throw new InvalidOperationException("Codex 请求编号冲突。");
        try
        {
            await WriteMessageAsync(new { id, method, @params = parameters }, cancellationToken);
            return await completion.Task.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken);
        }
        finally
        {
            _pending.TryRemove(id, out _);
        }
    }

    private Task SendNotificationAsync(string method, object? parameters, CancellationToken cancellationToken) =>
        WriteMessageAsync(new { method, @params = parameters }, cancellationToken);

    private async Task WriteMessageAsync(object message, CancellationToken cancellationToken)
    {
        StreamWriter input;
        lock (_connectionLock) input = _input ?? throw new InvalidOperationException("Codex app-server 尚未连接。");
        var json = JsonSerializer.Serialize(message);
        await _writeGate.WaitAsync(cancellationToken);
        try
        {
            await input.WriteLineAsync(json.AsMemory(), cancellationToken);
            await input.FlushAsync(cancellationToken);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    private void CloseConnection()
    {
        lock (_connectionLock)
        {
            _ready.TrySetException(new InvalidOperationException("Codex app-server 连接已断开。"));
            _input = null;
            _process = null;
        }
        foreach (var completion in _pending.Values)
            completion.TrySetException(new InvalidOperationException("Codex app-server 连接已断开。"));
        _pending.Clear();
    }

    private static TaskCompletionSource<bool> NewSignal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static async Task IgnoreCancellation(Task task)
    {
        try { await task; }
        catch (OperationCanceledException) { }
        catch (EndOfStreamException) { }
    }

    private static string ToUserMessage(Exception exception) => exception switch
    {
        FileNotFoundException => exception.Message,
        TimeoutException => "Codex 额度查询超时",
        _ => "Codex 额度服务暂时不可用"
    };

    public override void Dispose()
    {
        CloseConnection();
        _writeGate.Dispose();
        _refreshGate.Dispose();
        base.Dispose();
    }
}
