using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using RadCRHelper.Config;
using RadCRHelper.Services;
using RadCRHelper.Views;
using Wpf.Ui.Appearance;

namespace RadCRHelper;

/// <summary>
/// Startup, and the one place where action names ("display.mirror", "session.restart", ...) are wired to code.
/// Panel buttons, hotkeys and a future USB macro deck all call <see cref="Invoke"/>.
/// </summary>
public partial class App : Application
{
    public static bool IsSessionEnding { get; private set; }
    public static bool IsExiting { get; private set; }

    /// <summary>e.g. "0.1.0+a1b2c3d" — version plus git commit.</summary>
    public static string VersionText { get; } =
        Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString()
        ?? "unknown";

    private static Mutex? _singleInstance;

    private AppConfig _config = new();
    private PanelWindow? _panel;
    private HotkeyService? _hotkeys;
    private readonly Dictionary<string, Func<Task>> _actions = new(StringComparer.OrdinalIgnoreCase);
    private readonly DispatcherTimer _displayChangeDebounce = new() { Interval = TimeSpan.FromMilliseconds(600) };
    private bool _switching;
    private bool _promptOpen;
    private bool _startupComplete;
    private RoomInfoWindow? _roomInfo;
    private readonly Dictionary<string, DateTime> _lastLinkOpen = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // One copy per signed-in user.
        _singleInstance = new Mutex(true, @"Local\RadCRHelper", out bool firstCopy);
        if (!firstCopy)
        {
            Log.Info("Another copy is already running; exiting");
            IsExiting = true;
            Shutdown();
            return;
        }

        try
        {
            Log.Info($"Starting Rad CR Helper {VersionText} on {Environment.MachineName} as {Environment.UserName}");

            _config = AppConfig.Load();
            ApplyTheme(_config.Panel);
            RegisterActions();

            _panel = new PanelWindow(_config, Invoke);
            _panel.Show();
            _panel.Reposition();

            _hotkeys = new HotkeyService(_panel.Handle, Invoke);
            _hotkeys.RegisterAll(_config.Hotkeys);

            // Live screen detection: cables unplugged, TVs switched off, Win+P, resolution changes.
            _displayChangeDebounce.Tick += (_, _) => { _displayChangeDebounce.Stop(); OnDisplaysChanged(); };
            SystemEvents.DisplaySettingsChanged += (_, _) => Dispatcher.InvokeAsync(() =>
            {
                _displayChangeDebounce.Stop();
                _displayChangeDebounce.Start();
            });

            _ = ApplyStartupModeAsync();
            _startupComplete = true;
        }
        catch (Exception ex)
        {
            // Without this, a broken start would leave an invisible process holding the single-instance lock.
            Log.Error("Startup failed", ex);
            MessageBox.Show($"Rad CR Helper could not start.\n\n{ex.Message}\n\nLog: {Log.FilePath}",
                "Rad CR Helper", MessageBoxButton.OK, MessageBoxImage.Error);
            IsExiting = true;
            Shutdown();
        }
    }

    // =====================================================================
    // Actions
    // =====================================================================

    private void RegisterActions()
    {
        _actions["panel.toggle"] = () => { _panel?.SetExpanded(!_panel.IsExpanded); return Task.CompletedTask; };
        _actions["panel.expand"] = () => { _panel?.SetExpanded(true); return Task.CompletedTask; };
        _actions["panel.collapse"] = () => { _panel?.SetExpanded(false); return Task.CompletedTask; };

        _actions["display.mirror"] = () => SwitchModeAsync(ScreenMode.Mirror);
        _actions["display.extend"] = () => SwitchModeAsync(ScreenMode.Extend);

        _actions["session.signout"] = () => ConfirmSessionAsync(restart: false);
        _actions["session.restart"] = () => ConfirmSessionAsync(restart: true);

        // One action per quick-launch link: "link.resident-conference", etc.
        foreach (var link in _config.Links)
        {
            var captured = link;
            _actions[$"link.{link.Id}"] = () => { OpenLink(captured); return Task.CompletedTask; };
        }

        // Admin exit. No button on purpose; reach it with the hotkey in RadCRHelper.json.
        _actions["app.exit"] = () =>
        {
            Log.Info("Exit requested by hotkey");
            IsExiting = true;
            Shutdown();
            return Task.CompletedTask;
        };

        // Hidden exit from the panel: double-click the title. Asks first, "No" is the default.
        _actions["app.exit.prompt"] = () =>
        {
            var answer = MessageBox.Show("Close Rad CR Helper?", "Rad CR Helper",
                MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
            return answer == MessageBoxResult.Yes ? _actions["app.exit"]() : Task.CompletedTask;
        };

        _actions["roominfo.show"] = () =>
        {
            if (_roomInfo is { IsLoaded: true }) { _roomInfo.Activate(); return Task.CompletedTask; }
            _roomInfo = new RoomInfoWindow(_config);
            _roomInfo.Closed += (_, _) => _roomInfo = null;
            _roomInfo.Show();
            return Task.CompletedTask;
        };
    }

    /// <summary>Runs an action by name. Unknown names are logged and ignored.</summary>
    public async void Invoke(string action)
    {
        if (!_actions.TryGetValue(action, out var run))
        {
            Log.Warn($"Unknown action '{action}'");
            return;
        }

        try
        {
            await run();
        }
        catch (Exception ex)
        {
            Log.Error($"Action '{action}' failed", ex);
        }
    }

    // =====================================================================
    // Screens
    // =====================================================================

    private async Task ApplyStartupModeAsync()
    {
        var target = (_config.ThisRoom.DefaultModeAtLogin ?? _config.Display.DefaultModeAtLogin) switch
        {
            StartupDisplayMode.Mirror => ScreenMode.Mirror,
            StartupDisplayMode.Extend => ScreenMode.Extend,
            _ => ScreenMode.Unknown,
        };

        var snap = DisplayService.GetSnapshot();
        if (target != ScreenMode.Unknown && !_config.ThisRoom.HideDisplayControls && !snap.CanSwitch)
        {
            // Right after sign-in the TV's HDMI link may not be up yet. Look once more before giving up.
            _panel?.ShowDisplayState(snap, busy: false);
            await Task.Delay(TimeSpan.FromSeconds(8));
            snap = DisplayService.GetSnapshot();
        }

        if (target == ScreenMode.Unknown || _config.ThisRoom.HideDisplayControls || !snap.CanSwitch || snap.Mode == target)
        {
            _panel?.ShowDisplayState(snap, busy: false);
            return;
        }

        // Room default at sign-in, no prompt: nobody inherits the last person's presenter view.
        Log.Info($"Applying room default screen mode: {target}");
        _switching = true;
        try
        {
            _panel?.ShowDisplayState(snap, busy: true, "Setting up the screens…");
            await ApplyModeAsync(target);
        }
        finally
        {
            _switching = false;
            _panel?.Reposition();
            _panel?.ShowDisplayState(DisplayService.GetSnapshot(), busy: false);
        }
    }

    private async Task SwitchModeAsync(ScreenMode target)
    {
        if (_switching || _promptOpen || _panel is null) return;

        var before = DisplayService.GetSnapshot();
        if (!before.CanSwitch || _config.ThisRoom.HideDisplayControls) { _panel.ShowDisplayState(before, false); return; }
        if (before.Mode == target) { _panel.ShowDisplayState(before, false); return; }

        _switching = true;
        string? finalStatus = null;
        try
        {
            _panel.ShowDisplayState(before, busy: true, "Switching screens…");

            if (!await ApplyModeAsync(target))
            {
                finalStatus = "The screens could not be switched. Try again, or press Windows + P.";
                return;
            }

            _panel.Reposition();
            _panel.ShowDisplayState(DisplayService.GetSnapshot(), busy: true, "Check the TV…");

            // Safety net: a TV that cannot show the new mode may go black. No answer = go back.
            _promptOpen = true;
            var keep = new CountdownDialog(
                title: "Keep this screen setting?",
                message: "If the TV and this screen look right, choose Keep. " +
                         "If not, do nothing and the screens will go back to how they were.",
                countdownFormat: "Going back in {0} seconds",
                actText: "Keep",
                safeText: "Go back",
                seconds: _config.Display.RevertSeconds,
                timeoutResult: false,
                actIsDanger: false).ShowDialog() == true;
            _promptOpen = false;

            if (!keep)
            {
                // Previous state was Mirror or Extend -> restore it. Anything else -> the room default, Mirror.
                var back = before.Mode == ScreenMode.Extend ? ScreenMode.Extend : ScreenMode.Mirror;
                Log.Info($"Screen change not kept; going back to {back}");
                _panel.ShowDisplayState(DisplayService.GetSnapshot(), busy: true, "Going back…");
                if (!await ApplyModeAsync(back))
                    finalStatus = "The screens could not go back. Press Windows + P and choose Duplicate.";
            }
        }
        finally
        {
            _promptOpen = false;
            _switching = false;
            _panel.Reposition();
            _panel.ShowDisplayState(DisplayService.GetSnapshot(), busy: false, finalStatus);
        }
    }

    /// <summary>Sets the mode off the UI thread, then keeps the desk monitor as the main screen in Extend.</summary>
    private async Task<bool> ApplyModeAsync(ScreenMode mode)
    {
        bool ok = await Task.Run(() => DisplayService.SetMode(mode));
        if (!ok) return false;

        await Task.Delay(1500); // let Windows and the TV settle
        if (mode == ScreenMode.Extend)
            await Task.Run(() => DisplayService.EnsurePrimary(_config.ThisRoom.PresenterDisplay));

        return true;
    }

    private void OnDisplaysChanged()
    {
        if (_panel is null) return;
        _panel.Reposition();
        if (!_switching)
            _panel.ShowDisplayState(DisplayService.GetSnapshot(), busy: false);
    }

    // =====================================================================
    // Quick-launch links
    // =====================================================================

    private void OpenLink(LinkSetting link)
    {
        // Nervous users double- and triple-click when nothing seems to happen. One open per link every 5 s.
        if (_lastLinkOpen.TryGetValue(link.Id, out var last) && DateTime.Now - last < TimeSpan.FromSeconds(5))
            return;
        _lastLinkOpen[link.Id] = DateTime.Now;

        try
        {
            Log.Info($"Opening link '{link.Label}' ({link.Url})");
            Process.Start(new ProcessStartInfo(link.Url) { UseShellExecute = true });
            _panel?.ShowLinkStatus($"Opening {link.Label}…");
        }
        catch (Exception ex)
        {
            Log.Error($"Opening link '{link.Label}' failed", ex);
            MessageBox.Show($"{link.Label} could not be opened.\n\nPlease contact Radiology IT.",
                "Rad CR Helper", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    // =====================================================================
    // Sign out / Restart (forced, 30-second countdown, no shut down anywhere)
    // =====================================================================

    private Task ConfirmSessionAsync(bool restart)
    {
        if (_promptOpen || _switching) return Task.CompletedTask;
        _promptOpen = true;

        bool goAhead;
        try
        {
            goAhead = restart
                ? new CountdownDialog(
                    title: "Restart this computer?",
                    message: "All open programs will close and anything unsaved will be lost. " +
                             "The computer will be ready again in a few minutes.",
                    countdownFormat: "Restarting in {0} seconds",
                    actText: "Restart now",
                    safeText: "Cancel",
                    seconds: _config.Session.CountdownSeconds,
                    timeoutResult: true,
                    actIsDanger: true).ShowDialog() == true
                : new CountdownDialog(
                    title: "Sign out?",
                    message: "All open programs will close and anything unsaved will be lost. " +
                             "The next person will start fresh.",
                    countdownFormat: "Signing out in {0} seconds",
                    actText: "Sign out now",
                    safeText: "Cancel",
                    seconds: _config.Session.CountdownSeconds,
                    timeoutResult: true,
                    actIsDanger: true).ShowDialog() == true;
        }
        finally
        {
            _promptOpen = false;
        }

        if (!goAhead)
        {
            Log.Info(restart ? "Restart cancelled" : "Sign out cancelled");
            return Task.CompletedTask;
        }

        bool ok = restart ? SessionService.Restart() : SessionService.SignOut();
        if (!ok)
        {
            MessageBox.Show(
                restart ? "The computer could not be restarted. Please contact Radiology IT."
                        : "You could not be signed out. Please use the Start menu, or contact Radiology IT.",
                "Rad CR Helper", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        return Task.CompletedTask;
    }

    // =====================================================================
    // Plumbing
    // =====================================================================

    private static void ApplyTheme(PanelSettings panel)
    {
        var theme = string.Equals(panel.Theme, "Light", StringComparison.OrdinalIgnoreCase)
            ? ApplicationTheme.Light
            : ApplicationTheme.Dark;

        ApplicationThemeManager.Apply(theme, updateAccent: false);

        try
        {
            // The four-colour overload keeps the exact colour; the simple overload washes it out in Dark theme.
            var accent = (Color)ColorConverter.ConvertFromString(panel.AccentColor);
            ApplicationAccentColorManager.Apply(accent, accent, accent, accent);
        }
        catch (Exception ex)
        {
            Log.Warn($"Accent colour '{panel.AccentColor}' not understood: {ex.Message}");
        }
    }

    private void OnSessionEnding(object sender, SessionEndingCancelEventArgs e)
    {
        IsSessionEnding = true;
        Log.Info($"Windows session ending ({e.ReasonSessionEnding})");
    }

    protected override void OnExit(ExitEventArgs e)
    {
        IsExiting = true;
        _hotkeys?.Dispose();
        _singleInstance?.Dispose();
        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // A room PC with a crashed helper is worse than one that logged an error and carried on.
        Log.Error("Unhandled error", e.Exception);
        e.Handled = _startupComplete; // during startup, let OnStartup's catch deal with it
    }
}
