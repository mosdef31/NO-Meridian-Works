using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace MeridianWorks
{
    internal static class IgnitionDelay
    {

        private const float ReferenceAngle = 45f;

        private const float MinScale = 0.25f;
        private const float MaxScale = 2.0f;

        private static readonly FieldInfo? FMotors =
            AccessTools.Field(typeof(Missile), "motors");

        private static readonly FieldInfo? FTarget =
            AccessTools.Field(typeof(Missile), "target");

        private static FieldInfo? _fDelay;
        private static bool _delayLookedUp;

        private static FieldInfo? DelayField(object motor)
        {
            if (_delayLookedUp) return _fDelay;
            _delayLookedUp = true;
            _fDelay = AccessTools.Field(motor.GetType(), "delayTimer");
            if (_fDelay == null)
                Plugin.Log.LogWarning("[Meridian] Motor.delayTimer has moved; the "
                                      + "off-boresight ignition hold is inactive.");
            return _fDelay;
        }

        internal static float ShotAngle(Missile missile)
        {
            Vector3 to;

            if (FTarget?.GetValue(missile) is Unit t && t != null)
                to = t.transform.position - missile.transform.position;
            else
                return 0f;

            if (to.sqrMagnitude < 1f) return 0f;
            return Vector3.Angle(missile.transform.forward, to);
        }

        internal static string? Apply(Missile missile)
        {
            if (FMotors?.GetValue(missile) is not Array motors || motors.Length == 0)
                return null;

            object? first = motors.GetValue(0);
            if (first == null) return null;

            FieldInfo? f = DelayField(first);
            if (f == null) return null;

            if (f.GetValue(first) is not float authored || authored <= 0f) return null;

            float angle = ShotAngle(missile);
            float scale = Mathf.Clamp(angle / ReferenceAngle, MinScale, MaxScale);
            float held = authored * scale;

            f.SetValue(first, held);

            return $"shot {angle:0.#} deg off the nose, hold {authored:0.00}s x{scale:0.00} "
                   + $"= {held:0.00}s";
        }
    }

    [HarmonyPatch(typeof(Missile), "LocalStart")]
    internal static class Missile_LocalStart_IgnitionDelayPatch
    {
        [HarmonyPostfix]
        private static void Postfix(Missile __instance)
        {
            try
            {
                if (__instance.definition is not MissileDefinition def) return;
                if (!PluginInfo.IsOurMissileKey(def.jsonKey)) return;

                string? what = IgnitionDelay.Apply(__instance);
                if (what != null) Plugin.Diag($"[Meridian] HOLD {def.jsonKey}: {what}.");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[Meridian] Ignition hold failed: {ex.Message}");
            }
        }
    }
}
