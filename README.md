# Shared Sweeps

**Shared progression for [Moon Sweepers](https://store.steampowered.com/app/5335510) crews.** When anyone in your crew buys a tool, a dust-container upgrade or an upgrade-tree level, everyone gets it.

Moon Sweepers already pools the crew's money into one wallet, but every player still has to buy every brush, broom and vacuum upgrade separately. Shared Sweeps removes the double-buying: one purchase unlocks the item for the whole crew.

- **Host-only.** Only the person hosting the crew needs the mod. Crewmates can play unmodded.
- **No custom networking.** It works through the game's own co-op sync, so there's nothing extra to break or desync.
- **Save-friendly.** Shared unlocks are stored in the host's normal save. Removing the mod keeps what everyone already has.

---

## What gets shared

| Shared | Not shared |
|---|---|
| Owned cleaning tools (toothbrush, brushes, brooms, vacuums...) | Vehicles and vehicle upgrades (each player owns their own) |
| Each tool's dust-container tier | Money (the game already shares it) |
| Upgrade-tree levels from the workshop terminal | Cleaning progress on the Moon (the game already shares it) |

Each item is shared at the **highest level anyone in the crew owns**. Sharing only ever adds: a refund or a lower level is never pushed onto anyone else.

Crewmates who have played on your Moon before but are offline right now are included too (you can turn this off), so they'll find everything waiting when they rejoin.

## Requirements

- Moon Sweepers on Windows (Steam)
- [BepInEx 5](https://github.com/BepInEx/BepInEx/releases): the **`BepInEx_win_x64`** build, version 5.4.23 or newer

> **Use the x64 build of BepInEx.** Moon Sweepers is a 64-bit game. The x86 build installs without errors but never loads, so nothing happens.

## Installation

1. **Install BepInEx.** Extract `BepInEx_win_x64_5.4.23.x.zip` into the game folder, so that `winhttp.dll` and the `BepInEx` folder sit next to `MoonSweepers.exe`.
   To find the game folder, right-click Moon Sweepers in Steam → *Manage* → *Browse local files*.
2. **Run the game once**, then quit. BepInEx creates its `plugins` and `config` folders.
3. **Install Shared Sweeps.** Download `MoonSweepersSharedProgression.dll` from the [Releases](../../releases) page and put it in:
   ```
   Moon Sweepers\BepInEx\plugins\SharedSweeps\
   ```
4. **Host a crew.** That's it. Your crewmates don't need to install anything.

To check that it's working, open `BepInEx\LogOutput.log`. You should see:

```
[Info   :Shared Sweeps] Shared Sweeps 1.0.0 loaded. Host the crew to share tools and upgrades with everyone.
[Info   :Shared Sweeps] Shared with Friend (steam-7656119...): 1 tool(s), 2 container upgrade(s), 0 tree upgrade(s).
```

### Uninstalling

Delete the `BepInEx\plugins\SharedSweeps` folder. Unlocks that were already shared stay in your save.

## Configuration

The settings file is `BepInEx\config\com.skylarreed.moonsweepers.sharedprogression.cfg`. BepInEx creates it after the first launch with the mod. Only the **host's** settings matter.

| Setting | Default | What it does |
|---|---|---|
| `General.Enabled` | `true` | Master switch |
| `General.SweepSeconds` | `3` | How often the host checks for crewmates who are missing unlocks (for example, someone who just joined). Purchases are shared immediately regardless. |
| `Share.Tools` | `true` | Share owned tools |
| `Share.ContainerUpgrades` | `true` | Share dust-container tiers |
| `Share.UpgradeTree` | `true` | Share upgrade-tree levels |
| `Share.IncludeAbsentCrewmates` | `true` | Also share with, and from, crewmates who have played on this Moon before but aren't online |

## FAQ

**Does everyone need the mod?**
No, only the host. The host's game is in charge of everyone's upgrades and sends them to crewmates through the game's normal sync. Crewmates can install it too; it does nothing on their machines.

**My friend hosts and I have the mod. Why isn't anything shared?**
The host is the one who needs it. Ask your friend to install it, or host the crew yourself.

**What if two of us buy the same thing at the same moment?**
Both purchases go through and the money is spent twice. After that, the shop shows the item as owned for everyone, so it can't happen again for that item.

**Does it work in single player?**
On a Moon your crew has played before, yes. When you play it alone, you still get what offline crewmates bought (see `IncludeAbsentCrewmates`). On a brand-new solo Moon there's nothing to share, and the mod stays idle.

**Can it break my save?**
Shared Sweeps only uses the game's own upgrade-saving code. If a game update changes something it relies on, it switches itself off and writes a message to the log instead of changing anything.

## Troubleshooting

| Problem | Fix |
|---|---|
| No `BepInEx\LogOutput.log` after launching | BepInEx isn't loading. Make sure you installed the **x64** build, and that `winhttp.dll` sits directly next to `MoonSweepers.exe` rather than in a subfolder. |
| Log says `Disabled. Moon Sweepers has changed: could not find ...` | A game update renamed something Shared Sweeps uses. Check for a newer release, or open an issue. |
| Nothing is shared | Make sure the **host** has the mod, then look in `LogOutput.log` for errors. |

## How it works

Moon Sweepers' co-op is host-authoritative. The host's world keeps every crewmate's upgrade state (the host is `local`, crewmates are `steam-<SteamID>`) and regularly sends that state to crewmates, replacing their local copy.

Shared Sweeps hooks two methods with [Harmony](https://github.com/BepInEx/HarmonyX):

- `MoonWorld.CommitUpgrade`, which every tool, container and tree purchase goes through. A purchase triggers an immediate sharing pass.
- `MoonWorld.Update`, which triggers a throttled pass every few seconds. This catches crewmates who join.

Each pass reads every player's upgrade state, works out the highest level of each item anyone owns, and writes it back to any player who's behind. Writing uses the game's own `SetPlayerUpgradeState`, and the pass finishes by refreshing the UI (`TreeVersion`, `UpgradesChanged`) and saving. The game's own sync then delivers the result to crewmates. All game access goes through reflection by name, so a renamed member disables the mod cleanly instead of crashing the game.

## Building from source

You need the [.NET SDK](https://dotnet.microsoft.com/download) (any version from 3.0 up) and BepInEx installed in your game folder. The SDK is optional: `build.bat` falls back to the C# compiler built into Windows.

```bat
build.bat "C:\Program Files (x86)\Steam\steamapps\common\Moon Sweepers"
```

This builds `MoonSweepersSharedProgression.dll` and copies it into `BepInEx\plugins`. If the repository is cloned inside the game folder, you can leave out the path. To build without installing:

```bat
dotnet build -c Release -p:GameDir="C:\path\to\Moon Sweepers"
```

The project references the game's own DLLs and BepInEx from your install, so no game files are included in this repository.

### Updating for a game patch

If an update breaks the mod, the log names the missing member. `tools/dnparse.py` is a dependency-free Python script that lists the game's types, methods and IL, so you can find what was renamed:

```
python tools/dnparse.py "<game>\MoonSweepers_Data\Managed\Assembly-CSharp.dll" types.txt
```

The game members Shared Sweeps uses are all looked up in `GameApi.Bind()` in `src/GameApi.cs`.

## Compatibility

Tested with Moon Sweepers built on Unity 6000.6.0f1 (October 2026) and BepInEx 5.4.23.6.

## License

[MIT](LICENSE)

## Disclaimer

Shared Sweeps is an unofficial fan mod. It is not affiliated with or endorsed by DragDropGames, the developer of Moon Sweepers. Back up your saves before using any mod. Saves are in `%USERPROFILE%\AppData\LocalLow\DragDropGames\MoonSweepers\MoonSessions`.
