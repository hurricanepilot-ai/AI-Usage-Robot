using AIUsageRobot.Service;

namespace AIUsageRobot.Tests;

public sealed class ExtensionEventHubTests
{
    [Fact]
    public async Task Publish_DeliversImmediateSyncEvent_ToSubscriber()
    {
        var hub = new ExtensionEventHub();
        var subscription = hub.Subscribe();

        var delivered = hub.Publish("chatgpt-sync");
        var message = await subscription.Reader.ReadAsync();

        Assert.Equal(1, delivered);
        Assert.Equal("chatgpt-sync", message);
        hub.Unsubscribe(subscription.Id);
    }
}
