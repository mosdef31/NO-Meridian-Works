using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
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

        private const float BlastYield = 20f;
        private const float PierceDamage = 250f;

        private const float MaxRange = 6000f;

        private const float FinArea = 0.13f;
        private const float Torque = 4.0f;

        private static readonly Vector3 Pid = new Vector3(1.3f, 0f, 0.35f);

        private const float FlareRejection = 1.0f;

        private const float PositionalError = 0.2f;

        private const float DriftRate = 10f;

        private const float MaxLead = 6f;

        private const float GuidanceDelay = 0.03f;

        private const float TangibleDelay = 0.1f;

        private const float SelfDestructAtSpeed = 150f;

        private const float TargetMaxAltitude = 100000f;
        private const float TargetMaxSpeed = 1200f;
        private const float TargetMinAlignment = 45f;
        private const float TargetMinRange = 200f;

        private const float TargetMinIR = 0.01f;

        private const float LengthScale = 1.1f;

        private static bool _built;
        private static ScriptableObject? _def;
        private static ScriptableObject? _mount;
        private static ScriptableObject? _mountX12;

        private static WeaponInfo? _info;
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
                + "Encyclopedia, so there is nothing to borrow.");
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

                _def = CloneMissile((ScriptableObject)donorDef, enc);
                _mount = CloneMount((ScriptableObject)donorMount, MountKey, "x4");

                object? donorMountX12 = FindDonorMount(enc, DonorMountX12Key);
                if (donorMountX12 != null)
                    _mountX12 = CloneMount((ScriptableObject)donorMountX12, MountKeyX12, "x12");
                else
                    Plugin.Log.LogWarning(
                        "[Meridian] AGR-40 Hairpin: no stock '" + DonorMountX12Key + "' pod in "
                        + "this Encyclopedia, so only the x4 fitting is registered.");

                Plugin.Diag("[Meridian] AGR-40 Hairpin: both fittings share one WeaponInfo "
                            + $"('{(_info == null ? "none" : _info.name)}'), so the x4 and the x12 "
                            + "merge into a single weapon station.");

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
                _mountX12 = null;
                _info = null;
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

        private static ScriptableObject? CloneMissile(ScriptableObject donor, Encyclopedia enc)
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

                    Tune(missile);
                }

                SwapToIrSeeker(prefab, enc);
                Lengthen(prefab);

                fPrefab.SetValue(def, prefab);
                _prefab = prefab;
            }

            return def;
        }

        private static void Tune(Missile missile)
        {
            AccessTools.Field(typeof(Missile), "finArea")?.SetValue(missile, FinArea);
            AccessTools.Field(typeof(Missile), "torque")?.SetValue(missile, Torque);

            FieldInfo? fPid = AccessTools.Field(typeof(Missile), "PIDFactors");
            if (fPid != null)
            {
                try
                {
                    object? factors = NewPidFactors(fPid.FieldType);
                    if (factors != null)
                    {
                        fPid.SetValue(missile, factors);
                    }
                    else
                    {
                        Plugin.Log.LogWarning("[Meridian] AGR-40 Hairpin: could not build a "
                            + $"{fPid.FieldType.Name}, so the round keeps the Kingpin's PID. "
                            + "Handling is stock; the weapon is otherwise fine.");
                    }
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogWarning("[Meridian] AGR-40 Hairpin: PID tune skipped, "
                        + $"{ex.Message}. The round keeps the Kingpin's PID.");
                }
            }

            Plugin.Diag($"[Meridian] AGR-40 Hairpin: finArea {FinArea}, torque {Torque}, "
                        + $"PID {Pid} - against the Kingpin's 0.07, 0.5 and (1, 0, 0.6).");
        }

        private static object? NewPidFactors(Type type)
        {
            ConstructorInfo? ctor = type.GetConstructor(
                new[] { typeof(float), typeof(float), typeof(float) });
            if (ctor != null)
            {
                return ctor.Invoke(new object[] { Pid.x, Pid.y, Pid.z });
            }

            FieldInfo? fVector = AccessTools.Field(type, "PID");
            if (fVector == null)
            {
                return null;
            }

            object bare = FormatterServices.GetUninitializedObject(type);
            fVector.SetValue(bare, Pid);
            return bare;
        }

        private static void SwapToIrSeeker(GameObject prefab, Encyclopedia enc)
        {
            var missile = prefab.GetComponent<Missile>();
            if (missile == null)
            {
                Plugin.Log.LogWarning(
                    "[Meridian] AGR-40 Hairpin: the borrowed prefab carries no Missile, so the "
                    + "IR seeker was not fitted. The round keeps whatever guidance it had.");
                return;
            }

            IRSeeker? template = FindIrSeekerTemplate(enc);
            if (template == null)
            {
                Plugin.Log.LogWarning(
                    "[Meridian] AGR-40 Hairpin: no stock round in this Encyclopedia carries an "
                    + "IRSeeker, so there is nothing to copy a seeker off. The round keeps the "
                    + "Kingpin's laser guidance.");
                return;
            }

            foreach (MissileSeeker old in prefab.GetComponentsInChildren<MissileSeeker>(true))
            {
                if (old != null) UnityEngine.Object.DestroyImmediate(old);
            }

            var seeker = prefab.AddComponent<IRSeeker>();

            foreach (FieldInfo f in AccessTools.GetDeclaredFields(typeof(IRSeeker)))
            {
                if (f.IsStatic) continue;
                try { f.SetValue(seeker, f.GetValue(template)); }
                catch (Exception ex)
                {
                    Plugin.Diag($"[Meridian] AGR-40 Hairpin: seeker field '{f.Name}' not copied, "
                                + ex.Message + ".");
                }
            }

            AccessTools.Field(typeof(MissileSeeker), "missile")?.SetValue(seeker, missile);
            seeker.triggerMissileWarning = true;

            seeker.proximityFuse = false;

            AccessTools.Field(typeof(IRSeeker), "flareRejection")?.SetValue(seeker, FlareRejection);
            AccessTools.Field(typeof(IRSeeker), "positionalError")?.SetValue(seeker, PositionalError);
            AccessTools.Field(typeof(IRSeeker), "driftRate")?.SetValue(seeker, DriftRate);
            AccessTools.Field(typeof(IRSeeker), "maxLead")?.SetValue(seeker, MaxLead);
            AccessTools.Field(typeof(IRSeeker), "guidanceDelay")?.SetValue(seeker, GuidanceDelay);
            AccessTools.Field(typeof(IRSeeker), "tangibleDelay")?.SetValue(seeker, TangibleDelay);
            AccessTools.Field(typeof(IRSeeker), "selfDestructAtSpeed")?.SetValue(
                seeker, SelfDestructAtSpeed);

            Plugin.Diag(
                $"[Meridian] AGR-40 Hairpin: IR seeker fitted off '{template.name}'. Flare "
                + $"rejection {FlareRejection}, aim error {PositionalError} m, drift {DriftRate} "
                + $"m/s, lead {MaxLead} s, guidance after {GuidanceDelay} s, impact fuse only.");
        }

        private static IRSeeker? FindIrSeekerTemplate(Encyclopedia enc)
        {
            if (enc.missiles == null) return null;

            foreach (MissileDefinition d in enc.missiles)
            {
                if (d == null || d.jsonKey == MissileKey) continue;

                GameObject? p = AccessTools.Field(d.GetType(), "unitPrefab")?.GetValue(d)
                                as GameObject;
                if (p == null) continue;

                IRSeeker? found = p.GetComponentInChildren<IRSeeker>(true);
                if (found != null) return found;
            }

            return null;
        }

        private static void Lengthen(GameObject prefab)
        {
            Vector3 scale = prefab.transform.localScale;
            prefab.transform.localScale = new Vector3(scale.x, scale.y, scale.z * LengthScale);
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
            WeaponInfo? info = _info;

            if (info != null)
            {
                fInfo?.SetValue(mount, info);
            }
            else if (fInfo?.GetValue(mount) is WeaponInfo donorInfo)
            {
                info = UnityEngine.Object.Instantiate(donorInfo);
                info.name = MissileKey + "_WeaponInfo";
                UnityEngine.Object.DontDestroyOnLoad(info);

                info.weaponName = Designation;
                info.shortName = ShortName;

                info.description =
                    "The AGR-40 is an infrared kinetic interceptor. A heat seeking "
                    + "guidance section between the motor and the warhead turns an "
                    + "ordinary rocket into a short range counter to incoming munitions "
                    + "and light aircraft, detonating on contact. The seeker head is "
                    + "cheap and is easily decoyed by flares.";
                info.massPerRound = MassPerRound;
                info.costPerRound = CostPerRound;
                info.armorTierEffectiveness = ArmorTierEffectiveness;

                info.laserGuided = false;
                info.missile = true;

                info.effectiveness.antiSurface = 0.15f;
                info.effectiveness.antiAir = 0.6f;
                info.effectiveness.antiMissile = 0.85f;
                info.effectiveness.antiRadar = 0f;

                info.targetRequirements.maxRange = MaxRange;
                info.targetRequirements.minRange = TargetMinRange;
                info.targetRequirements.maxAltitude = TargetMaxAltitude;
                info.targetRequirements.maxSpeed = TargetMaxSpeed;
                info.targetRequirements.minAlignment = TargetMinAlignment;
                info.targetRequirements.minIR = TargetMinIR;
                if (_prefab != null) info.weaponPrefab = _prefab;

                fInfo.SetValue(mount, info);
                _info = info;
            }

            FieldInfo? fStore = AccessTools.Field(mount.GetType(), "prefab");
            if (fStore?.GetValue(mount) is GameObject donorStore && info != null)
            {
                GameObject store = UnityEngine.Object.Instantiate(donorStore, Park());
                store.name = ourKey;
                UnityEngine.Object.DontDestroyOnLoad(store);

                foreach (Weapon wpn in store.GetComponentsInChildren<Weapon>(true))
                    wpn.info = info;

                Lengthen(store);

                fStore.SetValue(mount, store);
            }

            return mount;
        }

        internal static int LastSetsExamined { get; private set; }

        internal static int LastKingpinSets { get; private set; }

        internal static int Place()
        {
            int sets = 0;
            int kingpinSets = 0;
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

                    if (anyKingpin) kingpinSets++;

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

            LastSetsExamined = sets;
            LastKingpinSets = kingpinSets;

            if (addedX4 > 0 || addedX12 > 0)
                Plugin.Diag($"[Meridian] AGR-40 Hairpin: x4 placed on {addedX4} hardpoint set(s) "
                            + $"wherever any {DonorMountPrefix} sits, x12 on {addedX12} wherever a "
                            + $"{DonorMountX12Key} sits, out of {sets} set(s) examined, "
                            + $"{kingpinSets} of which offer a Kingpin.");
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

    internal sealed class HairpinPlacer : MonoBehaviour
    {

        private const float FastInterval = 4f;

        private const float SlowInterval = 30f;

        private const int SettledAfter = 3;

        private float _next;
        private int _lastSets = -1;
        private int _steady;

        private void Update()
        {
            if (Time.unscaledTime < _next) return;

            int added;
            try
            {
                added = HairpinPod.Place();
            }
            catch (Exception ex)
            {

                Plugin.Log.LogWarning("[Meridian] AGR-40 Hairpin: a placement walk failed, "
                                      + "and it will be retried: " + ex.Message);
                _next = Time.unscaledTime + SlowInterval;
                return;
            }

            int sets = HairpinPod.LastSetsExamined;
            if (sets != _lastSets)
            {
                if (_lastSets >= 0)
                    Plugin.Diag($"[Meridian] AGR-40 Hairpin: {sets - _lastSets} more hardpoint "
                                + $"set(s) have loaded since the last walk, {sets} visible now, "
                                + $"{HairpinPod.LastKingpinSets} of them offering a Kingpin.");
                _lastSets = sets;
                _steady = 0;
            }
            else if (added == 0)
            {
                _steady++;
            }

            _next = Time.unscaledTime + (_steady >= SettledAfter ? SlowInterval : FastInterval);
        }
    }
}
