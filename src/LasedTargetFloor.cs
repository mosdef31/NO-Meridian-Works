using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace MeridianWorks
{

    internal static class LasedTargetFloor
    {

        internal const int Floor = 6;

        private static readonly FieldInfo? FMaxTargets =
            AccessTools.Field(typeof(LaserDesignator), "maxTargets");

        private static readonly Dictionary<LaserDesignator, int> _applied =
            new Dictionary<LaserDesignator, int>();

        private static readonly HashSet<string> _logged = new HashSet<string>();

        internal static void Apply(Aircraft aircraft)
        {
            if (FMaxTargets == null)
            {
                if (_logged.Add("nofield"))
                    Plugin.Log.LogWarning("[Meridian] LaserDesignator.maxTargets was not found.");
                return;
            }

            if (aircraft == null) return;

            LaserDesignator? designator = aircraft.GetLaserDesignator();

            if (designator == null) return;

            if (_applied.ContainsKey(designator)) return;

            int current = designator.GetMaxTargets();
            int delta = Mathf.Max(0, Floor - current);
            if (delta == 0) return;

            FMaxTargets.SetValue(designator, current + delta);
            _applied[designator] = delta;

            if (_logged.Add(aircraft.definition != null ? aircraft.definition.jsonKey : "?"))
                Plugin.Diag(
                    $"[Meridian] {(aircraft.definition != null ? aircraft.definition.unitName : "aircraft")}: " +
                    $"carrying Meridian rounds, so lased targets went {current} -> {current + delta} " +
                    $"(floor {Floor}, delta {delta}).");
        }

        internal static void Prune()
        {
            if (_applied.Count == 0) return;

            var dead = new List<LaserDesignator>();
            foreach (KeyValuePair<LaserDesignator, int> pair in _applied)
            {

                if (pair.Key == null) dead.Add(pair.Key!);
            }

            foreach (LaserDesignator d in dead) _applied.Remove(d);
        }
    }

    [HarmonyPatch(typeof(HardpointSet), nameof(HardpointSet.SpawnMounts))]
    internal static class HardpointSet_SpawnMounts_LasedTargetPatch
    {
        [HarmonyPostfix]
        private static void Postfix(Aircraft aircraft, WeaponMount weaponMount)
        {
            try
            {
                LasedTargetFloor.Prune();

                if (weaponMount == null) return;
                if (!PluginInfo.IsOurMountKey(weaponMount.jsonKey)) return;

                LasedTargetFloor.Apply(aircraft);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[Meridian] Lased-target floor threw: {ex.Message}");
            }
        }
    }
}
