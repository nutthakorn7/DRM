using System.Collections.ObjectModel;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;

namespace Drm.Viewer.Windows;

public partial class DrmExplorerWindow : Window
{
    private readonly Uri serverUrl;
    private readonly Guid tenantId;
    private readonly string adminKey;
    private readonly ObservableCollection<DrmExplorerFile> allFiles = new();
    private readonly ObservableCollection<DrmExplorerFile> visibleFiles = new();

    public DrmExplorerWindow(Uri serverUrl, Guid tenantId, string adminKey)
    {
        this.serverUrl = serverUrl;
        this.tenantId = tenantId;
        this.adminKey = adminKey;
        InitializeComponent();
        FilesGrid.ItemsSource = visibleFiles;
        Loaded += async (_, _) => await LoadFilesAsync();
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        await LoadFilesAsync();
    }

    private void FilterBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        ApplyFilter();
    }

    private async Task LoadFilesAsync()
    {
        StatusText.Text = "Loading…";
        RefreshButton.IsEnabled = false;
        try
        {
            using var http = new HttpClient { BaseAddress = serverUrl };
            http.DefaultRequestHeaders.TryAddWithoutValidation("X-DRM-Admin-Key", adminKey);
            using var response = await http.GetAsync(
                $"/api/admin/files?tenantId={tenantId}&q=");
            if (!response.IsSuccessStatusCode)
            {
                StatusText.Text = $"Server returned HTTP {(int)response.StatusCode}.";
                return;
            }
            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            allFiles.Clear();
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                allFiles.Add(new DrmExplorerFile
                {
                    FileId = TryGetString(item, "fileId"),
                    ContentType = TryGetString(item, "contentType"),
                    Permissions = TryGetString(item, "permissions"),
                    OwnerUserId = TryGetString(item, "ownerUserId"),
                    ExpiresAtUtc = TryGetString(item, "expiresAtUtc"),
                    Revoked = item.TryGetProperty("revoked", out var r) && r.GetBoolean()
                });
            }
            ApplyFilter();
            StatusText.Text = $"{allFiles.Count} file{(allFiles.Count == 1 ? "" : "s")} · filtered to {visibleFiles.Count}.";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Load failed: {ex.Message}";
        }
        finally
        {
            RefreshButton.IsEnabled = true;
        }
    }

    private void ApplyFilter()
    {
        var q = (FilterBox?.Text ?? string.Empty).Trim().ToLowerInvariant();
        visibleFiles.Clear();
        foreach (var file in allFiles)
        {
            if (string.IsNullOrEmpty(q) || file.ContainsAnywhere(q))
            {
                visibleFiles.Add(file);
            }
        }
        if (StatusText != null)
        {
            StatusText.Text = $"{allFiles.Count} file{(allFiles.Count == 1 ? "" : "s")} · filtered to {visibleFiles.Count}.";
        }
    }

    private static string TryGetString(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out var v) ? (v.GetString() ?? string.Empty) : string.Empty;
    }
}

public sealed class DrmExplorerFile
{
    public string FileId { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public string Permissions { get; set; } = string.Empty;
    public string OwnerUserId { get; set; } = string.Empty;
    public string ExpiresAtUtc { get; set; } = string.Empty;
    public bool Revoked { get; set; }

    public bool ContainsAnywhere(string lowercaseNeedle)
    {
        return FileId.ToLowerInvariant().Contains(lowercaseNeedle)
            || ContentType.ToLowerInvariant().Contains(lowercaseNeedle)
            || Permissions.ToLowerInvariant().Contains(lowercaseNeedle)
            || OwnerUserId.ToLowerInvariant().Contains(lowercaseNeedle);
    }
}
