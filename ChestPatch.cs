using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;

namespace Sts2Unlimited;

public static class ChestPatch
{
    // Cached by RegisterReflectionCache() — called once at mod load
    private static FieldInfo _multiplayerHoldersField;
    private static Type _relicHolderType;

    // Cached RunManager reflection
    private static Type _runManagerType;
    private static PropertyInfo _instanceProp;
    private static PropertyInfo _numPlayersProp;

    private static readonly MethodInfo _safeGetHolderGeneric =
        typeof(ChestPatch).GetMethod(nameof(SafeGetHolder), BindingFlags.Public | BindingFlags.Static);
    private static readonly MethodInfo _getLoopCountMethod =
        typeof(ChestPatch).GetMethod(nameof(GetLoopCount), BindingFlags.Public | BindingFlags.Static);

    /// <summary>
    /// Resolves types and fields once at mod load.
    /// </summary>
    public static void RegisterReflectionCache()
    {
        // -- RunManager --
        _runManagerType = Type.GetType("MegaCrit.Sts2.Core.Runs.RunManager, sts2", false);
        if (_runManagerType != null)
        {
            _instanceProp = _runManagerType.GetProperty("Instance",
                BindingFlags.Public | BindingFlags.Static);
            _numPlayersProp = _runManagerType.GetProperty("NumPlayers",
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Static);
        }
        Log.LogMessage(LogLevel.Info, LogType.Generic,
            $"[ChestPatch] RunManager: Instance={_instanceProp?.Name ?? "null"}, NumPlayers={_numPlayersProp?.Name ?? "null"}");

        // -- NTreasureRoomRelicCollection --
        var collectionType = Type.GetType(
            "MegaCrit.Sts2.Core.Nodes.Rooms.NTreasureRoomRelicCollection, sts2", false);

        if (collectionType == null)
        {
            // Fallback: search all loaded assemblies
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                collectionType = asm.GetType("MegaCrit.Sts2.Core.Nodes.Rooms.NTreasureRoomRelicCollection");
                if (collectionType != null) break;
            }
        }

        if (collectionType == null)
        {
            Log.LogMessage(LogLevel.Warn, LogType.Generic,
                "[ChestPatch] NTreasureRoomRelicCollection not found — chest patch will be skipped");
            return;
        }

        _multiplayerHoldersField = collectionType.GetField("_multiplayerHolders",
            BindingFlags.NonPublic | BindingFlags.Instance);

        if (_multiplayerHoldersField == null)
        {
            Log.LogMessage(LogLevel.Warn, LogType.Generic,
                "[ChestPatch] _multiplayerHolders field not found on NTreasureRoomRelicCollection");
            return;
        }

        var genericArgs = _multiplayerHoldersField.FieldType.GetGenericArguments();
        _relicHolderType = genericArgs.Length > 0 ? genericArgs[0] : null;

        Log.LogMessage(LogLevel.Info, LogType.Generic,
            $"[ChestPatch] _multiplayerHolders: {_multiplayerHoldersField.FieldType.Name}, " +
            $"holder type: {_relicHolderType?.Name ?? "unknown"}");
    }

    public static FieldInfo GetMultiplayerHoldersField() => _multiplayerHoldersField;

    public static MethodInfo GetInitializeRelicsMethod()
    {
        if (_multiplayerHoldersField == null) return null;
        return _multiplayerHoldersField.DeclaringType?.GetMethod(
            "InitializeRelics",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
    }

    /// <summary>
    /// Returns the actual number of players in the current run.
    /// Used as the loop bound in InitializeRelics so every player gets a relic holder.
    /// </summary>
    public static int GetLoopCount()
    {
        try
        {
            if (_instanceProp != null && _numPlayersProp != null)
            {
                var rm = _instanceProp.GetValue(null);
                if (rm != null)
                {
                    int count = (int)_numPlayersProp.GetValue(rm);
                    Log.LogMessage(LogLevel.Debug, LogType.Generic,
                        $"[ChestPatch] GetLoopCount() = {count}");
                    return count;
                }
            }
        }
        catch (Exception e)
        {
            Log.LogMessage(LogLevel.Warn, LogType.Generic,
                $"[ChestPatch] GetLoopCount() failed: {e.Message}");
        }
        return Sts2Unlimited.MaxPlayersOverride;
    }

    /// <summary>
    /// Bounds-safe replacement for _multiplayerHolders[i].
    /// Players beyond the 4 hardcoded scene slots are assigned to the last valid holder so
    /// that their relic is initialised and they can pick, unblocking RelicPickingFinished().
    /// </summary>
    public static T SafeGetHolder<T>(List<T> holders, int index)
        => holders[Math.Min(index, holders.Count - 1)];

    [HarmonyTranspiler]
    public static IEnumerable<CodeInstruction> Transpile_InitializeRelics(
        IEnumerable<CodeInstruction> instructions)
    {
        if (_multiplayerHoldersField == null || _relicHolderType == null)
        {
            Log.LogMessage(LogLevel.Warn, LogType.Generic,
                "[ChestPatch] Reflection cache not ready — InitializeRelics transpiler skipped");
            return instructions;
        }

        var codes = new List<CodeInstruction>(instructions);
        var holderListType = _multiplayerHoldersField.FieldType;
        var getCountMethod = holderListType.GetMethod("get_Count");
        var getItemMethod = holderListType.GetMethod("get_Item");
        var safeGetHolder = _safeGetHolderGeneric.MakeGenericMethod(_relicHolderType);

        bool patchedCount = false;
        int patchedItems = 0;

        for (int i = 0; i < codes.Count - 1; i++)
        {
            // Pattern A: replace loop upper-bound from _multiplayerHolders.Count to GetLoopCount()
            //
            // Target IL:
            //   ldfld  _multiplayerHolders   ← codes[i]
            //   callvirt get_Count           ← codes[i+1]
            //
            // Replacement:
            //   pop                          ← discard the 'this' that ldfld would have consumed
            //   call GetLoopCount            ← pushes actual player count
            if (!patchedCount &&
                codes[i].opcode == OpCodes.Ldfld &&
                codes[i].operand is FieldInfo fa && fa == _multiplayerHoldersField &&
                i + 1 < codes.Count &&
                codes[i + 1].opcode == OpCodes.Callvirt &&
                codes[i + 1].operand is MethodInfo ma && ma == getCountMethod)
            {
                codes[i] = new CodeInstruction(OpCodes.Pop);
                codes[i + 1] = new CodeInstruction(OpCodes.Call, _getLoopCountMethod);
                patchedCount = true;
                Log.LogMessage(LogLevel.Debug, LogType.Generic,
                    "[ChestPatch] Patched loop bound in InitializeRelics");
                i++; // skip the instruction we just rewrote
                continue;
            }

            // Pattern B: replace bounds-unsafe _multiplayerHolders[i] with SafeGetHolder
            //
            // Target IL:
            //   ldfld  _multiplayerHolders   ← codes[i]
            //   ldloc  <loop index>          ← codes[i+1]  (any ldloc variant)
            //   callvirt get_Item            ← codes[i+2]
            //
            // Replacement: just swap callvirt get_Item → call SafeGetHolder<T>
            // (stack layout is identical: [list, index] → [item])
            if (i + 2 < codes.Count &&
                codes[i].opcode == OpCodes.Ldfld &&
                codes[i].operand is FieldInfo fb && fb == _multiplayerHoldersField &&
                codes[i + 2].opcode == OpCodes.Callvirt &&
                codes[i + 2].operand is MethodInfo mb && mb == getItemMethod)
            {
                codes[i + 2] = new CodeInstruction(OpCodes.Call, safeGetHolder);
                patchedItems++;
            }
        }

        if (!patchedCount)
            Log.LogMessage(LogLevel.Warn, LogType.Generic,
                "[ChestPatch] Loop bound pattern not found in InitializeRelics — deadlock fix may be ineffective");
        if (patchedItems == 0)
            Log.LogMessage(LogLevel.Warn, LogType.Generic,
                "[ChestPatch] Holder access pattern not found in InitializeRelics");
        else
            Log.LogMessage(LogLevel.Info, LogType.Generic,
                $"[ChestPatch] Patched {patchedItems} holder access(es) in InitializeRelics (chest fix)");

        return codes;
    }
}
