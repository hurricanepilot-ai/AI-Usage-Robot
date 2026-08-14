using AIUsageRobot.Shared;
using System.ComponentModel;
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

namespace AIUsageRobot.Widget;

public partial class MainWindow : Window
{
    private static readonly Brush DeepSeekActiveBrush = new SolidColorBrush(Color.FromRgb(47, 174, 255));
    private static readonly Brush GptActiveBrush = new SolidColorBrush(Color.FromRgb(255, 70, 82));
    private static readonly Brush InactiveEyeBrush = new SolidColorBrush(Color.FromRgb(8, 10, 10));
    private readonly HttpClient _http = new() { BaseAddress = new Uri(LocalAppStorage.ApiBaseUrl), Timeout = TimeSpan.FromSeconds(5) };
    private readonly DispatcherTimer _refreshTimer = new() { Interval = TimeSpan.FromSeconds(30) };
    private bool _refreshing;

    public MainWindow()
    {
        InitializeComponent();
        try { _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", LocalAppStorage.GetOrCreateApiToken()); }
        catch { }
        _refreshTimer.Tick += async (_, _) => await RefreshAsync(false);
        BuildContextMenu();
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
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        _refreshTimer.Stop();
        SavePosition();
        _http.Dispose();
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
            UpdatedText.Text = "SYNC FAIL";
        }
    }

    private void ShowProvider(bool chatGpt)
    {
        ChatGptPage.Visibility = chatGpt ? Visibility.Visible : Visibility.Collapsed;
        DeepSeekPage.Visibility = chatGpt ? Visibility.Collapsed : Visibility.Visible;
        PageTitle.Text = chatGpt ? "CODEX" : "DEEPSEEK";
        DeepSeekEyeFill.Fill = chatGpt ? InactiveEyeBrush : DeepSeekActiveBrush;
        GptEyeFill.Fill = chatGpt ? GptActiveBrush : InactiveEyeBrush;
        AnimateArm(LeftArmRotation, chatGpt ? 0 : 180);
        AnimateArm(RightArmRotation, chatGpt ? -180 : 0);
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
            ChatGptQuotaText.Text = "Offline";
            DeepSeekBalanceText.Text = "Offline";
            DeepSeekAvailabilityText.Text = "本地服务未运行";
            UpdatedText.Text = "SERVICE";
            StatusLight.Fill = Brushes.IndianRed;
        }
        finally { _refreshing = false; }
    }

    private void Render(OverviewDto overview)
    {
        RenderChatGpt(overview.ChatGPT);
        RenderDeepSeek(overview.DeepSeek);
        UpdatedText.Text = DateTime.Now.ToString("HH:mm");
        StatusLight.Fill = CombineStatus(overview.ChatGPT.Percentage.Status, overview.DeepSeek.TotalBalance.Status);
    }

    private void RenderChatGpt(ChatGptQuotaDto data)
    {
        var value = data.Percentage.Value;
        var semanticLabel = data.MetricSemantics switch { "remaining" => "剩余", "used" => "已用", _ => "" };
        var display = value is int percentage ? $"{semanticLabel}{percentage}%" : data.Percentage.Status.ToString();
        ChatGptQuotaText.Text = display;
        ChatGptQuotaBar.Value = value ?? 0;
        ChatGptModelText.Text = data.Model ?? "动态模型 · Unknown";
        ChatGptPeriodText.Text = FormatPeriod(data.Period);
        ChatGptResetText.Text = data.ResetAt?.ToLocalTime().ToString("MM/dd HH:mm") ?? "Unknown";
        if (data.Percentage.Status is DataStatus.Stale or DataStatus.Unavailable)
            ChatGptModelText.Text = $"{data.Percentage.Status.ToString().ToUpperInvariant()} · {data.Model ?? "Unknown"}";
    }

    private void RenderDeepSeek(DeepSeekBalanceDto data)
    {
        var display = data.TotalBalance.Value is decimal amount
            ? $"{(data.Currency == "CNY" ? "¥" : data.Currency + " ")}{amount:N2}"
            : data.TotalBalance.Status.ToString();
        DeepSeekBalanceText.Text = display;
        DeepSeekAvailabilityText.Text = data.TotalBalance.Message ?? (data.IsAvailable == true ? "API 可用" : "余额不可用");
    }

    private static Brush CombineStatus(DataStatus chatGpt, DataStatus deepSeek)
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
        refresh.Click += async (_, _) => await RefreshAsync(true);
        var credential = new MenuItem { Header = "设置 DeepSeek API Key…" };
        credential.Click += async (_, _) => await ShowCredentialDialogAsync();
        var topmost = new MenuItem { Header = "始终置顶", IsCheckable = true, IsChecked = true };
        topmost.Click += (_, _) => Topmost = topmost.IsChecked;
        var exit = new MenuItem { Header = "退出" };
        exit.Click += (_, _) => Close();
        menu.Items.Add(refresh); menu.Items.Add(credential);
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
