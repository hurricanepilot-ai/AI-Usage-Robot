using AIUsageRobot.Shared;

namespace AIUsageRobot.Service;

public sealed class CodexExecutableResolver(IConfiguration configuration, ILogger<CodexExecutableResolver> logger)
{
    public string? Resolve()
    {
        var configured = configuration["Codex:ExecutablePath"];
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
            return PrepareExecutable(configured);

        var environmentPath = Environment.GetEnvironmentVariable("CODEX_EXECUTABLE");
        if (!string.IsNullOrWhiteSpace(environmentPath) && File.Exists(environmentPath))
            return PrepareExecutable(environmentPath);

        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
                     .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            try
            {
                var candidate = Path.Combine(directory.Trim('"'), "codex.exe");
                if (File.Exists(candidate)) return PrepareExecutable(candidate);
            }
            catch (Exception) { }
        }

        return FindPackagedExecutable() ?? FindCachedExecutable();
    }

    private string? FindPackagedExecutable()
    {
        var windowsApps = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "WindowsApps");
        try
        {
            var candidate = Directory.EnumerateDirectories(windowsApps, "OpenAI.Codex_*")
                .Select(directory => Path.Combine(directory, "app", "resources", "codex.exe"))
                .Where(File.Exists)
                .OrderByDescending(path => File.GetLastWriteTimeUtc(path))
                .FirstOrDefault();
            return candidate is null ? null : PrepareExecutable(candidate);
        }
        catch (Exception exception)
        {
            logger.LogDebug(exception, "Unable to inspect the packaged Codex installation.");
            return null;
        }
    }

    private string? FindCachedExecutable()
    {
        var cachedPath = Path.Combine(LocalAppStorage.RootDirectory, "codex-runtime", "codex.exe");
        if (!File.Exists(cachedPath)) return null;

        logger.LogWarning(
            "The installed Codex package is not directly accessible; using the last prepared local Codex CLI runtime.");
        return cachedPath;
    }

    private string PrepareExecutable(string sourcePath)
    {
        var fullPath = Path.GetFullPath(sourcePath);
        if (!fullPath.Contains($"{Path.DirectorySeparatorChar}WindowsApps{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            return fullPath;

        var runtimeDirectory = Path.Combine(LocalAppStorage.RootDirectory, "codex-runtime");
        var cachedPath = Path.Combine(runtimeDirectory, "codex.exe");
        Directory.CreateDirectory(runtimeDirectory);

        var source = new FileInfo(fullPath);
        var cached = new FileInfo(cachedPath);
        if (!cached.Exists || cached.Length != source.Length || cached.LastWriteTimeUtc != source.LastWriteTimeUtc)
        {
            logger.LogInformation("Preparing the packaged Codex CLI for the local monitor.");
            File.Copy(fullPath, cachedPath, true);
            File.SetLastWriteTimeUtc(cachedPath, source.LastWriteTimeUtc);
        }

        return cachedPath;
    }
}
