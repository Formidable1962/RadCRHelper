using System.Runtime.InteropServices;
using static RadCRHelper.Services.NativeDisplay;

namespace RadCRHelper.Services;

public enum ScreenMode
{
    Unknown,
    Single,  // only one screen showing a picture ("PC screen only" / "Second screen only")
    Mirror,  // Windows "Duplicate"
    Extend,
}

public sealed record DisplayInfo(
    string Name,
    string Connection,
    bool IsActive,
    bool IsPrimary,
    int Width,
    int Height);

public sealed record DisplaySnapshot(
    IReadOnlyList<DisplayInfo> Displays,
    ScreenMode Mode)
{
    /// <summary>Connected screens, whether or not they are currently showing a picture.</summary>
    public int ConnectedCount => Displays.Count;

    /// <summary>Mirror/Extend only makes sense with two connected screens.</summary>
    public bool CanSwitch => ConnectedCount >= 2;
}

/// <summary>
/// Reads and changes the screen layout through the Windows display API.
/// No admin rights needed. All methods are safe to call from any thread.
/// </summary>
public static class DisplayService
{
    /// <summary>Takes a fresh look at what is connected and how it is set up. Never throws.</summary>
    public static DisplaySnapshot GetSnapshot()
    {
        try
        {
            var displays = new List<DisplayInfo>();

            // Active paths: what is showing a picture right now, with position and resolution.
            var (activePaths, activeModes) = Query(QDC_ONLY_ACTIVE_PATHS);
            var activeTargets = new HashSet<(uint, int, uint)>();
            var activeSources = new HashSet<(uint, int, uint)>();

            foreach (var p in activePaths)
            {
                if ((p.flags & DISPLAYCONFIG_PATH_ACTIVE) == 0) continue;
                activeTargets.Add(Key(p.targetInfo.adapterId, p.targetInfo.id));
                activeSources.Add(Key(p.sourceInfo.adapterId, p.sourceInfo.id));

                int width = 0, height = 0;
                bool primary = false;
                var idx = p.sourceInfo.modeInfoIdx;
                if (idx != DISPLAYCONFIG_PATH_MODE_IDX_INVALID && idx < activeModes.Length &&
                    activeModes[idx].infoType == DISPLAYCONFIG_MODE_INFO_TYPE_SOURCE)
                {
                    var sm = activeModes[idx].sourceMode;
                    width = (int)sm.width;
                    height = (int)sm.height;
                    primary = sm.position.x == 0 && sm.position.y == 0; // Windows' main screen sits at 0,0
                }

                displays.Add(new DisplayInfo(
                    TargetName(p.targetInfo),
                    OutputTechnologyName(p.targetInfo.outputTechnology),
                    IsActive: true, primary, width, height));
            }

            // All paths: adds screens that are plugged in but not currently showing anything.
            var (allPaths, _) = Query(QDC_ALL_PATHS);
            var seen = new HashSet<(uint, int, uint)>(activeTargets);
            foreach (var p in allPaths)
            {
                if (p.targetInfo.targetAvailable == 0) continue;
                var key = Key(p.targetInfo.adapterId, p.targetInfo.id);
                if (!seen.Add(key)) continue;

                displays.Add(new DisplayInfo(
                    TargetName(p.targetInfo),
                    OutputTechnologyName(p.targetInfo.outputTechnology),
                    IsActive: false, IsPrimary: false, 0, 0));
            }

            return new DisplaySnapshot(displays, ReadMode(activeTargets.Count, activeSources.Count));
        }
        catch (Exception ex)
        {
            Log.Error("Reading display configuration failed", ex);
            return new DisplaySnapshot(Array.Empty<DisplayInfo>(), ScreenMode.Unknown);
        }
    }

    /// <summary>Switches to Mirror or Extend. Returns true on success. Slow (1-3 s): call off the UI thread.</summary>
    public static bool SetMode(ScreenMode mode)
    {
        uint topology = mode switch
        {
            ScreenMode.Mirror => SDC_TOPOLOGY_CLONE,
            ScreenMode.Extend => SDC_TOPOLOGY_EXTEND,
            _ => throw new ArgumentOutOfRangeException(nameof(mode), "Only Mirror or Extend can be set."),
        };

        int result = SetDisplayConfig(0, null, 0, null, SDC_APPLY | topology);
        if (result != ERROR_SUCCESS)
        {
            Log.Error($"SetDisplayConfig({mode}) failed with Windows error {result}");
            return false;
        }

        Log.Info($"Screen mode set to {mode}");
        return true;
    }

    /// <summary>
    /// In Extend, makes the desk monitor the main screen (so the taskbar and this panel stay on the desk,
    /// and PowerPoint puts the slide show on the TV). Matches <paramref name="presenterDisplay"/> against
    /// monitor names as shown in Room Info. Does nothing if no name is configured or nothing matches.
    /// </summary>
    public static bool EnsurePrimary(string? presenterDisplay)
    {
        if (string.IsNullOrWhiteSpace(presenterDisplay)) return false;

        try
        {
            var (paths, modes) = Query(QDC_ONLY_ACTIVE_PATHS);

            int match = Array.FindIndex(paths, p =>
                (p.flags & DISPLAYCONFIG_PATH_ACTIVE) != 0 &&
                TargetName(p.targetInfo)
                    .Contains(presenterDisplay, StringComparison.OrdinalIgnoreCase));
            if (match < 0)
            {
                Log.Warn($"Presenter display '{presenterDisplay}' not found among active screens");
                return false;
            }

            var idx = paths[match].sourceInfo.modeInfoIdx;
            if (idx == DISPLAYCONFIG_PATH_MODE_IDX_INVALID || idx >= modes.Length) return false;

            var origin = modes[idx].sourceMode.position;
            if (origin.x == 0 && origin.y == 0) return true; // already the main screen

            // Shift every screen so the desk monitor lands at 0,0. Relative layout is preserved.
            for (int i = 0; i < modes.Length; i++)
            {
                if (modes[i].infoType != DISPLAYCONFIG_MODE_INFO_TYPE_SOURCE) continue;
                modes[i].sourceMode.position.x -= origin.x;
                modes[i].sourceMode.position.y -= origin.y;
            }

            int result = SetDisplayConfig((uint)paths.Length, paths, (uint)modes.Length, modes,
                SDC_APPLY | SDC_USE_SUPPLIED_DISPLAY_CONFIG | SDC_SAVE_TO_DATABASE | SDC_ALLOW_CHANGES);
            if (result != ERROR_SUCCESS)
            {
                Log.Error($"Making '{presenterDisplay}' the main screen failed with Windows error {result}");
                return false;
            }

            Log.Info($"'{presenterDisplay}' is now the main screen");
            return true;
        }
        catch (Exception ex)
        {
            Log.Error("EnsurePrimary failed", ex);
            return false;
        }
    }

    // ------------------------------------------------------------------

    private static ScreenMode ReadMode(int activeTargets, int activeSources)
    {
        // Preferred: ask Windows which Win+P topology is in force.
        if (GetDisplayConfigBufferSizes(QDC_DATABASE_CURRENT, out var pc, out var mc) == ERROR_SUCCESS)
        {
            var paths = new DISPLAYCONFIG_PATH_INFO[pc];
            var modes = new DISPLAYCONFIG_MODE_INFO[mc];
            if (QueryDisplayConfigTopology(QDC_DATABASE_CURRENT, ref pc, paths, ref mc, modes, out var topology) == ERROR_SUCCESS)
            {
                ScreenMode? known = topology switch
                {
                    Topology.Clone => ScreenMode.Mirror,
                    Topology.Extend => ScreenMode.Extend,
                    Topology.Internal or Topology.External => ScreenMode.Single,
                    _ => null, // layout set by another tool: work it out from the paths below
                };
                if (known is not null) return known.Value;
            }
        }

        // Fallback: two screens on one source = mirrored; two sources = extended.
        if (activeTargets <= 1) return activeTargets == 1 ? ScreenMode.Single : ScreenMode.Unknown;
        return activeSources == 1 ? ScreenMode.Mirror : ScreenMode.Extend;
    }

    private static (DISPLAYCONFIG_PATH_INFO[] Paths, DISPLAYCONFIG_MODE_INFO[] Modes) Query(uint flags)
    {
        // The configuration can change between the size call and the query call; retry a few times.
        for (int attempt = 0; attempt < 5; attempt++)
        {
            int err = GetDisplayConfigBufferSizes(flags, out var pathCount, out var modeCount);
            if (err != ERROR_SUCCESS) throw new InvalidOperationException($"GetDisplayConfigBufferSizes error {err}");

            var paths = new DISPLAYCONFIG_PATH_INFO[pathCount];
            var modes = new DISPLAYCONFIG_MODE_INFO[modeCount];
            err = QueryDisplayConfig(flags, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero);

            if (err == ERROR_SUCCESS)
            {
                Array.Resize(ref paths, (int)pathCount);
                Array.Resize(ref modes, (int)modeCount);
                return (paths, modes);
            }
            if (err != ERROR_INSUFFICIENT_BUFFER)
                throw new InvalidOperationException($"QueryDisplayConfig error {err}");
        }
        throw new InvalidOperationException("QueryDisplayConfig kept changing size");
    }

    /// <summary>
    /// Monitor name from its EDID, e.g. "DELL P2422H". Screens that report no name get a stable label
    /// built from the connection, e.g. "Unnamed HDMI screen 2", so Room Info and presenterDisplay always agree.
    /// </summary>
    private static string TargetName(DISPLAYCONFIG_PATH_TARGET_INFO target)
    {
        var adapter = target.adapterId;
        var targetId = target.id;
        var request = new DISPLAYCONFIG_TARGET_DEVICE_NAME
        {
            header = new DISPLAYCONFIG_DEVICE_INFO_HEADER
            {
                type = DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME,
                size = (uint)Marshal.SizeOf<DISPLAYCONFIG_TARGET_DEVICE_NAME>(),
                adapterId = adapter,
                id = targetId,
            },
        };

        if (DisplayConfigGetDeviceInfo(ref request) != ERROR_SUCCESS)
            return $"Unnamed {OutputTechnologyName(target.outputTechnology)} screen {targetId}";

        return string.IsNullOrWhiteSpace(request.monitorFriendlyDeviceName)
            ? $"Unnamed {OutputTechnologyName(target.outputTechnology)} screen {request.connectorInstance}"
            : request.monitorFriendlyDeviceName;
    }

    private static (uint, int, uint) Key(LUID adapter, uint id) => (adapter.LowPart, adapter.HighPart, id);
}
