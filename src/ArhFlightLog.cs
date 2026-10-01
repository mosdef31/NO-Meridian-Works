using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace MeridianWorks
{

    internal sealed class ArhFlightLog : MonoBehaviour
    {
        internal const string Key = "MeridianAMRAAM_Missile";
        private const int MaxShots = 12;
        private const float SampleS = 0.5f;

        private static readonly FieldInfo? FTarget = AccessTools.Field(typeof(ARHSeeker), "targetUnit");
        private static readonly FieldInfo? FLock = AccessTools.Field(typeof(ARHSeeker), "radarLockEstablished");
        private static readonly FieldInfo? FReturn = AccessTools.Field(typeof(ARHSeeker), "returnStrength");
        private static readonly FieldInfo? FJam = AccessTools.Field(typeof(ARHSeeker), "jamAccumulation");
        private static readonly FieldInfo? FSlowSpeed = AccessTools.Field(typeof(ARHSeeker), "selfDestructAtSpeed");

        private static int _shots;
        private Missile _m = null!;
        private ARHSeeker _s = null!;
        private Unit? _launchTarget;
        private int _id;
        private float _next;
        private bool _failed;

        internal static void Begin(ARHSeeker s, Missile m, Unit? target)
        {
            if (_shots >= MaxShots || m.GetComponent<ArhFlightLog>() != null) return;
            ArhFlightLog log = m.gameObject.AddComponent<ArhFlightLog>();
            log._m = m;
            log._s = s;
            log._launchTarget = target;
            log._id = ++_shots;
            Plugin.Log.LogInfo($"[Meridian] AAM-120C #{log._id} LAUNCH target={(target != null ? target.unitName : "NONE")} " + log.Geometry(target));
        }

        private Unit? Target => FTarget?.GetValue(_s) as Unit;

        private string Geometry(Unit? t)
        {
            if (t == null || t.rb == null) return "range=- close=- off=-";
            Vector3 d = t.transform.position - _m.transform.position;
            float range = d.magnitude;
            float close = range > 1f ? Vector3.Dot(_m.rb.velocity - t.rb.velocity, d / range) : 0f;
            float off = Vector3.Angle(_m.transform.forward, d);
            return $"range={range:F0}m close={close:F0}m/s off={off:F0}deg";
        }

        private void FixedUpdate()
        {
            if (_failed || _m == null || _m.disabled || !_m.LocalSim || Time.time < _next) return;
            _next = Time.time + SampleS;
            try
            {
                Unit? t = Target;
                string same = t == _launchTarget ? "" : $" SWITCHED->{(t != null ? t.unitName : "NONE")}";
                Plugin.Log.LogInfo($"[Meridian] AAM-120C #{_id} t={_m.timeSinceSpawn:F1}s {Geometry(t)} "
                    + $"mode={_m.seekerMode} lock={FLock?.GetValue(_s)} ret={FReturn?.GetValue(_s):0.##} "
                    + $"jam={FJam?.GetValue(_s):0.##} spd={_m.speed:F0} engine={_m.EngineOn()}{same}");
            }
            catch (Exception e)
            {
                _failed = true;
                Plugin.Log.LogWarning("[Meridian] AAM-120C flight log stopped: " + e.Message);
            }
        }

        internal void End(Vector3 normal, bool hitArmor, bool hitTerrain)
        {
            if (_failed) return;
            try
            {
                Unit? t = Target;
                float miss = t != null ? Vector3.Distance(t.transform.position, _m.transform.position) : -1f;
                float slow = FSlowSpeed?.GetValue(_s) is float f ? f : 200f;
                Plugin.Log.LogInfo($"[Meridian] AAM-120C #{_id} END t={_m.timeSinceSpawn:F1}s miss={miss:F0}m "
                    + $"hitArmor={hitArmor} hitTerrain={hitTerrain} | selfdestruct clauses: "
                    + $"losingGround={_m.LosingGround()} missed={_m.MissedTarget()} "
                    + $"slow={_m.speed < slow}({_m.speed:F0}<{slow:F0}) noTarget={t == null}");
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("[Meridian] AAM-120C flight log end failed: " + e.Message);
            }
        }
    }

    [HarmonyPatch(typeof(ARHSeeker), nameof(ARHSeeker.Initialize))]
    internal static class ArhFlightLogStart
    {
        private static readonly FieldInfo? FMissile = AccessTools.Field(typeof(MissileSeeker), "missile");

        private static void Postfix(ARHSeeker __instance, Unit target)
        {
            try
            {
                if (FMissile?.GetValue(__instance) is not Missile m || !m.LocalSim) return;
                if ((m.definition as MissileDefinition)?.jsonKey != ArhFlightLog.Key) return;
                ArhFlightLog.Begin(__instance, m, target);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("[Meridian] AAM-120C flight log start failed: " + e.Message);
            }
        }
    }

    [HarmonyPatch(typeof(Missile), nameof(Missile.Detonate), new[] { typeof(Vector3), typeof(bool), typeof(bool) })]
    internal static class ArhFlightLogEnd
    {
        private static void Prefix(Missile __instance, Vector3 normal, bool hitArmor, bool hitTerrain)
        {
            if (__instance == null || __instance.disabled) return;
            ArhFlightLog? log = __instance.GetComponent<ArhFlightLog>();
            if (log != null) log.End(normal, hitArmor, hitTerrain);
        }
    }
}
