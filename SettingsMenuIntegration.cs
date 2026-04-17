using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;

namespace Sts2Unlimited;

// NSettingsSlider : Control
//   "Label"       → MegaRichTextLabel  (name; added by PARENT scene, missing from Duplicate(15))
//   "Slider"      → NSlider : Range    (handle position formula: _currentHandlePosition / MaxValue,
//                                        assumes MinValue=0; we use internal range [0,14] → display [2,16])
//   "SliderValue" → MegaLabel : Label  (value display)
//   "SelectionReticle" → NSelectionReticle
// NSlider._Ready: _handle = GetNode("%Handle")  ← scene-unique-name, breaks with Duplicate(7)
//   → use Duplicate(15) to keep NSlider working, copy "Label" from original manually.

public static class SettingsMenuIntegration
{
    // Internal NSlider range. Actual players = internalValue + PLAYER_OFFSET.
    private const int PLAYER_MIN    = 2;
    private const int PLAYER_MAX    = 16;
    private const int PLAYER_OFFSET = PLAYER_MIN;                   // 2
    private const int INTERNAL_MIN  = 0;
    private const int INTERNAL_MAX  = PLAYER_MAX - PLAYER_OFFSET;   // 14

    private static string SettingsPath => Path.Combine(
        Path.GetDirectoryName(typeof(Sts2Unlimited).Assembly.Location) ?? ".",
        "sts2unlimited.settings.json");

    public static void InitializeSettingsMenuUI(Harmony harmony)
    {
        try
        {
            var settingsScreenTypes = FindSettingsScreenTypes();
            if (settingsScreenTypes.Count == 0)
            {
                GD.PrintErr("[Sts2Unlimited] No settings screen types found.");
                return;
            }

            var postfix = new HarmonyMethod(typeof(SettingsMenuIntegration).GetMethod(
                nameof(Patch_NSettingsScreen_Ready),
                BindingFlags.NonPublic | BindingFlags.Static));

            int patched = 0;
            foreach (var screenType in settingsScreenTypes)
            {
                var readyMethod = screenType.GetMethod("_Ready",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                    null,
                    Type.EmptyTypes,
                    null);
                if (readyMethod == null) continue;

                harmony.Patch(readyMethod, postfix: postfix);
                patched++;
                GD.Print($"[Sts2Unlimited] Patched {screenType.FullName}._Ready.");
            }

            if (patched == 0)
                GD.PrintErr("[Sts2Unlimited] Found settings types, but no _Ready methods were patchable.");
        }
        catch (Exception e) { GD.PrintErr($"[Sts2Unlimited] Patch failed: {e.Message}"); }
    }

    private static void Patch_NSettingsScreen_Ready(object __instance)
    {
        if (__instance is not Node screen) return;
        try
        {
            if (FindNodeByName(screen, "Sts2UnlimitedMaxPlayersRow") != null)
                return;

            if (!TryFindTemplateRow(screen, out var templateRow))
            {
                GD.PrintErr("[Sts2Unlimited] Could not locate a settings slider template row.");
                return;
            }

            if (!TryFindInsertionPoint(screen, out var targetParent, out var anchor))
            {
                GD.PrintErr("[Sts2Unlimited] Could not locate insertion point in settings UI.");
                return;
            }

            // Duplicate the whole row (keeps internal scene-unique slider setup).
            Node sliderRow = (Node)templateRow.Duplicate(15);
            sliderRow.Name = "Sts2UnlimitedMaxPlayersRow";
            Node sliderOwner = FindSliderOwnerNode(sliderRow) ?? sliderRow;

            // Divider: clone nearby divider if possible; otherwise create a simple fallback.
            Node divider = FindSiblingDivider(targetParent, anchor)
                        ?? FindFirstDivider(targetParent)
                        ?? new HSeparator();
            divider = (Node)divider.Duplicate();
            divider.Name = "Sts2UnlimitedMaxPlayersDivider";

            int insertAt = anchor.GetIndex();
            targetParent.AddChild(divider);
            targetParent.AddChild(sliderRow);
            targetParent.MoveChild(divider,   insertAt + 1);
            targetParent.MoveChild(sliderRow, insertAt + 2);

            var tree = sliderRow.GetTree();
            if (tree == null) return;

            tree.Connect(SceneTree.SignalName.ProcessFrame, Callable.From(() =>
            {
                try { ConfigureMaxPlayersSlider(sliderOwner, sliderRow, Sts2Unlimited.MaxPlayersOverride); }
                catch (Exception e) { GD.PrintErr($"[Sts2Unlimited] Config error: {e.Message}\n{e.StackTrace}"); }
            }), (uint)GodotObject.ConnectFlags.OneShot);
        }
        catch (Exception e) { GD.PrintErr($"[Sts2Unlimited] Injection error: {e.Message}\n{e.StackTrace}"); }
    }

    private static void ConfigureMaxPlayersSlider(Node slider, Node sliderRow, int playerCount)
    {
        // ── 1. Name label — MegaRichTextLabel 'Label' inside the MarginContainer row ──
        var nameLabel = sliderRow.GetNodeOrNull("Label") ?? FindNodeByName(sliderRow, "Label");
        if (nameLabel != null)
            nameLabel.Set("text", "Max Players");
        else
            GD.PrintErr("[Sts2Unlimited] 'Label' not found in sliderRow.");

        // ── 2. NSlider (Range) ───────────────────────────────────────────────
        var nslider = slider?.GetNodeOrNull("Slider") as Godot.Range
                   ?? FindNodeByName(slider ?? sliderRow, "Slider") as Godot.Range;
        if (nslider == null) { GD.PrintErr("[Sts2Unlimited] 'Slider' child not found."); return; }

        // Disconnect existing handlers:
        //   NSettingsSlider.OnValueChanged  → formats label as "X%"
        //   NMasterVolumeSlider.OnValueChanged → modifies master audio volume  ← must remove!
        foreach (var conn in nslider.GetSignalConnectionList("value_changed"))
            nslider.Disconnect("value_changed", conn["callable"].As<Callable>());

        // NSlider.UpdateHandlePosition uses: _currentHandlePosition / MaxValue
        // This assumes MinValue=0. To have value=2 appear at the leftmost position,
        // use internal range [0, 14] and add PLAYER_OFFSET when reading/saving.
        int internalValue = Math.Clamp(playerCount - PLAYER_OFFSET, INTERNAL_MIN, INTERNAL_MAX);

        nslider.MinValue = INTERNAL_MIN;
        nslider.MaxValue = INTERNAL_MAX;
        nslider.Step     = 1;
        nslider.Value    = internalValue;
        // Snap the visual handle to the correct position immediately
        nslider.Call("SetValueWithoutAnimation", (double)internalValue);

        // Our handler: update MaxPlayersOverride and value display
        nslider.Connect(Godot.Range.SignalName.ValueChanged, Callable.From<double>(v =>
        {
            int players = (int)Math.Round(v) + PLAYER_OFFSET;
            Sts2Unlimited.MaxPlayersOverride = players;
            SaveMaxPlayers(players);
            var sliderValueNode = slider.GetNodeOrNull("SliderValue") ?? FindNodeByName(sliderRow, "SliderValue");
            sliderValueNode?.Set("text", $"{players}");
        }));

        // ── 3. Initial value display ─────────────────────────────────────────
        (slider.GetNodeOrNull("SliderValue") ?? FindNodeByName(sliderRow, "SliderValue"))?.Set("text", $"{playerCount}");

        GD.Print($"[Sts2Unlimited] Max Players slider configured: range [{PLAYER_MIN},{PLAYER_MAX}], current={playerCount}");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static Node FindSiblingDivider(Node parent, Node referenceNode)
    {
        var children = parent.GetChildren();
        for (int i = 0; i < children.Count; i++)
        {
            if (children[i] != referenceNode) continue;
            if (i > 0 && LooksDivider(children[i - 1])) return children[i - 1];
            if (i < children.Count - 1 && LooksDivider(children[i + 1])) return children[i + 1];
        }
        foreach (var child in children)
            if (LooksDivider(child)) return child;
        return null;
    }

    private static Node FindFirstDivider(Node root)
    {
        foreach (Node child in root.GetChildren(includeInternal: true))
        {
            if (LooksDivider(child)) return child;
        }
        return null;
    }

    private static bool LooksDivider(Node node)
    {
        if (node is HSeparator || node is VSeparator || node is ColorRect) return true;
        var typeName = node.GetType().Name;
        var nodeName = node.Name.ToString();
        return typeName.Contains("Separator") || typeName.Contains("Divider")
            || nodeName.Contains("Separator") || nodeName.Contains("Divider")
            || nodeName.StartsWith("Line");
    }

    private static Node FindAncestorBoxContainer(Node node)
    {
        var cur = node.GetParent();
        while (cur != null) { if (cur is BoxContainer) return cur; cur = cur.GetParent(); }
        return null;
    }

    private static Node FindNodeByType(Node root, Type targetType)
    {
        if (root.GetType() == targetType) return root;
        foreach (Node child in root.GetChildren(includeInternal: true))
        {
            var found = FindNodeByType(child, targetType);
            if (found != null) return found;
        }
        return null;
    }

    private static Node FindNodeByName(Node root, string nodeName)
    {
        if (root.Name == nodeName)
            return root;

        foreach (Node child in root.GetChildren(includeInternal: true))
        {
            var found = FindNodeByName(child, nodeName);
            if (found != null) return found;
        }
        return null;
    }

    private static bool TryFindTemplateRow(Node screen, out Node templateRow)
    {
        templateRow = null;

        // Preferred explicit types from known versions.
        var preferredTypeNames = new[]
        {
            "MegaCrit.Sts2.Core.Nodes.Screens.Settings.NMasterVolumeSlider",
            "MegaCrit.Sts2.Core.Nodes.Screens.Settings.NSettingsSlider"
        };

        foreach (var typeName in preferredTypeNames)
        {
            var t = ResolveType(typeName);
            if (t == null) continue;
            var node = FindNodeByType(screen, t);
            if (node?.GetParent() != null)
            {
                templateRow = node.GetParent();
                return true;
            }
        }

        // Generic fallback: any row that contains both Label + SliderValue and a Slider node.
        foreach (Node node in screen.GetChildren(includeInternal: true))
        {
            var candidate = FindSliderOwnerNode(node);
            if (candidate == null) continue;
            var row = candidate.GetParent() ?? candidate;
            if (FindNodeByName(row, "Label") != null && FindNodeByName(row, "SliderValue") != null)
            {
                templateRow = row;
                return true;
            }
        }

        return false;
    }

    private static Node FindSliderOwnerNode(Node root)
    {
        // Exact "Slider" child is the most stable marker of settings slider widgets.
        if (root.GetNodeOrNull("Slider") is Godot.Range)
            return root;

        foreach (Node child in root.GetChildren(includeInternal: true))
        {
            var found = FindSliderOwnerNode(child);
            if (found != null) return found;
        }

        return null;
    }

    private static bool TryFindInsertionPoint(Node screen, out Node targetParent, out Node anchor)
    {
        targetParent = null;
        anchor = null;

        // Stable names in older builds.
        Node moddingDivider = screen.GetNodeOrNull("%ModdingDivider") ?? FindNodeByName(screen, "ModdingDivider");
        Node moddingButton = screen.GetNodeOrNull("%Modding") ?? FindNodeByName(screen, "Modding");

        if (moddingButton?.GetParent() is Node parentFromButton)
        {
            targetParent = parentFromButton;
            anchor = moddingButton;
            return true;
        }

        if (moddingDivider?.GetParent() is Node parentFromDivider)
        {
            targetParent = parentFromDivider;
            anchor = moddingDivider;
            return true;
        }

        // Fallback: find any VBoxContainer row parent with a child whose name mentions "modding".
        var best = FindNodeByPredicate(screen, n =>
            n is BoxContainer &&
            n.GetChildren().OfType<Node>().Any(c => c.Name.ToString().ToLowerInvariant().Contains("modding")));

        if (best != null)
        {
            targetParent = best;
            anchor = best.GetChildren().OfType<Node>()
                .FirstOrDefault(c => c.Name.ToString().ToLowerInvariant().Contains("modding"))
                ?? best.GetChildren().OfType<Node>().LastOrDefault();
            return anchor != null;
        }

        return false;
    }

    private static Node FindNodeByPredicate(Node root, Func<Node, bool> predicate)
    {
        if (predicate(root)) return root;

        foreach (Node child in root.GetChildren(includeInternal: true))
        {
            var found = FindNodeByPredicate(child, predicate);
            if (found != null) return found;
        }

        return null;
    }

    private static Type ResolveType(string fullName)
    {
        var direct = Type.GetType($"{fullName}, sts2", false);
        if (direct != null) return direct;

        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            var t = asm.GetType(fullName, false);
            if (t != null) return t;
        }

        return null;
    }

    private static List<Type> FindSettingsScreenTypes()
    {
        var result = new List<Type>();

        // First, known canonical type names.
        var known = new[]
        {
            "MegaCrit.Sts2.Core.Nodes.Screens.Settings.NSettingsScreen"
        };

        foreach (var fullName in known)
        {
            var t = ResolveType(fullName);
            if (t != null && !result.Contains(t)) result.Add(t);
        }

        // Fallback: discover candidates by naming convention.
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type[] types;
            try { types = asm.GetTypes(); }
            catch { continue; }

            foreach (var t in types)
            {
                if (t == null || !typeof(Node).IsAssignableFrom(t)) continue;

                var full = t.FullName ?? string.Empty;
                if (!full.Contains("Settings", StringComparison.OrdinalIgnoreCase)) continue;
                if (!full.Contains("Screen", StringComparison.OrdinalIgnoreCase)) continue;

                var ready = t.GetMethod("_Ready",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                    null,
                    Type.EmptyTypes,
                    null);
                if (ready != null && !result.Contains(t)) result.Add(t);
            }
        }

        return result;
    }

    public static void SaveMaxPlayers(int value)
    {
        try { File.WriteAllText(SettingsPath, $"{{\"MaxPlayers\":{value}}}"); }
        catch (Exception e) { GD.PrintErr($"[Sts2Unlimited] Failed to save settings: {e.Message}"); }
    }
}
