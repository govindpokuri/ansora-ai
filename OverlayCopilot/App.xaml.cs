using System.Windows;
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using OverlayCopilot.Services;

namespace OverlayCopilot;
public partial class App : Application
{
    private Mutex? runtimeMutex;
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (!e.Args.Contains("--verify-package"))
        {
            runtimeMutex = new Mutex(true, @"Local\AnsoraAI.Desktop.Runtime", out var created);
            if (!created) { runtimeMutex.Dispose(); runtimeMutex = null; MessageBox.Show("Ansora AI is already running. Use Ctrl+Alt+H to restore its window."); Shutdown(); return; }
            new MainWindow().Show(); return;
        }
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        var reportPath = Path.Combine(AppContext.BaseDirectory, "package-check.json");
        var scratch = Path.Combine(Path.GetTempPath(), "call-assistant-check-" + Guid.NewGuid() + ".dpapi");
        var status = 1;
        try
        {
            var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "settings.defaults.json")))!;
            var store = new CredentialStore(scratch);
            if (!store.ImportTestPackage(Path.Combine(AppContext.BaseDirectory, "test-connection.json"), settings.AnswerUrl, settings.AnswerAuth))
                throw new InvalidOperationException("Test credential could not be provisioned.");
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
            using var speech = await WhisperTranscriber.CreateReadyAsync(timeout.Token);
            using var client = new EndpointClient(); var answer = new StringBuilder(); TokenUsage? usage = null;
            await client.AnswerAsync("Reply with exactly READY.", settings, store.Load(settings.AnswerUrl, settings.AnswerAuth), token => answer.Append(token), timeout.Token, value => usage = value);
            if (!answer.ToString().Contains("READY", StringComparison.OrdinalIgnoreCase) || usage is null) throw new InvalidOperationException("AI connection or usage verification failed.");
            File.WriteAllText(reportPath, JsonSerializer.Serialize(new { Success = true, FreshCredentialImport = true, LocalSpeech = true,
                Runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
                SpeechRuntime = Whisper.net.LibraryLoader.RuntimeOptions.LoadedLibrary.ToString(), Usage = usage }, new JsonSerializerOptions { WriteIndented = true }));
            status = 0;
        }
        catch { File.WriteAllText(reportPath, "{\"Success\":false,\"Message\":\"Package verification failed. Check native dependencies, network access, and test credential.\"}"); }
        finally { if (File.Exists(scratch)) File.Delete(scratch); Shutdown(status); }
    }
    protected override void OnExit(ExitEventArgs e)
    {
        if (runtimeMutex is not null) { runtimeMutex.ReleaseMutex(); runtimeMutex.Dispose(); }
        base.OnExit(e);
    }
}
