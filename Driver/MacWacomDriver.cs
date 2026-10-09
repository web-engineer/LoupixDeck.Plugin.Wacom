using System.Runtime.InteropServices;
using LoupixDeck.PluginSdk;

namespace LoupixDeck.Plugin.Wacom.Driver;

/// <summary>
/// Talks to the user-space Wacom tablet driver (WacomTabletDriver.app, Wacom Tablet 6.4.x) over the
/// CFMessagePort it serves for Wacom Center. The port is reachable from any unsandboxed process.
/// Each request is an <see cref="EventPacket"/>.
/// </summary>
internal sealed class MacWacomDriver(IPluginLogger logger) : IWacomDriver
{
    public const string PortName = "com.wacom.WacomTabletDriverMessageServer";

    private const double SendTimeout = 1.0;
    private const double ReplyTimeout = 1.0;

    public bool IsAvailable
    {
        get
        {
            var port = OpenPort();
            if (port == IntPtr.Zero) return false;
            CoreFoundation.CFRelease(port);
            return true;
        }
    }

    /// <summary>The driver numbers tablets from 0 with no gaps; the first index without a name ends the list.</summary>
    private const int MaxTablets = 32;

    public IReadOnlyList<WacomTablet> ListTablets()
    {
        var tablets = new List<WacomTablet>();
        for (var index = 0; index < MaxTablets; index++)
        {
            if (TabletValue(index, EventPacket.Id.TabletName) is not { } reply || EventPacket.ReadString(reply) is not { } name)
                break;

            tablets.Add(new WacomTablet(index, name,
                Connected: ReadFlag(index, EventPacket.Id.TabletConnected),
                HasPen: ReadFlag(index, EventPacket.Id.TabletHasPen)));
        }

        return tablets;
    }

    public bool Perform(WacomFunction function, int tablet) => function switch
    {
        WacomFunction.DisplayToggle => AdvanceDisplayToggle((uint)tablet),
        WacomFunction.PrecisionMode => TogglePrecisionMode((uint)tablet),
        _ => throw new ArgumentOutOfRangeException(nameof(function))
    };

    /// <summary>The name an ExpressKey passes is its own; any name is accepted.</summary>
    private const string PrecisionSource = "LoupixDeck";

    private bool _precisionOn;

    /// <summary>
    /// Precision Mode is held, not latched: <c>Set [driver, tablet, context manager, 0x119 by
    /// name]</c> with u16 1 turns it on and 0 turns it off. The host reports presses only, so each
    /// press flips between the two.
    /// </summary>
    private bool TogglePrecisionMode(uint tablet)
    {
        var engage = !_precisionOn;
        EventPacket.Key[] path =
        [
            new(EventPacket.Id.Driver), new(EventPacket.Id.Tablet, Reference: tablet),
            new(EventPacket.Id.ContextManager), new(EventPacket.Id.FinePoint, Name: PrecisionSource)
        ];

        var reply = Send(EventPacket.Build(EventPacket.Command.Set, path, (ushort)(engage ? 1 : 0)));
        if (reply is null) return false;

        var status = EventPacket.ReplyStatus(reply);
        if (status != EventPacket.StatusOk)
        {
            logger.Warn($"Wacom PrecisionMode: driver replied with status 0x{status:x4}");
            return false;
        }

        _precisionOn = engage;
        logger.Info($"Wacom PrecisionMode: tablet {tablet}, {(engage ? "on" : "off")}");
        return true;
    }

    public void Dispose() { }

    /// <summary>
    /// Advances Display Toggle: a Set of 0x10b on driver → tablet → context manager, carrying true.
    /// Addressed this way the context manager picks the tablet's active (front app) context, and
    /// the driver re-maps the pen itself. A Get on the same path first proves it is routed: the driver echoes
    /// an unrouted packet back empty with status 0. The target (0x311) is logged before and after.
    /// </summary>
    private bool AdvanceDisplayToggle(uint tablet)
    {
        var state = Send(EventPacket.Build(EventPacket.Command.Get, ActiveContext(tablet, EventPacket.Id.DisplayToggleParameter)));
        if (state is null) return false;
        if (!EventPacket.HasData(state))
        {
            logger.Warn("Wacom DisplayToggle: the driver did not answer for Display Toggle (unsupported driver version?)");
            return false;
        }

        var before = CurrentTarget(tablet);
        var reply = Send(EventPacket.Build(EventPacket.Command.Set, ActiveContext(tablet, EventPacket.Id.DisplayToggleParameter), true));
        if (reply is null) return false;

        var status = EventPacket.ReplyStatus(reply);
        if (status != EventPacket.StatusOk)
        {
            logger.Warn($"Wacom DisplayToggle: driver replied with status 0x{status:x4}");
            return false;
        }

        logger.Info($"Wacom DisplayToggle: tablet {tablet}, target {before ?? "?"} -> {CurrentTarget(tablet) ?? "?"}");
        return true;
    }

    private byte[]? TabletValue(int index, ushort parameter) =>
        Send(EventPacket.Build(EventPacket.Command.Get,
            [new(EventPacket.Id.Driver), new(EventPacket.Id.Tablet, Reference: (uint)index), new(parameter)]));

    private bool ReadFlag(int index, ushort parameter) =>
        TabletValue(index, parameter) is { } reply && EventPacket.ReadByte(reply) is > 0;

    /// <summary>Driver → tablet → its active context → <paramref name="parameter"/>.</summary>
    private static EventPacket.Key[] ActiveContext(uint tablet, ushort parameter) =>
        [new(EventPacket.Id.Driver), new(EventPacket.Id.Tablet, Reference: tablet), new(EventPacket.Id.ContextManager), new(parameter)];

    /// <summary>The active context's current toggle target, e.g. "3" or "Desktop". Unlike 0x10b it needs the overlay key.</summary>
    private string? CurrentTarget(uint tablet)
    {
        EventPacket.Key[] path =
        [
            new(EventPacket.Id.Driver), new(EventPacket.Id.Tablet, Reference: tablet), new(EventPacket.Id.ContextManager),
            new(EventPacket.Id.DisplayToggleObject), new(EventPacket.Id.DisplayToggleTarget)
        ];
        return Send(EventPacket.Build(EventPacket.Command.Get, path)) is { } reply ? EventPacket.ReadString(reply) : null;
    }

    /// <summary>Sends one packet and returns the driver's reply, or null when it couldn't be delivered.</summary>
    private byte[]? Send(byte[] packet)
    {
        var port = OpenPort();
        if (port == IntPtr.Zero)
        {
            logger.Warn($"Wacom: driver port {PortName} not found; is the Wacom driver running?");
            return null;
        }

        var data = IntPtr.Zero;
        try
        {
            data = CoreFoundation.CFDataCreate(IntPtr.Zero, packet, packet.Length);
            var status = CoreFoundation.CFMessagePortSendRequest(
                port, EventPacket.MsgId, data, SendTimeout, ReplyTimeout, CoreFoundation.DefaultRunLoopMode, out var reply);
            if (status == 0) return CoreFoundation.TakeData(reply);

            logger.Warn($"Wacom: CFMessagePortSendRequest returned {status}");
            return null;
        }
        finally
        {
            if (data != IntPtr.Zero) CoreFoundation.CFRelease(data);
            CoreFoundation.CFRelease(port);
        }
    }

    private static IntPtr OpenPort()
    {
        var name = CoreFoundation.CreateString(PortName);
        try
        {
            return CoreFoundation.CFMessagePortCreateRemote(IntPtr.Zero, name);
        }
        finally
        {
            CoreFoundation.CFRelease(name);
        }
    }
}

/// <summary>The few CoreFoundation calls the driver bridge needs.</summary>
internal static partial class CoreFoundation
{
    private const string Lib = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    private const uint Utf8 = 0x08000100;

    [LibraryImport(Lib)]
    public static partial IntPtr CFMessagePortCreateRemote(IntPtr allocator, IntPtr name);

    [LibraryImport(Lib)]
    public static partial int CFMessagePortSendRequest(
        IntPtr remote, int msgid, IntPtr data, double sendTimeout, double rcvTimeout, IntPtr replyMode, out IntPtr returnData);

    [LibraryImport(Lib)]
    public static partial IntPtr CFDataCreate(IntPtr allocator, byte[] bytes, nint length);

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    private static partial IntPtr CFStringCreateWithCString(IntPtr allocator, string text, uint encoding);

    [LibraryImport(Lib)]
    private static partial nint CFDataGetLength(IntPtr data);

    [LibraryImport(Lib)]
    private static partial IntPtr CFDataGetBytePtr(IntPtr data);

    [LibraryImport(Lib)]
    public static partial void CFRelease(IntPtr handle);

    /// <summary>kCFRunLoopDefaultMode; a reply mode is needed for the driver's reply to be waited for.</summary>
    public static IntPtr DefaultRunLoopMode { get; } = CreateString("kCFRunLoopDefaultMode");

    public static IntPtr CreateString(string text) => CFStringCreateWithCString(IntPtr.Zero, text, Utf8);

    /// <summary>Copies a CFData's bytes and releases it. A null handle gives an empty array.</summary>
    public static byte[] TakeData(IntPtr data)
    {
        if (data == IntPtr.Zero) return [];
        try
        {
            var bytes = new byte[CFDataGetLength(data)];
            Marshal.Copy(CFDataGetBytePtr(data), bytes, 0, bytes.Length);
            return bytes;
        }
        finally
        {
            CFRelease(data);
        }
    }
}
