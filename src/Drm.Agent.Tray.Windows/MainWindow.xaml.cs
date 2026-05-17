using System.Net.Http;
using System.IO;
using System.Windows;
using Drm.Agent.Core;
using Drm.Crypto;
using Drm.Domain;
using Microsoft.Win32;

namespace Drm.Agent.Tray.Windows;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        PrefillSourcePathFromCommandLine();
        SourcePathBox.TextChanged += (_, _) => UpdateDropZoneHint();
        UpdateDropZoneHint();
    }

    private void UpdateDropZoneHint()
    {
        if (DropZoneHint is null)
        {
            return;
        }

        DropZoneHint.Visibility = string.IsNullOrWhiteSpace(SourcePathBox.Text)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void Window_DragEnter(object sender, System.Windows.DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop)
            ? System.Windows.DragDropEffects.Copy
            : System.Windows.DragDropEffects.None;
        e.Handled = true;
    }

    private void Window_Drop(object sender, System.Windows.DragEventArgs e)
    {
        TrySetSourceFromDrop(e);
    }

    private void DropZone_DragEnter(object sender, System.Windows.DragEventArgs e)
    {
        if (e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop))
        {
            e.Effects = System.Windows.DragDropEffects.Copy;
            DropZone.BorderBrush = System.Windows.Media.Brushes.SteelBlue;
            DropZone.Background = System.Windows.Media.Brushes.AliceBlue;
        }
        else
        {
            e.Effects = System.Windows.DragDropEffects.None;
        }

        e.Handled = true;
    }

    private void DropZone_DragLeave(object sender, System.Windows.DragEventArgs e)
    {
        ResetDropZoneStyle();
    }

    private void DropZone_Drop(object sender, System.Windows.DragEventArgs e)
    {
        ResetDropZoneStyle();
        TrySetSourceFromDrop(e);
    }

    private void ResetDropZoneStyle()
    {
        DropZone.BorderBrush = new System.Windows.Media.SolidColorBrush(
            System.Windows.Media.Color.FromRgb(0xD1, 0xD5, 0xDB));
        DropZone.Background = System.Windows.Media.Brushes.White;
    }

    private void TrySetSourceFromDrop(System.Windows.DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop))
        {
            return;
        }

        if (e.Data.GetData(System.Windows.DataFormats.FileDrop) is not string[] paths || paths.Length == 0)
        {
            return;
        }

        var firstFile = paths.FirstOrDefault(File.Exists);
        if (firstFile is null)
        {
            SetStatus("Folders are not supported; drop a single file.");
            return;
        }

        SourcePathBox.Text = firstFile;
        SetStatus($"Ready to protect: {Path.GetFileName(firstFile)}");
        e.Handled = true;
    }

    private void BrowseButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false,
            Title = "Select file to protect"
        };

        if (dialog.ShowDialog(this) == true)
        {
            SourcePathBox.Text = dialog.FileName;
        }
    }

    private async void ProtectButton_Click(object sender, RoutedEventArgs e)
    {
        ProtectButton.IsEnabled = false;
        SetStatus("Protecting file...");

        try
        {
            var serverUrl = ParseServerUrl();
            var tenantId = ParseRequiredGuid(TenantIdBox.Text, "Tenant ID");
            var userId = ParseRequiredGuid(UserIdBox.Text, "User ID");
            var policyTemplateId = ParseOptionalGuid(PolicyTemplateIdBox.Text, "Policy template ID");
            var recipients = ParseRecipients(RecipientUserIdsBox.Text, RecipientGroupIdsBox.Text);
            var sourcePath = SourcePathBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(sourcePath))
            {
                throw new InvalidOperationException("Select a file before protecting.");
            }

            var clientApiKey = ClientApiKeyBox.Password.Trim();
            using var httpClient = new HttpClient { BaseAddress = serverUrl };
            var serverClient = new DrmServerClient(httpClient, clientApiKey);
            var inventory = new JsonProtectedFileInventory(ResolveDataPath("protected-inventory.json"));
            var keyStore = new JsonFileKeyStore(ResolveDataPath("file-keys.json"));
            var workflow = new ProtectFileWorkflow(serverClient, inventory, keyStore);

            var result = await workflow.ProtectAsync(
                new TenantId(tenantId),
                new UserId(userId),
                sourcePath,
                EnvelopeCrypto.GenerateKey(),
                new ProtectFilePolicyOptions(Permission.View | Permission.Print, policyTemplateId, recipients),
                DeleteOriginalBox.IsChecked == true,
                CancellationToken.None);

            SetStatus($"Protected: {result.DestinationPath}");
        }
        catch (Exception exception)
        {
            SetStatus($"Failed: {exception.Message}");
        }
        finally
        {
            ProtectButton.IsEnabled = true;
        }
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

    private static Guid? ParseOptionalGuid(string value, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return ParseRequiredGuid(value, fieldName);
    }

    private static IReadOnlyList<ProtectionRecipient> ParseRecipients(string userIds, string groupIds)
    {
        var recipients = new List<ProtectionRecipient>();
        recipients.AddRange(ParseGuidList(userIds, "Recipient user IDs")
            .Select(userId => new ProtectionRecipient("User", userId)));
        recipients.AddRange(ParseGuidList(groupIds, "Recipient group IDs")
            .Select(groupId => new ProtectionRecipient("Group", groupId)));
        return recipients;
    }

    private static IEnumerable<Guid> ParseGuidList(string value, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        return value
            .Split([',', ';', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(item => ParseRequiredGuid(item, fieldName))
            .ToList();
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

    private void SetStatus(string message)
    {
        StatusText.Text = message;
    }

    private void TransparentDropZone_DragEnter(object sender, System.Windows.DragEventArgs e)
    {
        if (e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop))
        {
            e.Effects = System.Windows.DragDropEffects.Copy;
            TransparentDropZone.BorderBrush = System.Windows.Media.Brushes.SteelBlue;
            TransparentDropZone.Background = System.Windows.Media.Brushes.AliceBlue;
        }
        else
        {
            e.Effects = System.Windows.DragDropEffects.None;
        }
        e.Handled = true;
    }

    private void TransparentDropZone_DragLeave(object sender, System.Windows.DragEventArgs e)
    {
        TransparentDropZone.BorderBrush = new System.Windows.Media.SolidColorBrush(
            System.Windows.Media.Color.FromRgb(0xD1, 0xD5, 0xDB));
        TransparentDropZone.Background = System.Windows.Media.Brushes.White;
    }

    private async void TransparentDropZone_Drop(object sender, System.Windows.DragEventArgs e)
    {
        TransparentDropZone_DragLeave(sender, e);
        if (!e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop)) return;
        if (e.Data.GetData(System.Windows.DataFormats.FileDrop) is not string[] paths || paths.Length == 0) return;

        var sourceFile = paths.FirstOrDefault(File.Exists);
        if (sourceFile is null)
        {
            SetStatus("Drop a file (folders are not supported).");
            return;
        }

        try
        {
            var serverUrl = ParseServerUrl();
            var tenantId = ParseRequiredGuid(TenantIdBox.Text, "Tenant ID");
            var ownerUserId = ParseRequiredGuid(UserIdBox.Text, "User ID");
            var fileId = Guid.NewGuid();
            using var httpClient = new HttpClient { BaseAddress = serverUrl };
            httpClient.DefaultRequestHeaders.TryAddWithoutValidation(
                "X-DRM-Admin-Key", ClientApiKeyBox.Password.Trim());

            using var secretResponse = await httpClient.GetAsync("/api/admin/transparent-files/secret");
            secretResponse.EnsureSuccessStatusCode();
            var secretJson = await secretResponse.Content.ReadAsStringAsync();
            using var secretDoc = System.Text.Json.JsonDocument.Parse(secretJson);
            var secret = secretDoc.RootElement.GetProperty("secret").GetString() ?? "";
            var hmacKey = System.Text.Encoding.UTF8.GetBytes(secret);

            var originalBytes = await File.ReadAllBytesAsync(sourceFile);
            var fileName = Path.GetFileName(sourceFile);
            var contentType = Path.GetExtension(sourceFile).ToLowerInvariant() switch
            {
                ".pdf" => "application/pdf",
                ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                ".pptx" => "application/vnd.openxmlformats-officedocument.presentationml.presentation",
                ".txt" => "text/plain",
                _ => "application/octet-stream"
            };

            var metadata = new Drm.Crypto.TransparentMetadata(
                tenantId,
                fileId,
                ownerUserId,
                contentType,
                fileName,
                DateTimeOffset.UtcNow,
                null);
            var stamped = Drm.Crypto.TransparentEnvelope.AppendTrailer(originalBytes, metadata, hmacKey);
            var outPath = Path.Combine(Path.GetDirectoryName(sourceFile)!, $"{Path.GetFileNameWithoutExtension(sourceFile)}-drm{Path.GetExtension(sourceFile)}");
            await File.WriteAllBytesAsync(outPath, stamped);

            var registerBody = new
            {
                tenantId,
                fileId,
                ownerUserId,
                originalFileName = fileName,
                contentType,
                policyTemplateId = (Guid?)null
            };
            using var registerContent = new StringContent(
                System.Text.Json.JsonSerializer.Serialize(registerBody),
                System.Text.Encoding.UTF8,
                "application/json");
            using var registerResponse = await httpClient.PostAsync("/api/admin/transparent-files", registerContent);
            var registered = registerResponse.IsSuccessStatusCode;

            SetStatus($"Transparent file written: {outPath} ({stamped.Length:N0} bytes). Server register: {(registered ? "ok" : (int)registerResponse.StatusCode)}.");
            TransparentDropHint.Text = $"Last protected: {fileName} → {Path.GetFileName(outPath)} (+{stamped.Length - originalBytes.Length} bytes trailer)";
        }
        catch (Exception ex)
        {
            SetStatus($"Transparent-protect failed: {ex.Message}");
        }
    }

    private void ContainerDropZone_DragEnter(object sender, System.Windows.DragEventArgs e)
    {
        if (e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop))
        {
            e.Effects = System.Windows.DragDropEffects.Copy;
            ContainerDropZone.BorderBrush = System.Windows.Media.Brushes.SteelBlue;
            ContainerDropZone.Background = System.Windows.Media.Brushes.AliceBlue;
        }
        else
        {
            e.Effects = System.Windows.DragDropEffects.None;
        }
        e.Handled = true;
    }

    private void ContainerDropZone_DragLeave(object sender, System.Windows.DragEventArgs e)
    {
        ContainerDropZone.BorderBrush = new System.Windows.Media.SolidColorBrush(
            System.Windows.Media.Color.FromRgb(0xD1, 0xD5, 0xDB));
        ContainerDropZone.Background = System.Windows.Media.Brushes.White;
    }

    private async void ContainerDropZone_Drop(object sender, System.Windows.DragEventArgs e)
    {
        ContainerDropZone_DragLeave(sender, e);
        if (!e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop)) return;
        if (e.Data.GetData(System.Windows.DataFormats.FileDrop) is not string[] paths || paths.Length == 0) return;

        var folder = paths.FirstOrDefault(Directory.Exists);
        if (folder is null)
        {
            SetStatus("Drop a folder (not a single file).");
            return;
        }

        var passphrase = ContainerPassphraseBox.Password;
        if (string.IsNullOrEmpty(passphrase) || passphrase.Length < 6)
        {
            SetStatus("Container passphrase must be at least 6 characters.");
            return;
        }

        try
        {
            var serverUrl = ParseServerUrl();
            var tenantId = ParseRequiredGuid(TenantIdBox.Text, "Tenant ID");
            var ownerUserId = ParseRequiredGuid(UserIdBox.Text, "User ID");
            var containerId = Guid.NewGuid();

            var entries = new List<Drm.Crypto.SecureContainerEntry>();
            foreach (var filePath in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(folder, filePath).Replace('\\', '/');
                var bytes = await File.ReadAllBytesAsync(filePath);
                entries.Add(new Drm.Crypto.SecureContainerEntry(rel, bytes));
            }

            if (entries.Count == 0)
            {
                SetStatus("Folder contains no files.");
                return;
            }

            var key = Drm.Crypto.SecureContainer.DeriveKey(passphrase, containerId);
            var container = Drm.Crypto.SecureContainer.Pack(containerId, entries, key);
            var displayName = Path.GetFileName(folder);
            if (string.IsNullOrWhiteSpace(displayName)) displayName = "secure-container";
            var outPath = Path.Combine(Path.GetDirectoryName(folder.TrimEnd(Path.DirectorySeparatorChar))!, $"{displayName}.drmcontainer");
            await File.WriteAllBytesAsync(outPath, container);

            using var httpClient = new HttpClient { BaseAddress = serverUrl };
            httpClient.DefaultRequestHeaders.TryAddWithoutValidation(
                "X-DRM-Admin-Key", ClientApiKeyBox.Password.Trim());
            var registerBody = new
            {
                tenantId,
                containerId,
                ownerUserId,
                displayName,
                policyTemplateId = (Guid?)null,
                files = entries.Select(e => new { relativePath = e.RelativePath, size = (long)e.Content.Length }).ToList()
            };
            using var registerContent = new StringContent(
                System.Text.Json.JsonSerializer.Serialize(registerBody),
                System.Text.Encoding.UTF8,
                "application/json");
            using var registerResponse = await httpClient.PostAsync("/api/admin/secure-containers", registerContent);
            var registered = registerResponse.IsSuccessStatusCode;

            SetStatus($"Container created: {outPath} ({entries.Count} files, {container.Length:N0} bytes). Server register: {(registered ? "ok" : (int)registerResponse.StatusCode)}.");
            ContainerDropHint.Text = $"Last sealed: {displayName} ({entries.Count} files → {Path.GetFileName(outPath)})";
        }
        catch (Exception ex)
        {
            SetStatus($"Container create failed: {ex.Message}");
        }
    }

    private async void CheckFolderWatcherButton_Click(object sender, RoutedEventArgs e)
    {
        CheckFolderWatcherButton.IsEnabled = false;
        FolderWatcherText.Text = "Checking…";
        FolderWatcherDot.Fill = System.Windows.Media.Brushes.Gold;

        try
        {
            var serverUrl = ParseServerUrl();
            var tenantId = ParseRequiredGuid(TenantIdBox.Text, "Tenant ID");
            using var httpClient = new HttpClient { BaseAddress = serverUrl };
            httpClient.DefaultRequestHeaders.TryAddWithoutValidation(
                "X-DRM-Admin-Key", ClientApiKeyBox.Password.Trim());
            using var response = await httpClient.GetAsync(
                $"/api/admin/folder-watcher/config?tenantId={tenantId}");
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                FolderWatcherText.Text = "Not configured";
                FolderWatcherDot.Fill = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0x9C, 0xA3, 0xAF));
                return;
            }
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync();
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var enabled = doc.RootElement.TryGetProperty("enabled", out var enEl) && enEl.GetBoolean();
            var folderCount = doc.RootElement.TryGetProperty("watchedFolders", out var folEl) ? folEl.GetArrayLength() : 0;
            var lastStatus = doc.RootElement.TryGetProperty("lastReportStatus", out var lsEl) ? lsEl.GetString() : null;
            var lastProtected = doc.RootElement.TryGetProperty("lastFilesProtected", out var lpEl) ? lpEl.GetInt32() : 0;
            if (enabled && string.Equals(lastStatus, "ok", StringComparison.OrdinalIgnoreCase))
            {
                FolderWatcherText.Text = $"Running • {folderCount} folder{(folderCount == 1 ? "" : "s")} • {lastProtected} protected";
                FolderWatcherDot.Fill = System.Windows.Media.Brushes.MediumSeaGreen;
            }
            else if (enabled)
            {
                FolderWatcherText.Text = $"Enabled (last status: {lastStatus ?? "n/a"})";
                FolderWatcherDot.Fill = System.Windows.Media.Brushes.Gold;
            }
            else
            {
                FolderWatcherText.Text = "Disabled";
                FolderWatcherDot.Fill = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0x9C, 0xA3, 0xAF));
            }
        }
        catch (Exception ex)
        {
            FolderWatcherText.Text = $"Error: {ex.Message}";
            FolderWatcherDot.Fill = System.Windows.Media.Brushes.IndianRed;
        }
        finally
        {
            CheckFolderWatcherButton.IsEnabled = true;
        }
    }

    private async void CheckLicenseButton_Click(object sender, RoutedEventArgs e)
    {
        CheckLicenseButton.IsEnabled = false;
        LicenseTierText.Text = "Checking…";
        try
        {
            var serverUrl = ParseServerUrl();
            using var httpClient = new HttpClient { BaseAddress = serverUrl };
            httpClient.DefaultRequestHeaders.TryAddWithoutValidation(
                "X-DRM-Admin-Key", ClientApiKeyBox.Password.Trim());
            using var response = await httpClient.GetAsync("/api/admin/license");
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync();
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var tiers = new List<string>();
            if (doc.RootElement.TryGetProperty("enabledTiers", out var tiersEl))
            {
                foreach (var t in tiersEl.EnumerateArray()) tiers.Add(t.GetString() ?? "");
            }
            var paid = doc.RootElement.TryGetProperty("paidEncrypterCount", out var pEl) ? pEl.GetInt32() : 0;
            var free = doc.RootElement.TryGetProperty("freeViewerCount", out var fEl) ? fEl.GetInt32() : 0;
            LicenseTierText.Text = $"{string.Join(" + ", tiers)} • {paid} paid · {free} free viewers";
        }
        catch (Exception exception)
        {
            LicenseTierText.Text = $"Error: {exception.Message}";
        }
        finally
        {
            CheckLicenseButton.IsEnabled = true;
        }
    }

    private async void CheckOutlookStatusButton_Click(object sender, RoutedEventArgs e)
    {
        CheckOutlookStatusButton.IsEnabled = false;
        OutlookStatusText.Text = "Checking…";
        OutlookStatusDot.Fill = System.Windows.Media.Brushes.Gold;

        try
        {
            var serverUrl = ParseServerUrl();
            var tenantId = ParseRequiredGuid(TenantIdBox.Text, "Tenant ID");
            using var httpClient = new HttpClient { BaseAddress = serverUrl };
            httpClient.DefaultRequestHeaders.TryAddWithoutValidation(
                "X-DRM-Admin-Key", ClientApiKeyBox.Password.Trim());

            using var response = await httpClient.GetAsync(
                $"/api/admin/outlook/config?tenantId={tenantId}");

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                OutlookStatusText.Text = "Not configured";
                OutlookStatusDot.Fill = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0x9C, 0xA3, 0xAF));
                return;
            }

            if (!response.IsSuccessStatusCode)
            {
                OutlookStatusText.Text = $"Error {(int)response.StatusCode}";
                OutlookStatusDot.Fill = System.Windows.Media.Brushes.IndianRed;
                return;
            }

            var json = await response.Content.ReadAsStringAsync();
            using var document = System.Text.Json.JsonDocument.Parse(json);
            var enabled = document.RootElement.TryGetProperty("enabled", out var enEl) && enEl.GetBoolean();
            var lifetime = document.RootElement.TryGetProperty("lifetimeProtectedCount", out var lpEl)
                ? lpEl.GetInt32()
                : 0;

            if (enabled)
            {
                OutlookStatusText.Text = $"Enabled • {lifetime} protected";
                OutlookStatusDot.Fill = System.Windows.Media.Brushes.MediumSeaGreen;
            }
            else
            {
                OutlookStatusText.Text = "Disabled";
                OutlookStatusDot.Fill = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0x9C, 0xA3, 0xAF));
            }
        }
        catch (Exception exception)
        {
            OutlookStatusText.Text = $"Error: {exception.Message}";
            OutlookStatusDot.Fill = System.Windows.Media.Brushes.IndianRed;
        }
        finally
        {
            CheckOutlookStatusButton.IsEnabled = true;
        }
    }

    private async void CheckBoxStatusButton_Click(object sender, RoutedEventArgs e)
    {
        CheckBoxStatusButton.IsEnabled = false;
        BoxStatusText.Text = "Checking…";
        BoxStatusDot.Fill = System.Windows.Media.Brushes.Gold;

        try
        {
            var serverUrl = ParseServerUrl();
            var tenantId = ParseRequiredGuid(TenantIdBox.Text, "Tenant ID");
            using var httpClient = new HttpClient { BaseAddress = serverUrl };
            httpClient.DefaultRequestHeaders.TryAddWithoutValidation(
                "X-DRM-Admin-Key", ClientApiKeyBox.Password.Trim());

            var request = new HttpRequestMessage(HttpMethod.Get,
                $"/api/admin/box/config?tenantId={tenantId}");
            using var response = await httpClient.SendAsync(request);

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                BoxStatusText.Text = "Not configured";
                BoxStatusDot.Fill = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0x9C, 0xA3, 0xAF));
                return;
            }

            if (!response.IsSuccessStatusCode)
            {
                BoxStatusText.Text = $"Error {(int)response.StatusCode}";
                BoxStatusDot.Fill = System.Windows.Media.Brushes.IndianRed;
                return;
            }

            var json = await response.Content.ReadAsStringAsync();
            using var document = System.Text.Json.JsonDocument.Parse(json);
            var enabled = document.RootElement.TryGetProperty("enabled", out var enEl) && enEl.GetBoolean();
            var lastStatus = document.RootElement.TryGetProperty("lastConnectionStatus", out var lsEl)
                ? lsEl.GetString()
                : null;

            if (enabled && string.Equals(lastStatus, "ok", StringComparison.OrdinalIgnoreCase))
            {
                BoxStatusText.Text = "Connected";
                BoxStatusDot.Fill = System.Windows.Media.Brushes.MediumSeaGreen;
            }
            else if (enabled)
            {
                BoxStatusText.Text = $"Enabled (last: {lastStatus ?? "untested"})";
                BoxStatusDot.Fill = System.Windows.Media.Brushes.Gold;
            }
            else
            {
                BoxStatusText.Text = "Disabled";
                BoxStatusDot.Fill = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0x9C, 0xA3, 0xAF));
            }
        }
        catch (Exception exception)
        {
            BoxStatusText.Text = $"Error: {exception.Message}";
            BoxStatusDot.Fill = System.Windows.Media.Brushes.IndianRed;
        }
        finally
        {
            CheckBoxStatusButton.IsEnabled = true;
        }
    }

    private void PrefillSourcePathFromCommandLine()
    {
        var sourcePath = TryGetCommandLineValue("--protect", Environment.GetCommandLineArgs());
        if (!string.IsNullOrWhiteSpace(sourcePath))
        {
            SourcePathBox.Text = sourcePath;
        }
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
