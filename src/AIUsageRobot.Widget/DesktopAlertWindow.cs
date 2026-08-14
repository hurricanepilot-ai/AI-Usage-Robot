using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Color = System.Windows.Media.Color;
using Brushes = System.Windows.Media.Brushes;
using Cursors = System.Windows.Input.Cursors;
using HorizontalAlignment = System.Windows.HorizontalAlignment;

namespace AIUsageRobot.Widget;

public sealed class DesktopAlertWindow : Window
{
    private readonly DispatcherTimer _closeTimer = new() { Interval = TimeSpan.FromSeconds(6) };
    private readonly TranslateTransform _translation = new(28, 0);
    private bool _closing;

    public DesktopAlertWindow(string title, string message, bool warning)
    {
        Title = title;
        Width = 370;
        Height = 126;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        Topmost = true;
        ShowActivated = false;
        Opacity = 0;
        Cursor = Cursors.Hand;

        var accent = new SolidColorBrush(warning ? Color.FromRgb(255, 82, 96) : Color.FromRgb(215, 165, 45));
        var icon = new Border
        {
            Width = 42,
            Height = 42,
            CornerRadius = new CornerRadius(21),
            Background = accent,
            Child = new TextBlock
            {
                Text = warning ? "!" : "✓",
                Foreground = Brushes.White,
                FontSize = 25,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };

        var copy = new StackPanel { Margin = new Thickness(15, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
        copy.Children.Add(new TextBlock
        {
            Text = title,
            Foreground = Brushes.White,
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        copy.Children.Add(new TextBlock
        {
            Text = message,
            Foreground = new SolidColorBrush(Color.FromRgb(191, 205, 201)),
            FontSize = 12,
            Margin = new Thickness(0, 7, 0, 0),
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 260
        });

        var content = new Grid { Margin = new Thickness(18, 14, 14, 14) };
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        content.ColumnDefinitions.Add(new ColumnDefinition());
        content.Children.Add(icon);
        Grid.SetColumn(copy, 1);
        content.Children.Add(copy);

        var card = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(247, 25, 31, 32)),
            BorderBrush = accent,
            BorderThickness = new Thickness(1, 1, 5, 1),
            CornerRadius = new CornerRadius(12),
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                BlurRadius = 20,
                ShadowDepth = 4,
                Opacity = 0.45,
                Color = Colors.Black
            },
            Child = content
        };
        card.RenderTransform = _translation;
        Content = card;

        MouseLeftButtonUp += (_, _) => CloseAnimated();
        _closeTimer.Tick += (_, _) => CloseAnimated();
        Loaded += (_, _) => ShowAnimated();
        Closed += (_, _) => _closeTimer.Stop();
    }

    private void ShowAnimated()
    {
        var workArea = SystemParameters.WorkArea;
        Left = workArea.Right - Width - 18;
        Top = workArea.Bottom - Height - 18;
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)));
        _translation.BeginAnimation(TranslateTransform.XProperty,
            new DoubleAnimation(28, 0, TimeSpan.FromMilliseconds(260)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        _closeTimer.Start();
    }

    private void CloseAnimated()
    {
        if (_closing) return;
        _closing = true;
        _closeTimer.Stop();
        var fade = new DoubleAnimation(Opacity, 0, TimeSpan.FromMilliseconds(180));
        fade.Completed += (_, _) => Close();
        BeginAnimation(OpacityProperty, fade);
        _translation.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, 24, TimeSpan.FromMilliseconds(180)));
    }
}
