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
    }

    private void BrowseButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "PDF files (*.pdf)|*.pdf",
            CheckFileExists = true,
            Multiselect = false,
            Title = "Select PDF to protect"
        };

        if (dialog.ShowDialog(this) == true)
        {
            SourcePathBox.Text = dialog.FileName;
        }
    }

    private async void ProtectButton_Click(object sender, RoutedEventArgs e)
    {
        ProtectButton.IsEnabled = false;
        SetStatus("Protecting PDF...");

        try
        {
            var serverUrl = ParseServerUrl();
            var tenantId = ParseRequiredGuid(TenantIdBox.Text, "Tenant ID");
            var userId = ParseRequiredGuid(UserIdBox.Text, "User ID");
            var sourcePath = SourcePathBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(sourcePath))
            {
                throw new InvalidOperationException("Select a PDF file before protecting.");
            }

            var clientApiKey = ClientApiKeyBox.Password.Trim();
            using var httpClient = new HttpClient { BaseAddress = serverUrl };
            var serverClient = new DrmServerClient(httpClient, clientApiKey);
            var inventory = new JsonProtectedFileInventory(ResolveDataPath("protected-inventory.json"));
            var keyStore = new JsonFileKeyStore(ResolveDataPath("file-keys.json"));
            var workflow = new ProtectPdfFileWorkflow(serverClient, inventory, keyStore);

            var result = await workflow.ProtectAsync(
                new TenantId(tenantId),
                new UserId(userId),
                sourcePath,
                EnvelopeCrypto.GenerateKey(),
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
}
