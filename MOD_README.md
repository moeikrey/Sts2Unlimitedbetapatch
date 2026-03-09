# STS 2 Unlimited - Multiplayer Player Count Mod

Play **Slay the Spire 2** with any number of players, not just 4!

## Installation

1. Locate your Slay the Spire 2 mods folder:
   - **Steam (Windows)**: `C:\Program Files (x86)\Steam\steamapps\common\Slay the Spire 2\mods\`
   - **Other locations**: Look for a `mods` folder inside your game directory

2. Copy **all files** from the mod package into the mods folder:
   - `sts2unlimited.dll`
   - `0Harmony.dll`
   - `sts2unlimited.pck`
   - `mod_manifest.json`
   - `icon.svg`
   - `sts2unlimited.maxplayers.txt` (optional, for custom player counts)

3. **Restart the game** — the mod loads at startup

## Usage

### Default Configuration
The mod defaults to **8 players** per lobby. Just install and play!

### Custom Player Counts
To change the maximum player count, edit `sts2unlimited.maxplayers.txt`:

1. Open `sts2unlimited.maxplayers.txt` in a text editor
2. Change the number to your desired player count (e.g., `16` for 16 players)
3. Save the file
4. Restart the game

## How It Works

This mod patches the Slay the Spire 2 networking layer to accept any player count instead of the hardcoded limit of 4. It uses **Harmony**, a safe method-patching library, to intercept multiplayer initialization:

- **StartSteamHost** — Steam networking lobby creation
- **StartENetHost** — ENet networking lobby creation  
- **NCharacterSelectScreen.InitializeMultiplayerAsHost** — Character selection UI
- **NCustomRunScreen.InitializeMultiplayerAsHost** — Custom run UI

All patches apply your configured player count transparently.

## Requirements

- **Slay the Spire 2** (Steam or standalone)
- **Windows** (currently tested on Windows only)
- **.NET 9.0 runtime** (usually included with game)

## Troubleshooting

### Game crashes on startup
- **Check file placement**: All 6 files must be in the mods folder
- **Verify `0Harmony.dll`**: This file is critical; mod won't load without it
- **Check mod folder path**: Game looks for mods in the exact location specified above

### Lobby shows only 4 player slots
- The UI might not update immediately
- Try restarting the game
- Check that `sts2unlimited.maxplayers.txt` exists and contains a valid number

### Config file isn't being read
- Place `sts2unlimited.maxplayers.txt` in the **same folder** as the DLLs
- The file should contain only a number (no extra text)
- Restart the game after editing

## Features

✅ Unlimited player count (tested with 8+ players)  
✅ Works with Steam hosting  
✅ Works with direct IP (ENet) hosting  
✅ Configurable per-run  
✅ No UI changes required  
✅ Safe method patching (Harmony)  

## Limitations

- **UI crowding** — Screens may not display well with very many players (8+ recommended)
- **Performance** — More players = more network traffic and CPU load
- **Game balance** — Designed for 4 players; balance at 8+ is untested
- **Platform support** — Currently Windows only

## Version

**STS 2 Unlimited v0.0.1**

Created by Andrew Jivoin

## Support

If you encounter issues:
1. Verify all 6 files are present in the mods folder
2. Check the game's log file for errors
3. Ensure Slay the Spire 2 is fully updated

---

**Enjoy playing with your friends!** 🎲
