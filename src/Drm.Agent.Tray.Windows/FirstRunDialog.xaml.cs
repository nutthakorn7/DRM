using System.Net.Http;
using System.Windows;
using System.Windows.Input;
using Drm.Agent.Core;

namespace Drm.Agent.Tray.Windows;

/// <summary>
/// Modal dialog shown on first launch of the tray. Asks for the
/// user's work email, calls /api/agent/discover, and surfaces an
/// AgentIdentityCacheEntry via the <see cref="Result"/> property when
/// the dialog closes with DialogResult=true.
///
/// Behaviour contract:
///   - DialogResult=true  → Result is non-null and ready to be saved.
///   - DialogResult=false → user clicked "Skip for now"; tray falls
///                          back to the manual textbox flow.
///   - DialogResult=null  → user closed via the title-bar X. Treated
///                          the same as Skip.
/// </summary>
public partial class FirstRunDialog : Window
{
    private readonly Func<string, CancellationToken, Task<AgentDiscoveryResult?>> discoverFunc;
    private readonly Uri serverUrl;

    public AgentIdentityCacheEntry? Result { get; private set; }

    /// <summary>
    /// Production constructor: builds its own HttpClient against the
    /// configured server URL and calls /api/agent/discover via
    /// DrmServerClient.
    /// </summary>
    public FirstRunDialog(Uri serverUrl) : this(
        serverUrl,
        async (email, ct) =>
        {
            using var http = new HttpClient { BaseAddress = serverUrl };
            var client = new DrmServerClient(http);
            return await client.DiscoverAsync(email, ct);
        })
    {
    }

    /// <summary>
    /// Test seam: lets unit tests inject a fake discover function
    /// without spinning up a real HttpClient or web server.
    /// </summary>
    public FirstRunDialog(
        Uri serverUrl,
        Func<string, CancellationToken, Task<AgentDiscoveryResult?>> discoverFunc)
    {
        InitializeComponent();
        this.serverUrl = serverUrl ?? throw new ArgumentNullException(nameof(serverUrl));
        this.discoverFunc = discoverFunc ?? throw new ArgumentNullException(nameof(discoverFunc));
        EmailBox.Focus();
    }

    private void EmailBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            _ = TrySignInAsync();
        }
    }

    private async void SignInButton_Click(object sender, RoutedEventArgs e)
    {
        await TrySignInAsync();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private async Task TrySignInAsync()
    {
        var email = EmailBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@'))
        {
            ShowStatus("Please enter a valid work email (must include '@').", isError: true);
            return;
        }

        // Lock the buttons while in-flight so a double-click doesn't
        // fire two concurrent /api/agent/discover calls (which would
        // both succeed but is wasteful and racy).
        SignInButton.IsEnabled = false;
        CancelButton.IsEnabled = false;
        ShowStatus($"Connecting to {serverUrl.Host}…", isError: false);

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var discoveryResult = await discoverFunc(email, cts.Token);

            if (discoveryResult is null)
            {
                // 404 — server has no record of this email. Common UX
                // mistake (typo, wrong domain), so the message says
                // "check the email" rather than implying the server is
                // broken.
                ShowStatus(
                    $"We couldn't find {email} on {serverUrl.Host}. " +
                    "Check the spelling or ask your admin to add you.",
                    isError: true);
                return;
            }

            Result = new AgentIdentityCacheEntry(
                TenantId: discoveryResult.TenantId,
                UserId: discoveryResult.UserId,
                Email: discoveryResult.Email,
                DisplayName: discoveryResult.DisplayName,
                ServerUrl: serverUrl,
                DefaultPolicyTemplateId: discoveryResult.DefaultPolicyTemplateId,
                DefaultExpiryDays: discoveryResult.DefaultExpiryDays,
                SavedAtUtc: DateTimeOffset.UtcNow);

            DialogResult = true;
            Close();
        }
        catch (OperationCanceledException)
        {
            ShowStatus(
                $"Connection to {serverUrl.Host} timed out. Check your network and try again.",
                isError: true);
        }
        catch (HttpRequestException exception)
        {
            ShowStatus(
                $"Couldn't reach {serverUrl.Host}: {exception.Message}",
                isError: true);
        }
        catch (Exception exception)
        {
            // Defensive catch-all so an unexpected server response
            // (malformed JSON, etc.) doesn't crash the tray's
            // first-launch — the user can still click Skip.
            ShowStatus($"Unexpected error: {exception.Message}", isError: true);
        }
        finally
        {
            SignInButton.IsEnabled = true;
            CancelButton.IsEnabled = true;
        }
    }

    private void ShowStatus(string message, bool isError)
    {
        StatusBox.Visibility = Visibility.Visible;
        StatusText.Text = message;
        if (isError)
        {
            StatusBox.Background = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0xFE, 0xE2, 0xE2));
            StatusBox.BorderBrush = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0xEF, 0x44, 0x44));
            StatusText.Foreground = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0x99, 0x1B, 0x1B));
        }
        else
        {
            StatusBox.Background = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0xDB, 0xEA, 0xFE));
            StatusBox.BorderBrush = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0x60, 0xA5, 0xFA));
            StatusText.Foreground = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0x1E, 0x3A, 0x8A));
        }
    }
}
