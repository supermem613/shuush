using Microsoft.Win32;

namespace Shuush;

/// <summary>
/// Cheap "is Teams in a call" gate based on Windows microphone usage tracking.
///
/// Windows records per-app capture state under the CapabilityAccessManager
/// ConsentStore: while an app is capturing the microphone, LastUsedTimeStop is 0;
/// once it stops, that value becomes a real FILETIME. Teams holds the mic open
/// for many calls even while muted, but some meetings leave this history stale.
///
/// This is a positive activity hint, not proof of call absence. UI Automation
/// remains authoritative. Reads that fail are treated as "active".
/// </summary>
internal static class CallActivityProbe
{
    private const string MicrophonePath =
        @"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\microphone";

    /// <summary>Registry path of the per-app microphone consent store, under HKCU.</summary>
    internal const string MicrophoneKeyPath = MicrophonePath;

    /// <summary>Whether a Teams app is currently capturing the microphone.</summary>
    public static bool IsTeamsMicActive()
    {
        try
        {
            using RegistryKey? microphone = Registry.CurrentUser.OpenSubKey(MicrophonePath);
            if (microphone is null)
            {
                return true;
            }

            if (AnyTeamsCapturing(microphone))
            {
                return true;
            }

            using RegistryKey? nonPackaged = microphone.OpenSubKey("NonPackaged");
            return nonPackaged is not null && AnyTeamsCapturing(nonPackaged);
        }
        catch (System.Security.SecurityException)
        {
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return true;
        }
        catch (IOException)
        {
            return true;
        }
    }

    private static bool AnyTeamsCapturing(RegistryKey parent)
    {
        foreach (string name in parent.GetSubKeyNames())
        {
            if (name.IndexOf("teams", StringComparison.OrdinalIgnoreCase) < 0)
            {
                continue;
            }

            using RegistryKey? app = parent.OpenSubKey(name);
            if (app is null)
            {
                continue;
            }

            // REG_QWORD reads back as a boxed long. Stop == 0 means capturing now.
            if (app.GetValue("LastUsedTimeStop") is long stop && stop == 0)
            {
                return true;
            }
        }

        return false;
    }
}
