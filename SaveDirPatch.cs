using System.Linq;
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Saves;

namespace Sts2Unlimited;

public static class SaveDirPatch
{
    public static bool Prefix_ConstructDefault(ref SaveManager __result)
    {
        string saveDir = CommandLineHelper.GetValue("save-dir");

        if (string.IsNullOrWhiteSpace(saveDir))
            return true; // no arg — let original run

        Log.LogMessage(LogLevel.Info, LogType.Generic,
            $"[SaveDirPatch] --save-dir detected: '{saveDir}'. Redirecting all save I/O.");

        ISaveStore store = new GodotFileIo(saveDir);
        __result = new SaveManager(store);
        return false; // skip original ConstructDefault
    }

    public static void ReplaceInstance()
    {
        string saveDir = CommandLineHelper.GetValue("save-dir");
        if (string.IsNullOrWhiteSpace(saveDir))
            return;

        var instance = SaveManager.Instance;
        if (instance == null)
        {
            Log.LogMessage(LogLevel.Warn, LogType.Generic,
                "[SaveDirPatch] SaveManager.Instance is null — live store replacement skipped");
            return;
        }

        // Swap the ISaveStore field on the existing instance rather than replacing the whole
        // singleton. This preserves already-loaded state (settings, locale prefs, etc.) so that
        // subsequent game systems (LocManager, etc.) can still read from it — only future
        // reads/writes are redirected to the custom path.
        var storeField = typeof(SaveManager)
            .GetFields(BindingFlags.NonPublic | BindingFlags.Instance)
            .FirstOrDefault(f => typeof(ISaveStore).IsAssignableFrom(f.FieldType));

        if (storeField == null)
        {
            Log.LogMessage(LogLevel.Warn, LogType.Generic,
                "[SaveDirPatch] ISaveStore field not found on SaveManager — live store replacement skipped");
            return;
        }

        storeField.SetValue(instance, new GodotFileIo(saveDir));
        Log.LogMessage(LogLevel.Info, LogType.Generic,
            $"[SaveDirPatch] SaveManager store swapped — all save I/O now goes to '{saveDir}'");
    }
}
