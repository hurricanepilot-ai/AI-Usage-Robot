using AIUsageRobot.Service;

namespace AIUsageRobot.Widget;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Any(argument => string.Equals(argument, "--service", StringComparison.OrdinalIgnoreCase)))
        {
            var serviceArgs = args
                .Where(argument => !string.Equals(argument, "--service", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            ServiceHost.RunAsync(serviceArgs).GetAwaiter().GetResult();
            return 0;
        }

        var app = new App();
        return app.Run();
    }
}
