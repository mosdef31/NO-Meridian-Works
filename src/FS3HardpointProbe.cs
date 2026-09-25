using System;
using System.Collections.Generic;
using System.Text;
using HarmonyLib;
using UnityEngine;

namespace MeridianWorks
{

    [HarmonyPatch(typeof(WeaponManager), "Awake")]
    internal static class FS3HardpointProbe
    {

        private const string TargetAircraft = "P_Trisurface1";

        private static readonly HashSet<string> _reported = new HashSet<string>();

        [HarmonyPostfix]
        private static void Postfix(WeaponManager __instance)
        {
            try
            {
                if (!Plugin.Diagnostics) return;
                if (__instance == null || __instance.hardpointSets == null) return;

                Aircraft? aircraft = __instance.GetComponent<Aircraft>();
                string prefabName = aircraft != null ? aircraft.gameObject.name : __instance.gameObject.name;
                if (prefabName.IndexOf(TargetAircraft, StringComparison.OrdinalIgnoreCase) < 0) return;

                string instanceKey = __instance.GetInstanceID().ToString();
                if (!_reported.Add(instanceKey)) return;

                var sb = new StringBuilder();
                sb.Append($"[Meridian] FS3 HARDPOINTS on '{prefabName}', ")
                  .Append(__instance.hardpointSets.Length)
                  .Append(" set(s). Axes match mount-geometry.py: x=span, y=vertical, ")
                  .Append("z=nose-positive fore/aft.");

                for (int s = 0; s < __instance.hardpointSets.Length; s++)
                {
                    HardpointSet set = __instance.hardpointSets[s];
                    if (set == null) continue;

                    sb.Append($"\n  set[{s}] '{set.name}', {set.hardpoints?.Count ?? 0} hardpoint(s):");

                    if (set.hardpoints == null) continue;
                    for (int h = 0; h < set.hardpoints.Count; h++)
                    {
                        Hardpoint hp = set.hardpoints[h];
                        Transform? t = hp?.transform;
                        if (t == null)
                        {
                            sb.Append($"\n    [{h}] no transform");
                            continue;
                        }

                        Vector3 p = t.localPosition;
                        Vector3 e = t.localEulerAngles;
                        sb.Append($"\n    [{h}] '{t.name}' localPos=({p.x:0.000}, {p.y:0.000}, {p.z:0.000}) "
                                  + $"localEuler=({e.x:0.0}, {e.y:0.0}, {e.z:0.0})");
                    }
                }

                Plugin.Diag(sb.ToString());
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[Meridian] FS3 hardpoint probe threw: {ex.Message}");
            }
        }
    }
}
