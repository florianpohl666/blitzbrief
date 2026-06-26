using System.Net;
using System.Net.Http;
using System.Windows;
using BlitzBrief.Core.OpenAI;
using BlitzBrief.Core.Security;
using BlitzBrief.Core.Settings;
using BlitzBrief.Core.Workflow;

namespace BlitzBrief.Windows;

public partial class App : System.Windows.Application
{
    private TrayController? trayController;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            AppLog.Write("OnStartup begin");

            var settingsStore = new SettingsStore();
            AppLog.Write("SettingsStore created.");
            var settings = await settingsStore.LoadAsync();
            AppLog.Write("Settings loaded.");
            var apiKeyStore = new ApiKeyStore();
            AppLog.Write("ApiKeyStore created.");

            // SocketsHttpHandler mit HTTP/2 und langlebigem Verbindungspool: hält die TLS-Verbindung
            // zu api.openai.com warm, damit nicht jedes Diktat nach kurzer Pause den Handshake zahlt.
            var handler = new SocketsHttpHandler
            {
                PooledConnectionIdleTimeout = TimeSpan.FromMinutes(5),
                PooledConnectionLifetime = TimeSpan.FromMinutes(15),
                EnableMultipleHttp2Connections = true
            };
            var httpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(90),
                DefaultRequestVersion = HttpVersion.Version20,
                DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrHigher
            };
            var openAIClient = new OpenAIClient(httpClient);
            var realtimeTranscriber = new RealtimeTranscriber();
            AppLog.Write("OpenAIClient created.");
            var runner = new WorkflowRunner(openAIClient, apiKeyStore, () => settings);
            AppLog.Write("WorkflowRunner created.");

            trayController = new TrayController(
                settings,
                settingsStore,
                apiKeyStore,
                runner,
                realtimeTranscriber);
            AppLog.Write("TrayController created.");
            trayController.Start();
            AppLog.Write("TrayController started.");

            // Verbindung vorwärmen (DNS + TLS + HTTP/2-Pool), damit das erste Diktat schnell ist.
            _ = WarmUpConnectionAsync(httpClient, apiKeyStore);
        }
        catch (Exception ex)
        {
            AppLog.Write($"Startup exception: {ex}");
            throw;
        }
    }

    private static async Task WarmUpConnectionAsync(HttpClient http, ApiKeyStore apiKeyStore)
    {
        try
        {
            var key = apiKeyStore.Load();
            if (string.IsNullOrWhiteSpace(key))
            {
                return;
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.openai.com/v1/models");
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", key);
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            AppLog.Write($"Connection warm-up done status={(int)response.StatusCode}");
        }
        catch (Exception ex)
        {
            AppLog.Write($"Connection warm-up failed (ignored): {ex.Message}");
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        AppLog.Write("OnExit");
        trayController?.Dispose();
        base.OnExit(e);
    }
}
