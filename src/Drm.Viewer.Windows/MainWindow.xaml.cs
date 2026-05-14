using System.IO;
using System.Windows;

namespace Drm.Viewer.Windows;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        PermissionText.Text = "Permissions: not loaded";
        WatermarkText.Text = "DRM Protected";
        StatusText.Text = "No document loaded.";
    }

    public void LoadPdfFromTemporaryFile(string path, string watermark, string permissions)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Temporary PDF file was not found.", path);
        }

        PermissionText.Text = permissions;
        WatermarkText.Text = watermark;
        StatusText.Text = $"Loaded protected PDF: {Path.GetFileName(path)}";
        PdfHost.Navigate(path);
    }
}
