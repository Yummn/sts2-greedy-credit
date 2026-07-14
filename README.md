# Greedy Credit / 贪婪赊账

Slay the Spire 2 mod: shop purchases and card-removal service may push gold below 0. Any negative debt adds built-in Greed curses by 50-gold bands; entering a shop later reconciles paid debt and removes the curses created by this mod.

## Compatibility

- Mod version: v0.2.1
- Game compatibility: mobile v0.103.2 and desktop v0.107.x
- Dependencies: none. BaseLib is not required.

## Install

Download the release ZIP and extract the `GreedyCredit` folder into the game's `mods` directory:

```text
mods/GreedyCredit/GreedyCredit.dll
mods/GreedyCredit/GreedyCredit.json
```

## Behavior

- Shop entries are considered affordable while stocked.
- Purchase/card-removal cost is charged manually after a successful purchase, so gold can go negative.
- Debt curses use the game's built-in curse card:
  - model id: `CARD.GREED`
  - display name: `Greed` / `贪婪`
  - type: Curse, keyword: Unplayable
- Desired curse count is `ceil(abs(gold) / 50)` while gold is negative.
  - -1 to -50: 1 curse
  - -51 to -100: 2 curses
  - -101 to -150: 3 curses
- On entering a merchant room, if gold is no longer as negative, the mod removes only the Greed curses it has tracked/created.

## v0.2.1 fixes

- Negative debt now uses ceiling bands, so `-1..-50` immediately gives 1 Greed curse.
- Card-removal service now uses the same credit/debt path as normal shop purchases; insufficient gold no longer gets clamped back to 0 by vanilla payment.

## v0.2.0 BaseLib-free build

v0.1.1 used BaseLib to register a custom Greed marker. v0.2.0 removes BaseLib entirely and uses StS2's built-in `MegaCrit.Sts2.Core.Models.Cards.Greed` (`CARD.GREED`) instead. If `CARD.GREED` is not already in `ModelDb`, the mod injects the game's own `Greed` model type directly.

## Build

```powershell
.\scripts\build.ps1 -Sts2Path 'G:\Asus\Steam\steamapps\common\Slay the Spire 2'
```

## Logs to verify

After launching, `%APPDATA%\SlayTheSpire2\logs\godot.log` should include:

```text
[GreedyCredit] loaded: shop credit enabled; any debt => built-in Greed curses by 50-gold bands; BaseLib not required.
[GreedyCredit] built-in Greed curse available in ModelDb: CARD.GREED
```

After a debt purchase crosses a -50 boundary, look for lines like:

```text
[GreedyCredit] debt curse add result added=True, deckGreeds 0->1, template=CARD.GREED
[GreedyCredit] add debt curses requested=1, added=1, desired=1
```
