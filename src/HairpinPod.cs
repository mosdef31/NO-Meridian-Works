using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace MeridianWorks
{
    internal static class HairpinPod
    {
        internal const string MissileKey = "MeridianAGR40_Missile";
        internal const string MountKey = "MeridianAGR40_4Pod";

        internal const string MountKeyX12 = "MeridianAGR40_12Pod";

        internal const string Designation = "AGR-40 Hairpin";
        internal const string ShortName = "AGR-40";

        private const string DonorMissileKey = "Rocket2";
        private const string DonorMountPrefix = "Rocket2_4Pod";

        private const string DonorMountX12Key = "Rocket2_4Podx3";

        private const float MassPerRound = 20f;
        private const float CostPerRound = 0.05f;
        private const float ArmorTierEffectiveness = 5.5f;

        private const float BlastYield = 5f;
        private const float PierceDamage = 700f;

        private const float MaxRange = 6000f;

        private static bool _built;
        private static ScriptableObject? _def;
        private static ScriptableObject? _mount;
        private static ScriptableObject? _mountX12;
        private static GameObject? _prefab;
        private static Transform? _park;

        private static Transform Park()
        {
            if (_park != null) return _park;

            var holder = new GameObject("MeridianWorks_HairpinPrefabs")
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            UnityEngine.Object.DontDestroyOnLoad(holder);
            holder.SetActive(false);

            _park = holder.transform;
            return _park;
        }

        internal static ScriptableObject? Definition => _def;
        internal static ScriptableObject? Mount => _mount;
        internal static ScriptableObject? MountX12 => _mountX12;

        internal static bool Build(Encyclopedia enc)
        {
            if (_built) return _def != null && _mount != null;
            _built = true;

            try
            {
                object? donorDef = FindDonorMissile(enc);
                if (donorDef == null)
                {
                    Plugin.Log.LogWarning(
                        "[Meridian] AGR-40 Hairpin: no stock AGR-24 Kingpin in this "
                        + "Encyclopedia, so there is nothing to borrow. The pod is not "
                        + "registered and nothing else is affected.");
                    return false;
                }

                object? donorMount = FindDonorMount(enc, DonorMountPrefix);
                if (donorMount == null)
                {
                    Plugin.Log.LogWarning(
                        "[Meridian] AGR-40 Hairpin: the Kingpin is present but its "
                        + $"'{DonorMountPrefix}' mount is not. Not registered.");
                    return false;
                }

                _def = CloneMissile((ScriptableObject)donorDef);
                _mount = CloneMount((ScriptableObject)donorMount, MountKey, "x4");

                object? donorMountX12 = FindDonorMount(enc, DonorMountX12Key);
                if (donorMountX12 != null)
                    _mountX12 = CloneMount((ScriptableObject)donorMountX12, MountKeyX12, "x12");
                else
                    Plugin.Log.LogWarning(
                        "[Meridian] AGR-40 Hairpin: no stock '" + DonorMountX12Key + "' pod in "
                        + "this Encyclopedia, so only the x4 fitting is registered.");

                Plugin.Diag($"[Meridian] AGR-40 Hairpin: cloned '{DonorMissileKey}', "
                            + $"'{DonorMountPrefix}' and '{DonorMountX12Key}' into '{MissileKey}', "
                            + $"'{MountKey}' and '{MountKeyX12}'"
                            + (_mountX12 == null ? " (x12 absent)" : "") + ".");
                return _def != null && _mount != null;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[Meridian] AGR-40 Hairpin build failed: {ex.Message}");
                _def = null;
                _mount = null;
                return false;
            }
        }

        private static object? FindDonorMissile(Encyclopedia enc)
        {
            if (enc.missiles == null) return null;
            foreach (MissileDefinition d in enc.missiles)
                if (d != null && d.jsonKey == DonorMissileKey) return d;
            return null;
        }

        private static object? FindDonorMount(Encyclopedia enc, string key)
        {
            if (enc.weaponMounts == null) return null;

            foreach (WeaponMount m in enc.weaponMounts)
                if (m != null && m.jsonKey == key) return m;

            foreach (WeaponMount m in enc.weaponMounts)
                if (m != null && m.jsonKey != null && m.jsonKey.StartsWith(key)
                    && m.jsonKey != DonorMountX12Key && m.jsonKey != DonorMountPrefix)
                    return m;

            return null;
        }

        private static ScriptableObject? CloneMissile(ScriptableObject donor)
        {
            var def = UnityEngine.Object.Instantiate(donor);
            def.name = MissileKey;
            UnityEngine.Object.DontDestroyOnLoad(def);

            AccessTools.Field(def.GetType(), "jsonKey")?.SetValue(def, MissileKey);

            FieldInfo? fPrefab = AccessTools.Field(def.GetType(), "unitPrefab");
            if (fPrefab?.GetValue(def) is GameObject donorPrefab)
            {
                GameObject prefab = UnityEngine.Object.Instantiate(donorPrefab, Park());
                prefab.name = MissileKey;
                UnityEngine.Object.DontDestroyOnLoad(prefab);

                var missile = prefab.GetComponent<Missile>();
                if (missile != null)
                {
                    AccessTools.Field(typeof(Missile), "blastYield")?.SetValue(missile, BlastYield);
                    AccessTools.Field(typeof(Missile), "pierceDamage")?.SetValue(missile, PierceDamage);
                    AccessTools.Field(typeof(Missile), "definition")?.SetValue(missile, def);
                }

                fPrefab.SetValue(def, prefab);
                _prefab = prefab;
            }

            return def;
        }

        private static ScriptableObject? CloneMount(ScriptableObject donor, string ourKey,
                                                   string fittingLabel)
        {
            var mount = UnityEngine.Object.Instantiate(donor);
            mount.name = ourKey;
            UnityEngine.Object.DontDestroyOnLoad(mount);

            AccessTools.Field(mount.GetType(), "jsonKey")?.SetValue(mount, ourKey);
            AccessTools.Field(mount.GetType(), "mountName")?.SetValue(
                mount, Designation + " " + fittingLabel);

            FieldInfo? fInfo = AccessTools.Field(mount.GetType(), "info");
            WeaponInfo? info = null;

            if (fInfo?.GetValue(mount) is WeaponInfo donorInfo)
            {
                info = UnityEngine.Object.Instantiate(donorInfo);
                info.name = MissileKey + "_WeaponInfo";
                UnityEngine.Object.DontDestroyOnLoad(info);

                info.weaponName = Designation;
                info.shortName = ShortName;
                info.description =
                    "The AGR-40 is a laser guided rocket. A guidance section between the "
                    + "motor and the warhead turns an ordinary rocket into a precise one, "
                    + "so a pod of four can be put onto small targets one after another "
                    + "for very little money. It hits softer than a missile and it is not "
                    + "meant to reach as far.";
                info.massPerRound = MassPerRound;
                info.costPerRound = CostPerRound;
                info.armorTierEffectiveness = ArmorTierEffectiveness;
                info.targetRequirements.maxRange = MaxRange;
                if (_prefab != null) info.weaponPrefab = _prefab;

                fInfo.SetValue(mount, info);
            }

            FieldInfo? fStore = AccessTools.Field(mount.GetType(), "prefab");
            if (fStore?.GetValue(mount) is GameObject donorStore && info != null)
            {
                GameObject store = UnityEngine.Object.Instantiate(donorStore, Park());
                store.name = ourKey;
                UnityEngine.Object.DontDestroyOnLoad(store);

                foreach (Weapon wpn in store.GetComponentsInChildren<Weapon>(true))
                    wpn.info = info;

                fStore.SetValue(mount, store);
            }

            return mount;
        }

        internal static int Place()
        {
            int sets = 0;
            int addedX4 = 0;
            int addedX12 = 0;

            foreach (WeaponManager wm in Resources.FindObjectsOfTypeAll<WeaponManager>())
            {
                if (wm == null) continue;
                if (wm.name != null && wm.name.IndexOf("UFO", StringComparison.OrdinalIgnoreCase) >= 0)
                    continue;

                foreach (var set in HardpointSets(wm))
                {
                    if (set == null) continue;
                    sets++;

                    bool anyKingpin = false;
                    bool bigKingpin = false;
                    bool hasX4 = false;
                    bool hasX12 = false;

                    foreach (WeaponMount m in set)
                    {
                        if (m == null || m.jsonKey == null) continue;
                        if (m.jsonKey == MountKey) hasX4 = true;
                        else if (m.jsonKey == MountKeyX12) hasX12 = true;
                        else if (m.jsonKey == DonorMountX12Key) { anyKingpin = true; bigKingpin = true; }
                        else if (m.jsonKey.StartsWith(DonorMountPrefix)) anyKingpin = true;
                    }

                    if (_mount != null && anyKingpin && !hasX4)
                    {
                        set.Add((WeaponMount)(object)_mount);
                        addedX4++;
                    }

                    if (_mountX12 != null && bigKingpin && !hasX12)
                    {
                        set.Add((WeaponMount)(object)_mountX12);
                        addedX12++;
                    }
                }
            }

            Plugin.Diag($"[Meridian] AGR-40 Hairpin: x4 placed on {addedX4} hardpoint set(s) "
                        + $"wherever any {DonorMountPrefix} sits, x12 on {addedX12} wherever a "
                        + $"{DonorMountX12Key} sits, out of {sets} set(s) examined.");
            return addedX4 + addedX12;
        }

        private static IEnumerable<List<WeaponMount>> HardpointSets(WeaponManager wm)
        {
            HardpointSet[] sets = wm.hardpointSets;
            if (sets == null) yield break;

            foreach (HardpointSet set in sets)
            {
                if (set == null || set.weaponOptions == null) continue;
                yield return set.weaponOptions;
            }
        }
    }
}
