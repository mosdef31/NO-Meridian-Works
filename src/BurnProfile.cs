using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace MeridianWorks
{
    internal static class BurnProfile
    {

        private const string Key = "MeridianAAM41";

        private const float CloseRangeMetres = 25000f;

        private const float CloseAngleDeg = 30f;

        private const float ReachBurnMultiple = 2.5f;

        private static readonly FieldInfo? FMotors =
            AccessTools.Field(typeof(Missile), "motors");

        private static readonly FieldInfo? FTarget =
            AccessTools.Field(typeof(Missile), "target");

        private static FieldInfo? _fThrust;
        private static FieldInfo? _fBurnTime;
        private static bool _lookedUp;

        private static bool Fields(object motor)
        {
            if (!_lookedUp)
            {
                _lookedUp = true;
                _fThrust = AccessTools.Field(motor.GetType(), "thrust");
                _fBurnTime = AccessTools.Field(motor.GetType(), "burnTime");
                if (_fThrust == null || _fBurnTime == null)
                    Plugin.Log.LogWarning(
                        "[Meridian] Motor.thrust or Motor.burnTime has moved, so the AAM-41 "
                        + "flies its authored booster on every shot.");
            }
            return _fThrust != null && _fBurnTime != null;
        }

        internal static string? Apply(Missile missile)
        {
            if (FMotors?.GetValue(missile) is not Array motors || motors.Length == 0)
                return null;

            object? boost = motors.GetValue(0);
            if (boost == null || !Fields(boost)) return null;

            if (_fThrust!.GetValue(boost) is not float thrust || thrust <= 0f) return null;
            if (_fBurnTime!.GetValue(boost) is not float burn || burn <= 0f) return null;

            float range = float.PositiveInfinity;
            float angle = 180f;

            if (FTarget?.GetValue(missile) is Unit t && t != null)
            {
                Vector3 to = t.transform.position - missile.transform.position;
                range = to.magnitude;
                angle = to.sqrMagnitude < 1f
                    ? 0f
                    : Vector3.Angle(missile.transform.forward, to);
            }

            bool close = range < CloseRangeMetres && angle <= CloseAngleDeg;

            if (close)
            {

                return $"target at {range / 1000f:0.0} km, {angle:0.#} deg off the nose, "
                       + $"so the AGGRESSIVE booster is kept as authored: "
                       + $"{thrust:0} N for {burn:0.0} s.";
            }

            float reachBurn = burn * ReachBurnMultiple;
            float reachThrust = thrust / ReachBurnMultiple;

            _fThrust.SetValue(boost, reachThrust);
            _fBurnTime.SetValue(boost, reachBurn);

            string why = range >= CloseRangeMetres
                ? $"target at {range / 1000f:0.0} km, beyond the {CloseRangeMetres / 1000f:0.#} km close band"
                : $"target {angle:0.#} deg off the nose, wider than the {CloseAngleDeg:0.#} deg it can lead at speed";

            return $"{why}, so the EFFICIENT booster: {thrust:0} N for {burn:0.0} s became "
                   + $"{reachThrust:0} N for {reachBurn:0.0} s. Same fuel, same impulse, so the "
                   + "delta-V and the sustainer cap are unchanged.";
        }

        internal static bool Owns(string? jsonKey)
        {
            string k = jsonKey?.Trim() ?? "";
            if (k.Length == 0) return false;
            if (string.Equals(k, Key, StringComparison.OrdinalIgnoreCase)) return true;
            return k.StartsWith(Key + "_", StringComparison.OrdinalIgnoreCase);
        }
    }

    [HarmonyPatch(typeof(Missile), "LocalStart")]
    internal static class Missile_LocalStart_BurnProfilePatch
    {
        [HarmonyPostfix]
        private static void Postfix(Missile __instance)
        {
            try
            {
                if (__instance.definition is not MissileDefinition def) return;
                if (!BurnProfile.Owns(def.jsonKey)) return;

                string? what = BurnProfile.Apply(__instance);
                if (what != null) Plugin.Diag($"[Meridian] BURN {def.jsonKey}: {what}");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[Meridian] Burn profile selection failed: {ex.Message}");
            }
        }
    }
}
