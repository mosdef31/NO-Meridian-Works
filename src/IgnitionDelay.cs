

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

        private static readonly System.Collections.Generic.Dictionary<string, float>
            SeparationHold = new()
        {
            { "MeridianExocetAir_Missile", 0.6f },
            { "MeridianYashma_Missile", 0.9f },
        };

        private static readonly System.Collections.Generic.Dictionary<string, float>
            EjectSpeed = new()
        {
            { "MeridianExocetAir_Missile", 2.0f },
            { "MeridianYashma_Missile", 2.5f },
        };

        internal static string? Eject(Missile missile, string? key)
        {
            if (key == null || !EjectSpeed.TryGetValue(key, out float speed)) return null;
            if (missile == null || missile.rb == null || missile.rb.isKinematic) return null;

            missile.rb.velocity += -missile.transform.up * speed;
            return $"ejected downward at {speed:0.0} m/s";
        }

        private static float HoldFloor(string? key)
        {
            if (key != null && SeparationHold.TryGetValue(key, out float s)) return s;
            return 0f;
        }

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

        internal static string? Apply(Missile missile, string? key)
        {
            if (FMotors?.GetValue(missile) is not Array motors || motors.Length == 0)
                return null;

            object? first = motors.GetValue(0);
            if (first == null) return null;

            FieldInfo? f = DelayField(first);
            if (f == null) return null;

            if (f.GetValue(first) is not float authored) return null;

            float floor = HoldFloor(key);

            if (authored <= 0f && floor <= 0f) return null;

            float angle = ShotAngle(missile);
            float scale = Mathf.Clamp(angle / ReferenceAngle, MinScale, MaxScale);
            float held = authored * scale;

            bool floored = floor > held;
            if (floored) held = floor;

            f.SetValue(first, held);

            if (authored <= 0f)
                return $"no authored hold, separation floor {floor:0.00}s applied";

            return $"shot {angle:0.#} deg off the nose, hold {authored:0.00}s x{scale:0.00} "
                   + $"= {held:0.00}s"
                   + (floored ? $" (raised to the {floor:0.00}s separation floor)" : "");
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

                string? what = IgnitionDelay.Apply(__instance, def.jsonKey);
                if (what != null) Plugin.Diag($"[Meridian] HOLD {def.jsonKey}: {what}.");

                string? pushed = IgnitionDelay.Eject(__instance, def.jsonKey);
                if (pushed != null) Plugin.Diag($"[Meridian] EJECT {def.jsonKey}: {pushed}.");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[Meridian] Ignition hold failed: {ex.Message}");
            }
        }
    }
}
