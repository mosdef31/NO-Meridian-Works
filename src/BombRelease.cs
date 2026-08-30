using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace MeridianWorks
{
    internal static class BombRelease
    {

        private const float MaxFallSeconds = 60f;

        private const float Step = 0.1f;

        private const float EjectSpeed = 3f;

        private static readonly FieldInfo? FKnownPos =
            AccessTools.Field(typeof(OpticalSeeker), "knownPos");

        private static readonly FieldInfo? FKnownVel =
            AccessTools.Field(typeof(OpticalSeeker), "knownVel");

        private static readonly FieldInfo? FTargetUnit =
            AccessTools.Field(typeof(MissileSeeker), "targetUnit");

        private static readonly FieldInfo? FMotors =
            AccessTools.Field(typeof(Missile), "motors");

        private static readonly FieldInfo? FMissile =
            AccessTools.Field(typeof(MissileSeeker), "missile");

        internal static Missile? MissileOf(MissileSeeker seeker) =>
            FMissile?.GetValue(seeker) as Missile;

        internal static bool IsOurBomb(Missile missile)
        {
            if (missile == null) return false;
            if (missile.definition is not MissileDefinition def) return false;
            if (!PluginInfo.IsOurMissileKey(def.jsonKey)) return false;
            return FMotors?.GetValue(missile) is Array m && m.Length == 0;
        }

        internal static bool BallisticImpact(Vector3 from, Vector3 velocity, out Vector3 hit)
        {
            hit = Vector3.zero;

            Vector3 p = from;
            Vector3 v = velocity;
            int mask = ~PhysicsLayers.ExclusionZonesMask.value;

            for (float t = 0f; t < MaxFallSeconds; t += Step)
            {
                v += Physics.gravity * Step;
                Vector3 next = p + v * Step;

                if (Physics.Linecast(p, next, out RaycastHit info, mask))
                {
                    hit = info.point;
                    return true;
                }

                if (next.y < Datum.LocalSeaY)
                {
                    hit = new Vector3(next.x, Datum.LocalSeaY, next.z);
                    return true;
                }

                p = next;
            }

            return false;
        }

        internal static void Eject(Missile missile)
        {
            if (missile == null || missile.rb == null || missile.rb.isKinematic) return;
            missile.rb.velocity += -missile.transform.up * EjectSpeed;
        }

        internal static string Reaim(Missile missile, OpticalSeeker seeker)
        {
            if (FKnownPos == null || FKnownVel == null)
                return "the seeker's knownPos/knownVel fields have moved; left alone";

            if (FTargetUnit?.GetValue(seeker) is Unit u && u != null)
                return "has a target; left alone";

            if (!BallisticImpact(missile.transform.position, missile.rb.velocity, out Vector3 impact))
                return "no ground found within " + MaxFallSeconds + " s; left alone";

            FKnownPos.SetValue(seeker, impact.ToGlobalPosition());
            FKnownVel.SetValue(seeker, Vector3.zero);
            missile.SetAimpoint(impact.ToGlobalPosition(), Vector3.zero);

            float fall = missile.transform.position.y - impact.y;
            float range = Vector3.ProjectOnPlane(impact - missile.transform.position, Vector3.up).magnitude;
            return $"aimed at its own impact point, {range:0} m ahead and {fall:0} m below";
        }
    }

    [HarmonyPatch(typeof(OpticalSeeker), nameof(OpticalSeeker.Initialize))]
    internal static class OpticalSeeker_Initialize_BombAimpointPatch
    {
        [HarmonyPostfix]
        private static void Postfix(OpticalSeeker __instance)
        {
            try
            {
                Missile? missile = BombRelease.MissileOf(__instance);
                if (missile == null || !BombRelease.IsOurBomb(missile)) return;

                string what = BombRelease.Reaim(missile, __instance);

                if (missile.definition is MissileDefinition def)
                    Plugin.Diag($"[Meridian] BOMB {def.jsonKey}: {what}.");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[Meridian] Bomb aimpoint failed: {ex.Message}");
            }
        }
    }

    [HarmonyPatch(typeof(Missile), "LocalStart")]
    internal static class Missile_LocalStart_EjectPatch
    {
        [HarmonyPostfix]
        private static void Postfix(Missile __instance)
        {
            try
            {
                if (!BombRelease.IsOurBomb(__instance)) return;
                BombRelease.Eject(__instance);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[Meridian] Bomb ejection failed: {ex.Message}");
            }
        }
    }
}
