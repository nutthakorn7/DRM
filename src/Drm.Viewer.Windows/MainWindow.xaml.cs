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
    private const string PdfContentType = "application/pdf";

    private static readonly IReadOnlyDictionary<string, string> ExportExtensions =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [PdfContentType] = ".pdf",
            ["application/vnd.openxmlformats-officedocument.wordprocessingml.document"] = ".docx",
            ["application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"] = ".xlsx",
            ["application/vnd.openxmlformats-officedocument.presentationml.presentation"] = ".pptx",
            ["application/zip"] = ".zip",
            ["text/plain"] = ".txt",
            ["text/csv"] = ".csv"
        };

    private string? temporaryPdfPath;
    private byte[]? currentContent;
    private string currentContentType = string.Empty;
    private Permission currentPermissions = Permission.None;
    private AgentIdentity? currentIdentity;
    private Guid? currentFileId;
    private Uri? currentServerUrl;
    private string currentClientApiKey = string.Empty;

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
            var opened = await new OpenProtectedFileWorkflow(serverClient, keyStore, decisionCache)
                .OpenAsync(protectedPath, userId, deviceId, CancellationToken.None);

            if (string.Equals(opened.ContentType, PdfContentType, StringComparison.OrdinalIgnoreCase))
            {
                var tempPath = Path.Combine(Path.GetTempPath(), $"drm-viewer-{Guid.NewGuid():N}.pdf");
                await File.WriteAllBytesAsync(tempPath, opened.Content);
                LoadPdfFromTemporaryFile(tempPath, opened.Watermark, opened.Permissions);
            }
            else
            {
                LoadGenericProtectedFile(opened.Content, opened.ContentType, opened.Watermark, opened.Permissions);
            }

            currentIdentity = new AgentIdentity(opened.TenantId, userId.Value, deviceId.Value);
            currentFileId = opened.FileId;
            currentServerUrl = serverUrl;
            currentClientApiKey = clientApiKey;
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
        currentContentType = PdfContentType;
        currentPermissions = permissions;
        ApplyPermissionState();
        WatermarkText.Text = watermark;
        StatusText.Text = $"Loaded protected PDF file: {Path.GetFileName(path)}";
        PdfHost.Navigate(path);
    }

    private void LoadGenericProtectedFile(byte[] content, string contentType, string watermark, Permission permissions)
    {
        DeleteTemporaryPdf();
        temporaryPdfPath = null;
        currentContent = content.ToArray();
        currentContentType = contentType;
        currentPermissions = permissions;
        ApplyPermissionState();
        WatermarkText.Text = watermark;
        StatusText.Text = $"Loaded protected file: {contentType}";
        PdfHost.Navigate("about:blank");
    }

    private async void CopyButton_Click(object sender, RoutedEventArgs e)
    {
        if (!await RequireViewerActionAsync(ViewerControlledAction.Copy, "Copy"))
        {
            return;
        }

        await AuditViewerActionAsync(ViewerControlledAction.Copy, allowed: true);
        StatusText.Text = "Copy is allowed by policy; use text selection in the embedded PDF renderer when available.";
    }

    private async void PrintButton_Click(object sender, RoutedEventArgs e)
    {
        if (!await RequireViewerActionAsync(ViewerControlledAction.Print, "Print"))
        {
            return;
        }

        try
        {
            PdfHost.InvokeScript("print");
            await AuditViewerActionAsync(ViewerControlledAction.Print, allowed: true);
            StatusText.Text = "Print requested.";
        }
        catch (Exception exception)
        {
            StatusText.Text = $"Print unavailable: {exception.Message}";
        }
    }

    private async void ExportButton_Click(object sender, RoutedEventArgs e)
    {
        if (!await RequireViewerActionAsync(ViewerControlledAction.ExportOriginal, "Export"))
        {
            return;
        }

        var baseName = Path.GetFileNameWithoutExtension(ProtectedPathBox.Text.Trim());
        if (string.IsNullOrWhiteSpace(baseName))
        {
            baseName = "protected";
        }

        var exportExtension = GetExportExtension(currentContentType);
        var dialog = new SaveFileDialog
        {
            AddExtension = true,
            DefaultExt = exportExtension,
            FileName = BuildExportFileName(baseName, exportExtension),
            Filter = BuildExportFilter(currentContentType, exportExtension),
            OverwritePrompt = true,
            Title = "Export original file"
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        await File.WriteAllBytesAsync(dialog.FileName, currentContent!);
        await AuditViewerActionAsync(ViewerControlledAction.ExportOriginal, allowed: true);
        StatusText.Text = $"Exported original file: {dialog.FileName}";
    }

    private async void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
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
            await RequireViewerActionAsync(ViewerControlledAction.Copy, "Copy");
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
            currentContentType = string.Empty;
            currentPermissions = Permission.None;
            currentIdentity = null;
            currentFileId = null;
            currentServerUrl = null;
            currentClientApiKey = string.Empty;
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
            currentContentType = string.Empty;
            currentPermissions = Permission.None;
            currentIdentity = null;
            currentFileId = null;
            currentServerUrl = null;
            currentClientApiKey = string.Empty;
            ApplyPermissionState();
        }
    }

    private async Task<bool> RequireViewerActionAsync(ViewerControlledAction action, string label)
    {
        if (currentContent is null)
        {
            StatusText.Text = "No document loaded.";
            return false;
        }

        if ((action == ViewerControlledAction.Copy || action == ViewerControlledAction.Print) && !CurrentFileIsPdf())
        {
            StatusText.Text = $"{label} is unavailable for this file type.";
            return false;
        }

        if (!ViewerPermissionState.From(currentPermissions).Allows(action))
        {
            StatusText.Text = $"{label} is blocked by policy.";
            await AuditViewerActionAsync(action, allowed: false);
            return false;
        }

        return true;
    }

    private async Task AuditViewerActionAsync(ViewerControlledAction action, bool allowed)
    {
        if (currentIdentity is null || currentFileId is null || currentServerUrl is null)
        {
            return;
        }

        try
        {
            using var httpClient = new HttpClient { BaseAddress = currentServerUrl };
            var serverClient = new DrmServerClient(httpClient, currentClientApiKey);
            var record = ViewerActionAudit.Create(
                currentIdentity,
                currentFileId.Value,
                action,
                allowed,
                DateTimeOffset.UtcNow);

            await serverClient.UploadAuditAsync(record, CancellationToken.None);
        }
        catch (Exception exception)
        {
            StatusText.Text = $"{StatusText.Text} Audit upload failed: {exception.Message}";
        }
    }

    private void ApplyPermissionState()
    {
        var state = ViewerPermissionState.From(currentPermissions);
        var hasDocument = currentContent is not null;
        var canRenderInline = hasDocument && CurrentFileIsPdf();

        CopyButton.IsEnabled = canRenderInline && state.CanCopy;
        PrintButton.IsEnabled = canRenderInline && state.CanPrint;
        ExportButton.IsEnabled = hasDocument && state.CanExportOriginal;
        PermissionText.Text = hasDocument
            ? $"Permissions: {currentPermissions}"
            : "Permissions: not loaded";
    }

    private bool CurrentFileIsPdf()
    {
        return string.Equals(currentContentType, PdfContentType, StringComparison.OrdinalIgnoreCase);
    }

    private static string GetExportExtension(string contentType)
    {
        return ExportExtensions.TryGetValue(contentType, out var extension)
            ? extension
            : ".bin";
    }

    private static string BuildExportFileName(string baseName, string extension)
    {
        if (string.IsNullOrWhiteSpace(baseName))
        {
            return $"protected{extension}";
        }

        return baseName.EndsWith(extension, StringComparison.OrdinalIgnoreCase)
            ? baseName
            : $"{baseName}{extension}";
    }

    private static string BuildExportFilter(string contentType, string extension)
    {
        var label = string.Equals(contentType, PdfContentType, StringComparison.OrdinalIgnoreCase)
            ? "PDF files"
            : "Original files";

        return $"{label} (*{extension})|*{extension}|All files (*.*)|*.*";
    }
}
