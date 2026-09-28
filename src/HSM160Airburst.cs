using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace MeridianWorks
{

    [HarmonyPatch(typeof(BallisticMissileGuidance), nameof(BallisticMissileGuidance.Initialize))]
    internal static class HSM160Airburst
    {
        private const float HoldArmSeconds = 1000f;

        private static readonly FieldInfo? FMissile = AccessTools.Field(typeof(MissileSeeker), "missile");
        private static readonly FieldInfo? FAirburst = AccessTools.Field(typeof(BallisticMissileGuidance), "airburstHeight");
        private static readonly FieldInfo? FArmDelay = AccessTools.Field(typeof(BallisticMissileGuidance), "armDelay");
        private static bool _logged;

        [HarmonyPostfix]
        private static void Postfix(BallisticMissileGuidance __instance)
        {
            try
            {
                if (FMissile?.GetValue(__instance) is not Missile m) return;
                if ((m.definition as MissileDefinition)?.jsonKey is not string key || !HSM160Rounds.Ours.Contains(key)) return;
                WeaponInfo? info = m.GetWeaponInfo();
                if (info == null || info.airburstHeight <= 0f) return;
                if (FAirburst == null || FArmDelay == null)
                {
                    if (!_logged) { _logged = true; Plugin.Log.LogWarning("[Meridian] HSM-160 airburst: guidance fields not found, burst NOT set."); }
                    return;
                }

                float wasHeight = (float)FAirburst.GetValue(__instance);
                float wasDelay = (float)FArmDelay.GetValue(__instance);
                FAirburst.SetValue(__instance, info.airburstHeight);
                FArmDelay.SetValue(__instance, HoldArmSeconds);

                if (!_logged)
                {
                    _logged = true;
                    Plugin.Log.LogInfo($"[Meridian] HSM-160 airburst set on {key}: height {wasHeight:F0} -> {info.airburstHeight:F0} m, "
                        + $"armDelay {wasDelay:F0} -> {HoldArmSeconds:F0} s.");
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("[Meridian] HSM-160 airburst setup failed: " + e.Message);
            }
        }
    }
}
