using System.Text.Json;

namespace Clicky.App.Configuration;

public sealed record ClickyAppConfiguration(string WorkerBaseUrl, string DefaultClaudeModel);

public static class AppConfigurationLoader
{
    public static ClickyAppConfiguration Load()
    {
        var appSettingsFilePath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        RawAppSettings rawAppSettings = new();

        if (File.Exists(appSettingsFilePath))
        {
            var rawJson = File.ReadAllText(appSettingsFilePath);
            rawAppSettings = JsonSerializer.Deserialize<RawAppSettings>(rawJson) ?? new RawAppSettings();
        }

        var workerBaseUrl = NormalizeWorkerBaseUrl(
            Environment.GetEnvironmentVariable("CLICKY_WORKER_BASE_URL")
            ?? rawAppSettings.WorkerBaseUrl
            ?? "https://your-worker-name.your-subdomain.workers.dev");

        var defaultClaudeModel =
            Environment.GetEnvironmentVariable("CLICKY_DEFAULT_MODEL")
            ?? rawAppSettings.DefaultClaudeModel
            ?? "claude-sonnet-4-6";

        return new ClickyAppConfiguration(workerBaseUrl, defaultClaudeModel);
    }

    private static string NormalizeWorkerBaseUrl(string workerBaseUrl)
    {
        return workerBaseUrl.Trim().TrimEnd('/');
    }

    private sealed class RawAppSettings
    {
        public string? WorkerBaseUrl { get; init; }

        public string? DefaultClaudeModel { get; init; }
    }
}

