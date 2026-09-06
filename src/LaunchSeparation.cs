using System;
using HarmonyLib;
using UnityEngine;

namespace MeridianWorks
{

    [HarmonyPatch(typeof(Missile), nameof(Missile.Detonate),
                  new[] { typeof(Vector3), typeof(bool), typeof(bool) })]
    internal static class Missile_Detonate_LaunchSeparationPatch
    {

        private const float SeparationRadius = 60f;

        private const float SeparationSeconds = 3f;

        private static bool _warned;

        [HarmonyPrefix]
        private static bool Prefix(Missile __instance)
        {
            try
            {
                if (__instance == null || __instance.disabled) return true;
                if (__instance.definition is not MissileDefinition def) return true;
                if (!PluginInfo.IsOurMissileKey(def.jsonKey)) return true;

                if (__instance.timeSinceSpawn > SeparationSeconds) return true;

                if (!__instance.ownerID.TryGetUnit(out Unit launcher)) return true;
                if (launcher == null) return true;

                if (!FastMath.InRange(launcher.transform.position,
                                      __instance.transform.position,
                                      SeparationRadius))
                    return true;

                if (!_warned)
                {
                    _warned = true;
                    Plugin.Log.LogWarning(
                        $"[Meridian] {def.jsonKey} tried to detonate beside its launcher; held off.");
                }

                return false;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[Meridian] Launch separation guard failed: {ex.Message}");
                return true;
            }
        }
    }
}
