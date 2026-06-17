using System.Text.Json;

namespace Shuush;

/// <summary>
/// User-adjustable settings, persisted as JSON under
/// %AppData%\shuush\config.json. Loading never throws: a missing or corrupt file
/// yields defaults.
/// </summary>
internal sealed class AppConfig
{
    /// <summary>Poll cadence while in a call, in milliseconds.</summary>
    public int PollIntervalMs { get; set; } = 750;

    /// <summary>Poll cadence while not in a call, in milliseconds. Slower to keep idle CPU low.</summary>
    public int IdleIntervalMs { get; set; } = 2000;

    /// <summary>Whether to drive the MuteMe LED at all.</summary>
    public bool DriveLed { get; set; } = true;

    /// <summary>Whether to apply the dim bit to the LED.</summary>
    public bool DimLed { get; set; }

    /// <summary>Whether the tray icon color follows the mute state.</summary>
    public bool MirrorTrayColor { get; set; } = true;

    /// <summary>Palette name used while muted.</summary>
    public string MutedColor { get; set; } = "Red";

    /// <summary>Palette name used while live (unmuted, in a call).</summary>
    public string LiveColor { get; set; } = "Green";

    /// <summary>Whether shuush launches at sign-in (HKCU Run key).</summary>
    public bool StartWithWindows { get; set; }

    /// <summary>Absolute path of the config file.</summary>
    public static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "shuush",
        "config.json");

    /// <summary>Loads settings, returning defaults if the file is missing or invalid.</summary>
    public static AppConfig Load()
    {
        try
        {
            string path = FilePath;
            if (File.Exists(path))
            {
                string json = File.ReadAllText(path);
                AppConfig? loaded = JsonSerializer.Deserialize<AppConfig>(json);
                if (loaded is not null)
                {
                    loaded.Normalize();
                    return loaded;
                }
            }
        }
        catch (IOException)
        {
        }
        catch (JsonException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        return new AppConfig();
    }

    /// <summary>Persists settings. Failures are swallowed; settings are best-effort.</summary>
    public void Save()
    {
        this.Normalize();
        try
        {
            string path = FilePath;
            string? dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            string json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    /// <summary>Returns a deep copy for editing in the settings dialog.</summary>
    public AppConfig Clone() => new()
    {
        PollIntervalMs = this.PollIntervalMs,
        IdleIntervalMs = this.IdleIntervalMs,
        DriveLed = this.DriveLed,
        DimLed = this.DimLed,
        MirrorTrayColor = this.MirrorTrayColor,
        MutedColor = this.MutedColor,
        LiveColor = this.LiveColor,
        StartWithWindows = this.StartWithWindows,
    };

    private void Normalize()
    {
        this.PollIntervalMs = Math.Clamp(this.PollIntervalMs, 200, 5000);
        this.IdleIntervalMs = Math.Clamp(this.IdleIntervalMs, this.PollIntervalMs, 10000);
        if (!LedPalette.Names.Contains(this.MutedColor))
        {
            this.MutedColor = "Red";
        }

        if (!LedPalette.Names.Contains(this.LiveColor))
        {
            this.LiveColor = "Green";
        }
    }
}
