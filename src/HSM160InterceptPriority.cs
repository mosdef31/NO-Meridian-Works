using System;
using HarmonyLib;

namespace MeridianWorks
{

    [HarmonyPatch(typeof(Missile), nameof(Missile.InterceptPriority))]
    internal static class HSM160InterceptPriority
    {
        private const float Floor = 1f;
        private static bool _failed, _logged;

        private static void Postfix(Missile __instance, ref float __result)
        {
            if (_failed || __result >= Floor) return;
            try
            {
                bool ours = HSM160Dart.Darts.TryGetValue(__instance, out _);
                if (!ours && (__instance.definition as MissileDefinition)?.jsonKey is string key)
                    ours = HSM160Rounds.Ours.Contains(key);
                if (!ours) return;

                __result = Floor;
                if (!_logged)
                {
                    _logged = true;
                    Plugin.Log.LogInfo("[Meridian] HSM-160 intercept priority: air defence weighs "
                        + __instance.unitName + " at full priority.");
                }
            }
            catch (Exception e)
            {
                _failed = true;
                Plugin.Log.LogWarning("[Meridian] HSM-160 intercept priority stopped: " + e.Message);
            }
        }
    }
}
