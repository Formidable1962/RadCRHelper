using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;
using RadCRHelper.Config;

namespace RadCRHelper.Services;

/// <summary>
/// System-wide keyboard shortcuts ("Ctrl+Alt+H", "F13", ...) mapped to action names.
/// Works while other programs have focus. A future USB macro deck just sends these keys.
/// </summary>
public sealed class HotkeyService : IDisposable
{
    private const int WM_HOTKEY = 0x0312;
    private const uint MOD_ALT = 0x1, MOD_CONTROL = 0x2, MOD_SHIFT = 0x4, MOD_WIN = 0x8, MOD_NOREPEAT = 0x4000;

    private readonly IntPtr _hwnd;
    private readonly HwndSource _source;
    private readonly Action<string> _invoke;
    private readonly Dictionary<int, string> _actionsById = new();
    private int _nextId = 1;

    public HotkeyService(IntPtr hwnd, Action<string> invoke)
    {
        _hwnd = hwnd;
        _invoke = invoke;
        _source = HwndSource.FromHwnd(hwnd) ?? throw new InvalidOperationException("No window source for hotkeys");
        _source.AddHook(WndProc);
    }

    public void RegisterAll(IEnumerable<HotkeySetting> hotkeys)
    {
        foreach (var hk in hotkeys)
        {
            if (!TryParse(hk.Keys, out var modifiers, out var vk))
            {
                Log.Warn($"Hotkey '{hk.Keys}' could not be understood; skipped");
                continue;
            }

            int id = _nextId++;
            if (RegisterHotKey(_hwnd, id, modifiers | MOD_NOREPEAT, vk))
            {
                _actionsById[id] = hk.Action;
                Log.Info($"Hotkey {hk.Keys} -> {hk.Action}");
            }
            else
            {
                Log.Warn($"Hotkey {hk.Keys} is already used by another program (error {Marshal.GetLastWin32Error()})");
            }
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && _actionsById.TryGetValue(wParam.ToInt32(), out var action))
        {
            handled = true;
            // Run after the message is handled, so a dialog opened by the action does not nest inside WndProc.
            _source.Dispatcher.InvokeAsync(() => _invoke(action));
        }
        return IntPtr.Zero;
    }

    /// <summary>"Ctrl+Alt+H" -> modifiers + virtual key. Key names are WPF Key names (A, F13, OemComma...).</summary>
    private static bool TryParse(string text, out uint modifiers, out uint vk)
    {
        modifiers = 0;
        vk = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var parts = text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0) return false;
        foreach (var part in parts[..^1])
        {
            switch (part.ToLowerInvariant())
            {
                case "ctrl": case "control": modifiers |= MOD_CONTROL; break;
                case "alt": modifiers |= MOD_ALT; break;
                case "shift": modifiers |= MOD_SHIFT; break;
                case "win": case "windows": modifiers |= MOD_WIN; break;
                default: return false;
            }
        }

        // Plain numbers would parse as enum values ("1" = Key.Cancel). Digits are written D1, NumPad1, etc.
        if (int.TryParse(parts[^1], out _)) return false;
        if (!Enum.TryParse<Key>(parts[^1], ignoreCase: true, out var key) || key == Key.None) return false;
        vk = (uint)KeyInterop.VirtualKeyFromKey(key);
        return vk != 0;
    }

    public void Dispose()
    {
        foreach (var id in _actionsById.Keys) UnregisterHotKey(_hwnd, id);
        _actionsById.Clear();
        _source.RemoveHook(WndProc);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hwnd, int id);
}
