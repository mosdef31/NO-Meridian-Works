using System.Collections.Generic;
using UnityEngine;

namespace MeridianWorks
{

    internal static class AiEnvelope
    {

        private const float MaxAltitude = 100000f;

        private const float AirFloor = 2f;

        internal static void Apply(IList<WeaponMount> mounts, IList<MissileDefinition> defs)
        {
            var done = new HashSet<WeaponInfo>();
            int fixedCount = 0;

            foreach (WeaponMount mount in mounts)
            {
                if (mount == null || mount.info == null) continue;
                if (!PluginInfo.IsOurMountKey(mount.jsonKey)) continue;
                if (Repair(mount.info, mount.jsonKey, done)) fixedCount++;
            }

            foreach (MissileDefinition def in defs)
            {
                if (def == null) continue;
                if (!PluginInfo.IsOurMissileKey(def.jsonKey)) continue;

                Missile? missile = MissileOn(def);
                WeaponInfo? info = missile != null ? missile.GetWeaponInfo() : null;
                if (info == null) continue;
                if (Repair(info, def.jsonKey, done)) fixedCount++;
            }

            if (fixedCount > 0)
                Plugin.Log.LogInfo(
                    "[Meridian] AI envelope: " + fixedCount + " WeaponInfo(s) shipped with "
                    + "targetRequirements.maxAltitude at 0, which makes CombatAI.AnalyzeTarget "
                    + "score every AIRBORNE target as zero opportunity - the reason AI flights "
                    + "never fired this pack's air-to-air rounds. Raised to " + MaxAltitude
                    + " m. The generator now writes the authored value, so this repair retires "
                    + "itself on the next bundle.");
            else
                Plugin.Diag(
                    "[Meridian] AI envelope: every WeaponInfo already carries a non-zero "
                    + "maxAltitude, so nothing was repaired.");
        }

        private static bool Repair(WeaponInfo info, string? key, HashSet<WeaponInfo> done)
        {
            if (!done.Add(info)) return false;
            if (info.targetRequirements.maxAltitude > 0f) return false;

            info.targetRequirements.maxAltitude = MaxAltitude;

            if (info.targetRequirements.minAltitude <= 0f && info.effectiveness.antiAir > 0.5f)
                info.targetRequirements.minAltitude = AirFloor;

            Plugin.Diag(
                "[Meridian] AI envelope: '" + (key ?? "?") + "' maxAltitude 0 -> " + MaxAltitude
                + " m, minAltitude " + info.targetRequirements.minAltitude.ToString("0.##") + " m.");
            return true;
        }

        private static Missile? MissileOn(MissileDefinition def)
        {
            GameObject? prefab = def.unitPrefab;
            return prefab != null ? prefab.GetComponent<Missile>() : null;
        }
    }
}
