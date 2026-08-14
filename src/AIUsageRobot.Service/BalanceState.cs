using AIUsageRobot.Shared;

namespace AIUsageRobot.Service;

public sealed class BalanceState(
    BalanceRepository repository,
    ICredentialStore credentials,
    DeepSeekBalanceClient client,
    ILogger<BalanceState> logger)
{
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
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
                _transientStatus = DataStatus.Unknown;
                _message = "未配置 DeepSeek API Key";
                return;
            }

            try
            {
                var balance = await client.GetAsync(apiKey, cancellationToken);
                await repository.SaveAsync(balance, cancellationToken);
                _transientStatus = null;
                _message = null;
            }
            catch (DeepSeekAuthenticationException)
            {
                _transientStatus = DataStatus.AuthError;
                _message = "API Key 无效或已失效";
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                _transientStatus = DataStatus.Offline;
                _message = "DeepSeek 暂时不可达";
                logger.LogWarning("DeepSeek balance refresh failed: {ErrorType}", ex.GetType().Name);
            }
        }
        finally { _refreshLock.Release(); }
    }

    public async Task<DeepSeekBalanceDto> GetOverviewAsync(CancellationToken cancellationToken)
    {
        var stored = await repository.GetAsync(cancellationToken);
        var hasCredential = await credentials.GetAsync(cancellationToken) is not null;
        if (stored is null)
        {
            var emptyStatus = _transientStatus ?? DataStatus.Unknown;
            return new DeepSeekBalanceDto(
                new Metric<decimal?>(null, emptyStatus, null, _message ?? "Unknown"), "CNY", null, hasCredential);
        }

        var age = DateTimeOffset.UtcNow - stored.UpdatedAt;
        var freshness = age < TimeSpan.FromMinutes(15) ? DataStatus.Fresh
            : age <= TimeSpan.FromHours(24) ? DataStatus.Stale
            : DataStatus.Unavailable;
        var status = _transientStatus is DataStatus.AuthError or DataStatus.Offline ? _transientStatus.Value : freshness;
        return new DeepSeekBalanceDto(
            new Metric<decimal?>(stored.Total, status, stored.UpdatedAt, _message),
            stored.Currency, stored.IsAvailable, hasCredential);
    }

    public async Task ClearAsync(CancellationToken cancellationToken)
    {
        await repository.ClearAsync(cancellationToken);
        _transientStatus = DataStatus.Unknown;
        _message = "未配置 DeepSeek API Key";
    }
}

public sealed class BalanceRefreshWorker(BalanceState state, ILogger<BalanceRefreshWorker> logger) : BackgroundService
{
    public static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RefreshSafelyAsync(stoppingToken);
        using var timer = new PeriodicTimer(RefreshInterval);
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
