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
    private readonly DesktopAgentConfiguration desktopConfiguration;

    private static readonly string[] CadExtensionList =
    {
        ".dwg",
        ".dxf",
        ".dwt",
        ".dws",
        ".step",
        ".stp",
        ".iges",
        ".igs",
        ".sldprt",
        ".sldasm",
        ".slddrw",
        ".x_t",
        ".x_b",
        ".prt",
        ".asm",
        ".catpart",
        ".catproduct",
        ".jt",
        ".ifc",
        ".sat",
        ".stl"
    };

    private static readonly HashSet<string> CadExtensions = new(CadExtensionList, StringComparer.OrdinalIgnoreCase);

    private static bool TransparentProtectEnabled => false;

    private static bool IsSupportedCadFile(string path)
        => CadExtensions.Contains(Path.GetExtension(path));

    private static string BuildCadOpenFileDialogFilter()
    {
        var patterns = string.Join(";", CadExtensionList.Select(extension => $"*{extension}"));
        return $"CAD files ({patterns})|{patterns}|All files (*.*)|*.*";
    }

    /// <summary>
    /// Legacy parameterless constructor — kept so the previous
    /// StartupUri-driven launch path (and any unit-test code that
    /// new'd MainWindow() directly) still compiles. App.xaml.cs has
    /// been switched to the (serverUrl, cached) overload so it can
    /// pre-fill from the discover-endpoint result.
    /// </summary>
    public MainWindow() : this(serverUrl: null, cachedIdentity: null, desktopConfiguration: null)
    {
    }

    /// <summary>
    /// Identity-aware constructor: pre-fills Server URL / Tenant ID /
    /// User ID / Policy Template fields from the cached
    /// AgentIdentityCacheEntry produced by FirstRunDialog. Either
    /// argument may be null — the corresponding field then stays
    /// blank and the user fills it in manually.
    /// </summary>
    public MainWindow(
        Uri? serverUrl,
        AgentIdentityCacheEntry? cachedIdentity,
        DesktopAgentConfiguration? desktopConfiguration = null)
    {
        InitializeComponent();
        this.desktopConfiguration = desktopConfiguration ?? DesktopAgentConfiguration.Load();
        PrefillSourcePathFromCommandLine();
        PrefillIdentityFromCache(serverUrl, cachedIdentity, this.desktopConfiguration);
        SourcePathBox.TextChanged += (_, _) => UpdateDropZoneHint();
        UpdateDropZoneHint();
        _ = LoadRecentRecipientsAsync();
    }

    // Stage 15 — shared store + helper. Both Quick Send and Folder Share
    // hydrate their ComboBoxes from the same recent-recipients list so a
    // recipient typed into either surface shows up next time on both.
    private readonly IRecentRecipientsStore recentRecipientsStore =
        new JsonRecentRecipientsStore(ResolveDataPath("recent-recipients.json"));

    private async Task LoadRecentRecipientsAsync()
    {
        try
        {
            var recipients = await recentRecipientsStore.ListAsync(CancellationToken.None);
            if (QuickRecipientBox is not null)
            {
                var preserved = QuickRecipientBox.Text;
                QuickRecipientBox.ItemsSource = recipients;
                QuickRecipientBox.Text = preserved;
            }
            if (FolderShareRecipientBox is not null)
            {
                var preserved = FolderShareRecipientBox.Text;
                FolderShareRecipientBox.ItemsSource = recipients;
                FolderShareRecipientBox.Text = preserved;
            }
        }
        catch
        {
            // Recent-recipients is a polish feature — silently fall back
            // to empty dropdowns if the JSON store can't be read for any
            // reason. The user can still type addresses by hand.
        }
    }

    private async Task RememberRecipientAsync(string email)
    {
        if (string.IsNullOrWhiteSpace(email)) return;
        try
        {
            await recentRecipientsStore.RememberAsync(email, CancellationToken.None);
            await LoadRecentRecipientsAsync();
        }
        catch
        {
            // Don't surface storage errors — the share succeeded; this is
            // just history bookkeeping.
        }
    }

    /// <summary>
    /// Stage 3 pre-fill: the FirstRunDialog gave us a (TenantId,
    /// UserId, server URL, default template) tuple. Drop those into
    /// the existing textboxes so the operator sees them on first
    /// open and doesn't have to copy-paste GUIDs.
    ///
    /// We pre-fill silently — no toast or modal — because at first
    /// launch the user has just dismissed FirstRunDialog and any
    /// additional "we set these for you" UI would feel paternalistic.
    /// On subsequent launches the boxes are already filled from the
    /// cache, so nothing surprising happens.
    /// </summary>
    private void PrefillIdentityFromCache(
        Uri? serverUrl,
        AgentIdentityCacheEntry? cached,
        DesktopAgentConfiguration desktopConfiguration)
    {
        var configuredServerUrl = desktopConfiguration.ServerUrl ?? serverUrl;
        if (configuredServerUrl is not null && ServerUrlBox is not null
            && string.IsNullOrWhiteSpace(ServerUrlBox.Text))
        {
            ServerUrlBox.Text = configuredServerUrl.ToString();
        }

        if (!string.IsNullOrWhiteSpace(desktopConfiguration.ClientApiKey) && ClientApiKeyBox is not null)
        {
            ClientApiKeyBox.Password = desktopConfiguration.ClientApiKey;
        }

        if (desktopConfiguration.TenantId is { } configuredTenantId && TenantIdBox is not null)
        {
            TenantIdBox.Text = configuredTenantId.ToString();
        }

        if (desktopConfiguration.UserId is { } configuredUserId && UserIdBox is not null)
        {
            UserIdBox.Text = configuredUserId.ToString();
        }

        if (cached is null)
        {
            return;
        }

        // Prefer the URL we already resolved from registry/default
        // over the one we cached previously — registry takes
        // precedence so a sysadmin who re-pointed the MSI registry
        // key gets honoured.
        if (configuredServerUrl is null && ServerUrlBox is not null
            && string.IsNullOrWhiteSpace(ServerUrlBox.Text))
        {
            ServerUrlBox.Text = cached.ServerUrl.ToString();
        }

        if (TenantIdBox is not null && string.IsNullOrWhiteSpace(TenantIdBox.Text))
        {
            TenantIdBox.Text = cached.TenantId.ToString();
        }

        if (UserIdBox is not null && string.IsNullOrWhiteSpace(UserIdBox.Text))
        {
            UserIdBox.Text = cached.UserId.ToString();
        }

        if (cached.DefaultPolicyTemplateId is { } templateId
            && PolicyTemplateIdBox is not null
            && string.IsNullOrWhiteSpace(PolicyTemplateIdBox.Text))
        {
            PolicyTemplateIdBox.Text = templateId.ToString();
        }

        Title = $"zcrDRM Agent — {cached.DisplayName} ({cached.Email})";
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
            Filter = BuildCadOpenFileDialogFilter(),
            CheckFileExists = true,
            Multiselect = false,
            Title = "Select CAD file to protect"
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
            var sourcePath = SourcePathBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(sourcePath))
            {
                throw new InvalidOperationException("Select a CAD file before protecting.");
            }

            if (!IsSupportedCadFile(sourcePath))
            {
                throw new InvalidOperationException("Internal protection only accepts CAD files.");
            }

            var clientApiKey = ClientApiKeyBox.Password.Trim();
            if (string.IsNullOrWhiteSpace(clientApiKey))
            {
                throw new InvalidOperationException("Client API key is not configured on this machine.");
            }

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
                new ProtectFilePolicyOptions(Permission.View, policyTemplateId, Recipients: []),
                DeleteOriginalBox.IsChecked == true,
                CancellationToken.None);

            SetStatus($"Protected internally: {result.DestinationPath}");
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

        if (!TransparentProtectEnabled)
        {
            SetStatus("Transparent protect is disabled for internal CAD mode.");
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

            // Server-side stamping: bytes go up, stamped bytes come back. The
            // HMAC trailer secret never leaves the server.
            var stampBody = new
            {
                tenantId,
                fileId,
                ownerUserId,
                originalFileName = fileName,
                contentType,
                policyTemplateId = (Guid?)null,
                fileBytesBase64 = Convert.ToBase64String(originalBytes)
            };
            using var stampContent = new StringContent(
                System.Text.Json.JsonSerializer.Serialize(stampBody),
                System.Text.Encoding.UTF8,
                "application/json");
            using var stampResponse = await httpClient.PostAsync("/api/admin/transparent-files/stamp", stampContent);
            if (!stampResponse.IsSuccessStatusCode)
            {
                SetStatus($"Server stamp failed: HTTP {(int)stampResponse.StatusCode} {await stampResponse.Content.ReadAsStringAsync()}");
                return;
            }
            var stampJson = await stampResponse.Content.ReadAsStringAsync();
            using var stampDoc = System.Text.Json.JsonDocument.Parse(stampJson);
            var stamped = Convert.FromBase64String(
                stampDoc.RootElement.GetProperty("stampedFileBytesBase64").GetString() ?? "");

            var outPath = Path.Combine(
                Path.GetDirectoryName(sourceFile)!,
                $"{Path.GetFileNameWithoutExtension(sourceFile)}-drm-{DateTime.UtcNow:yyyyMMddHHmmss}{Path.GetExtension(sourceFile)}");
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

    private static readonly System.Windows.Media.Brush WizActiveBrush =
        new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xA4, 0x5B, 0x13));
    private static readonly System.Windows.Media.Brush WizInactiveBrush =
        new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x9C, 0xA3, 0xAF));

    private void UpdateContainerWizardDots(int activeStep)
    {
        WizStep1Dot.Background = activeStep >= 1 ? WizActiveBrush : WizInactiveBrush;
        WizStep2Dot.Background = activeStep >= 2 ? WizActiveBrush : WizInactiveBrush;
        WizStep3Dot.Background = activeStep >= 3 ? WizActiveBrush : WizInactiveBrush;
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

        await PackContainerFromFolderAsync(folder);
    }

    private string? pendingProtectFolder;

    private void BeginProtectFolderFromCommandLine(string folder)
    {
        pendingProtectFolder = folder;
        UpdateContainerWizardDots(2);
        if (ContainerDropHint != null)
        {
            ContainerDropHint.Text =
                $"Folder ready (right-click): {Path.GetFileName(folder)} — type a passphrase ≥ 6 chars and click 'Seal folder'.";
        }
        ContainerPassphraseBox?.Focus();
    }

    private async void SealFolderButton_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        var folder = pendingProtectFolder;
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
        {
            SetStatus("No folder loaded — drop one into the box or relaunch from Explorer right-click.");
            return;
        }
        await PackContainerFromFolderAsync(folder);
    }

    /// <summary>
    /// Core container packaging logic — shared by the drag-and-drop entry
    /// point and the Explorer-right-click + Seal-button entry point. Reads
    /// every file in the folder, runs <see cref="Drm.Crypto.SecureContainer.Pack"/>
    /// with a freshly derived per-container key (passphrase + random salt),
    /// writes the <c>.drmcontainer</c> next to the folder, and registers
    /// the container's metadata with the server.
    /// </summary>
    private async Task PackContainerFromFolderAsync(string folder)
    {
        UpdateContainerWizardDots(2);

        var passphrase = ContainerPassphraseBox.Password;
        if (string.IsNullOrEmpty(passphrase) || passphrase.Length < 6)
        {
            SetStatus("Step 2: set a passphrase ≥ 6 characters, then drop the folder again or click 'Seal folder'.");
            ContainerDropHint.Text = $"Folder ready: {Path.GetFileName(folder)} — fill the passphrase below and click 'Seal folder' (or drop again).";
            ContainerPassphraseBox.Focus();
            return;
        }
        UpdateContainerWizardDots(3);

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

            var salt = System.Security.Cryptography.RandomNumberGenerator.GetBytes(Drm.Crypto.SecureContainer.SaltSize);
            var key = Drm.Crypto.SecureContainer.DeriveKey(passphrase, salt);
            var container = Drm.Crypto.SecureContainer.Pack(containerId, entries, key, salt);
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
            ContainerDropHint.Text = $"✓ Sealed: {displayName} ({entries.Count} files → {Path.GetFileName(outPath)})";
            UpdateContainerWizardDots(0);
            ContainerPassphraseBox.Password = string.Empty;
            pendingProtectFolder = null;
        }
        catch (Exception ex)
        {
            SetStatus($"Container create failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Stage 9: per-recipient share for folders. Packs the folder with the
    /// user-supplied passphrase (same crypto as Seal-self-share), writes
    /// the .drmcontainer next to the source folder, and then calls
    /// /api/me/share with contentType="application/vnd.zcrdrm.container"
    /// to register the recipient binding (email + permissions + expiry).
    /// The server returns a share URL which is copied to clipboard so the
    /// operator can paste it into Outlook alongside the .drmcontainer
    /// attachment. The passphrase is still conveyed out-of-band — this
    /// path adds policy/audit, not key delivery, matching the file Quick
    /// Send model exactly.
    /// </summary>
    private async void ShareFolderButton_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        var folder = pendingProtectFolder;
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
        {
            SetFolderShareResult("Drop a folder or relaunch from Explorer right-click first.", isError: true);
            return;
        }

        var recipient = FolderShareRecipientBox?.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(recipient) || !recipient.Contains('@'))
        {
            SetFolderShareResult("Enter the recipient's email address.", isError: true);
            FolderShareRecipientBox?.Focus();
            return;
        }

        var passphrase = ContainerPassphraseBox.Password;
        if (string.IsNullOrEmpty(passphrase) || passphrase.Length < 6)
        {
            SetFolderShareResult("Set a passphrase ≥ 6 chars — recipient unpacks with this out-of-band.", isError: true);
            ContainerPassphraseBox.Focus();
            return;
        }

        ShareFolderButton.IsEnabled = false;
        SetFolderShareResult("Packing and registering…", isError: false);

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
                SetFolderShareResult("Folder contains no files.", isError: true);
                return;
            }

            var salt = System.Security.Cryptography.RandomNumberGenerator.GetBytes(Drm.Crypto.SecureContainer.SaltSize);
            var key = Drm.Crypto.SecureContainer.DeriveKey(passphrase, salt);
            var container = Drm.Crypto.SecureContainer.Pack(containerId, entries, key, salt);
            var displayName = Path.GetFileName(folder);
            if (string.IsNullOrWhiteSpace(displayName)) displayName = "secure-container";
            var outPath = Path.Combine(Path.GetDirectoryName(folder.TrimEnd(Path.DirectorySeparatorChar))!, $"{displayName}.drmcontainer");
            await File.WriteAllBytesAsync(outPath, container);

            using var httpClient = new HttpClient { BaseAddress = serverUrl };
            httpClient.DefaultRequestHeaders.TryAddWithoutValidation(
                "X-DRM-Admin-Key", ClientApiKeyBox.Password.Trim());
            var body = new
            {
                tenantId,
                userId = ownerUserId,
                recipientEmail = recipient,
                fileName = $"{displayName}.drmcontainer",
                contentType = "application/vnd.zcrdrm.container",
                fileBytesBase64 = Convert.ToBase64String(container),
                expiresInHours = ParseFolderShareExpiryHours(),
                allowPrint          = FolderShareAllowPrintBox?.IsChecked == true,
                allowCopy           = FolderShareAllowCopyBox?.IsChecked == true,
                allowEdit           = FolderShareAllowEditBox?.IsChecked == true,
                allowExportOriginal = FolderShareAllowExportOriginalBox?.IsChecked == true,
            };
            using var content = new StringContent(
                System.Text.Json.JsonSerializer.Serialize(body),
                System.Text.Encoding.UTF8, "application/json");
            using var response = await httpClient.PostAsync("/api/me/share", content);
            if (!response.IsSuccessStatusCode)
            {
                SetFolderShareResult(
                    $"Server register failed: HTTP {(int)response.StatusCode}. .drmcontainer was written to {outPath} for self-share.",
                    isError: true);
                return;
            }
            var json = await response.Content.ReadAsStringAsync();
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var shareUrl = doc.RootElement.GetProperty("shareUrl").GetString() ?? string.Empty;
            System.Windows.Clipboard.SetText(shareUrl);
            // Stage 10: open default mail client with subject + body
            // pre-filled. Body explicitly instructs sender to share the
            // passphrase through a separate channel — never in the same
            // email as the .drmcontainer attachment.
            var containerFileName = Path.GetFileName(outPath);
            var containerFullPath = Path.GetFullPath(outPath);
            SetFolderShareResult("Opening email composer…", isError: false);
            var composeResult = ComposeShareEmail(
                recipient,
                $"Encrypted folder: {containerFileName}",
                bodyFor: inlined => BuildContainerShareEmailBody(shareUrl, containerFileName, containerFullPath, inlined),
                attachmentPath: containerFullPath);
            SetFolderShareResult(
                composeResult switch
                {
                    { AttachmentInlined: true } =>
                        $"✅ Outlook opened with {containerFileName} attached. Hit Send — and remember to message the passphrase separately.",
                    { ComposerOpened: true } =>
                        $"✅ Share URL copied + email composer opened. Attach {containerFileName} and send the passphrase to {recipient} on a separate channel.",
                    // Stage 16: no composer succeeded — share URL is on
                    // clipboard, sender just has to paste into webmail.
                    _ =>
                        $"✅ Wrote {containerFileName} + share URL copied. " +
                        "Couldn't open an email composer — set a default mail client in " +
                        "Settings → Apps → Default apps → Mail. Send the passphrase to " +
                        $"{recipient} on a separate channel."
                },
                isError: false);
            // Stage 15: same recent-recipients store as Quick Send.
            await RememberRecipientAsync(recipient);
            if (ContainerDropHint != null)
            {
                ContainerDropHint.Text = $"✓ Shared: {displayName} → {recipient}";
            }
            ContainerPassphraseBox.Password = string.Empty;
            FolderShareRecipientBox!.Text = string.Empty;
            pendingProtectFolder = null;
        }
        catch (Exception ex)
        {
            SetFolderShareResult($"Share failed: {ex.Message}", isError: true);
        }
        finally
        {
            ShareFolderButton.IsEnabled = true;
        }
    }

    private void SetFolderShareResult(string message, bool isError)
    {
        if (FolderShareResultText is null) return;
        FolderShareResultText.Text = message;
        FolderShareResultText.Foreground = new System.Windows.Media.SolidColorBrush(
            isError
                ? System.Windows.Media.Color.FromRgb(0xB9, 0x1C, 0x1C)
                : System.Windows.Media.Color.FromRgb(0x04, 0x78, 0x57));
    }

    private int ParseFolderShareExpiryHours()
    {
        const int defaultHours = 168;
        if (FolderShareExpiryDropdown?.SelectedItem is not System.Windows.Controls.ComboBoxItem item)
        {
            return defaultHours;
        }
        return int.TryParse(item.Tag?.ToString(), out var hours) ? hours : defaultHours;
    }

    /// <summary>
    /// Stage 10 + 14 — recipient UX polish. After Quick Send /
    /// Share-with-recipient succeeds, open the sender's email client with
    /// a pre-composed message + (when possible) the encrypted file already
    /// attached, so they just hit Send.
    ///
    /// Stage 14 added Outlook COM as the preferred path. mailto: stays as
    /// the fallback for non-Outlook mail clients (Thunderbird, Mail.app,
    /// webmail) — there mailto can't carry attachments so the body asks
    /// the sender to drag the .drmx in themselves. The body factory takes
    /// a bool that says which case we landed in so the body matches reality.
    /// </summary>
    // Stage 19 — bulk-send helpers. Static so they're easy to unit-test
    // via the source-presence pattern + their semantics are pure.

    private sealed record BulkSendOutcome(
        string Recipient,
        bool Success,
        bool ComposerOpened,
        bool AttachmentInlined,
        string? FailureReason);

    private static string BuildBulkResultMessage(string drmxName, IReadOnlyList<BulkSendOutcome> outcomes)
    {
        var successes = outcomes.Where(o => o.Success).ToList();
        var failures = outcomes.Where(o => !o.Success).ToList();
        var inlinedCount = successes.Count(o => o.AttachmentInlined);
        var composerOpenedCount = successes.Count(o => o.ComposerOpened);

        if (outcomes.Count == 1)
        {
            var only = outcomes[0];
            if (!only.Success) return $"Share-link failed: {only.FailureReason}. .drmx still written to {drmxName}.";
            return only switch
            {
                { AttachmentInlined: true } =>
                    $"✅ Wrote {drmxName} + Outlook opened with it attached. Just hit Send.",
                { ComposerOpened: true } =>
                    $"✅ Wrote {drmxName}. Share URL copied + email composer opened — attach the .drmx and send.",
                _ =>
                    $"✅ Wrote {drmxName} + share URL copied to clipboard. " +
                    "Couldn't open an email composer — set a default mail client in " +
                    "Settings → Apps → Default apps → Mail, then paste the URL into a new email."
            };
        }

        // Bulk path — concise summary that names the file once + counts
        // successes / failures so the sender knows what they need to
        // follow up on manually.
        var summary = $"✅ Wrote {drmxName} + created {successes.Count} share link(s)";
        if (composerOpenedCount > 0)
        {
            summary += $", opened {composerOpenedCount} composer(s)";
            if (inlinedCount > 0 && inlinedCount == composerOpenedCount)
                summary += " with attachment already inlined";
            else if (inlinedCount > 0)
                summary += $" ({inlinedCount} with attachment inlined)";
        }
        summary += ".";
        if (failures.Count > 0)
        {
            var failedList = string.Join(", ", failures.Select(f => $"{f.Recipient} [{f.FailureReason}]"));
            summary += $" ⚠ {failures.Count} failed: {failedList}";
        }
        return summary;
    }

    private static EmailComposeResult ComposeShareEmail(
        string recipient,
        string subject,
        Func<bool, string> bodyFor,
        string attachmentPath)
    {
        // Try Outlook COM first — when Outlook is installed it can attach
        // the .drmx programmatically and the sender just clicks Send.
        var outlookResult = new OutlookComEmailComposer().Compose(new EmailComposition(
            recipient,
            subject,
            bodyFor(true),
            File.Exists(attachmentPath) ? [attachmentPath] : []));
        if (outlookResult.ComposerOpened)
        {
            return outlookResult;
        }

        // Fall back to mailto: — body asks the sender to attach the file
        // manually because mailto: links can't pre-fill attachments. If
        // the user has no default mail client configured even this fails;
        // the share URL is still on the clipboard so they can paste it
        // into webmail by hand.
        return new MailtoEmailComposer().Compose(new EmailComposition(
            recipient,
            subject,
            bodyFor(false),
            AttachmentPaths: []));
    }

    private static string BuildFileShareEmailBody(string shareUrl, string fileName, string? localPath, bool attachmentInlined)
    {
        // Stage 13: when ProtectAsync wrote the .drmx, we know the exact
        // path on disk and bake it into the body so the sender can drag
        // it straight in from Explorer rather than hunting in Sent.
        // Stage 14: when Outlook attaches the file programmatically, the
        // body switches to "see attachment" — sender doesn't have to do
        // anything but Send.
        var attachLine = attachmentInlined
            ? "The encrypted .drmx file is already attached to this email — just click Send."
            : string.IsNullOrEmpty(localPath)
                ? "BEFORE SENDING THIS EMAIL: attach the .drmx file from your sent folder " +
                  "(your zcrDRM agent prepared it)."
                : $"BEFORE SENDING THIS EMAIL: attach the file at:\n  {localPath}";
        return
            $"Hi,\n\n" +
            $"I'm sharing a DRM-protected file ({fileName}) with you through zcrDRM.\n\n" +
            $"{attachLine}\n\n" +
            $"To open the file:\n" +
            $"1. Click this link to verify your email: {shareUrl}\n" +
            $"2. Save the .drmx attachment from this email\n" +
            $"3. Double-click to open in the zcrDRM viewer\n" +
            $"   (no viewer yet? the /share/ link has a download button)\n\n" +
            $"Thanks,";
    }

    private static string BuildContainerShareEmailBody(string shareUrl, string containerFileName, string? localPath, bool attachmentInlined)
    {
        // Stage 14: Outlook auto-attach lets us drop the "attach the file"
        // sentence. The passphrase warning stays — sending the passphrase
        // in the same email defeats the encryption, no matter how the
        // attachment got there.
        var attachLine = attachmentInlined
            ? "The encrypted .drmcontainer is already attached to this email. " +
              "REMEMBER: send the passphrase through a separate channel (SMS/chat) — " +
              "do NOT include it in this email."
            : string.IsNullOrEmpty(localPath)
                ? "BEFORE SENDING THIS EMAIL: attach the .drmcontainer file (your " +
                  "zcrDRM agent wrote it next to the source folder) and ALSO send me " +
                  "the passphrase through a separate channel (e.g. SMS or chat — do " +
                  "NOT include it in this email)."
                : $"BEFORE SENDING THIS EMAIL: attach the file at:\n  {localPath}\n" +
                  "ALSO send me the passphrase through a separate channel (e.g. SMS " +
                  "or chat — do NOT include it in this email).";
        return
            $"Hi,\n\n" +
            $"I'm sharing a DRM-protected folder ({containerFileName}) with you through zcrDRM.\n\n" +
            $"{attachLine}\n\n" +
            $"To open the folder:\n" +
            $"1. Click this link to verify your email: {shareUrl}\n" +
            $"2. Save the .drmcontainer attachment from this email\n" +
            $"3. Double-click to open in the zcrDRM viewer\n" +
            $"4. Enter the passphrase I send you separately\n\n" +
            $"Thanks,";
    }

    private string? quickPickedFile;

    private void QuickDropZone_DragEnter(object sender, System.Windows.DragEventArgs e)
    {
        if (e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop))
        {
            e.Effects = System.Windows.DragDropEffects.Copy;
            QuickDropZone.BorderBrush = System.Windows.Media.Brushes.SteelBlue;
            QuickDropZone.Background = System.Windows.Media.Brushes.AliceBlue;
        }
        else
        {
            e.Effects = System.Windows.DragDropEffects.None;
        }
        e.Handled = true;
    }

    private void QuickDropZone_DragLeave(object sender, System.Windows.DragEventArgs e)
    {
        QuickDropZone.BorderBrush = new System.Windows.Media.SolidColorBrush(
            System.Windows.Media.Color.FromRgb(0xD1, 0xD5, 0xDB));
        QuickDropZone.Background = new System.Windows.Media.SolidColorBrush(
            System.Windows.Media.Color.FromRgb(0xFA, 0xFA, 0xFA));
    }

    private void QuickDropZone_Drop(object sender, System.Windows.DragEventArgs e)
    {
        QuickDropZone_DragLeave(sender, e);
        if (!e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop)) return;
        if (e.Data.GetData(System.Windows.DataFormats.FileDrop) is not string[] paths || paths.Length == 0) return;
        var firstFile = paths.FirstOrDefault(File.Exists);
        if (firstFile is null) { QuickResultText.Text = "Folders are not supported; drop a single file."; return; }
        quickPickedFile = firstFile;
        QuickDropFile.Text = Path.GetFileName(firstFile);
        QuickResultText.Text = string.Empty;
    }

    private void QuickBrowseButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = BuildCadOpenFileDialogFilter(),
            CheckFileExists = true,
            Multiselect = false,
            Title = "Select CAD file to protect"
        };
        if (dialog.ShowDialog(this) == true)
        {
            quickPickedFile = dialog.FileName;
            QuickDropFile.Text = Path.GetFileName(dialog.FileName);
            QuickResultText.Text = string.Empty;
        }
    }

    private async void QuickSendButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(quickPickedFile))
        {
            QuickResultText.Text = "Drop a CAD file or click Browse CAD first.";
            return;
        }
        if (!IsSupportedCadFile(quickPickedFile))
        {
            QuickResultText.Text = "This internal flow only protects CAD files.";
            return;
        }

        QuickSendButton.IsEnabled = false;
        QuickResultText.Text = "Encrypting CAD file...";
        try
        {
            var serverUrl = ParseServerUrl();
            var tenantId = ParseRequiredGuid(TenantIdBox.Text, "Tenant ID");
            var userId = ParseRequiredGuid(UserIdBox.Text, "User ID");
            var clientApiKey = ClientApiKeyBox.Password.Trim();
            if (string.IsNullOrWhiteSpace(clientApiKey))
            {
                throw new InvalidOperationException("Client API key is not configured on this machine.");
            }

            using var httpClient = new HttpClient { BaseAddress = serverUrl };
            var serverClient = new DrmServerClient(httpClient, clientApiKey);
            var inventory = new JsonProtectedFileInventory(ResolveDataPath("protected-inventory.json"));
            var keyStore = new JsonFileKeyStore(ResolveDataPath("file-keys.json"));
            var workflow = new ProtectFileWorkflow(serverClient, inventory, keyStore);

            var result = await workflow.ProtectAsync(
                new TenantId(tenantId),
                new UserId(userId),
                quickPickedFile,
                EnvelopeCrypto.GenerateKey(),
                new ProtectFilePolicyOptions(Permission.View, PolicyTemplateId: null, Recipients: []),
                deleteOriginalAfterProtection: false,
                CancellationToken.None);

            QuickResultText.Text =
                $"Protected internally: {Path.GetFileName(result.DestinationPath)}. " +
                "Opening requires an AD-joined trusted device.";
            quickPickedFile = null;
            QuickDropFile.Text = string.Empty;
        }
        catch (Exception ex)
        {
            QuickResultText.Text = $"Protect failed: {ex.Message}";
        }
        finally
        {
            QuickSendButton.IsEnabled = true;
        }
    }

    /// <summary>
    /// Reads QuickExpiryDropdown's selected item Tag (set in XAML to
    /// "168" / "720" / "2160" / "8760") and returns the matching hour
    /// count. Falls back to the default 7 days (168h) if the dropdown
    /// hasn't been initialised yet (legacy code path / unit-test
    /// constructor that doesn't run XAML).
    /// </summary>
    private int ParseQuickExpiryHours()
    {
        const int defaultHours = 168;

        if (QuickExpiryDropdown?.SelectedItem is not System.Windows.Controls.ComboBoxItem item)
        {
            return defaultHours;
        }

        if (item.Tag is string tag && int.TryParse(tag, out var hours) && hours > 0)
        {
            return hours;
        }

        return defaultHours;
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
        var args = Environment.GetCommandLineArgs();

        // The Windows Explorer right-click entry passes the selected CAD file here.
        var quickProtectPath = TryGetCommandLineValue("--quick-protect", args);
        if (!string.IsNullOrWhiteSpace(quickProtectPath) && File.Exists(quickProtectPath))
        {
            quickPickedFile = quickProtectPath;
            if (QuickDropFile != null)
            {
                QuickDropFile.Text = Path.GetFileName(quickProtectPath);
            }
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
