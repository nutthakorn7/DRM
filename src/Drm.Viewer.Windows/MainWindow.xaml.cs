using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Input;
using Drm.Agent.Core;
using Drm.Domain;
using Microsoft.Win32;

namespace Drm.Viewer.Windows;

public partial class MainWindow : Window
{
    private string? temporaryPdfPath;
    private byte[]? currentContent;
    private Permission currentPermissions = Permission.None;

    public MainWindow()
    {
        InitializeComponent();
        PreviewKeyDown += MainWindow_PreviewKeyDown;
        WatermarkText.Text = "DRM Protected";
        StatusText.Text = "No document loaded.";
        ApplyPermissionState();
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
            LoadPdfFromTemporaryFile(tempPath, opened.Watermark, opened.Permissions);
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

    public void LoadPdfFromTemporaryFile(string path, string watermark, Permission permissions)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Temporary PDF file was not found.", path);
        }

        DeleteTemporaryPdf();
        temporaryPdfPath = path;
        currentContent = File.ReadAllBytes(path);
        currentPermissions = permissions;
        ApplyPermissionState();
        WatermarkText.Text = watermark;
        StatusText.Text = $"Loaded protected PDF: {Path.GetFileName(path)}";
        PdfHost.Navigate(path);
    }

    private void CopyButton_Click(object sender, RoutedEventArgs e)
    {
        if (!RequireViewerAction(ViewerControlledAction.Copy, "Copy"))
        {
            return;
        }

        StatusText.Text = "Copy is allowed by policy; use text selection in the embedded PDF renderer when available.";
    }

    private void PrintButton_Click(object sender, RoutedEventArgs e)
    {
        if (!RequireViewerAction(ViewerControlledAction.Print, "Print"))
        {
            return;
        }

        try
        {
            PdfHost.InvokeScript("print");
            StatusText.Text = "Print requested.";
        }
        catch (Exception exception)
        {
            StatusText.Text = $"Print unavailable: {exception.Message}";
        }
    }

    private async void ExportButton_Click(object sender, RoutedEventArgs e)
    {
        if (!RequireViewerAction(ViewerControlledAction.ExportOriginal, "Export"))
        {
            return;
        }

        var baseName = Path.GetFileNameWithoutExtension(ProtectedPathBox.Text.Trim());
        if (string.IsNullOrWhiteSpace(baseName))
        {
            baseName = "protected";
        }

        var dialog = new SaveFileDialog
        {
            AddExtension = true,
            DefaultExt = ".pdf",
            FileName = $"{baseName}.pdf",
            Filter = "PDF files (*.pdf)|*.pdf",
            OverwritePrompt = true,
            Title = "Export original PDF"
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        await File.WriteAllBytesAsync(dialog.FileName, currentContent!);
        StatusText.Text = $"Exported original PDF: {dialog.FileName}";
    }

    private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) != ModifierKeys.Control)
        {
            return;
        }

        if (e.Key == Key.P)
        {
            e.Handled = true;
            PrintButton_Click(sender, new RoutedEventArgs());
            return;
        }

        if (e.Key == Key.S)
        {
            e.Handled = true;
            ExportButton_Click(sender, new RoutedEventArgs());
            return;
        }

        if (e.Key == Key.C && !ViewerPermissionState.From(currentPermissions).CanCopy)
        {
            e.Handled = true;
            StatusText.Text = "Copy is blocked by policy.";
        }
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
            currentContent = null;
            currentPermissions = Permission.None;
            ApplyPermissionState();
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
            currentContent = null;
            currentPermissions = Permission.None;
            ApplyPermissionState();
        }
    }

    private bool RequireViewerAction(ViewerControlledAction action, string label)
    {
        if (temporaryPdfPath is null || currentContent is null)
        {
            StatusText.Text = "No document loaded.";
            return false;
        }

        if (!ViewerPermissionState.From(currentPermissions).Allows(action))
        {
            StatusText.Text = $"{label} is blocked by policy.";
            return false;
        }

        return true;
    }

    private void ApplyPermissionState()
    {
        var state = ViewerPermissionState.From(currentPermissions);
        var hasDocument = temporaryPdfPath is not null && currentContent is not null;

        CopyButton.IsEnabled = hasDocument && state.CanCopy;
        PrintButton.IsEnabled = hasDocument && state.CanPrint;
        ExportButton.IsEnabled = hasDocument && state.CanExportOriginal;
        PermissionText.Text = hasDocument
            ? $"Permissions: {currentPermissions}"
            : "Permissions: not loaded";
    }
}
