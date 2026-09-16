using System.Text;
using System.Windows;
using RadCRHelper.Config;
using RadCRHelper.Services;

namespace RadCRHelper.Views;

public partial class RoomInfoWindow : Window
{
    private readonly AppConfig _config;

    public RoomInfoWindow(AppConfig config)
    {
        InitializeComponent();
        _config = config;
        Fill();
    }

    private void Fill()
    {
        var snap = DisplayService.GetSnapshot();
        var sb = new StringBuilder();

        sb.AppendLine($"Rad CR Helper   {App.VersionText}");
        sb.AppendLine($"Computer        {Environment.MachineName}");
        sb.AppendLine($"Room            {_config.ThisRoom.RoomName ?? "(not in settings file)"}");
        sb.AppendLine($"Signed in       {Environment.UserDomainName}\\{Environment.UserName}");
        sb.AppendLine($"Windows         {Environment.OSVersion.VersionString}");
        sb.AppendLine();
        sb.AppendLine($"Screen mode     {snap.Mode}");
        sb.AppendLine($"Screens         {snap.ConnectedCount} connected");

        int n = 1;
        foreach (var d in snap.Displays)
        {
            var size = d.IsActive ? $"{d.Width}x{d.Height}" : "not showing";
            var main = d.IsPrimary ? ", main screen" : "";
            sb.AppendLine($"  {n++}. {d.Name}  ({d.Connection}, {size}{main})");
        }

        sb.AppendLine($"Desk monitor    {_config.ThisRoom.PresenterDisplay ?? "(not set - use a name from the list above)"}");
        sb.AppendLine();
        sb.AppendLine("Shortcuts");
        foreach (var hk in _config.Hotkeys)
            sb.AppendLine($"  {hk.Keys,-16} {hk.Action}");
        sb.AppendLine();
        sb.AppendLine($"Settings file   {_config.FilePath}");
        if (_config.LoadWarning is not null)
            sb.AppendLine($"                ! {_config.LoadWarning}");
        sb.AppendLine($"Log file        {Log.FilePath}");

        InfoText.Text = sb.ToString();
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => Fill();

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        try { Clipboard.SetText(InfoText.Text); }
        catch (Exception ex) { Log.Warn($"Copy to clipboard failed: {ex.Message}"); }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
