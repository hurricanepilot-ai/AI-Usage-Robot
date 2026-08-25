using System.Security.Cryptography;
using System.Text;

namespace AIUsageRobot.Shared;

public enum DataStatus
{
    Unknown,
    Fresh,
    Stale,
    Unavailable,
    Offline,
    AuthError
}

public sealed record Metric<T>(
    T? Value,
    DataStatus Status,
    DateTimeOffset? UpdatedAt,
    string? Message);

public sealed record DeepSeekBalanceDto(
    Metric<decimal?> TotalBalance,
    string Currency,
    bool? IsAvailable,
    bool HasCredential);

public sealed record CodexQuotaWindowDto(
    string Name,
    Metric<int?> RemainingPercentage,
    string? Period,
    DateTimeOffset? ResetAt);

public sealed record CodexDailyUsageDto(
    DateOnly StartDate,
    long Tokens);

public sealed record CodexUsageSummaryDto(
    long? LifetimeTokens,
    long? PeakDailyTokens,
    int? CurrentStreakDays,
    int? LongestStreakDays,
    long? LongestRunningTurnSeconds,
    IReadOnlyList<CodexDailyUsageDto> DailyUsage,
    DateTimeOffset? UpdatedAt);

public sealed record ChatGptQuotaDto(
    string? Model,
    Metric<int?> Percentage,
    string MetricSemantics,
    string? Period,
    DateTimeOffset? ResetAt,
    string? ParserVersion,
    IReadOnlyList<CodexQuotaWindowDto>? Windows = null,
    CodexUsageSummaryDto? Usage = null);

public sealed record OverviewDto(
    ChatGptQuotaDto ChatGPT,
    DeepSeekBalanceDto DeepSeek,
    DateTimeOffset ServerTime);

public sealed record SaveCredentialRequest(string ApiKey);

public sealed record CodexQuotaWindowInput(
    string Name,
    int RemainingPercentage,
    string? Period,
    DateTimeOffset? ResetAt);

public sealed record CodexQuotaSnapshotInput(
    string? Model,
    IReadOnlyList<CodexQuotaWindowInput> Windows,
    DateTimeOffset CollectedAt,
    string ParserVersion);

public sealed record CodexUsageInput(
    long? LifetimeTokens,
    long? PeakDailyTokens,
    int? CurrentStreakDays,
    int? LongestStreakDays,
    long? LongestRunningTurnSeconds,
    IReadOnlyList<CodexDailyUsageDto> DailyUsage,
    DateTimeOffset CollectedAt);

public sealed record ProviderSnapshotDto(
    string Provider,
    string Metric,
    decimal Value,
    string Unit,
    DateTimeOffset CollectedAt);

public sealed record DeepSeekDailyAmountDto(
    DateOnly Date,
    decimal AmountUsed,
    bool HasData,
    int SampleCount,
    decimal? OpeningBalance,
    decimal? ClosingBalance,
    string Currency);

public sealed record DeepSeekUsageTrendDto(
    IReadOnlyList<DeepSeekDailyAmountDto> Days,
    DateTimeOffset? HistoryStartedAt,
    string CalculationMethod);

public static class LocalAppStorage
{
    public const string ApiBaseUrl = "http://127.0.0.1:17860";
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("AIUsageRobot.LocalApi.v1");

    public static string RootDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AIUsageRobot");

    public static string DatabasePath => Path.Combine(RootDirectory, "usage.db");
    public static string ApiTokenPath => Path.Combine(RootDirectory, "local-api-token.bin");

    public static string GetOrCreateApiToken()
    {
        Directory.CreateDirectory(RootDirectory);
        if (File.Exists(ApiTokenPath))
        {
            var protectedBytes = File.ReadAllBytes(ApiTokenPath);
            var bytes = ProtectedData.Unprotect(protectedBytes, Entropy, DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(bytes);
        }

        var token = RandomNumberGenerator.GetBytes(32);
        var encrypted = ProtectedData.Protect(token, Entropy, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(ApiTokenPath, encrypted);
        return Convert.ToBase64String(token);
    }
}
