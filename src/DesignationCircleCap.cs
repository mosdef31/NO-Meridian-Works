using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace MeridianWorks
{

    internal static class DesignationCircleCap
    {

        internal const float MaxArcDegrees = 12f;

        private static readonly FieldInfo? FOuterCircle =
            AccessTools.Field(typeof(HUDLaserGuidedState), "outerCircle");
        private static readonly FieldInfo? FPrefabMissile =
            AccessTools.Field(typeof(HUDLaserGuidedState), "prefabMissile");
        private static readonly FieldInfo? FTargetDist =
            AccessTools.Field(typeof(HUDLaserGuidedState), "targetDist");
        private static readonly FieldInfo? FMinRange =
            AccessTools.Field(typeof(HUDLaserGuidedState), "minRange");
        private static readonly FieldInfo? FMinAlignment =
            AccessTools.Field(typeof(HUDLaserGuidedState), "minAlignment");

        private static readonly FieldInfo? FCam =
            AccessTools.Field(typeof(HUDLaserGuidedState), "cam");

        private static bool _warned;

        internal static bool Available =>
            FOuterCircle != null && FPrefabMissile != null && FTargetDist != null &&
            FMinRange != null && FMinAlignment != null && FCam != null;

        private static bool IsOurs(HUDLaserGuidedState state)
        {
            var missile = FPrefabMissile!.GetValue(state) as Missile;
            var def = missile != null ? missile.definition as MissileDefinition : null;
            return def != null && PluginInfo.IsOurMissileKey(def.jsonKey);
        }

        internal static void Apply(HUDLaserGuidedState state)
        {
            if (!Available)
            {
                if (!_warned)
                {
                    _warned = true;
                    Plugin.Log.LogWarning(
                        "[Meridian] HUDLaserGuidedState no longer carries the fields the designation " +
                        "circle cap reads, so the circle is left at stock behaviour. Check " +
                        "API-WATCHLIST.md against this game version.");
                }
                return;
            }

            if (state == null) return;
            if (!IsOurs(state)) return;

            var cam = FCam!.GetValue(state) as Camera;
            if (cam == null) return;

            if (FOuterCircle!.GetValue(state) is not Image outer || outer == null) return;

            if (!outer.enabled) return;

            float fov = cam.fieldOfView;
            if (fov <= 0.01f) return;

            float targetDist = (float)FTargetDist!.GetValue(state);
            float minRange = (float)FMinRange!.GetValue(state);
            float minAlignment = (float)FMinAlignment!.GetValue(state);

            float gameArc = Mathf.Min(minAlignment, Mathf.Max(targetDist, minRange) * 0.002f);
            float cappedArc = Mathf.Min(gameArc, MaxArcDegrees);

            outer.transform.localScale = 50f / fov * (cappedArc / 8f) * Vector3.one;
        }
    }

    [HarmonyPatch(typeof(HUDLaserGuidedState),
                  nameof(HUDLaserGuidedState.UpdateWeaponDisplay))]
    internal static class HUDLaserGuidedState_UpdateWeaponDisplay_CirclePatch
    {
        [HarmonyPostfix]
        private static void Postfix(HUDLaserGuidedState __instance)
        {
            try
            {
                DesignationCircleCap.Apply(__instance);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[Meridian] Designation circle cap threw: {ex.Message}");
            }
        }
    }
}
