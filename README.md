# FoodGuard

A client-side Valheim mod that reminds you to eat. Built for players who forget to eat food & rest,
when leaving base or going into combat.

**Author:** jg224 · **Version:** 0.1.0 (pre-release — under active testing)

## Publish to Thunderstore

The repo includes a `publish.ps1` that builds the complete Thunderstore package (DLL + manifest +
README + LICENSE + 256×256 icon.png, all at the zip root with no nested folder — exactly what
Thunderstore requires).

```powershell
# Build the package (regenerates the icon, builds the DLL, zips everything)
powershell -ExecutionPolicy Bypass -File foodguard\publish.ps1
# Override the version:
powershell -ExecutionPolicy Bypass -File foodguard\publish.ps1 -Version 0.2.0
```

Output: `foodguard\dist\FoodGuard-<version>.zip`.

To upload (requires the Thunderstore CLI `tcli`, already on this machine):

```powershell
tcli login                                          # one-time; creates an auth token
tcli publish --file "foodguard\dist\FoodGuard-0.1.0.zip" `
    --package-namespace jg224 --package-name foodguard --package-version 0.1.0
```

Notes:
- The icon is auto-generated (`make_icon.ps1`). To use custom art, drop a hand-made `icon.png`
  (exactly 256×256) into `foodguard/` and re-run `publish.ps1` (it overwrites).
- `manifest.json` lists `denikson-BepInExPack_Valheim-5.4.2333` as a dependency — matches the
  BepInEx version on the server (5.4.23.3).
- Thunderstore enforces unique version numbers per package; bump `-Version` on every publish.

## What it does

Four reminders, each a large center-screen popup (the same banner Valheim uses for "World saved").
The most important one — combat with low food — also plays an alert sound.

| # | When | Popup | Sound |
|---|---|---|---|
| 1 | You **leave base** while needing food | yes, once per base-exit | no |
| 2 | A food is at **25% time remaining** | yes (quiet while in base) | no |
| 3 | You're **in combat** AND food is low | yes | **yes** |
| 4 | You have **no food eaten** and are away from base | yes | no |
| 5 | You're **in combat** AND **not Rested** | yes | **yes** |

"Needing food" = any active food at/under the threshold, or an empty food slot. The plain low-food
nudge (#2) is suppressed at home so it doesn't spam while you're crafting; the leave-base and combat
triggers always fire, because those are exactly when it matters.

Each trigger has its own cooldown so it never loops.

## Install (r2modman)

1. Copy `FoodGuard.dll` onto the player's PC.
2. Open **r2modman** → select the Valheim profile the server uses.
3. **Settings** → **Profile** → **Import local mod** (or "Local Packages" → drag the DLL in).
   - On some r2modman versions: **Profile** menu → **Import/Update** → **From file** → pick `FoodGuard.dll`.
4. Launch with **Start modded** as usual. That's it — no profile-code change, no server restart.

Alternative manual path: drop the DLL directly into
`%APPDATA%\r2modmanPlus-local\Valheim\<ProfileName>\BepInEx\plugins\` and **Start modded**.

## Configure

A config file appears after first launch at
`<r2modman profile>\BepInEx\config\jg224.FoodGuard.cfg`. Edit it while the game is closed (or use
BepInEx's ConfigurationManager if installed). Every knob is exposed:

```ini
[General]
Enabled              = true
DebugMode            = false
FoodThresholdPercent = 20      # remind when a food has this % of its time left
EmptySlotCountsAsLow = true    # an empty food slot also counts as needing food

[Base]
BaseZoneMode         = Both    # CraftingStation | Building | Both
SuppressLowFoodInBase = true   # the plain low-food nudge stays quiet at home

[Triggers]
LeaveBaseEnabled = true        # #1
LowFoodEnabled   = true        # #2
CombatEnabled    = true        # #3
NoFoodOutEnabled = true        # #4
CombatNoRestEnabled = true     # #5 (combat without the Rested buff)

[Sound]
CombatSoundEnabled = true      # sound is reserved for the combat case only
CombatSoundCooldown = 60       # seconds between repeated combat alert sounds
AlertSfxName = sfx_perfectblock  # vanilla SFX prefab name (resolved via ZNetScene)

[Cooldowns]
LeaveBaseCooldown = 8
LowFoodCooldown   = 30
CombatCooldown    = 60
NoFoodOutCooldown = 45
CombatNoRestCooldown = 60

[Messages]
LeaveBaseMessage = You left base without food -- EAT NOW before you head out!
LowFoodMessage   = Food at {pct}% -- time to eat!
CombatMessage    = COMBAT with low food -- EAT NOW!
NoFoodOutMessage = You have no food eaten and you're away from base -- EAT!
CombatNoRestMessage = COMBAT without Rested -- get to a fire/shelter first!
```

`{pct}` in the low-food message is replaced with the lowest remaining-food percentage.

## How "base" is detected

**Marked (default, recommended):** you mark your *one* main base by standing at its center and pressing
**F7**. "In base" = within `MarkedBaseRadius` (default 30 m) of that point. The mark is saved to the
config file and persists across restarts; re-mark any time you move base.

This is deliberately strict: a walled farm, an outpost, or another player's base will **never** count
as your base, no matter how many workbenches or walls it has. If you haven't marked yet, *nothing*
counts as base (so the low-food nudge won't be suppressed anywhere — which is the safe default).

Alternatives if you prefer them (set `BaseZoneMode`):
- **CraftingStation** — inside any workbench/forge effect area (broad; trips on farms).
- **Building** — inside a roofed, fired interior (`WarmCozyArea`).
- **Both** — either of the two above.

## Hotkeys

- **F7** — mark your main base at your current position (saved to config). Re-mark to move it.
- **F8** — dump the live state the mod sees (food %, base in/out + distance, rested, combat, teleport,
  login/death timers) to the screen and the BepInEx log. Use it to diagnose any "why did/didn't it
  fire" moment.
- **F9** — dump every `sfx_*` (and `vfx_*`) prefab name your game has loaded to the BepInEx log. Use it
  to find a valid value for `AlertSfxName` — any name in the dump is guaranteed to work on your game
  version. Goes to the log only (there are hundreds), so check the BepInEx log after pressing.

All three are rebindable (`MarkBaseHotkey`, `DebugHotkey`, `SfxDumpHotkey`); set to `None` to disable.

## Notes

- **Client-only.** No server half, no networking, no ServerSync. On a dedicated server the DLL loads
  but does nothing (no local player there).
- **Combat detection** = a creature is currently targeting *you*. It uses a brief 5s clear-window so
  the signal doesn't flicker off during retargeting.
- **The alert sound** is resolved from Valheim's prefab table by name (`sfx_perfectblock` by default —
  the sharp metallic perfect-block ring). If that prefab is missing or has no audio clip, the sound is
  skipped silently — the popup still shows. Change `AlertSfxName` to any other networked SFX prefab
  (press F9 to list them); see the Sound section of the config for the full list of options.
- **No bundled game DLLs.** The build references the game/BepInEx DLLs already on the machine
  (`Private=false`), matching the project's other custom mods.
