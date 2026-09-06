using System;
using System.Reflection;
using HarmonyLib;

namespace MeridianWorks
{

    [HarmonyPatch(typeof(ARHSeeker), "SlowChecks")]
    internal static class ARHSeeker_SlowChecks_NoLockEndurancePatch
    {

        private const float FloorSeconds = 20f;

        private static readonly FieldInfo? FMissile =
            AccessTools.Field(typeof(MissileSeeker), "missile");

        private static readonly FieldInfo? FTargetUnit =
            AccessTools.Field(typeof(MissileSeeker), "targetUnit");

        private static bool _warned;

        [HarmonyPrefix]
        private static bool Prefix(ARHSeeker __instance)
        {
            try
            {

                if (FMissile == null || FTargetUnit == null)
                {
                    if (!_warned)
                    {
                        _warned = true;
                        Plugin.Log.LogWarning("[Meridian] The no-lock endurance floor is OFF.");
                    }
                    return true;
                }

                if (FMissile.GetValue(__instance) is not Missile m || m == null) return true;
                if (m.definition is not MissileDefinition def) return true;
                if (!PluginInfo.IsOurMissileKey(def.jsonKey)) return true;

                if (FTargetUnit.GetValue(__instance) is Unit u && u != null) return true;

                if (m.timeSinceSpawn >= FloorSeconds) return true;

                return false;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[Meridian] No-lock endurance floor failed: {ex.Message}");
                return true;
            }
        }
    }
}
