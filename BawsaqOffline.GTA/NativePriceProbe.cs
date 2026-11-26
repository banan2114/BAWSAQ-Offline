using System;
using System.IO;
using System.Windows.Forms;

using GTA;
using GTA.Native;

public sealed class NativePriceProbe : Script
{
    private const string StatName = "SM_PRICE_FRT";

    private const ulong StatGetFloatHash = 0xD7AE6C9C9C6AC54CUL;
    private const ulong StatSetFloatHash = 0x4851997F37FE9B3CUL;

    private readonly uint statHash;
    private readonly string logPath;

    private bool originalRead;
    private bool testApplied;
    private float originalValue;

    public NativePriceProbe()
    {
        statHash = StringHash.AtStringHashUtf8(StatName);

        string directory = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "BAWSAQOffline"
        );

        Directory.CreateDirectory(directory);
        logPath = Path.Combine(directory, "price-probe.log");

        originalRead = TryGetPrice(out originalValue);

        Log(
            "INIT | stat=" + StatName +
            " | hash=0x" + statHash.ToString("X8") +
            " | read=" + originalRead +
            " | value=" + originalValue.ToString("R")
        );

        if (originalRead)
        {
            GTA.UI.Screen.ShowSubtitle(
                "BAWSAQ probe: " + StatName +
                " = " + originalValue.ToString("0.00") +
                " | F8 test, F9 restore",
                7000
            );
        }
        else
        {
            GTA.UI.Screen.ShowSubtitle(
                "BAWSAQ probe: STAT_GET_FLOAT failed. See price-probe.log",
                7000
            );
        }

        KeyUp += OnKeyUp;
    }

    private void OnKeyUp(object sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.F8)
        {
            ApplyTestValue();
        }
        else if (e.KeyCode == Keys.F9)
        {
            RestoreOriginalValue();
        }
    }

    private void ApplyTestValue()
    {
        if (!originalRead)
        {
            GTA.UI.Screen.ShowSubtitle("BAWSAQ probe: no readable baseline", 4000);
            return;
        }

        if (testApplied)
        {
            GTA.UI.Screen.ShowSubtitle("BAWSAQ probe: test value already applied", 3000);
            return;
        }

        // If the offline stat is currently zero, use a recognizable test price.
        // The original value is kept in memory and can be restored with F9.
        float testValue = originalValue > 0f
            ? originalValue * 1.10f
            : 100f;

        bool writeSucceeded = TrySetPrice(testValue);
        bool readSucceeded = TryGetPrice(out float actualValue);

        testApplied = writeSucceeded && readSucceeded &&
            Math.Abs(actualValue - testValue) < 0.001f;

        Log(
            "WRITE | requested=" + testValue.ToString("R") +
            " | nativeResult=" + writeSucceeded +
            " | readBackSucceeded=" + readSucceeded +
            " | readBack=" + actualValue.ToString("R") +
            " | verified=" + testApplied
        );

        GTA.UI.Screen.ShowSubtitle(
            testApplied
                ? "BAWSAQ probe: stat changed to " + actualValue.ToString("0.00") + " (F9 restores)"
                : "BAWSAQ probe: write/readback failed. See price-probe.log",
            5000
        );
    }

    private void RestoreOriginalValue()
    {
        if (!originalRead)
        {
            GTA.UI.Screen.ShowSubtitle("BAWSAQ probe: no original value to restore", 3000);
            return;
        }

        bool writeSucceeded = TrySetPrice(originalValue);
        bool readSucceeded = TryGetPrice(out float actualValue);

        bool restored = writeSucceeded && readSucceeded &&
            Math.Abs(actualValue - originalValue) < 0.001f;

        if (restored)
            testApplied = false;

        Log(
            "RESTORE | requested=" + originalValue.ToString("R") +
            " | nativeResult=" + writeSucceeded +
            " | readBackSucceeded=" + readSucceeded +
            " | readBack=" + actualValue.ToString("R") +
            " | verified=" + restored
        );

        GTA.UI.Screen.ShowSubtitle(
            restored
                ? "BAWSAQ probe: original value restored"
                : "BAWSAQ probe: restore failed. See price-probe.log",
            5000
        );
    }

    private bool TryGetPrice(out float value)
    {
        using (var output = new OutputArgument())
        {
            bool succeeded = Function.Call<bool>(
                (Hash)StatGetFloatHash,
                statHash,
                output,
                -1
            );

            value = succeeded ? output.GetResult<float>() : 0f;
            return succeeded;
        }
    }

    private bool TrySetPrice(float value)
    {
        return Function.Call<bool>(
            (Hash)StatSetFloatHash,
            statHash,
            value,
            false
        );
    }

    private void Log(string message)
    {
        try
        {
            File.AppendAllText(
                logPath,
                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") +
                " " + message + Environment.NewLine
            );
        }
        catch
        {
            // Logging must not interrupt the game script.
        }
    }
}
