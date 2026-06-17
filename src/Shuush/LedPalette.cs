using System.Drawing;

namespace Shuush;

/// <summary>
/// Maps the small set of colors the MuteMe LED can display to its HID command
/// byte and to a matching <see cref="Color"/> for the tray icon. The MuteMe
/// command byte is a 3-bit color bitmask (red=1 green=2 blue=4), so only these
/// seven combinations are physically reproducible on the device.
/// </summary>
internal static class LedPalette
{
    /// <summary>The color names offered to the user, in menu order.</summary>
    public static IReadOnlyList<string> Names { get; } = new[]
    {
        "Red", "Green", "Blue", "Yellow", "Cyan", "Purple", "White",
    };

    /// <summary>Returns the MuteMe HID command byte for a palette name.</summary>
    public static byte ToCommand(string name) => name switch
    {
        "Red" => MuteMeDevice.Red,
        "Green" => MuteMeDevice.Green,
        "Blue" => MuteMeDevice.Blue,
        "Yellow" => MuteMeDevice.Yellow,
        "Cyan" => MuteMeDevice.Cyan,
        "Purple" => MuteMeDevice.Purple,
        "White" => MuteMeDevice.White,
        _ => MuteMeDevice.Red,
    };

    /// <summary>Returns the tray-icon color that matches a palette name.</summary>
    public static Color ToColor(string name) => name switch
    {
        "Red" => Color.FromArgb(220, 40, 40),
        "Green" => Color.FromArgb(40, 190, 70),
        "Blue" => Color.FromArgb(50, 110, 230),
        "Yellow" => Color.FromArgb(225, 190, 40),
        "Cyan" => Color.FromArgb(40, 200, 210),
        "Purple" => Color.FromArgb(170, 70, 210),
        "White" => Color.FromArgb(235, 235, 235),
        _ => Color.FromArgb(220, 40, 40),
    };
}
