using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace MeridianWorks
{

    [HarmonyPatch(typeof(SubmunitionDispenser), nameof(SubmunitionDispenser.AssignSubmunitionTargets))]
    internal static class HSM160Dispenser
    {
        private const string StockSubmunition = "Submunition1_info";
        private const float ReleaseIntervalS = 0.05f;
        private const int LoggedRounds = 32;

        private static readonly FieldInfo? FType = AccessTools.Field(typeof(SubmunitionDispenser), "submunitionType");
        private static readonly FieldInfo? FMissile = AccessTools.Field(typeof(SubmunitionDispenser), "missile");
        private static readonly FieldInfo? FSubs = AccessTools.Field(typeof(SubmunitionDispenser), "submunitions");
        private static readonly FieldInfo? FEjectSpeed = AccessTools.Field(typeof(SubmunitionDispenser), "ejectSpeed");
        private static readonly FieldInfo? FCasings = AccessTools.Field(typeof(SubmunitionDispenser), "casings");
        private const float PetalGhostS = 1.0f;

        private static WeaponInfo? _stock;
        private static bool _said;
        private static int _logged;

        [HarmonyPrefix]
        private static bool Prefix(SubmunitionDispenser __instance)
        {
            try
            {
                if (FMissile?.GetValue(__instance) is not Missile m) return true;
                if ((m.definition as MissileDefinition)?.jsonKey is not string key || !HSM160Rounds.Ours.Contains(key))
                    return true;

                if (FType != null && FType.GetValue(__instance) == null && !FillType(__instance, key)) return true;
                if (FType?.GetValue(__instance) is not WeaponInfo type || FSubs?.GetValue(__instance) is not GameObject[] subs)
                    return true;

                float eject = FEjectSpeed?.GetValue(__instance) is float f ? f : 0f;
                __instance.StartCoroutine(GhostPetals(__instance));
                __instance.StartCoroutine(Release(m, key, type, subs, eject));
                return false;
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("[Meridian] HSM-160 dispenser failed, stock release kept: " + e.Message);
                return true;
            }
        }

        private static bool FillType(SubmunitionDispenser d, string key)
        {
            if (_stock == null)
            {
                foreach (WeaponInfo info in Resources.FindObjectsOfTypeAll<WeaponInfo>())
                {
                    if (info.name == StockSubmunition) { _stock = info; break; }
                }
            }

            if (_stock == null)
            {
                Plugin.Log.LogWarning($"[Meridian] {key}: dispenser has no submunition type and stock "
                    + $"'{StockSubmunition}' was not found, nothing will be released.");
                return false;
            }

            FType!.SetValue(d, _stock);
            if (!_said)
            {
                _said = true;
                Plugin.Log.LogInfo($"[Meridian] {key}: dispenser submunition type filled from stock '{_stock.name}'.");
            }
            return true;
        }

        private static System.Collections.IEnumerator GhostPetals(SubmunitionDispenser d)
        {
            var off = new System.Collections.Generic.List<Collider>();
            if (FCasings?.GetValue(d) is System.Collections.IEnumerable casings)
            {
                foreach (object o in casings)
                {
                    GameObject? go = o as GameObject ?? (o as Component)?.gameObject;
                    if (go == null) continue;
                    foreach (Collider c in go.GetComponentsInChildren<Collider>(true))
                        if (c.enabled) { c.enabled = false; off.Add(c); }
                }
            }
            yield return new WaitForSeconds(PetalGhostS);
            foreach (Collider c in off)
                if (c != null) c.enabled = true;
        }

        private static System.Collections.IEnumerator Release(Missile m, string key, WeaponInfo type, GameObject[] subs, float eject)
        {
            var targets = new System.Collections.Generic.List<Unit>();
            if (m.targetID.TryGetUnit(out Unit aimed) && aimed != null && !aimed.disabled)
                targets.Add(aimed);

            if (aimed != null)
            {
                var near = new System.Collections.Generic.List<Unit>();
                BattlefieldGrid.GetUnitsInRangeNonAlloc(aimed.GlobalPosition(), HSM160DispenseEarly.DetectM, near);
                foreach (Unit u in near)
                {
                    if (u == null || u == aimed || u.disabled || u.NetworkHQ == m.NetworkHQ || u is Scenery || u is Missile
                        || u.speed > 60f || FastMath.OutOfRange(u.GlobalPosition(), aimed.GlobalPosition(), HSM160DispenseEarly.DetectM))
                        continue;
                    targets.Add(u);
                }
            }

            int released = 0;
            string why = "all out";
            if (targets.Count == 0) why = "no target left";
            for (int i = 0; i < subs.Length && targets.Count > 0; i++)
            {
                yield return new WaitForSeconds(ReleaseIntervalS);
                if (m == null || m.disabled) { why = "round lost first"; break; }

                Unit target = targets[i % targets.Count];
                if (m.IsServer && subs[i] != null)
                {
                    Vector3 side = Vector3.Dot(subs[i].transform.position - m.transform.position, m.transform.right) > 0f
                        ? m.transform.right : -m.transform.right;
                    NetworkSceneSingleton<Spawner>.i.SpawnMissile(type.weaponPrefab, subs[i].transform.position,
                        m.transform.rotation, m.rb.velocity + side * eject, target, m);
                }
                if (subs[i] != null) subs[i].SetActive(false);
                released++;
            }

            if (_logged < LoggedRounds)
            {
                _logged++;
                string who = m != null ? (m.GetWeaponInfo()?.weaponName ?? key) : key;
                Plugin.Log.LogInfo($"[Meridian] {who}: {released} of {subs.Length} darts out, {targets.Count} target(s), {why}.");
            }

            yield return new WaitForSeconds(1f);
            if (m != null && !m.disabled) HSM160Spent.Begin(m, key);
        }
    }

    [HarmonyPatch(typeof(SubmunitionDispenser), "TargetApproachCheck")]
    internal static class HSM160DispenseEarly
    {
        internal const float DispenseM = 30000f;
        internal const float DetectM = 3000f;

        internal const float HeadingErrCapDeg = 100f;

        internal const float DispenseMach = 5.0f;
        internal const float ShortRangeM = 8000f;
        internal const float FadeMs = 15f;

        internal const float GroundAheadS = 4f;

        private sealed class Peak { internal float speed; }
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Missile, Peak> Peaks = new();

        private static readonly FieldInfo? FMissile = AccessTools.Field(typeof(SubmunitionDispenser), "missile");
        private static readonly FieldInfo? FDispensed = AccessTools.Field(typeof(SubmunitionDispenser), "dispensed");
        private static readonly FieldInfo? FDmgId = AccessTools.Field(typeof(SubmunitionDispenser), "dmgID");
        private static readonly FieldInfo? FDetect = AccessTools.Field(typeof(SubmunitionDispenser), "detectionRange");
        private static readonly FieldInfo? FDistance = AccessTools.Field(typeof(SubmunitionDispenser), "dispenseDistance");
        private const int SaidRounds = 32;
        private static int _said;

        [HarmonyPrefix]
        private static bool Prefix(SubmunitionDispenser __instance)
        {
            try
            {
                if (FMissile?.GetValue(__instance) is not Missile m) return true;
                if ((m.definition as MissileDefinition)?.jsonKey is not string key || !HSM160Rounds.Ours.Contains(key))
                    return true;
                if (FDispensed == null || FDmgId == null) return true;

                FDetect?.SetValue(__instance, DetectM);
                FDistance?.SetValue(__instance, DispenseM);

                if ((bool)FDispensed.GetValue(__instance) || m.NetworkHQ == null) return false;

                if (m.rb != null && !m.boosterIsAttached && m.rb.velocity.y < 0f
                    && m.targetID.TryGetUnit(out Unit groundUnit) && groundUnit != null)
                {
                    Vector3 gp = m.transform.position;
                    Vector3 gvel = m.rb.velocity;
                    bool seaAhead = gp.y + gvel.y * GroundAheadS < Datum.LocalSeaY;

                    float ceiling = -gvel.y * GroundAheadS + 3000f;
                    bool terrainAhead = !seaAhead && gp.y - Datum.LocalSeaY <= ceiling
                        && Physics.Linecast(gp, gp + gvel * GroundAheadS, out _, PhysicsLayers.StaticsMask.value);
                    if (seaAhead || terrainAhead)
                    {
                        Open(m, key, groundUnit, (byte)FDmgId.GetValue(__instance), "ground ahead");
                        return false;
                    }
                }

                bool diving = HSM160TrajectoryStateTable.States.TryGetValue(m, out HSM160TrajectoryState st)
                    ? st.pastApex
                    : m.rb != null && m.rb.velocity.y < 0f && !m.boosterIsAttached;
                if (!diving) return false;
                if (!m.targetID.TryGetUnit(out Unit unit)) return false;

                bool fix = m.NetworkHQ.IsTargetPositionAccurate(unit, DetectM) || HSM160Seeker.HasLock(m);
                if (!fix || !FastMath.InRange(unit.GlobalPosition(), m.GlobalPosition(), DispenseM)
                    || !unit.LineOfSight(m.transform.position, 1000f))
                    return false;

                Vector3 h = unit.GlobalPosition() - m.GlobalPosition();
                h.y = 0f;
                Vector3 hn = h.sqrMagnitude > 0.0001f ? h.normalized : m.transform.forward;
                Vector3 vel = m.rb != null ? m.rb.velocity : m.transform.forward;
                if (HSM160Trajectory.HeadingErrDeg(vel, hn) > HeadingErrCapDeg) return false;

                float rangeM = FastMath.Distance(unit.GlobalPosition(), m.GlobalPosition());
                float speed = vel.magnitude;
                float mach = speed / Mathf.Max(1f, LevelInfo.GetSpeedOfSound(m.GlobalPosition().y));
                Peak peak = Peaks.GetOrCreateValue(m);
                bool fading = speed < peak.speed - FadeMs;
                peak.speed = Mathf.Max(peak.speed, speed);
                if (mach < DispenseMach && rangeM > ShortRangeM && !fading) return false;

                string gate = mach >= DispenseMach ? "speed" : rangeM <= ShortRangeM ? "short range" : "slowing";
                Open(m, key, unit, (byte)FDmgId.GetValue(__instance), gate);
                return false;
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("[Meridian] HSM-160 early dispense check failed, stock check kept: " + e.Message);
                return true;
            }
        }

        private static void Open(Missile m, string key, Unit unit, byte dmgId, string gate)
        {
            m.NetworkHQ.RpcUpdateTrackingInfo(m.targetID);
            m.Damage(dmgId, new DamageInfo(0f, 0f, 0f, 1f));
            if (_said < SaidRounds)
            {
                _said++;
                string who = m.GetWeaponInfo()?.weaponName ?? key;
                float rangeM = FastMath.Distance(unit.GlobalPosition(), m.GlobalPosition());
                float alt = m.GlobalPosition().y - unit.GlobalPosition().y;
                Vector3 vel = m.rb != null ? m.rb.velocity : m.speed * m.transform.forward;
                float mach = vel.magnitude / Mathf.Max(1f, LevelInfo.GetSpeedOfSound(m.GlobalPosition().y));
                Plugin.Log.LogInfo($"[Meridian] {who} dispensing at {rangeM / 1000f:F1} km, {alt / 1000f:F1} km up, Mach {mach:F1} ({gate}).");
            }
        }
    }
}
