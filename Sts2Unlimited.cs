using Godot;
using System;
using System.IO;
using System.Reflection;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Logging;
using HarmonyLib;

namespace Sts2Unlimited;

[ModInitializer("ModLoaded")]
public static class Sts2Unlimited
{
	/// <summary>
	/// Maximum number of players to allow in a lobby. Defaults to 8 but
	/// can be overridden by a text config file located next to the mod DLL.
	/// </summary>
	private static int maxPlayersOverride = 8;

	private static Harmony harmony;

	public static int MaxPlayersOverride { get => maxPlayersOverride; set => maxPlayersOverride = value; }

	public static void ModLoaded()
	{
		LoadConfig();
		ApplyHarmonyPatches();
		Log.LogMessage(LogLevel.Info, LogType.Generic, $"Sts2Unlimited loaded successfully! maxPlayers set to {MaxPlayersOverride}");
	}

	private static void LoadConfig()
	{
		try
		{
			string dllDir = Path.GetDirectoryName(typeof(Sts2Unlimited).Assembly.Location) ?? ".";
			string cfgPath = Path.Combine(dllDir, "sts2unlimited.maxplayers.txt");
			if (File.Exists(cfgPath))
			{
				string txt = File.ReadAllText(cfgPath).Trim();
				if (int.TryParse(txt, out var val) && val > 0)
				{
					MaxPlayersOverride = val;
				}
			}
			else
			{
				Log.LogMessage(LogLevel.Warn, LogType.Generic, $"Max player config file not found at {cfgPath}. Using default value of {MaxPlayersOverride}.");
			}
		}
		catch (Exception e)
		{
			Log.LogMessage(LogLevel.Error, LogType.Generic, $"Unable to read max-player config: {e.Message}");
		}
	}

	private static void ApplyHarmonyPatches()
	{
		try
		{
			harmony = new Harmony("sts2unlimited.modifier");

			// Patch StartSteamHost to use custom player count
			var steamHostMethod = typeof(MegaCrit.Sts2.Core.Multiplayer.NetHostGameService).GetMethod(
				"StartSteamHost",
				BindingFlags.Public | BindingFlags.Instance,
				null,
				[typeof(int)],
				null
			);

			if (steamHostMethod != null)
			{
				var steamPatch = typeof(Sts2Unlimited).GetMethod(nameof(Patch_StartSteamHost), BindingFlags.NonPublic | BindingFlags.Static);
				harmony.Patch(steamHostMethod, prefix: new HarmonyMethod(steamPatch));
				Log.LogMessage(LogLevel.Debug, LogType.Generic, "Patched StartSteamHost");
			}

			// Patch StartENetHost to use custom player count
			var enetHostMethod = typeof(MegaCrit.Sts2.Core.Multiplayer.NetHostGameService).GetMethod(
				"StartENetHost",
				BindingFlags.Public | BindingFlags.Instance,
				null,
				[typeof(ushort), typeof(int)],
				null
			);

			if (enetHostMethod != null)
			{
				var enetPatch = typeof(Sts2Unlimited).GetMethod(nameof(Patch_StartENetHost), BindingFlags.NonPublic | BindingFlags.Static);
				harmony.Patch(enetHostMethod, prefix: new HarmonyMethod(enetPatch));
				Log.LogMessage(LogLevel.Debug, LogType.Generic, "Patched StartENetHost");
			}

			// Patch NCharacterSelectScreen.InitializeMultiplayerAsHost
			var charSelectType = typeof(MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect.NCharacterSelectScreen);
			var charInitMethod = charSelectType.GetMethod(
				"InitializeMultiplayerAsHost",
				BindingFlags.Public | BindingFlags.Instance,
				null,
				[typeof(INetGameService), typeof(int)],
				null
			);

			if (charInitMethod != null)
			{
				var charPatch = typeof(Sts2Unlimited).GetMethod(nameof(Patch_CharacterSelectInit), BindingFlags.NonPublic | BindingFlags.Static);
				harmony.Patch(charInitMethod, prefix: new HarmonyMethod(charPatch));
				Log.LogMessage(LogLevel.Debug, LogType.Generic, "Patched NCharacterSelectScreen.InitializeMultiplayerAsHost");
			}

			// Patch NCustomRunScreen.InitializeMultiplayerAsHost
			var customRunType = typeof(MegaCrit.Sts2.Core.Nodes.Screens.CustomRun.NCustomRunScreen);
			var customInitMethod = customRunType.GetMethod(
				"InitializeMultiplayerAsHost",
				BindingFlags.Public | BindingFlags.Instance,
				null,
				[typeof(INetGameService), typeof(int)],
				null
			);

			if (customInitMethod != null)
			{
				var customPatch = typeof(Sts2Unlimited).GetMethod(nameof(Patch_CustomRunInit), BindingFlags.NonPublic | BindingFlags.Static);
				harmony.Patch(customInitMethod, prefix: new HarmonyMethod(customPatch));
				Log.LogMessage(LogLevel.Debug, LogType.Generic, "Patched NCustomRunScreen.InitializeMultiplayerAsHost");
			}
		}
		catch (Exception e)
		{
			Log.LogMessage(LogLevel.Error, LogType.Generic, $"Failed to apply Harmony patches: {e.Message}");
		}
	}

	// Harmony patch methods - these intercept and modify the parameters before the actual method runs

	private static bool Patch_StartSteamHost(ref int maxClients)
	{
		maxClients = MaxPlayersOverride;
		return true; // continue with patched method
	}

	private static bool Patch_StartENetHost(ushort port, ref int maxClients)
	{
		maxClients = MaxPlayersOverride;
		return true;
	}

	private static bool Patch_CharacterSelectInit(INetGameService gameService, ref int maxPlayers)
	{
		maxPlayers = MaxPlayersOverride;
		return true;
	}

	private static bool Patch_CustomRunInit(INetGameService gameService, ref int maxPlayers)
	{
		maxPlayers = MaxPlayersOverride;
		return true;
	}
}
