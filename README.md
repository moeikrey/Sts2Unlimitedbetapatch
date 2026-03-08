# Sts2Unlimited - Multiplayer Player Count Mod

A mod for Slay the Spire 2 that enables multiplayer games with unlimited player counts instead of the hardcoded limit of 4.

## Features

- **Unlimited player lobbies**: Host multiplayer games with any number of players
- **Configuration file support**: Set your desired player limit in a simple text file
- **Runtime detours**: Uses method pointer replacement to override hard-coded player count values

## Installation

1. Build the mod using `dotnet build`
2. Copy the generated DLL to your Slay the Spire 2 mods folder:
   - The DLL is located at `.godot/mono/temp/bin/Debug/sts2unlimited.dll`
   - Place it in your game's mod directory

## Configuration

### Setting Custom Player Count

Create a file named `sts2unlimited.maxplayers.txt` in the same directory as the mod DLL.

Add a single integer to the file representing the maximum number of players:

```
8
```

This example allows up to 8 players in a lobby.

#### Default Behavior

If the configuration file is not present, the mod defaults to 4 players (the vanilla limit).

## How It Works

The mod uses several techniques to override the player limit:

1. **Method Detours**: At load time, it captures references to the original network and UI initialization methods
2. **Pointer Replacement**: Uses `RuntimeHelpers.PrepareMethod()` combined with unsafe pointer manipulation to replace method code pointers
3. **Delegation**: Replacement methods intercept the original max-player parameter and substitute the configured value

### Patched Methods

- `NetHostGameService.StartSteamHost()` - Steam multiplayer initialization
- `NetHostGameService.StartENetHost()` - Local network initialization
- `NCharacterSelectScreen.InitializeMultiplayerAsHost()` - Standard run lobby setup
- `NCustomRunScreen.InitializeMultiplayerAsHost()` - Custom run lobby setup

## Technical Details

The MegaCrit libraries already support arbitrary player counts in their networking layer—the limit is entirely enforced at the UI/initialization level by hardcoding the value `4` in various entry points. This mod intercepts those calls and substitutes a configurable value.

### Key Components

- **Configuration Loading** (`LoadConfig`): Reads max player count from optional config file
- **Detour Application** (`ApplyDetours`): Captures original methods and applies runtime patches
- **Hook Method** (`HookMethod`): Performs unsafe pointer replacement in native code
- **Replacement Methods**: Forward calls to original methods with overridden parameters

## Limitations

- **Platform Limits**: Some transports (Steam lobbies, ENet bandwidth) may have practical limits
- **UI Layout**: The character selection screen may become visually crowded with many players
- **Synchronization**: Game balance and performance with >4 players is untested

## Troubleshooting

### Build Fails with "unsafe code"
Ensure your project allows unsafe code blocks. This is already set in the `.csproj` file.

### Mod Not Loading
- Verify the DLL is placed in the correct mods folder
- Check that the file is named exactly `sts2unlimited.dll`
- Review game console/logs for any error messages

### Player Count Not Changing
- Confirm the config file is named exactly `sts2unlimited.maxplayers.txt`
- Verify the file contains only an integer (no extra whitespace or text)
- Check that the file is in the same directory as the DLL
- Reload the game to reload the config

## Development

### Building from Source

```bash
dotnet build
```

Output DLL: `.godot/mono/temp/bin/Debug/sts2unlimited.dll`

### Project Structure

- `Sts2Unlimited.cs` - Main mod class with detour logic
- `sts2unlimited.csproj` - Project configuration
- `sts2unlimited.maxplayers.txt` - Configuration file (create manually)

## License

This mod is provided as-is for Slay the Spire 2 modding.
