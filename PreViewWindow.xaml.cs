using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace PrintLogPdf3
{
    public partial class PreviewWindow : Window
    {
        public PreviewWindow(List<byte[]> images)
        {
            InitializeComponent();

            foreach (var imgBytes in images)
            {
                var bitmap = new BitmapImage();
                using var ms = new MemoryStream(imgBytes);

                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.StreamSource = ms;
                bitmap.EndInit();
                bitmap.Freeze();

                double displayWidth = 700;
                double displayHeight = displayWidth * bitmap.PixelHeight / bitmap.PixelWidth;

                PreviewPanel.Children.Add(new Image
                {
                    Source = bitmap,
                    Margin = new Thickness(10),
                    Stretch = System.Windows.Media.Stretch.Uniform,
                    Width = displayWidth,
                    Height = displayHeight
                });
            }
        }

    }
}
