using AIUsageRobot.Shared;

namespace AIUsageRobot.Service;

public sealed class BalanceState(
    BalanceRepository repository,
    MonitoringHistoryRepository history,
    ICredentialStore credentials,
    DeepSeekBalanceClient client,
    ILogger<BalanceState> logger)
{
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private readonly object _statusGate = new();
    private DataStatus? _transientStatus;
    private string? _message;

    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        if (!await _refreshLock.WaitAsync(0, cancellationToken)) return;
        try
        {
            var apiKey = await credentials.GetAsync(cancellationToken);
            if (apiKey is null)
            {
                SetTransient(DataStatus.Unknown, "未配置 DeepSeek API Key");
                return;
            }

            try
            {
                var balance = await client.GetAsync(apiKey, cancellationToken);
                await repository.SaveAsync(balance, cancellationToken);
                await history.SaveAsync(new ProviderSnapshotDto(
                    "deepseek", "balance", balance.Total, balance.Currency, balance.UpdatedAt), cancellationToken);
                SetTransient(null, null);
            }
            catch (DeepSeekAuthenticationException)
            {
                SetTransient(DataStatus.AuthError, "API Key 无效或已失效");
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                SetTransient(DataStatus.Offline, "DeepSeek 暂时不可达");
                logger.LogWarning("DeepSeek balance refresh failed: {ErrorType}", ex.GetType().Name);
            }
        }
        finally { _refreshLock.Release(); }
    }

    public async Task<DeepSeekBalanceDto> GetOverviewAsync(CancellationToken cancellationToken)
    {
        var stored = await repository.GetAsync(cancellationToken);
        var hasCredential = await credentials.GetAsync(cancellationToken) is not null;
        var (transientStatus, message) = ReadTransient();
        if (stored is null)
        {
            var emptyStatus = transientStatus ?? DataStatus.Unknown;
            return new DeepSeekBalanceDto(
                new Metric<decimal?>(null, emptyStatus, null, message ?? "Unknown"), "CNY", null, hasCredential);
        }

        var age = DateTimeOffset.UtcNow - stored.UpdatedAt;
        var freshness = age < TimeSpan.FromMinutes(15) ? DataStatus.Fresh
            : age <= TimeSpan.FromHours(24) ? DataStatus.Stale
            : DataStatus.Unavailable;
        var status = transientStatus is DataStatus.AuthError or DataStatus.Offline ? transientStatus.Value : freshness;
        return new DeepSeekBalanceDto(
            new Metric<decimal?>(stored.Total, status, stored.UpdatedAt, message),
            stored.Currency, stored.IsAvailable, hasCredential);
    }

    public async Task ClearAsync(CancellationToken cancellationToken)
    {
        await repository.ClearAsync(cancellationToken);
        SetTransient(DataStatus.Unknown, "未配置 DeepSeek API Key");
    }

    private void SetTransient(DataStatus? status, string? message)
    {
        lock (_statusGate)
        {
            _transientStatus = status;
            _message = message;
        }
    }

    private (DataStatus? Status, string? Message) ReadTransient()
    {
        lock (_statusGate)
        {
            return (_transientStatus, _message);
        }
    }
}

public sealed class BalanceRefreshWorker(
    BalanceState state,
    IConfiguration configuration,
    ILogger<BalanceRefreshWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RefreshSafelyAsync(stoppingToken);
        var minutes = Math.Clamp(configuration.GetValue("DeepSeek:RefreshIntervalMinutes", 5), 1, 60);
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(minutes));
        while (await timer.WaitForNextTickAsync(stoppingToken))
            await RefreshSafelyAsync(stoppingToken);
    }

    private async Task RefreshSafelyAsync(CancellationToken stoppingToken)
    {
        try
        {
            await state.RefreshAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        catch (Exception ex) { logger.LogError(ex, "Balance refresh cycle failed."); }
    }
}
