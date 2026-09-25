using System;
using System.Collections.Generic;

namespace MeridianWorks
{

    internal static class NameApply
    {
        private static bool _ran;

        internal static void Apply(IList<WeaponMount> mounts)
        {
            if (_ran) return;
            _ran = true;

            var done = new HashSet<WeaponInfo>();
            int changed = 0;

            foreach (WeaponMount mount in mounts)
            {
                if (mount == null || mount.info == null) continue;

                PluginInfo.Weapon? owner = PluginInfo.WeaponForMountKey(mount.jsonKey);
                if (owner == null) continue;
                if (!done.Add(mount.info)) continue;

                string want = owner.WeaponName;
                string wantShort = ShortOf(want);

                if (mount.info.weaponName == want && mount.info.shortName == wantShort) continue;

                Plugin.Log.LogInfo(
                    "[Meridian] Name: '" + mount.info.weaponName + "' / '" + mount.info.shortName
                    + "' in the bundle, '" + want + "' / '" + wantShort + "' in the key table. "
                    + "Renamed to the table. The bundle catches up on the next export; the "
                    + "jsonKey '" + mount.jsonKey + "' has not moved.");

                mount.info.weaponName = want;
                mount.info.shortName = wantShort;
                changed++;
            }

            if (changed > 0)
                Plugin.Log.LogInfo(
                    "[Meridian] Name: " + changed + " weapon(s) renamed at load because the "
                    + "bundle predates the rename. weaponName is a KEY as well as a label - "
                    + "OffBoresightCue and PylonBorrow both match on it - so leaving the two "
                    + "out of step would have stopped those patches recognising these rounds.");
        }

        private static string ShortOf(string weaponName)
        {
            if (string.IsNullOrEmpty(weaponName)) return weaponName;
            int space = weaponName.IndexOf(' ');
            return space > 0 ? weaponName.Substring(0, space) : weaponName;
        }
    }
}
