using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace MeridianWorks
{
    internal static class Aam54Endurance
    {
        internal const string Key = "MeridianAAM54_Missile";

        private const float LostGraceS = 15f;
        private const float NoLockFloorS = 60f;

        private sealed class Track
        {
            public bool HadTarget;
            public float LostSince = -1f;
        }

        private static readonly ConditionalWeakTable<Missile, Track> Tracks = new();

        private static readonly FieldInfo? FMissile = AccessTools.Field(typeof(MissileSeeker), "missile");
        private static readonly FieldInfo? FTargetUnit = AccessTools.Field(typeof(MissileSeeker), "targetUnit");
        private static readonly FieldInfo? FSelfDestruct = AccessTools.Field(typeof(ARHSeeker), "selfDestructAtSpeed");

        internal static Missile? Holding;

        private static bool _saidHold;

        internal static bool IsAam54(Missile? m) =>
            m != null && m.definition is MissileDefinition def && def.jsonKey == Key;

        internal static Missile? MissileOf(ARHSeeker s) => FMissile?.GetValue(s) as Missile;

        internal static bool ShouldHold(ARHSeeker s, Missile m)
        {
            float floor = FSelfDestruct?.GetValue(s) is float f ? f : 200f;
            if (m.speed < floor) return false;

            Track tr = Tracks.GetValue(m, _ => new Track());
            float now = m.timeSinceSpawn;

            if (FTargetUnit?.GetValue(s) is Unit u && u != null && !u.disabled)
            {
                tr.HadTarget = true;
                tr.LostSince = -1f;
                Vector3 to = u.GlobalPosition() - m.GlobalPosition();
                bool passed = Vector3.Dot(to, m.rb.velocity) < 0f;
                return !passed;
            }

            if (!tr.HadTarget) return now < NoLockFloorS;
            if (tr.LostSince < 0f) tr.LostSince = now;
            return now - tr.LostSince < LostGraceS;
        }

        internal static void SaidHold()
        {
            if (_saidHold) return;
            _saidHold = true;
            Plugin.Log.LogInfo("[Meridian] AAM-54: held an early self-destruct; the round flies on.");
        }
    }

    [HarmonyPatch(typeof(ARHSeeker), "SlowChecks")]
    internal static class Aam54Endurance_SlowChecks
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static void Prefix(ARHSeeker __instance)
        {
            try
            {
                Aam54Endurance.Holding = null;
                Missile? m = Aam54Endurance.MissileOf(__instance);
                if (!Aam54Endurance.IsAam54(m) || m!.disabled) return;
                if (Aam54Endurance.ShouldHold(__instance, m)) Aam54Endurance.Holding = m;
            }
            catch (Exception e)
            {
                Aam54Endurance.Holding = null;
                Plugin.Log.LogWarning("[Meridian] AAM-54 endurance check failed: " + e.Message);
            }
        }

        [HarmonyPostfix]
        private static void Postfix() => Aam54Endurance.Holding = null;
    }

    [HarmonyPatch(typeof(Missile), nameof(Missile.Detonate), new[] { typeof(Vector3), typeof(bool), typeof(bool) })]
    internal static class Aam54Endurance_Detonate
    {
        [HarmonyPrefix]
        private static bool Prefix(Missile __instance)
        {
            if (Aam54Endurance.Holding == null || !ReferenceEquals(Aam54Endurance.Holding, __instance)) return true;
            Aam54Endurance.SaidHold();
            return false;
        }
    }
}
