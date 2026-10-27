using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading;
using System.Windows;
using OverlayCopilot.Services;

namespace OverlayCopilot;
public partial class AccountWindow : Window
{
    private readonly CommercialAccountStore store;
    private readonly CommercialAccountClient client = new();
    private readonly CancellationTokenSource lifetime = new();
    private string challenge = "", challengeUrl = "";
    public string BaseUrl { get; private set; }
    public bool Changed { get; private set; }
    public AccountWindow(string baseUrl, bool fixedUrl, CommercialAccountStore store)
    {
        InitializeComponent(); this.store = store; BaseUrl = baseUrl; ServerUrl.Text = baseUrl; ServerUrl.IsReadOnly = fixedUrl; ServicePanel.Visibility = fixedUrl ? Visibility.Collapsed : Visibility.Visible;
        var session = store.Load(baseUrl); EmailBox.Text = session?.Email ?? ""; SignOutButton.IsEnabled = session is not null;
        if (session is not null) AccountStatus.Text = "Signed in as " + session.Email + ".";
        else if (Uri.TryCreate(baseUrl,UriKind.Absolute,out var service) && service.IsLoopback) AccountStatus.Text = "Local preview · the backend on this PC must be running. Real email requests use its configured mail provider.";
    }
    private async void SendCodeClick(object sender, RoutedEventArgs e)
    {
        if (AcceptTerms.IsChecked != true) { AccountStatus.Text = "Read the website terms and privacy notice, then tick the agreement box."; return; }
        SendCodeButton.IsEnabled = SignInButton.IsEnabled = false;
        try
        {
            challengeUrl = ServerUrl.Text.Trim().TrimEnd('/'); AppSettings.ValidateEndpoint(challengeUrl);
            challenge = "";
            var result = await client.StartDetailedAsync(challengeUrl, EmailBox.Text.Trim(), lifetime.Token);
            challenge = result.Challenge; CodePanel.Visibility = SignInButton.Visibility = Visibility.Visible; CodeBox.Focus();
            AccountStatus.Text = result.Message;
        }
        catch (Exception ex) { if (!lifetime.IsCancellationRequested) AccountStatus.Text = ex is InvalidOperationException or ArgumentException ? ex.Message : "Could not reach the account API at " + challengeUrl + ". Confirm the backend is deployed. For localhost testing, use the Local Preview installer."; }
        finally { if (!lifetime.IsCancellationRequested) SendCodeButton.IsEnabled = SignInButton.IsEnabled = true; }
    }
    private async void SignInClick(object sender, RoutedEventArgs e)
    {
        if (challenge.Length == 0 || AcceptTerms.IsChecked != true || ServerUrl.Text.Trim().TrimEnd('/') != challengeUrl) { AccountStatus.Text = "Accept the terms and request a code for this website first."; return; }
        SignInButton.IsEnabled = false;
        try { var session = await client.VerifyAsync(challengeUrl, challenge, CodeBox.Text.Trim(), lifetime.Token); store.Save(session); BaseUrl = session.BaseUrl; Changed = true; Close(); }
        catch (Exception ex) { if (!lifetime.IsCancellationRequested) AccountStatus.Text = ex is InvalidOperationException ? ex.Message : "Sign-in failed. Check the code and connection."; }
        finally { if (!lifetime.IsCancellationRequested) SignInButton.IsEnabled = true; }
    }
    private async void SignOutClick(object sender, RoutedEventArgs e)
    {
        var session = store.Load(BaseUrl); SignOutButton.IsEnabled = false;
        try { if (session is not null) await client.LogoutAsync(session, lifetime.Token); }
        catch { AccountStatus.Text = "Server sign-out was unavailable. Local sign-in has been cleared."; }
        finally { store.Clear(); Changed = true; if (!lifetime.IsCancellationRequested) Close(); }
    }
    private void WebsiteClick(object sender, RoutedEventArgs e)
    {
        try { var url = ServerUrl.Text.Trim().TrimEnd('/'); AppSettings.ValidateEndpoint(url); Process.Start(new ProcessStartInfo(url + "/dashboard") { UseShellExecute = true }); }
        catch { AccountStatus.Text = "Enter a valid service website first."; }
    }
    protected override void OnClosing(CancelEventArgs e) { lifetime.Cancel(); client.Dispose(); base.OnClosing(e); }
}
