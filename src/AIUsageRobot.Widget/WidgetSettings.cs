using AIUsageRobot.Shared;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using MessageBox = System.Windows.MessageBox;
using Orientation = System.Windows.Controls.Orientation;
using TextBox = System.Windows.Controls.TextBox;

namespace AIUsageRobot.Widget;

public sealed class AlertSettings
{
    public bool Enabled { get; set; } = true;
    public int CodexRemainingThreshold { get; set; } = 20;
    public decimal DeepSeekBalanceThreshold { get; set; } = 10;

    private static string SettingsPath => Path.Combine(LocalAppStorage.RootDirectory, "widget-settings.json");

    public static AlertSettings Load()
    {
        try
        {
            var settings = JsonSerializer.Deserialize<AlertSettings>(File.ReadAllText(SettingsPath));
            if (settings is not null)
            {
                settings.CodexRemainingThreshold = Math.Clamp(settings.CodexRemainingThreshold, 1, 100);
                settings.DeepSeekBalanceThreshold = Math.Clamp(settings.DeepSeekBalanceThreshold, 0.01m, 1_000_000m);
                return settings;
            }
        }
        catch { }
        return new AlertSettings();
    }

    public void Save()
    {
        Directory.CreateDirectory(LocalAppStorage.RootDirectory);
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }
}

public sealed class AlertSettingsWindow : Window
{
    private readonly CheckBox _enabled;
    private readonly TextBox _codexThreshold;
    private readonly TextBox _deepSeekThreshold;

    public AlertSettingsWindow(Window owner, AlertSettings settings)
    {
        Owner = owner;
        Title = "额度预警设置";
        Width = 360;
        Height = 245;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        Background = System.Windows.Media.Brushes.White;

        _enabled = new CheckBox { Content = "启用 Windows 额度预警", IsChecked = settings.Enabled, Margin = new Thickness(0, 0, 0, 16) };
        _codexThreshold = new TextBox { Text = settings.CodexRemainingThreshold.ToString(CultureInfo.InvariantCulture), Width = 90 };
        _deepSeekThreshold = new TextBox { Text = settings.DeepSeekBalanceThreshold.ToString(CultureInfo.InvariantCulture), Width = 90 };

        var form = new Grid();
        form.ColumnDefinitions.Add(new ColumnDefinition());
        form.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        form.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        form.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        AddRow(form, 0, "Codex 剩余低于（%）", _codexThreshold);
        AddRow(form, 1, "DeepSeek 余额低于", _deepSeekThreshold);

        var save = new Button { Content = "保存", Width = 82, Height = 30, IsDefault = true, Margin = new Thickness(6) };
        var cancel = new Button { Content = "取消", Width = 82, Height = 30, IsCancel = true, Margin = new Thickness(6) };
        save.Click += (_, _) => Save(settings);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(save);
        buttons.Children.Add(cancel);

        var panel = new StackPanel { Margin = new Thickness(24) };
        panel.Children.Add(_enabled);
        panel.Children.Add(form);
        panel.Children.Add(new TextBlock { Text = "达到阈值时显示 Windows 托盘通知。", Foreground = System.Windows.Media.Brushes.DimGray, Margin = new Thickness(0, 14, 0, 8) });
        panel.Children.Add(buttons);
        Content = panel;
    }

    private static void AddRow(Grid grid, int row, string label, UIElement input)
    {
        var text = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 5, 20, 5) };
        Grid.SetRow(text, row);
        Grid.SetColumn(text, 0);
        Grid.SetRow(input, row);
        Grid.SetColumn(input, 1);
        grid.Children.Add(text);
        grid.Children.Add(input);
    }

    private void Save(AlertSettings settings)
    {
        if (!int.TryParse(_codexThreshold.Text, out var codex) || codex is < 1 or > 100 ||
            !decimal.TryParse(_deepSeekThreshold.Text, NumberStyles.Number, CultureInfo.InvariantCulture, out var deepSeek) || deepSeek <= 0)
        {
            MessageBox.Show(this, "请输入有效阈值：Codex 为 1–100，DeepSeek 必须大于 0。", "额度预警设置", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        settings.Enabled = _enabled.IsChecked == true;
        settings.CodexRemainingThreshold = codex;
        settings.DeepSeekBalanceThreshold = deepSeek;
        settings.Save();
        DialogResult = true;
    }
}
