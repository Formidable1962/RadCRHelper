using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using RadCRHelper.Config;
using RadCRHelper.Services;
using ControlAppearance = Wpf.Ui.Controls.ControlAppearance;
using SymbolRegular = Wpf.Ui.Controls.SymbolRegular;
using UiButton = Wpf.Ui.Controls.Button;
using UiSymbolIcon = Wpf.Ui.Controls.SymbolIcon;

namespace RadCRHelper.Views;

/// <summary>
/// The right-docked panel. It only draws itself and reports clicks; App decides what actions do.
/// </summary>
public partial class PanelWindow : Window
{
    private readonly AppConfig _config;
    private readonly Action<string> _invoke;
    private readonly DispatcherTimer _linkStatusTimer = new() { Interval = TimeSpan.FromSeconds(6) };

    public bool IsExpanded { get; private set; }

    public IntPtr Handle { get; private set; }

    public PanelWindow(AppConfig config, Action<string> invoke)
    {
        InitializeComponent();
        _config = config;
        _invoke = invoke;

        RoomNameText.Text = config.ThisRoom.RoomName ?? Environment.MachineName;
        VersionText.Text = $"Version {App.VersionText}";

        if (config.LoadWarning is not null)
        {
            WarningText.Text = config.LoadWarning;
            WarningBox.Visibility = Visibility.Visible;
        }

        if (config.ThisRoom.HideDisplayControls)
            ScreensSection.Visibility = Visibility.Collapsed;

        BuildLinks(config.Links);
        _linkStatusTimer.Tick += (_, _) =>
        {
            _linkStatusTimer.Stop();
            LinkStatusText.Visibility = Visibility.Collapsed;
        };

        SourceInitialized += (_, _) =>
        {
            Handle = new WindowInteropHelper(this).Handle;
            WindowInterop.HideFromAltTab(Handle);
            if (_config.Panel.HideFromScreenShare)
                WindowInterop.SetHiddenFromCapture(Handle, true);
        };

        // Taskbar moved/resized, or the screen layout changed.
        SystemParameters.StaticPropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SystemParameters.WorkArea))
                Dispatcher.InvokeAsync(Reposition);
        };

        SetExpanded(config.Panel.StartExpanded);
    }

    public void SetExpanded(bool expanded)
    {
        IsExpanded = expanded;
        TabView.Visibility = expanded ? Visibility.Collapsed : Visibility.Visible;
        ExpandedView.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
        Reposition();
        // No Activate(): opening the panel from the hotkey must not pull focus from a running slide show.
    }

    /// <summary>Docks to the right edge of the main screen's usable area (above the taskbar).</summary>
    public void Reposition()
    {
        var area = SystemParameters.WorkArea;

        if (IsExpanded)
        {
            Width = _config.Panel.WidthDip;
            Height = area.Height;
            Top = area.Top;
        }
        else
        {
            Width = _config.Panel.TabWidthDip;
            Height = _config.Panel.TabHeightDip;
            Top = area.Top + (area.Height - Height) / 2;
        }

        Left = area.Right - Width;
    }

    /// <summary>Lights the current mode and enables/disables the Mirror/Extend buttons.</summary>
    public void ShowDisplayState(DisplaySnapshot snapshot, bool busy, string? status = null)
    {
        bool mirror = snapshot.Mode == ScreenMode.Mirror;
        bool extend = snapshot.Mode == ScreenMode.Extend;

        MirrorButton.Appearance = mirror ? ControlAppearance.Primary : ControlAppearance.Secondary;
        ExtendButton.Appearance = extend ? ControlAppearance.Primary : ControlAppearance.Secondary;
        MirrorOnText.Visibility = mirror ? Visibility.Visible : Visibility.Collapsed;
        ExtendOnText.Visibility = extend ? Visibility.Visible : Visibility.Collapsed;

        bool enabled = snapshot.CanSwitch && !busy;
        MirrorButton.IsEnabled = enabled;
        ExtendButton.IsEnabled = enabled;

        status ??= snapshot.CanSwitch ? null : "Second display not detected. Check that the TV is on and its cable is connected.";
        DisplayStatusText.Text = status ?? "";
        DisplayStatusText.Visibility = status is null ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>One big button per link, styled like the Mirror/Extend buttons.</summary>
    private void BuildLinks(IReadOnlyList<LinkSetting> links)
    {
        if (links.Count == 0)
        {
            LinksSection.Visibility = Visibility.Collapsed;
            return;
        }

        foreach (var link in links)
        {
            var symbol = Enum.TryParse<SymbolRegular>(link.Icon, ignoreCase: true, out var s) ? s : SymbolRegular.Video24;

            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(new TextBlock { Text = link.Label, FontSize = 19, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
            if (!string.IsNullOrWhiteSpace(link.Subtitle))
                text.Children.Add(new TextBlock { Text = link.Subtitle, FontSize = 13, Opacity = 0.85, Margin = new Thickness(0, 2, 0, 0), TextWrapping = TextWrapping.Wrap });

            var content = new Grid();
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var icon = new UiSymbolIcon { Symbol = symbol, FontSize = 30, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(text, 1);
            content.Children.Add(icon);
            content.Children.Add(text);

            var button = new UiButton
            {
                Appearance = ControlAppearance.Secondary,
                MinHeight = 84,
                Padding = new Thickness(14, 8, 14, 8),
                Margin = new Thickness(0, LinksPanel.Children.Count == 0 ? 0 : 10, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Tag = $"link.{link.Id}",
                ToolTip = link.Url,
                Content = content,
            };
            button.Click += Action_Click;
            LinksPanel.Children.Add(button);
        }
    }

    /// <summary>Short reassurance under the Meetings buttons, e.g. "Opening Resident Conference…".</summary>
    public void ShowLinkStatus(string message)
    {
        LinkStatusText.Text = message;
        LinkStatusText.Visibility = Visibility.Visible;
        _linkStatusTimer.Stop();
        _linkStatusTimer.Start();
    }

    private void Title_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            e.Handled = true;
            _invoke("app.exit.prompt");
        }
    }

    private void Action_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string action })
            _invoke(action);
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        // Users cannot close the panel (Alt+F4 does nothing). It closes only when Windows signs out or restarts.
        if (!App.IsSessionEnding && !App.IsExiting) e.Cancel = true;
        base.OnClosing(e);
    }
}
