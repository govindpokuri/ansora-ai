using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Win32;
using OverlayCopilot.Services;

namespace OverlayCopilot;

public partial class ResumeWindow : Window
{
    private readonly AppSettings settings;
    private readonly string key;
    private readonly CandidateProfileStore store;
    private CancellationTokenSource? analysisCts;
    private CandidateProfile? draft;
    private bool closed;
    public CandidateProfile? Result { get; private set; }

    public ResumeWindow(AppSettings settings, string key, CandidateProfile? profile, CandidateProfileStore store)
    {
        InitializeComponent(); this.settings = settings; this.key = key; this.store = store;
        Result = profile; draft = profile; ShowDraft();
        SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(this).Handle;
            WindowPrivacy.Apply(handle); WindowInteraction.ExcludeFromTaskbar(handle);
        };
    }
    private void ShowDraft()
    {
        SourceLabel.Text = draft?.SourceName ?? "No resume uploaded";
        ExperienceBox.Text = draft?.Experience ?? "";
        LanguagesBox.Text = draft is null ? "" : string.Join(", ", draft.Languages);
        ResumeTextBox.Text = draft?.ResumeText ?? "";
        SaveButton.IsEnabled = draft is not null; RemoveButton.IsEnabled = Result is not null;
    }
    private async void UploadClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "Choose candidate resume", Filter = "Resume files (*.pdf;*.docx;*.txt)|*.pdf;*.docx;*.txt", CheckFileExists = true, Multiselect = false };
        if (dialog.ShowDialog(this) != true) return;
        UploadButton.IsEnabled = SaveButton.IsEnabled = RemoveButton.IsEnabled = false;
        ExperienceBox.IsEnabled = LanguagesBox.IsEnabled = false;
        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2)); analysisCts = cts;
        ProfileStatus.Text = "Reading resume…";
        try
        {
            var text = await Task.Run(() => ResumeReader.Read(dialog.FileName), cts.Token);
            cts.Token.ThrowIfCancellationRequested();
            if (closed) return;
            draft = new CandidateProfile { SourceName = Path.GetFileName(dialog.FileName), ResumeText = text };
            ShowDraft(); SaveButton.IsEnabled = RemoveButton.IsEnabled = false;
            ProfileStatus.Text = "Analysing experience and coding languages…";
            using var client = new EndpointClient(); var output = new StringBuilder(); TokenUsage? usage = null;
            await client.AnswerAsync("Resume data:\n" + text, settings with { Candidate = null }, key,
                token => output.Append(token), cts.Token, value => usage = value, CandidateProfile.AnalysisInstructions);
            cts.Token.ThrowIfCancellationRequested();
            var profile = CandidateProfile.FromAnalysis(Path.GetFileName(dialog.FileName), text, output.ToString());
            if (closed) return;
            draft = profile; ShowDraft();
            ProfileStatus.Text = "Analysis ready. Review and correct the summary and languages, then choose Use this profile."
                + (usage is null ? "" : $" Analysis tokens: {usage.Total:N0}.");
        }
        catch (OperationCanceledException) { if (!closed) ProfileStatus.Text = "Analysis cancelled or timed out. If resume text is visible, enter the summary and languages manually, or upload again."; }
        catch (ArgumentException ex) { if (!closed) ProfileStatus.Text = ex.Message; }
        catch { if (!closed) ProfileStatus.Text = "Could not complete AI analysis. If resume text is visible, enter the summary and languages manually. Otherwise check the file and upload again."; }
        finally
        {
            analysisCts = null;
            if (!closed)
            {
                UploadButton.IsEnabled = true; SaveButton.IsEnabled = draft is not null; RemoveButton.IsEnabled = Result is not null;
                ExperienceBox.IsEnabled = LanguagesBox.IsEnabled = true;
            }
        }
    }
    private void SaveClick(object sender, RoutedEventArgs e)
    {
        if (draft is null || analysisCts is not null) return;
        try
        {
            var profile = draft with { Experience = ExperienceBox.Text.Trim(), Languages = LanguagesBox.Text.Split(',')
                .Select(x => x.Trim()).Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray() };
            profile.Validate(); store.Save(profile); Result = profile; DialogResult = true;
        }
        catch (ArgumentException ex) { ProfileStatus.Text = ex.Message; }
        catch { ProfileStatus.Text = "Could not save this profile. Try again."; }
    }
    private void RemoveClick(object sender, RoutedEventArgs e)
    {
        if (analysisCts is not null) return;
        try { store.Clear(); Result = null; DialogResult = true; }
        catch { ProfileStatus.Text = "Could not remove the saved profile. Try again."; }
    }
    private void CancelClick(object sender, RoutedEventArgs e) => Close();
    protected override void OnClosing(CancelEventArgs e)
    {
        closed = true; analysisCts?.Cancel(); base.OnClosing(e);
    }
}
