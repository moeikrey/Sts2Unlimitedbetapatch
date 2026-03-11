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
    private static PropertyInfo _stateProp;
    private static PropertyInfo _playersListProp;

    // Cached holder Index setter
    private static MethodInfo _indexSetter;

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
            _stateProp = _runManagerType.GetProperty("State",
                BindingFlags.Public | BindingFlags.Instance);
            // Players is declared on IRunState; get it from the property's return type
            _playersListProp = _stateProp?.PropertyType.GetProperty("Players",
                BindingFlags.Public | BindingFlags.Instance);
        }
        Log.LogMessage(LogLevel.Info, LogType.Generic,
            $"[ChestPatch] RunManager cache: Instance={_instanceProp?.Name ?? "null"}, " +
            $"State={_stateProp?.Name ?? "null"}, Players={_playersListProp?.Name ?? "null"}");

        // -- NTreasureRoomRelicCollection --
        var collectionType = Type.GetType(
            "MegaCrit.Sts2.Core.Nodes.Screens.TreasureRoomRelic.NTreasureRoomRelicCollection, sts2", false);

        if (collectionType == null)
        {
            // Fallback: search all loaded assemblies by simple name
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                collectionType = asm.GetType("MegaCrit.Sts2.Core.Nodes.Screens.TreasureRoomRelic.NTreasureRoomRelicCollection");
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

        _indexSetter = _relicHolderType?.GetMethod("set_Index",
            BindingFlags.Public | BindingFlags.Instance);

        Log.LogMessage(LogLevel.Info, LogType.Generic,
            $"[ChestPatch] _multiplayerHolders: {_multiplayerHoldersField.FieldType.Name}, " +
            $"holder type: {_relicHolderType?.Name ?? "unknown"}, " +
            $"set_Index: {(_indexSetter != null ? "found" : "null")}");
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
            if (_instanceProp != null && _stateProp != null && _playersListProp != null)
            {
                var rm = _instanceProp.GetValue(null);
                if (rm != null)
                {
                    var state = _stateProp.GetValue(rm);
                    if (state != null)
                    {
                        var players = _playersListProp.GetValue(state);
                        if (players != null)
                        {
                            int count = (int)players.GetType()
                                .GetProperty("Count")!.GetValue(players)!;
                            Log.LogMessage(LogLevel.Debug, LogType.Generic,
                                $"[ChestPatch] GetLoopCount() = {count}");
                            return count;
                        }
                    }
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
    /// Runs before InitializeRelics. If _multiplayerHolders has fewer entries than the
    /// current player count, duplicates the last holder node for each extra player and
    /// appends it to the list so the transpiler's extended loop has a real node per player.
    /// </summary>
    public static void Prefix_InitializeRelics(object __instance)
    {
        try
        {
            if (_multiplayerHoldersField == null) return;

            var holders = _multiplayerHoldersField.GetValue(__instance) as System.Collections.IList;
            if (holders == null || holders.Count == 0) return;

            int numPlayers = GetLoopCount();
            if (holders.Count >= numPlayers) return;

            var lastHolder = holders[holders.Count - 1] as Godot.Node;
            if (lastHolder == null) return;

            var parent = lastHolder.GetParent();
            if (parent == null)
            {
                Log.LogMessage(LogLevel.Warn, LogType.Generic,
                    "[ChestPatch] Last holder has no parent — cannot duplicate holders");
                return;
            }

            // Arrange all holders in a circle around the center of the parent.
            var parentCtrl = parent as Godot.Control;
            var holderSize = (holders[0] as Godot.Control)?.Size ?? Godot.Vector2.Zero;

            Godot.Vector2 center = parentCtrl != null
                ? parentCtrl.Size / 2f
                : holderSize / 2f;

            // Radius: half the shorter dimension of the parent, inset slightly.
            float radius = parentCtrl != null
                ? Math.Min(parentCtrl.Size.X, parentCtrl.Size.Y) * 0.35f
                : 250f;

            Log.LogMessage(LogLevel.Info, LogType.Generic,
                $"[ChestPatch] Circle layout: center={center}, radius={radius}, n={numPlayers}");

            // Duplicate extra holders first so we can position everything in one loop.
            int startIndex = holders.Count;
            for (int i = startIndex; i < numPlayers; i++)
            {
                var duplicate = lastHolder.Duplicate();
                if (duplicate == null) continue;
                parent.AddChild(duplicate);
                _indexSetter?.Invoke(duplicate, new object[] { i });
                holders.Add(duplicate);
            }

            // Position all holders evenly around the circle, starting at the top.
            for (int i = 0; i < numPlayers; i++)
            {
                if (holders[i] is not Godot.Control ctrl) continue;
                float angle = -MathF.PI / 2f + 2f * MathF.PI * i / numPlayers;
                ctrl.Position = center
                    + new Godot.Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius
                    - holderSize / 2f;
            }

            Log.LogMessage(LogLevel.Info, LogType.Generic,
                $"[ChestPatch] Extended _multiplayerHolders from {startIndex} to {numPlayers}");
        }
        catch (Exception e)
        {
            Log.LogMessage(LogLevel.Warn, LogType.Generic,
                $"[ChestPatch] Prefix_InitializeRelics failed: {e.Message}");
        }
    }

    [HarmonyTranspiler]
    public static IEnumerable<CodeInstruction> Transpile_InitializeRelics(
        IEnumerable<CodeInstruction> instructions)
    {
        if (_multiplayerHoldersField == null)
        {
            Log.LogMessage(LogLevel.Warn, LogType.Generic,
                "[ChestPatch] Reflection cache not ready — InitializeRelics transpiler skipped");
            return instructions;
        }

        var codes = new List<CodeInstruction>(instructions);
        var getCountMethod = _multiplayerHoldersField.FieldType.GetMethod("get_Count");

        // Replace loop upper-bound: _multiplayerHolders.Count → GetLoopCount()
        //
        // Target IL:
        //   ldfld  _multiplayerHolders   ← codes[i]
        //   callvirt get_Count           ← codes[i+1]
        //
        // Replacement:
        //   pop                          ← discard the 'this' ldfld would have consumed
        //   call GetLoopCount            ← pushes actual player count
        for (int i = 0; i < codes.Count - 1; i++)
        {
            if (codes[i].opcode == OpCodes.Ldfld &&
                codes[i].operand is FieldInfo f && f == _multiplayerHoldersField &&
                codes[i + 1].opcode == OpCodes.Callvirt &&
                codes[i + 1].operand is MethodInfo m && m == getCountMethod)
            {
                codes[i]     = new CodeInstruction(OpCodes.Pop);
                codes[i + 1] = new CodeInstruction(OpCodes.Call, _getLoopCountMethod);
                Log.LogMessage(LogLevel.Info, LogType.Generic,
                    "[ChestPatch] Patched loop bound in InitializeRelics");
                return codes;
            }
        }

        Log.LogMessage(LogLevel.Warn, LogType.Generic,
            "[ChestPatch] Loop bound pattern not found in InitializeRelics — deadlock fix may be ineffective");
        return codes;
    }
}
