using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace MeridianWorks
{

    internal static class TerminalTraceProbe
    {

        private const float LastLeg = 2000f;

        private sealed class Trace
        {
            internal string Key = "";
            internal float Closest = float.MaxValue;
            internal float InLock;
            internal float Frozen;
            internal float Lost;
            internal float ConeBailAt = -1f;
            internal float TargetDroppedAt = -1f;
            internal float PerseveranceRanOutAt = -1f;
            internal float MaxAimpointError;
            internal bool EverInLastLeg;
        }

        private static readonly Dictionary<int, Trace> _live = new Dictionary<int, Trace>();
        private static readonly HashSet<string> _reported = new HashSet<string>();

        private static readonly FieldInfo? FKnownPos =
            AccessTools.Field(typeof(ARHSeeker), "knownPos");
        private static readonly FieldInfo? FReturnStrength =
            AccessTools.Field(typeof(ARHSeeker), "returnStrength");
        private static readonly FieldInfo? FHomingLockTime =
            AccessTools.Field(typeof(ARHSeeker), "homingLockTime");
        private static readonly FieldInfo? FHomingLockDelay =
            AccessTools.Field(typeof(ARHSeeker), "homingLockDelay");
        private static readonly FieldInfo? FMaxTrackingAngle =
            AccessTools.Field(typeof(ARHSeeker), "maxTrackingAngle");
        private static readonly FieldInfo? FLockPerseverance =
            AccessTools.Field(typeof(ARHSeeker), "lockPerseverance");
        private static readonly FieldInfo? FTimeWithoutReturn =
            AccessTools.Field(typeof(ARHSeeker), "timeWithoutReturn");
        private static readonly FieldInfo? FRadarParameters =
            AccessTools.Field(typeof(ARHSeeker), "radarParameters");
        private static readonly FieldInfo? FRadarLockEstablished =
            AccessTools.Field(typeof(ARHSeeker), "radarLockEstablished");

        internal static bool Wired =>
            FKnownPos != null && FReturnStrength != null && FHomingLockTime != null
            && FHomingLockDelay != null && FMaxTrackingAngle != null
            && FLockPerseverance != null && FTimeWithoutReturn != null
            && FRadarParameters != null && FRadarLockEstablished != null;

        internal static void Sample(ARHSeeker seeker, Unit? target)
        {
            if (!Plugin.Diagnostics || !Wired || seeker == null) return;

            Missile? missile = Traverse.Create(seeker).Field<Missile>("missile").Value;
            if (missile == null) return;

            string key = (missile.definition as MissileDefinition)?.jsonKey ?? missile.name;
            if (!PluginInfo.IsOurMissileKey(key)) return;

            int id = missile.GetInstanceID();
            if (!_live.TryGetValue(id, out Trace t))
            {
                t = new Trace { Key = key };
                _live[id] = t;
            }

            if (target == null)
            {
                if (t.TargetDroppedAt < 0f && t.Closest < float.MaxValue)
                    t.TargetDroppedAt = t.Closest;
                return;
            }

            float range = Vector3.Distance(missile.transform.position, target.transform.position);
            if (range < t.Closest) t.Closest = range;
            if (range > LastLeg) return;
            t.EverInLastLeg = true;

            float dt = Time.fixedDeltaTime;
            float ret = (float)FReturnStrength!.GetValue(seeker);
            float minSignal = MinSignal(seeker);
            bool haveReturn = ret > minSignal;

            if (haveReturn)
            {
                t.InLock += dt;

                float held = (float)FHomingLockTime!.GetValue(seeker);
                float delay = (float)FHomingLockDelay!.GetValue(seeker);
                if (held <= delay) t.Frozen += dt;
            }
            else
            {
                t.Lost += dt;

                if ((float)FTimeWithoutReturn!.GetValue(seeker) > (float)FLockPerseverance!.GetValue(seeker)
                    && t.PerseveranceRanOutAt < 0f)
                    t.PerseveranceRanOutAt = range;
            }

            if (FKnownPos!.GetValue(seeker) is GlobalPosition known)
            {

                float err = (known - target.GlobalPosition()).magnitude;
                if (err > t.MaxAimpointError) t.MaxAimpointError = err;

                if (!haveReturn && (bool)FRadarLockEstablished!.GetValue(seeker))
                {
                    float cone = (float)FMaxTrackingAngle!.GetValue(seeker);
                    float ang = Vector3.Angle(known - missile.GlobalPosition(),
                                              missile.transform.forward);
                    if (ang > cone && t.ConeBailAt < 0f) t.ConeBailAt = range;
                }
            }
        }

        internal static void Finish(Missile? missile)
        {
            if (!Plugin.Diagnostics || missile == null) return;

            int id = missile.GetInstanceID();
            if (!_live.TryGetValue(id, out Trace t)) return;
            _live.Remove(id);

            if (!t.EverInLastLeg) return;
            if (!_reported.Add(t.Key + "|" + (t.ConeBailAt >= 0f) + "|" + (t.PerseveranceRanOutAt >= 0f)))
                return;

            string verdict;
            if (t.ConeBailAt >= 0f)
                verdict = $"THE CONE BAILOUT FIRED at {t.ConeBailAt:0} m - the aimpoint went "
                        + "outside maxTrackingAngle on a lost return and was replaced with "
                        + "'straight ahead'. Raising maxTrackingAngle is the lever, NOT agility.";
            else if (t.PerseveranceRanOutAt >= 0f)
                verdict = $"PERSEVERANCE RAN OUT at {t.PerseveranceRanOutAt:0} m - the return "
                        + "stayed under minSignal longer than lockPerseverance and the target "
                        + "was dropped. The lever is the return, not the aimpoint.";
            else if (t.Frozen > t.InLock * 0.5f && t.InLock > 0f)
                verdict = $"THE AIMPOINT WAS FROZEN for {t.Frozen:0.00} s of {t.InLock:0.00} s "
                        + "in lock - homingLockDelay had not elapsed, so knownPos sat on the "
                        + "datalink estimate rather than the target. That is the last-leg drift.";
            else if (t.InLock <= 0f)
                verdict = "NO USABLE RETURN AT ALL inside the last leg, so the round flew the "
                        + "datalink's estimate the whole way in.";
            else
                verdict = "the aimpoint tracked the target through the last leg, so whatever "
                        + "the miss was, it was not this seeker losing its aimpoint.";

            Plugin.Diag(
                $"[Meridian] TERMINAL {t.Key}: closest {t.Closest:0.0} m; inside {LastLeg:0} m it "
                + $"held a return for {t.InLock:0.00} s and lost it for {t.Lost:0.00} s; the "
                + $"aimpoint was frozen for {t.Frozen:0.00} s and drifted up to "
                + $"{t.MaxAimpointError:0.0} m from the target. VERDICT: {verdict}");
        }

        private static float MinSignal(ARHSeeker seeker)
        {
            object? p = FRadarParameters?.GetValue(seeker);
            if (p == null) return 0.5f;
            return AccessTools.Field(p.GetType(), "minSignal")?.GetValue(p) is float m ? m : 0.5f;
        }
    }

    [HarmonyPatch(typeof(ARHSeeker), nameof(ARHSeeker.Seek))]
    internal static class ARHSeeker_Seek_TerminalTracePatch
    {
        [HarmonyPostfix]
        private static void Postfix(ARHSeeker __instance)
        {
            try
            {
                TerminalTraceProbe.Sample(
                    __instance,
                    Traverse.Create(__instance).Field<Unit>("targetUnit").Value);
            }
            catch
            {

            }
        }
    }

    [HarmonyPatch(typeof(Missile), nameof(Missile.Detonate))]
    internal static class Missile_Detonate_TerminalTracePatch
    {
        [HarmonyPostfix]
        private static void Postfix(Missile __instance)
        {
            try { TerminalTraceProbe.Finish(__instance); }
            catch (Exception ex) { Plugin.Log.LogWarning("[Meridian] Terminal trace: " + ex.Message); }
        }
    }
}
