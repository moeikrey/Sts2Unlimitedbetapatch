[b]Sts2Unlimited - Multiplayer Player Count Mod[/b]

A mod for Slay the Spire 2 that enables multiplayer games with unlimited player counts instead of the hardcoded limit of 4.

[b]Features[/b]

[list]
[*][b]Unlimited player lobbies[/b]: Host multiplayer games with any number of players
[*][b]Configuration file support[/b]: Set your desired player limit in a simple text file
[*][b]Runtime detours[/b]: Uses method pointer replacement to override hard-coded player count values
[/list]

[b]Installation[/b]

[list=1]
[*]Build the mod using [font=Courier New]dotnet build[/font]
[*]Copy the generated DLL to your Slay the Spire 2 mods folder:
[list]
[*]The DLL is located at [font=Courier New].godot/mono/temp/bin/Debug/sts2unlimited.dll[/font]
[*]Place it in your game's mod directory
[/list]
[/list]

[b]Configuration[/b]

[b]Setting Custom Player Count[/b]

Create a file named [font=Courier New]sts2unlimited.maxplayers.txt[/font] in the same directory as the mod DLL.

Add a single integer to the file representing the maximum number of players:

[code]
8
[/code]

This example allows up to 8 players in a lobby.

[b]Default Behavior[/b]

If the configuration file is not present, the mod defaults to 4 players (the vanilla limit).

[b]How It Works[/b]

The mod uses several techniques to override the player limit:

[list=1]
[*][b]Method Detours[/b]: At load time, it captures references to the original network and UI initialization methods
[*][b]Pointer Replacement[/b]: Uses [font=Courier New]RuntimeHelpers.PrepareMethod()[/font] combined with unsafe pointer manipulation to replace method code pointers
[*][b]Delegation[/b]: Replacement methods intercept the original max-player parameter and substitute the configured value
[/list]

[b]Patched Methods[/b]

[list]
[*][font=Courier New]NetHostGameService.StartSteamHost()[/font] - Steam multiplayer initialization
[*][font=Courier New]NetHostGameService.StartENetHost()[/font] - Local network initialization
[*][font=Courier New]NCharacterSelectScreen.InitializeMultiplayerAsHost()[/font] - Standard run lobby setup
[*][font=Courier New]NCustomRunScreen.InitializeMultiplayerAsHost()[/font] - Custom run lobby setup
[/list]

[b]Technical Details[/b]

The MegaCrit libraries already support arbitrary player counts in their networking layer—the limit is entirely enforced at the UI/initialization level by hardcoding the value [font=Courier New]4[/font] in various entry points. This mod intercepts those calls and substitutes a configurable value.

[b]Key Components[/b]

[list]
[*][b]Configuration Loading[/b] ([font=Courier New]LoadConfig[/font]): Reads max player count from optional config file
[*][b]Detour Application[/b] ([font=Courier New]ApplyDetours[/font]): Captures original methods and applies runtime patches
[*][b]Hook Method[/b] ([font=Courier New]HookMethod[/font]): Performs unsafe pointer replacement in native code
[*][b]Replacement Methods[/b]: Forward calls to original methods with overridden parameters
[/list]

[b]Limitations[/b]

[list]
[*][b]Platform Limits[/b]: Some transports (Steam lobbies, ENet bandwidth) may have practical limits
[*][b]UI Layout[/b]: The character selection screen may become visually crowded with many players
[*][b]Synchronization[/b]: Game balance and performance with >4 players is untested
[/list]

[b]Troubleshooting[/b]

[b]Build Fails with "unsafe code"[/b]

Ensure your project allows unsafe code blocks. This is already set in the [font=Courier New].csproj[/font] file.

[b]Mod Not Loading[/b]

[list]
[*]Verify the DLL is placed in the correct mods folder
[*]Check that the file is named exactly [font=Courier New]sts2unlimited.dll[/font]
[*]Review game console/logs for any error messages
[/list]

[b]Player Count Not Changing[/b]

[list]
[*]Confirm the config file is named exactly [font=Courier New]sts2unlimited.maxplayers.txt[/font]
[*]Verify the file contains only an integer (no extra whitespace or text)
[*]Check that the file is in the same directory as the DLL
[*]Reload the game to reload the config
[/list]

[b]Development[/b]

[b]Building from Source[/b]

[code]
dotnet build
[/code]

Output DLL: [font=Courier New].godot/mono/temp/bin/Debug/sts2unlimited.dll[/font]

[b]Project Structure[/b]

[list]
[*][font=Courier New]Sts2Unlimited.cs[/font] - Main mod class with detour logic
[*][font=Courier New]sts2unlimited.csproj[/font] - Project configuration
[*][font=Courier New]sts2unlimited.maxplayers.txt[/font] - Configuration file (create manually)
[/list]

[b]License[/b]

This mod is provided as-is for Slay the Spire 2 modding.
