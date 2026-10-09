using LoupixDeck.Plugin.Wacom.Driver;
using LoupixDeck.PluginSdk;

namespace LoupixDeck.Plugin.Wacom.Commands;

/// <summary>
/// One key per Wacom driver function, e.g. <c>Wacom.DisplayToggle</c>. Each acts on the plugin's tablet (see
/// <see cref="WacomPlugin.ResolveTablet"/>) and brings its icon and caption as ordinary layers the
/// user can restyle in the button editor.
/// </summary>
internal sealed class FunctionCommand(WacomPlugin plugin, WacomFunction function) : IPluginCommand
{
    public CommandDescriptor Descriptor { get; } = Describe(function);

    public ButtonTargets SupportedTargets => ButtonTargets.All;

    public Task Execute(CommandContext ctx)
    {
        try
        {
            var tablet = plugin.ResolveTablet();
            if (tablet is null || !plugin.Driver.Perform(function, tablet.Value)) Hint(ctx, "Wacom not ready");
        }
        catch (Exception ex)
        {
            ctx.Host.Logger.Error($"Wacom {function} failed", ex);
        }

        return Task.CompletedTask;
    }

    private static void Hint(CommandContext ctx, string text)
    {
        if (ctx.Target == ButtonTargets.TouchButton && ctx.SourceIndex is { } slot)
        {
            ctx.Host.OverlayTouchText(slot, text, TimeSpan.FromSeconds(2));
        }
    }

    private static CommandDescriptor Describe(WacomFunction function) => function switch
    {
        WacomFunction.DisplayToggle => Make("DisplayToggle", "Display Toggle", "Display", "\U000F037A", // mdi-monitor-multiple
            "Move the pen to the next display set up for Display Toggle in Wacom Center."),
        WacomFunction.PrecisionMode => Make("PrecisionMode", "Precision Mode", "Precision", "\U000F01A3", // mdi-crosshairs
            "Toggle Wacom Precision Mode: a small area around the pen maps to the whole tablet. Press again to turn it off."),
        _ => throw new ArgumentOutOfRangeException(nameof(function))
    };

    private const string BackgroundHex = "#26323F";
    private const string WhiteHex = "#FFFFFF";

    private static CommandDescriptor Make(string name, string displayName, string caption, string icon, string description) => new()
    {
        CommandName = "Wacom." + name,
        DisplayName = displayName,
        Group = WacomPlugin.GroupName,
        Icon = icon,
        Description = description,
        ButtonLayout = Layout(caption)
    };

    /// <summary>The function's icon above a short caption, white on slate.</summary>
    private static ButtonLayoutDescriptor Layout(string caption) => new()
    {
        Mode = ButtonLayoutMode.Custom,
        BackgroundColor = BackgroundHex,
        Layers =
        [
            new ButtonLayerDescriptor { Kind = ButtonLayerKind.Symbol, Name = "Icon", IconScale = 0.46, OffsetY = -9, Color = WhiteHex },
            new ButtonLayerDescriptor
            {
                Kind = ButtonLayerKind.Text, Name = "Caption", Text = caption, TextSize = 13,
                OffsetY = 26, BoxWidth = 88, BoxHeight = 22, Color = WhiteHex
            }
        ]
    };
}
