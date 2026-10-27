using System.Net;
using System.Net.Http;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OverlayCopilot;
using OverlayCopilot.Services;

internal static class Program
{
    private static int passed;
    [STAThread]
    public static int Main(string[] args)
    {
        try
        {
            if (args.Length == 2 && args[0] == "--export-test-credential")
            {
                var settings = AppSettings.Load(); new CredentialStore().ExportTestPackage(args[1], settings.AnswerUrl, settings.AnswerAuth);
                Console.WriteLine("Test package credential provisioned. No credential printed."); return 0;
            }
            if (args.Contains("--configure-key"))
            {
                var settings = AppSettings.Load();
                var supplied = Environment.GetEnvironmentVariable("OVERLAYCOPILOT_AI_KEY");
                Environment.SetEnvironmentVariable("OVERLAYCOPILOT_AI_KEY", null);
                if (string.IsNullOrWhiteSpace(supplied)) throw new InvalidOperationException("No background credential supplied.");
                new CredentialStore().Save(settings.AnswerUrl, settings.AnswerAuth, supplied);
                Console.WriteLine("Background AI credential configured with Windows encryption.");
                return 0;
            }
            if (args.Contains("--live"))
            {
                VerifyLiveEndpointAsync().GetAwaiter().GetResult();
                return 0;
            }
            if (args.Contains("--audio-loopback"))
            {
                VerifyLoopbackAsync().GetAwaiter().GetResult(); return 0;
            }
            if (args.Contains("--audio-microphone"))
            {
                VerifyMicrophoneAsync().GetAwaiter().GetResult(); return 0;
            }
            if (args.Contains("--startup-check")) { VerifyStartupConnection(); return 0; }
            if (args.Contains("--update-badge-check")) { VerifyUpdateBadge(); return 0; }
            if (args.Contains("--update-badge-ui-check")) { RenderWindow(false,true); return 0; }
            if (args.Contains("--stream-recovery-check")) { VerifyStreamRecovery(); return 0; }
            if (args.Contains("--options-ui-check")) { RenderWindow(true); return 0; }
            if (args.Contains("--ui-check")) { RenderWindow(); return 0; }
            if (args.Contains("--commercial-check")) { VerifyCommercialAsync().GetAwaiter().GetResult(); return 0; }
            if (args.Contains("--account-ui-check")) { VerifyAccountUi(); return 0; }
            if (args.Contains("--manual-check")) { VerifyManualQuestion(); return 0; }
            if (args.Contains("--local-account-check")) { VerifyLocalAccountAsync().GetAwaiter().GetResult(); return 0; }
            if (args.Contains("--caller-answer-check")) { VerifyCallerAnswerAsync().GetAwaiter().GetResult(); return 0; }
            if (args.Contains("--shutdown-check")) { VerifyWindowShutdown(); return 0; }
            if (args.Contains("--export-check")) { VerifyCallExport(); return 0; }
            if (args.Contains("--efficiency-check")) { VerifyEfficiency(); VerifyCallExport(); VerifyResumeFiles(); return 0; }
            if (args.Contains("--session-check")) { VerifySessionAccounting(); return 0; }
            if (args.Contains("--answer-style-check")) { VerifyAnswerStyleAsync().GetAwaiter().GetResult(); return 0; }
            if (args.Contains("--conversation-style-check")) { VerifyConversationStyleAsync().GetAwaiter().GetResult(); return 0; }
            if (args.Contains("--resume-check")) { VerifyResumeFiles(); VerifyResumeAnswersAsync().GetAwaiter().GetResult(); return 0; }
            if (args.Contains("--resume-ui-check")) { RenderResumeWindow(); return 0; }
            if(args.Contains("--onboarding-check")) { VerifyOnboarding(); Console.WriteLine($"PASS: {passed} onboarding checks"); return 0; }
            if(args.Contains("--product-flow-check")) { VerifyProductFlowAsync().GetAwaiter().GetResult(); Console.WriteLine($"PASS: {passed} product flow checks"); return 0; }
            RunTestsAsync().GetAwaiter().GetResult();
            if (args.Contains("--render")) RenderWindow();
            Console.WriteLine($"PASS: {passed} checks"); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
    private static async Task VerifyLiveEndpointAsync()
    {
        var key = Environment.GetEnvironmentVariable("OVERLAYCOPILOT_AI_KEY") ?? "";
        Environment.SetEnvironmentVariable("OVERLAYCOPILOT_AI_KEY", null);
        
        var settings = AppSettings.Load();
        if (string.IsNullOrWhiteSpace(key)) key = new CredentialStore().Load(settings.AnswerUrl, settings.AnswerAuth);
        using var client = new EndpointClient();
        var response = new StringBuilder();
        var watch = System.Diagnostics.Stopwatch.StartNew();
        TokenUsage? usage = null;
        await client.AnswerAsync("Reply with exactly READY, no other text.", settings, key, token => response.Append(token), default, value => usage = value);
        Check(response.ToString().Trim().Contains("READY", StringComparison.OrdinalIgnoreCase), "live Azure model returns the requested test response");
        Check(usage is { Input: > 0, Output: > 0, Total: > 0 }, "live Azure endpoint reports streaming token usage");
        Console.WriteLine($"Provider token counts: input {usage!.Input}, output {usage.Output}, total {usage.Total}.");
        Console.WriteLine($"Live streamed response verified in {watch.Elapsed.TotalSeconds:F1}s. No plaintext key or provider response saved.");
    }
    private static void Check(bool value, string name)
    {
        if (!value) throw new Exception("FAIL: " + name);
        passed++; Console.WriteLine("PASS: " + name);
    }
    private static async Task Throws<T>(Func<Task> action, string name) where T : Exception
    {
        try { await action(); } catch (T) { Check(true, name); return; }
        throw new Exception("FAIL: " + name);
    }
    private static byte[] Tone(int milliseconds)
    {
        var bytes = new byte[milliseconds * 32];
        for (var i = 0; i < bytes.Length; i += 2)
        { var value = (short)(Math.Sin(i / 2.0 * 2 * Math.PI * 440 / 16000) * 9000); bytes[i] = (byte)value; bytes[i + 1] = (byte)(value >> 8); }
        return bytes;
    }
    private static void VerifyEfficiency()
    {
        var clock = DateTimeOffset.UtcNow;
        var queue = new LiveAnswerQueue();
        queue.Offer("", "What is", "turn1", false, clock);
        Check(queue.TryTake(clock.AddSeconds(5)) is null, "incomplete caller prefixes do not generate paid requests");
        queue.Offer("", "What is dependency injection?", "turn1", false, clock);
        Check(queue.TryTake(clock.AddSeconds(2)) is null, "partial questions wait for a short stability interval");
        queue.Offer("", "What is dependency injection in C#?", "turn1", false, clock.AddSeconds(2));
        Check(queue.TryTake(clock.AddSeconds(3)) is null, "changing partial questions replace the pending draft");
        queue.Offer("Caller: Previous unrelated question", "What is dependency injection in C#?", "turn1", true, clock.AddSeconds(3));
        var first = queue.TryTake(clock.AddSeconds(3.5));
        Check(first is not null && first.Question.EndsWith("C#?") && first.Prompt.Contains("currentQuestion"), "final wording dispatches quickly with an explicit current question");
        queue.Complete(first!.Version);
        for (var i = 4; i < 10; i++) queue.Offer("more transcript context", "What is dependency injection in C#?", "turn1", false, clock.AddSeconds(i));
        Check(queue.TryTake(clock.AddSeconds(12)) is null, "later transcript previews cannot regenerate a submitted caller turn");
        queue.Offer("", "What is dependency injection in C#", "duplicate", true, clock.AddSeconds(12));
        Check(queue.TryTake(clock.AddSeconds(13)) is null, "short-term duplicate text ignores punctuation changes across segment boundaries");
        queue.Offer("", "How do you handle that?", "followup", true, clock.AddSeconds(14));
        var followup = queue.TryTake(clock.AddSeconds(15));
        Check(followup is not null, "contextual follow-up questions remain eligible");
        queue.Complete(followup!.Version);
        queue.Offer("", "Write code to process 10 records", "code", false, clock.AddSeconds(16));
        var code = queue.TryTake(clock.AddSeconds(19)); queue.Complete(code!.Version);
        queue.Offer("", "Write code to process 20 records without LINQ", "code", true, clock.AddSeconds(20));
        var correction = queue.TryTake(clock.AddSeconds(21));
        Check(correction is not null && correction.Question.Contains("without LINQ"), "a final number or constraint correction is answered once");
        queue.Complete(correction!.Version);
        queue.Offer("", "Write code to process 30 records without LINQ", "code", true, clock.AddSeconds(22));
        Check(queue.TryTake(clock.AddSeconds(24)) is null, "one caller turn cannot trigger an unlimited correction loop");
        queue.Offer("", "Explain garbage collection", "next1", true, clock.AddSeconds(25));
        var active = queue.TryTake(clock.AddSeconds(26));
        queue.Offer("", "Explain polymorphism", "next2", true, clock.AddSeconds(27));
        queue.Offer("", "Compare threads and processes", "next3", true, clock.AddSeconds(28));
        Check(queue.TryTake(clock.AddSeconds(30)) is null, "only one answer request can run at a time");
        queue.Complete(active!.Version);
        var latest = queue.TryTake(clock.AddSeconds(31));
        Check(latest?.Question == "Compare threads and processes", "the newest pending question wins while the previous answer streams");
        queue.Reset(); queue.Complete(latest!.Version);
        queue.Offer("", "Explain deadlocks", "new-session", true, clock.AddSeconds(32));
        var fresh = queue.TryTake(clock.AddSeconds(33)); queue.Complete(latest.Version);
        queue.Offer("", "Explain async await", "new-followup", true, clock.AddSeconds(34));
        Check(fresh is not null && queue.TryTake(clock.AddSeconds(35)) is null, "stale completions cannot release a new session's active request");
        var context = string.Join("\n", Enumerable.Range(0, 20).Select(i => "Caller: Previous question number " + i + new string('x', 600)));
        var prompt = QuestionPrompt.Build("What is a deadlock?", context);
        using var payload = JsonDocument.Parse(prompt[(prompt.IndexOf('\n') + 1)..]);
        Check(payload.RootElement.GetProperty("currentQuestion").GetString() == "What is a deadlock?" && payload.RootElement.GetProperty("recentCallerContext").GetArrayLength() == 3 && prompt.Length < 1700, "recent context is bounded and cannot bury the current question");
        var profile = SampleCandidate() with { ResumeText = "Contact: private@example.com\n" + string.Concat(Enumerable.Repeat("Unrelated resume history with filler. ", 300)) + "\nBuilt Python order tracking service.", Experience = "Built a Python order tracking service." };
        var compact = AnswerPresentation.ComposePrompt("", profile, "Explain Python order tracking");
        Check(compact.Length < 3000 && !compact.Contains("private@example.com") && !compact.Contains("Unrelated resume history"), "each answer uses compact reviewed facts and relevant excerpts instead of the full resume");
        Check(!CallerQuestion.IsReady("What is the difference between process and process?") && !CallerQuestion.IsReady("Can you explain...") && CallerQuestion.IsReady("Can you explain this one?"), "obvious incomplete recognition is filtered without losing valid follow-ups");
        foreach (var streaming in new[] { true, false })
        {
            var content = streaming
                ? "data: {\"model\":\"test-model\"}\n\ndata: {\"choices\":[{\"delta\":{\"content\":\"Short answer\"},\"finish_reason\":\"stop\"}]}\n\ndata: [DONE]\n\n"
                : "{\"model\":\"test-model\",\"choices\":[{\"message\":{\"content\":\"Short answer\"},\"finish_reason\":\"stop\"}]}";
            using var client = new EndpointClient(new FakeHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(content, Encoding.UTF8, streaming ? "text/event-stream" : "application/json") })));
            string resolved = "", finished = "";
            client.AnswerAsync("Explain threads", new AppSettings { AnswerUrl = "http://localhost/chat", AnswerModel = "test" }, "", _ => {}, default,
                reportMetadata: (model, reason) => { if(model.Length > 0) resolved = model; if(reason.Length > 0) finished = reason; }).GetAwaiter().GetResult();
            Check(resolved == "test-model" && finished == "stop", $"{(streaming ? "streaming" : "JSON")} response metadata reaches call records");
        }
        Console.WriteLine("Efficiency regression checks completed.");
    }
    private static void VerifyCallExport()
    {
        using var missing = JsonDocument.Parse("{\"usage\":null}");
        using var invalid = JsonDocument.Parse("{\"usage\":{\"prompt_tokens\":\"unknown\",\"completion_tokens\":3}}");
        Check(TokenUsage.Read(missing.RootElement) is null && TokenUsage.Read(invalid.RootElement) is null, "missing or malformed usage is unknown rather than zero");
        var longCall = new CallArchive();
        for (var i = 0; i < 40; i++) longCall.UpdateCaller(1, i, $"Turn {i}: " + new string('a', 600), true);
        Check(longCall.Snapshot().Transcript.Length == 40 && longCall.Snapshot().Transcript[0].Text.StartsWith("Turn 0:"), "export history retains early caller turns beyond the rolling context limit");
        longCall.UpdateCaller(1, 39, "Revised final wording", false);
        longCall.UpdateCaller(1, 39, "Revised final wording.", true);
        longCall.UpdateCaller(2, 39, "A new capture period", false);
        Check(longCall.Snapshot().Transcript.Length == 41 && longCall.Snapshot().Transcript[39].Text == "Revised final wording.", "draft revisions replace their own turn and resumed capture keeps distinct segments");
        var archive = new CallArchive(); archive.StartCapture("Synthetic test headphones");
        archive.UpdateCaller(1, 1, "What is the difference between a process and a thread?", true);
        var settings = new AppSettings();
        var first = archive.StartAnswer("What is the difference between a process and a thread?", "Caller: What is the difference between a process and a thread?", settings);
        first.Append("A **process** runs with its own memory space. A **thread** runs inside a process and shares that memory with other threads.\n\nI would use separate processes when isolation matters, and threads when tasks need to share data efficiently.\n\n- Processes provide stronger isolation.\n- Threads are lighter but need synchronization for shared data.\n\nFor example, a desktop app can use a worker thread to keep the interface responsive during a long calculation.");
        first.ResolvedModel = "synthetic-model"; first.FinishReason = "stop";
        first.Usage = new(1200, 650, 1850, 400); first.Usage = new(1200, 650, 1850, 400); first.Finish("Completed");
        var second = archive.StartAnswer("Can you show an example?", "Caller: Can you show an example?", settings);
        second.Append("```csharp\nvar task = Task.Run(Work);\n```\n\nThis offloads the work. <script>alert('unsafe')</script>"); second.Finish("Canceled · partial answer");
        archive.EndCapture();
        var report = archive.Snapshot();
        Check(report.ReportedUsage.Total == 1850 && report.MissingUsageRequests == 1 && report.Answers.Length == 2, "request usage is counted once; canceled requests with missing usage are flagged");
        var html = CallExporter.Html(report); var json = CallExporter.Json(report);
        Check(html.Contains("1,850") && html.Contains("Complete caller transcript") && html.Contains("partial answer") && html.Contains("<strong>process</strong>"), "report contains transcript, formatted answers, status, and token totals");
        Check(!html.Contains("<script>") && html.Contains("&lt;script&gt;") && html.Contains("<pre><code>"), "export escapes untrusted transcript and answer content while retaining code blocks");
        using var parsed = JsonDocument.Parse(json);
        Check(parsed.RootElement.GetProperty("Answers").GetArrayLength() == 2 && parsed.RootElement.GetProperty("Transcript").GetArrayLength() == 1 && parsed.RootElement.GetProperty("ReportedUsage").GetProperty("Total").GetInt64() == 1850, "JSON export retains complete reusable call data and token totals");
        Check(!json.Contains("ApiKey") && !json.Contains(settings.AnswerUrl) && !json.Contains("answer-key.dpapi"), "exports contain neither endpoint credentials nor credential storage paths");
        Check(report.Answers[0].Model == "synthetic-model" && report.Answers[0].FirstTokenAt is not null && report.Answers[0].FinishReason == "stop" && report.Answers[0].Instructions.Contains("25–50 words"), "exports retain actual prompt, resolved model, first-text time and finish reason");
        first.Append("Later change");
        Check(!report.Answers[0].Answer.Contains("Later change") && report.CapturePeriods.Single().EndedAt is not null, "export snapshots stay stable and preserve stopped capture timestamps");
        Directory.CreateDirectory("artifacts");
        File.WriteAllText("artifacts/call-report-preview.html", html, new UTF8Encoding(false));
        File.WriteAllText("artifacts/call-report-preview.json", json, new UTF8Encoding(false));
        var atomicPath = Path.Combine("artifacts", "atomic-export-test.html");
        try
        {
            CallExporter.WriteAtomicAsync(atomicPath, html).GetAwaiter().GetResult();
            Check(File.ReadAllText(atomicPath).EndsWith("</html>"), "atomic export saves a complete HTML document");
            try { CallExporter.WriteAtomicAsync(atomicPath, "<html>truncated").GetAwaiter().GetResult(); throw new Exception("Incomplete export was accepted"); }
            catch (InvalidDataException) { Check(File.ReadAllText(atomicPath) == html, "incomplete exports cannot overwrite a previously complete report"); }
        }
        finally { if (File.Exists(atomicPath)) File.Delete(atomicPath); }
    }
    private static async Task RunTestsAsync()
    {
        await VerifyUpdatesAsync();
        await VerifyCommercialAsync();
        VerifyResumeFiles();
        var streamBuffer = new AnswerStreamBuffer("question", 7);
        await Task.Run(() => { for (var i = 0; i < 1000; i++) streamBuffer.Append(i + ","); });
        Check(streamBuffer.Drain() == string.Concat(Enumerable.Range(0, 1000).Select(i => i + ",")) && streamBuffer.Drain() == "", "background tokens are batched without loss or repeated delivery");
        VerifyCallExport();
        var callerTranscript = new LiveTranscript(callerOnly: true);
        callerTranscript.Update(1, "You", 1, "My own voice should not reach the LLM", false);
        Check(callerTranscript.Context.Length == 0, "caller-only feed rejects microphone text");
        callerTranscript.Update(1, "Caller", 1, "What is dependency injection?", false);
        Check(callerTranscript.Display.Contains("Caller (live)") && CallerQuestion.IsReady("What is dependency injection?"), "a live caller question is ready before pause detection");
        Check(CallerQuestion.IsReady("Explain polymorphism") && CallerQuestion.IsReady("Can you compare threads and processes"), "short caller requests and comparison questions trigger answers");
        Check(!CallerQuestion.IsReady("What is") && !CallerQuestion.IsReady("Tell me") && !CallerQuestion.IsReady("Thank you for joining today"), "incomplete question prefixes and ordinary greetings do not trigger answers");
        var bearerDefault = AppSettings.Normalize(new AppSettings { AnswerUrl = "https://api.example.com/v1/chat/completions", AnswerAuth = "Bearer" });
        Check(bearerDefault.AnswerAuth == "Bearer", "Community configuration preserves explicit Bearer authentication");
        var migratedSettings = AppSettings.Normalize(new AppSettings { AnswerUrl = "", AnswerModel = "", UseMicrophone = true, UseBothAudio = true });
        Check(migratedSettings.AnswerUrl == "https://api.example.com/v1/chat/completions" && migratedSettings.AnswerModel == "gpt-oss-120b" && !migratedSettings.UseMicrophone && !migratedSettings.UseBothAudio, "empty settings recover the placeholder caller-only connection");
        var live = new LiveTranscript();
        live.Update(1, "You", 1, "Please explain how a database", false);
        live.Update(1, "You", 1, "Please explain how a database transaction works", false);
        Check(live.Display.Contains("(live)") && !live.Display.Contains("database\n"), "live revisions replace their own turn before any pause");
        live.Update(1, "Call", 1, "Transactions provide atomicity.", false);
        Check(live.Context.Contains("You:") && live.Context.Contains("Call:"), "LLM context includes both independent audio sources");
        live.Update(1, "You", 1, "Please explain how a database transaction works.", true);
        Check(live.Display.Split("Please explain").Length == 2, "finalizing a live draft does not duplicate words");
        live.Update(2, "You", 1, "New listening session", true);
        Check(live.Context.Contains("New listening session") && live.Context.Contains("transaction"), "restarted input IDs preserve older transcript turns");
        VerifyEfficiency();
        Directory.CreateDirectory("artifacts");
        var credentialPath = Path.GetFullPath("artifacts/test-answer-key.dpapi");
        var credential = new CredentialStore(credentialPath);
        try
        {
            credential.Save("https://example.com/chat", "api-key", "synthetic-test-key");
            Check(!Encoding.UTF8.GetString(File.ReadAllBytes(credentialPath)).Contains("synthetic-test-key"), "remembered key is encrypted on disk");
            Check(credential.Load("https://example.com/chat", "api-key") == "synthetic-test-key", "Windows user can restore their remembered key");
            Check(credential.Load("https://example.com/chat?api-version=new", "api-key") == "synthetic-test-key", "API version changes do not lose the resource credential");
            Check(credential.Load("https://different.example/chat", "api-key") == "", "remembered key is not reused for a changed endpoint");
            Check(credential.Load("https://example.com/chat", "Bearer") == "", "remembered key is bound to its authentication mode");
            var packageCredentialPath = Path.GetFullPath("artifacts/synthetic-package-connection.json");
            var testerCredential = new CredentialStore(Path.GetFullPath("artifacts/synthetic-tester-key.dpapi"));
            try
            {
                credential.ExportTestPackage(packageCredentialPath, "https://example.com/chat", "api-key");
                Check(testerCredential.ImportTestPackage(packageCredentialPath, "https://example.com/chat", "api-key") && testerCredential.Load("https://example.com/chat", "api-key") == "synthetic-test-key", "fresh tester credential store imports the portable test credential");
                Check(!testerCredential.ImportTestPackage(packageCredentialPath, "https://other.example/chat", "api-key") && !testerCredential.ImportTestPackage(packageCredentialPath, "https://example.com/chat", "Bearer"), "test package import rejects another resource or auth mode");
            }
            finally { testerCredential.Forget(); File.Delete(packageCredentialPath); }
            credential.Forget(); Check(!File.Exists(credentialPath), "forget removes the stored credential");
        }
        finally { credential.Forget(); }
        using var noKeyClient = new EndpointClient(new FakeHandler(_ => throw new Exception("Missing key must never reach HTTP")));
        await Throws<ArgumentException>(() => noKeyClient.AnswerAsync("q", new AppSettings { AnswerUrl = "https://example.com/chat", AnswerModel = "test" }, "", _ => { }, default), "empty AI key is blocked before an unauthorized remote request");
        var stalledCleanup = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var closeWatch = System.Diagnostics.Stopwatch.StartNew();
        await ShutdownCoordinator.CompleteWithinAsync(stalledCleanup.Task, TimeSpan.FromMilliseconds(100));
        Check(closeWatch.Elapsed < TimeSpan.FromSeconds(2), "shutdown remains bounded when native cleanup stalls");
        await ShutdownCoordinator.CompleteWithinAsync(Task.FromException(new IOException("device stopped")), TimeSpan.FromMilliseconds(100));
        Check(true, "cleanup failure does not prevent shutdown");
        stalledCleanup.SetResult();
        Check(AppSettings.ValidateEndpoint("https://example.com/chat").Scheme == "https", "HTTPS endpoint accepted");
        Check(AppSettings.ValidateEndpoint("http://localhost:8080/chat").IsLoopback, "local HTTP accepted");
        await Throws<ArgumentException>(() => Task.Run(() => AppSettings.ValidateEndpoint("http://example.com/chat")), "remote HTTP rejected");
        await Throws<ArgumentException>(() => Task.Run(() => AppSettings.ValidateEndpoint("https://secret@example.com/chat")), "URL credentials rejected");
        Check(!JsonSerializer.Serialize(new AppSettings()).Contains("Key"), "settings contain no API keys");
        var now = DateTimeOffset.UtcNow;
        var segmenter = new AudioSegmenter();
        Check(segmenter.Feed(new byte[32000], 32000, now).Count == 0, "silence produces no transcription requests");
        var tone = Tone(3200);
        var chunks = segmenter.Feed(tone, tone.Length, now);
        Check(chunks.Count == 1 && chunks[0].Preview && chunks[0].Pcm.Length == 64000, "continuous audio yields a live draft without discarding context");
        var end = segmenter.Tick(now.AddMilliseconds(1200));
        Check(end.Count == 1 && end[0].EndOfUtterance && end[0].Pcm.Length == tone.Length + 9600 && end[0].Pcm.TakeLast(tone.Length).SequenceEqual(tone), "missing loopback callbacks finish an utterance");
        Check(segmenter.Tick(now.AddSeconds(2)).Count == 0, "idle marker is not repeated");
        var emptyCallbacks = new AudioSegmenter(); var phrase = Tone(500);
        emptyCallbacks.Feed(phrase, phrase.Length, now);
        for (var i = 1; i <= 8; i++) emptyCallbacks.Feed(Array.Empty<byte>(), 0, now.AddMilliseconds(100 * i));
        Check(emptyCallbacks.Tick(now.AddMilliseconds(1200)).Single().EndOfUtterance, "empty WASAPI callbacks do not postpone short-phrase transcription");
        var fragmented = new AudioSegmenter(); var output = new List<AudioChunk>();
        for (var i = 0; i < tone.Length; i += 137)
        { var part = tone.Skip(i).Take(137).ToArray(); output.AddRange(fragmented.Feed(part, part.Length, now)); }
        output.AddRange(fragmented.Tick(now.AddSeconds(2)));
        Check(output.Where(x => !x.Preview).SelectMany(x => x.Pcm).SequenceEqual(tone), "odd callback boundaries preserve all PCM samples");
        var pause = new AudioSegmenter(); var shortTone = Tone(300);
        pause.Feed(shortTone, shortTone.Length, now);
        Check(pause.Feed(new byte[32000], 32000, now).Single().EndOfUtterance, "one-second silence finalizes the full phrase");
        var wav = AudioSegmenter.ToWav(shortTone);
        Check(Encoding.ASCII.GetString(wav, 0, 4) == "RIFF" && BitConverter.ToInt32(wav, 24) == 16000 && BitConverter.ToInt32(wav, 40) == shortTone.Length, "valid 16-kHz PCM WAV header");
        var settings = new AppSettings { TranscriptionUrl = "http://localhost/transcribe", AnswerUrl = "http://localhost/chat?api-version=2024-05-01-preview", AnswerModel = "gpt-oss-120b", Candidate = SampleCandidate() };
        var handler = new FakeHandler(async request =>
        {
            if (request.RequestUri!.AbsolutePath == "/transcribe")
            {
                Check(request.Headers.Authorization?.Parameter == "speech-key", "transcription Bearer header");
                var payload = await request.Content!.ReadAsByteArrayAsync();
                Check(Encoding.UTF8.GetString(payload).Contains("audio.wav") && Encoding.UTF8.GetString(payload).Contains("RIFF"), "multipart request carries WAV file");
                return Json("{\"text\":\"Explain a thread.\"}");
            }
            Check(request.Headers.GetValues("api-key").Single() == "azure-key" && request.Headers.Authorization is null, "Azure api-key authentication");
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            Check(body.RootElement.GetProperty("model").GetString() == "gpt-oss-120b" && body.RootElement.GetProperty("stream").GetBoolean(), "AI request model and streaming flag");
            Check(body.RootElement.GetProperty("stream_options").GetProperty("include_usage").GetBoolean(), "streamed requests ask the endpoint for usage");
            Check(body.RootElement.GetProperty("max_tokens").GetInt32() == 4096 && body.RootElement.GetProperty("temperature").GetDouble() == 0.1, "generation limits and temperature match configuration");
            Check(request.RequestUri.Query == "?api-version=2024-05-01-preview", "Azure API version query is preserved");
            Check(body.RootElement.GetProperty("messages")[1].GetProperty("content").GetString() == "Explain a thread.", "transcript reaches answer endpoint");
            var instructions = body.RootElement.GetProperty("messages")[0].GetProperty("content").GetString()!;
            Check(instructions.Contains("25–50 words") && instructions.Contains("No introductions") && instructions.Contains(settings.SystemPrompt[..Math.Min(settings.SystemPrompt.Length, 450)]), "compact spoken guidance and bounded custom preferences reach the endpoint");
            Check(instructions.Contains("order tracking") && instructions.Contains("Python") && instructions.Contains("Do not produce code in an unlisted language"), "actual HTTP answer request includes reviewed resume facts and language restrictions");
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("data: {\"choices\":[{\"delta\":{\"role\":\"assistant\"}}],\"usage\":null}\n\ndata: {\"choices\":[{\"delta\":{\"content\":\"A thread \"}}]}\n\ndata: {\"choices\":[{\"delta\":{\"content\":\"shares memory.\"}}]}\n\ndata: {\"choices\":[],\"usage\":{\"prompt_tokens\":120,\"completion_tokens\":35,\"total_tokens\":155}}\n\ndata: [DONE]\n\n", Encoding.UTF8, "text/event-stream") };
        });
        using var client = new EndpointClient(handler);
        var transcript = await client.TranscribeAsync(wav, settings, "speech-key", default);
        var answer = new StringBuilder();
        TokenUsage? streamedUsage = null;
        await client.AnswerAsync(transcript, settings, "azure-key", token => answer.Append(token), default, usage => streamedUsage = usage);
        Check(answer.ToString() == "A thread shares memory.", "SSE answer tokens assembled");
        Check(streamedUsage is { Input: 120, Output: 35, Total: 155 }, "usage-only SSE chunk with no choices is retained");
        using var jsonClient = new EndpointClient(new FakeHandler(_ => Task.FromResult(Json("{\"choices\":[{\"message\":{\"content\":\"JSON answer\"}}],\"usage\":{\"prompt_tokens\":10,\"completion_tokens\":3,\"total_tokens\":13}}"))));
        TokenUsage? jsonUsage = null;
        answer.Clear(); await jsonClient.AnswerAsync("q", settings, "", token => answer.Append(token), default, value => jsonUsage = value);
        Check(answer.ToString() == "JSON answer", "nonstreaming response fallback");
        Check(jsonUsage is { Total: 13 }, "nonstreaming response usage is captured");
        await Throws<ArgumentException>(() => jsonClient.AnswerAsync("q", settings with { MaxTokens = 0 }, "", _ => { }, default), "invalid token limit rejected before HTTP request");
        await Throws<ArgumentException>(() => jsonClient.AnswerAsync("q", settings with { Temperature = double.NaN }, "", _ => { }, default), "invalid temperature rejected before HTTP request");
        using var bad = new EndpointClient(new FakeHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized) { Content = new StringContent("secret body") })));
        await Throws<HttpRequestException>(() => bad.AnswerAsync("q", settings, "", _ => { }, default), "HTTP authentication failure handled");
        using var malformed = new EndpointClient(new FakeHandler(_ => Task.FromResult(Json("{\"other\":123}"))));
        await Throws<InvalidDataException>(() => malformed.TranscribeAsync(wav, settings, "", default), "invalid transcription schema rejected");
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        await Throws<OperationCanceledException>(() => client.AnswerAsync("q", settings, "", _ => { }, canceled.Token), "requests respect cancellation");
        using var speech = await WhisperTranscriber.CreateReadyAsync(default);
        Check(true, "local Whisper model and native runtime initialize"); Console.WriteLine("Runtime: " + Whisper.net.LibraryLoader.RuntimeOptions.LoadedLibrary);
        var questions = new[] {
            "What is the difference between a process and a thread?",
            "I am explaining how we deploy our application to Azure and monitor the database.",
            "The microphone must capture my voice while the call audio captures the other speaker."
        };
        var verifiedWaves = new List<byte[]>();
        foreach (var question in questions)
        {
            using var synthesized = new MemoryStream();
            using (var voice = new System.Speech.Synthesis.SpeechSynthesizer())
            {
                voice.SetOutputToAudioStream(synthesized, new System.Speech.AudioFormat.SpeechAudioFormatInfo(16000, System.Speech.AudioFormat.AudioBitsPerSample.Sixteen, System.Speech.AudioFormat.AudioChannel.Mono));
                voice.Speak(question); voice.SetOutputToNull();
            }
            var pcm = synthesized.ToArray();
            verifiedWaves.Add(AudioSegmenter.ToWav(pcm));
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var recognized = await speech.TranscribeAsync(AudioSegmenter.ToWav(pcm), default);
            var expected = question.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim('.', '?', ',').ToLowerInvariant()).Where(s => s.Length > 3).ToArray();
            Check(expected.Count(s => recognized.Contains(s, StringComparison.OrdinalIgnoreCase)) >= expected.Length * .75, "Whisper recovers key words in a synthetic phrase");
            Console.WriteLine($"Decode {pcm.Length / 32000.0:F1}s audio in {watch.Elapsed.TotalSeconds:F2}s. Synthetic text: {recognized}");
            var streaming = new AudioSegmenter();
            var frames = new List<AudioChunk>();
            for (var offset = 0; offset < pcm.Length; offset += 3200)
            {
                var part = pcm.Skip(offset).Take(3200).ToArray();
                frames.AddRange(streaming.Feed(part, part.Length, now.AddMilliseconds(offset / 32)));
            }
            frames.AddRange(streaming.Tick(now.AddMilliseconds(pcm.Length / 32 + 1200)));
            Check(frames.Any(c => !c.Preview && c.EndOfUtterance), "streaming segmentation commits spoken audio");
            foreach (var frame in frames.Where(c => !c.Preview && c.Pcm.Length >= 6400))
                Check(!string.IsNullOrWhiteSpace(await speech.TranscribeAsync(AudioSegmenter.ToWav(frame.Pcm), default)), "Whisper decodes a committed streaming phrase");
        }
        var simultaneous = await Task.WhenAll(speech.TranscribeAsync(verifiedWaves[0], default), speech.TranscribeAsync(verifiedWaves[1], default));
        Check(simultaneous[0].Contains("thread", StringComparison.OrdinalIgnoreCase) && simultaneous[1].Contains("Azure", StringComparison.OrdinalIgnoreCase), "simultaneous audio sources decode independently through the shared engine");
        using var canceledSpeech = new CancellationTokenSource(); canceledSpeech.Cancel();
        await Throws<OperationCanceledException>(() => speech.TranscribeAsync(wav, canceledSpeech.Token), "Whisper respects canceled sessions");
        using var activeCancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(30));
        await Throws<OperationCanceledException>(() => speech.TranscribeAsync(AudioSegmenter.ToWav(Tone(5000)), activeCancellation.Token), "active inference cancels safely");
        var longPhrase = new AudioSegmenter(); var continuous = Tone(16200);
        var longChunks = longPhrase.Feed(continuous, continuous.Length, now);
        Check(longChunks.Count(c => !c.Preview) == 1 && longChunks.Single(c => !c.Preview).Pcm.Length == 480000, "long speech commits bounded context without duplicate draft commits");
        Check(longPhrase.Tick(now.AddSeconds(2)).Single().Pcm.Length == 38400, "speech following the context cap is retained");
    }
    private static async Task VerifyUpdatesAsync()
    {
        const string origin = "https://example.com";
        var bytes = Encoding.UTF8.GetBytes("synthetic-installer");
        var release = new AppRelease(true, "99.0.0", origin + "/updates/AnsoraAI-Setup-99.0.0.exe", Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)), bytes.Length, "Synthetic release");
        using var client = new AppUpdates(new FakeHandler(request => Task.FromResult(request.RequestUri!.AbsolutePath.EndsWith(".json")
            ? Json(JsonSerializer.Serialize(release)) : new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) })));
        Check((await client.CheckAsync(origin, default))?.Version == "99.0.0", "a newer valid HTTPS release is offered");
        var directory = Path.Combine(Path.GetTempPath(), "ansora-update-tests-" + Guid.NewGuid());
        try
        {
            var path = await client.DownloadAsync(release, origin, directory, default);
            Check(File.ReadAllBytes(path).SequenceEqual(bytes), "verified update download preserves the exact installer bytes");
            File.Delete(path);
            await Throws<InvalidDataException>(() => client.DownloadAsync(release with { Sha256 = new string('0', 64) }, origin, directory, default), "a modified installer is rejected before launch");
            Check(!Directory.GetFiles(directory).Any(), "failed update downloads leave no executable or partial file");
            await Throws<InvalidDataException>(() => client.DownloadAsync(release with { Size = bytes.Length + 1 }, origin, directory, default), "truncated or mismatched update length is rejected");
            await Throws<InvalidDataException>(() => client.DownloadAsync(release with { Url = "https://other.example/update.exe" }, origin, directory, default), "update binaries from another origin are rejected");
            await Throws<InvalidDataException>(() => client.DownloadAsync(release with { Url = "http://example.com/update.exe" }, origin, directory, default), "unencrypted installer URLs are rejected");
            await Throws<InvalidDataException>(() => client.DownloadAsync(release with { Version = "0.0.1" }, origin, directory, default), "downgrade installation is rejected");
            using var canceled = new CancellationTokenSource(); canceled.Cancel();
            await Throws<OperationCanceledException>(() => client.DownloadAsync(release, origin, directory, canceled.Token), "update downloading honors app shutdown cancellation");
            using var disabled = new AppUpdates(new FakeHandler(_ => Task.FromResult(Json("{\"enabled\":false}"))));
            Check(await disabled.CheckAsync(origin, default) is null, "a withdrawn or unpublished release is not offered");
            using var current = new AppUpdates(new FakeHandler(_ => Task.FromResult(Json(JsonSerializer.Serialize(release with { Version = AppUpdates.CurrentVersion.ToString(3) })))));
            Check(await current.CheckAsync(origin, default) is null, "the installed version is not offered again");
            using var missing = new AppUpdates(new FakeHandler(_ => Task.FromResult(Json("{\"enabled\":true}"))));
            await Throws<InvalidDataException>(() => missing.CheckAsync(origin, default), "incomplete update manifests are rejected");
            using var oversized = new AppUpdates(new FakeHandler(_ => Task.FromResult(Json(new string('a', 20000)))));
            await Throws<InvalidDataException>(() => oversized.CheckAsync(origin, default), "oversized update manifests are rejected before parsing");
            using var redirect = new AppUpdates(new FakeHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Redirect))));
            await Throws<HttpRequestException>(() => redirect.CheckAsync(origin, default), "update redirects cannot bypass origin checks");
            await Throws<InvalidDataException>(() => client.CheckAsync("http://example.com", default), "update checks require HTTPS");
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
    private static HttpResponseMessage Json(string data) => new(HttpStatusCode.OK) { Content = new StringContent(data, Encoding.UTF8, "application/json") };
    private static async Task VerifyLoopbackAsync()
    {
        var defaultId = LoopbackListener.GetDefaultDeviceId();
        Check(LoopbackListener.GetDevices().Any(d => d.Id == defaultId), "system default output is present in the device selector");
        using var synthesized = new MemoryStream();
        using (var voice = new System.Speech.Synthesis.SpeechSynthesizer())
        {
            voice.SetOutputToWaveStream(synthesized);
            voice.Speak("What is the difference between a process and a thread?");
            voice.SetOutputToNull();
        }
        using var enumerator = new NAudio.CoreAudioApi.MMDeviceEnumerator();
        using var device = enumerator.GetDevice(defaultId);
        using var wav = new NAudio.Wave.WaveFileReader(new MemoryStream(synthesized.ToArray()));
        using var playback = new NAudio.Wave.WasapiOut(device, NAudio.CoreAudioApi.AudioClientShareMode.Shared, false, 100);
        playback.Init(wav);
        await using var capture = new LoopbackListener(defaultId);
        using var speech = await WhisperTranscriber.CreateReadyAsync(default);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        var recognized = new StringBuilder();
        capture.Start(); playback.Play();
        await foreach (var chunk in capture.Reader.ReadAllAsync(timeout.Token))
        {
            if (!chunk.Preview && chunk.Pcm.Length > 0) recognized.Append(await speech.TranscribeAsync(AudioSegmenter.ToWav(chunk.Pcm), timeout.Token));
            if (chunk.EndOfUtterance) break;
        }
        playback.Stop();
        Check(capture.ReceivedBytes > 0, "WASAPI captures real output-device packets at 16 kHz");
        Check(!string.IsNullOrWhiteSpace(recognized.ToString()), "output playback → segmentation → local transcription produces words");
        Console.WriteLine("Hardware audio pipeline verified. Transcript and raw audio were not saved.");
    }
    private static async Task VerifyMicrophoneAsync()
    {
        var defaultId = LoopbackListener.GetDefaultDeviceId(true);
        Check(LoopbackListener.GetDevices(true).Any(d => d.Id == defaultId), "default microphone is present in the input selector");
        await using var capture = new LoopbackListener(defaultId, true);
        capture.Start();
        for (var i = 0; i < 30 && capture.ReceivedBytes == 0; i++) await Task.Delay(100);
        Check(capture.ReceivedBytes > 0, "default microphone delivers PCM16 packets at 16 kHz");
        Console.WriteLine("Microphone capture verified. No audio was transcribed, saved, or sent externally.");
    }
    private static async Task VerifyLocalAccountAsync()
    {
        using var client = new CommercialAccountClient();
        var email = "windows-account-check-" + Guid.NewGuid().ToString("N") + "@example.test";
        var result = await client.StartDetailedAsync("http://127.0.0.1:8181",email,default);
        Check(result.Message.Contains("local outbox") && !result.Message.Contains("Code sent"), "desktop displays the actual local delivery status rather than claiming email was sent");
        var path = Path.Combine("commercial","storage","outbox",result.Challenge + ".json");
        using var mail = JsonDocument.Parse(await File.ReadAllTextAsync(path));
        var session = await client.VerifyAsync("http://127.0.0.1:8181",result.Challenge,mail.RootElement.GetProperty("code").GetString()!,default);
        var balance = await client.BalanceAsync(session,default);
        Check(session.Email == email && balance.Email == email && balance.Available == 0, "Windows account client completes local OTP sign-in and reads the correct server balance");
        await client.LogoutAsync(session,default);
    }
    private static void VerifyManualQuestion()
    {
        var app = new App(); app.InitializeComponent();
        var window = new MainWindow();
        SynchronizationContext.SetSynchronizationContext(new System.Windows.Threading.DispatcherSynchronizationContext(window.Dispatcher));
        var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        var endpointField = typeof(MainWindow).GetField("endpoints", flags)!;
        ((EndpointClient)endpointField.GetValue(window)!).Dispose();
        var requests = 0;
        endpointField.SetValue(window, new EndpointClient(new FakeHandler(async request =>
        {
            Interlocked.Increment(ref requests);
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            var messages = body.RootElement.GetProperty("messages");
            Check(messages[1].GetProperty("content").GetString()!.Contains("def solve(nums):\n    pass"), "pasted code reaches AI with indentation and line breaks intact");
            Check(messages[1].GetProperty("content").GetString()!.Contains("complete solution in a fenced code block") && messages[0].GetProperty("content").GetString()!.Contains("Python"), "coding requests include solution instructions and the reviewed resume languages");
            return Json("{\"choices\":[{\"message\":{\"content\":\"Use a set.\\n```python\\ndef solve(nums):\\n    return len(set(nums))\\n```\"}}],\"usage\":{\"prompt_tokens\":50,\"completion_tokens\":30,\"total_tokens\":80}}");
        })));
        typeof(MainWindow).GetField("candidate", flags)!.SetValue(window, SampleCandidate());
        ((System.Windows.Controls.TextBox)window.FindName("AnswerUrl")).Text = "http://127.0.0.1:8182/chat/completions";
        ((System.Windows.Controls.TextBox)window.FindName("AnswerModel")).Text = "synthetic-model";
        var tabs = (System.Windows.Controls.TabControl)window.FindName("QuestionTabs");
        var input = (System.Windows.Controls.TextBox)window.FindName("ManualQuestionBox");
        tabs.SelectedIndex = 1;
        var question = "Count distinct items in Python.\ndef solve(nums):\n    pass";
        input.Text = question;
        ((System.Windows.Controls.TextBox)window.FindName("QuestionBox")).Text = "New caller speech";
        Check(input.Text == question && input.AcceptsReturn && input.AcceptsTab, "caller context cannot overwrite the separate multiline coding draft");
        var send = typeof(MainWindow).GetMethod("AnswerManualAsync", flags)!;
        var task = (Task)send.Invoke(window, null)!;
        var frame = new System.Windows.Threading.DispatcherFrame();
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
        timer.Tick += (_, _) => frame.Continue = false; timer.Start();
        task.ContinueWith(_ => window.Dispatcher.BeginInvoke(() => frame.Continue = false));
        System.Windows.Threading.Dispatcher.PushFrame(frame); timer.Stop();
        Check(task.IsCompletedSuccessfully && requests == 1, "pasted question completes through the existing AI request path");
        var report = ((CallArchive)typeof(MainWindow).GetField("callArchive", flags)!.GetValue(window)!).Snapshot();
        Check(report.Answers.Length == 1 && report.Answers[0].Question == question && report.ReportedUsage.Total == 80, "manual question and provider token usage are retained in the call export");
        Check(((System.Windows.Controls.TextBlock)window.FindName("AnswerQuestionText")).Text.Contains("Count distinct") && ((System.Windows.Controls.TextBox)window.FindName("AnswerBox")).Text.Contains("def solve"), "coding answer displays against the pasted question");
        var history = (System.Windows.Controls.ComboBox)window.FindName("AnswerHistory");
        var hold = (System.Windows.Controls.CheckBox)window.FindName("PinAnswer");
        Check(history.IsEnabled && history.Items.Count == 2, "completed answers are added to selectable recent history");
        history.SelectedIndex = 1;
        Check(hold.IsChecked == true && ((System.Windows.Controls.TextBox)window.FindName("AnswerBox")).Text.Contains("def solve"), "reviewing a previous answer holds automatic generation while reading");
        history.SelectedIndex = 0;
        Check(hold.IsChecked == false, "returning to live answer releases the reading hold");
        input.Clear(); ((Task)send.Invoke(window, null)!).GetAwaiter().GetResult();
        input.Text = new string('a', 24001); ((Task)send.Invoke(window, null)!).GetAwaiter().GetResult();
        Check(requests == 1, "empty and oversized pasted questions do not consume AI requests");
        input.Text = question;
        ((System.Windows.Controls.TextBlock)window.FindName("AnswerStatus")).Text = "Answer ready · select and copy text with Ctrl+C";
        var root = (FrameworkElement)window.Content;
        root.Measure(new Size(1058, 758)); root.Arrange(new Rect(0, 0, 1058, 758)); root.UpdateLayout();
        Check(input.ActualHeight > 120, "coding input has a usable multiline editing area");
        var bitmap = new RenderTargetBitmap(1058, 758, 96, 96, PixelFormats.Pbgra32); bitmap.Render(root);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var file = File.Create("artifacts/manual-question-preview.png")) encoder.Save(file);
        window.Close();
    }
    private static void RenderWindow(bool showOptions = false, bool showBadge = false)
    {
        var app = new App(); app.InitializeComponent();
        var window = new MainWindow();
        var handle = new WindowInteropHelper(window).EnsureHandle();
        window.Left = 100; window.Top = 100;
        var mover = (System.Windows.Controls.Primitives.Thumb)window.FindName("MoveHandle");
        mover.RaiseEvent(new System.Windows.Controls.Primitives.DragDeltaEventArgs(30, 20));
        Check(window.Left == 130 && window.Top == 120, "move handle drag changes window position");
        var protection = WindowPrivacy.Apply(handle);
        Check(protection.StartsWith("Capture exclusion enabled"), "native window capture exclusion enabled and read back");
        Check(((System.Windows.Controls.ComboBox)window.FindName("AudioSource")).Items.Count == 1 && window.FindName("MicDeviceBox") is null, "only caller output is selectable; microphone controls are absent");
        Check(!WindowInteraction.IsClickThrough(handle) && ((System.Windows.Controls.CheckBox)window.FindName("ClickThrough")).IsChecked != true, "overlay starts interactive so clicks and scrolling reach its controls");
        Check(window.AllowsTransparency && ((System.Windows.Media.SolidColorBrush)((System.Windows.Controls.Border)window.Content).Background).Color.A == 255, "overlay has an opaque reading background");
        var backgroundSlider = (System.Windows.Controls.Slider)window.FindName("BackgroundOpacitySlider");
        backgroundSlider.Value = 0.35;
        var contrastAnswer = (System.Windows.Controls.RichTextBox)window.FindName("AnswerDisplay");
        var contrastDocument = contrastAnswer.Document;
        Check(((System.Windows.Media.SolidColorBrush)contrastDocument.Foreground).Color == System.Windows.Media.Colors.White && contrastAnswer.Effect is System.Windows.Media.Effects.DropShadowEffect { Opacity: 1 }, "transparent answers use white text with a dark shadow");
        Check(Math.Abs(((System.Windows.Media.SolidColorBrush)window.Resources["OverlayBackground"]).Opacity - 0.35) < 0.001 && Math.Abs(((System.Windows.Media.SolidColorBrush)window.Resources["OverlayAnswerBackground"]).Opacity - 0.35) < 0.001 && window.Opacity == 1, "background slider fades both surfaces while keeping text and controls opaque");
        backgroundSlider.Value = 0;
        Check(((System.Windows.Media.SolidColorBrush)window.Resources["OverlayPanelBackground"]).Opacity == 0, "background slider supports a fully transparent surface");
        backgroundSlider.Value = 1;
        Check(ReferenceEquals(contrastDocument, contrastAnswer.Document) && ((System.Windows.Media.SolidColorBrush)contrastDocument.Foreground).Color != System.Windows.Media.Colors.White, "solid mode restores dark answer text without replacing the document");
        Check(WindowInteraction.SetClickThrough(handle, true) && WindowInteraction.IsClickThrough(handle), "native click-through mode enables desktop input");
        Check(WindowInteraction.SetClickThrough(handle, false) && !WindowInteraction.IsClickThrough(handle), "native interaction mode can be restored");
        Check(!window.ShowInTaskbar && WindowInteraction.IsExcludedFromTaskbar(handle), "window is excluded from the taskbar through WPF and native styles");
        window.Show(); window.Hide(); window.Show();
        Check(!window.ShowInTaskbar && WindowInteraction.IsExcludedFromTaskbar(handle), "showing and hiding the window preserves taskbar exclusion");
        Check(((System.Windows.Controls.CheckBox)window.FindName("AutoAnswer")).IsChecked == true && ((FrameworkElement)window.FindName("AnswerPanel")).Visibility == Visibility.Visible, "automatic streaming answers are enabled and visible by default");
        Check(window.FindName("AnswerKey") is null && window.FindName("TranscriptionKey") is null && window.FindName("RememberKey") is null, "credential entry controls are absent from the window");
        ((System.Windows.Controls.TextBlock)window.FindName("CallerQuestionText")).Text = "What is the difference between a process and a thread?";
        ((System.Windows.Controls.TextBlock)window.FindName("AnswerQuestionText")).Text = "Answering: What is the difference between a process and a thread?";
        ((System.Windows.Controls.TextBox)window.FindName("TranscriptBox")).Text = "Caller\nCould you explain the difference between a process and a thread in terms of memory isolation and scheduling?";
        ((System.Windows.Controls.TextBox)window.FindName("AnswerBox")).Text = """
A **process** is an independent running program with its own memory space. A **thread** is a unit of execution inside that process. Threads share the process’s memory, so communication is easier, but shared data needs careful synchronization.

I would choose separate processes when isolation matters, and threads when tasks need to share data efficiently.

- Processes provide stronger isolation: one crash is less likely to affect another process.
- Threads are lighter, but a bug can affect the entire process.

For example, a desktop app might use a worker thread for a long calculation so the interface stays responsive.
""";
        window.RefreshAnswerPresentation();
        var formatted = AnswerPresentation.Format("## Approach\n\nUse **isolation** and `threads`.\n\n- Share memory carefully.\n\n```csharp\nvar task = Task.Run(Work);");
        Check(formatted.Blocks.Count == 4 && new System.Windows.Documents.TextRange(formatted.ContentStart, formatted.ContentEnd).Text.Contains("var task = Task.Run(Work);"), "answer formatting preserves code while an unfinished fence is streaming");
        var root = (FrameworkElement)window.Content;
        var renderWidth = (int)window.Width - 2; var renderHeight = (int)window.Height - 2;
        root.Measure(new Size(renderWidth, renderHeight)); root.Arrange(new Rect(0, 0, renderWidth, renderHeight)); root.UpdateLayout();
        var answerControl = (System.Windows.Controls.RichTextBox)window.FindName("AnswerDisplay");
        var answerTextBox = (System.Windows.Controls.TextBox)window.FindName("AnswerBox");
        var savedAnswer = answerTextBox.Text;
        var savedQuestion = ((System.Windows.Controls.TextBlock)window.FindName("AnswerQuestionText")).Text;
        var simulatedStream = new AnswerStreamBuffer("Long streamed answer", 0);
        var streamField = typeof(MainWindow).GetField("activeStream", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        streamField.SetValue(window, simulatedStream);
        simulatedStream.Append(string.Join("\n\n", Enumerable.Range(0, 60).Select(i => "Paragraph " + i + ": This is enough answer text to read and scroll while the next words arrive.")));
        window.RefreshAnswerPresentation(); root.UpdateLayout(); answerControl.ScrollToVerticalOffset(150); root.UpdateLayout();
        var documentDuringStream = answerControl.Document; var readingOffset = answerControl.VerticalOffset;
        simulatedStream.Append("\n\nAdditional words arrive in the same document.");
        window.RefreshAnswerPresentation(); root.UpdateLayout();
        Check(readingOffset > 0 && ReferenceEquals(documentDuringStream, answerControl.Document) && Math.Abs(answerControl.VerticalOffset - readingOffset) < 2, "streaming keeps the same document and preserves the reader's scroll position");
        Check(new System.Windows.Documents.TextRange(answerControl.Document.ContentStart, answerControl.Document.ContentEnd).Text.Contains("Additional words arrive"), "batched streaming updates appear without rebuilding the answer document");
        streamField.SetValue(window, null); answerTextBox.Text = savedAnswer;
        ((System.Windows.Controls.TextBlock)window.FindName("AnswerQuestionText")).Text = savedQuestion;
        window.RefreshAnswerPresentation(); root.UpdateLayout();
        if(showBadge)
        {
            var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
            typeof(MainWindow).GetField("availableUpdate",flags)!.SetValue(window,new AppRelease(true,"0.1.14","https://example.com/updates/example.exe",new string('a',64),100,"Synthetic preview"));
            typeof(MainWindow).GetMethod("RefreshUpdateControls",flags)!.Invoke(window,new object[] { false });
            root.UpdateLayout();
        }
        if(showOptions)
        {
            ((FrameworkElement)window.FindName("OptionsPanel")).Visibility=Visibility.Visible;
            ((FrameworkElement)window.FindName("UpdateButton")).Visibility=Visibility.Visible;
            ((FrameworkElement)window.FindName("AccountButton")).Visibility=Visibility.Visible;
            ((System.Windows.Controls.TextBlock)window.FindName("AccountButtonLabel")).Text="candidate.long.email@example.com";
            root.UpdateLayout();
        }
        var bitmap = new RenderTargetBitmap(renderWidth, renderHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(root);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        Directory.CreateDirectory("artifacts"); using (var file = File.Create(showBadge ? "artifacts/update-badge-preview.png" : showOptions ? "artifacts/options-preview.png" : "artifacts/overlay-preview.png")) encoder.Save(file);
        window.Width=window.MinWidth; window.Height=window.MinHeight;
        root.Measure(new Size(window.MinWidth - 2, window.MinHeight - 2));
        root.Arrange(new Rect(0, 0, window.MinWidth - 2, window.MinHeight - 2)); root.UpdateLayout();
        Console.WriteLine($"Minimum answer area: {((FrameworkElement)window.FindName("AnswerDisplay")).ActualWidth:F0} x {((FrameworkElement)window.FindName("AnswerDisplay")).ActualHeight:F0}");
        Check(((FrameworkElement)window.FindName("AnswerDisplay")).ActualWidth > 400 && ((FrameworkElement)window.FindName("AnswerDisplay")).ActualHeight > 170, "minimum window size retains a usable answer reading area");
        var smallBitmap = new RenderTargetBitmap((int)window.MinWidth - 2, (int)window.MinHeight - 2, 96, 96, PixelFormats.Pbgra32);
        smallBitmap.Render(root); var smallEncoder = new PngBitmapEncoder(); smallEncoder.Frames.Add(BitmapFrame.Create(smallBitmap));
        using (var file = File.Create(showBadge ? "artifacts/update-badge-preview-small.png" : showOptions ? "artifacts/options-preview-small.png" : "artifacts/overlay-preview-small.png")) smallEncoder.Save(file);
        Console.WriteLine(protection); window.Close();
    }
    private static async Task VerifyCallerAnswerAsync()
    {
        using var speech = await WhisperTranscriber.CreateReadyAsync(default);
        using var pcm = new MemoryStream();
        using (var voice = new System.Speech.Synthesis.SpeechSynthesizer())
        {
            voice.SetOutputToAudioStream(pcm, new System.Speech.AudioFormat.SpeechAudioFormatInfo(16000, System.Speech.AudioFormat.AudioBitsPerSample.Sixteen, System.Speech.AudioFormat.AudioChannel.Mono));
            voice.Speak("Could you explain the difference between a process and a thread in terms of memory isolation and scheduling in an operating system?");
            voice.SetOutputToNull();
        }
        var audio = pcm.ToArray(); var segmenter = new AudioSegmenter();
        var chunks = segmenter.Feed(audio, audio.Length, DateTimeOffset.UtcNow);
        string callerText = "";
        foreach (var chunk in chunks.Where(c => c.Preview))
        {
            var text = await speech.TranscribeAsync(AudioSegmenter.ToWav(chunk.Pcm), default);
            if (CallerQuestion.IsReady(text) && text.Contains("thread", StringComparison.OrdinalIgnoreCase)) { callerText = text; break; }
        }
        Check(callerText.Length > 0, "ongoing caller speech yields an answerable live question before a pause");
        var live = new LiveTranscript(callerOnly: true); live.Update(1, "Caller", 1, callerText, false);
        var queue = new LiveAnswerQueue(); queue.Offer(live.Context);
        var job = queue.TryTake(DateTimeOffset.UtcNow.AddSeconds(1))!;
        var settings = AppSettings.Load() with { MaxTokens = 512 };
        var key = new CredentialStore().Load(settings.AnswerUrl, settings.AnswerAuth);
        using var client = new EndpointClient(); var answer = new StringBuilder();
        await client.AnswerAsync("Answer this caller question concisely:\n" + job.Prompt, settings, key, token => answer.Append(token), default);
        Check(answer.ToString().Contains("process", StringComparison.OrdinalIgnoreCase) && answer.ToString().Contains("thread", StringComparison.OrdinalIgnoreCase), "live caller transcript automatically reaches Azure and streams a relevant answer");
        Console.WriteLine("Synthetic caller-to-Whisper-to-Azure pipeline verified. No call audio or transcript saved.");
    }
    private static CandidateProfile SampleCandidate() => new()
    {
        SourceName = "synthetic-resume.txt",
        ResumeText = "Candidate worked for three years on an online shop order tracking backend. Built Python services and SQL queries with PostgreSQL. Improved order status updates and fixed slow database queries. Programming languages: Python, SQL.",
        Experience = "Three years building an online shop order tracking backend using Python and PostgreSQL, including order updates and query improvements.",
        Languages = new[] { "Python", "SQL" }
    };
    private static void VerifyResumeFiles()
    {
        var directory = Path.Combine("artifacts", "resume-fixtures-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        try
        {
            var sample = SampleCandidate(); var textPath = Path.Combine(directory, "resume.txt"); File.WriteAllText(textPath, sample.ResumeText);
            Check(ResumeReader.Read(textPath) == sample.ResumeText, "TXT resume extraction preserves experience and coding languages");
            var docxPath = Path.Combine(directory, "resume.docx");
            using (var zip = System.IO.Compression.ZipFile.Open(docxPath, System.IO.Compression.ZipArchiveMode.Create))
            using (var writer = new StreamWriter(zip.CreateEntry("word/document.xml").Open()))
                writer.Write("<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body><w:p><w:r><w:t>Candidate with three years of experience.</w:t></w:r></w:p><w:tbl><w:tr><w:tc><w:p><w:r><w:t>Python and SQL order tracking backend.</w:t></w:r></w:p></w:tc></w:tr></w:tbl></w:body></w:document>");
            Check(ResumeReader.Read(docxPath).Contains("Python and SQL") && ResumeReader.Read(docxPath).Contains("three years"), "DOCX extracts paragraphs and table text without Word installed");
            var pdfPath = Path.Combine(directory, "resume.pdf"); File.WriteAllBytes(pdfPath, SyntheticPdf("Candidate with three years using Python and SQL for order tracking."));
            Check(ResumeReader.Read(pdfPath).Contains("Python and SQL"), "PDF resume extraction preserves readable experience text");
            var emptyPdfPath = Path.Combine(directory, "empty.pdf"); File.WriteAllBytes(emptyPdfPath, SyntheticPdf(""));
            try { ResumeReader.Read(emptyPdfPath); throw new Exception("Blank PDF was accepted"); }
            catch (ArgumentException) { Check(true, "PDF without readable text gives an actionable error instead of a blank profile"); }
            var hugePath = Path.Combine(directory, "large.txt"); File.WriteAllText(hugePath, new string('x', ResumeReader.MaxCharacters + 1));
            try { ResumeReader.Read(hugePath); throw new Exception("Oversize text was accepted"); }
            catch (ArgumentException) { Check(true, "oversize resume is rejected rather than silently losing experience"); }
            var analysed = CandidateProfile.FromAnalysis("resume.txt", sample.ResumeText, "```json\n{\"experience\":\"Python order tracking backend\",\"languages\":[\"Python\",\"SQL\",\"python\"]}\n```");
            Check(analysed.Languages.Length == 2 && analysed.Experience.Contains("order tracking"), "analysis JSON is parsed and duplicate language names are removed");
            var storePath = Path.Combine(directory, "profile.dpapi"); var store = new CandidateProfileStore(storePath); store.Save(sample);
            var loaded = store.Load();
            Check(loaded?.ResumeText == sample.ResumeText && loaded.Languages.SequenceEqual(sample.Languages), "reviewed profile survives encrypted local storage and reload");
            Check(!Encoding.UTF8.GetString(File.ReadAllBytes(storePath)).Contains("order tracking"), "saved resume profile is encrypted rather than plaintext");
            Check(!JsonSerializer.Serialize(new AppSettings { Candidate = sample }).Contains("order tracking"), "candidate data is excluded from settings and distributable defaults");
            var prompt = AnswerPresentation.ComposePrompt("Old preference: answer at length", sample);
            Check(prompt.Contains("Python") && prompt.Contains("Do not produce code in an unlisted language") && prompt.Contains("never invent"), "answer prompt carries candidate evidence, language limits and factual safeguards");
            Check((sample with { Languages = Array.Empty<string>() }).PromptContext().Contains("ask which language"), "a profile without confirmed languages requests clarification before code");
            store.Clear(); Check(store.Load() is null, "removing a profile deletes its saved candidate context");
        }
        finally { foreach (var file in Directory.GetFiles(directory)) File.Delete(file); Directory.Delete(directory); }
    }
    private static byte[] SyntheticPdf(string text)
    {
        var content = "BT /F1 12 Tf 20 760 Td (" + text + ") Tj ET";
        var objects = new[] { "<< /Type /Catalog /Pages 2 0 R >>", "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>", "<< /Length " + Encoding.ASCII.GetByteCount(content) + " >>\nstream\n" + content + "\nendstream" };
        var result = new StringBuilder("%PDF-1.4\n"); var offsets = new List<int>();
        for (var i = 0; i < objects.Length; i++) { offsets.Add(Encoding.ASCII.GetByteCount(result.ToString())); result.Append($"{i + 1} 0 obj\n{objects[i]}\nendobj\n"); }
        var xref = Encoding.ASCII.GetByteCount(result.ToString()); result.Append("xref\n0 6\n0000000000 65535 f \n");
        foreach (var offset in offsets) result.Append(offset.ToString("D10") + " 00000 n \n");
        result.Append($"trailer\n<< /Size 6 /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF"); return Encoding.ASCII.GetBytes(result.ToString());
    }
    private static async Task VerifyResumeAnswersAsync()
    {
        var settings = AppSettings.Load(); var key = new CredentialStore().Load(settings.AnswerUrl, settings.AnswerAuth);
        var analysis = new StringBuilder(); TokenUsage? usage = null;
        await SendResumeTestAsync("Resume data:\n" + SampleCandidate().ResumeText, settings, key, analysis,
            value => usage = value, CandidateProfile.AnalysisInstructions);
        var profile = CandidateProfile.FromAnalysis("synthetic-resume.txt", SampleCandidate().ResumeText, analysis.ToString());
        Check(profile.Languages.Contains("Python") && profile.Languages.Contains("SQL") && !profile.Languages.Contains("Java"), "live Azure analysis identifies evidenced coding languages without adding Java");
        Check(!profile.Experience.Contains("percent", StringComparison.OrdinalIgnoreCase) && !profile.Experience.Contains('%'), "analysis does not add an unstated percentage improvement");
        Check(usage is { Total: > 0 }, "resume analysis reports provider token usage");
        var project = new StringBuilder();
        await SendResumeTestAsync("Tell me about your project experience.", settings with { Candidate = profile }, key, project);
        Check(project.ToString().Contains("order", StringComparison.OrdinalIgnoreCase) && project.ToString().Contains("Python", StringComparison.OrdinalIgnoreCase), "live spoken answer reflects the candidate's actual order tracking project");
        Check(!project.ToString().Contains("percent", StringComparison.OrdinalIgnoreCase) && !project.ToString().Contains('%'), "personal answer does not fabricate an unstated percentage improvement");
        Console.WriteLine("Synthetic project answer:\n" + project);
        var code = new StringBuilder();
        await SendResumeTestAsync("Write a short function to check whether a string is a palindrome, with code.", settings with { Candidate = profile }, key, code);
        Check(code.ToString().Contains("```python", StringComparison.OrdinalIgnoreCase) && code.ToString().Contains("def "), "coding answer uses the candidate's experienced Python language");
        var other = new StringBuilder();
        await SendResumeTestAsync("Write Java code to check whether a string is a palindrome.", settings with { Candidate = profile }, key, other);
        Check(!other.ToString().Contains("```java", StringComparison.OrdinalIgnoreCase) && !other.ToString().Contains("public static") && other.ToString().Contains("Python", StringComparison.OrdinalIgnoreCase), "an unlisted Java request is redirected to the candidate's Python experience");
        Console.WriteLine("Synthetic alternate-language answer:\n" + other);
    }
    private static async Task SendResumeTestAsync(string question, AppSettings settings, string key, StringBuilder output, Action<TokenUsage>? usage = null, string? system = null)
    {
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            output.Clear(); using var client = new EndpointClient();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
            try { await client.AnswerAsync(question, settings, key, token => output.Append(token), timeout.Token, usage, system); return; }
            catch (HttpRequestException) when (attempt < 3) { Console.WriteLine("Retrying synthetic model check after a connection interruption."); }
            catch (OperationCanceledException) when (attempt < 3) { Console.WriteLine("Retrying synthetic model check after a connection timeout."); }
            await Task.Delay(500);
        }
    }
    private static void RenderResumeWindow()
    {
        var app = new App(); app.InitializeComponent();
        var sample = SampleCandidate(); var window = new ResumeWindow(new AppSettings(), "", sample, new CandidateProfileStore()) { Width = 800, Height = 740 };
        window.Show(); window.UpdateLayout(); var root = (FrameworkElement)window.Content;
        Check(((System.Windows.Controls.TextBox)window.FindName("LanguagesBox")).Text == "Python, SQL", "resume panel exposes reviewed coding languages");
        Check(((System.Windows.Controls.TextBox)window.FindName("ResumeTextBox")).IsReadOnly && ((System.Windows.Controls.Button)window.FindName("SaveButton")).IsEnabled, "resume evidence is readable and profile needs an explicit use action");
        var bitmap = new RenderTargetBitmap((int)root.ActualWidth, (int)root.ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(root);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var file = File.Create("artifacts/resume-preview.png")) encoder.Save(file);
        window.Close();
    }
    private static async Task VerifyConversationStyleAsync()
    {
        // Synthetic profile only; no real candidate resume is sent for this style check.
        var profile = new CandidateProfile {
            SourceName = "synthetic-style-test.txt",
            ResumeText = "Worked on the Ezicomms salary-packaging project. Used Hangfire for daily payroll exports. Logged failed jobs and retried them with a limit. Wrote C# services and SQL queries.",
            Experience = "Worked on the Ezicomms salary-packaging project. Used Hangfire for daily payroll exports. Logged failed jobs and retried them with a limit. Wrote C# services and SQL queries.",
            Languages = new[] { "C#", "SQL" }
        };
        var settings = new AppSettings { Candidate = profile };
        var key = new CredentialStore().Load(settings.AnswerUrl, settings.AnswerAuth);
        using var client = new EndpointClient();
        var examples = new StringBuilder();
        foreach (var question in new[] { "How did you schedule payroll exports?", "How did you handle failed jobs?", "What is dependency injection?", "Which project did you work on?" })
        {
            var answer = new StringBuilder(); using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
            await client.AnswerAsync(QuestionPrompt.Build(question, ""), settings, key, t => answer.Append(t), timeout.Token);
            var text = answer.ToString().Trim();
            var identify = question.StartsWith("Which project");
            Check(!text.StartsWith("In the ", StringComparison.OrdinalIgnoreCase), "live response starts directly: " + question);
            Check(identify || (!text.Contains("Ezicomms", StringComparison.OrdinalIgnoreCase) && !text.Contains("salary-packaging", StringComparison.OrdinalIgnoreCase)), "project name is used only when relevant: " + question);
            Check(System.Text.RegularExpressions.Regex.Matches(text, @"\S+").Count <= 70, "conversational response remains concise: " + question);
            if (identify) Check(text.Contains("Ezicomms", StringComparison.OrdinalIgnoreCase), "explicit project-identification questions still name the confirmed project");
            examples.AppendLine("Question: " + question).AppendLine(text).AppendLine();
        }
        Directory.CreateDirectory("artifacts"); File.WriteAllText("artifacts/conversation-style-preview.txt", examples.ToString());
        Console.WriteLine(examples);
    }
    private static async Task VerifyAnswerStyleAsync()
    {
        var settings = AppSettings.Load(); var key = new CredentialStore().Load(settings.AnswerUrl, settings.AnswerAuth);
        using var client = new EndpointClient(); var answer = new StringBuilder();
        await client.AnswerAsync("Explain dependency injection in C# and how you would use it in a small web application. Give the actual answer I could speak to the caller.", settings, key, token => answer.Append(token), default);
        var text = answer.ToString(); var words = System.Text.RegularExpressions.Regex.Matches(text, @"\S+").Count;
        Check(words >= 25 && words <= 110 && !text.Contains("As an AI", StringComparison.OrdinalIgnoreCase), "live model keeps a multi-part spoken answer short and meaningful even with saved older preferences");
        Check(text.Contains("service", StringComparison.OrdinalIgnoreCase) && (text.Contains("register", StringComparison.OrdinalIgnoreCase) || text.Contains("constructor", StringComparison.OrdinalIgnoreCase)), "short answer retains the practical dependency injection approach");
        Check(!text.TrimStart().StartsWith("Sure", StringComparison.OrdinalIgnoreCase) && !text.Contains("you can say", StringComparison.OrdinalIgnoreCase), "spoken answer starts directly without candidate coaching or a canned introduction");
        Check(!text.Contains("```"), "concept questions use spoken paragraphs instead of an unrequested full code listing");
        File.WriteAllText("artifacts/answer-style-preview.txt", text);
        Console.WriteLine("Synthetic answer for language review:\n" + text);
        var code = new StringBuilder();
        await client.AnswerAsync("Write a C# code to print palindrom", settings with { Candidate = null }, key, token => code.Append(token), default);
        var codingText = code.ToString();
        Check(codingText.Contains("```csharp", StringComparison.OrdinalIgnoreCase) || codingText.Contains("```c#", StringComparison.OrdinalIgnoreCase), "reported palindrome request returns a labelled C# code block");
        Check(codingText.Contains("Console.WriteLine") && (codingText.Contains("Main(") || codingText.Contains("Main (")), "palindrome answer includes a runnable program and printed output instead of prose alone");
        File.WriteAllText("artifacts/coding-answer-preview.txt", codingText);
        Console.WriteLine("Synthetic coding answer:\n" + codingText);
    }
    private static void VerifySessionAccounting()
    {
        var app = new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var window = new MainWindow(); window.Show();
        var watch = System.Diagnostics.Stopwatch.StartNew(); var asked = false;
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        timer.Tick += (_, _) =>
        {
            var status = ((System.Windows.Controls.TextBlock)window.FindName("AnswerStatus")).Text;
            if (!asked && status == "Ready to read")
            {
                Check(((System.Windows.Controls.TextBlock)window.FindName("TokenSummary")).Text == "AI tokens · 0", "startup connection checks do not inflate call usage");
                ((System.Windows.Controls.TextBox)window.FindName("QuestionBox")).Text = "Caller: What is the difference between a process and a thread?";
                ((System.Windows.Controls.TextBlock)window.FindName("CallerQuestionText")).Text = "What is the difference between a process and a thread?";
                asked = true;
                ((System.Windows.Controls.Button)window.FindName("AnswerButton")).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                Check(!((System.Windows.Controls.Button)window.FindName("ExportButton")).IsEnabled, "export waits for an active AI answer to finish");
            }
            else if (asked && status == "Ready to read")
            {
                timer.Stop();
                var archive = (CallArchive)typeof(MainWindow).GetField("callArchive", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(window)!;
                var report = archive.Snapshot();
                Check(report.Answers.Length == 1 && report.ReportedUsage.Total > 0 && report.Answers[0].Status == "Completed", "real window retains its completed AI answer and provider token totals");
                Check(((System.Windows.Controls.TextBlock)window.FindName("TokenSummary")).Text.Contains(report.ReportedUsage.Total.ToString("N0")) && ((System.Windows.Controls.Button)window.FindName("ExportButton")).IsEnabled, "top counter and export availability match the completed call archive");
                window.Close();
            }
            else if (watch.Elapsed > TimeSpan.FromSeconds(50)) { timer.Stop(); Environment.ExitCode = 1; Console.WriteLine("Session status: " + status); window.Close(); }
        };
        window.Closed += (_, _) => app.Shutdown(); timer.Start(); app.Run();
        if (Environment.ExitCode != 0) throw new InvalidOperationException("Call session accounting did not complete.");
    }
    private static void VerifyStartupConnection()
    {
        var app = new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var window = new MainWindow(); window.Show();
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        timer.Tick += (_, _) =>
        {
            var status = ((System.Windows.Controls.TextBlock)window.FindName("AnswerStatus")).Text;
            if (status == "Ready to read")
            {
                timer.Stop(); Check(true, "real window startup loads the saved background credential and verifies Azure");
                Check(window.AllowsTransparency && ((System.Windows.Controls.ComboBox)window.FindName("AudioSource")).SelectedIndex == 0, "startup uses a caller-only window");
                window.Close();
            }
            else if (watch.Elapsed > TimeSpan.FromSeconds(25))
            {
                timer.Stop(); Console.WriteLine("Startup status: " + status); Environment.ExitCode = 1; window.Close();
            }
        };
        window.Closed += (_, _) => app.Shutdown();
        timer.Start(); app.Run();
        if (Environment.ExitCode != 0) throw new InvalidOperationException("Startup connection did not complete.");
    }
    private static void VerifyWindowShutdown()
    {
        // A plain Application prevents App.xaml's StartupUri from opening a second test window.
        var app = new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var window = new MainWindow(); window.Show();
        var archive=(CallArchive)typeof(MainWindow).GetField("callArchive",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(window)!;
        archive.StartCapture("synthetic call"); archive.UpdateCaller(1,1,"A question before closing",true);
        var answer=archive.StartAnswer("A question","A question",new AppSettings()); answer.Append("Saved answer"); answer.Finish("Completed");
        var watch=new System.Diagnostics.Stopwatch();
        var timer=new System.Windows.Threading.DispatcherTimer { Interval=TimeSpan.FromMilliseconds(200) };
        timer.Tick+=(_,_)=> { timer.Stop(); watch.Start(); window.Close(); };
        window.Closed+=(_,_)=> {
            Check(watch.Elapsed<TimeSpan.FromSeconds(2),"window closes promptly while saving a call");
            var path=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),"Ansora AI","Calls",$"call-{archive.Id:N}.html");
            Check(File.Exists(path) && File.ReadAllText(path).Contains("Saved answer"),"close preserves the latest transcript and answer without export");
            Console.WriteLine($"Close completed in {watch.Elapsed.TotalMilliseconds:F0} ms."); app.Shutdown();
        };
        timer.Start(); app.Run();
    }
    private static void VerifyAccountUi()
    {
        var app = new App(); app.InitializeComponent();
        var window = new MainWindow();
        var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        var load = typeof(MainWindow).GetMethod("LoadSettings", flags)!;
        var header = typeof(MainWindow).GetMethod("UpdateAccountHeader", flags)!;
        load.Invoke(window, new object[] { new AppSettings { CommercialMode = true, CommercialBaseUrl = "https://account-ui.example", AnswerUrl = "https://account-ui.example/api/chat/completions", AnswerModel = "", AnswerAuth = "Bearer" } });
        Check(((FrameworkElement)window.FindName("AiEndpointSettings")).Visibility == Visibility.Collapsed && ((FrameworkElement)window.FindName("SpeechEndpointSettings")).Visibility == Visibility.Collapsed, "commercial settings hide AI deployment, endpoint, authentication and speech endpoint fields");
        Check(((FrameworkElement)window.FindName("UseLocalSpeech")).Visibility == Visibility.Collapsed && ((System.Windows.Controls.CheckBox)window.FindName("UseLocalSpeech")).IsChecked == true, "commercial transcription uses local speech without exposing backend options");
        var label = (System.Windows.Controls.TextBlock)window.FindName("AccountButtonLabel");
        Check(label.Text == "Sign in", "logged-out commercial header displays Sign in");
        header.Invoke(window, new object[] { "candidate@example.test" });
        Check(label.Text == "candidate@example.test" && ((System.Windows.Controls.Button)window.FindName("AccountButton")).ToolTip.ToString()!.Contains("candidate@example.test"), "signed-in header displays the user's email and account actions");
        header.Invoke(window, new object?[] { null });
        Check(label.Text == "Sign in", "sign-out returns the header to Sign in");
        load.Invoke(window, new object[] { new AppSettings() });
        Check(((FrameworkElement)window.FindName("AiEndpointSettings")).Visibility == Visibility.Visible, "direct endpoint builds retain their model settings");
        ((System.Windows.Controls.Button)window.FindName("TestAiButton")).IsEnabled = false;
        ((System.Windows.Controls.Expander)window.FindName("SettingsPanel")).IsExpanded = true;
        var device = (System.Windows.Controls.ComboBox)window.FindName("DeviceBox");
        device.ItemsSource = new[] { new { Id = "speaker", Name = "Speakers (test device)" }, new { Id = "headset", Name = "Headphones (test device)" } };
        device.SelectedIndex = 0;
        window.Show(); window.UpdateLayout(); device.ApplyTemplate();
        device.IsDropDownOpen = true; window.UpdateLayout();
        var popup = (System.Windows.Controls.Primitives.Popup)device.Template.FindName("PART_Popup", device);
        var popupSurface = (System.Windows.Controls.Border)popup.Child;
        Check(popup.IsOpen && ((SolidColorBrush)popupSurface.Background).Color == Color.FromRgb(27, 38, 56) && ((SolidColorBrush)device.Foreground).Color == Color.FromRgb(244, 247, 255), "audio dropdown and selected device have explicit dark surfaces and bright text");
        device.SelectedIndex = 1;
        Check(device.SelectedValue?.ToString() == "headset", "styled dropdown preserves device selection");
        device.IsDropDownOpen = false;
        window.Close(); app.Shutdown();
    }
    private static async Task VerifyCommercialAsync()
    {
        var settings = AppSettings.Normalize(new AppSettings { AnswerUrl = "https://unrelated.example/chat", AnswerModel = "", UseMicrophone = true },
            new AppSettings { CommercialMode = true, CommercialBaseUrl = "https://ansora.example" });
        Check(settings.CommercialMode && settings.AnswerUrl == "https://ansora.example/api/chat/completions" && settings.AnswerAuth == "Bearer" && !settings.UseMicrophone,
            "commercial defaults fix the service endpoint and caller-only capture");
        var validateAnswer = typeof(MainWindow).GetMethod("ValidateAnswerSettings", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        validateAnswer.Invoke(null, new object[] { settings });
        Check(true, "production window accepts a blank client model because the server owns deployment configuration");
        try { (settings with { CommercialMode = false }).ValidateAnswer(); throw new Exception("Direct endpoint accepted an empty model."); }
        catch (ArgumentException) { Check(true, "direct endpoint mode still requires a deployment model"); }
        var path = Path.Combine(Path.GetTempPath(), "ansora-account-" + Guid.NewGuid().ToString("N") + ".dpapi");
        var store = new CommercialAccountStore(path);
        var session = new CommercialSession("https://ansora.example", "candidate@example.test", "synthetic-account-token", DateTimeOffset.UtcNow.AddDays(1).ToUnixTimeSeconds());
        try
        {
            store.Save(session);
            Check(!Encoding.UTF8.GetString(File.ReadAllBytes(path)).Contains(session.Token) && store.Load(session.BaseUrl)?.Token == session.Token,
                "commercial account token is encrypted for the current Windows user");
            Check(store.Load("https://other.example") is null, "account credentials cannot be reused on a different website");
            store.Save(session with { ExpiresAt = 1 }); Check(store.Load(session.BaseUrl) is null, "expired desktop sessions are rejected locally");
            store.Clear(); Check(!File.Exists(path), "commercial sign-out removes the local session");
        }
        finally { if (File.Exists(path)) File.Delete(path); }
        using var account = new CommercialAccountClient(new FakeHandler(async request =>
        {
            Check(request.Headers.GetValues("X-Client").Single() == "desktop", "account requests identify the desktop client");
            if (request.RequestUri!.AbsolutePath.EndsWith("auth/start"))
            {
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
                Check(body.RootElement.GetProperty("terms_accepted").GetBoolean(), "desktop account sign-in carries accepted terms");
                return Json(JsonSerializer.Serialize(new { challenge = "synthetic-challenge" }));
            }
            Check(request.Headers.Authorization?.Scheme == "Bearer" && request.Headers.Authorization.Parameter == session.Token,
                "desktop account balance uses the signed-in session token");
            return Json(JsonSerializer.Serialize(new { user = new { email = session.Email }, wallet = new { available = 5000, reserved = 100 }, ai_configured = true }));
        }));
        Check(await account.StartAsync(session.BaseUrl,session.Email,default) == "synthetic-challenge", "desktop email code flow parses server challenge");
        var balance = await account.BalanceAsync(session,default);
        Check(balance.Available == 5000 && balance.Reserved == 100 && balance.AiConfigured, "desktop reads server-controlled available and reserved balances");
        var requestIds = new HashSet<string>();
        using var endpoint = new EndpointClient(new FakeHandler(async request =>
        {
            Check(request.Headers.Authorization?.Scheme == "Bearer" && request.Headers.Authorization.Parameter == session.Token && !request.Headers.Contains("api-key"),
                "commercial AI request sends account authorization without an Azure key");
            Check(requestIds.Add(request.Headers.GetValues("X-Request-Id").Single()), "commercial AI requests have unique billing IDs");
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            Check(body.RootElement.GetProperty("session_id").GetString() == "synthetic-call", "commercial usage carries the call session ID");
            Check(!body.RootElement.TryGetProperty("model", out _), "commercial requests leave model selection to the server");
            Check(body.RootElement.GetProperty("purpose").GetString() == (requestIds.Count == 1 ? "answer" : "resume"), "answers and resume analysis are metered separately");
            return Json(JsonSerializer.Serialize(new { choices = new[] { new { message = new { content = "Synthetic answer" } } } }));
        }));
        await endpoint.AnswerAsync("Explain threads",settings with { CommercialSessionId = "synthetic-call" },session.Token,_ => { },default);
        await endpoint.AnswerAsync("Synthetic resume",settings with { CommercialSessionId = "synthetic-call" },session.Token,_ => { },default,systemInstructions: CandidateProfile.AnalysisInstructions);
    }
    private static void VerifyUpdateBadge()
    {
        var app=new App(); app.InitializeComponent(); var window=new MainWindow();
        var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
        var badge=(System.Windows.Controls.Button)window.FindName("UpdateBadge");
        var panel=(FrameworkElement)window.FindName("OptionsPanel");
        var method=typeof(MainWindow).GetMethod("RefreshUpdateControls",flags)!;
        var release=typeof(MainWindow).GetField("availableUpdate",flags)!;
        Check(badge.Visibility==Visibility.Collapsed,"no update badge is shown before a newer release is discovered");
        release.SetValue(window,new AppRelease(true,"0.1.14","https://example.com/updates/example.exe",new string('a',64),100,"Synthetic release"));
        method.Invoke(window,new object[] { false });
        Check(badge.Visibility==Visibility.Visible && panel.Visibility==Visibility.Collapsed,"new release badge appears outside the closed Options menu");
        Check(((SolidColorBrush)badge.Background).Color==Color.FromRgb(246,196,83),"update badge has a distinct yellow colour");
        Check(badge.ToolTip.ToString()!.Contains("0.1.14"),"badge identifies the available version");
        var parent=System.Windows.Media.VisualTreeHelper.GetParent(badge); bool insideOptions=false;
        while(parent is not null) { if(ReferenceEquals(parent,panel))insideOptions=true; parent=System.Windows.Media.VisualTreeHelper.GetParent(parent); }
        Check(!insideOptions,"badge belongs to the top bar rather than Options content");
        typeof(MainWindow).GetField("updateBusy",flags)!.SetValue(window,true); method.Invoke(window,new object[] { true });
        Check(!badge.IsEnabled && ((System.Windows.Controls.TextBlock)window.FindName("UpdateBadgeLabel")).Text=="Downloading…","downloading badge disables duplicate clicks and shows progress state");
        typeof(MainWindow).GetField("updateBusy",flags)!.SetValue(window,false); method.Invoke(window,new object[] { false });
        Check(badge.IsEnabled,"badge is actionable again after download failure or cancellation");
        release.SetValue(window,null); method.Invoke(window,new object[] { false });
        Check(badge.Visibility==Visibility.Collapsed,"badge disappears when no newer release is published");
        var timer=(System.Windows.Threading.DispatcherTimer)typeof(MainWindow).GetField("updateTimer",flags)!.GetValue(window)!;
        Check(timer.Interval==TimeSpan.FromMinutes(15),"running clients check for updates every fifteen minutes");
        window.Close(); Console.WriteLine($"PASS: {passed} update badge checks");
    }
    private static void VerifyStreamRecovery()
    {
        var app=new App(); app.InitializeComponent(); var window=new MainWindow();
        SynchronizationContext.SetSynchronizationContext(new System.Windows.Threading.DispatcherSynchronizationContext(window.Dispatcher));
        var flags=System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance;
        var endpointField=typeof(MainWindow).GetField("endpoints",flags)!;
        ((EndpointClient)endpointField.GetValue(window)!).Dispose();
        var calls=0;
        endpointField.SetValue(window,new EndpointClient(new FakeHandler(_=> {
            calls++;
            if(calls==3 || calls==5)return Task.FromResult(new HttpResponseMessage(calls==3?HttpStatusCode.ServiceUnavailable:HttpStatusCode.Unauthorized));
            // PHP historically serialised an empty delta as []. The failure occurred
            // after visible text and finish_reason, before the final metering frame.
            var events="data: {\"model\":\"synthetic-mini\"}\n\n"
                +"data: {\"choices\":[{\"delta\":{\"content\":\"Answer "+calls+"\"},\"finish_reason\":null}]}\n\n"
                +"data: {\"choices\":[{\"delta\":[],\"finish_reason\":\"stop\"}]}\n\n"
                +"data: []\n\ndata: {\"choices\":null}\n\ndata: {\"choices\":[[]]}\n\n"
                +"data: {\"choices\":[],\"usage\":{\"prompt_tokens\":10,\"completion_tokens\":20,\"total_tokens\":30}}\n\ndata: [DONE]\n\n";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content=new StringContent(events,Encoding.UTF8,"text/event-stream") });
        })));
        ((System.Windows.Controls.TextBox)window.FindName("AnswerUrl")).Text="http://127.0.0.1:8182/chat/completions";
        ((System.Windows.Controls.TextBox)window.FindName("AnswerModel")).Text="synthetic-mini";
        var generate=typeof(MainWindow).GetMethod("GenerateAnswerAsync",flags)!;
        void Ask(string question)
        {
            var task=(Task)generate.Invoke(window,new object[] { question,true,question,"synthetic-"+question })!;
            var frame=new System.Windows.Threading.DispatcherFrame();
            var timer=new System.Windows.Threading.DispatcherTimer { Interval=TimeSpan.FromSeconds(10) };
            timer.Tick+=(_,_)=>frame.Continue=false; timer.Start();
            task.ContinueWith(_=>window.Dispatcher.BeginInvoke(()=>frame.Continue=false));
            System.Windows.Threading.Dispatcher.PushFrame(frame); timer.Stop();
            Check(task.IsCompletedSuccessfully,"automatic question task finishes without a UI exception");
        }
        bool Paused()=>(bool)typeof(MainWindow).GetField("automaticSuppressed",flags)!.GetValue(window)!;
        var archive=(CallArchive)typeof(MainWindow).GetField("callArchive",flags)!.GetValue(window)!;
        Ask("Explain polymorphism");
        var first=archive.Snapshot().Answers[0];
        Check(first.Status=="Completed" && first.FinishReason=="stop" && first.Usage?.Total==30 && !first.UsageIncomplete,"array-shaped finish delta preserves completed answer and trailing token usage");
        Check(!Paused(),"empty stream metadata does not pause automatic answers");
        Ask("How do services call REST APIs?");
        Check(calls==2 && archive.RequestCount==2 && ((System.Windows.Controls.TextBox)window.FindName("AnswerBox")).Text=="Answer 2","a later caller question replaces the first answer");
        Ask("What is circuit breaking?");
        Check(!Paused(),"temporary HTTP failure does not permanently pause future questions");
        Ask("What is dependency injection?");
        Check(calls==4 && archive.Snapshot().Answers[3].Status=="Completed" && ((System.Windows.Controls.TextBox)window.FindName("AnswerBox")).Text=="Answer 4","the next distinct question succeeds after a temporary failure without retrying the failed question");
        Ask("Explain interfaces");
        Check(Paused() && calls==5,"authentication failure still pauses automatic requests");
        Check(archive.ReportedUsage.Total==90,"all three completed answers retain verified metering");
        window.Close(); Console.WriteLine($"PASS: {passed} stream recovery checks");
    }
    private static void VerifyOnboarding()
    {
        var app=new Application { ShutdownMode=ShutdownMode.OnExplicitShutdown };
        var directory=Path.Combine(Path.GetTempPath(),"ansora-account-test-"+Guid.NewGuid().ToString("N"));
        var store=new CommercialAccountStore(Path.Combine(directory,"session"));
        var account=new AccountWindow("https://onboarding.example",true,store);
        Check(((FrameworkElement)account.FindName("ServicePanel")).Visibility==Visibility.Collapsed,"production sign-in hides the known service URL");
        Check(((FrameworkElement)account.FindName("CodePanel")).Visibility==Visibility.Collapsed && ((FrameworkElement)account.FindName("SignInButton")).Visibility==Visibility.Collapsed,"first sign-in step asks only for email and consent before sending code");
        account.Close();
        var window=new MainWindow();
        Check(((FrameworkElement)window.FindName("OptionsPanel")).Visibility==Visibility.Collapsed,"secondary controls are initially tucked away");
        typeof(MainWindow).GetMethod("OptionsClick",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.Invoke(window,new object[] { window,new RoutedEventArgs() });
        Check(((FrameworkElement)window.FindName("OptionsPanel")).Visibility==Visibility.Visible,"one Options control reveals secondary settings");
        Check(((System.Windows.Controls.TextBlock)((System.Windows.Controls.CheckBox)window.FindName("PinAnswer")).Content).Text=="Keep this on screen","reading hold uses clear language");
        window.Close(); app.Shutdown();
    }
    private static async Task VerifyProductFlowAsync()
    {
        var settings=new AppSettings { CommercialMode=true, CommercialBaseUrl="https://ansora.example", AnswerUrl="https://ansora.example/api/chat/completions", AnswerAuth="Bearer", MaxTokens=4096 };
        var ids=new HashSet<string>(); int calls=0; var answer=new StringBuilder(); TokenUsage? usage=null; string finish="";
        using(var endpoint=new EndpointClient(new FakeHandler(async request => {
            calls++; Check(ids.Add(request.Headers.GetValues("X-Request-Id").Single()),"continuations have independent billing IDs");
            using var body=JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            if(calls>1) { var messages=body.RootElement.GetProperty("messages"); Check(messages.GetArrayLength()==4 && messages[2].GetProperty("content").GetString()=="```csharp\nclass C {", "continuation carries previous code and original context"); Check(body.RootElement.GetProperty("original_question").GetString()=="Write C# code", "continuation retains original question for server cap"); }
            return Json(JsonSerializer.Serialize(new { model="gpt-4.1-mini", choices=new[] { new { message=new { content=calls==1?"```csharp\nclass C {":"}\n```" }, finish_reason=calls==1?"length":"stop" } }, usage=new { prompt_tokens=10,completion_tokens=20,total_tokens=30 } }));
        }))) await endpoint.AnswerToCompletionAsync("Write C# code",settings,"synthetic",t=>answer.Append(t),default,u=>usage=u,(_,f)=> { if(f.Length>0)finish=f; });
        Check(calls==2 && finish=="stop" && answer.ToString()=="```csharp\nclass C {}\n```","cut-off code continues automatically in the same answer");
        Check(usage?.Total==60,"continuation usage is accumulated");
        calls=0;
        using(var endpoint=new EndpointClient(new FakeHandler(_=> { calls++; return Task.FromResult(Json("{\"choices\":[{\"message\":{\"content\":\"partial\"},\"finish_reason\":\"length\"}]}")); })))
        {
            bool incomplete=false; await endpoint.AnswerToCompletionAsync("Explain threads",settings,"synthetic",_=>{},default,reportIncompleteUsage:v=>incomplete=v);
            Check(calls==3 && incomplete,"continuation has a paid-loop bound and unknown usage stays unknown");
            using var cancelled=new CancellationTokenSource(); cancelled.Cancel();
            try { await endpoint.AnswerToCompletionAsync("Explain threads",settings,"synthetic",_=>{},cancelled.Token); throw new Exception("Cancellation ignored"); } catch(OperationCanceledException) { Check(calls==3,"cancelled answers do not initiate continuation"); }
        }
        var archive=new CallArchive(); archive.StartCapture("synthetic audio"); archive.UpdateCaller(1,1,"Question",true);
        var item=archive.StartAnswer("Question","Explain threads",settings); item.Append("An answer"); item.Usage=new(10,20,30); item.Finish("Completed"); archive.EndCapture();
        var directory=Path.Combine(Path.GetTempPath(),"ansora-save-test-"+Guid.NewGuid().ToString("N"));
        try {
            var save=new CallAutoSave(directory); var revision=archive.Revision; await save.SaveAsync(archive.Snapshot(),revision);
            Check(File.ReadAllText(save.LastPath).EndsWith("</html>") && File.Exists(Path.ChangeExtension(save.LastPath,".json")),"automatic save writes complete HTML and recovery JSON");
            Check(save.IsSaved(archive.Id,revision),"autosave tracks saved call revision"); item.Append(" updated"); Check(!save.IsSaved(archive.Id,archive.Revision),"new answer content marks the call unsaved");
            await save.SaveAsync(archive.Snapshot(),archive.Revision); Check(File.ReadAllText(save.LastPath).Contains("updated"),"checkpoint replaces the same call with latest text");
            Check(Directory.GetFiles(directory,"*.tmp").Length==0,"atomic save leaves no temporary files");
        } finally { Directory.Delete(directory,true); }
        var now=DateTimeOffset.Now; var report=archive.Snapshot(); var entries=Enumerable.Range(0,3).Select(_=>report.Answers[0] with { Usage=new TokenUsage(10000,20000,30000) }).ToArray();
        report=report with { CapturePeriods=new[] { new CapturePeriod(now.AddMinutes(-30),now,"test") }, Answers=entries, ReportedUsage=new(30000,60000,90000) };
        Check(CallMinutes.Estimate(report,30000,now)=="About 10 minutes left","minutes use this call's observed token rate and user's balance");
        Check(CallMinutes.Estimate(report,30000,now.AddHours(1))=="About 10 minutes left","paused time does not inflate remaining minutes");
        Check(CallMinutes.Estimate(report with { Answers=entries.Take(1).ToArray() },30000,now).StartsWith("Estimating"),"minutes wait for a sufficient sample");
        Check(CallMinutes.Estimate(report with { Answers=new[] { entries[0] with { UsageIncomplete=true } } },30000,now).StartsWith("Estimating"),"small samples remain provisional");
        Check(CallMinutes.Estimate(report with { Answers=entries.Select(a=>a with { UsageIncomplete=true }).ToArray() },30000,now)=="Minutes estimate unavailable","unknown billed usage cannot produce misleading minutes");
        Check(CallMinutes.Estimate(report,0,now)=="No minutes available","zero balance is explicit");
    }
    private sealed class FakeHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        { cancellationToken.ThrowIfCancellationRequested(); return respond(request); }
    }
}










