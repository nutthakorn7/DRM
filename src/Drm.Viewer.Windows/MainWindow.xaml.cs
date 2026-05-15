using System.IO;
using System.Net.Http;
using System.Windows;
using Drm.Agent.Core;
using Drm.Domain;
using Microsoft.Win32;

namespace Drm.Viewer.Windows;

public partial class MainWindow : Window
{
    private string? temporaryPdfPath;

    public MainWindow()
    {
        InitializeComponent();
        PermissionText.Text = "Permissions: not loaded";
        WatermarkText.Text = "DRM Protected";
        StatusText.Text = "No document loaded.";
    }

    private void BrowseButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "DRM protected files (*.drmx)|*.drmx",
            CheckFileExists = true,
            Multiselect = false,
            Title = "Select protected file"
        };

        if (dialog.ShowDialog(this) == true)
        {
            ProtectedPathBox.Text = dialog.FileName;
        }
    }

    private async void OpenButton_Click(object sender, RoutedEventArgs e)
    {
        OpenButton.IsEnabled = false;
        StatusText.Text = "Opening protected file...";

        try
        {
            var serverUrl = ParseServerUrl();
            var userId = new UserId(ParseRequiredGuid(UserIdBox.Text, "User ID"));
            var deviceId = new DeviceId(ParseRequiredGuid(DeviceIdBox.Text, "Device ID"));
            var protectedPath = ProtectedPathBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(protectedPath))
            {
                throw new InvalidOperationException("Select a protected file before opening.");
            }

            var clientApiKey = ClientApiKeyBox.Password.Trim();
            using var httpClient = new HttpClient { BaseAddress = serverUrl };
            var serverClient = new DrmServerClient(httpClient, clientApiKey);
            var keyStore = new JsonFileKeyStore(ResolveDataPath("file-keys.json"));
            var decisionCache = new JsonPolicyDecisionCache(ResolveDataPath("policy-decisions.json"));
            var opened = await new OpenProtectedPdfFileWorkflow(serverClient, keyStore, decisionCache)
                .OpenAsync(protectedPath, userId, deviceId, CancellationToken.None);

            var tempPath = Path.Combine(Path.GetTempPath(), $"drm-viewer-{Guid.NewGuid():N}.pdf");
            await File.WriteAllBytesAsync(tempPath, opened.Content);
            LoadPdfFromTemporaryFile(tempPath, opened.Watermark, opened.Permissions.ToString());
        }
        catch (Exception exception)
        {
            StatusText.Text = $"Failed: {exception.Message}";
        }
        finally
        {
            OpenButton.IsEnabled = true;
        }
    }

    public void LoadPdfFromTemporaryFile(string path, string watermark, string permissions)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Temporary PDF file was not found.", path);
        }

        DeleteTemporaryPdf();
        temporaryPdfPath = path;
        PermissionText.Text = permissions;
        WatermarkText.Text = watermark;
        StatusText.Text = $"Loaded protected PDF: {Path.GetFileName(path)}";
        PdfHost.Navigate(path);
    }

    protected override void OnClosed(EventArgs e)
    {
        DeleteTemporaryPdf();
        base.OnClosed(e);
    }

    private Uri ParseServerUrl()
    {
        if (!Uri.TryCreate(ServerUrlBox.Text.Trim(), UriKind.Absolute, out var uri))
        {
            throw new InvalidOperationException("Server URL must be an absolute URL.");
        }

        return uri;
    }

    private static Guid ParseRequiredGuid(string value, string fieldName)
    {
        if (!Guid.TryParse(value.Trim(), out var parsed) || parsed == Guid.Empty)
        {
            throw new InvalidOperationException($"{fieldName} must be a non-empty GUID.");
        }

        return parsed;
    }

    private static string ResolveDataPath(string fileName)
    {
        var commonApplicationData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        if (string.IsNullOrWhiteSpace(commonApplicationData))
        {
            commonApplicationData = Path.Combine(AppContext.BaseDirectory, "data");
        }

        return Path.Combine(commonApplicationData, "DRM", fileName);
    }

    private void DeleteTemporaryPdf()
    {
        if (temporaryPdfPath is null || !File.Exists(temporaryPdfPath))
        {
            return;
        }

        try
        {
            File.Delete(temporaryPdfPath);
        }
        catch (IOException)
        {
            // The embedded browser can keep the file open briefly; cleanup is best-effort.
        }
        finally
        {
            temporaryPdfPath = null;
        }
    }
}
