using System.Reflection;
using System.Windows.Forms;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        try
        {
            SettingsDialogPersistsDisplayedNoCallColor();
            Console.WriteLine("PASS SettingsDialogPersistsDisplayedNoCallColor");
            SettingsDialogOkPersistsNoCallColorAcrossReload();
            Console.WriteLine("PASS SettingsDialogOkPersistsNoCallColorAcrossReload");
            SettingsDialogPersistsAutoMuteOnMeetingStart();
            Console.WriteLine("PASS SettingsDialogPersistsAutoMuteOnMeetingStart");
            AutoMutePersistsAcrossSaveAndLoad();
            Console.WriteLine("PASS AutoMutePersistsAcrossSaveAndLoad");
            ActiveCallHoldsLiveAcrossTransientMissedPoll();
            Console.WriteLine("PASS ActiveCallHoldsLiveAcrossTransientMissedPoll");
            ActiveCallHoldsMutedAcrossTransientMissedPoll();
            Console.WriteLine("PASS ActiveCallHoldsMutedAcrossTransientMissedPoll");
            EndedCallReportsNoCall();
            Console.WriteLine("PASS EndedCallReportsNoCall");
            AutoMuteFiresOnObservedMeetingStartWhenEnabled();
            Console.WriteLine("PASS AutoMuteFiresOnObservedMeetingStartWhenEnabled");
            AutoMuteSkipsWhenDisabled();
            Console.WriteLine("PASS AutoMuteSkipsWhenDisabled");
            AutoMuteSkipsWhenJoiningMuted();
            Console.WriteLine("PASS AutoMuteSkipsWhenJoiningMuted");
            AutoMuteSkipsManualUnmuteMidMeeting();
            Console.WriteLine("PASS AutoMuteSkipsManualUnmuteMidMeeting");
            AutoMuteSkipsDelayedLiveAfterMicAlreadyActive();
            Console.WriteLine("PASS AutoMuteSkipsDelayedLiveAfterMicAlreadyActive");
            AutoMuteSkipsWhenLaunchingIntoCall();
            Console.WriteLine("PASS AutoMuteSkipsWhenLaunchingIntoCall");
            AutoMuteSkipsRightAfterUnpause();
            Console.WriteLine("PASS AutoMuteSkipsRightAfterUnpause");
            AutoMuteRetriesAfterTransientToggleMiss();
            Console.WriteLine("PASS AutoMuteRetriesAfterTransientToggleMiss");
            AutoMuteClearsOnceMuted();
            Console.WriteLine("PASS AutoMuteClearsOnceMuted");
            AutoMuteClearsWhenCallEnds();
            Console.WriteLine("PASS AutoMuteClearsWhenCallEnds");
            AutoMuteDoesNotReArmAfterManualUnmute();
            Console.WriteLine("PASS AutoMuteDoesNotReArmAfterManualUnmute");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static void ActiveCallHoldsLiveAcrossTransientMissedPoll()
    {
        MuteState resolved = ResolveCallState(micActive: true, polled: MuteState.NoCall, last: MuteState.Live, haveLast: true);
        if (resolved != MuteState.Live)
        {
            throw new InvalidOperationException(
                $"A transient missed poll during an active call must hold Live, but resolved to {resolved}.");
        }
    }

    private static void ActiveCallHoldsMutedAcrossTransientMissedPoll()
    {
        MuteState resolved = ResolveCallState(micActive: true, polled: MuteState.NoCall, last: MuteState.Muted, haveLast: true);
        if (resolved != MuteState.Muted)
        {
            throw new InvalidOperationException(
                $"A transient missed poll during an active call must hold Muted, but resolved to {resolved}.");
        }
    }

    private static void EndedCallReportsNoCall()
    {
        MuteState resolved = ResolveCallState(micActive: false, polled: MuteState.NoCall, last: MuteState.Live, haveLast: true);
        if (resolved != MuteState.NoCall)
        {
            throw new InvalidOperationException(
                $"When the mic gate reports the call ended, the state must be NoCall, but resolved to {resolved}.");
        }
    }

    private static void AutoMuteFiresOnObservedMeetingStartWhenEnabled()
    {
        bool should = ShouldMuteAtMeetingStart(enabled: true, micBecameActive: true, wasPaused: false, state: MuteState.Live);
        if (!should)
        {
            throw new InvalidOperationException(
                "An observed mic-active -> live edge with auto-mute enabled must mute, but it did not.");
        }
    }

    private static void AutoMuteSkipsWhenDisabled()
    {
        bool should = ShouldMuteAtMeetingStart(enabled: false, micBecameActive: true, wasPaused: false, state: MuteState.Live);
        if (should)
        {
            throw new InvalidOperationException(
                "Auto-mute must not fire when the setting is disabled.");
        }
    }

    private static void AutoMuteSkipsWhenJoiningMuted()
    {
        bool should = ShouldMuteAtMeetingStart(enabled: true, micBecameActive: true, wasPaused: false, state: MuteState.Muted);
        if (should)
        {
            throw new InvalidOperationException(
                "Auto-mute must not fire when the meeting is joined already muted.");
        }
    }

    private static void AutoMuteSkipsManualUnmuteMidMeeting()
    {
        bool should = ShouldMuteAtMeetingStart(enabled: true, micBecameActive: false, wasPaused: false, state: MuteState.Live);
        if (should)
        {
            throw new InvalidOperationException(
                "Auto-mute must not re-fire when the user manually unmutes mid-meeting.");
        }
    }

    private static void AutoMuteSkipsDelayedLiveAfterMicAlreadyActive()
    {
        bool should = ShouldMuteAtMeetingStart(enabled: true, micBecameActive: false, wasPaused: false, state: MuteState.Live);
        if (should)
        {
            throw new InvalidOperationException(
                "Auto-mute must not fire when the first live UI state appears after the mic was already active.");
        }
    }

    private static void AutoMuteSkipsWhenLaunchingIntoCall()
    {
        bool should = ShouldMuteAtMeetingStart(enabled: true, micBecameActive: false, wasPaused: false, state: MuteState.Live);
        if (should)
        {
            throw new InvalidOperationException(
                "Auto-mute must not fire when shuush launches into an already-running meeting.");
        }
    }

    private static void AutoMuteSkipsRightAfterUnpause()
    {
        bool should = ShouldMuteAtMeetingStart(enabled: true, micBecameActive: true, wasPaused: true, state: MuteState.Live);
        if (should)
        {
            throw new InvalidOperationException(
                "Auto-mute must not fire on the first poll after unpause (start not continuously observed).");
        }
    }

    private static bool ShouldMuteAtMeetingStart(bool enabled, bool micBecameActive, bool wasPaused, MuteState state)
    {
        Assembly assembly = Assembly.Load("shuush");
        Type resolverType = assembly.GetType("Shuush.AutoMuteResolver", throwOnError: true)!;
        Type stateType = assembly.GetType("Shuush.MuteState", throwOnError: true)!;
        object result = resolverType.GetMethod("ShouldMuteAtMeetingStart")!.Invoke(null, new object[]
        {
            enabled,
            micBecameActive,
            wasPaused,
            Enum.ToObject(stateType, (int)state),
        })!;
        return (bool)result;
    }

    private static void AutoMuteRetriesAfterTransientToggleMiss()
    {
        // First poll armed the intent and tried to mute but the toggle missed, so
        // the applied state is still Live. The intent must survive to retry.
        bool pending = ArmOrHoldPending(enabled: true, micBecameActive: false, wasPaused: false, state: MuteState.Live, pending: true);
        if (!pending)
        {
            throw new InvalidOperationException(
                "A pending auto-mute must be held across a transient toggle miss while the call stays live.");
        }
    }

    private static void AutoMuteClearsOnceMuted()
    {
        bool pending = ArmOrHoldPending(enabled: true, micBecameActive: false, wasPaused: false, state: MuteState.Muted, pending: true);
        if (pending)
        {
            throw new InvalidOperationException(
                "A pending auto-mute must clear once the call reads muted.");
        }
    }

    private static void AutoMuteClearsWhenCallEnds()
    {
        bool pending = ArmOrHoldPending(enabled: true, micBecameActive: false, wasPaused: false, state: MuteState.NoCall, pending: true);
        if (pending)
        {
            throw new InvalidOperationException(
                "A pending auto-mute must clear when the call ends.");
        }
    }

    private static void AutoMuteDoesNotReArmAfterManualUnmute()
    {
        // The call was muted (auto-mute already done, intent cleared) and the user
        // manually unmutes. Nothing armed it, so it must not fire again.
        bool pending = ArmOrHoldPending(enabled: true, micBecameActive: false, wasPaused: false, state: MuteState.Live, pending: false);
        if (pending)
        {
            throw new InvalidOperationException(
                "Auto-mute must not re-arm after a manual unmute mid-meeting.");
        }
    }

    private static bool ArmOrHoldPending(bool enabled, bool micBecameActive, bool wasPaused, MuteState state, bool pending)
    {
        Assembly assembly = Assembly.Load("shuush");
        Type resolverType = assembly.GetType("Shuush.AutoMuteResolver", throwOnError: true)!;
        Type stateType = assembly.GetType("Shuush.MuteState", throwOnError: true)!;
        object result = resolverType.GetMethod("ArmOrHoldPending")!.Invoke(null, new object[]
        {
            enabled,
            micBecameActive,
            wasPaused,
            Enum.ToObject(stateType, (int)state),
            pending,
        })!;
        return (bool)result;
    }

    private static MuteState ResolveCallState(bool micActive, MuteState polled, MuteState last, bool haveLast)
    {
        Assembly assembly = Assembly.Load("shuush");
        Type resolverType = assembly.GetType("Shuush.CallStateResolver", throwOnError: true)!;
        Type stateType = assembly.GetType("Shuush.MuteState", throwOnError: true)!;
        object result = resolverType.GetMethod("Resolve")!.Invoke(null, new object[]
        {
            micActive,
            Enum.ToObject(stateType, (int)polled),
            Enum.ToObject(stateType, (int)last),
            haveLast,
        })!;
        return (MuteState)(int)result;
    }

    private enum MuteState
    {
        NoCall,
        Live,
        Muted,
    }

    private static void SettingsDialogPersistsAutoMuteOnMeetingStart()
    {
        Assembly assembly = Assembly.Load("shuush");
        Type configType = assembly.GetType("Shuush.AppConfig", throwOnError: true)!;
        Type formType = assembly.GetType("Shuush.SettingsForm", throwOnError: true)!;

        object config = Activator.CreateInstance(configType)!;
        configType.GetProperty("AutoMuteOnMeetingStart")!.SetValue(config, false);

        using Form form = (Form)Activator.CreateInstance(formType)!;
        formType.GetMethod("Initialize")!.Invoke(form, new[] { config });

        CheckBox autoMuteInput = (CheckBox)formType
            .GetField("autoMuteInput", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(form)!;

        autoMuteInput.Checked = true;
        object updated = formType.GetProperty("UpdatedConfig")!.GetValue(form)!;
        bool saved = (bool)configType.GetProperty("AutoMuteOnMeetingStart")!.GetValue(updated)!;
        if (!saved)
        {
            throw new InvalidOperationException("Expected AutoMuteOnMeetingStart to be true after checking the box.");
        }
    }

    private static void AutoMutePersistsAcrossSaveAndLoad()
    {
        Assembly assembly = Assembly.Load("shuush");
        Type configType = assembly.GetType("Shuush.AppConfig", throwOnError: true)!;
        string configPath = (string)configType.GetProperty("FilePath")!.GetValue(null)!;
        string? originalJson = File.Exists(configPath) ? File.ReadAllText(configPath) : null;

        try
        {
            object config = Activator.CreateInstance(configType)!;
            configType.GetProperty("AutoMuteOnMeetingStart")!.SetValue(config, true);
            configType.GetMethod("Save")!.Invoke(config, null);

            object loaded = configType.GetMethod("Load")!.Invoke(null, null)!;
            bool loadedValue = (bool)configType.GetProperty("AutoMuteOnMeetingStart")!.GetValue(loaded)!;
            if (!loadedValue)
            {
                throw new InvalidOperationException("Expected AutoMuteOnMeetingStart to survive Save/Load, but it was false.");
            }
        }
        finally
        {
            if (originalJson is null)
            {
                File.Delete(configPath);
            }
            else
            {
                File.WriteAllText(configPath, originalJson);
            }
        }
    }

    private static void SettingsDialogPersistsDisplayedNoCallColor()
    {
        Assembly assembly = Assembly.Load("shuush");
        Type configType = assembly.GetType("Shuush.AppConfig", throwOnError: true)!;
        Type formType = assembly.GetType("Shuush.SettingsForm", throwOnError: true)!;

        object config = Activator.CreateInstance(configType)!;
        configType.GetProperty("NoCallColor")!.SetValue(config, "Off");

        using Form form = (Form)Activator.CreateInstance(formType)!;
        formType.GetMethod("Initialize")!.Invoke(form, new[] { config });

        ComboBox noCallColorInput = (ComboBox)formType
            .GetField("noCallColorInput", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(form)!;

        noCallColorInput.SelectedItem = "Blue";
        object updated = formType.GetProperty("UpdatedConfig")!.GetValue(form)!;
        string savedColor = (string)configType.GetProperty("NoCallColor")!.GetValue(updated)!;
        if (savedColor != "Blue")
        {
            throw new InvalidOperationException($"Expected NoCallColor to be Blue, but it was {savedColor}.");
        }
    }

    private static void SettingsDialogOkPersistsNoCallColorAcrossReload()
    {
        Assembly assembly = Assembly.Load("shuush");
        Type configType = assembly.GetType("Shuush.AppConfig", throwOnError: true)!;
        Type formType = assembly.GetType("Shuush.SettingsForm", throwOnError: true)!;
        string configPath = (string)configType.GetProperty("FilePath")!.GetValue(null)!;
        string? originalJson = File.Exists(configPath) ? File.ReadAllText(configPath) : null;

        try
        {
            object config = Activator.CreateInstance(configType)!;
            configType.GetProperty("NoCallColor")!.SetValue(config, "Off");
            configType.GetMethod("Save")!.Invoke(config, null);

            using Form form = (Form)Activator.CreateInstance(formType)!;
            formType.GetMethod("Initialize")!.Invoke(form, new[] { config });
            form.Shown += (_, _) =>
            {
                ComboBox noCallColorInput = GetNoCallColorInput(formType, form);
                noCallColorInput.SelectedItem = "Blue";
                GetButton(form, "OK").PerformClick();
            };

            if (form.ShowDialog() != DialogResult.OK)
            {
                throw new InvalidOperationException("Expected Settings dialog to close with OK.");
            }

            object updated = formType.GetProperty("UpdatedConfig")!.GetValue(form)!;
            string updatedColor = (string)configType.GetProperty("NoCallColor")!.GetValue(updated)!;
            if (updatedColor != "Blue")
            {
                throw new InvalidOperationException($"Expected OK to produce NoCallColor Blue, but it produced {updatedColor}.");
            }

            configType.GetMethod("Save")!.Invoke(updated, null);
            string savedJson = File.ReadAllText(configPath);
            if (!savedJson.Contains("\"NoCallColor\": \"Blue\"", StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Expected saved JSON to contain NoCallColor Blue, but it was:{Environment.NewLine}{savedJson}");
            }

            object loaded = configType.GetMethod("Load")!.Invoke(null, null)!;
            string loadedColor = (string)configType.GetProperty("NoCallColor")!.GetValue(loaded)!;
            if (loadedColor != "Blue")
            {
                throw new InvalidOperationException($"Expected loaded NoCallColor to be Blue, but it was {loadedColor}.");
            }

            using Form reopened = (Form)Activator.CreateInstance(formType)!;
            formType.GetMethod("Initialize")!.Invoke(reopened, new[] { loaded });

            string reopenedColor = GetNoCallColorInput(formType, reopened).SelectedItem as string ?? string.Empty;
            if (reopenedColor != "Blue")
            {
                throw new InvalidOperationException($"Expected reopened NoCallColor to be Blue, but it was {reopenedColor}.");
            }
        }
        finally
        {
            if (originalJson is null)
            {
                File.Delete(configPath);
            }
            else
            {
                File.WriteAllText(configPath, originalJson);
            }
        }
    }

    private static ComboBox GetNoCallColorInput(Type formType, Form form) => (ComboBox)formType
        .GetField("noCallColorInput", BindingFlags.Instance | BindingFlags.NonPublic)!
        .GetValue(form)!;

    private static Button GetButton(Control root, string text)
    {
        foreach (Control child in root.Controls)
        {
            if (child is Button button && button.Text == text)
            {
                return button;
            }

            Button? nested = TryGetButton(child, text);
            if (nested is not null)
            {
                return nested;
            }
        }

        throw new InvalidOperationException($"Could not find button {text}.");
    }

    private static Button? TryGetButton(Control root, string text)
    {
        foreach (Control child in root.Controls)
        {
            if (child is Button button && button.Text == text)
            {
                return button;
            }

            Button? nested = TryGetButton(child, text);
            if (nested is not null)
            {
                return nested;
            }
        }

        return null;
    }
}
