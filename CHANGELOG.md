# Changelog

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
