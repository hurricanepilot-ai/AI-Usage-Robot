using AIUsageRobot.Shared;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Button = System.Windows.Controls.Button;
using Color = System.Windows.Media.Color;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using MessageBox = System.Windows.MessageBox;
using Orientation = System.Windows.Controls.Orientation;

namespace AIUsageRobot.Widget;

public partial class MainWindow : Window
{
    private static readonly System.Windows.Media.Brush DeepSeekActiveBrush = new SolidColorBrush(Color.FromRgb(47, 174, 255));
    private static readonly System.Windows.Media.Brush GptActiveBrush = new SolidColorBrush(Color.FromRgb(255, 70, 82));
    private static readonly System.Windows.Media.Brush InactiveEyeBrush = new SolidColorBrush(Color.FromRgb(8, 10, 10));
    private readonly HttpClient _http = new() { BaseAddress = new Uri(LocalAppStorage.ApiBaseUrl), Timeout = TimeSpan.FromSeconds(5) };
    private readonly DispatcherTimer _refreshTimer = new() { Interval = TimeSpan.FromSeconds(30) };
    private readonly System.Windows.Forms.NotifyIcon _trayIcon;
    private readonly AlertSettings _alertSettings = AlertSettings.Load();
    private bool _refreshing;
    private bool _serviceStartAttempted;
    private Process? _serviceProcess;
    private int _codexNotificationLevel;
    private int _deepSeekNotificationLevel;
    private OverviewDto? _lastOverview;
    private TrendWindow? _trendWindow;
    private DesktopAlertWindow? _desktopAlert;
    private bool _showingChatGpt;

    public MainWindow()
    {
        InitializeComponent();
        try { _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", LocalAppStorage.GetOrCreateApiToken()); }
        catch { }
        _refreshTimer.Tick += async (_, _) => await RefreshAsync(false);
        BuildContextMenu();
        _trayIcon = BuildTrayIcon();
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        RestorePosition();
        _refreshTimer.Start();
        var startWithCodex = Environment.GetCommandLineArgs()
            .Any(argument => string.Equals(argument, "--codex", StringComparison.OrdinalIgnoreCase));
        ShowProvider(chatGpt: startWithCodex);
        if (startWithCodex)
            await SyncSelectedProviderAsync(chatGpt: true);
        else
            await RefreshAsync(false);
        if (Environment.GetCommandLineArgs().Any(argument => string.Equals(argument, "--trend", StringComparison.OrdinalIgnoreCase)))
            OpenTrendWindow();
        if (Environment.GetCommandLineArgs().Any(argument => string.Equals(argument, "--test-alert", StringComparison.OrdinalIgnoreCase)))
            TestAlert();
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        _refreshTimer.Stop();
        SavePosition();
        _http.Dispose();
        StopOwnedService();
        var trayIcon = _trayIcon.Icon;
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        trayIcon?.Dispose();
    }

    private void Robot_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (ScreenFrame.IsMouseOver || DeepSeekEyeButton.IsMouseOver || GptEyeButton.IsMouseOver) return;
        if (e.ClickCount == 1 && e.LeftButton == MouseButtonState.Pressed) DragMove();
    }

    private async void DeepSeekEyeButton_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        ShowProvider(chatGpt: false);
        await SyncSelectedProviderAsync(chatGpt: false);
    }

    private async void GptEyeButton_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        ShowProvider(chatGpt: true);
        await SyncSelectedProviderAsync(chatGpt: true);
    }

    private async Task SyncSelectedProviderAsync(bool chatGpt)
    {
        UpdatedText.Text = "SYNC…";
        try
        {
            var endpoint = chatGpt ? "api/codex/refresh" : "api/deepseek/refresh";
            using var response = await _http.PostAsync(endpoint, null);
            response.EnsureSuccessStatusCode();
            await RefreshAsync(false);
        }
        catch
        {
            await EnsureServiceStartedAsync();
            try
            {
                var endpoint = chatGpt ? "api/codex/refresh" : "api/deepseek/refresh";
                using var retry = await _http.PostAsync(endpoint, null);
                retry.EnsureSuccessStatusCode();
                await RefreshAsync(false);
            }
            catch { UpdatedText.Text = "SYNC FAIL"; }
        }
    }

    private void ShowProvider(bool chatGpt)
    {
        _showingChatGpt = chatGpt;
        ChatGptPage.Visibility = chatGpt ? Visibility.Visible : Visibility.Collapsed;
        DeepSeekPage.Visibility = chatGpt ? Visibility.Collapsed : Visibility.Visible;
        PageTitle.Text = chatGpt ? "CODEX" : "DEEPSEEK";
        DeepSeekEyeFill.Fill = chatGpt ? InactiveEyeBrush : DeepSeekActiveBrush;
        GptEyeFill.Fill = chatGpt ? GptActiveBrush : InactiveEyeBrush;
        AnimateArm(LeftArmRotation, chatGpt ? 0 : 180);
        AnimateArm(RightArmRotation, chatGpt ? -180 : 0);
        UpdateResetText();
    }

    private static void AnimateArm(RotateTransform transform, double targetAngle)
    {
        var animation = new DoubleAnimation
        {
            To = targetAngle,
            Duration = TimeSpan.FromMilliseconds(260),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.HoldEnd
        };
        transform.BeginAnimation(RotateTransform.AngleProperty, animation, HandoffBehavior.SnapshotAndReplace);
    }

    private async Task RefreshAsync(bool refreshDeepSeek)
    {
        if (_refreshing) return;
        _refreshing = true;
        try
        {
            if (refreshDeepSeek) await _http.PostAsync("api/deepseek/refresh", null);
            var overview = await _http.GetFromJsonAsync<OverviewDto>("api/overview", new JsonSerializerOptions(JsonSerializerDefaults.Web));
            if (overview is not null) Render(overview);
        }
        catch
        {
            await EnsureServiceStartedAsync();
            ChatGptQuotaText.Text = "Offline";
            DeepSeekBalanceText.Text = "Offline";
            ChatGptQuotaBar.Value = 0;
            DeepSeekBalanceBar.IsIndeterminate = false;
            DeepSeekBalanceBar.Value = 0;
            UpdatedText.Text = "SERVICE";
            StatusLight.Fill = Brushes.IndianRed;
        }
        finally { _refreshing = false; }
    }

    private void Render(OverviewDto overview)
    {
        _lastOverview = overview;
        RenderChatGpt(overview.ChatGPT);
        RenderDeepSeek(overview.DeepSeek);
        EvaluateNotifications(overview);
        UpdateResetText();
        StatusLight.Fill = CombineStatus(overview.ChatGPT.Percentage.Status, overview.DeepSeek.TotalBalance.Status);
        _trendWindow?.UpdateOverview(overview);
    }

    private void RenderChatGpt(ChatGptQuotaDto data)
    {
        var value = data.Percentage.Value;
        var semanticLabel = data.MetricSemantics switch { "remaining" => "剩余", "used" => "已用", _ => "" };
        var display = value is int percentage ? $"{semanticLabel}{percentage}%" : data.Percentage.Status.ToString();
        ChatGptQuotaText.Text = display;
        ChatGptQuotaBar.Value = value ?? 0;
    }

    private void RenderDeepSeek(DeepSeekBalanceDto data)
    {
        var display = data.TotalBalance.Value is decimal amount
            ? $"{(data.Currency == "CNY" ? "¥" : data.Currency + " ")}{amount:N2}"
            : data.TotalBalance.Status.ToString();
        DeepSeekBalanceText.Text = display;
        DeepSeekBalanceBar.IsIndeterminate = data.TotalBalance.Status == DataStatus.Fresh;
        DeepSeekBalanceBar.Value = data.TotalBalance.Status == DataStatus.Fresh ? 100
            : data.TotalBalance.Status == DataStatus.Stale ? 50 : 0;
    }

    private void UpdateResetText()
    {
        if (!_showingChatGpt)
        {
            UpdatedText.Text = "余额不重置";
            return;
        }

        var resetAt = _lastOverview?.ChatGPT.ResetAt;
        UpdatedText.Text = resetAt is null
            ? "重置时间未知"
            : $"下次重置 {resetAt.Value.ToLocalTime():MM/dd HH:mm}";
    }

    private static System.Windows.Media.Brush CombineStatus(DataStatus chatGpt, DataStatus deepSeek)
    {
        var statuses = new[] { chatGpt, deepSeek };
        if (statuses.Contains(DataStatus.Offline) || statuses.Contains(DataStatus.AuthError)) return Brushes.IndianRed;
        if (statuses.Contains(DataStatus.Stale) || statuses.Contains(DataStatus.Unavailable)) return Brushes.Gold;
        if (statuses.All(x => x == DataStatus.Fresh)) return Brushes.SpringGreen;
        return Brushes.Gray;
    }

    private static string FormatPeriod(string? period)
    {
        if (string.IsNullOrWhiteSpace(period)) return "Unknown";
        var parts = period.Split('_', 2);
        if (parts.Length != 2) return period;
        return $"{parts[0]} {parts[1] switch { "minutes" => "分钟", "hours" => "小时", "days" => "天", "weeks" => "周", "months" => "月", _ => parts[1] }}";
    }

    private static long PeriodMinutes(string? period)
    {
        var parts = period?.Split('_', 2);
        if (parts is not { Length: 2 } || !long.TryParse(parts[0], out var value)) return long.MaxValue;
        return parts[1] switch { "minutes" => value, "hours" => value * 60, "days" => value * 1_440, "weeks" => value * 10_080, _ => long.MaxValue };
    }

    private System.Windows.Forms.NotifyIcon BuildTrayIcon()
    {
        var executableIcon = !string.IsNullOrWhiteSpace(Environment.ProcessPath)
            ? System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath)
            : null;
        var tray = new System.Windows.Forms.NotifyIcon
        {
            Icon = executableIcon ?? System.Drawing.SystemIcons.Application,
            Text = "AI Usage Robot",
            Visible = true
        };
        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("显示机器人", null, (_, _) => Dispatcher.Invoke(() => { Show(); Activate(); }));
        menu.Items.Add("查看详情", null, (_, _) => Dispatcher.Invoke(ShowDetails));
        menu.Items.Add("立即同步全部", null, (_, _) => Dispatcher.Invoke(() => _ = SyncAllAsync()));
        menu.Items.Add("测试额度预警", null, (_, _) => Dispatcher.Invoke(TestAlert));
        menu.Items.Add("预警设置", null, (_, _) => Dispatcher.Invoke(ShowAlertSettings));
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) => Dispatcher.Invoke(Close));
        tray.ContextMenuStrip = menu;
        tray.DoubleClick += (_, _) => Dispatcher.Invoke(() => { Show(); Activate(); });
        return tray;
    }

    private async Task SyncAllAsync()
    {
        try
        {
            await Task.WhenAll(
                _http.PostAsync("api/deepseek/refresh", null),
                _http.PostAsync("api/codex/refresh", null));
            await RefreshAsync(false);
        }
        catch { UpdatedText.Text = "SYNC FAIL"; }
    }

    private void EvaluateNotifications(OverviewDto overview)
    {
        if (!_alertSettings.Enabled)
        {
            _codexNotificationLevel = 0;
            _deepSeekNotificationLevel = 0;
            return;
        }
        var codexRemaining = overview.ChatGPT.Windows?.Min(window => window.RemainingPercentage.Value) ?? overview.ChatGPT.Percentage.Value;
        var criticalCodex = Math.Max(5, _alertSettings.CodexRemainingThreshold / 2);
        var codexLevel = !codexRemaining.HasValue ? 0
            : codexRemaining.Value <= criticalCodex ? 2
            : codexRemaining.Value <= _alertSettings.CodexRemainingThreshold ? 1 : 0;
        if (codexLevel > _codexNotificationLevel && codexRemaining is int codexValue)
            ShowBalloon("Codex 配额提醒", $"当前最低周期仅剩 {codexValue}%", System.Windows.Forms.ToolTipIcon.Warning);
        _codexNotificationLevel = codexLevel;

        var balance = overview.DeepSeek.TotalBalance.Value;
        var deepSeekLevel = !balance.HasValue ? 0
            : balance.Value <= _alertSettings.DeepSeekBalanceThreshold / 2 ? 2
            : balance.Value <= _alertSettings.DeepSeekBalanceThreshold ? 1 : 0;
        if (deepSeekLevel > _deepSeekNotificationLevel && balance is decimal amount)
            ShowBalloon("DeepSeek 余额提醒", $"当前余额 {overview.DeepSeek.Currency} {amount:N2}", System.Windows.Forms.ToolTipIcon.Warning);
        _deepSeekNotificationLevel = deepSeekLevel;
    }

    private void ShowBalloon(string title, string message, System.Windows.Forms.ToolTipIcon icon)
    {
        _desktopAlert?.Close();
        _desktopAlert = new DesktopAlertWindow(title, message, icon == System.Windows.Forms.ToolTipIcon.Warning);
        _desktopAlert.Closed += (_, _) => _desktopAlert = null;
        _desktopAlert.Show();

        _trayIcon.BalloonTipTitle = title;
        _trayIcon.BalloonTipText = message;
        _trayIcon.BalloonTipIcon = icon;
        _trayIcon.ShowBalloonTip(5000);
    }

    private void TestAlert() => ShowBalloon(
        "AI Usage Robot 测试预警",
        $"Windows 额度预警正常。Codex 阈值 {_alertSettings.CodexRemainingThreshold}%，DeepSeek 阈值 {_alertSettings.DeepSeekBalanceThreshold:N2}。",
        System.Windows.Forms.ToolTipIcon.Info);

    private void ShowAlertSettings()
    {
        var dialog = new AlertSettingsWindow(this, _alertSettings);
        if (dialog.ShowDialog() != true) return;
        _codexNotificationLevel = 0;
        _deepSeekNotificationLevel = 0;
        if (_lastOverview is not null) EvaluateNotifications(_lastOverview);
    }

    private void ScreenFrame_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        OpenTrendWindow();
    }

    private void OpenTrendWindow()
    {
        if (_trendWindow is { IsLoaded: true } && _trendWindow.IsCodex == _showingChatGpt)
        {
            _trendWindow.Activate();
            return;
        }
        _trendWindow?.Close();
        _trendWindow = new TrendWindow(this, _showingChatGpt, _alertSettings, TestAlert, ShowAlertSettings);
        _trendWindow.Closed += (_, _) => _trendWindow = null;
        if (_lastOverview is not null) _trendWindow.UpdateOverview(_lastOverview);
        _trendWindow.Show();
    }

    private void ShowDetails()
    {
        if (_lastOverview is null) return;
        var codex = _lastOverview.ChatGPT;
        var orderedWindows = codex.Windows?.OrderBy(window => PeriodMinutes(window.Period)).ToArray();
        var windows = orderedWindows is { Length: > 0 }
            ? string.Join(Environment.NewLine, orderedWindows.Select((window, index) =>
                $"{(orderedWindows.Length == 1 ? "配额" : index == 0 ? "短周期" : "长周期")}: {window.RemainingPercentage.Value?.ToString() ?? "--"}% · {FormatPeriod(window.Period)} · 重置 {window.ResetAt?.ToLocalTime():MM/dd HH:mm}"))
            : "暂无配额窗口";
        var usage = codex.Usage is null
            ? "暂无 token 使用统计"
            : $"累计 tokens: {codex.Usage.LifetimeTokens:N0}\n峰值日: {codex.Usage.PeakDailyTokens:N0}\n当前连续使用: {codex.Usage.CurrentStreakDays ?? 0} 天";
        var deepSeek = _lastOverview.DeepSeek.TotalBalance.Value is decimal balance
            ? $"{_lastOverview.DeepSeek.Currency} {balance:N2}"
            : _lastOverview.DeepSeek.TotalBalance.Status.ToString();
        MessageBox.Show(this, $"Codex\n{windows}\n\n{usage}\n\nDeepSeek\n余额: {deepSeek}", "AI Usage Robot 详情",
            MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private async Task EnsureServiceStartedAsync()
    {
        if (_serviceStartAttempted) return;
        _serviceStartAttempted = true;
        var executable = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executable) || !File.Exists(executable)) return;
        var startInfo = new ProcessStartInfo(executable)
        {
            ArgumentList = { "--service", "--parent-pid", Environment.ProcessId.ToString() }
        };
        startInfo.UseShellExecute = false;
        startInfo.CreateNoWindow = true;
        startInfo.WindowStyle = ProcessWindowStyle.Hidden;
        try
        {
            _serviceProcess = Process.Start(startInfo);
            await Task.Delay(1500);
        }
        catch { }
    }

    private void StopOwnedService()
    {
        try
        {
            if (_serviceProcess is { HasExited: false })
                _serviceProcess.Kill(entireProcessTree: true);
        }
        catch { }
        finally
        {
            _serviceProcess?.Dispose();
            _serviceProcess = null;
        }
    }

    private void BuildContextMenu()
    {
        var menu = new ContextMenu();
        var deepSeekPage = new MenuItem { Header = "DeepSeek" };
        deepSeekPage.Click += (_, _) => ShowProvider(chatGpt: false);
        var chatGptPage = new MenuItem { Header = "Codex" };
        chatGptPage.Click += (_, _) => ShowProvider(chatGpt: true);
        menu.Items.Add(deepSeekPage);
        menu.Items.Add(chatGptPage);
        menu.Items.Add(new Separator());
        var refresh = new MenuItem { Header = "立即刷新" };
        refresh.Click += async (_, _) => await SyncAllAsync();
        var details = new MenuItem { Header = "查看详情" };
        details.Click += (_, _) => ShowDetails();
        var testAlert = new MenuItem { Header = "测试 Windows 额度预警" };
        testAlert.Click += (_, _) => TestAlert();
        var alertSettings = new MenuItem { Header = "额度预警设置…" };
        alertSettings.Click += (_, _) => ShowAlertSettings();
        var credential = new MenuItem { Header = "设置 DeepSeek API Key…" };
        credential.Click += async (_, _) => await ShowCredentialDialogAsync();
        var topmost = new MenuItem { Header = "始终置顶", IsCheckable = true, IsChecked = true };
        topmost.Click += (_, _) => Topmost = topmost.IsChecked;
        var exit = new MenuItem { Header = "退出" };
        exit.Click += (_, _) => Close();
        menu.Items.Add(refresh); menu.Items.Add(details); menu.Items.Add(testAlert); menu.Items.Add(alertSettings); menu.Items.Add(credential);
        menu.Items.Add(new Separator()); menu.Items.Add(topmost); menu.Items.Add(new Separator()); menu.Items.Add(exit);
        ContextMenu = menu;
    }

    private async Task ShowCredentialDialogAsync()
    {
        var box = new PasswordBox { Margin = new Thickness(18, 8, 18, 10), MinWidth = 330 };
        var save = new Button { Content = "保存并测试", Width = 110, Height = 30, IsDefault = true, Margin = new Thickness(6) };
        var cancel = new Button { Content = "取消", Width = 80, Height = 30, IsCancel = true, Margin = new Thickness(6) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(save); buttons.Children.Add(cancel);
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = "API Key 仅保存到 Windows 凭据管理器", Margin = new Thickness(18, 16, 18, 2) });
        panel.Children.Add(box); panel.Children.Add(buttons);
        var dialog = new Window { Title = "DeepSeek 设置", Content = panel, SizeToContent = SizeToContent.WidthAndHeight, WindowStartupLocation = WindowStartupLocation.CenterOwner, Owner = this, ResizeMode = ResizeMode.NoResize };
        save.Click += (_, _) => { if (!string.IsNullOrWhiteSpace(box.Password)) dialog.DialogResult = true; };
        if (dialog.ShowDialog() != true) return;
        try
        {
            using var response = await _http.PutAsJsonAsync("api/deepseek/credential", new SaveCredentialRequest(box.Password));
            response.EnsureSuccessStatusCode();
            await RefreshAsync(false);
        }
        catch (Exception ex) { MessageBox.Show(this, $"保存或连接测试失败：{ex.Message}", "AI Usage Robot", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    private static string PositionPath => Path.Combine(LocalAppStorage.RootDirectory, "widget-position.json");
    private void SavePosition()
    {
        try { Directory.CreateDirectory(LocalAppStorage.RootDirectory); File.WriteAllText(PositionPath, JsonSerializer.Serialize(new { Left, Top })); }
        catch { }
    }

    private void RestorePosition()
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(PositionPath));
            var left = document.RootElement.GetProperty("Left").GetDouble();
            var top = document.RootElement.GetProperty("Top").GetDouble();
            if (left >= SystemParameters.VirtualScreenLeft && left + Width <= SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth &&
                top >= SystemParameters.VirtualScreenTop && top + Height <= SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight)
            { Left = left; Top = top; }
        }
        catch { WindowStartupLocation = WindowStartupLocation.CenterScreen; }
    }
}
