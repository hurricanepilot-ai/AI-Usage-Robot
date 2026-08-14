using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace AIUsageRobot.Service;

public sealed class ExtensionPairingService
{
    private readonly ConcurrentDictionary<string, DateTimeOffset> _codes = new();

    public (string Code, DateTimeOffset ExpiresAt) Create()
    {
        RemoveExpired();
        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
        var expires = DateTimeOffset.UtcNow.AddMinutes(5);
        _codes[code] = expires;
        return (code, expires);
    }

    public bool Consume(string code)
    {
        RemoveExpired();
        return _codes.TryRemove(code.Trim(), out var expires) && expires > DateTimeOffset.UtcNow;
    }

    private void RemoveExpired()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var pair in _codes.Where(x => x.Value <= now)) _codes.TryRemove(pair.Key, out _);
    }
}
