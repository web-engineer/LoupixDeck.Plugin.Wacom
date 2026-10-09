namespace LoupixDeck.Plugin.Wacom.Driver;

/// <summary>
/// Wacom driver functions an ExpressKey can be bound to that have no keystroke equivalent.
/// </summary>
public enum WacomFunction
{
    DisplayToggle,
    PrecisionMode
}

/// <summary>
/// A tablet the Wacom driver knows. <see cref="Index"/> is the driver's own numbering: every tablet
/// Wacom Center has ever set up, connected or not, so it is not stable across machines.
/// </summary>
public sealed record WacomTablet(int Index, string Name, bool Connected, bool HasPen)
{
    public override string ToString() =>
        $"{Index}: {Name}{(HasPen ? "" : " (no pen)")}{(Connected ? "" : " (not connected)")}";
}

/// <summary>A way of asking the running Wacom driver to perform a function now.</summary>
public interface IWacomDriver : IDisposable
{
    /// <summary>True when the driver is running and its message port answers.</summary>
    bool IsAvailable { get; }

    /// <summary>The tablets the driver knows, in its own order. Empty when it can't be reached.</summary>
    IReadOnlyList<WacomTablet> ListTablets();

    /// <summary>
    /// Asks the driver to perform <paramref name="function"/> for the tablet with driver index
    /// <paramref name="tablet"/>. Returns false when the request could not be delivered.
    /// </summary>
    bool Perform(WacomFunction function, int tablet);
}

/// <summary>Stand-in on systems this plugin has no driver bridge for yet (Windows, Linux).</summary>
internal sealed class UnsupportedWacomDriver : IWacomDriver
{
    public bool IsAvailable => false;
    public IReadOnlyList<WacomTablet> ListTablets() => [];
    public bool Perform(WacomFunction function, int tablet) => false;
    public void Dispose() { }
}
