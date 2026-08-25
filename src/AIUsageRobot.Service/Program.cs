using AIUsageRobot.Shared;
using Microsoft.AspNetCore.Http.HttpResults;
using System.Diagnostics;

namespace AIUsageRobot.Service;

public static class ServiceHost
{
    public static async Task RunAsync(string[] args, CancellationToken cancellationToken = default)
    {
        var parentProcessId = ReadParentProcessId(args);
        var builder = WebApplication.CreateBuilder(args);
        builder.WebHost.UseUrls(LocalAppStorage.ApiBaseUrl);
        builder.Services.AddSingleton<BalanceRepository>();
        builder.Services.AddSingleton<ChatGptQuotaRepository>();
        builder.Services.AddSingleton<MonitoringHistoryRepository>();
        builder.Services.AddSingleton<DeepSeekUsageService>();
        builder.Services.AddSingleton<ICredentialStore, WindowsCredentialStore>();
        builder.Services.AddHttpClient<DeepSeekBalanceClient>(client =>
        {
            client.BaseAddress = new Uri("https://api.deepseek.com/");
            client.Timeout = TimeSpan.FromSeconds(15);
        });
        builder.Services.AddSingleton<BalanceState>();
        builder.Services.AddSingleton<ChatGptQuotaState>();
        builder.Services.AddHostedService<BalanceRefreshWorker>();
        builder.Services.AddSingleton<CodexExecutableResolver>();
        builder.Services.AddSingleton<CodexAppServerClient>();
        builder.Services.AddHostedService(serviceProvider => serviceProvider.GetRequiredService<CodexAppServerClient>());

        var app = builder.Build();
        await app.Services.GetRequiredService<BalanceRepository>().InitializeAsync();
        await app.Services.GetRequiredService<ChatGptQuotaRepository>().InitializeAsync();
        await app.Services.GetRequiredService<MonitoringHistoryRepository>().InitializeAsync();

        var localToken = LocalAppStorage.GetOrCreateApiToken();
        app.Use(async (context, next) =>
        {
            if (context.Request.Path.StartsWithSegments("/api") &&
                !string.Equals(context.Request.Headers.Authorization, $"Bearer {localToken}", StringComparison.Ordinal))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsJsonAsync(new { error = "LOCAL_AUTH_REQUIRED" });
                return;
            }

            await next();
        });

        app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
        app.MapGet("/api/overview", async (BalanceState deepSeek, ChatGptQuotaState chatGpt, CancellationToken ct) =>
            Results.Ok(new OverviewDto(await chatGpt.GetAsync(ct), await deepSeek.GetOverviewAsync(ct), DateTimeOffset.UtcNow)));

        app.MapGet("/api/history/{provider}", async (
            string provider,
            int? hours,
            MonitoringHistoryRepository history,
            CancellationToken ct) =>
        {
            var normalized = provider.ToLowerInvariant();
            if (normalized is not ("codex" or "deepseek"))
                return Results.BadRequest(new { error = "provider 必须是 codex 或 deepseek" });
            return Results.Ok(await history.GetAsync(normalized, hours ?? 24 * 7, ct));
        });

        app.MapGet("/api/deepseek/usage/daily", async (
            int? days,
            DeepSeekUsageService usage,
            CancellationToken ct) =>
        {
            var requestedDays = days ?? 7;
            if (requestedDays is < 1 or > 90)
                return Results.BadRequest(new { error = "days 必须在 1 到 90 之间" });
            return Results.Ok(await usage.GetDailyAsync(requestedDays, ct));
        });

        app.MapPost("/api/codex/refresh", async (
            CodexAppServerClient client,
            ChatGptQuotaState state,
            CancellationToken ct) =>
        {
            try
            {
                await client.RefreshAsync(ct);
                return Results.Ok(await state.GetAsync(ct));
            }
            catch (Exception exception) when (exception is TimeoutException or InvalidOperationException or IOException)
            {
                return Results.Problem(exception.Message, statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        });

        app.MapPut("/api/deepseek/credential", async Task<Results<Ok, BadRequest<string>>> (
            SaveCredentialRequest request,
            ICredentialStore credentials,
            BalanceState state,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.ApiKey) || request.ApiKey.Length < 8)
                return TypedResults.BadRequest("API Key 格式无效。");

            await credentials.SaveAsync(request.ApiKey.Trim(), ct);
            await state.RefreshAsync(ct);
            return TypedResults.Ok();
        });

        app.MapDelete("/api/deepseek/credential", async (
            ICredentialStore credentials,
            BalanceState state,
            CancellationToken ct) =>
        {
            await credentials.DeleteAsync(ct);
            await state.ClearAsync(ct);
            return Results.NoContent();
        });

        app.MapPost("/api/deepseek/refresh", async (BalanceState state, CancellationToken ct) =>
        {
            await state.RefreshAsync(ct);
            return Results.Ok(await state.GetOverviewAsync(ct));
        });

        using var lifetimeCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var parentMonitor = parentProcessId is int processId
            ? MonitorParentAsync(processId, app.Lifetime.StopApplication, lifetimeCancellation.Token)
            : Task.CompletedTask;
        await app.RunAsync(cancellationToken);
        lifetimeCancellation.Cancel();
        try { await parentMonitor; } catch (OperationCanceledException) { }
    }

    private static int? ReadParentProcessId(string[] args)
    {
        for (var index = 0; index < args.Length - 1; index++)
        {
            if (string.Equals(args[index], "--parent-pid", StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(args[index + 1], out var processId) && processId > 0)
                return processId;
        }
        return null;
    }

    private static async Task MonitorParentAsync(int processId, Action stopApplication, CancellationToken cancellationToken)
    {
        try
        {
            using var parent = Process.GetProcessById(processId);
            await parent.WaitForExitAsync(cancellationToken);
        }
        catch (ArgumentException)
        {
            // The parent already exited before the service finished starting.
        }
        if (!cancellationToken.IsCancellationRequested)
            stopApplication();
    }
}
