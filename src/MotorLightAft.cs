using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace MeridianWorks
{

    [HarmonyPatch]
    internal static class MotorLightAft
    {
        private static readonly Dictionary<string, float> AftM = new Dictionary<string, float>
        {
            { "MeridianAAM63_Missile", 1.2f },
        };

        private static readonly HashSet<int> Moved = new HashSet<int>();
        private static bool _failed, _logged;

        private static System.Reflection.MethodBase TargetMethod() =>
            AccessTools.Method(AccessTools.Inner(typeof(Missile), "Motor"), "Activate");

        private static void Postfix(object __instance, Missile missile)
        {
            if (_failed || missile == null) return;
            try
            {
                string? key = (missile.definition as MissileDefinition)?.jsonKey;
                if (key == null || !AftM.TryGetValue(key, out float aft)) return;
                if (Traverse.Create(__instance).Field("lights").GetValue() is not Light[] lights) return;
                int n = 0;
                foreach (Light l in lights)
                {
                    if (l == null || !Moved.Add(l.GetInstanceID())) continue;
                    l.transform.position -= missile.transform.forward * aft;
                    l.range += aft;
                    n++;
                }
                if (n > 0 && !_logged)
                {
                    _logged = true;
                    Plugin.Log.LogInfo($"[Meridian] {key}: {n} motor light(s) moved {aft} m aft of the nozzle.");
                }
            }
            catch (Exception e)
            {
                _failed = true;
                Plugin.Log.LogWarning("[Meridian] motor light aft stopped: " + e.Message);
            }
        }
    }
}
