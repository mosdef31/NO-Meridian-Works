using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace MeridianWorks
{

    internal static class HSM160ApexState
    {
        internal static readonly ConditionalWeakTable<Missile, object> Released = new();
    }

    [HarmonyPatch(typeof(Missile), "EngineOn")]
    internal static class HSM160SuppressClimb
    {
        private static bool Prefix(Missile __instance, ref bool __result)
        {
            if ((__instance.definition as MissileDefinition)?.jsonKey is string key && HSM160Rounds.Ours.Contains(key))
            {
                __result = false;
                return false;
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(Missile), "MotorThrust")]
    internal static class HSM160HoldFire
    {
        private static bool Prefix(Missile __instance)
        {
            if ((__instance.definition as MissileDefinition)?.jsonKey is string key && HSM160Rounds.Ours.Contains(key)
                && !HSM160ApexState.Released.TryGetValue(__instance, out _))
            {
                return false;
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(Missile), "MotorThrust")]
    internal static class HSM160SpeedCap
    {
        internal const float CapMach = 5.5f;

        private static readonly Type? MotorType = AccessTools.Inner(typeof(Missile), "Motor");
        private static readonly FieldInfo? FMotors = AccessTools.Field(typeof(Missile), "motors");
        private static readonly FieldInfo? FTopSpeed = MotorType != null ? AccessTools.Field(MotorType, "topSpeed") : null;
        private static bool _failed, _logged;

        private static void Prefix(Missile __instance)
        {
            if (_failed) return;
            try
            {
                if ((__instance.definition as MissileDefinition)?.jsonKey is not string key || !HSM160Rounds.Ours.Contains(key)) return;
                Rigidbody? rb = __instance.rb;
                if (rb == null) return;
                float cap = CapMach * Mathf.Max(1f, LevelInfo.GetSpeedOfSound(__instance.GlobalPosition().y));
                if (FMotors?.GetValue(__instance) is Array motors && motors.Length > 0 && motors.GetValue(0) is object m0)
                    FTopSpeed?.SetValue(m0, cap);
                if (!rb.isKinematic && rb.velocity.sqrMagnitude > cap * cap)
                {
                    rb.velocity = rb.velocity.normalized * cap;
                    if (!_logged)
                    {
                        _logged = true;
                        Plugin.Log.LogInfo($"[Meridian] HSM-160 speed cap: held at Mach {CapMach} ({cap:F0} m/s) at {__instance.GlobalPosition().y / 1000f:F1} km.");
                    }
                }
            }
            catch (Exception e)
            {
                _failed = true;
                Plugin.Log.LogWarning("[Meridian] HSM-160 speed cap stopped: " + e.Message);
            }
        }
    }

    internal static class HSM160ApexRelease
    {

    }
}
