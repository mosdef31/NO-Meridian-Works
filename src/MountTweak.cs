using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using BepInEx;
using UnityEngine;

namespace MeridianWorks
{

    internal static class MountTweak
    {
        internal const string FileName = "meridian-mount-tweaks.txt";

        internal struct Offset
        {

            internal Vector3 Move;

            internal Vector3 Turn;

            internal float Rail;

            internal bool IsZero =>
                Move.sqrMagnitude < 1e-10f && Turn.sqrMagnitude < 1e-10f
                && Mathf.Abs(Rail) < 1e-5f;
        }

        internal struct PlumeOverride
        {
            internal float? Aft;
            internal float? Width;
            internal float? Glow;
            internal float? Blue;
            internal float? Smoke;
        }

        private static readonly Dictionary<string, Offset> _mounts =
            new Dictionary<string, Offset>(StringComparer.Ordinal);

        private static readonly Dictionary<string, Offset> _authored =
            new Dictionary<string, Offset>(StringComparer.Ordinal)
            {

                { "Aryx_MC260_Chimera | Aryx_Cargo1_Fuselage_Floor/BombBay_Left | MeridianKalibr_internalx4",
                  new Offset { Move = new Vector3(0f, 0f, 0.450f), Rail = 0.925f } },
                { "Aryx_MC260_Chimera | Aryx_Cargo1_Fuselage_Floor/BombBay_Right | MeridianKalibr_internalx4",
                  new Offset { Move = new Vector3(0f, 0f, 0.450f), Rail = 0.925f } },

                { "Darkreach | weaponbay_L | MeridianKalibr_internalx4_lifted",
                  new Offset { Move = new Vector3(0f, -0.867f, 0f), Rail = -0.050f } },
                { "Darkreach | weaponbay_R | MeridianKalibr_internalx4_lifted",
                  new Offset { Move = new Vector3(0f, -0.867f, 0f), Rail = -0.050f } },

                { "Darkreach | engine_L/weaponbay_LL | MeridianKalibr_internalx2_lifted",
                  new Offset { Move = new Vector3(0f, -1.500f, 0f), Rail = 0.250f } },
                { "Darkreach | engine_R/weaponbay_RR | MeridianKalibr_internalx2_lifted",
                  new Offset { Move = new Vector3(0.002f, -1.500f, 0f), Rail = 0.250f } },

                { "FastBomber1 | weaponBay_combined | MeridianKalibr_internalx2",
                  new Offset { Move = new Vector3(0f, 0.050f, 0f), Rail = -0.150f } },

                { "Aryx_MC260_Chimera | Aryx_Cargo1_Fuselage_Floor/BombBay_Left | MeridianKalibrEW_internalx4",
                  new Offset { Move = new Vector3(0f, 0f, 0.450f), Rail = 0.925f } },
                { "Aryx_MC260_Chimera | Aryx_Cargo1_Fuselage_Floor/BombBay_Right | MeridianKalibrEW_internalx4",
                  new Offset { Move = new Vector3(0f, 0f, 0.450f), Rail = 0.925f } },
                { "Darkreach | weaponbay_L | MeridianKalibrEW_internalx4_lifted",
                  new Offset { Move = new Vector3(0f, -0.867f, 0f), Rail = -0.050f } },
                { "Darkreach | weaponbay_R | MeridianKalibrEW_internalx4_lifted",
                  new Offset { Move = new Vector3(0f, -0.867f, 0f), Rail = -0.050f } },
                { "Darkreach | engine_L/weaponbay_LL | MeridianKalibrEW_internalx2_lifted",
                  new Offset { Move = new Vector3(0f, -1.500f, 0f), Rail = 0.250f } },
                { "Darkreach | engine_R/weaponbay_RR | MeridianKalibrEW_internalx2_lifted",
                  new Offset { Move = new Vector3(0.002f, -1.500f, 0f), Rail = 0.250f } },
                { "FastBomber1 | weaponBay_combined | MeridianKalibrEW_internalx2",
                  new Offset { Move = new Vector3(0f, 0.050f, 0f), Rail = -0.150f } },

                { "Darkreach | weaponbay_L | MeridianBlackArrow_internalx18",
                  new Offset { Rail = 0.900f } },
                { "Darkreach | weaponbay_R | MeridianBlackArrow_internalx18",
                  new Offset { Rail = 0.900f } },
                { "Darkreach | engine_L/weaponbay_LL | MeridianBlackArrow_internalx9_flat",
                  new Offset { Rail = 1.100f } },
                { "Darkreach | engine_R/weaponbay_RR | MeridianBlackArrow_internalx9_flat",
                  new Offset { Rail = 1.100f } },
                { "Aryx_MC260_Chimera | Aryx_Cargo1_Fuselage_Floor/BombBay_Left | MeridianBlackArrow_internalx9_flat",
                  new Offset { Rail = 0.600f } },
                { "Aryx_MC260_Chimera | Aryx_Cargo1_Fuselage_Floor/BombBay_Right | MeridianBlackArrow_internalx9_flat",
                  new Offset { Rail = 0.600f } },
                { "EW1 | weaponBayHardpoint | MeridianBlackArrow_internalx2_stack",
                  new Offset { Rail = 1.000f } },
                { "P_Trisurface1 | fuselage_F/weaponBay_F | MeridianBlackArrow_internalx3",
                  new Offset { Rail = 0.900f } },

                { "Aryx_KingRaptor | Pylon_Weaponbay_L_Outer | MeridianBlackArrow_internalx2_tandem_tight",
                  new Offset { Move = new Vector3(0.040f, 0f, 0.115f) } },
                { "Aryx_KingRaptor | Pylon_Weaponbay_R_Outer | MeridianBlackArrow_internalx2_tandem_tight",
                  new Offset { Move = new Vector3(-0.040f, 0f, 0.115f) } },
                { "Aryx_KingRaptor | Pylon_Weaponbay_L_Inner | MeridianBlackArrow_internalx2_tandem_tight",
                  new Offset { Move = new Vector3(-0.050f, 0.021f, 0.040f) } },
                { "Aryx_KingRaptor | Pylon_Weaponbay_R_Inner | MeridianBlackArrow_internalx2_tandem_tight",
                  new Offset { Move = new Vector3(0.050f, 0.021f, 0.040f) } },

                { "Aryx_KingRaptor | Pylon_Weaponbay_L | MeridianBlackArrow_internalx4_tandem_tight",
                  new Offset { Move = new Vector3(0.074f, 0.009f, 0.023f) } },
                { "Aryx_KingRaptor | Pylon_Weaponbay_R | MeridianBlackArrow_internalx4_tandem_tight",
                  new Offset { Move = new Vector3(-0.074f, 0.009f, 0.023f) } },
            };

        internal static bool HasAuthoredSeat(Hardpoint? hardpoint, WeaponMount? mount)
        {
            return _authored.ContainsKey(KeyFor(hardpoint, mount));
        }

        private static readonly Dictionary<string, PlumeOverride> _plumes =
            new Dictionary<string, PlumeOverride>(StringComparer.Ordinal);

        private static readonly Dictionary<string, string> _donors =
            new Dictionary<string, string>(StringComparer.Ordinal);

        private static bool _loaded;
        private static string _path = "";

        private static readonly List<string> _unknown = new List<string>();

        internal static string Path
        {
            get
            {
                if (_path.Length == 0)
                    _path = System.IO.Path.Combine(Paths.ConfigPath, FileName);
                return _path;
            }
        }

        internal static void EnsureLoaded()
        {
            if (_loaded) return;
            _loaded = true;

            try
            {
                if (!File.Exists(Path)) return;
                ParseInto(File.ReadAllLines(Path));
                Plugin.Log.LogInfo(
                    "[Meridian] Mount tweaks: read " + _mounts.Count + " mount offset(s) and "
                    + _plumes.Count + " plume trim(s) from '" + Path + "'. These OVERRIDE the "
                    + "seating rules and the compiled trim table, so if a store is not where "
                    + "the code says it should be, this file is the first thing to look at.");
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning(
                    "[Meridian] Mount tweaks: '" + Path + "' could not be read, so none were "
                    + "applied: " + e.Message);
            }
        }

        private static void ParseInto(string[] lines)
        {

            _unknown.Clear();

            string section = "mounts";

            foreach (string raw in lines)
            {
                string line = raw.Trim();
                if (line.Length == 0 || line[0] == '#') continue;

                if (line[0] == '[' && line[line.Length - 1] == ']')
                {
                    section = line.Substring(1, line.Length - 2).Trim().ToLowerInvariant();
                    continue;
                }

                if (section != "mounts" && section != "donors" && section != "plumes")
                {
                    _unknown.Add("[" + section + "] " + line);
                    continue;
                }

                int eq = line.IndexOf('=');
                if (eq <= 0) continue;

                string key = line.Substring(0, eq).Trim();
                string[] parts = line.Substring(eq + 1)
                    .Split(new[] { ' ', '\t', ',' }, StringSplitOptions.RemoveEmptyEntries);

                if (section == "donors")
                {
                    string donor = line.Substring(eq + 1).Trim();
                    if (donor.Length > 0 && donor != "-") _donors[key] = donor;
                    continue;
                }

                if (section == "plumes")
                {
                    var p = new PlumeOverride();
                    if (parts.Length > 0) p.Aft = Num(parts[0]);
                    if (parts.Length > 1) p.Width = Num(parts[1]);
                    if (parts.Length > 2) p.Glow = Num(parts[2]);
                    if (parts.Length > 3) p.Blue = Num(parts[3]);
                    if (parts.Length > 4) p.Smoke = Num(parts[4]);
                    _plumes[key] = p;
                    continue;
                }

                if (parts.Length < 3) continue;
                var o = new Offset
                {
                    Move = new Vector3(Or0(Num(parts[0])), Or0(Num(parts[1])), Or0(Num(parts[2])))
                };
                if (parts.Length >= 6)
                    o.Turn = new Vector3(Or0(Num(parts[3])), Or0(Num(parts[4])), Or0(Num(parts[5])));

                if (parts.Length >= 7) o.Rail = Or0(Num(parts[6]));
                _mounts[key] = o;
            }
        }

        private static float Or0(float? v) { return v.HasValue ? v.Value : 0f; }

        private static float? Num(string s)
        {
            if (s == "-" || s == "_") return null;
            float v;
            if (float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v)) return v;
            return null;
        }

        internal static string KeyFor(Hardpoint? hardpoint, WeaponMount? mount)
        {
            string air = "unknown";
            Transform? root = hardpoint != null ? PylonBorrow.AircraftRootOf(hardpoint) : null;
            if (root != null) air = root.name;

            string point = "unknown";
            if (hardpoint != null && hardpoint.transform != null)
                point = PathUnder(hardpoint.transform, root);

            string key = mount != null && !string.IsNullOrEmpty(mount.jsonKey)
                ? mount.jsonKey
                : "unknown";

            return air + " | " + point + " | " + key;
        }

        private static string PathUnder(Transform t, Transform? stop)
        {
            var parts = new List<string>();
            Transform? cur = t;
            while (cur != null && cur != stop)
            {
                parts.Add(cur.name);
                cur = cur.parent;
            }
            parts.Reverse();
            return string.Join("/", parts.ToArray());
        }

        internal sealed class Live
        {
            internal string Key = "";
            internal Transform? Root;
            internal Vector3 BasePos;
            internal Quaternion BaseRot;
            internal Offset Offset;

            internal Transform? Point;

            internal Transform? Air;

            internal string Mount = "";

            internal Round[] Rounds = _noRounds;
        }

        internal sealed class Round
        {
            internal Transform? T;

            internal Vector3 BasePos;

            internal Vector3 AxisInParent;
        }

        private static readonly Round[] _noRounds = new Round[0];

        private static readonly System.Reflection.FieldInfo? _railDirField =
            HarmonyLib.AccessTools.Field(typeof(MountedMissile), "railDirection");

        private static Vector3 RailVectorOf(MountedMissile round)
        {
            try
            {
                if (_railDirField != null && round != null)
                {
                    switch ((int)_railDirField.GetValue(round))
                    {
                        case 0: return new Vector3(0f, 0f, 1f);
                        case 1: return new Vector3(0f, -1f, 0f);
                        case 2: return new Vector3(1f, 0f, 0f);
                        case 3: return new Vector3(-1f, 0f, 0f);
                        case 4: return new Vector3(0f, 0f, -1f);
                        case 5: return new Vector3(0f, 1f, 0f);
                    }
                }
            }
            catch { }
            return new Vector3(0f, 0f, 1f);
        }

        private static Round[] RoundsOf(Transform root)
        {
            var list = new List<Round>();
            try
            {
                foreach (MountedMissile mm in root.GetComponentsInChildren<MountedMissile>(true))
                {
                    if (mm == null || mm.transform == null) continue;

                    if (ReferenceEquals(mm.transform, root)) continue;

                    list.Add(new Round
                    {
                        T = mm.transform,
                        BasePos = mm.transform.localPosition,
                        AxisInParent = mm.transform.localRotation * RailVectorOf(mm),
                    });
                }
            }
            catch { }
            return list.Count == 0 ? _noRounds : list.ToArray();
        }

        internal static readonly List<Live> _live = new List<Live>();

        internal static void Register(Hardpoint hardpoint, WeaponMount mount, GameObject spawned)
        {
            if (spawned == null) return;
            EnsureLoaded();

            string key = KeyFor(hardpoint, mount);

            var live = new Live
            {
                Key = key,
                Root = spawned.transform,
                BasePos = spawned.transform.localPosition,
                BaseRot = spawned.transform.localRotation,
                Point = hardpoint != null ? hardpoint.transform : null,
                Air = hardpoint != null ? PylonBorrow.AircraftRootOf(hardpoint) : null,
                Mount = mount != null && !string.IsNullOrEmpty(mount.jsonKey) ? mount.jsonKey : "",
                Rounds = RoundsOf(spawned.transform),
            };

            Offset saved;
            if (_mounts.TryGetValue(key, out saved)) live.Offset = saved;
            else if (mount != null && !string.IsNullOrEmpty(mount.jsonKey)
                     && _mounts.TryGetValue("* | * | " + mount.jsonKey, out saved))
                live.Offset = saved;
            else if (_authored.TryGetValue(key, out saved)) live.Offset = saved;

            _live.Add(live);
            Push(live);

            if (!live.Offset.IsZero)
                Plugin.Diag(
                    "[Meridian] TWEAK " + key + ": moved ("
                    + live.Offset.Move.x.ToString("0.000") + ", "
                    + live.Offset.Move.y.ToString("0.000") + ", "
                    + live.Offset.Move.z.ToString("0.000") + ") m and turned ("
                    + live.Offset.Turn.x.ToString("0.0") + ", "
                    + live.Offset.Turn.y.ToString("0.0") + ", "
                    + live.Offset.Turn.z.ToString("0.0") + ") deg by "

                    + (_mounts.ContainsKey(key) ? FileName : "the compiled seat table")
                    + ", AFTER the seating rules.");
        }

        internal static void Push(Live? live)
        {
            if (live == null || live.Root == null) return;
            live.Root.localRotation = live.BaseRot
                * Quaternion.Euler(live.Offset.Turn.x, live.Offset.Turn.y, live.Offset.Turn.z);

            live.Root.localPosition = live.BasePos + live.Offset.Move;

            foreach (Round r in live.Rounds)
            {
                if (r.T == null) continue;
                r.T.localPosition = r.BasePos + r.AxisInParent * live.Offset.Rail;
            }
        }

        internal static string? DonorFor(string missileKey)
        {
            EnsureLoaded();
            return _donors.TryGetValue(missileKey, out string donor) ? donor : null;
        }

        internal static bool TryPlume(string missileKey, int stage, out PlumeOverride trim)
        {
            EnsureLoaded();
            return _plumes.TryGetValue(
                missileKey + "/" + stage.ToString(CultureInfo.InvariantCulture), out trim);
        }

        internal sealed class Fx
        {
            internal string MissileKey = "";
            internal int Stage;
            internal string Role = "plume";
            internal Transform? Root;
            internal Vector3 BasePos;
            internal float BaseAft;

            internal ParticleSystem[] Systems = new ParticleSystem[0];

            internal bool Frozen;

            internal float Extra;

            internal Missile? Round;

            internal float Aft => BaseAft + Extra;

            internal string Key => MissileKey + "/" + Stage.ToString(CultureInfo.InvariantCulture);

            internal string Label =>
                Key + " " + Role + "  aft " + Aft.ToString("0.000", CultureInfo.InvariantCulture)
                + " m";
        }

        internal static readonly List<Fx> _fx = new List<Fx>();

        internal static void RegisterFx(string missileKey, int stage, string role,
                                        Transform? root, float seatedAft)
        {
            if (root == null) return;

            ParticleSystem[] systems;
            try { systems = root.GetComponentsInChildren<ParticleSystem>(true); }
            catch { systems = new ParticleSystem[0]; }

            Missile? round = null;
            try { round = root.GetComponentInParent<Missile>(); }
            catch { }

            _fx.Add(new Fx
            {
                MissileKey = missileKey,
                Stage = stage,
                Role = role,
                Root = root,
                BasePos = root.localPosition,
                BaseAft = seatedAft,
                Systems = systems ?? new ParticleSystem[0],
                Round = round,
            });
        }

        internal static void PushFx(Fx? fx)
        {
            if (fx == null || fx.Root == null) return;
            fx.Root.localPosition = fx.BasePos + new Vector3(0f, 0f, -fx.Extra);
        }

        internal static string Save(IDictionary<string, string>? donorPicks = null)
        {
            try
            {

                if (File.Exists(Path)) ParseInto(File.ReadAllLines(Path));

                if (File.Exists(Path))
                {
                    try { File.Copy(Path, Path + ".bak", true); }
                    catch (Exception be)
                    {
                        Plugin.Log.LogWarning("[Meridian] Mount tweaks: no backup made ("
                                              + be.Message + "); saving anyway.");
                    }
                }

                var sb = new StringBuilder();
                sb.AppendLine("# Meridian Works mount tweaks.");
                sb.AppendLine("# Written "
                    + DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)
                    + " by the in-game editor.");
                sb.AppendLine("#");
                sb.AppendLine("# [mounts]  <aircraft> | <hardpoint> | <mount key> = dx dy dz [pitch yaw roll [rail]]");
                sb.AppendLine("#           Applied AFTER every seating rule.");
                sb.AppendLine("#           dx dy dz are metres in the HARDPOINT'S frame, which is the");
                sb.AppendLine("#           aeroplane's: +z forward, +y up, +x right, whatever turn the");
                sb.AppendLine("#           mount itself was seated with. Before 2026-09-14 they were in");
                sb.AppendLine("#           the mount's own turned frame, so +z raised a bay mount instead");
                sb.AppendLine("#           of moving it forward - RE-MEASURE any line written before then.");
                sb.AppendLine("#           pitch yaw roll are degrees about the MOUNT'S own axes, unchanged.");
                sb.AppendLine("#           An aircraft and hardpoint of '*' apply to that mount everywhere.");
                sb.AppendLine("#           `rail` is the SEVENTH number and is different in kind from the other");
                sb.AppendLine("#           six: it moves each ROUND along its own rail vector and leaves the");
                sb.AppendLine("#           launcher, the shoe and every other authored part where they are.");
                sb.AppendLine("#");
                sb.AppendLine("# [donors]  <missile key> = <donor key or any part of one>");
                sb.AppendLine("#           The stock motor effect this round borrows its SMOKE from.");
                sb.AppendLine("#           Overrides MotorEffects.Recipes. Several may be given,");
                sb.AppendLine("#           separated by ';', and the first one loaded in the mission wins.");
                sb.AppendLine("#");
                sb.AppendLine("# [plumes]  <missile key>/<stage> = aft width glow blue smoke");
                sb.AppendLine("#           Overrides MotorEffects' compiled trim for that stage. '-' leaves a");
                sb.AppendLine("#           field as the code has it. `aft` is metres further back along -z.");
                sb.AppendLine();
                sb.AppendLine("[mounts]");

                var written = new HashSet<string>(StringComparer.Ordinal);
                foreach (Live live in _live)
                {
                    if (live.Offset.IsZero) continue;
                    if (!written.Add(live.Key)) continue;
                    sb.AppendLine(Line(live.Key, live.Offset));
                }

                foreach (KeyValuePair<string, Offset> kv in _mounts)
                {
                    if (written.Contains(kv.Key)) continue;
                    if (kv.Value.IsZero) continue;
                    sb.AppendLine(Line(kv.Key, kv.Value));
                }

                sb.AppendLine();
                sb.AppendLine("[donors]");

                var donorWritten = new HashSet<string>(StringComparer.Ordinal);
                if (donorPicks != null)
                {
                    foreach (KeyValuePair<string, string> kv in donorPicks)
                    {
                        if (string.IsNullOrEmpty(kv.Value)) continue;
                        if (!donorWritten.Add(kv.Key)) continue;
                        sb.AppendLine(kv.Key + " = " + kv.Value);
                    }
                }

                foreach (KeyValuePair<string, string> kv in _donors)
                {
                    if (donorWritten.Contains(kv.Key)) continue;
                    sb.AppendLine(kv.Key + " = " + kv.Value);
                }

                sb.AppendLine();
                sb.AppendLine("[plumes]");

                var fxWritten = new HashSet<string>(StringComparer.Ordinal);
                foreach (Fx fx in _fx)
                {
                    if (Mathf.Abs(fx.Extra) < 0.0005f) continue;
                    if (!fxWritten.Add(fx.Key)) continue;

                    PlumeOverride had;
                    _plumes.TryGetValue(fx.Key, out had);
                    sb.AppendLine(fx.Key + " = "
                        + fx.Aft.ToString("0.###", CultureInfo.InvariantCulture) + " "
                        + F(had.Width) + " " + F(had.Glow) + " " + F(had.Blue) + " "
                        + F(had.Smoke));
                }

                foreach (KeyValuePair<string, PlumeOverride> kv in _plumes)
                {
                    if (fxWritten.Contains(kv.Key)) continue;
                    PlumeOverride p = kv.Value;
                    sb.AppendLine(kv.Key + " = " + F(p.Aft) + " " + F(p.Width) + " " + F(p.Glow)
                                  + " " + F(p.Blue) + " " + F(p.Smoke));
                }

                if (_unknown.Count > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine("# Kept verbatim from the previous file. This build does not");
                    sb.AppendLine("# understand these sections and has not touched their values.");
                    string last = "";
                    foreach (string entry in _unknown)
                    {
                        int close = entry.IndexOf(']');
                        if (close <= 0) continue;
                        string sec = entry.Substring(0, close + 1);
                        string body = entry.Substring(close + 1).Trim();
                        if (sec != last) { sb.AppendLine(); sb.AppendLine(sec); last = sec; }
                        sb.AppendLine(body);
                    }
                }

                File.WriteAllText(Path, sb.ToString());
                Plugin.Log.LogInfo("[Meridian] Mount tweaks: wrote '" + Path + "' ("
                                   + _mounts.Count + " mount(s), " + _donors.Count + " donor(s), "
                                   + _plumes.Count + " plume(s), " + _unknown.Count
                                   + " line(s) kept verbatim). Previous file copied to '"
                                   + Path + ".bak'.");

                foreach (Live live in _live)
                {
                    if (live.Offset.IsZero) continue;
                    Plugin.Log.LogInfo("[Meridian] TWEAK " + Line(live.Key, live.Offset));
                }

                var said = new HashSet<string>(StringComparer.Ordinal);
                foreach (Fx fx in _fx)
                {
                    if (Mathf.Abs(fx.Extra) < 0.0005f) continue;
                    if (!said.Add(fx.Key)) continue;
                    Plugin.Log.LogInfo("[Meridian] TRIM " + fx.Key + " = "
                        + fx.Aft.ToString("0.###", CultureInfo.InvariantCulture)
                        + "  (seated at " + fx.BaseAft.ToString("0.###", CultureInfo.InvariantCulture)
                        + ", moved " + (-fx.Extra).ToString("0.###", CultureInfo.InvariantCulture)
                        + " m). Fold this into MotorEffects.Trims once it is settled.");
                }

                return "saved to " + Path;
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning(
                    "[Meridian] Mount tweaks: could not write '" + Path + "': " + e.Message);
                return "SAVE FAILED: " + e.Message;
            }
        }

        private static string Line(string key, Offset o)
        {
            return string.Format(CultureInfo.InvariantCulture,
                "{0} = {1:0.000} {2:0.000} {3:0.000}  {4:0.0} {5:0.0} {6:0.0}  {7:0.000}",
                key, o.Move.x, o.Move.y, o.Move.z, o.Turn.x, o.Turn.y, o.Turn.z, o.Rail);
        }

        internal static string F(float? v)
        {
            return v.HasValue ? v.Value.ToString("0.###", CultureInfo.InvariantCulture) : "-";
        }
    }
}
