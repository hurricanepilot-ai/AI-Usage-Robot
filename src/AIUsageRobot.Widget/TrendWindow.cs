using AIUsageRobot.Shared;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Brushes = System.Windows.Media.Brushes;
using Button = System.Windows.Controls.Button;
using Color = System.Windows.Media.Color;
using Orientation = System.Windows.Controls.Orientation;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using FlowDirection = System.Windows.FlowDirection;
using Pen = System.Windows.Media.Pen;
using Point = System.Windows.Point;

namespace AIUsageRobot.Widget;

public sealed class TrendWindow : Window
{
    private readonly bool _codex;
    private readonly AlertSettings _settings;
    private readonly Action _testAlert;
    private readonly Action _showSettings;
    private readonly HttpClient _http = new() { BaseAddress = new Uri(LocalAppStorage.ApiBaseUrl), Timeout = TimeSpan.FromSeconds(8) };
    private readonly SevenDayChart _chart = new();
    private readonly TextBlock _headline = new();
    private readonly TextBlock _summary = new();
    private readonly TextBlock _alertStatus = new();
    private OverviewDto? _overview;
    public bool IsCodex => _codex;

    public TrendWindow(Window owner, bool codex, AlertSettings settings, Action testAlert, Action showSettings)
    {
        Owner = owner;
        _codex = codex;
        _settings = settings;
        _testAlert = testAlert;
        _showSettings = showSettings;
        Title = $"{(codex ? "Codex" : "DeepSeek")} · 七日趋势";
        Width = 620;
        Height = 450;
        MinWidth = 540;
        MinHeight = 400;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(20, 25, 26));
        Foreground = Brushes.White;

        try { _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", LocalAppStorage.GetOrCreateApiToken()); }
        catch { }

        _headline.FontSize = 25;
        _headline.FontWeight = FontWeights.Bold;
        _headline.Foreground = codex ? new SolidColorBrush(Color.FromRgb(255, 92, 104)) : new SolidColorBrush(Color.FromRgb(74, 188, 255));
        _summary.Foreground = new SolidColorBrush(Color.FromRgb(190, 205, 201));
        _summary.Margin = new Thickness(0, 5, 0, 0);
        _alertStatus.Foreground = new SolidColorBrush(Color.FromRgb(240, 201, 92));
        _alertStatus.VerticalAlignment = VerticalAlignment.Center;

        var refresh = MakeButton("立即同步");
        refresh.Click += async (_, _) => await RefreshAsync(forceSync: true);
        var alert = MakeButton("测试 Windows 预警");
        alert.Click += (_, _) => _testAlert();
        var settingsButton = MakeButton("预警设置");
        settingsButton.Click += (_, _) => { _showSettings(); UpdateAlertStatus(); };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(refresh);
        buttons.Children.Add(alert);
        buttons.Children.Add(settingsButton);

        var footer = new Grid { Margin = new Thickness(0, 14, 0, 0) };
        footer.ColumnDefinitions.Add(new ColumnDefinition());
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        footer.Children.Add(_alertStatus);
        Grid.SetColumn(buttons, 1);
        footer.Children.Add(buttons);

        var panel = new Grid { Margin = new Thickness(26, 22, 26, 20) };
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.RowDefinitions.Add(new RowDefinition());
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.Children.Add(_headline);
        Grid.SetRow(_summary, 1);
        panel.Children.Add(_summary);
        Grid.SetRow(_chart, 2);
        panel.Children.Add(_chart);
        Grid.SetRow(footer, 3);
        panel.Children.Add(footer);
        Content = panel;
        Closed += (_, _) => _http.Dispose();
        Loaded += async (_, _) => await RefreshAsync(forceSync: false);
        UpdateAlertStatus();
    }

    public void UpdateOverview(OverviewDto overview)
    {
        _overview = overview;
        Render();
    }

    private async Task RefreshAsync(bool forceSync)
    {
        try
        {
            if (forceSync)
            {
                var endpoint = _codex ? "api/codex/refresh" : "api/deepseek/refresh";
                using var response = await _http.PostAsync(endpoint, null);
                response.EnsureSuccessStatusCode();
            }
            _overview = await _http.GetFromJsonAsync<OverviewDto>("api/overview", new JsonSerializerOptions(JsonSerializerDefaults.Web));
            Render();
        }
        catch (Exception exception)
        {
            _headline.Text = "趋势暂不可用";
            _summary.Text = exception.Message;
        }
    }

    private void Render()
    {
        if (_overview is null) return;
        if (_codex) RenderCodex(_overview.ChatGPT);
        else _ = RenderDeepSeekAsync(_overview.DeepSeek);
        UpdateAlertStatus();
    }

    private void RenderCodex(ChatGptQuotaDto codex)
    {
        var remaining = codex.Windows?.Min(window => window.RemainingPercentage.Value) ?? codex.Percentage.Value;
        _headline.Text = remaining is int value ? $"剩余 {value}%" : "Codex 配额未知";
        _summary.Text = $"近七日 Token 使用 · 累计 {codex.Usage?.LifetimeTokens:N0}";
        var usage = codex.Usage?.DailyUsage.ToDictionary(item => item.StartDate, item => (double)item.Tokens) ?? [];
        var points = LastSevenDays().Select(date => new TrendPoint(date, usage.GetValueOrDefault(date))).ToArray();
        _chart.SetPoints(points, "tokens", Color.FromRgb(255, 82, 96), bars: true);
    }

    private async Task RenderDeepSeekAsync(DeepSeekBalanceDto deepSeek)
    {
        try
        {
            var trend = await _http.GetFromJsonAsync<DeepSeekUsageTrendDto>(
                "api/deepseek/usage/daily?days=7",
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
            var today = trend?.Days.LastOrDefault();
            _headline.Text = today is not null
                ? $"今日使用 {today.Currency} {today.AmountUsed:N2}"
                : "DeepSeek 使用量未知";
            var balanceText = deepSeek.TotalBalance.Value is decimal balance
                ? $"当前余额 {deepSeek.Currency} {balance:N2}"
                : "当前余额未知";
            var coverage = trend?.HistoryStartedAt is DateTimeOffset startedAt
                ? FormatCoverage(DateTimeOffset.UtcNow - startedAt)
                : "尚无历史";
            _summary.Text = $"{balanceText} · 已记录 {coverage} · 由余额下降推导";
            var points = trend?.Days.Select(day => new TrendPoint(
                day.Date,
                day.HasData ? (double)day.AmountUsed : double.NaN)).ToArray() ?? [];
            _chart.SetPoints(points, deepSeek.Currency, Color.FromRgb(61, 174, 255), bars: true);
        }
        catch (Exception exception)
        {
            _headline.Text = "DeepSeek 使用量暂不可用";
            _summary.Text = exception.Message;
            _chart.SetPoints([], deepSeek.Currency, Color.FromRgb(61, 174, 255), bars: true);
        }
    }

    private static string FormatCoverage(TimeSpan age) => age.TotalDays >= 1
        ? $"{Math.Max(1, (int)age.TotalDays)} 天"
        : $"{Math.Max(1, (int)age.TotalHours)} 小时";

    private void UpdateAlertStatus()
    {
        _alertStatus.Text = _settings.Enabled
            ? $"● 预警已启用  Codex ≤ {_settings.CodexRemainingThreshold}% · DeepSeek ≤ {_settings.DeepSeekBalanceThreshold:N2}"
            : "○ Windows 额度预警已关闭";
    }

    private static IEnumerable<DateOnly> LastSevenDays()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        for (var day = 6; day >= 0; day--) yield return today.AddDays(-day);
    }

    private static Button MakeButton(string text) => new()
    {
        Content = text,
        Height = 31,
        Padding = new Thickness(12, 0, 12, 0),
        Margin = new Thickness(7, 0, 0, 0),
        Background = new SolidColorBrush(Color.FromRgb(215, 165, 45)),
        Foreground = new SolidColorBrush(Color.FromRgb(23, 28, 29)),
        BorderThickness = new Thickness(0),
        FontWeight = FontWeights.SemiBold,
        Cursor = System.Windows.Input.Cursors.Hand
    };
}

public sealed record TrendPoint(DateOnly Date, double Value);

public sealed class SevenDayChart : FrameworkElement
{
    private IReadOnlyList<TrendPoint> _points = [];
    private string _unit = "";
    private Color _color = Colors.White;
    private bool _bars;

    public SevenDayChart()
    {
        MinHeight = 210;
        Margin = new Thickness(0, 18, 0, 0);
    }

    public void SetPoints(IReadOnlyList<TrendPoint> points, string unit, Color color, bool bars)
    {
        _points = points;
        _unit = unit;
        _color = color;
        _bars = bars;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        var width = ActualWidth;
        var height = ActualHeight;
        if (width <= 0 || height <= 0) return;
        var plot = new Rect(48, 15, Math.Max(1, width - 65), Math.Max(1, height - 54));
        var gridPen = new Pen(new SolidColorBrush(Color.FromRgb(58, 68, 68)), 1);
        var textBrush = new SolidColorBrush(Color.FromRgb(166, 180, 177));
        for (var row = 0; row <= 4; row++)
        {
            var y = plot.Top + plot.Height * row / 4;
            drawingContext.DrawLine(gridPen, new Point(plot.Left, y), new Point(plot.Right, y));
        }
        if (_points.Count == 0) return;

        var valid = _points.Where(point => !double.IsNaN(point.Value)).ToArray();
        var max = valid.Length == 0 ? 1 : Math.Max(1, valid.Max(point => point.Value));
        var min = _bars || valid.Length == 0 ? 0 : valid.Min(point => point.Value);
        if (Math.Abs(max - min) < 0.001) min = Math.Max(0, max * 0.9);
        var accent = new SolidColorBrush(_color);
        var accentPen = new Pen(accent, 3);
        var typeface = new Typeface("Segoe UI");
        var pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        Point? previous = null;

        for (var index = 0; index < _points.Count; index++)
        {
            var point = _points[index];
            var x = plot.Left + plot.Width * (index + 0.5) / _points.Count;
            var dateText = new FormattedText(point.Date.ToString("MM/dd", CultureInfo.InvariantCulture), CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight, typeface, 11, textBrush, pixelsPerDip);
            drawingContext.DrawText(dateText, new Point(x - dateText.Width / 2, plot.Bottom + 8));
            if (double.IsNaN(point.Value)) { previous = null; continue; }
            var ratio = (point.Value - min) / Math.Max(0.0001, max - min);
            var y = plot.Bottom - ratio * (plot.Height - 10);
            if (_bars)
            {
                var barWidth = Math.Min(42, plot.Width / _points.Count * 0.58);
                drawingContext.DrawRoundedRectangle(accent, null, new Rect(x - barWidth / 2, y, barWidth, plot.Bottom - y), 4, 4);
            }
            else
            {
                var current = new Point(x, y);
                if (previous is Point last) drawingContext.DrawLine(accentPen, last, current);
                drawingContext.DrawEllipse(accent, new Pen(Brushes.White, 1.5), current, 4.5, 4.5);
                previous = current;
            }
        }

        var maxText = new FormattedText(FormatValue(max), CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface, 10, textBrush, pixelsPerDip);
        drawingContext.DrawText(maxText, new Point(0, plot.Top - 2));
        var unitText = new FormattedText(_unit, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface, 10, textBrush, pixelsPerDip);
        drawingContext.DrawText(unitText, new Point(plot.Right - unitText.Width, 0));
    }

    private static string FormatValue(double value) => value switch
    {
        >= 1_000_000 => $"{value / 1_000_000:0.#}M",
        >= 1_000 => $"{value / 1_000:0.#}K",
        _ => $"{value:0.##}"
    };
}
