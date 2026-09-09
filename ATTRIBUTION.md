# Attribution

## Visual and audio effects

The motor plumes, impact effects and explosions used by the weapons in this pack are
derived from Nuclear Option's own effects, created by **Shockfront Studios**. They
were taken from an installed copy of the game and edited for these weapons.

They are not original work and are not offered under this pack's terms. All rights
in them remain with Shockfront Studios.

## Mount textures

The two authored mounts are modelled by **[mosdef31](https://github.com/mosdef31)**,
but they are painted with texture sets taken from third-party models rather than
authored for them. Four sets are used, two per mount:

| Mount | Part | Texture set |
|---|---|---|
| `meridian_pylon_x2_heavy` | pylon shell | `f100_pylon5` |
| `meridian_pylon_x2_heavy` | rail | `lau_7` |
| `meridian_pylon_x2_light` | launcher shoe | `ch_pl12_launcher` |
| `meridian_pylon_x2_light` | pylon | `j_10a_wing_twin_pylon` |

They are not original work and their origin is recorded in the note below.

## The Exocet MM40 donor is NOT under this pack's licence

The Exocet MM40 Block 3C model is by **lash2145** on CGTrader, under CGTrader's
**Royalty Free License**:

https://www.cgtrader.com/designers/lash2145?utm_source=credit&utm_source=credit_item_page

That licence permits the mesh to be used inside a game and shipped inside the compiled
bundle. It does **not** permit redistribution in editable or extractable form and it does
**not** permit sublicensing.

**So this donor is carved out of the pack's CC BY 4.0 grant.** Nothing in this pack's
terms applies to it, no rights in it are passed on, and it is not offered for reuse. The
source `.blend` and its texture set are not published anywhere in this project. All
rights in the model remain with lash2145.

## Everything else

The weapon data and source code are original work by
**[mosdef31](https://github.com/mosdef31)**.

The source is published so that anyone installing the pack can read what it does
before running it. Being readable is not a grant: it is not open source, and no
licence to reuse it is given here.

---

## Verified 2026-09-07: every donor is CC BY, and the authors are named

Queried against the Sketchfab API (`api.sketchfab.com/v3/models/<uid>`), not read off a
cached page. All eleven donor pages return licence `CC Attribution` (slug `by`).

| Author | Covers |
|---|---|
| **KojfDiscord** | Kh-29TD, AS-30L, Kh-25ML, GBU-15, KAB-500Kr, and all four mount aircraft |
| **Rhine_Lab_Muelsyse** | Russian weapon pack, so RVV-AE-PD, R-77M, R-73, R-27R, Kh-31P |
| **Jeyhun1985** | F-111F Aardvark with AGM-130A, and the AIM-120C AMRAAM |
| **Peter Primini (Planetrix23)** | AIM-160A Screamer |

The last two were added on 2026-09-08 and checked the same way, against
`api.sketchfab.com/v3/search`. The Screamer's download is the only one of the eleven
that shipped a `license.txt` of its own. The Exocet MM40 is not on Sketchfab and is
covered separately below.

Two consequences, both settled:

- **The textures question is closed without needing the two inferred mappings.** All
  four mount texture sets come off the four mount aircraft, and all four of those are
  KojfDiscord. Whichever aircraft `lau_7_*` and `ch_pl12_launcher_*` actually came off,
  the credit is the same name, so the published file credits the author and does not
  claim the aircraft. The round textures are the Kh-25ML, AS-30L and Kh-29 sets, also
  KojfDiscord.
- **The pack's licence does not move.** It was already CC BY 4.0 because of the first
  three models, and every donor added since is on the same terms.

The published `ATTRIBUTION.md` was rewritten to carry all of this and pushed as
`3608f88` on `mosdef31/NO-Meridian-Works`. The Shockfront effects question below is
untouched and is still the owner's.

## Shockfront's effects, still to resolve

Shipping the game's effects inside our bundle is a different thing from borrowing
them at runtime, which is what the pack does for pylons, racks and warheads and
which redistributes nothing at all. Whether Shockfront's terms allow their assets to
be redistributed inside a mod is a question about their EULA, not about this file,
and mod scenes vary in how they treat it.

Read Shockfront's terms on redistributing extracted assets, or ask them. A short
answer from the developer is worth more than any wording here. The README already
says plainly that the effects are the game's own.

The alternative, already planned as a separate project, is to study the extracted
effects, author replacements, and drop this section entirely.

## Donor models

Every weapon body and every borrowed mount in this pack is adapted from a
third-party model.

### Weapon bodies

Authors are in the verified table above. All CC BY 4.0.

| Weapon | Donor | Source |
|---|---|---|
| AAM-41 Gram | RVV-AE-PD | Russian weapon pack, below |
| AAM-63 Falchion | R-77M | Russian weapon pack, below |
| SRM-8 Kukri | R-73 | Russian weapon pack, below |
| IRM-L7 | R-27R, standing in for the R-27T | Russian weapon pack, below |
| ARAD-72 | Kh-31P | Russian weapon pack, below |
| AGM-84 | Kh-29TD | https://sketchfab.com/3d-models/su-kh-29td-missile-war-thunder-fb22c00fe4b246c59d4837a15a2d69f6 |
| AGM-57L | AS-30L | https://sketchfab.com/3d-models/fr-as-30l-nord-missile-war-thunder-dbaaa50115c648e9a188aa90c4121360 |
| AGM-33L | Kh-25ML | https://sketchfab.com/3d-models/su-kh-25ml-war-thunder-71e9333f0c624f75a0de263a3308ac84 |
| GBO-900 | GBU-15 | https://sketchfab.com/3d-models/us-gbu-15v1b-guided-bomb-war-thunder-515e562805fc4fe488cbe3441bf4b288 |
| AGM-92 | AGM-130 | https://sketchfab.com/3d-models/f-111f-aardvark-with-agm-130a-1ae233493f2b4d5a83c2498d44702c9a |
| GBP-500 Bodkin | KAB-500Kr | https://sketchfab.com/3d-models/su-kab-500kr-500-kg-bomb-war-thunder-4e779192f9ba4c3aa0ac8644a8784591 |
| Screamer | AIM-160A Screamer, by Peter Primini (Planetrix23) | Sketchfab, CC BY 4.0 |
| AIM-120C | AIM-120C AMRAAM, by Jeyhun1985 | Sketchfab, CC Attribution |
| Exocet AM39 | Exocet MM40 Block 3C, by lash2145 | CGTrader, see the section above |

**Russian weapon pack**, covering the five rounds above:
https://sketchfab.com/3d-models/russian-weapon-pack-af00b7135a184ecfb2812a0e54458a8a

### Mounts, pylons and rails

All borrowed at load and modelled by other people, all four by KojfDiscord under
CC BY 4.0. They come in through `pylon-bench.blend`.

| Mount | Source |
|---|---|
| Black x6 radial pylon | F-111A Aardvark (Custom Payload) (War Thunder), https://sketchfab.com/3d-models/f-111a-aardvark-custom-payload-war-thunder-8dec9494392247c48a1fe0bfec262240 |
| Variable x2 pylon | F-100F Super Sabre 1 (Custom) (War Thunder), https://sketchfab.com/3d-models/tw-f-100f-super-sabre-1-custom-war-thunder-8b0d77f07b5c4cb296c178b7abc317b9 |
| x2 pylon (JF) | JF-17 (Custom Payload) (War Thunder), https://sketchfab.com/3d-models/jf-17-custom-payload-war-thunder-f1eef256dc33429ba62008b67380f5f4 |
| 01 x3 pylon, spin and rails | F-4J Phantom II 4 (Custom) (War Thunder), https://sketchfab.com/3d-models/f-4j-phantom-ii-4-custom-war-thunder-af1cc0d95fb540739aee8a18ca6985c7 |

### Two mappings are INFERRED, not stated

The owner named the mounts by how they look; the project files are named after the
aircraft the mesh came off. Four names and four sources match cleanly. Two do not
line up by name alone and were matched by reasoning rather than told:

- **`lau_7_*`** is taken to come from the **F-4J Phantom II** page, which is the
  one described as carrying rails. The LAU-7 is a Sidewinder rail and the Phantom
  is the obvious donor for one.
- **`ch_pl12_launcher_*`** is taken to come from the **JF-17** page. The PL-12 is
  Chinese, the JF-17 carries it, and no other supplied source is Chinese.

The same reasoning resolves the question this file used to raise: the textures
named `j_10a_wing_twin_pylon_*` sit with `ch_pl12_launcher_*`, so both most likely
came off the JF-17 model despite the J-10A file name.

They are the only two lines here that nobody has actually stated. **They no longer
block publishing**: both candidate aircraft are KojfDiscord's, so the credit is the
same either way, and the published file names the author rather than the aircraft.

### Authored here

`meridian_pylon_x2_heavy.fbx` and `meridian_pylon_x2_light.fbx` are modelled by
[mosdef31](https://github.com/mosdef31) and need no third-party credit.
