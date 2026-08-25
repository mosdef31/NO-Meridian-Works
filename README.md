# Meridian Works

**A weapon pack for Nuclear Option.**

[![Latest release](https://img.shields.io/github/v/release/mosdef31/NO-Meridian-Works?style=for-the-badge&label=download&color=2ea043)](https://github.com/mosdef31/NO-Meridian-Works/releases/latest)
[![Downloads](https://img.shields.io/github/downloads/mosdef31/NO-Meridian-Works/total?style=for-the-badge&color=blue)](https://github.com/mosdef31/NO-Meridian-Works/releases)
[![Game version](https://img.shields.io/badge/Nuclear%20Option-0.34%2B-orange?style=for-the-badge)](https://store.steampowered.com/app/2168680/Nuclear_Option/)
[![License](https://img.shields.io/badge/license-CC%20BY%204.0-lightgrey?style=for-the-badge)](./LICENSE)
[![Issues](https://img.shields.io/github/issues/mosdef31/NO-Meridian-Works?style=for-the-badge&color=orange)](https://github.com/mosdef31/NO-Meridian-Works/issues)

📥 **[Download the latest release](https://github.com/mosdef31/NO-Meridian-Works/releases/latest)** &nbsp;·&nbsp;
📝 **[What's new](./CHANGELOG.md)** &nbsp;·&nbsp;
🏷️ **[Credits and licence](./ATTRIBUTION.md)** &nbsp;·&nbsp;
📖 **[Where it comes from](./LORE.md)** &nbsp;·&nbsp;
🐛 **[Report a bug](https://github.com/mosdef31/NO-Meridian-Works/issues)**

---

## What it is

Guided weapons from the Meridian Combine, an arms exporter that sells to both sides. The
pack grows over time; this is what is in it now.

### Air to ground

| Weapon | Guidance | Range | AP | HE | Mass | Cost |
|---|---|---|---|---|---|---|
| AGM-84 Hornbeam | Optical, datalink | 20 km | 1100 | 330 | 660 kg | $1.1m |
| AGM-57L Alder | Laser | 15 km | 400 | 220 | 520 kg | $550k |
| AGM-33L Bramble | Laser | 12 km | 250 | 90 | 300 kg | $300k |

- **AGM-84 Hornbeam:** locked on before release and needs nothing from you afterwards. A
  datalink keeps the aimpoint fresh, so you can shoot from further out than you can
  identify. The heavy answer to hardened targets.
- **AGM-57L Alder:** rides a laser somebody is holding, not necessarily you. Big blast
  warhead, and cheaper and lighter than anything fire and forget in the same role.
- **AGM-33L Bramble:** the light one, for helicopters and light attack aircraft. A guided
  answer to armour at a price nothing fire and forget reaches.

The two laser rounds need the target lit until impact. That is the trade.

## Carriage

| Weapon | Pylon | Bay |
|---|---|---|
| AGM-84 Hornbeam | single, twin | four-round, six-round |
| AGM-57L Alder | single, twin | single |
| AGM-33L Bramble | single, twin, triple | single, twin |

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

The asset bundle is embedded in the DLL, so there is one file and nothing to keep in step.

## AI use

I use an AI agent to help with coding, refactoring, asset modification, and authoring
long bodies of text and lore.

It raises the quality ceiling beyond what my own skills currently guarantee, while I
learn and develop them. Every decision, every number, and everything that ships is mine.

## About this source

`src/` is the mod's C# with the comments stripped, published so that anyone installing
the pack can read what it does before running it. It is not a buildable checkout: the
asset bundle and the Unity project that generates it are not here.

The models are not mine. See [ATTRIBUTION.md](./ATTRIBUTION.md) for who made them and
what the licence requires of you.
