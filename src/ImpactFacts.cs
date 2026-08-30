using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace MeridianWorks
{

    internal static class ImpactFacts
    {
        private const BindingFlags Inst =
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

        private static readonly FieldInfo? F_blastYield = typeof(Missile).GetField("blastYield", Inst);
        private static readonly FieldInfo? F_pierce = typeof(Missile).GetField("pierceDamage", Inst);
        private static readonly FieldInfo? F_warhead = typeof(Missile).GetField("warhead", Inst);
        private static readonly FieldInfo? F_impactFuse = typeof(Missile).GetField("impactFuse", Inst);
        private static readonly FieldInfo? F_fuseDelay = typeof(Missile).GetField("impactFuseDelay", Inst);

        private const float ShockwaveThreshold = 200f;

        private const float ProbeRadius = 12f;

        internal static void Report(Missile missile, string key, Vector3 at)
        {
            if (!Plugin.Diagnostics || missile == null) return;

            try
            {
                float yield = F_blastYield?.GetValue(missile) as float? ?? 0f;
                float pierce = F_pierce?.GetValue(missile) as float? ?? 0f;
                bool impactFuse = F_impactFuse?.GetValue(missile) as bool? ?? false;
                float fuseDelay = F_fuseDelay?.GetValue(missile) as float? ?? 0f;

                bool overThreshold = yield > ShockwaveThreshold;
                string path = overThreshold
                    ? "SHOCKWAVE (ExplosionForceOnPhysicsFrame is SKIPPED)"
                    : "explosion force";

                Plugin.Diag(
                    $"[Meridian] IMPACT {key}: blastYield {yield:0.#}, pierce {pierce:0.#}, " +
                    $"impactFuse {impactFuse}, impactFuseDelay {fuseDelay:0.###}. " +
                    $"Damage path is {path}, because the threshold is {ShockwaveThreshold:0}.");

                if (overThreshold) ReportShockwaveChild(missile, key);
                ReportWhatWasHit(missile, key, at);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[Meridian] Impact facts failed on {key}: {ex.Message}");
            }
        }

        private static void ReportShockwaveChild(Missile missile, string key)
        {
            object? warhead = F_warhead?.GetValue(missile);
            if (warhead == null) return;

            foreach (string field in new[] { "armorEffect", "terrainEffect", "airEffect" })
            {
                var prefab = warhead.GetType().GetField(field, Inst)?.GetValue(warhead) as GameObject;
                if (prefab == null)
                {
                    Plugin.Log.LogWarning($"[Meridian] IMPACT {key}: {field} is NULL.");
                    continue;
                }

                bool hasShockwave = prefab.GetComponentInChildren<Shockwave>(true) != null;
                string verdict = hasShockwave
                    ? "carries a Shockwave"
                    : "HAS NO SHOCKWAVE CHILD, so above the threshold this effect does no blast damage at all";

                Plugin.Diag($"[Meridian] IMPACT {key}: {field} '{prefab.name}' {verdict}.");
            }
        }

        private static void ReportWhatWasHit(Missile missile, string key, Vector3 at)
        {
            Rigidbody? rb = missile.GetComponent<Rigidbody>();
            Vector3 ours = rb == null ? Vector3.zero : rb.velocity;

            Collider[] near = Physics.OverlapSphere(at, ProbeRadius);
            Rigidbody? theirs = null;
            string what = "(nothing with a Rigidbody within " + ProbeRadius + " m)";

            foreach (Collider c in near)
            {
                Rigidbody? other = c.attachedRigidbody;
                if (other == null || other == rb) continue;
                theirs = other;
                what = other.name;
                break;
            }

            if (theirs == null)
            {
                Plugin.Diag(
                    $"[Meridian] IMPACT {key}: round speed {ours.magnitude:0.0} m/s. {what}.");
                return;
            }

            float closing = (ours - theirs.velocity).magnitude;
            string gate = closing < 100f
                ? "UNDER 100, which is the band where the engine SKIPS the contact detonation and the " +
                  "round is snapped back onto the surface to try again next physics step"
                : "over 100, so the contact detonation was not skipped on this hit";

            Plugin.Diag(
                $"[Meridian] IMPACT {key}: round speed {ours.magnitude:0.0} m/s against " +
                $"'{what}' at {theirs.velocity.magnitude:0.0} m/s, kinematic={theirs.isKinematic}. " +
                $"Closing speed {closing:0.0} m/s - {gate}.");
        }
    }
}
