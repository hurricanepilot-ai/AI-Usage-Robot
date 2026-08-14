using AIUsageRobot.Service;
using AIUsageRobot.Shared;
using Microsoft.AspNetCore.Http.HttpResults;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls(LocalAppStorage.ApiBaseUrl);
builder.Services.AddSingleton<BalanceRepository>();
builder.Services.AddSingleton<ChatGptQuotaRepository>();
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

app.Run();

public partial class Program { }
