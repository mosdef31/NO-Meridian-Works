using System;
using System.Reflection;
using HarmonyLib;
using Shared.Ballistics;
using UnityEngine;

namespace MeridianWorks
{
    internal static class BombRelease
    {

        private const float MaxFallSeconds = 60f;

        private const float Step = 0.1f;

        private const float EjectSpeed = 3f;

        private const float MinAimAhead = 5f;

        private const float MinAimBelow = 5f;

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

        internal static bool BallisticImpact(Missile round, Vector3 from,
                                             Vector3 velocity, out Vector3 hit)
        {
            hit = Vector3.zero;
            if (round == null) return false;

            TrajectorySolver.RoundSpec? spec = SpecFor(round);
            if (spec == null)
            {

                return false;
            }

            TrajectorySolver.Result r = TerrainImpact.Solve(
                spec, from, velocity, stepScale: 1f,
                sampleGround: SampleGround,
                sampleTerrain: true);

            if (!r.Hit) return false;
            hit = r.ImpactPoint;
            return true;
        }

        private static bool SampleGround(Vector3 at, out float groundY)
        {
            groundY = Datum.LocalSeaY;

            int mask = (int)PhysicsLayers.StaticsMask | (int)PhysicsLayers.ShipsMask;
            if (Physics.Raycast(new Vector3(at.x, at.y + 2000f, at.z), Vector3.down,
                                out RaycastHit info, 20000f, mask))
            {
                groundY = info.point.y;
                return true;
            }

            return false;
        }

        private static readonly System.Collections.Generic.Dictionary<string, TrajectorySolver.RoundSpec>
            _specs = new System.Collections.Generic.Dictionary<string, TrajectorySolver.RoundSpec>();

        private static TrajectorySolver.RoundSpec? SpecFor(Missile round)
        {
            string key = round.definition != null ? round.definition.jsonKey : round.name;
            if (_specs.TryGetValue(key, out TrajectorySolver.RoundSpec cached)) return cached;

            TrajectorySolver.RoundSpec? made =
                RoundSpecFactory.FromMissile(round, Plugin.Log);
            if (made == null) return null;

            _specs[key] = made;
            Plugin.Log.LogInfo(
                $"[Meridian] Release solution for '{key}' now comes from the shared "
                + "trajectory solver.");
            return made;
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

            if (!BallisticImpact(missile, missile.transform.position, missile.rb.velocity,
                                 out Vector3 impact))
                return "no ground found within " + MaxFallSeconds + " s; left alone";

            float ahead = Vector3.ProjectOnPlane(impact - missile.transform.position, Vector3.up).magnitude;
            float below = missile.transform.position.y - impact.y;
            if (ahead < MinAimAhead && below < MinAimBelow)
                return $"the solved impact point is only {ahead:0.#} m ahead and {below:0.#} m "
                     + "below, which is the round's own position rather than a solution; "
                     + "left alone";

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
