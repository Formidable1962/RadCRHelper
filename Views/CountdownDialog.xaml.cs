using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using ControlAppearance = Wpf.Ui.Controls.ControlAppearance;

namespace RadCRHelper.Views;

/// <summary>
/// "This will happen in N seconds" prompt with exactly two buttons.
/// ShowDialog() returns true if the action should go ahead.
/// </summary>
public partial class CountdownDialog : Window
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly string _countdownFormat;
    private readonly bool _timeoutResult;
    private int _remaining;
    private bool _allowClose;

    /// <param name="title">Big heading, e.g. "Signing out".</param>
    /// <param name="message">Plain-language consequence.</param>
    /// <param name="countdownFormat">e.g. "Signing out in {0} seconds". {0} = seconds left.</param>
    /// <param name="actText">Button that goes ahead now, e.g. "Sign out now".</param>
    /// <param name="safeText">Button that backs out, e.g. "Cancel".</param>
    /// <param name="seconds">Countdown length.</param>
    /// <param name="timeoutResult">What happens when the countdown ends: true = go ahead, false = back out.</param>
    /// <param name="actIsDanger">Red "go ahead" button for destructive actions.</param>
    public CountdownDialog(string title, string message, string countdownFormat,
        string actText, string safeText, int seconds, bool timeoutResult, bool actIsDanger)
    {
        InitializeComponent();

        TitleText.Text = title;
        MessageText.Text = message;
        ActButton.Content = actText;
        SafeButton.Content = safeText;
        ActButton.Appearance = actIsDanger ? ControlAppearance.Danger : ControlAppearance.Primary;

        _countdownFormat = countdownFormat;
        _timeoutResult = timeoutResult;
        _remaining = Math.Max(1, seconds);
        CountdownBar.Maximum = _remaining;

        _timer.Tick += (_, _) => Tick();
        Loaded += (_, _) =>
        {
            // Centre on the MAIN screen (where the panel is), never on whichever screen the mouse is on.
            var area = SystemParameters.WorkArea;
            Left = area.Left + (area.Width - ActualWidth) / 2;
            Top = area.Top + (area.Height - ActualHeight) / 2;

            UpdateCountdown();
            _timer.Start();
            // Focus the safe choice for destructive prompts, the "keep" choice otherwise.
            (actIsDanger ? SafeButton : ActButton).Focus();
            Activate();
        };

        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { Finish(false); e.Handled = true; }
        };
    }

    private void Tick()
    {
        _remaining--;
        if (_remaining <= 0) Finish(_timeoutResult);
        else UpdateCountdown();
    }

    private void UpdateCountdown()
    {
        CountdownText.Text = string.Format(_countdownFormat, _remaining);
        CountdownBar.Value = _remaining;
    }

    private void ActButton_Click(object sender, RoutedEventArgs e) => Finish(true);

    private void SafeButton_Click(object sender, RoutedEventArgs e) => Finish(false);

    private void Finish(bool result)
    {
        if (_allowClose) return;
        _timer.Stop();
        _allowClose = true;
        DialogResult = result;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        // Blocks Alt+F4. Only our buttons, Esc, or the countdown close this window.
        if (!_allowClose && !App.IsSessionEnding) e.Cancel = true;
        base.OnClosing(e);
    }
}
