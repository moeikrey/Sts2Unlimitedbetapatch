using Godot;
using System;
using System.IO;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Entities.Multiplayer;   // NetErrorInfo
using MegaCrit.Sts2.Core.Multiplayer.Game;      // INetGameService

namespace Sts2Unlimited;

[ModInitializer("ModLoaded")]
public static class Sts2Unlimited
{
	/// <summary>
	/// Maximum number of players to allow in a lobby.  Defaults to 4 but
	/// can be overridden by a text config file located next to the mod DLL.
	/// </summary>
	public static int MaxPlayersOverride = 4;

	public static void ModLoaded()
	{
		LoadConfig();

		ApplyDetours();
		Console.WriteLine($"Sts2Unlimited loaded successfully! maxPlayers={MaxPlayersOverride}");
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
		}
		catch (Exception e)
		{
			Console.WriteLine($"Unable to read max‑player config: {e.Message}");
		}
	}

	// manual detour helpers -------------------------------------------------------------

	// delegates that keep references to the original methods so we can call them
	private static Func<MegaCrit.Sts2.Core.Multiplayer.NetHostGameService, int, System.Threading.Tasks.Task<NetErrorInfo?>> _origSteamHost;
	private static Func<MegaCrit.Sts2.Core.Multiplayer.NetHostGameService, ushort, int, NetErrorInfo?> _origENetHost;
	private static Action<object, INetGameService, int> _origCharInit;
	private static Action<object, INetGameService, int> _origCustomInit;

	private static void ApplyDetours()
	{
		try
		{
			var hostType = typeof(MegaCrit.Sts2.Core.Multiplayer.NetHostGameService);
			var mSteam = hostType.GetMethod("StartSteamHost");
			var mENet = hostType.GetMethod("StartENetHost");
			if (mSteam != null)
			{
				_origSteamHost = (Func<MegaCrit.Sts2.Core.Multiplayer.NetHostGameService, int, System.Threading.Tasks.Task<NetErrorInfo?>>)
						Delegate.CreateDelegate(typeof(Func<MegaCrit.Sts2.Core.Multiplayer.NetHostGameService, int, System.Threading.Tasks.Task<NetErrorInfo?>>), mSteam);
				HookMethod(mSteam, typeof(Sts2Unlimited).GetMethod(nameof(Replacement_StartSteamHost), BindingFlags.NonPublic | BindingFlags.Static));
			}
			if (mENet != null)
			{
				_origENetHost = (Func<MegaCrit.Sts2.Core.Multiplayer.NetHostGameService, ushort, int, NetErrorInfo?>)
						Delegate.CreateDelegate(typeof(Func<MegaCrit.Sts2.Core.Multiplayer.NetHostGameService, ushort, int, NetErrorInfo?>), mENet);
				HookMethod(mENet, typeof(Sts2Unlimited).GetMethod(nameof(Replacement_StartENetHost), BindingFlags.NonPublic | BindingFlags.Static));
			}

			var charType = typeof(MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect.NCharacterSelectScreen);
			var customType = typeof(MegaCrit.Sts2.Core.Nodes.Screens.CustomRun.NCustomRunScreen);
			var init1 = charType.GetMethod("InitializeMultiplayerAsHost", new Type[] { typeof(INetGameService), typeof(int) });
			if (init1 != null)
			{
				_origCharInit = (Action<object, INetGameService, int>)
						Delegate.CreateDelegate(typeof(Action<object, INetGameService, int>), init1);
				HookMethod(init1, typeof(Sts2Unlimited).GetMethod(nameof(Replacement_CharInit), BindingFlags.NonPublic | BindingFlags.Static));
			}
			var init2 = customType.GetMethod("InitializeMultiplayerAsHost", new Type[] { typeof(INetGameService), typeof(int) });
			if (init2 != null)
			{
				_origCustomInit = (Action<object, INetGameService, int>)
						Delegate.CreateDelegate(typeof(Action<object, INetGameService, int>), init2);
				HookMethod(init2, typeof(Sts2Unlimited).GetMethod(nameof(Replacement_CustomInit), BindingFlags.NonPublic | BindingFlags.Static));
			}
		}
		catch (Exception e)
		{
			Console.WriteLine($"Detour failed: {e}");
		}
	}

	private static void HookMethod(MethodInfo target, MethodInfo replacement)
	{
		RuntimeHelpers.PrepareMethod(target.MethodHandle);
		RuntimeHelpers.PrepareMethod(replacement.MethodHandle);
		unsafe
		{
			if (IntPtr.Size == 8)
			{
				ulong* inj = (ulong*)target.MethodHandle.GetFunctionPointer().ToPointer();
				ulong* rep = (ulong*)replacement.MethodHandle.GetFunctionPointer().ToPointer();
				*inj = *rep;
			}
			else
			{
				uint* inj = (uint*)target.MethodHandle.GetFunctionPointer().ToPointer();
				uint* rep = (uint*)replacement.MethodHandle.GetFunctionPointer().ToPointer();
				*inj = *rep;
			}
		}
	}

	// replacement implementations -------------------------------------------------------

	private static System.Threading.Tasks.Task<NetErrorInfo?> Replacement_StartSteamHost(MegaCrit.Sts2.Core.Multiplayer.NetHostGameService self, int maxClients)
	{
		return _origSteamHost(self, MaxPlayersOverride);
	}

	private static NetErrorInfo? Replacement_StartENetHost(MegaCrit.Sts2.Core.Multiplayer.NetHostGameService self, ushort port, int maxClients)
	{
		return _origENetHost(self, port, MaxPlayersOverride);
	}

	private static void Replacement_CharInit(object self, INetGameService gameService, int maxPlayers)
	{
		_origCharInit(self, gameService, MaxPlayersOverride);
	}

	private static void Replacement_CustomInit(object self, INetGameService gameService, int maxPlayers)
	{
		_origCustomInit(self, gameService, MaxPlayersOverride);
	}
}

