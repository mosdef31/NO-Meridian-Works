using System;
using System.Collections.Generic;
using HarmonyLib;
using NuclearOption.Networking;
using NuclearOption.SavedMission;
using UnityEngine;

namespace MeridianWorks
{

    internal static class AiLoadout
    {

        internal const float BaseChance = 0.35f;

        private const float RoleDistance = 0.30f;

        private static readonly HashSet<string> _logged = new HashSet<string>();

        internal static Loadout? Substitute(WeaponManager? weapons, Loadout? basis, FactionHQ? hq)
        {
            if (weapons == null || weapons.hardpointSets == null) return null;
            HardpointSet[] sets = weapons.hardpointSets;
            if (sets.Length == 0) return null;

            int carrying = 0, covered = 0;
            var picks = new WeaponMount?[sets.Length];

            for (int i = 0; i < sets.Length; i++)
            {
                WeaponMount? had = basis != null && i < basis.weapons.Count ? basis.weapons[i] : null;
                if (had == null || had.info == null) continue;

                if (had.info.gun) continue;

                carrying++;

                WeaponMount? ours = SameRoleMountIn(sets[i], had, hq);
                if (ours == null) continue;

                picks[i] = ours;
                covered++;
            }

            if (carrying == 0 || covered == 0) return null;

            float coverage = (float)covered / carrying;
            if (UnityEngine.Random.value > BaseChance * coverage) return null;

            var loadout = new Loadout();
            int swapped = 0;
            for (int i = 0; i < sets.Length; i++)
            {
                WeaponMount? had = basis != null && i < basis.weapons.Count ? basis.weapons[i] : null;
                if (picks[i] != null)
                {
                    loadout.weapons.Add(picks[i]!);
                    swapped++;
                }
                else
                {
                    loadout.weapons.Add(had!);
                }
            }

            while (loadout.weapons.Count < sets.Length) loadout.weapons.Add(null!);

            if (swapped == 0) return null;

            string airframe = weapons.name;
            if (_logged.Add(airframe))
                Plugin.Diag(
                    $"[Meridian] AI loadout on {airframe}: {covered} of {carrying} carrying "
                    + $"station(s) can take a Meridian store of the same role, so coverage is "
                    + $"{coverage:0.00} and the chance was {BaseChance * coverage:0.00}. This "
                    + $"flight won the roll and {swapped} station(s) were swapped.");

            return loadout;
        }

        private static WeaponMount? SameRoleMountIn(HardpointSet set, WeaponMount had, FactionHQ? hq)
        {
            if (set == null || set.weaponOptions == null || had.info == null) return null;

            WeaponMount? best = null;
            float bestDistance = RoleDistance;

            foreach (WeaponMount option in set.weaponOptions)
            {
                if (option == null || option.info == null) continue;
                if (!PluginInfo.IsOurMountKey(option.jsonKey)) continue;
                if (!Legal(option, hq)) continue;

                float d = RoleGap(had.info, option.info);
                if (d >= bestDistance) continue;

                bestDistance = d;
                best = option;
            }

            return best;
        }

        private static float RoleGap(WeaponInfo a, WeaponInfo b)
        {
            float s = a.effectiveness.antiSurface - b.effectiveness.antiSurface;
            float r = a.effectiveness.antiAir - b.effectiveness.antiAir;
            float m = a.effectiveness.antiMissile - b.effectiveness.antiMissile;
            float d = a.effectiveness.antiRadar - b.effectiveness.antiRadar;
            return Mathf.Sqrt(s * s + r * r + m * m + d * d);
        }

        private static bool Legal(WeaponMount mount, FactionHQ? hq)
        {
            if (mount.info == null) return false;
            if (mount.info.nuclear) return false;
            if (hq == null) return true;
            if (hq.restrictedWeapons != null && hq.restrictedWeapons.Contains(mount.name)) return false;
            return true;
        }
    }

    [HarmonyPatch(typeof(AircraftParameters), nameof(AircraftParameters.GetRandomStandardLoadout))]
    internal static class AircraftParameters_GetRandomStandardLoadout_MeridianPatch
    {
        [HarmonyPostfix]
        private static void Postfix(AircraftDefinition definition, FactionHQ hq,
                                    ref StandardLoadout __result)
        {
            try
            {
                if (__result == null || definition == null || definition.unitPrefab == null) return;

                Aircraft prefab = definition.unitPrefab.GetComponent<Aircraft>();
                if (prefab == null || prefab.weaponManager == null) return;

                Loadout? ours = AiLoadout.Substitute(prefab.weaponManager, __result.loadout, hq);
                if (ours == null) return;

                __result = new StandardLoadout
                {
                    disabled = false,
                    Name = __result.Name,
                    FuelRatio = __result.FuelRatio,
                    loadout = ours,
                };
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError(
                    "[Meridian] AI loadout substitution threw; the flight keeps the loadout the "
                    + "game chose: " + ex);
            }
        }
    }

    [HarmonyPatch(typeof(WeaponManager), nameof(WeaponManager.SelectAIAircraftWeapons))]
    internal static class WeaponManager_SelectAIAircraftWeapons_MeridianPatch
    {
        [HarmonyPostfix]
        private static void Postfix(WeaponManager __instance, ref Loadout __result)
        {
            try
            {
                if (__result == null) return;

                Aircraft? owner = Traverse.Create(__instance).Field<Aircraft>("aircraft").Value;
                Loadout? ours = AiLoadout.Substitute(
                    __instance, __result, owner != null ? owner.NetworkHQ : null);
                if (ours == null) return;

                __result = ours;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError(
                    "[Meridian] AI loadout substitution threw; the flight keeps the loadout the "
                    + "hangar chose: " + ex);
            }
        }
    }

    [HarmonyPatch(typeof(Spawner), nameof(Spawner.SpawnAircraft))]
    internal static class Spawner_SpawnAircraft_MeridianLoadoutPatch
    {
        [HarmonyPrefix]
        private static void Prefix(Player player, GameObject prefab, ref Loadout loadout,
                                   FactionHQ HQ)
        {
            try
            {

                if (player != null) return;
                if (prefab == null || loadout == null) return;

                Aircraft aircraft = prefab.GetComponent<Aircraft>();
                if (aircraft == null || aircraft.weaponManager == null) return;

                Loadout? ours = AiLoadout.Substitute(aircraft.weaponManager, loadout, HQ);
                if (ours == null) return;

                loadout = ours;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError(
                    "[Meridian] AI loadout substitution threw; the flight keeps the loadout it "
                    + "was spawned with: " + ex);
            }
        }
    }
}
