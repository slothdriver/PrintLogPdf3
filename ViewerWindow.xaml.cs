using Microsoft.Web.WebView2.Core;
using System;
using System.Windows;
using System.IO;

namespace PrintLogPdf3
{
    public partial class ViewerWindow : Window
    {
        private readonly byte[] _pdfBytes;

        public ViewerWindow(byte[] pdfBytes)
        {
            InitializeComponent();
            _pdfBytes = pdfBytes;
            Loaded += ViewerWindow_Loaded;
        }

        private async void ViewerWindow_Loaded(object sender, RoutedEventArgs e)
        {
            await webView.EnsureCoreWebView2Async();

            var tempPath = Path.Combine(
                Path.GetTempPath(),
                $"preview_{Guid.NewGuid()}.pdf");

            await File.WriteAllBytesAsync(tempPath, _pdfBytes);

            webView.Source = new Uri(tempPath);
        }

    }
}
