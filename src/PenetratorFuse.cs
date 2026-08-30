using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace MeridianWorks
{
    internal sealed class PenetratorFuse : MonoBehaviour
    {

        private const float ContactSpeed = 20f;

        private const float FlyingSpeed = 60f;

        private const float ContactHold = 0.5f;

        private const float Grace = 0.35f;

        private static readonly FieldInfo? FImpactFuse =
            AccessTools.Field(typeof(Missile), "impactFuse");

        private static readonly FieldInfo? FImpactFuseDelay =
            AccessTools.Field(typeof(Missile), "impactFuseDelay");

        private Missile? _missile;
        private float _fuseDelay;

        private bool _wasArmed;
        private bool _wasFlying;
        private bool _triggered;
        private float _slowSince = -1f;
        private float _fireAt;

        internal static void Attach(Missile missile, string? jsonKey)
        {
            if (missile == null || !PluginInfo.IsOurMissileKey(jsonKey)) return;
            if (missile.GetComponent<PenetratorFuse>() != null) return;

            var f = missile.gameObject.AddComponent<PenetratorFuse>();
            f._missile = missile;
            f._fuseDelay = FImpactFuseDelay?.GetValue(missile) as float? ?? 0f;
        }

        private void FixedUpdate()
        {
            Missile m = _missile!;
            if (m == null) { enabled = false; return; }

            if (m.disabled) { enabled = false; return; }

            if (m.IsArmed()) _wasArmed = true;

            float speed = m.rb != null ? m.rb.velocity.magnitude : 0f;
            if (speed > FlyingSpeed) _wasFlying = true;

            if (_triggered)
            {

                if (m.rb != null && !m.rb.isKinematic) m.rb.velocity = Vector3.zero;

                if (Time.time >= _fireAt) Fire(m);
                return;
            }

            bool penetrating = FImpactFuse?.GetValue(m) is bool fuse && !fuse;

            bool stranded = false;
            if (_wasFlying && speed < ContactSpeed)
            {
                if (_slowSince < 0f) _slowSince = Time.time;
                stranded = Time.time - _slowSince >= ContactHold;
            }
            else
            {
                _slowSince = -1f;
            }

            bool skipped = _wasArmed && m.IsArmed() && EngineSkippedAHit(m);

            if (!penetrating && !stranded && !skipped) return;

            _triggered = true;

            _fireAt = skipped ? Time.time : Time.time + _fuseDelay + Grace;
        }

        private static bool EngineSkippedAHit(Missile m)
        {
            if (m.rb == null) return false;

            Vector3 v = m.rb.velocity;
            if (v.sqrMagnitude < 0.01f) return false;

            int mask = m.IsTangible()
                ? ~PhysicsLayers.ExclusionZonesMask.value
                : PhysicsLayers.StaticsMask.value;

            Vector3 from = m.transform.position;
            Vector3 to = from + 1.1f * Time.fixedDeltaTime * v;

            if (!Physics.Linecast(from, to, out RaycastHit hit, mask)) return false;

            Rigidbody? other = hit.collider.attachedRigidbody;
            if (other == null || other.isKinematic) return false;

            return FastMath.InRange(other.velocity, v, 100f);
        }

        private void Fire(Missile m)
        {
            enabled = false;

            if (m == null || m.disabled) return;

            if (_wasArmed && !m.IsArmed()) m.Arm();

            Plugin.Diag(
                $"[Meridian] FUSE {name}: the engine did not detonate this round after "
                + $"contact, so the backstop did. armedInFlight={_wasArmed} "
                + $"fuseDelay={_fuseDelay:0.##}s speed={(m.rb != null ? m.rb.velocity.magnitude : 0f):0.#} m/s.");

            m.Detonate(Vector3.up, hitArmor: true, hitTerrain: false);
        }
    }

    [HarmonyPatch(typeof(OpticalSeeker), "SlowChecks")]
    internal static class OpticalSeeker_SlowChecks_PenetrationGuardPatch
    {
        private static readonly FieldInfo? FMissile =
            AccessTools.Field(typeof(MissileSeeker), "missile");

        private static readonly FieldInfo? FImpactFuse =
            AccessTools.Field(typeof(Missile), "impactFuse");

        [HarmonyPrefix]
        private static bool Prefix(OpticalSeeker __instance)
        {
            try
            {
                if (FMissile?.GetValue(__instance) is not Missile m || m == null) return true;
                if (m.definition is not MissileDefinition def) return true;
                if (!PluginInfo.IsOurMissileKey(def.jsonKey)) return true;

                if (FImpactFuse?.GetValue(m) is bool fuse && !fuse) return false;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[Meridian] Penetration guard failed: {ex.Message}");
            }

            return true;
        }
    }

    [HarmonyPatch(typeof(Missile), "OnStartClient")]
    internal static class Missile_OnStartClient_PenetratorFusePatch
    {
        [HarmonyPostfix]
        private static void Postfix(Missile __instance)
        {
            try
            {
                if (__instance.definition is not MissileDefinition def) return;
                PenetratorFuse.Attach(__instance, def.jsonKey);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[Meridian] Penetrator fuse attach failed: {ex.Message}");
            }
        }
    }
}
