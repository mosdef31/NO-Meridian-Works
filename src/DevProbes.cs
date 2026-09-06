using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace MeridianWorks
{

    internal static class LoadoutProbe
    {
        private static bool _ran;

        internal static void RunOnce()
        {
            if (!Plugin.Diagnostics) return;
            if (_ran) return;
            _ran = true;

            var go = new GameObject(nameof(LoadoutProbeRunner), typeof(LoadoutProbeRunner))
            {
                hideFlags = HideFlags.HideAndDontSave
            };
        }

        internal static void ReportNow()
        {
            try
            {
                Report();
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[Meridian] Loadout probe failed: {ex.Message}");
            }
        }

        internal static int Placed()
        {
            int n = 0;
            foreach (WeaponManager wm in Resources.FindObjectsOfTypeAll<WeaponManager>())
            {
                if (wm == null || wm.hardpointSets == null) continue;
                foreach (HardpointSet set in wm.hardpointSets)
                {
                    if (set?.weaponOptions == null) continue;
                    foreach (WeaponMount option in set.weaponOptions)
                        if (option != null
                            && (PluginInfo.IsOurMountKey(option.jsonKey) || IsExtra(option))) n++;
                }
            }
            return n;
        }

        private static bool IsExtra(WeaponMount mount)
        {
            foreach (WeaponMount extra in EncyclopediaRegistration.ExtraMounts)
                if (ReferenceEquals(extra, mount)) return true;
            return false;
        }

        private static void Report()
        {

            WeaponManager[] managers = Resources.FindObjectsOfTypeAll<WeaponManager>();
            if (managers.Length == 0)
            {
                Plugin.Diag("[Meridian] Loadout probe: no WeaponManager loaded yet.");
                return;
            }

            var placed = new Dictionary<string, List<string>>();

            foreach (WeaponManager wm in managers)
            {
                if (wm == null || wm.hardpointSets == null) continue;

                for (int i = 0; i < wm.hardpointSets.Length; i++)
                {
                    HardpointSet set = wm.hardpointSets[i];
                    if (set?.weaponOptions == null) continue;

                    foreach (WeaponMount option in set.weaponOptions)
                    {

                        if (option == null) continue;
                        if (!PluginInfo.IsOurMountKey(option.jsonKey) && !IsExtra(option)) continue;

                        if (!placed.TryGetValue(option.jsonKey, out List<string> where))
                            placed[option.jsonKey] = where = new List<string>();

                        where.Add($"{wm.transform.root.name}[{i}] \"{set.name}\"");
                    }
                }
            }

            var keys = new List<string>();
            foreach (PluginInfo.Weapon w in PluginInfo.Weapons)
                keys.AddRange(w.MountKeys);
            foreach (WeaponMount extra in EncyclopediaRegistration.ExtraMounts)
                if (extra != null && !string.IsNullOrEmpty(extra.jsonKey)) keys.Add(extra.jsonKey);

            {
                foreach (string key in keys)
                {
                    if (placed.TryGetValue(key, out List<string> where))
                        Plugin.Diag(
                            $"[Meridian] Loadout probe: '{key}' is an option on {where.Count} set(s) - " +
                            string.Join(", ", where.Take(12).ToArray()) +
                            (where.Count > 12 ? ", ..." : ""));
                    else if (PluginInfo.IsArchivedMountKey(key))
                        Plugin.Diag(
                            $"[Meridian] Loadout probe: '{key}' is on no hardpoint set, which is " +
                            "deliberate - it is an archived spare, kept for a future chance and placed " +
                            "on nothing on purpose.");
                    else
                        Plugin.Log.LogWarning(
                            $"[Meridian] Loadout probe: '{key}' is on NO hardpoint set of any aircraft, " +
                            "so it cannot appear in a loadout however well it registered in the " +
                            "Encyclopedia. Its manifest op resolved nothing - check the aircraft names " +
                            "and the set indices in patch_manifest.json against a fresh hardpoint scan.");
                }
            }
        }
    }

    internal sealed class LoadoutProbeRunner : MonoBehaviour
    {
        private float _next;
        private int _last = -1;
        private int _stable;
        private int _checks;

        private void Update()
        {
            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + 2f;
            _checks++;

            int placed = LoadoutProbe.Placed();
            if (placed == _last) _stable++;
            else { _stable = 0; _last = placed; }

            if ((placed > 0 && _stable >= 2) || _checks > 45)
            {
                LoadoutProbe.ReportNow();
                Destroy(gameObject);
            }
        }
    }

    internal sealed class RoundTelemetry : MonoBehaviour
    {

        private const float SampleInterval = 0.25f;
        private const int MaxSamples = 60;

        private static readonly FieldInfo? FHasLock =
            AccessTools.Field(AccessTools.TypeByName("LaserSeeker"), "hasLock");
        private static readonly FieldInfo? FSeekerAngle =
            AccessTools.Field(AccessTools.TypeByName("LaserSeeker"), "maxSeekerAngle");
        private static readonly FieldInfo? FTargetUnit =
            AccessTools.Field(typeof(MissileSeeker), "targetUnit");
        private static readonly FieldInfo? FAimPoint = AccessTools.Field(typeof(Missile), "aimPoint");
        private static readonly FieldInfo? FTarget = AccessTools.Field(typeof(Missile), "target");

        private Missile? _missile;
        private string _key = "?";
        private float _next;
        private int _samples;
        private float _born;

        internal static void Attach(Missile m, string key)
        {

            if (!Plugin.Diagnostics) return;

            var t = m.gameObject.AddComponent<RoundTelemetry>();
            t._missile = m;
            t._key = key;
        }

        private void Start()
        {
            _born = Time.time;
            _next = Time.time + SampleInterval;

            Missile? m = _missile;
            if (m == null) return;

            if (GameManager.gameState == GameState.Encyclopedia)
            {
                Destroy(this);
                return;
            }

            object? warhead = AccessTools.Field(typeof(Missile), "warhead")?.GetValue(m);
            object? armed = warhead == null
                ? null
                : AccessTools.Field(warhead.GetType(), "Armed")?.GetValue(warhead);

            object? motors = AccessTools.Field(typeof(Missile), "motors")?.GetValue(m);
            string motorText = "none";
            if (motors is Array a && a.Length > 0 && a.GetValue(0) is object motor)
            {
                float thrust = AccessTools.Field(motor.GetType(), "thrust")?.GetValue(motor) as float? ?? 0f;
                float burn = AccessTools.Field(motor.GetType(), "burnTime")?.GetValue(motor) as float? ?? 0f;
                motorText = $"{thrust:0} N for {burn:0.#}s";
            }

            bool impactFuse = AccessTools.Field(typeof(Missile), "impactFuse")?.GetValue(m) as bool? ?? false;

            Plugin.Diag(
                $"[Meridian] SHOT {_key}: LocalSim={m.LocalSim} armed={armed} impactFuse={impactFuse} " +
                $"motor={motorText} rbMass={(m.rb != null ? m.rb.mass : -1f):0} " +
                $"gravity={(m.rb != null && m.rb.useGravity)} " +
                $"collisionMode={(m.rb != null ? m.rb.collisionDetectionMode.ToString() : "?")} " +
                $"seeker={(m.GetComponent<MissileSeeker>() != null ? m.GetComponent<MissileSeeker>().GetType().Name : "NONE")} " +
                SeekerSetup(m));
        }

        private Vector3 _lastVel;
        private bool _haveLastVel;
        private int _bounces;

        private void WatchForBounce(Missile m)
        {
            Vector3 v = m.rb.velocity;

            if (_haveLastVel && _bounces < 4)
            {
                float was = _lastVel.magnitude;
                float now = v.magnitude;

                bool reversed = was > 1f && now > 0.01f
                                && Vector3.Dot(_lastVel.normalized, v.normalized) < 0f;
                bool quartered = was > 10f && now < was * 0.45f;

                if (reversed && quartered)
                {
                    _bounces++;
                    object? warhead = AccessTools.Field(typeof(Missile), "warhead")?.GetValue(m);
                    object? armed = warhead == null ? null
                        : AccessTools.Field(warhead.GetType(), "Armed")?.GetValue(warhead);
                    object? fuse = AccessTools.Field(typeof(Missile), "impactFuse")?.GetValue(m);
                    object? tang = AccessTools.Field(typeof(Missile), "tangible")?.GetValue(m);

                    Vector3 p = m.transform.position;
                    string under = Physics.Raycast(p, Vector3.down, out RaycastHit hit, 60f)
                        ? $"'{hit.collider.name}' {hit.distance:0.0} m below, "
                          + $"rb={(hit.collider.attachedRigidbody == null ? "none" : (hit.collider.attachedRigidbody.isKinematic ? "kinematic" : "dynamic"))}"
                        : "nothing within 60 m below";

                    Plugin.Log.LogWarning(
                        $"[Meridian] BOUNCE {_key} t+{Time.time - _born:0.0}s at alt {p.y:0} m: "
                        + $"{was:0} -> {now:0} m/s, direction reversed. "
                        + $"impactFuse={fuse} armed={armed} tangible={tang}. {under}.");
                }
            }

            _lastVel = v;
            _haveLastVel = true;
        }

        private void FixedUpdate()
        {
            Missile? m = _missile;
            if (m == null) { enabled = false; return; }

            WatchForBounce(m);

            if (_samples >= MaxSamples) return;
            if (Time.time < _next) return;
            _next = Time.time + SampleInterval;
            _samples++;

            Vector3 p = m.transform.position;

            float alt = p.GlobalY();

            bool ground = Physics.Raycast(p, Vector3.down, out RaycastHit hit, 3000f,
                                          PhysicsLayers.StaticsMask);

            Plugin.Diag(
                $"[Meridian] SHOT {_key} t+{Time.time - _born:0.0}s: speed {(m.rb != null ? m.rb.velocity.magnitude : 0f):0} m/s " +
                $"alt {alt:0} m  nose·vel {(m.rb != null && m.rb.velocity.sqrMagnitude > 1f ? Vector3.Dot(m.transform.forward, m.rb.velocity.normalized) : 0f):0.00} " +
                $"tangible={m.IsTangible()} " +
                (ground ? $"ground {hit.distance:0} m below ('{hit.collider.name}')" : "NO STATIC COLLIDER BELOW") +
                SeekerState(m));
        }

        private static string SeekerSetup(Missile m)
        {
            try
            {
                WeaponInfo? info = m.GetWeaponInfo();
                float declared = info != null ? info.GetMaxSpeed() : -1f;
                var seeker = m.GetComponent<MissileSeeker>();

                bool seekerHasArc = seeker != null && FSeekerAngle?.DeclaringType != null &&
                                    FSeekerAngle.DeclaringType.IsInstanceOfType(seeker);
                float arc = (seekerHasArc ? FSeekerAngle!.GetValue(seeker) as float? : null) ?? -1f;

                var owner = m.owner as Aircraft;
                LaserDesignator? designator = owner != null ? owner.GetLaserDesignator() : null;

                return $"maxSpeed={declared:0} seekerArc={arc:0.#} " +
                       $"owner={(owner != null ? owner.unitName : "none")} " +
                       $"designator={(designator != null ? designator.LasedTargetCount() + " lased" : "NONE")} " +
                       $"hq={(m.NetworkHQ != null)} targetID={m.targetID.IsValid}";
            }
            catch
            {
                return "maxSpeed=? seekerArc=?";
            }
        }

        private static string SeekerState(Missile m)
        {
            try
            {
                var seeker = m.GetComponent<MissileSeeker>();
                if (seeker == null) return "  seeker=NONE";

                bool onLaser = FHasLock != null &&
                               FHasLock.DeclaringType != null &&
                               FHasLock.DeclaringType.IsInstanceOfType(seeker);

                bool? locked = onLaser ? FHasLock!.GetValue(seeker) as bool? : null;
                var unit = FTargetUnit?.GetValue(seeker) as Unit;

                string lased = "n/a";
                string arc = "n/a";
                if (unit != null)
                {
                    FactionHQ? hq = m.NetworkHQ;
                    lased = hq != null ? hq.IsTargetLased(unit).ToString() : "noHQ";
                    arc = Vector3.Angle(unit.transform.position - m.transform.position,
                                        m.transform.forward).ToString("0.#");
                }

                string aimErr = "n/a";
                string gate = "n/a";
                if (FAimPoint?.GetValue(m) is GlobalPosition ap)
                {
                    Vector3 aim = ap.ToLocalPosition();
                    if (unit != null) aimErr = Vector3.Distance(aim, unit.transform.position).ToString("0");

                    var hard = FTarget?.GetValue(m) as Unit;
                    Vector3 b2 = (hard != null && hard.maxRadius < 20f) ? hard.transform.position : aim;
                    float speed = m.rb != null ? m.rb.velocity.magnitude : 0f;
                    gate = $"{Vector3.Distance(m.transform.position, b2):0}/{speed * 0.25f:0}";
                }

                return $"  lock={locked?.ToString() ?? "?"} target={(unit != null ? unit.unitName : "none")} " +
                       $"lased={lased} arc={arc} aimErr={aimErr} m gate={gate}";
            }
            catch (Exception ex)
            {
                return $"  seekerState failed: {ex.Message}";
            }
        }
    }

    [HarmonyPatch(typeof(Missile), "OnStartClient")]
    internal static class Missile_OnStartClient_TelemetryPatch
    {
        [HarmonyPostfix]
        private static void Postfix(Missile __instance)
        {
            if (!Plugin.Diagnostics) return;

            try
            {
                var def = __instance.definition as MissileDefinition;
                if (def == null || !PluginInfo.IsOurMissileKey(def.jsonKey)) return;
                RoundTelemetry.Attach(__instance, def.jsonKey);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[Meridian] Telemetry attach failed: {ex.Message}");
            }
        }
    }

    [HarmonyPatch(typeof(Missile), nameof(Missile.Detonate))]
    internal static class Missile_Detonate_TelemetryPatch
    {
        [HarmonyPrefix]
        private static void Prefix(Missile __instance, bool hitArmor, bool hitTerrain)
        {
            if (!Plugin.Diagnostics) return;

            try
            {
                var def = __instance.definition as MissileDefinition;
                if (def == null || !PluginInfo.IsOurMissileKey(def.jsonKey)) return;

                Vector3 p = __instance.transform.position;
                Plugin.Diag(
                    $"[Meridian] SHOT {def.jsonKey} DETONATED at alt {p.y:0} m, " +
                    $"hitTerrain={hitTerrain} hitArmor={hitArmor}.");

                ImpactFacts.Report(__instance, def.jsonKey, p);
            }
            catch
            {

            }
        }
    }
}
