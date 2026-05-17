using System.Collections.ObjectModel;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
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
    private string currentWatermarkBase = "DRM Protected";
    private readonly ObservableCollection<string> watermarkTiles = new();
    private readonly DispatcherTimer watermarkRefreshTimer;
    private readonly Random watermarkJitterRng = new();
    private const int WatermarkTileCount = 16;

    public MainWindow()
    {
        InitializeComponent();
        PreviewKeyDown += MainWindow_PreviewKeyDown;
        WatermarkTileHost.ItemsSource = watermarkTiles;
        WatermarkText.Text = currentWatermarkBase;
        watermarkRefreshTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        watermarkRefreshTimer.Tick += (_, _) => RefreshWatermarkTiles();
        watermarkRefreshTimer.Start();
        RefreshWatermarkTiles();
        StatusText.Text = "No document loaded.";
        PrefillProtectedPathFromCommandLine();
        ApplyPermissionState();
    }

    private void RefreshWatermarkTiles()
    {
        var nowUtc = DateTimeOffset.UtcNow;
        var stamp = nowUtc.ToString("yyyy-MM-dd HH:mm:ss 'UTC'");
        var label = string.IsNullOrWhiteSpace(currentWatermarkBase) ? "DRM Protected" : currentWatermarkBase;
        var tileText = $"{label}\n{stamp}";

        if (watermarkTiles.Count != WatermarkTileCount)
        {
            watermarkTiles.Clear();
            for (var i = 0; i < WatermarkTileCount; i++)
            {
                watermarkTiles.Add(tileText);
            }
        }
        else
        {
            for (var i = 0; i < WatermarkTileCount; i++)
            {
                watermarkTiles[i] = tileText;
            }
        }

        var jitterX = (watermarkJitterRng.NextDouble() - 0.5) * 12.0;
        var jitterY = (watermarkJitterRng.NextDouble() - 0.5) * 12.0;
        WatermarkOffset.X = jitterX;
        WatermarkOffset.Y = jitterY;
    }

    private void Window_DragEnter(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            return;
        }

        if (e.Data.GetData(DataFormats.FileDrop) is not string[] paths || paths.Length == 0)
        {
            return;
        }

        var firstDrmx = paths.FirstOrDefault(path =>
            File.Exists(path) && path.EndsWith(".drmx", StringComparison.OrdinalIgnoreCase));

        if (firstDrmx is null)
        {
            StatusText.Text = "Drop a .drmx protected file to open.";
            return;
        }

        ProtectedPathBox.Text = firstDrmx;
        StatusText.Text = $"Ready to open: {Path.GetFileName(firstDrmx)}";
        e.Handled = true;
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
        SetWatermarkBase(watermark);
        StatusText.Text = $"Loaded protected PDF file: {Path.GetFileName(path)}";
        PdfHost.Navigate(path);
    }

    private void SetWatermarkBase(string watermark)
    {
        currentWatermarkBase = string.IsNullOrWhiteSpace(watermark) ? "DRM Protected" : watermark;
        WatermarkText.Text = currentWatermarkBase;
        RefreshWatermarkTiles();
    }

    private void LoadGenericProtectedFile(byte[] content, string contentType, string watermark, Permission permissions)
    {
        DeleteTemporaryPdf();
        temporaryPdfPath = null;
        currentContent = content.ToArray();
        currentContentType = contentType;
        currentPermissions = permissions;
        ApplyPermissionState();
        SetWatermarkBase(watermark);
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
            if (PrintWmEnabledBox.IsChecked == true && !string.IsNullOrWhiteSpace(PrintWmPatternBox.Text)
                && CurrentFileIsPdf() && currentContent is not null)
            {
                var pattern = PrintWmPatternBox.Text.Trim();
                var resolved = PrintWatermarkComposer.ResolveTokens(
                    pattern,
                    currentIdentity?.UserId,
                    currentFileId);
                var stamped = PrintWatermarkComposer.Stamp(
                    currentContent,
                    new PrintWatermarkOptions(resolved, 33, "diagonal"));
                var stampedPath = Path.Combine(Path.GetTempPath(), $"drm-print-{Guid.NewGuid():N}.pdf");
                await File.WriteAllBytesAsync(stampedPath, stamped);
                PdfHost.Navigate(stampedPath);
                StatusText.Text = $"Print watermark applied ({resolved.Length} chars). Triggering print…";
                await Task.Delay(800);
            }

            PdfHost.InvokeScript("print");
            await AuditViewerActionAsync(ViewerControlledAction.Print, allowed: true);
            if (StatusText.Text.StartsWith("Print watermark", StringComparison.Ordinal))
            {
                StatusText.Text = $"{StatusText.Text} Print requested.";
            }
            else
            {
                StatusText.Text = "Print requested.";
            }
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
        watermarkRefreshTimer.Stop();
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
        if (hasDocument)
        {
            var extras = new List<string>();
            if ((currentPermissions & Permission.RunMacros) != 0) extras.Add("Macros");
            if ((currentPermissions & Permission.TransferOwnership) != 0) extras.Add("Transfer");
            var extrasText = extras.Count > 0 ? $" • {string.Join(" • ", extras)}" : string.Empty;
            PermissionText.Text = $"Permissions: {currentPermissions}{extrasText}";
        }
        else
        {
            PermissionText.Text = "Permissions: not loaded";
        }
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

    private void PrefillProtectedPathFromCommandLine()
    {
        var protectedPath = TryGetProtectedPathFromCommandLine(Environment.GetCommandLineArgs());
        if (!string.IsNullOrWhiteSpace(protectedPath))
        {
            ProtectedPathBox.Text = protectedPath;
        }
    }

    private static string? TryGetProtectedPathFromCommandLine(string[] args)
    {
        var explicitPath = TryGetCommandLineValue("--open", args);
        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            return explicitPath;
        }

        return args
            .Skip(1)
            .FirstOrDefault(argument =>
                argument.EndsWith(".drmx", StringComparison.OrdinalIgnoreCase));
    }

    private static string? TryGetCommandLineValue(string optionName, string[] args)
    {
        for (var index = 1; index < args.Length - 1; index++)
        {
            if (string.Equals(args[index], optionName, StringComparison.OrdinalIgnoreCase))
            {
                return args[index + 1];
            }
        }

        return null;
    }
}
