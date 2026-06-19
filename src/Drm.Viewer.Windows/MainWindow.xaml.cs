using System.Collections.ObjectModel;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Drm.Agent.Core;
using Drm.Domain;
using Drm.Watermark;
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
    private readonly List<string> temporaryPrintWatermarkFiles = new();
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
    private readonly DesktopAgentConfiguration desktopConfiguration;
    private readonly AgentIdentityCacheEntry? cachedIdentity;
    private bool autoOpenFromCommandLine;
    private const int WatermarkTileCount = 16;

    public MainWindow()
    {
        InitializeComponent();
        desktopConfiguration = DesktopAgentConfiguration.Load();
        cachedIdentity = ReadCachedIdentity();
        PrefillDesktopConfiguration();
        // Block external screen capture (Snipping Tool, OBS, screen share)
        // before the window paints. The helper defers if the HWND isn't
        // ready yet and applies on SourceInitialized.
        ScreenCaptureProtection.Enable(this);
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
        autoOpenFromCommandLine = PrefillProtectedPathFromCommandLine();
        ApplyPermissionState();
        Loaded += MainWindow_Loaded;
        if (!autoOpenFromCommandLine && ShouldShowFirstRunOverlay())
        {
            HelpOverlayRoot.Visibility = Visibility.Visible;
        }
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        if (autoOpenFromCommandLine && HasConfiguredOpenIdentity())
        {
            await OpenSelectedProtectedFileAsync();
        }
    }

    private static AgentIdentityCacheEntry? ReadCachedIdentity()
    {
        return ViewerIdentityCache.TryReadDefault();
    }

    private void PrefillDesktopConfiguration()
    {
        var serverUrl = desktopConfiguration.ServerUrl ?? cachedIdentity?.ServerUrl;
        if (serverUrl is not null)
        {
            ServerUrlBox.Text = serverUrl.ToString();
        }

        if (!string.IsNullOrWhiteSpace(desktopConfiguration.ClientApiKey))
        {
            ClientApiKeyBox.Password = desktopConfiguration.ClientApiKey;
        }

        var tenantUser = desktopConfiguration.TryCreateIdentity(cachedIdentity);
        if (tenantUser is not null)
        {
            UserIdBox.Text = tenantUser.UserId.ToString();
            DeviceIdBox.Text = tenantUser.DeviceId.ToString();
        }
        else
        {
            if (desktopConfiguration.UserId is { } configuredUserId)
            {
                UserIdBox.Text = configuredUserId.ToString();
            }
            else if (cachedIdentity is not null)
            {
                UserIdBox.Text = cachedIdentity.UserId.ToString();
            }

            if (desktopConfiguration.DeviceId is { } configuredDeviceId)
            {
                DeviceIdBox.Text = configuredDeviceId.ToString();
            }
        }
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

    private byte[]? droppedContainerBytes;
    private string? droppedContainerPath;

    private async void Window_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            return;
        }

        if (e.Data.GetData(DataFormats.FileDrop) is not string[] paths || paths.Length == 0)
        {
            return;
        }

        var firstContainer = paths.FirstOrDefault(p =>
            File.Exists(p) && p.EndsWith(".drmcontainer", StringComparison.OrdinalIgnoreCase));
        if (firstContainer is not null)
        {
            await PrepareContainerAsync(firstContainer);
            e.Handled = true;
            return;
        }

        var firstDrmx = paths.FirstOrDefault(path =>
            File.Exists(path) && path.EndsWith(".drmx", StringComparison.OrdinalIgnoreCase));

        if (firstDrmx is not null)
        {
            ProtectedPathBox.Text = firstDrmx;
            StatusText.Text = $"Ready to open: {Path.GetFileName(firstDrmx)}";
            e.Handled = true;
            return;
        }

        var firstFile = paths.FirstOrDefault(File.Exists);
        if (firstFile is not null)
        {
            await TryShowTransparentBannerAsync(firstFile);
            e.Handled = true;
        }
        else
        {
            StatusText.Text = "Drop a .drmx, .drmcontainer, or transparent-protected file.";
        }
    }

    private async Task PrepareContainerAsync(string path)
    {
        try
        {
            droppedContainerBytes = await File.ReadAllBytesAsync(path);
            droppedContainerPath = path;
            ContainerBanner.Visibility = Visibility.Visible;
            ContainerFilesList.Visibility = Visibility.Collapsed;
            ContainerBannerHeader.Text = $"Secure container detected: {Path.GetFileName(path)} ({droppedContainerBytes.Length:N0} bytes). Enter passphrase to list contents.";
            StatusText.Text = $"Container ready: {Path.GetFileName(path)}";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Container load failed: {ex.Message}";
        }
    }

    private void OpenContainerButton_Click(object sender, RoutedEventArgs e)
    {
        if (droppedContainerBytes is null)
        {
            StatusText.Text = "Drop a .drmcontainer onto the window first.";
            return;
        }
        var passphrase = ContainerOpenPassphraseBox.Password;
        if (string.IsNullOrEmpty(passphrase))
        {
            StatusText.Text = "Enter the container passphrase.";
            return;
        }
        try
        {
            // Sniff salt from header to derive the v2 key; fall back to legacy
            // v1 derivation when the file predates the salt addition.
            var headerOffset = Drm.Crypto.SecureContainer.Magic.Length + 1;
            var containerIdBytes = droppedContainerBytes.AsSpan(headerOffset, 16).ToArray();
            var containerId = new Guid(containerIdBytes);
            var salt = Drm.Crypto.SecureContainer.TryReadSalt(droppedContainerBytes);
#pragma warning disable CS0618 // v1 legacy KDF retained for backward compatibility
            var key = salt is not null
                ? Drm.Crypto.SecureContainer.DeriveKey(passphrase, salt)
                : Drm.Crypto.SecureContainer.DeriveKeyLegacyV1(passphrase, containerId);
#pragma warning restore CS0618
            var unpacked = Drm.Crypto.SecureContainer.Unpack(droppedContainerBytes, key);

            ContainerFilesList.ItemsSource = unpacked.Manifest.Entries
                .Select(e => $"{e.RelativePath}  ({e.Size:N0} bytes)")
                .ToList();
            ContainerFilesList.Visibility = Visibility.Visible;
            ContainerBannerHeader.Text = $"Container opened: {unpacked.Entries.Count} files, container ID {containerId}";
            StatusText.Text = $"Container unpacked ({unpacked.Entries.Count} files).";
        }
        catch (System.Security.Cryptography.AuthenticationTagMismatchException)
        {
            StatusText.Text = "Wrong passphrase or container has been tampered with.";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Container open failed: {ex.Message}";
        }
    }

    private async Task TryShowTransparentBannerAsync(string filePath)
    {
        try
        {
            var bytes = await File.ReadAllBytesAsync(filePath);
            var serverUrl = ServerUrlBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(serverUrl) || !Uri.TryCreate(serverUrl, UriKind.Absolute, out var uri))
            {
                StatusText.Text = "Set Server URL to verify transparent trailers.";
                return;
            }

            // Verification runs server-side so the HMAC secret never leaves
            // the trust boundary.
            using var http = new System.Net.Http.HttpClient { BaseAddress = uri };
            http.DefaultRequestHeaders.TryAddWithoutValidation(
                "X-DRM-Admin-Key", ClientApiKeyBox.Password.Trim());
            var verifyBody = new { fileBytesBase64 = Convert.ToBase64String(bytes) };
            using var verifyContent = new StringContent(
                System.Text.Json.JsonSerializer.Serialize(verifyBody),
                System.Text.Encoding.UTF8,
                "application/json");
            using var verifyResp = await http.PostAsync("/api/admin/transparent-files/verify", verifyContent);
            if (!verifyResp.IsSuccessStatusCode)
            {
                StatusText.Text = $"Transparent verify failed (HTTP {(int)verifyResp.StatusCode}).";
                return;
            }

            var verifyJson = await verifyResp.Content.ReadAsStringAsync();
            using var doc = System.Text.Json.JsonDocument.Parse(verifyJson);
            var valid = doc.RootElement.GetProperty("valid").GetBoolean();
            if (valid)
            {
                var tenantId = doc.RootElement.GetProperty("tenantId").GetString() ?? "";
                var fileId = doc.RootElement.GetProperty("fileId").GetString() ?? "";
                var registered = doc.RootElement.GetProperty("registeredAtUtc").GetString() ?? "";
                var originalLength = doc.RootElement.GetProperty("originalLength").GetInt32();
                TransparentBanner.Visibility = Visibility.Visible;
                TransparentBannerText.Text = $"Transparent DRM file detected · Tenant {tenantId} · File {fileId} · Registered {registered} · Original size {originalLength:N0} bytes.";
                StatusText.Text = $"Transparent trailer valid. Open file in its native application: {filePath}";
            }
            else
            {
                TransparentBanner.Visibility = Visibility.Collapsed;
                StatusText.Text = "No DRM transparent trailer found (or HMAC mismatch).";
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Transparent trailer check failed: {ex.Message}";
        }
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
        => await OpenSelectedProtectedFileAsync();

    private async Task OpenSelectedProtectedFileAsync()
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
            if (string.IsNullOrWhiteSpace(clientApiKey))
            {
                throw new InvalidOperationException("Client API key is not configured on this machine.");
            }

            using var httpClient = new HttpClient { BaseAddress = serverUrl };
            var serverClient = new DrmServerClient(
                httpClient,
                clientApiKey,
                deviceRequestSigner: new LocalDeviceRequestSigner());
            var keyStore = new JsonFileKeyStore(ResolveDataPath("file-keys.json"));
            var decisionCache = new JsonPolicyDecisionCache(ResolveDataPath("policy-decisions.json"));
            var opened = await new OpenProtectedFileWorkflow(serverClient, keyStore, decisionCache)
                .OpenAsync(protectedPath, userId, deviceId, CancellationToken.None);

            var compatNotice = CompatibilityNotices.ForContentType(opened.ContentType);
            if (compatNotice is not null)
            {
                StatusText.Text = $"Compatibility notice: {compatNotice}";
            }

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

    private bool HasConfiguredOpenIdentity()
    {
        return Uri.TryCreate(ServerUrlBox.Text.Trim(), UriKind.Absolute, out _) &&
            !string.IsNullOrWhiteSpace(ClientApiKeyBox.Password) &&
            Guid.TryParse(UserIdBox.Text.Trim(), out var userId) &&
            userId != Guid.Empty &&
            Guid.TryParse(DeviceIdBox.Text.Trim(), out var deviceId) &&
            deviceId != Guid.Empty &&
            !string.IsNullOrWhiteSpace(ProtectedPathBox.Text);
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
                temporaryPrintWatermarkFiles.Add(stampedPath);
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

    private const string FirstRunOverlayPathSegment = "DRM/viewer-first-run.flag";

    private bool ShouldShowFirstRunOverlay()
    {
        var flag = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            FirstRunOverlayPathSegment);
        return !File.Exists(flag);
    }

    private void MarkFirstRunOverlaySeen()
    {
        var flag = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            FirstRunOverlayPathSegment);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(flag)!);
            File.WriteAllText(flag, DateTimeOffset.UtcNow.ToString("O"));
        }
        catch (IOException) { /* not fatal */ }
    }

    private void DismissHelpOverlay_Click(object sender, RoutedEventArgs e)
    {
        HelpOverlayRoot.Visibility = Visibility.Collapsed;
        MarkFirstRunOverlaySeen();
    }

    private void OpenDrmExplorer_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var serverUrl = ParseServerUrl();
            var tenantIdValue = currentIdentity?.TenantId
                ?? throw new InvalidOperationException("Open a protected file first so the explorer knows which tenant to query.");
            var adminKey = ClientApiKeyBox.Password.Trim();
            if (string.IsNullOrEmpty(adminKey))
            {
                StatusText.Text = "DRM Explorer needs the machine client key to be configured.";
                return;
            }
            var explorer = new DrmExplorerWindow(serverUrl, tenantIdValue, adminKey)
            {
                Owner = this
            };
            explorer.Show();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Could not open DRM Explorer: {ex.Message}";
        }
    }

    private async void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F1)
        {
            HelpOverlayRoot.Visibility = Visibility.Visible;
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape && HelpOverlayRoot.Visibility == Visibility.Visible)
        {
            HelpOverlayRoot.Visibility = Visibility.Collapsed;
            e.Handled = true;
            return;
        }

        // Intercept Print Screen + screen-snip shortcuts. WPF still receives
        // the key event even though SetWindowDisplayAffinity already blocks
        // the captured image; we add the visible feedback so the user knows
        // their screenshot attempt was noticed. Sequence: clear clipboard
        // (in case Snipping Tool wrote something), show overlay, audit.
        if (e.Key == Key.PrintScreen ||
            (e.Key == Key.S && Keyboard.Modifiers == (ModifierKeys.Windows | ModifierKeys.Shift)))
        {
            e.Handled = true;
            try
            {
                Clipboard.Clear();
            }
            catch
            {
                // Clipboard.Clear can throw if another process owns the
                // clipboard. We treat that as fine — SetWindowDisplayAffinity
                // already blocked the actual image capture.
            }
            StatusText.Text = "Screen capture blocked. This file is protected.";
            return;
        }

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
        DeleteTemporaryPrintWatermarkFiles();
        base.OnClosed(e);
    }

    private void DeleteTemporaryPrintWatermarkFiles()
    {
        foreach (var path in temporaryPrintWatermarkFiles)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch (IOException) { /* embedded browser may still hold the file briefly */ }
        }
        temporaryPrintWatermarkFiles.Clear();
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

    private bool PrefillProtectedPathFromCommandLine()
    {
        var protectedPath = TryGetProtectedPathFromCommandLine(Environment.GetCommandLineArgs());
        if (!string.IsNullOrWhiteSpace(protectedPath))
        {
            ProtectedPathBox.Text = protectedPath;
            return true;
        }

        return false;
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
