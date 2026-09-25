# Changelog

## 1.1.0

**Seven new rounds, new aircraft to carry them, real motor effects, and stores that cost
the aircraft something to carry.** Everything below is what changed since 1.0.2.

### New rounds

- **AGM-190A Black Arrow.** A small, cheap subsonic cruise missile carried in large
  numbers: 250 km, flies low on a small jet, then climbs to pick out its target and dives
  on it. Its wing and fins are folded on the rack and open after release. It hangs singly,
  in side-by-side pairs, on a four-round rack, and in weapon bays in blocks sized to each
  bay, up to eighteen at a time on the largest.
- **AGM-102 Kalibr.** A long-range subsonic cruise missile, 300 km, that flies a waypoint
  route low and slow and then climbs and dives on its target. Folded on the rail, open
  after release.
- **AGM-102E Kalibr.** The electronic-warfare Kalibr. It cruises silent, then jams one
  radar once it closes within 30 km: its own target, or, if another round already has
  that one, the nearest free radar in the lane ahead. Several fired together jam several
  radars. Its warhead is cut to a fifth. Fire it at the head of a salvo.
- **AShM-500 Yashma.** A heavy ramjet anti-ship missile that flies its final approach at
  Mach 2.5, five metres above the water.
- **AShM-140 Exocet.** An air-launched anti-ship missile that skims three metres above
  the water for its whole approach and flies a programmed route, so the launching
  aircraft can turn away at once.
- **AAM-90 Estoc.** A hypersonic long-range radar-guided air-to-air missile with a
  proximity fuse. It arrives too fast for a large aircraft to turn away.
- **AAM-120C.** A cheap medium-range radar-guided air-to-air missile. It flies as well as
  anything in its class; its radar is the weak part, and clutter or a beam-on target
  breaks the lock outright. It fits every bay the AAM-63 Falchion fits.

### Cruise missiles fly routes

The cruise rounds follow a route of several waypoints drawn on the map, in order, rather
than flying straight to the last one. A waypoint close to the launch point is reached
rather than overflown.

### Where the rounds go

- New carriage on the Darkreach, the F-22E Strike Raptor, the FS-3, the FS-41 and the
  AB-4's fore and aft bays.
- The GBP-500 Bodkin gains a nine-round bay block.
- Adds compatibility with the F-22E Strike Raptor: the pack's rounds go in its bays and on
  its wings.
- The PAB-125HD is offered in every fitting and on every hardpoint the PAB-125 is, bays
  included, instead of only as a triple on external pylons.

### Stores cost something to carry

Every external mount now adds radar signature, drag and weight to the aircraft carrying
it, per round. Before this most of the pack hung on a pylon for free. Internal bay mounts
add nothing, the same as the base game's own bay mounts.

### Motors look and sound like motors

- Every round draws its own flame, heat haze and smoke trail, shaped to its motor: solid
  boosters, sustainers, ramjets and small turbojets each look different.
- The smoke trail is one connected ribbon, the way the base game draws its own missiles.
- A boost and sustain motor draws one continuous plume, not a flame that lights, cuts and
  lights again.
- Every motor has a sound.

### Multiplayer

- Rounds fired by a player who is not hosting now fly under power and guide. Before, a
  client's Meridian rounds left the rail and coasted with no thrust.

### Handling and balance

- Air-to-air rounds lead a turning target along its turn over the whole
  flight instead of aiming where it is now.
- The AAM-63 Falchion no longer circles and self-destructs after losing its lock.
- The AAM-41 Gram turns less and costs more.
- Wing stores are moved clear of the flaps behind them.

### Smaller things

- The incoming-missile mark on the HUD shows for every round in the pack.
- Weapon descriptions rewritten in plain words.
- The pack's models are lighter, about a quarter fewer triangles, with no change in shape.

### Known issues

- The PAB-125HD's CCIP pipper is inaccurate. The bomb does not always land where the
  pipper shows.

## 1.0.2

Multiplayer works again, and the off-boresight ring reads the right way round.

- Joining a server works. With this pack installed the host flew fine and nobody else
  could join: the faction menu never initialised and the map came up empty. Every
  networked part of the pack was being given an identity worked out fresh on each
  machine, so a host and a client disagreed about what was what and the client never
  finished loading the mission. Identities now come from the part itself, so both sides
  reach the same answer with nothing negotiated. Everyone on the server needs 1.0.2.
- The off-boresight ring is steady amber when you cannot take the shot and pulses red
  when you can. It used to sit steady green for no shot, which is the colour the rest of
  the HUD uses to mean everything is fine.
- The 3x and 2x Falchion racks stand on the same pylon the single mount uses, which lifts
  the fitting clear of the wing. The triple was cutting into the FS-41 Eclipse's flaps.
- The AGM-92 burns for three and a half minutes instead of two and a half and tops out
  near Mach 0.97 instead of 0.88. Its listed range goes from 75 to 90 km to match.
- The stock PAB-125HD high drag bomb is available again.
- The AGM-92's exhaust fire sits ten centimetres further forward on both stages, so it
  stays put across the stage change.
- A bomb fuse change made after 1.0.1 has been taken back out. Bombs behave as they did
  in 1.0.0 and 1.0.1.

## 1.0.1

Bomb fixes. Nothing was added.

- The GBP-500 Bodkin no longer bursts in the air short of the target. It was destroying
  itself the moment it arrived over the aimpoint, and dropped in a stick each bomb went
  off earlier and higher than the one before it.
- The Bodkin goes through a hardened target again before it detonates, which is what the
  weapon is for. What it punches through is unchanged.
- The eighteen round Bodkin block no longer shows through the Darkreach's inner bay
  floor.
- The PAB-125HD is written up in the README. It is not new here: 1.0.0 already switched
  it back on.

## 1.0.0

The first public release.

**Delete any existing `BepInEx/plugins/MeridianWorks/` folder before installing this
one.** The asset bundle is embedded in the DLL, and Blueprinter keeps the higher version
number when it finds two. A leftover bundle from an earlier test build carries a higher
number than this release does and would quietly win.


Nine more weapons, and the pack stops being three air to ground missiles.

- **AAM-41 Gram** and **AAM-63 Falchion**, active radar air to air missiles at 80 and
  60 km.
- **IRM-L7** and **SRM-8 Kukri**, heat seekers, one long legged and one for close in.
- **AGM-92**, a cruise missile that boosts and then holds just under the speed of sound
  for most of a 75 km run.
- **ARAD-72**, which homes on a radar that is transmitting.
- **GBO-900**, a heavy glide bomb, and **GBP-500 Bodkin**, which goes through concrete
  before it goes off.
- **AGR-40 Hairpin**, guided rockets in a four round or twelve round pod, wherever the
  stock Kingpin pods go.

Also new:

- Multi round fittings on pylons and blocks inside weapon bays, decided by what the
  station or bay already carries rather than by a list.
- A ring on the HUD over a target a heat seeking missile can still be launched at when
  the shot is wide.
- Nothing released can strike the aircraft that released it for a second and a half, or
  two seconds for a bomb.
- A hardened bomb detonates after it penetrates instead of going quiet.
- Air to air weapons on the two largest airframes are event content.


## 1.0.0

First public release.

Three guided air to ground weapons from the Meridian Combine, carried on pylons and in
bays across the aircraft that should have them.

- **AGM-84 Warhawk.** Optical seeker with a datalink. Locked on before release and needs
  nothing from you afterwards.
- **AGM-57L Bulldog.** Laser guided, big blast warhead, cheaper and lighter than anything
  fire and forget in the same role.
- **AGM-33L Hornet.** The light one, for helicopters and light attack aircraft.

Carrying any Meridian mount raises your lased target limit to six. It is a floor rather
than a bonus, so it will not compound with another mod that raises the same limit.

One setting, `Diagnostics`, off by default. Turn it on to put detailed lines in the
BepInEx log when you are reporting a problem.

Needs Blueprinter. The asset bundle is embedded in the DLL, so there is one file to
install and nothing to keep in step.
