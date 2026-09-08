using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace MeridianWorks
{

    internal static class GravityBombTrace
    {
        private const BindingFlags Inst =
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

        private static readonly FieldInfo? FMotors = AccessTools.Field(typeof(Missile), "motors");
        private static readonly FieldInfo? FSeeker = AccessTools.Field(typeof(Missile), "seeker");
        private static readonly FieldInfo? FAimPoint = AccessTools.Field(typeof(Missile), "aimPoint");
        private static readonly FieldInfo? FKnownPos = AccessTools.Field(typeof(OpticalSeeker), "knownPos");
        private static readonly FieldInfo? FRadarAlt = AccessTools.Field(typeof(Missile), "radarAlt");

        private const float ProbeRadius = 12f;

        private static readonly Dictionary<int, Vector3> _released = new Dictionary<int, Vector3>();

        internal static bool IsGravityBomb(Missile m) =>
            m != null && FMotors?.GetValue(m) is Array a && a.Length == 0;

        internal static string KeyOf(Missile m) =>
            (m.definition as MissileDefinition)?.jsonKey ?? m.name;

        internal static void Released(Missile m)
        {
            if (!Plugin.Diagnostics || m == null || !IsGravityBomb(m)) return;

            try
            {
                Vector3 p = m.transform.position;
                _released[m.GetInstanceID()] = p;

                Plugin.Diag(
                    $"[Meridian] BOMB {KeyOf(m)} RELEASED at alt {p.y:0} m, "
                    + $"{(m.rb != null ? m.rb.velocity.magnitude : 0f):0} m/s, "
                    + $"seeker {(FSeeker?.GetValue(m)?.GetType().Name ?? "none")}.");
            }
            catch
            {

            }
        }

        internal static void Detonated(Missile m, bool hitArmor, bool hitTerrain)
        {
            if (!Plugin.Diagnostics || m == null || !IsGravityBomb(m)) return;

            try
            {
                string key = KeyOf(m);
                Vector3 p = m.transform.position;

                string miss = "(no aimpoint readable)";
                if (FAimPoint?.GetValue(m) is GlobalPosition gp)
                {
                    Vector3 aim = gp.ToLocalPosition();
                    Vector3 flat = new Vector3(aim.x - p.x, 0f, aim.z - p.z);
                    miss = $"{flat.magnitude:0} m short of its own aimpoint, which was {aim.y - p.y:0} m below it";
                }

                string solved = "";
                if (FSeeker?.GetValue(m) is OpticalSeeker os && FKnownPos?.GetValue(os) is Vector3 kp)
                {
                    Vector3 flat = new Vector3(kp.x - p.x, 0f, kp.z - p.z);
                    solved = $" The seeker's own solution was {flat.magnitude:0} m away.";
                }

                float fell = _released.TryGetValue(m.GetInstanceID(), out Vector3 start)
                    ? start.y - p.y
                    : float.NaN;
                _released.Remove(m.GetInstanceID());

                float radarAlt = FRadarAlt?.GetValue(m) as float? ?? float.NaN;

                Plugin.Diag(
                    $"[Meridian] BOMB {key} DETONATED at alt {p.y:0} m, {radarAlt:0} m above what is under it, "
                    + $"{(m.rb != null ? m.rb.velocity.magnitude : 0f):0} m/s, armed={m.IsArmed()}, "
                    + $"hitTerrain={hitTerrain} hitArmor={hitArmor}, after falling {fell:0} m. "
                    + miss + "." + solved);

                ReportNeighbour(m, key, p);
            }
            catch
            {

            }
        }

        private static void ReportNeighbour(Missile m, string key, Vector3 at)
        {
            Rigidbody? ours = m.GetComponent<Rigidbody>();

            foreach (Collider c in Physics.OverlapSphere(at, ProbeRadius))
            {
                if (c == null) continue;
                Rigidbody? other = c.attachedRigidbody;
                if (other != null && other == ours) continue;

                Plugin.Diag(
                    $"[Meridian] BOMB {key}: nearest thing at the burst was '{c.name}', "
                    + $"{Vector3.Distance(at, c.ClosestPoint(at)):0.0} m away, "
                    + $"rigidbody={(other == null ? "none" : other.name)}.");
                return;
            }

            Plugin.Diag($"[Meridian] BOMB {key}: nothing at all within {ProbeRadius:0} m of the burst.");
        }
    }

    [HarmonyPatch(typeof(Missile), "LocalStart")]
    internal static class Missile_LocalStart_GravityBombTrace
    {
        [HarmonyPostfix]
        private static void Postfix(Missile __instance) => GravityBombTrace.Released(__instance);
    }

    [HarmonyPatch(typeof(Missile), nameof(Missile.Detonate))]
    internal static class Missile_Detonate_GravityBombTrace
    {
        [HarmonyPrefix]
        private static void Prefix(Missile __instance, bool hitArmor, bool hitTerrain) =>
            GravityBombTrace.Detonated(__instance, hitArmor, hitTerrain);
    }
}
