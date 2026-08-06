# FoodGuard

A client-side Valheim mod that reminds you to eat. Built for players who forget to eat food & rest,
when leaving base or going into combat.

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

Rebindable (`MarkBaseHotkey`); set to `None` to disable.

## Notes

- **Client-only.** No server half, no networking, no ServerSync. On a dedicated server the DLL loads
  but does nothing (no local player there).
- **Combat detection** = a creature is currently targeting *you*. It uses a brief 5s clear-window so
  the signal doesn't flicker off during retargeting.
- **The alert sound** is resolved from Valheim's prefab table by name (`sfx_perfectblock` by default —
  the sharp metallic perfect-block ring). If that prefab is missing or has no audio clip, the sound is
  skipped silently — the popup still shows. Change `AlertSfxName` to any other networked SFX prefab.
- **No bundled game DLLs.** The build references the game/BepInEx DLLs already on the machine
  (`Private=false`), matching the project's other custom mods.
