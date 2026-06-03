using System.Net.Http;
using System.Windows;
using Blitztext.Core.OpenAI;
using Blitztext.Core.Security;
using Blitztext.Core.Settings;
using Blitztext.Core.Workflow;

namespace Blitztext.Windows;

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
            var openAIClient = new OpenAIClient(new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(90)
            });
            AppLog.Write("OpenAIClient created.");
            var runner = new WorkflowRunner(openAIClient, apiKeyStore, () => settings);
            AppLog.Write("WorkflowRunner created.");

            trayController = new TrayController(
                settings,
                settingsStore,
                apiKeyStore,
                runner);
            AppLog.Write("TrayController created.");
            trayController.Start();
            AppLog.Write("TrayController started.");
        }
        catch (Exception ex)
        {
            AppLog.Write($"Startup exception: {ex}");
            throw;
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        AppLog.Write("OnExit");
        trayController?.Dispose();
        base.OnExit(e);
    }
}
