# Meridian Works

**Three guided air-to-ground missiles for Nuclear Option: one optical, two laser.**

[![Game version](https://img.shields.io/badge/Nuclear%20Option-0.34%2B-orange?style=for-the-badge)](https://store.steampowered.com/app/2168680/Nuclear_Option/)

📝 **[What's new](./CHANGELOG.md)** &nbsp;·&nbsp;
🏷️ **[Attribution and terms](./ATTRIBUTION.md)** &nbsp;·&nbsp;
📖 **[Where it comes from](./LORE.md)**

---

## What it is

A guided anti-surface pack. One heavy fire-and-forget round for hard targets, and two
laser-guided rounds that ride a designation somebody else is holding.

| Weapon | Guidance | Range | AP | HE | Mass | Cost |
|---|---|---|---|---|---|---|
| AGM-84 | Optical, datalink | 20 km | 1100 | 330 | 660 kg | $1.1m |
| AGM-57L | Laser | 15 km | 400 | 220 | 520 kg | $550k |
| AGM-33L | Laser | 12 km | 250 | 90 | 300 kg | $300k |

The AGM-84 is locked on before release and needs nothing from you afterwards. The two
laser rounds need the target lit until impact, by any friendly source, which does not
have to be the launching aircraft. That is the trade: they are cheaper and lighter than
anything fire-and-forget in the same role, and they are useless without a designation.

## Carriage

| Weapon | Pylon | Bay |
|---|---|---|
| AGM-84 | single, twin | four-round, six-round |
| AGM-57L | single, twin | single |
| AGM-33L | single, twin, triple | single, twin |

Bay blocks are laid out in the prefab rather than measured at runtime, so a bomber
carries them the way the airframe was built to.

## Carrying these raises your lased target limit

An aircraft with any Meridian mount fitted holds **six** lased targets instead of the
stock three, which is what makes a multi-round laser load worth carrying.

It is a floor, not a bonus. If another mod has already raised the limit, this raises it
the rest of the way to six rather than adding on top, so two mods cannot compound into a
number neither of them intended.

## Installation

1. Install [BepInEx](https://github.com/BepInEx/BepInEx) for Nuclear Option.
2. Install Blueprinter, which loads the weapon assets. Meridian Works will not load
   without it.
3. Drop `MeridianWorks/` into `BepInEx/plugins/`.

The asset bundle is embedded in the DLL, so there is one file and no loose `.nobp` to
keep in step.

## About this source

`src/` is the mod's C# with the comments stripped, published so that anyone installing
the pack can read what it does before running it. It is not a buildable checkout: the
asset bundle and the Unity project that generates it are not here.

Being readable is not a grant of reuse. See [ATTRIBUTION.md](./ATTRIBUTION.md) for the
terms, including the parts of the effects work that are Shockfront Studios' and not ours.
