namespace Shuush;

/// <summary>Application entry point.</summary>
internal static class Program
{
    [STAThread]
    private static void Main()
    {
        // Single instance: a second launch exits silently so only one tray icon
        // and one MuteMe owner exist.
        using Mutex mutex = new(initiallyOwned: true, "Local\\shuush-singleton", out bool createdNew);
        if (!createdNew)
        {
            return;
        }

        ApplicationConfiguration.Initialize();
        using TrayContext context = new();
        Application.Run(context);
    }
}
