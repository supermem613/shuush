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
            ActiveCallHoldsLiveAcrossTransientMissedPoll();
            Console.WriteLine("PASS ActiveCallHoldsLiveAcrossTransientMissedPoll");
            ActiveCallHoldsMutedAcrossTransientMissedPoll();
            Console.WriteLine("PASS ActiveCallHoldsMutedAcrossTransientMissedPoll");
            EndedCallReportsNoCall();
            Console.WriteLine("PASS EndedCallReportsNoCall");
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
