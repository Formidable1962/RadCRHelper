using System.ComponentModel;
using System.Windows;
using System.Windows.Interop;
using RadCRHelper.Config;
using RadCRHelper.Services;
using ControlAppearance = Wpf.Ui.Controls.ControlAppearance;

namespace RadCRHelper.Views;

/// <summary>
/// The right-docked panel. It only draws itself and reports clicks; App decides what actions do.
/// </summary>
public partial class PanelWindow : Window
{
    private readonly AppConfig _config;
    private readonly Action<string> _invoke;

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
