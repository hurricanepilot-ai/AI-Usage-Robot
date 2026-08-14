using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace AIUsageRobot.Service;

public sealed class DeepSeekAuthenticationException : Exception;

public sealed class DeepSeekBalanceClient(HttpClient httpClient)
{
    public async Task<StoredBalance> GetAsync(string apiKey, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "user/balance");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            throw new DeepSeekAuthenticationException();
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return Parse(await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken));
    }

    public static StoredBalance Parse(JsonDocument document)
    {
        var root = document.RootElement;
        var available = root.GetProperty("is_available").GetBoolean();
        var balances = root.GetProperty("balance_infos").EnumerateArray().ToArray();
        var selected = balances.FirstOrDefault(x => x.GetProperty("currency").GetString() == "CNY");
        if (selected.ValueKind == JsonValueKind.Undefined && balances.Length > 0) selected = balances[0];
        if (selected.ValueKind == JsonValueKind.Undefined) throw new JsonException("balance_infos 为空。");
        var total = decimal.Parse(selected.GetProperty("total_balance").GetString()!, CultureInfo.InvariantCulture);
        var currency = selected.GetProperty("currency").GetString() ?? "CNY";
        return new StoredBalance(total, currency, available, DateTimeOffset.UtcNow);
    }
}
