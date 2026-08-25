using System;
using System.Linq;
using UnityEngine;

namespace MeridianWorks
{

    internal static class NameGate
    {
        private static bool _ran;

        internal static void RunOnce()
        {
            if (_ran) return;
            _ran = true;

            try
            {
                Check();
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[Meridian] Name gate failed: {ex.Message}");
            }
        }

        private static void Check()
        {
            int faults = 0;

            foreach (WeaponMount mount in EncyclopediaRegistration.ResolvedMounts)
            {
                if (mount == null) continue;

                faults += ReportWhitespace("WeaponMount.jsonKey", mount.name, mount.jsonKey);

                if (mount.info == null)
                {
                    Plugin.Log.LogError(
                        $"[Meridian] Mount '{mount.jsonKey}' has no WeaponInfo. It cannot be loaded " +
                        "onto an aircraft and the loadout entry has nothing to name itself from.");
                    faults++;
                    continue;
                }

                faults += ReportWhitespace("WeaponInfo.weaponName", mount.info.name, mount.info.weaponName);

                PluginInfo.Weapon? owner = PluginInfo.Weapons
                    .FirstOrDefault(w => w.MountKeys.Contains(mount.jsonKey));

                if (owner == null)
                {

                    continue;
                }

                if (mount.info.weaponName != owner.WeaponName)
                {
                    Plugin.Log.LogError(
                        $"[Meridian] {owner.Designation}: WeaponInfo.weaponName is " +
                        $"'{mount.info.weaponName}' but the key table expects '{owner.WeaponName}'. " +
                        "One of the two moved without the other. The bundle is authoritative for what " +
                        "a player sees; PluginInfo.cs is authoritative for what the code gates on, and " +
                        "while they disagree the loadout and the code are describing different weapons.");
                    faults++;
                }
            }

            foreach (MissileDefinition def in EncyclopediaRegistration.ResolvedMissiles)
            {
                if (def == null) continue;

                faults += ReportWhitespace("MissileDefinition.jsonKey", def.name, def.jsonKey);

                if (def.unitPrefab == null)
                {
                    Plugin.Log.LogError(
                        $"[Meridian] MissileDefinition '{def.jsonKey}' has no unitPrefab, so the round " +
                        "has nothing to spawn. Re-check what was ticked into the bundle.");
                    faults++;
                }
            }

            if (faults == 0)
                Plugin.Log.LogInfo(
                    $"[Meridian] Name gate clean: {EncyclopediaRegistration.ResolvedMounts.Count} " +
                    $"mount(s) and {EncyclopediaRegistration.ResolvedMissiles.Count} missile " +
                    "definition(s), no stray whitespace and no name mismatch.");
            else
                Plugin.Log.LogError(
                    $"[Meridian] Name gate found {faults} fault(s). Each one is silent in game - fix " +
                    "them in Unity and re-export before flying anything.");
        }

        private static int ReportWhitespace(string what, string assetName, string? value)
        {
            if (string.IsNullOrEmpty(value))
            {
                Plugin.Log.LogError($"[Meridian] Asset '{assetName}': {what} is empty.");
                return 1;
            }

            string actual = value!;
            if (actual == actual.Trim()) return 0;

            Plugin.Log.LogError(
                $"[Meridian] Asset '{assetName}': {what} is '{actual}' - it has stray whitespace. " +
                "Fix it at the source in Unity and re-export.");
            return 1;
        }
    }
}
