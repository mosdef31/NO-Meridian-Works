using System;

namespace MeridianWorks
{

    internal static class PluginInfo
    {

        internal const string GUID = "com.meridianworks";
        internal const string Name = "Meridian Works";

        internal const string Version = "1.0.0";

        internal const string BlueprinterGUID = "com.nikkorap.blueprinter";

        internal const string BundleName = "meridianworks.nobp";

        internal const float FallbackVisibleRangeMetres = 2000f;

        internal const string MountAssetFragment = "_weaponmount";
        internal const string MissileDefAssetFragment = "_missiledefinition";

        internal sealed class Weapon
        {
            internal Weapon(string designation, string jsonKey, string weaponName, string[] rackSuffixes,
                            float bodyRadiusM)
            {
                Designation = designation;
                JsonKey = jsonKey;
                WeaponName = weaponName;
                RackSuffixes = rackSuffixes;
                BodyRadiusM = bodyRadiusM;
            }

            internal string Designation { get; }

            internal string JsonKey { get; }

            internal string WeaponName { get; }

            internal string[] RackSuffixes { get; }

            internal float BodyRadiusM { get; }

            internal string MissileKey => JsonKey + "_Missile";

            internal string[] MountKeys
            {
                get
                {
                    var keys = new string[RackSuffixes.Length];
                    for (int i = 0; i < keys.Length; i++) keys[i] = JsonKey + "_" + RackSuffixes[i];
                    return keys;
                }
            }
        }

        internal static readonly Weapon[] Weapons =
        {
            new Weapon("AGM-84",  "MeridianAGM84",  "AGM-84",
                       new[] { "single", "x2", "internal", "internalx2", "internalx4", "internalx6" }, 0.192f),
            new Weapon("AGM-57L", "MeridianAGM57L", "AGM-57L",
                       new[] { "single", "x2", "triple", "internal" }, 0.175f),
            new Weapon("AGM-33L", "MeridianAGM33L", "AGM-33L",
                       new[] { "single", "double_compact", "triple", "internal", "internalx2" }, 0.139f),
        };

        internal static readonly string[] ArchivedMountKeys =
        {
            "MeridianAGM57L_triple",

            "MeridianAGM84_internalx2",
        };

        internal static bool IsArchivedMountKey(string? key) =>
            !string.IsNullOrEmpty(key) && Array.IndexOf(ArchivedMountKeys, key) >= 0;

        internal static bool IsOurMountKey(string? key)
        {
            if (string.IsNullOrEmpty(key)) return false;
            foreach (Weapon w in Weapons)
                foreach (string k in w.MountKeys)
                    if (k == key) return true;
            return false;
        }

        internal static bool IsOurMissileKey(string? key)
        {
            if (string.IsNullOrEmpty(key)) return false;
            foreach (Weapon w in Weapons)
                if (w.MissileKey == key) return true;
            return false;
        }

        internal static Weapon? WeaponForMountKey(string? key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            foreach (Weapon w in Weapons)
                foreach (string k in w.MountKeys)
                    if (k == key) return w;
            return null;
        }

        internal static Weapon? WeaponForMissileKey(string? key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            foreach (Weapon w in Weapons)
                if (w.MissileKey == key) return w;
            return null;
        }

        internal static int ExpectedMountCount
        {
            get
            {
                int n = 0;
                foreach (Weapon w in Weapons) n += w.RackSuffixes.Length;
                return n;
            }
        }
    }
}
