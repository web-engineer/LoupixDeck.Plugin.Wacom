using LoupixDeck.Plugin.Wacom.Commands;
using LoupixDeck.Plugin.Wacom.Driver;
using LoupixDeck.PluginSdk;

namespace LoupixDeck.Plugin.Wacom;

/// <summary>
/// LoupixDeck plugin entry point. Gives Loupedeck keys the Wacom driver functions that otherwise
/// only an ExpressKey (or ExpressKey Remote) bound to them can trigger: Display Toggle and
/// Precision Mode. Keystrokes, modifiers, app launches and the like need no plugin; the host's own
/// macro commands already cover them.
/// </summary>
public sealed class WacomPlugin : LoupixPlugin, IPluginSettingsPage
{
    public const string GroupName = "Wacom";

    private const string TabletKey = "tablet";

    private IPluginHost _host = null!;
    private IWacomDriver _driver = null!;

    public override PluginMetadata Metadata { get; } = new()
    {
        Id = "wacom",
        Name = "Wacom",
        Version = new Version(0, 1, 0),
        SdkVersion = SdkInfo.Version,
        Author = "web-engineer",
        Description = "Display Toggle and Precision Mode for Wacom tablets on Loupedeck keys (macOS only)."
    };

    internal IWacomDriver Driver => _driver;

    /// <summary>
    /// The tablet the functions act on: the connected tablet with a pen. With more than one, the
    /// Tablet setting picks (by the driver's number for it), else the first. Null when none is
    /// connected; the reason is logged.
    /// </summary>
    internal int? ResolveTablet()
    {
        var connected = ConnectedTablets();
        if (connected.Count == 0)
        {
            _host.Logger.Warn("Wacom: no connected tablet with a pen; is the tablet on and the Wacom driver running?");
            return null;
        }

        if (connected.Count > 1 && int.TryParse(_host.Settings.Get(TabletKey, string.Empty)?.Trim(), out var chosen))
        {
            if (connected.Any(t => t.Index == chosen)) return chosen;
            _host.Logger.Warn($"Wacom: tablet {chosen} from the settings is not connected; using {connected[0]}");
        }

        return connected[0].Index;
    }

    /// <summary>Connected tablets with a pen (an ExpressKey Remote is a "tablet" without one).</summary>
    private List<WacomTablet> ConnectedTablets() =>
        _driver.ListTablets().Where(t => t.Connected && t.HasPen).ToList();

    public override void Initialize(IPluginHost host)
    {
        _host = host;
        _driver = OperatingSystem.IsMacOS() ? new MacWacomDriver(host.Logger) : new UnsupportedWacomDriver();
        host.Logger.Info(_driver.IsAvailable
            ? "Wacom driver message port found"
            : "Wacom driver not reachable; Wacom keys will show a hint until it is running");
    }

    public override void Shutdown() => _driver?.Dispose();

    public override IEnumerable<IPluginCommand> GetCommands() =>
    [
        new FunctionCommand(this, WacomFunction.DisplayToggle),
        new FunctionCommand(this, WacomFunction.PrecisionMode)
    ];

    // ---- IPluginSettingsPage ----

    /// <summary>
    /// Nothing to choose with a single connected tablet, so the page only names it. With several,
    /// a Tablet field lists them by the driver's number for each.
    /// </summary>
    public IReadOnlyList<PluginSettingDescriptor> SettingsSchema
    {
        get
        {
            var connected = ConnectedTablets();
            if (connected.Count <= 1)
            {
                return
                [
                    new PluginSettingDescriptor
                    {
                        Key = TabletKey + "_info",
                        Label = "Tablet",
                        Kind = PluginSettingKind.Heading,
                        Description = connected.Count == 1
                            ? $"Wacom keys act on your {connected[0].Name}."
                            : "No Wacom tablet is connected, or the Wacom driver isn't running."
                    }
                ];
            }

            return
            [
                new PluginSettingDescriptor
                {
                    Key = TabletKey,
                    Label = "Tablet",
                    Kind = PluginSettingKind.Text,
                    DefaultValue = string.Empty,
                    Description = "Several tablets are connected. Enter the number of the one Wacom keys act on "
                        + "(empty: the first): " + string.Join(", ", connected.Select(t => $"{t.Index} = {t.Name}"))
                }
            ];
        }
    }

    public IReadOnlyList<PluginSettingAction> SettingsActions => [];

    public void OnSettingsSaved() { }

    public override IReadOnlyList<CommandGroupDescriptor> GetCommandGroups() =>
    [
        new CommandGroupDescriptor
        {
            Group = GroupName,
            Description = "Wacom driver functions: Display Toggle and Precision Mode",
            Icon = "\U000F19B9" // mdi-draw-pen
        }
    ];
}
