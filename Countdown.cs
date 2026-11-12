using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Knipser;

/// <summary>Kleine Zähler-Anzeige oben auf dem Hauptbildschirm während der Aufnahmeverzögerung.</summary>
internal class Countdown : Window
{
    readonly TextBlock t = new() { Foreground = Brushes.White, FontSize = 28, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };

    public Countdown()
    {
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent;
        ShowInTaskbar = false; Topmost = true; ShowActivated = false; ResizeMode = ResizeMode.NoResize;
        Width = 200; Height = 56; IsHitTestVisible = false;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = (SystemParameters.PrimaryScreenWidth - Width) / 2; Top = 12;
        Content = new Border { CornerRadius = new CornerRadius(10), Background = new SolidColorBrush(Color.FromArgb(0xE0, 0x20, 0x20, 0x20)), Child = t };
    }

    public void Set(int sec) => t.Text = $"Aufnahme in {sec} s";
}
