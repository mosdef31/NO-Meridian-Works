using System;
using HarmonyLib;
using UnityEngine;

namespace MeridianWorks
{

    [HarmonyPatch(typeof(Missile), nameof(Missile.GetRadarReturn))]
    internal static class SeaSkimEcm
    {

        private const string Key = "MeridianExocetAir";

        private const float SkimCeilingMetres = 10f;

        private const float FloorFraction = 0.60f;

        private static bool _said;

        [HarmonyPostfix]
        private static void Postfix(Missile __instance, RadarParams radarParameters,
                                    ref float __result)
        {
            try
            {
                if (__instance == null) return;

                if (!PluginInfo.IsRound((__instance.definition as MissileDefinition)?.jsonKey, Key)) return;

                if (__result <= radarParameters.minSignal) return;

                if (__instance.radarAlt > SkimCeilingMetres) return;

                float jam = FloorFraction * radarParameters.minSignal;
                if (jam <= 0f) return;

                __result -= jam;

                if (!_said)
                {
                    _said = true;
                    Plugin.Log.LogInfo(
                        "[Meridian] Sea-skim ECM: the AShM-140 now subtracts "
                        + FloorFraction.ToString("0.##") + " of the looking radar's own "
                        + "minSignal from its return while it is under "
                        + SkimCeilingMetres.ToString("0") + " m. Stock rounds and the rest of "
                        + "this pack are untouched.");
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[Meridian] Sea-skim ECM failed: " + ex.Message);
            }
        }
    }
}
