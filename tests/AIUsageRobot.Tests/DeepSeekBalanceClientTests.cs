using AIUsageRobot.Service;
using System.Text.Json;

namespace AIUsageRobot.Tests;

public sealed class DeepSeekBalanceClientTests
{
    [Fact]
    public void Parse_PrefersCnyBalance_AndPreservesAvailability()
    {
        using var json = JsonDocument.Parse("""
            {
              "is_available": true,
              "balance_infos": [
                { "currency": "USD", "total_balance": "2.25", "granted_balance": "0", "topped_up_balance": "2.25" },
                { "currency": "CNY", "total_balance": "86.42", "granted_balance": "6.42", "topped_up_balance": "80" }
              ]
            }
            """);

        var balance = DeepSeekBalanceClient.Parse(json);

        Assert.Equal(86.42m, balance.Total);
        Assert.Equal("CNY", balance.Currency);
        Assert.True(balance.IsAvailable);
        Assert.Equal(TimeSpan.Zero, balance.UpdatedAt.Offset);
    }

    [Fact]
    public void Parse_RejectsMissingBalanceRows()
    {
        using var json = JsonDocument.Parse("""{ "is_available": false, "balance_infos": [] }""");
        Assert.Throws<JsonException>(() => DeepSeekBalanceClient.Parse(json));
    }
}
