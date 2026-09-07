using System;
using System.Collections.Generic;

namespace MeridianWorks
{

    internal static class PluginInfo
    {

        internal const string GUID = "com.meridianworks";
        internal const string Name = "Meridian Works";

        internal const string Version = "1.0.1.0";

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
                       new[] {
                                 "single", "x2", "internal", "internalx2",
                                 "internalx4", "internalx6" }, 0.192f),
            new Weapon("AGM-57L", "MeridianAGM57L", "AGM-57L",
                       new[] { "single", "x2", "triple", "internal" }, 0.175f),
            new Weapon("AGM-33L", "MeridianAGM33L", "AGM-33L",
                       new[] {
                                 "single", "double_compact", "triple", "internal",
                                 "internalx2" }, 0.139f),

            new Weapon("AAM-41 Gram",     "MeridianAAM41",  "AAM-41 Gram",     new[] {
                                 "single", "internal", "double_compact", "internalx2",
                                 "internalx3", "internalx4", "internalx6", "triple",
                                 "internalx8" }, 0.126f),
            new Weapon("SRM-8 Kukri",     "MeridianSRM8",   "SRM-8 Kukri",     new[] {
                                 "single", "internal", "double_compact", "internalx2",
                                 "internalx4", "internalx6" }, 0.095f),
            new Weapon("IRM-L7",          "MeridianIRML7",  "IRM-L7",          new[] {
                                 "single", "internal", "x2", "internalx2",
                                 "internalx4", "internalx6", "internalx6_tight" }, 0.133f),
            new Weapon("AAM-63 Falchion", "MeridianAAM63",  "AAM-63 Falchion", new[] {
                                 "single", "internal", "double_compact", "triple",
                                 "internalx2", "internalx3", "internalx4", "internalx6",
                                 "internalx8" }, 0.106f),
            new Weapon("ARAD-72",         "MeridianARAD72", "ARAD-72",         new[] { "single", "internal", "internalx2", "internalx4" }, 0.180f),
            new Weapon("GBO-900",         "MeridianGBO900", "GBO-900",         new[] { "single", "internal", "internalx2" }, 0.232f),

            new Weapon("AGM-92",          "MeridianAGM92",  "AGM-92",          new[] { "single", "internal" }, 0.234f),
            new Weapon("GBP-500 Bodkin",  "MeridianGBP500", "GBP-500 Bodkin",  new[] {
                                 "single", "internal", "x2", "internalx2",
                                 "internalx4", "internalx6", "internalx6_flat", "internalx18" }, 0.189f),
        };

        internal static readonly string[] ArchivedMountKeys =
        {

            "MeridianAGM33L_triple",
            "MeridianAGM57L_triple",
            "MeridianAAM41_triple",
            "MeridianSRM8_internalx4",
            "MeridianSRM8_internalx6",
            "MeridianIRML7_internal",
            "MeridianIRML7_internalx2",
            "MeridianIRML7_internalx4",
            "MeridianAGM84_internalx6",
            "MeridianAAM41_internalx4",
            "MeridianAAM41_internalx6",
            "MeridianAAM63_internalx4",
            "MeridianAAM63_internalx6",
            "MeridianAGM84_internal",
            "MeridianARAD72_internal",
            "MeridianGBO900_internalx2",
            "MeridianGBP500_internalx4",
            "MeridianGBP500_x2",
        };

        internal static readonly string[] UnderStubMountKeys =
        {
            "MeridianAGM33L_double_compact",
        };

        internal static bool HangsUnderStub(string? key) =>
            !string.IsNullOrEmpty(key)
            && (Array.IndexOf(UnderStubMountKeys, key) >= 0
                || key!.EndsWith("_single", StringComparison.Ordinal));

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

        internal static readonly HashSet<string> TerminalDenialKeys =
            new HashSet<string> { "MeridianIRML7_Missile" };

        internal static bool IsOurWeaponName(string? name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            foreach (Weapon w in Weapons)
                if (w.WeaponName == name) return true;
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

        internal static readonly Dictionary<string, float> TurnRates = new Dictionary<string, float>
        {
            { "MeridianAGM84_Missile", 20f },
            { "MeridianAGM57L_Missile", 20f },
            { "MeridianAGM33L_Missile", 20f },
            { "MeridianAAM41_Missile", 45f },
            { "MeridianSRM8_Missile", 230f },
            { "MeridianIRML7_Missile", 35f },
            { "MeridianAAM63_Missile", 55f },
            { "MeridianARAD72_Missile", 25f },
            { "MeridianGBO900_Missile", 15f },
            { "MeridianAGM92_Missile", 15f },
            { "MeridianGBP500_Missile", 12f },
        };

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
