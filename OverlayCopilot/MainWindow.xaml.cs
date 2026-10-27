using System;
using System.CodeDom.Compiler;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using Microsoft.Win32;
using OverlayCopilot.Services;

namespace OverlayCopilot;

public partial class MainWindow : Window
{
	private readonly EndpointClient endpoints = new EndpointClient();

	private readonly CredentialStore credentials = new CredentialStore();

	private readonly CandidateProfileStore candidateProfiles = new CandidateProfileStore();

	private CandidateProfile? candidate;

	private readonly CommercialAccountStore accounts = new CommercialAccountStore();

	private readonly CommercialAccountClient accountClient = new CommercialAccountClient();

	private readonly AppUpdates updates = new AppUpdates();

	private readonly CancellationTokenSource updateLifetime = new CancellationTokenSource();

	private readonly DispatcherTimer updateTimer = new DispatcherTimer
	{
		Interval = TimeSpan.FromMinutes(15)
	};

	private AppRelease? availableUpdate;

	private bool updateBusy;

	private CommercialBalance? accountBalance;

	private bool commercialMode;

	private bool fixedCommercialUrl;

	private bool refreshingAccount;

	private string commercialBaseUrl = "";

	private string commercialSessionId = Guid.NewGuid().ToString("N");

	private readonly LiveTranscript transcript = new LiveTranscript(callerOnly: true);

	private readonly LiveAnswerQueue liveAnswers = new LiveAnswerQueue();

	private CallArchive callArchive = new CallArchive();

	private readonly CallAutoSave autoSave = new CallAutoSave();

	private readonly DispatcherTimer saveTimer = new DispatcherTimer
	{
		Interval = TimeSpan.FromSeconds(15L)
	};

	private bool savingCall;

	private TaskCompletionSource? answerIdle;

	private readonly DispatcherTimer answerTimer = new DispatcherTimer
	{
		Interval = TimeSpan.FromMilliseconds(500L, 0L)
	};

	private readonly DispatcherTimer presentationTimer = new DispatcherTimer(DispatcherPriority.Background)
	{
		Interval = TimeSpan.FromMilliseconds(200L, 0L)
	};

	private AnswerStreamBuffer? activeStream;

	private Paragraph? streamingParagraph;

	private bool answerDirty = true;

	private double answerTextSize = 18.0;

	private bool automaticSuppressed;

	private readonly string transcriptionKey;

	private nint hwnd;

	private bool interactionHotkeyRegistered;

	private LoopbackListener? listener;

	private WhisperTranscriber? localSpeech;

	private CancellationTokenSource? listeningCts;

	private CancellationTokenSource? answerCts;

	private Task? transcriptionTask;

	private bool transitioning;

	private bool closing;

	private int generation;

	private int sessionGeneration;

	private readonly DispatcherTimer audioTimer = new DispatcherTimer
	{
		Interval = TimeSpan.FromMilliseconds(250L, 0L)
	};

	private DateTimeOffset listeningStarted;

	private long lastAudioBytes;

	private DateTimeOffset lastAudioDetected;

	public MainWindow()
	{
		InitializeComponent();
		presentationTimer.Tick += delegate
		{
			RefreshAnswerPresentation();
		};
		presentationTimer.Start();
		RefreshAnswerPresentation();
		UpdateTokenSummary();
		ComboBox[] array = new ComboBox[4] { DeviceBox, TranscriptionAuth, AnswerAuth, AudioSource };
		for (int num = 0; num < array.Length; num++)
		{
			array[num].DropDownOpened += ProtectDropdown;
		}
		try
		{
			LoadSettings(AppSettings.Load());
			RefreshDevices();
		}
		catch
		{
			Status.Text = "Could not load settings or audio devices. Open settings to configure.";
		}
		try
		{
			candidate = candidateProfiles.Load();
			UpdateCandidateStatus();
		}
		catch
		{
			CandidateStatus.Text = "Saved candidate profile could not be loaded. Upload the resume again.";
		}
		var text = Environment.GetEnvironmentVariable("OVERLAYCOPILOT_AI_KEY") ?? "";
		Environment.SetEnvironmentVariable("OVERLAYCOPILOT_AI_KEY", null);
		transcriptionKey = Environment.GetEnvironmentVariable("OVERLAYCOPILOT_SPEECH_KEY") ?? "";
		Environment.SetEnvironmentVariable("OVERLAYCOPILOT_SPEECH_KEY", null);
		audioTimer.Tick += delegate
		{
			UpdateAudioStatus();
		};
		answerTimer.Tick += delegate
		{
			DispatchLiveAnswer();
		};
		answerTimer.Start();
		try
		{
			AppSettings appSettings = ReadSettings();
			if (!string.IsNullOrWhiteSpace(text))
			{
				credentials.Save(appSettings.AnswerUrl, appSettings.AnswerAuth, text);
			}
			AnswerStatus.Text = ((GetAnswerKey(appSettings).Length > 0) ? "Checking background AI connection…" : "Background AI connection is not configured for this endpoint.");
		}
		catch
		{
			AnswerStatus.Text = "Background AI connection could not be loaded.";
		}
		SettingsPanel.IsExpanded = string.IsNullOrEmpty(AnswerUrl.Text);
		base.SourceInitialized += delegate
		{
			InitializeNativeWindow();
		};
		bool firstSignIn = commercialMode && accounts.Load(commercialBaseUrl) is null;
		base.Loaded += async delegate
		{
			await VerifyAiConnectionAsync();
			if (firstSignIn && !closing)
			{
				AccountClick(this, new RoutedEventArgs());
			}
		};
		saveTimer.Tick += async delegate
		{
			UpdateTokenSummary();
			if (!savingCall)
			{
				await SaveCallAsync();
			}
		};
		saveTimer.Start();
		Uri? result;
		bool flag = commercialMode && Uri.TryCreate(commercialBaseUrl, UriKind.Absolute, out result) && !result.IsLoopback;
		UpdateButton.Visibility = ((!flag) ? Visibility.Collapsed : Visibility.Visible);
		updateTimer.Tick += async delegate
		{
			await CheckUpdatesAsync(interactive: false);
		};
		if (flag)
		{
			updateTimer.Start();
			base.Loaded += async delegate
			{
				await CheckUpdatesAsync(interactive: false);
			};
		}
	}

    private async Task CheckUpdatesAsync(bool interactive)
    {
        if (closing || updateBusy || !commercialMode) return;
        updateBusy = true;
        try
        {
            availableUpdate = await updates.CheckAsync(commercialBaseUrl, updateLifetime.Token);
            if (closing) return;
            if (interactive && availableUpdate is null) MessageBox.Show(this, "You have the latest published version.", "Ansora AI updates");
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or InvalidDataException or JsonException)
        {
            if (interactive && !closing) MessageBox.Show(this, "Could not check for updates. Please try again later.", "Ansora AI updates");
        }
        finally { updateBusy = false; if(!closing)RefreshUpdateControls(); }
    }
    private void RefreshUpdateControls(bool downloading=false)
    {
        UpdateBadge.Visibility=availableUpdate is null?Visibility.Collapsed:Visibility.Visible;
        UpdateBadge.IsEnabled=UpdateButton.IsEnabled=!updateBusy;
        UpdateBadgeLabel.Text=downloading?"Downloading…":"↑ Update available";
        UpdateButton.Content=availableUpdate is null?"Updates":"Install update";
        UpdateBadge.ToolTip=UpdateButton.ToolTip=availableUpdate is null?$"Installed version {AppUpdates.CurrentVersion}":$"Install version {availableUpdate.Version}: {availableUpdate.Notes}";
    }
    private async void UpdateClick(object sender, RoutedEventArgs e)
    {
        if (updateBusy || closing) return;
        if (availableUpdate is null) await CheckUpdatesAsync(true);
        if (availableUpdate is not { } release || closing) return;
        if (listener is not null || transitioning || answerCts is not null)
        {
            MessageBox.Show(this,"Finish your call, stop listening, and wait for the answer before installing. Your call will be saved automatically.","Update available"); return;
        }
        if (MessageBox.Show(this,$"Install Ansora AI {release.Version}?\n\n{release.Notes}\n\nThe app will close and open the setup wizard. Your saved profile, account and settings will remain.","Ansora AI update",MessageBoxButton.OKCancel)!=MessageBoxResult.OK)return;
        updateBusy=true; RefreshUpdateControls(downloading:true);
        try
        {
            var path=await updates.DownloadAsync(release,commercialBaseUrl,Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"OverlayCopilot","updates"),updateLifetime.Token);
            if(closing)return;
            if(listener is not null || transitioning || answerCts is not null)
            {
                File.Delete(path); MessageBox.Show(this,"Finish your call before installing; select Update available again when finished.","Ansora AI update"); return;
            }
            if(!await SaveCallAsync())return;
            Process.Start(new ProcessStartInfo(path) { UseShellExecute=true }); Close();
        }
        catch(Exception ex) when(ex is HttpRequestException or OperationCanceledException or InvalidDataException or IOException or UnauthorizedAccessException or Win32Exception)
        {
            if(!closing)MessageBox.Show(this,"The update could not be downloaded, verified or started. Your installed app has not changed. Please try again later.","Ansora AI update");
        }
        finally { updateBusy=false; if(!closing)RefreshUpdateControls(); }
    }

	private void InitializeNativeWindow()
	{
		hwnd = new WindowInteropHelper(this).Handle;
		base.ShowInTaskbar = false;
		WindowInteraction.SetClickThrough(hwnd, enabled: false);
		if (!WindowInteraction.ExcludeFromTaskbar(hwnd))
		{
			Status.Text = "Could not apply taskbar visibility settings.";
		}
		ProtectionStatus.Text = WindowPrivacy.Apply(hwnd);
		HwndSource.FromHwnd(hwnd)?.AddHook(WindowMessage);
		bool flag = WindowPrivacy.RegisterHotKey(hwnd, 1, 16387u, 72u);
		HideButton.IsEnabled = flag;
		flag &= WindowPrivacy.RegisterHotKey(hwnd, 2, 16387u, 76u);
		flag &= WindowPrivacy.RegisterHotKey(hwnd, 3, 16387u, 13u);
		interactionHotkeyRegistered = WindowPrivacy.RegisterHotKey(hwnd, 4, 16387u, 84u);
		flag &= interactionHotkeyRegistered;
		flag &= WindowPrivacy.RegisterHotKey(hwnd, 5, 16387u, 81u);
		ClickThrough.IsEnabled = interactionHotkeyRegistered;
		if (!flag)
		{
			Status.Text = "One or more shortcuts are already in use. Window buttons remain available.";
		}
	}

	private nint WindowMessage(nint handle, int message, nint wParam, nint lParam, ref bool handled)
	{
		if (message != 786)
		{
			return IntPtr.Zero;
		}
		handled = true;
		switch (((IntPtr)wParam).ToInt32())
		{
		case 1:
			ToggleVisibility();
			break;
		case 2:
			_ = ToggleListeningAsync();
			break;
		case 3:
			AnswerCurrentAsync();
			break;
		case 4:
			ClickThrough.IsChecked = ClickThrough.IsChecked != true;
			break;
		case 5:
			Close();
			break;
		}
		return IntPtr.Zero;
	}

	private void ToggleVisibility()
	{
		if (base.IsVisible)
		{
			Hide();
			return;
		}
		Show();
		Activate();
	}

	private void ProtectDropdown(object? sender, EventArgs e)
	{
		if (sender is ComboBox comboBox && comboBox.Template.FindName("PART_Popup", comboBox) is Popup { Child: { } child } && PresentationSource.FromVisual(child) is HwndSource hwndSource && !WindowPrivacy.Apply(hwndSource.Handle).StartsWith("Capture exclusion enabled", StringComparison.Ordinal))
		{
			Status.Text = "Dropdown capture exclusion could not be verified. Close settings while sharing.";
		}
	}

	private void MoveHandleDragDelta(object sender, DragDeltaEventArgs e)
	{
		base.Left = (double.IsNaN(base.Left) ? 0.0 : base.Left) + e.HorizontalChange;
		base.Top = (double.IsNaN(base.Top) ? 0.0 : base.Top) + e.VerticalChange;
	}

	private void MoveHandleKeyDown(object sender, KeyEventArgs e)
	{
		int num = (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 40 : 10);
		(int, int) tuple = e.Key switch
		{
			Key.Left => (-num, 0), 
			Key.Right => (num, 0), 
			Key.Up => (0, -num), 
			Key.Down => (0, num), 
			_ => (0, 0), 
		};
		(int, int) tuple2 = tuple;
		if (tuple2.Item1 != 0 || tuple2.Item2 != 0)
		{
			MoveHandleDragDelta(sender, new DragDeltaEventArgs(tuple.Item1, tuple.Item2));
			e.Handled = true;
		}
	}

	private void FocusModeChanged(object sender, RoutedEventArgs e)
	{
		if (QuestionPanel != null && AnswerPanel != null && TranscriptOnly != null && AutoAnswer != null)
		{
			AnswerPanel.Visibility = ((TranscriptOnly.IsChecked == true) ? Visibility.Collapsed : Visibility.Visible);
			QuestionPanel.Visibility = ((TranscriptOnly.IsChecked == true || AutoAnswer.IsChecked == true) ? Visibility.Collapsed : Visibility.Visible);
		}
	}

	private void AutoAnswerChanged(object sender, RoutedEventArgs e)
	{
		if (liveAnswers != null && AnswerPanel != null)
		{
			liveAnswers.Reset();
			automaticSuppressed = false;
			if (AutoAnswer.IsChecked == true && transcript.Context.Length > 0)
			{
				liveAnswers.Offer(transcript.Context);
			}
			FocusModeChanged(sender, e);
		}
	}

	private void DispatchLiveAnswer()
	{
		if (!closing && PinAnswer.IsChecked != true && QuestionTabs.SelectedIndex != 1 && listener != null && AutoAnswer.IsChecked == true && !automaticSuppressed && answerCts == null)
		{
			var answerJob = liveAnswers.TryTake(DateTimeOffset.UtcNow);
			if (answerJob is not null)
			{
				_ = RunLiveAnswerAsync(answerJob);
			}
		}
	}

	private async Task RunLiveAnswerAsync(AnswerJob job)
	{
		try
		{
			await GenerateAnswerAsync(job.Prompt, automatic: true, job.Question, job.QuestionId);
		}
		finally
		{
			liveAnswers.Complete(job.Version);
		}
	}

	private string GetAnswerKey(AppSettings settings)
	{
		try
		{
			if (settings.CommercialMode)
			{
				return accounts.Load(settings.CommercialBaseUrl)?.Token ?? "";
			}
			var text = credentials.Load(settings.AnswerUrl, settings.AnswerAuth);
			if (text.Length == 0 && credentials.ImportTestPackage(Path.Combine(AppContext.BaseDirectory, "test-connection.json"), settings.AnswerUrl, settings.AnswerAuth))
			{
				text = credentials.Load(settings.AnswerUrl, settings.AnswerAuth);
			}
			return text;
		}
		catch
		{
			return "";
		}
	}

	private async void TestAiClick(object sender, RoutedEventArgs e)
	{
		await VerifyAiConnectionAsync();
	}

	private async Task VerifyAiConnectionAsync()
	{
		if (!TestAiButton.IsEnabled || closing)
		{
			return;
		}
		TestAiButton.IsEnabled = false;
		try
		{
			AppSettings appSettings = ReadSettings();
			if (appSettings.CommercialMode)
			{
				await RefreshCommercialAccountAsync();
				automaticSuppressed = accountBalance is null || accountBalance.Available <= 0 || !accountBalance.AiConfigured;
				if (!automaticSuppressed)
				{
					liveAnswers.Reset();
					liveAnswers.Offer(transcript.Context);
					AnswerStatus.Text = "Ready to read";
				}
				else
				{
					AnswerStatus.Text = ((accountBalance is null) ? "Sign in to enable AI answers." : "Top up your tokens or check service availability on the website.");
				}
				return;
			}
			ValidateAnswerSettings(appSettings);
			if (string.IsNullOrWhiteSpace(GetAnswerKey(appSettings)))
			{
				throw new ArgumentException("Background AI connection is not configured for this endpoint.");
			}
			AnswerStatus.Text = "Testing AI authentication…";
			StringBuilder response = new StringBuilder();
			using CancellationTokenSource timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20L));
			await endpoints.AnswerAsync("Reply with exactly READY.", appSettings with
			{
				Candidate = null
			}, GetAnswerKey(appSettings), delegate(string token)
			{
				response.Append(token);
			}, timeout.Token);
			if (closing)
			{
				return;
			}
			if (response.Length == 0)
			{
				throw new InvalidOperationException("AI returned no text.");
			}
			automaticSuppressed = false;
			liveAnswers.Reset();
			liveAnswers.Offer(transcript.Context);
			AnswerStatus.Text = "Ready to read";
		}
		catch (Exception ex)
		{
			if (!closing)
			{
				AnswerStatus.Text = FriendlyError(ex, "AI connection failed");
			}
		}
		finally
		{
			if (!closing)
			{
				TestAiButton.IsEnabled = true;
			}
		}
	}

	private void ClickThroughChanged(object sender, RoutedEventArgs e)
	{
		if (hwnd == IntPtr.Zero)
		{
			return;
		}
		bool valueOrDefault = ClickThrough.IsChecked == true;
		if (valueOrDefault && !interactionHotkeyRegistered)
		{
			ClickThrough.IsChecked = false;
			return;
		}
		if (!WindowInteraction.SetClickThrough(hwnd, valueOrDefault))
		{
			Status.Text = "Could not change desktop interaction mode.";
		}
		InteractionHint.Visibility = ((!valueOrDefault) ? Visibility.Collapsed : Visibility.Visible);
	}

	private void HideClick(object sender, RoutedEventArgs e)
	{
		Hide();
	}

	private void ExitClick(object sender, RoutedEventArgs e)
	{
		Close();
	}

	private void UpdateTokenSummary()
	{
		TokenUsage reportedUsage = callArchive.ReportedUsage;
		MinutesLeft.Text = (commercialMode ? CallMinutes.Estimate(callArchive.Snapshot(), accountBalance?.Available, DateTimeOffset.Now) : "Local AI connection");
		TokenSummary.Text = $"AI tokens · {reportedUsage.Total:N0} · {callArchive.RequestCount} requests" + ((callArchive.MissingUsageRequests > 0) ? " + unknown" : "") + ((callArchive.PendingRequests > 0) ? " · updating" : "");
		TokenSummary.ToolTip = $"Provider-reported input: {reportedUsage.Input:N0}; output: {reportedUsage.Output:N0}. Usage unavailable for {callArchive.MissingUsageRequests} request(s). Connection checks are excluded. Counts update when the provider reports them.";
		ExportButton.IsEnabled = callArchive.HasContent;
	}

	private async void ExportCallClick(object sender, RoutedEventArgs e)
	{
		if (!callArchive.HasContent)
		{
			Status.Text = "No call details to export yet.";
			return;
		}
		CallReport report = callArchive.Snapshot();
		SaveFileDialog dialog = new SaveFileDialog
		{
			Title = "Export call reference",
			FileName = $"call-{DateTime.Now:yyyyMMdd-HHmmss}",
			Filter = "Call reference document (*.html)|*.html|Complete call data (*.json)|*.json",
			DefaultExt = ".html",
			AddExtension = true
		};
		if (dialog.ShowDialog(this) != true)
		{
			return;
		}
		ExportButton.IsEnabled = false;
		try
		{
			var content = (Path.GetExtension(dialog.FileName).Equals(".json", StringComparison.OrdinalIgnoreCase) ? CallExporter.Json(report) : CallExporter.Html(report));
			await CallExporter.WriteAtomicAsync(dialog.FileName, content);
			if (!closing)
			{
				Status.Text = "Call exported · " + Path.GetFileName(dialog.FileName);
			}
		}
		catch
		{
			if (!closing)
			{
				Status.Text = "Could not export the call. Choose a writable location and try again.";
			}
		}
		finally
		{
			if (!closing)
			{
				UpdateTokenSummary();
			}
		}
	}

	private void OptionsClick(object sender, RoutedEventArgs e)
	{
		OptionsPanel.Visibility = ((OptionsPanel.Visibility == Visibility.Visible) ? Visibility.Collapsed : Visibility.Visible);
	}

	private void SavedCallsClick(object sender, RoutedEventArgs e)
	{
		try
		{
			var text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Personal), "Ansora AI", "Calls");
			Directory.CreateDirectory(text);
			Process.Start(new ProcessStartInfo(text)
			{
				UseShellExecute = true
			});
		}
		catch
		{
			AnswerStatus.Text = "Could not open saved calls.";
		}
	}

	private async Task<bool> SaveCallAsync()
	{
		CallArchive callArchive = this.callArchive;
		if (!callArchive.HasContent)
		{
			return true;
		}
		long revision = callArchive.Revision;
		if (autoSave.IsSaved(callArchive.Id, revision))
		{
			return true;
		}
		savingCall = true;
		try
		{
			await autoSave.SaveAsync(callArchive.Snapshot(), revision);
			return true;
		}
		catch
		{
			AnswerStatus.Text = "Call could not be saved. Check Documents folder access.";
			return false;
		}
		finally
		{
			savingCall = false;
		}
	}

	private void OfferResumeSetup()
	{
		Window dialog = new Window
		{
			Title = "Personalise your answers",
			Owner = this,
			Width = 460.0,
			Height = 240.0,
			ResizeMode = ResizeMode.NoResize,
			WindowStartupLocation = WindowStartupLocation.CenterOwner,
			Background = Brushes.MidnightBlue,
			Foreground = Brushes.White,
			ShowInTaskbar = false
		};
		StackPanel stackPanel = new StackPanel
		{
			Margin = new Thickness(24.0)
		};
		stackPanel.Children.Add(new TextBlock
		{
			Text = "Add your resume",
			FontSize = 22.0,
			FontWeight = FontWeights.SemiBold
		});
		stackPanel.Children.Add(new TextBlock
		{
			Text = "Use your experience and languages for more relevant answers. You can add it later from Options → Resume.",
			TextWrapping = TextWrapping.Wrap,
			Margin = new Thickness(0.0, 12.0, 0.0, 16.0)
		});
		WrapPanel wrapPanel = new WrapPanel();
		Button button = new Button
		{
			Content = "Upload resume"
		};
		Button button2 = new Button
		{
			Content = "Skip for this call"
		};
		button.Click += delegate
		{
			dialog.DialogResult = true;
		};
		button2.Click += delegate
		{
			dialog.DialogResult = false;
		};
		wrapPanel.Children.Add(button);
		wrapPanel.Children.Add(button2);
		stackPanel.Children.Add(wrapPanel);
		dialog.Content = stackPanel;
		if (dialog.ShowDialog() == true)
		{
			ResumeClick(this, new RoutedEventArgs());
		}
	}

	private void SettingsClick(object sender, RoutedEventArgs e)
	{
		SettingsPanel.IsExpanded = !SettingsPanel.IsExpanded;
	}

	private void ResumeClick(object sender, RoutedEventArgs e)
	{
		try
		{
			AppSettings settings = ReadSettings();
			ResumeWindow resumeWindow = new ResumeWindow(settings, GetAnswerKey(settings), candidate, candidateProfiles)
			{
				Owner = this
			};
			if (resumeWindow.ShowDialog() == true)
			{
				candidate = resumeWindow.Result;
				UpdateCandidateStatus();
			}
			if (commercialMode)
			{
				_ = RefreshCommercialAccountAsync();
			}
		}
		catch
		{
			Status.Text = "Could not open the resume panel. Check the background AI settings.";
		}
	}

	private void UpdateCandidateStatus()
	{
		ResumeButton.Content = ((candidate is null) ? "Resume" : "Resume ✓");
		CandidateStatus.Text = ((candidate is null) ? "No candidate profile · upload a resume to personalise answers" : ("Profile: " + candidate.SourceName + " · Coding: " + ((candidate.Languages.Length == 0) ? "no languages confirmed" : string.Join(", ", candidate.Languages))));
	}

	private async void AccountClick(object sender, RoutedEventArgs e)
	{
		try
		{
			AccountWindow window = new AccountWindow(commercialBaseUrl, fixedCommercialUrl, accounts)
			{
				Owner = this
			};
			window.ShowDialog();
			if (window.Changed)
			{
				commercialBaseUrl = window.BaseUrl;
				AnswerUrl.Text = commercialBaseUrl.TrimEnd('/') + "/api/chat/completions";
				ReadSettings().Save();
			}
			await VerifyAiConnectionAsync();
			if (window.Changed && accounts.Load(commercialBaseUrl) is not null && candidate is null)
			{
				OfferResumeSetup();
			}
		}
		catch
		{
			CommercialSummary.Text = "Could not open the account. Check the service website.";
		}
	}

	private async Task RefreshCommercialAccountAsync()
	{
		if (!commercialMode || refreshingAccount || closing)
		{
			return;
		}
		refreshingAccount = true;
		try
		{
			var commercialSession = accounts.Load(commercialBaseUrl);
			accountBalance = null;
			UpdateAccountHeader(commercialSession?.Email);
			if (commercialSession is null)
			{
				CommercialSummary.Text = "Sign in · purchase tokens on the website.";
				return;
			}
			using CancellationTokenSource timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15L));
			accountBalance = await accountClient.BalanceAsync(commercialSession, timeout.Token);
			if (!closing)
			{
				UpdateAccountHeader(accountBalance.Email);
				CommercialSummary.Text = accountBalance.Email + " · manage balance on the website";
			}
		}
		catch (Exception ex)
		{
			if (!closing)
			{
				CommercialSummary.Text = ((ex is InvalidOperationException) ? ex.Message : "Could not refresh account balance. Select your account to retry.");
			}
		}
		finally
		{
			refreshingAccount = false;
			if (!closing)
			{
				UpdateTokenSummary();
			}
		}
	}

	private void UpdateAccountHeader(string? email)
	{
		bool flag = !string.IsNullOrWhiteSpace(email);
		AccountButtonLabel.Text = (flag ? email : "Sign in");
		AccountButton.ToolTip = (flag ? ("Signed in as " + email + ". Select to manage your account or sign out.") : "Sign in with your email to use your tokens.");
		AutomationProperties.SetName(AccountButton, flag ? ("Account: " + email) : "Sign in");
	}

	private void AnswerTextChanged(object sender, TextChangedEventArgs e)
	{
		answerDirty = true;
	}

	private void AnswerHistoryChanged(object sender, SelectionChangedEventArgs e)
	{
		if (AnswerHistory != null && AnswerBox != null && activeStream == null)
		{
			if (AnswerHistory.SelectedIndex > 0 && AnswerHistory.SelectedItem is ComboBoxItem { Tag: RecordedAnswer tag })
			{
				PinAnswer.IsChecked = true;
				AnswerBox.Text = tag.Answer;
				AnswerQuestionText.Text = tag.Question;
				RefreshAnswerPresentation();
			}
			else if (AnswerHistory.SelectedIndex == 0 && AnswerHistory.Items.Count > 1 && AnswerHistory.Items[1] is ComboBoxItem { Tag: RecordedAnswer tag2 })
			{
				PinAnswer.IsChecked = false;
				AnswerBox.Text = tag2.Answer;
				AnswerQuestionText.Text = tag2.Question;
				RefreshAnswerPresentation();
			}
		}
	}

	private void SmallerTextClick(object sender, RoutedEventArgs e)
	{
		ChangeAnswerTextSize(-1);
	}

	private void LargerTextClick(object sender, RoutedEventArgs e)
	{
		ChangeAnswerTextSize(1);
	}

	private void ChangeAnswerTextSize(int step)
	{
		answerTextSize = Math.Clamp(answerTextSize + (double)step, 15.0, 24.0);
		AnswerDisplay.Document.FontSize = answerTextSize;
		AnswerDisplay.Document.LineHeight = answerTextSize * 1.55;
		answerDirty = true;
		RefreshAnswerPresentation();
	}

	public void RefreshAnswerPresentation()
	{
		DrainAnswerStream();
		if (activeStream == null && answerDirty && AnswerDisplay != null)
		{
			double verticalOffset = AnswerDisplay.VerticalOffset;
			AnswerDisplay.Document = AnswerPresentation.Format(AnswerBox.Text, answerTextSize);
			ApplyAnswerContrast();
			AnswerDisplay.UpdateLayout();
			AnswerDisplay.ScrollToVerticalOffset(verticalOffset);
			answerDirty = false;
		}
	}

	private void DrainAnswerStream()
	{
		var answerStreamBuffer = activeStream;
		if (answerStreamBuffer == null || answerStreamBuffer.Generation != generation || closing)
		{
			return;
		}
		var text = answerStreamBuffer.Drain();
		if (text.Length != 0)
		{
			if (answerStreamBuffer.FirstBatch)
			{
				AnswerQuestionText.Text = "Answering: " + answerStreamBuffer.Question;
				AnswerBox.Clear();
				FlowDocument flowDocument = AnswerPresentation.Format("", answerTextSize);
				flowDocument.Blocks.Clear();
				streamingParagraph = new Paragraph
				{
					Margin = new Thickness(0.0)
				};
				flowDocument.Blocks.Add(streamingParagraph);
				AnswerDisplay.Document = flowDocument;
				ApplyAnswerContrast();
				AnswerDisplay.ScrollToHome();
				answerStreamBuffer.FirstBatch = false;
			}
			AnswerDisplay.Document.FontSize = answerTextSize;
			AnswerDisplay.Document.LineHeight = answerTextSize * 1.55;
			streamingParagraph!.Inlines.Add(new Run(text));
			AnswerBox.AppendText(text);
		}
	}

	private async void ListenClick(object sender, RoutedEventArgs e)
	{
		await ToggleListeningAsync();
	}

	private async void AnswerClick(object sender, RoutedEventArgs e)
	{
		await AnswerCurrentAsync();
	}

	private void QuestionTabChanged(object sender, SelectionChangedEventArgs e)
	{
		if (e.Source == QuestionTabs && ManualQuestionBox != null)
		{
			liveAnswers.Reset();
			if (QuestionTabs.SelectedIndex == 1)
			{
				TranscriptOnly.IsChecked = false;
				ManualQuestionBox.Focus();
			}
		}
	}

	private async void ManualAnswerClick(object sender, RoutedEventArgs e)
	{
		await AnswerManualAsync();
	}

	private async void ManualQuestionKeyDown(object sender, KeyEventArgs e)
	{
		if (e.Key == Key.Return && Keyboard.Modifiers == ModifierKeys.Control)
		{
			e.Handled = true;
			await AnswerManualAsync();
		}
	}

	private Task AnswerManualAsync()
	{
		if (answerCts != null)
		{
			answerCts.Cancel();
			return Task.CompletedTask;
		}
		var text = ManualQuestionBox.Text.Trim();
		if (text.Length == 0)
		{
			AnswerStatus.Text = "Paste or type a question first.";
			ManualQuestionBox.Focus();
			return Task.CompletedTask;
		}
		if (text.Length > 23000)
		{
			AnswerStatus.Text = "Pasted question is too long. Limit it to 23,000 characters.";
			return Task.CompletedTask;
		}
		TranscriptOnly.IsChecked = false;
		var text2 = ((CodingAnswer.IsChecked == true) ? "Solve this coding problem or fix the supplied code. Give a short approach, a complete solution in a fenced code block, and time/space complexity where relevant. Use only the candidate's reviewed coding languages. If no reviewed language is available, use the explicitly requested language; otherwise ask which language to use. State essential assumptions briefly. Keep explanations in simple English." : "Answer this pasted question directly in short, simple English. Follow the requested format and the candidate's reviewed experience.");
		return GenerateAnswerAsync(text2 + "\n\nPasted question / problem:\n" + text, automatic: false, text);
	}

	private async void ClearClick(object sender, RoutedEventArgs e)
	{
		if (listener != null || answerCts != null)
		{
			AnswerStatus.Text = "Stop listening and let the answer finish before clearing.";
		}
		else if (await SaveCallAsync())
		{
			answerCts?.Cancel();
			generation++;
			sessionGeneration++;
			transcript.Clear();
			liveAnswers.Reset();
			automaticSuppressed = false;
			TranscriptBox.Clear();
			QuestionBox.Clear();
			ManualQuestionBox.Clear();
			AnswerBox.Clear();
			DraftText.Text = "Live drafts appear here while you speak.";
			callArchive = new CallArchive();
			UpdateTokenSummary();
			PinAnswer.IsChecked = false;
			while (AnswerHistory.Items.Count > 1)
			{
				AnswerHistory.Items.RemoveAt(1);
			}
			AnswerHistory.SelectedIndex = 0;
			AnswerHistory.IsEnabled = true;
			commercialSessionId = Guid.NewGuid().ToString("N");
			CallerQuestionText.Text = "The caller’s question will appear here as they speak.";
			AnswerQuestionText.Text = "Waiting for a caller question";
			Status.Text = "Session cleared.";
		}
	}

	private void RefreshDevicesClick(object sender, RoutedEventArgs e)
	{
		try
		{
			RefreshDevices();
			Status.Text = "Audio devices refreshed.";
		}
		catch
		{
			Status.Text = "Could not enumerate audio devices.";
		}
	}

	private void RefreshDevices()
	{
		var selectedValue = DeviceBox.SelectedValue as string;
		DeviceBox.ItemsSource = LoopbackListener.GetDevices();
		DeviceBox.SelectedValue = selectedValue;
		if (DeviceBox.SelectedIndex < 0 && DeviceBox.Items.Count > 0)
		{
			DeviceBox.SelectedValue = LoopbackListener.GetDefaultDeviceId();
		}
	}

	private void AudioSourceChanged(object sender, SelectionChangedEventArgs e)
	{
		if (DeviceBox == null)
		{
			return;
		}
		try
		{
			DeviceBox.SelectedValue = null;
			RefreshDevices();
		}
		catch
		{
			if (Status != null)
			{
				Status.Text = "No active device for this source. Select another audio source.";
			}
		}
	}

	private void UpdateAudioStatus()
	{
		if (listener != null)
		{
			DateTimeOffset utcNow = DateTimeOffset.UtcNow;
			long receivedBytes = listener.ReceivedBytes;
			AudioMeter.Value = Math.Min(100f, listener.Peak * 300f);
			if (receivedBytes != lastAudioBytes && listener.Peak >= 0.004f)
			{
				lastAudioDetected = utcNow;
			}
			lastAudioBytes = receivedBytes;
			AudioStatus.Text = ((listener.Peak >= 0.004f) ? "Caller · audio received" : "Caller · waiting for speech");
			if (utcNow - listeningStarted > TimeSpan.FromSeconds(5L) && utcNow - lastAudioDetected > TimeSpan.FromSeconds(5L))
			{
				AudioStatus.Text = "No caller audio. Choose the speakers/headphones actually used by the call.";
			}
		}
	}

	private void BackgroundOpacityChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
	{
		bool flag = e.NewValue < 0.7;
		(string, string)[] array = new(string, string)[3]
		{
			("OverlayBackground", "#101827"),
			("OverlayPanelBackground", "#1B2638"),
			("OverlayAnswerBackground", "#F3F6FC")
		};
		for (int i = 0; i < array.Length; i++)
		{
			var (text, text2) = array[i];
			base.Resources[text] = new SolidColorBrush((Color)ColorConverter.ConvertFromString((text == "OverlayAnswerBackground" && flag) ? "#101827" : text2))
			{
				Opacity = Math.Clamp(e.NewValue, 0.0, 1.0)
			};
		}
		array = new(string, string)[4]
		{
			("OverlaySecondaryText", flag ? "#EDF4FF" : "#9CAEC5"),
			("OverlayAnswerText", flag ? "#FFFFFF" : "#17243A"),
			("OverlayAnswerSecondaryText", flag ? "#EDF4FF" : "#536781"),
			("OverlayAnswerHeading", flag ? "#A4FFF0" : "#356960")
		};
		for (int i = 0; i < array.Length; i++)
		{
			var (key, value) = array[i];
			base.Resources[key] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(value));
		}
		base.Resources["OverlayTextShadow"] = new DropShadowEffect
		{
			Color = Colors.Black,
			BlurRadius = 4.0,
			ShadowDepth = 1.0,
			Opacity = (flag ? 1 : 0)
		};
		ApplyAnswerContrast();
	}

	private void ApplyAnswerContrast()
	{
		if (AnswerDisplay == null)
		{
			return;
		}
		AnswerDisplay.Document.Foreground = (Brush)base.Resources["OverlayAnswerText"];
		Slider backgroundOpacitySlider = BackgroundOpacitySlider;
		bool flag = backgroundOpacitySlider != null && backgroundOpacitySlider.Value < 0.7;
		foreach (Block block in AnswerDisplay.Document.Blocks)
		{
			if (block.FontFamily?.Source == "Consolas")
			{
				block.Background = new SolidColorBrush(flag ? Color.FromArgb(200, 16, 24, 39) : Color.FromRgb(225, 233, 244));
			}
		}
	}

	private void SaveBackgroundOpacity(object sender, RoutedEventArgs e)
	{
		if (!base.IsLoaded)
		{
			return;
		}
		try
		{
			ReadSettings().Save();
		}
		catch
		{
			Status.Text = "Background adjusted for this session; the preference could not be saved.";
		}
	}

	private void LoadSettings(AppSettings settings)
	{
		BackgroundOpacitySlider.Value = (double.IsFinite(settings.BackgroundOpacity) ? Math.Clamp(settings.BackgroundOpacity, 0.0, 1.0) : 1.0);
		commercialMode = settings.CommercialMode;
		commercialBaseUrl = settings.CommercialBaseUrl;
		if (commercialMode)
		{
			base.Title = "Ansora AI";
		}
		try
		{
			var path = Path.Combine(AppContext.BaseDirectory, "settings.defaults.json");
			fixedCommercialUrl = File.Exists(path) && !string.IsNullOrWhiteSpace(JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path))?.CommercialBaseUrl);
		}
		catch
		{
			fixedCommercialUrl = false;
		}
		Button accountButton = AccountButton;
		Visibility visibility = (CommercialSummary.Visibility = ((!commercialMode) ? Visibility.Collapsed : Visibility.Visible));
		accountButton.Visibility = visibility;
		StackPanel aiEndpointSettings = AiEndpointSettings;
		visibility = (SpeechEndpointSettings.Visibility = (commercialMode ? Visibility.Collapsed : Visibility.Visible));
		aiEndpointSettings.Visibility = visibility;
		UseLocalSpeech.Visibility = (commercialMode ? Visibility.Collapsed : Visibility.Visible);
		SettingsPanel.Header = (commercialMode ? "Audio & answer preferences" : "Endpoints & audio settings");
		TestAiButton.Content = (commercialMode ? "Refresh account status" : "Test AI connection");
		try
		{
			UpdateAccountHeader((!commercialMode) ? null : accounts.Load(commercialBaseUrl)?.Email);
		}
		catch
		{
			UpdateAccountHeader(null);
		}
		TextBox answerUrl = AnswerUrl;
		bool isReadOnly = (AnswerModel.IsReadOnly = commercialMode);
		answerUrl.IsReadOnly = isReadOnly;
		AnswerAuth.IsEnabled = !commercialMode;
		TranscriptionUrl.Text = settings.TranscriptionUrl;
		TranscriptionModel.Text = settings.TranscriptionModel;
		AnswerUrl.Text = settings.AnswerUrl;
		AnswerModel.Text = settings.AnswerModel;
		MaxTokens.Text = settings.MaxTokens.ToString(CultureInfo.InvariantCulture);
		Temperature.Text = settings.Temperature.ToString(CultureInfo.InvariantCulture);
		SystemPrompt.Text = settings.SystemPrompt;
		TranscriptionAuth.SelectedIndex = ((settings.TranscriptionAuth == "api-key") ? 1 : 0);
		AnswerAuth.SelectedIndex = ((settings.AnswerAuth == "Bearer") ? 1 : 0);
		UseLocalSpeech.IsChecked = commercialMode || settings.UseLocalSpeech;
		AudioSource.SelectedIndex = 0;
		DeviceBox.ItemsSource = LoopbackListener.GetDevices();
		DeviceBox.SelectedValue = settings.DeviceId;
	}

	private AppSettings ReadSettings()
	{
		int result;
		double result2;
		return new AppSettings
		{
			BackgroundOpacity = BackgroundOpacitySlider.Value,
			TranscriptionUrl = TranscriptionUrl.Text.Trim(),
			TranscriptionModel = TranscriptionModel.Text.Trim(),
			AnswerUrl = AnswerUrl.Text.Trim(),
			AnswerModel = AnswerModel.Text.Trim(),
			DeviceId = ((DeviceBox.SelectedValue as string) ?? ""),
			SystemPrompt = SystemPrompt.Text.Trim(),
			Candidate = candidate,
			CommercialMode = commercialMode,
			CommercialBaseUrl = commercialBaseUrl,
			CommercialSessionId = commercialSessionId,
			TranscriptionAuth = ((ComboBoxItem)TranscriptionAuth.SelectedItem).Content.ToString() ?? "Bearer",
			AnswerAuth = ((ComboBoxItem)AnswerAuth.SelectedItem).Content.ToString() ?? "api-key",
			UseLocalSpeech = (UseLocalSpeech.IsChecked == true),
			UseMicrophone = false,
			UseBothAudio = false,
			MaxTokens = (int.TryParse(MaxTokens.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out result) ? result : 0),
			Temperature = (double.TryParse(Temperature.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out result2) ? result2 : double.NaN)
		};
	}

	private void SaveSettingsClick(object sender, RoutedEventArgs e)
	{
		try
		{
			AppSettings appSettings = ReadSettings();
			appSettings.ValidateGeneration();
			if (appSettings.AnswerUrl.Length > 0)
			{
				AppSettings.ValidateEndpoint(appSettings.AnswerUrl);
			}
			if (appSettings.TranscriptionUrl.Length > 0)
			{
				AppSettings.ValidateEndpoint(appSettings.TranscriptionUrl);
			}
			appSettings.Save();
			automaticSuppressed = false;
			liveAnswers.Reset();
			liveAnswers.Offer(transcript.Context);
			Status.Text = "Settings saved. AI connection is managed in the background.";
		}
		catch (ArgumentException ex)
		{
			Status.Text = ex.Message;
		}
		catch
		{
			Status.Text = "Could not save settings.";
		}
	}

	private async Task ToggleListeningAsync()
	{
		if (transitioning || closing)
		{
			return;
		}
		transitioning = true;
		ListenButton.IsEnabled = false;
		CaptureSettings.IsEnabled = false;
		try
		{
			if (listener != null)
			{
				await StopListeningAsync();
				Status.Text = "Listening stopped.";
				return;
			}
			AppSettings settings = ReadSettings();
			listeningCts = new CancellationTokenSource();
			var cts = listeningCts;
			if (settings.UseLocalSpeech)
			{
				Status.Text = "Preparing local Whisper transcription…";
				localSpeech = await Task.Run(() => WhisperTranscriber.CreateReadyAsync(cts.Token));
				if (closing)
				{
					await Task.Run((Action)localSpeech.Dispose);
					localSpeech = null;
					return;
				}
			}
			else
			{
				AppSettings.ValidateEndpoint(settings.TranscriptionUrl);
			}
			if (AutoAnswer.IsChecked == true)
			{
				try
				{
					ValidateAnswerSettings(settings);
					automaticSuppressed = string.IsNullOrWhiteSpace(GetAnswerKey(settings));
					AnswerStatus.Text = (automaticSuppressed ? "Background AI connection is unavailable. Transcription continues." : "Listening");
				}
				catch (ArgumentException ex)
				{
					automaticSuppressed = true;
					AnswerStatus.Text = ex.Message;
				}
			}
			var key = transcriptionKey;
			listener = new LoopbackListener(settings.DeviceId);
			listener.Error += delegate(string message)
			{
				base.Dispatcher.BeginInvoke((Action)delegate
				{
					if (!closing)
					{
						Status.Text = message;
					}
				});
			};
			listener.Start();
			callArchive.StartCapture((DeviceBox.SelectedItem as AudioDevice)?.Name ?? settings.DeviceId);
			listeningStarted = DateTimeOffset.UtcNow;
			lastAudioDetected = DateTimeOffset.MinValue;
			lastAudioBytes = 0L;
			audioTimer.Start();
			int epoch = ++sessionGeneration;
			var liveCapture = listener;
			var speechForCapture = localSpeech;
			transcriptionTask = Task.Run(() => ProcessAudioAsync(liveCapture, settings, key, cts, epoch, "Caller", speechForCapture));
			ListenButton.Content = "Stop listening";
			SettingsPanel.IsExpanded = false;
			ClickThrough.IsChecked = false;
			CaptureSettings.IsEnabled = false;
			OptionsPanel.Visibility = Visibility.Collapsed;
			AnswerStatus.Text = "Listening";
			DraftText.Text = "Listening…";
			Status.Text = "Listening · words update every ~2 seconds plus processing time.";
		}
		catch (Exception ex2)
		{
			await StopListeningAsync();
			SettingsPanel.IsExpanded = true;
			Status.Text = FriendlyError(ex2, "Could not start listening");
		}
		finally
		{
			transitioning = false;
			if (!closing)
			{
				ListenButton.IsEnabled = true;
				UpdateTokenSummary();
			}
		}
	}

	private async Task StopListeningAsync()
	{
		liveAnswers.Reset();
		var capture = listener;
		listener = null;
		callArchive.EndCapture();
		audioTimer.Stop();
		AudioMeter.Value = 0.0;
		var cts = listeningCts;
		listeningCts = null;
		var task = transcriptionTask;
		transcriptionTask = null;
		var speech = localSpeech;
		localSpeech = null;
		cts?.Cancel();
		var captureCleanup = ((capture == null) ? Task.CompletedTask : Task.Run(async delegate
		{
			await capture.DisposeAsync();
		}));
		try
		{
			_ = 1;
			try
			{
				if (task != null)
				{
					try
					{
						await task;
					}
					catch (OperationCanceledException)
					{
					}
				}
				await captureCleanup;
			}
			catch
			{
				if (!closing)
				{
					Status.Text = "Audio device did not shut down cleanly.";
				}
			}
		}
		finally
		{
			cts?.Dispose();
			if (speech != null)
			{
				await Task.Run((Action)speech.Dispose);
			}
		}
		if (!closing)
		{
			ListenButton.Content = "Start listening";
			CaptureSettings.IsEnabled = true;
			AudioStatus.Text = "Audio stopped. Caller audio uses the call speakers/headphones.";
			ClickThrough.IsChecked = false;
			UpdateTokenSummary();
			if (answerCts == null)
			{
				AnswerStatus.Text = "Ready to read";
			}
		}
		await SaveCallAsync();
	}

	private async Task ProcessAudioAsync(LoopbackListener capture, AppSettings settings, string key, CancellationTokenSource cts, int epoch, string source, WhisperTranscriber? speech)
	{
		CancellationToken ct = cts.Token;
		AudioChunk? pending = null;
		try
		{
			while (true)
			{
				bool flag = pending is not null;
				if (!flag)
				{
					flag = await capture.Reader.WaitToReadAsync(ct);
				}
				if (!flag)
				{
					break;
				}
				var audioChunk = pending;
				if (audioChunk is null)
				{
					audioChunk = await capture.Reader.ReadAsync(ct);
				}
				AudioChunk chunk = audioChunk;
				pending = null;
				AudioChunk? item;
				while (chunk.Preview && capture.Reader.TryRead(out item))
				{
					if (item.SegmentId == chunk.SegmentId)
					{
						chunk = item;
						continue;
					}
					pending = item;
					break;
				}
				string text;
				if (chunk.Pcm.Length < 6400)
				{
					text = "";
				}
				else
				{
					var text2 = ((!settings.UseLocalSpeech) ? (await endpoints.TranscribeAsync(AudioSegmenter.ToWav(chunk.Pcm), settings, key, ct)) : (await speech!.TranscribeAsync(AudioSegmenter.ToWav(chunk.Pcm), ct)));
					text = text2;
				}
				var text3 = text;
				ct.ThrowIfCancellationRequested();
				await base.Dispatcher.InvokeAsync(delegate
				{
					if (epoch == sessionGeneration && !ct.IsCancellationRequested && !closing)
					{
						transcript.Update(epoch, source, chunk.SegmentId, text3, !chunk.Preview);
						if (source == "Caller")
						{
							callArchive.UpdateCaller(epoch, chunk.SegmentId, text3, !chunk.Preview);
						}
						bool num = TranscriptBox.VerticalOffset >= TranscriptBox.ExtentHeight - TranscriptBox.ViewportHeight - 20.0;
						double verticalOffset = TranscriptBox.VerticalOffset;
						TranscriptBox.Text = transcript.Display;
						if (num)
						{
							TranscriptBox.ScrollToEnd();
						}
						else
						{
							TranscriptBox.ScrollToVerticalOffset(verticalOffset);
						}
						QuestionBox.Text = transcript.Context;
						if (text3.Length > 0)
						{
							CallerQuestionText.Text = text3;
						}
						DraftText.Text = "Live words may be revised as more speech arrives.";
						Status.Text = ((text3.Length > 0) ? "Listening · live transcript" : "Audio received · no words recognized yet");
						if (AutoAnswer.IsChecked == true && source == "Caller")
						{
							liveAnswers.Offer(transcript.Context, text3, $"{epoch}:{chunk.SegmentId}", !chunk.Preview && chunk.EndOfUtterance);
						}
						DispatchLiveAnswer();
					}
				});
			}
		}
		catch (OperationCanceledException) when (ct.IsCancellationRequested)
		{
		}
		catch (Exception ex2)
		{
			Exception ex3 = ex2;
			await base.Dispatcher.InvokeAsync(delegate
			{
				if (!closing)
				{
					Status.Text = FriendlyError(ex3, "Transcription stopped");
				}
			});
		}
		finally
		{
			if (!ct.IsCancellationRequested)
			{
				_ = base.Dispatcher.BeginInvoke((Func<Task>)async delegate
				{
					if (listeningCts == cts && !closing)
					{
						await StopListeningAsync();
					}
				});
			}
		}
	}

	private Task AnswerCurrentAsync()
	{
		if (QuestionTabs.SelectedIndex == 1)
		{
			return AnswerManualAsync();
		}
		if (answerCts != null)
		{
			answerCts.Cancel();
			return Task.CompletedTask;
		}
		TranscriptOnly.IsChecked = false;
		return GenerateAnswerAsync(QuestionBox.Text.Trim());
	}

	private static void ValidateAnswerSettings(AppSettings settings)
	{
		settings.ValidateAnswer();
	}

	private async Task GenerateAnswerAsync(string question, bool automatic = false, string displayQuestion = "", string questionId = "")
	{
		if (closing)
		{
			return;
		}
		if (string.IsNullOrWhiteSpace(question))
		{
			AnswerStatus.Text = "Waiting for transcript text.";
			return;
		}
		if (question.Length > 24000)
		{
			AnswerStatus.Text = "Question is too long. Limit it to 24,000 characters.";
			return;
		}
		AppSettings settings = ReadSettings();
		var key = GetAnswerKey(settings);
		try
		{
			ValidateAnswerSettings(settings);
			if (string.IsNullOrWhiteSpace(key) && !AppSettings.ValidateEndpoint(settings.AnswerUrl).IsLoopback)
			{
				throw new ArgumentException("Background AI connection is not configured for this endpoint.");
			}
		}
		catch (ArgumentException ex)
		{
			AnswerStatus.Text = ex.Message;
			automaticSuppressed = automatic;
			return;
		}
		answerCts?.Cancel();
		if (!automatic)
		{
			PinAnswer.IsChecked = false;
			AnswerHistory.SelectedIndex = 0;
		}
		AnswerHistory.IsEnabled = false;
		int current = ++generation;
		var cts = new CancellationTokenSource();
		try
		{
			answerCts = cts;
			TaskCompletionSource idle = (answerIdle = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
			CallArchive archive = callArchive;
			CallAnswer recordedAnswer = archive.StartAnswer((displayQuestion.Length > 0) ? displayQuestion : CallerQuestionText.Text, question, settings, questionId);
			UpdateTokenSummary();
			AnswerStreamBuffer stream = new AnswerStreamBuffer((displayQuestion.Length > 0) ? displayQuestion : CallerQuestionText.Text, current);
			activeStream = stream;
			AnswerButton.Content = "Cancel answer";
			ManualAnswerButton.Content = "Cancel answer";
			AnswerStatus.Text = "Writing";
			try
			{
				await Task.Run(async delegate
				{
					await endpoints.AnswerToCompletionAsync(question, settings, key, delegate(string token)
					{
						recordedAnswer.Append(token);
						stream.Append(token);
					}, cts.Token, delegate(TokenUsage usage)
					{
						recordedAnswer.Usage = usage;
						base.Dispatcher.BeginInvoke((Action)delegate
						{
							if (!closing && callArchive == archive)
							{
								UpdateTokenSummary();
							}
						}, DispatcherPriority.Background);
					}, delegate(string model, string finish)
					{
						if (model.Length > 0)
						{
							recordedAnswer.ResolvedModel = model;
						}
						if (finish.Length > 0)
						{
							recordedAnswer.FinishReason = finish;
						}
					}, delegate(bool value)
					{
						recordedAnswer.UsageIncomplete = value;
					});
				});
				DrainAnswerStream();
				recordedAnswer.Finish((recordedAnswer.FinishReason == "length") ? "Incomplete · continuation limit reached" : "Completed");
				if (current == generation && !closing)
				{
					AnswerStatus.Text = ((recordedAnswer.FinishReason == "length") ? "Answer incomplete · automatic continuation limit reached" : "Ready to read");
				}
			}
			catch (OperationCanceledException)
			{
				recordedAnswer.Finish("Canceled · partial answer");
				if (current == generation && !closing)
				{
					AnswerStatus.Text = "Answer canceled.";
				}
			}
			catch (Exception ex3)
			{
				recordedAnswer.Finish(FriendlyError(ex3, "Answer failed"));
				if (current == generation && !closing)
				{
					AnswerStatus.Text = FriendlyError(ex3, "Answer failed");
					if (automatic)
					{
						bool flag = ((ex3 is HttpRequestException { StatusCode: { } statusCode } && ((uint)(statusCode - 400) <= 4u || statusCode == HttpStatusCode.UnprocessableEntity)) ? true : false);
						automaticSuppressed = flag;
						AnswerStatus.Text += (automaticSuppressed ? " Automatic AI paused; check your account or service configuration." : " Listening continues; the next question will be answered automatically.");
					}
				}
			}
			finally
			{
				if (activeStream == stream)
				{
					DrainAnswerStream();
					activeStream = null;
					streamingParagraph = null;
					answerDirty = true;
					RefreshAnswerPresentation();
				}
				if (answerCts == cts)
				{
					answerCts = null;
					AnswerButton.Content = "Answer now";
					ManualAnswerButton.Content = "Get answer";
				}
				if (!closing)
				{
					UpdateTokenSummary();
					if (commercialMode)
					{
						_ = RefreshCommercialAccountAsync();
					}
				}
				if (!closing && callArchive == archive && current == generation)
				{
					RecordedAnswer recordedAnswer2 = recordedAnswer.Snapshot();
					AnswerHistory.Items.Insert(1, new ComboBoxItem
					{
						Content = ((recordedAnswer2.Question.Length > 75) ? (recordedAnswer2.Question.Substring(0, 75) + "…") : recordedAnswer2.Question),
						Tag = recordedAnswer2
					});
					while (AnswerHistory.Items.Count > 21)
					{
						AnswerHistory.Items.RemoveAt(AnswerHistory.Items.Count - 1);
					}
					AnswerHistory.SelectedIndex = 0;
					AnswerHistory.IsEnabled = true;
				}
				idle.TrySetResult();
				if (listener == null && !closing)
				{
					await SaveCallAsync();
				}
			}
		}
		finally
		{
			if (cts != null)
			{
				((IDisposable)cts).Dispose();
			}
		}
	}

	private static string FriendlyError(Exception ex, string prefix)
	{
		if (!(ex is ArgumentException) && !(ex is InvalidDataException) && !(ex is InvalidOperationException))
		{
			if (ex is HttpRequestException { StatusCode: var statusCode })
			{
				if (statusCode.HasValue)
				{
					HttpStatusCode valueOrDefault = statusCode.GetValueOrDefault();
					return valueOrDefault switch
					{
						HttpStatusCode.Unauthorized => "AI authentication failed (401). The background connection needs updating.", 
						HttpStatusCode.PaymentRequired => "Not enough available tokens. Open Account and top up on the website.", 
						_ => $"{prefix}: HTTP {valueOrDefault}. Check URL, credentials, and model.", 
					};
				}
				return prefix + ": endpoint request failed. Check URL, credentials, model, and connectivity.";
			}
			if (!(ex is OperationCanceledException))
			{
				if (ex is COMException)
				{
					return $"{prefix}: audio/speech device error 0x{ex.HResult:X8}. Check device and Windows speech configuration.";
				}
				return prefix + ". Check settings and restart the session.";
			}
			return prefix + ": request timed out.";
		}
		return ex.Message;
	}

	protected override async void OnClosing(CancelEventArgs e)
	{
		if (!closing)
		{
			e.Cancel = true;
			closing = true;
			generation++;
			sessionGeneration++;
			saveTimer.Stop();
			audioTimer.Stop();
			answerTimer.Stop();
			presentationTimer.Stop();
			liveAnswers.Reset();
			answerCts?.Cancel();
			await ShutdownCoordinator.CompleteWithinAsync(StopListeningAsync(), TimeSpan.FromMilliseconds(500L, 0L));
			var taskCompletionSource = answerIdle;
			if (taskCompletionSource != null)
			{
				await ShutdownCoordinator.CompleteWithinAsync(taskCompletionSource.Task, TimeSpan.FromSeconds(1L));
			}
			if (!(await SaveCallAsync()) && MessageBox.Show(this, "The latest call could not be saved. Exit and discard the unsaved changes?", "Unsaved call", MessageBoxButton.YesNo, MessageBoxImage.Exclamation) != MessageBoxResult.Yes)
			{
				closing = false;
				saveTimer.Start();
				answerTimer.Start();
				presentationTimer.Start();
				return;
			}
			Hide();
			updateTimer.Stop();
			updateLifetime.Cancel();
			updates.Dispose();
			for (int i = 1; i <= 5; i++)
			{
				WindowPrivacy.UnregisterHotKey(hwnd, i);
			}
			endpoints.Dispose();
			accountClient.Dispose();
			Close();
		}
		base.OnClosing(e);
	}

}
