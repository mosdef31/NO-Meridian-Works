using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx;
using HarmonyLib;
using UnityEngine;

namespace MeridianWorks
{

    [HarmonyPatch(typeof(Encyclopedia), "AfterLoad", new Type[0])]
    internal static class Encyclopedia_AfterLoad_RegistrationPatch
    {
        [HarmonyPrefix]
        private static void Prefix(Encyclopedia __instance)
        {

            try
            {
                EncyclopediaRegistration.EnsureInLists(__instance);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError(
                    "[Meridian] Encyclopedia registration prefix threw (non-fatal, the pack may be " +
                    $"unregistered this run): {ex}");
            }
        }
    }

    internal static class EncyclopediaRegistration
    {
        private static readonly List<WeaponMount> _mounts = new List<WeaponMount>();
        private static readonly List<MissileDefinition> _defs = new List<MissileDefinition>();
        private static AssetBundle? _ourBundle;
        private static bool _resolveAttempted;
        private static bool _addedLogged;

        internal static IList<WeaponMount> ResolvedMounts => _mounts;

        internal static IList<MissileDefinition> ResolvedMissiles => _defs;

        private static bool _hairpinPlaced;

        internal static void EnsureInLists(Encyclopedia enc)
        {
            if (enc == null) return;
            if (!TryResolveAssets()) return;

            TurnRateCompat.Apply(_defs);

            StatOverrides.ApplyIfPresent(_defs);

            bool added = false;

            foreach (WeaponMount mount in _mounts)
            {
                if (mount == null || enc.weaponMounts == null) continue;
                if (!PluginInfo.IsOurMountKey(mount.jsonKey)) continue;
                if (ContainsMount(enc, mount)) continue;
                enc.weaponMounts.Add(mount);
                added = true;
            }

            foreach (MissileDefinition def in _defs)
            {
                if (def == null || enc.missiles == null) continue;
                if (!PluginInfo.IsOurMissileKey(def.jsonKey)) continue;
                if (ContainsMissile(enc, def)) continue;
                enc.missiles.Add(def);
                added = true;
            }

            if (HairpinPod.Build(enc))
            {
                if (HairpinPod.Definition is MissileDefinition hdef
                    && enc.missiles != null && !ContainsMissile(enc, hdef))
                {
                    enc.missiles.Add(hdef);
                    added = true;
                }

                if (HairpinPod.Mount is WeaponMount hmount
                    && enc.weaponMounts != null && !ContainsMount(enc, hmount))
                {
                    enc.weaponMounts.Add(hmount);
                    added = true;
                }

                if (HairpinPod.MountX12 is WeaponMount hmount12
                    && enc.weaponMounts != null && !ContainsMount(enc, hmount12))
                {
                    enc.weaponMounts.Add(hmount12);
                    added = true;
                }

                if (!_hairpinPlaced)
                {
                    _hairpinPlaced = true;
                    HairpinPod.Place();
                }
            }

            if (!added || _addedLogged) return;

            _addedLogged = true;
            Plugin.Diag(
                $"[Meridian] Registered into Encyclopedia - {_mounts.Count} mount(s): " +
                string.Join(", ", _mounts.Where(m => m != null).Select(m => $"'{m.jsonKey}'").ToArray()) +
                $"; {_defs.Count} missile(s): " +
                string.Join(", ", _defs.Where(d => d != null).Select(d => $"'{d.jsonKey}'").ToArray()) +
                ". AfterLoad's rebuild will index them.");
        }

        internal static bool EnsureRegisteredAndRebuild()
        {
            Encyclopedia? enc = GameData.EncyclopediaOrNull();
            if (enc == null)
            {
                Plugin.Log.LogWarning("[Meridian] No Encyclopedia instance available yet.");
                return false;
            }

            bool alreadyPresent =
                Encyclopedia.WeaponLookup != null &&
                _mounts.Count > 0 &&
                _mounts.All(m => m == null || string.IsNullOrEmpty(m.jsonKey) ||
                                 Encyclopedia.WeaponLookup.ContainsKey(m.jsonKey));

            EnsureInLists(enc);

            if (!alreadyPresent)
            {
                MethodInfo? afterLoad =
                    AccessTools.Method(typeof(Encyclopedia), "AfterLoad", Type.EmptyTypes);
                if (afterLoad != null)
                {
                    afterLoad.Invoke(enc, null);
                    Plugin.Diag("[Meridian] Forced Encyclopedia.AfterLoad() to index late registration.");
                }
                else
                {
                    Plugin.Log.LogWarning("[Meridian] Could not find Encyclopedia.AfterLoad() to force a rebuild.");
                }
            }

            bool ok = _mounts.Count > 0;
            foreach (WeaponMount mount in _mounts)
            {
                if (mount == null || string.IsNullOrEmpty(mount.jsonKey)) continue;
                bool present = Encyclopedia.WeaponLookup != null &&
                               Encyclopedia.WeaponLookup.ContainsKey(mount.jsonKey);
                if (!present)
                    Plugin.Log.LogError($"[Meridian] WeaponLookup does NOT contain '{mount.jsonKey}'.");
                ok &= present;
            }

            if (ok)
                Plugin.Diag($"[Meridian] All {_mounts.Count} mount(s) are in WeaponLookup.");
            return ok;
        }

        private static bool ContainsMount(Encyclopedia enc, WeaponMount mount) =>
            enc.weaponMounts.Any(w => w == mount ||
                (w != null && !string.IsNullOrEmpty(w.jsonKey) && w.jsonKey == mount.jsonKey));

        private static bool ContainsMissile(Encyclopedia enc, MissileDefinition def) =>
            enc.missiles.Any(m => m == def ||
                (m != null && !string.IsNullOrEmpty(m.jsonKey) && m.jsonKey == def.jsonKey));

        private static bool TryResolveAssets()
        {
            if (_mounts.Count > 0) return true;
            if (_resolveAttempted && _mounts.Count == 0) return false;
            _resolveAttempted = true;

            _ourBundle = FindOurLoadedBundle();
            if (_ourBundle != null)
            {
                _mounts.AddRange(LoadOurs<WeaponMount>(_ourBundle, PluginInfo.MountAssetFragment));
                _defs.AddRange(LoadOurs<MissileDefinition>(_ourBundle, PluginInfo.MissileDefAssetFragment));
            }

            if (_mounts.Count == 0)
            {
                AssetBundle? bundle = TryLoadBundleFromDisk();
                if (bundle != null)
                {
                    _ourBundle = bundle;
                    _mounts.AddRange(LoadOurs<WeaponMount>(bundle, PluginInfo.MountAssetFragment));
                    if (_defs.Count == 0)
                        _defs.AddRange(LoadOurs<MissileDefinition>(bundle, PluginInfo.MissileDefAssetFragment));
                    if (_mounts.Count > 0)
                        Plugin.Log.LogWarning(
                            $"[Meridian] Loaded a loose {PluginInfo.BundleName} from disk because the " +
                            "embedded bundle was not resident. NetworkIdentity PrefabHashes may be " +
                            "unassigned, which Blueprinter normally does, so multiplayer spawning could " +
                            "fail. Confirm Blueprinter is installed - and delete that loose file, " +
                            "because Blueprinter scans loose bundles BEFORE embedded ones and keeps " +
                            "whichever has the higher modVersion, so a stale copy silently wins.");
                }
            }

            if (_mounts.Count == 0)
            {
                Plugin.Log.LogError(
                    "[Meridian] Could not resolve a single WeaponMount from any loaded bundle or from " +
                    $"disk. Expected assets whose path contains '{PluginInfo.MountAssetFragment}'. " +
                    "Nothing in this pack can register.");
                DumpLoadedBundleNames();
                return false;
            }

            foreach (WeaponMount mount in _mounts) NormalizeJsonKey(mount, ref mount.jsonKey);
            foreach (MissileDefinition def in _defs) NormalizeJsonKey(def, ref def.jsonKey);

            EnsureUnitPrefabs();
            EnsureVisibleRange();
            EnsurePrefabBackLinks();
            EnsureWeaponInfoBackLink();
            PreflightMounts();
            AuditAgainstKeyTable();
            return true;
        }

        private static void EnsureUnitPrefabs()
        {
            for (int i = _defs.Count - 1; i >= 0; i--)
            {
                MissileDefinition def = _defs[i];
                if (def == null) { _defs.RemoveAt(i); continue; }
                if (def.unitPrefab != null) continue;

                GameObject? prefab = FindMissilePrefab(def.jsonKey);

                if (prefab != null && prefab.GetComponent<Unit>() == null)
                {
                    Plugin.Log.LogError(
                        $"[Meridian] '{def.jsonKey}': the prefab '{prefab.name}' has no Unit component " +
                        "(its Missile is missing), so CacheMass would throw on it exactly as a null " +
                        "prefab does. Not using it.");
                    prefab = null;
                }

                if (prefab != null)
                {
                    def.unitPrefab = prefab;
                    Plugin.Log.LogWarning(
                        $"[Meridian] '{def.jsonKey}' shipped with a NULL unitPrefab and was repaired at " +
                        $"load from the bundle's '{prefab.name}'. Encyclopedia.AfterLoad dereferences " +
                        "that field with no null check, so without this repair the game draws its main " +
                        "menu and never becomes clickable. The generator writes the field now, so a " +
                        "rebuilt bundle will not need this.");
                    continue;
                }

                _defs.RemoveAt(i);
                Plugin.Log.LogError(
                    $"[Meridian] '{def.jsonKey}' has a NULL unitPrefab and no prefab for it could be " +
                    "found in the bundle, so it is NOT being registered and that weapon will not exist " +
                    "this run. Registering it anyway would throw inside Encyclopedia.AfterLoad and stop " +
                    "the game reaching its main menu at all, which is the worse of the two.");
            }
        }

        private static void EnsurePrefabBackLinks()
        {
            foreach (MissileDefinition def in _defs)
            {
                if (def == null || def.unitPrefab == null) continue;

                var missile = def.unitPrefab.GetComponent<Missile>();
                if (missile == null) continue;
                if (missile.definition != null) continue;

                missile.definition = def;
                Plugin.Log.LogWarning(
                    $"[Meridian] '{def.jsonKey}': the flying prefab's Missile.definition was NULL and " +
                    "was pointed back at its definition. WeaponMount.Initialize reads " +
                    "info.weaponPrefab.GetComponent<Missile>().definition.mass with nothing checked, so " +
                    "without this the game draws its main menu and never becomes clickable. Fix it in " +
                    "Unity and re-export.");
            }
        }

        private static void EnsureWeaponInfoBackLink()
        {
            FieldInfo? fInfo = AccessTools.Field(typeof(Missile), "info");
            if (fInfo == null)
            {
                Plugin.Log.LogError(
                    "[Meridian] Missile.info could not be reached by reflection, so the flying prefabs " +
                    "keep a null WeaponInfo. Any laser-guided round will throw inside LaserSeeker." +
                    "Initialize and fly blind. Check whether the field has been renamed in this build.");
                return;
            }

            var done = new HashSet<GameObject>();

            foreach (WeaponMount mount in _mounts)
            {
                WeaponInfo? info = mount != null ? mount.info : null;
                if (info == null || info.weaponPrefab == null) continue;
                if (!done.Add(info.weaponPrefab)) continue;

                var missile = info.weaponPrefab.GetComponent<Missile>();
                if (missile == null) continue;
                if (fInfo.GetValue(missile) != null) continue;

                fInfo.SetValue(missile, info);
                Plugin.Log.LogWarning(
                    $"[Meridian] '{info.weaponName}': the flying prefab's Missile.info was NULL and was " +
                    "pointed back at its WeaponInfo. LaserSeeker.Initialize dereferences it with no null " +
                    "check, and the throw aborts Missile.LocalStart, which leaves the round with no " +
                    "target and undeployed fins. Fix it in Unity and re-export.");
            }
        }

        private static void PreflightMounts()
        {
            for (int i = _mounts.Count - 1; i >= 0; i--)
            {
                WeaponMount mount = _mounts[i];
                string? why = MountFault(mount);
                if (why == null) continue;

                _mounts.RemoveAt(i);
                Plugin.Log.LogError(
                    $"[Meridian] Mount '{(mount != null ? mount.jsonKey : "(null)")}' is NOT being " +
                    $"registered: {why}. WeaponMount.Initialize would throw on it inside " +
                    "Encyclopedia.AfterLoad, and that stops the game reaching a clickable main menu. " +
                    "Fix it in Unity and re-export.");
            }
        }

        private static string? MountFault(WeaponMount? mount)
        {
            if (mount == null) return "the asset is null";
            if (mount.prefab == null) return "WeaponMount.prefab is null";
            if (mount.info == null) return "WeaponMount.info is null";

            if (mount.info.weaponPrefab == null) return null;

            var missile = mount.info.weaponPrefab.GetComponent<Missile>();
            if (missile == null)
                return $"WeaponInfo.weaponPrefab '{mount.info.weaponPrefab.name}' has no Missile component";
            if (missile.definition == null)
                return $"the Missile on '{mount.info.weaponPrefab.name}' has a null definition";

            return null;
        }

        private static void EnsureVisibleRange()
        {
            foreach (MissileDefinition def in _defs)
            {
                if (def == null || def.visibleRange > 0f) continue;

                def.visibleRange = PluginInfo.FallbackVisibleRangeMetres;
                Plugin.Log.LogWarning(
                    $"[Meridian] '{def.jsonKey}' shipped with visibleRange 0, so the round would never " +
                    $"be drawn as a contact. Set to {PluginInfo.FallbackVisibleRangeMetres:0} m, which " +
                    "is what the stock guided AGMs carry. Fix it in Unity and re-export.");
            }
        }

        private static GameObject? FindMissilePrefab(string? missileKey)
        {
            if (_ourBundle == null || string.IsNullOrEmpty(missileKey)) return null;

            string[] names;
            try { names = _ourBundle.GetAllAssetNames(); }
            catch { return null; }

            string wanted = "/" + missileKey!.Trim().ToLowerInvariant() + ".prefab";
            foreach (string name in names)
            {
                if (!name.EndsWith(wanted, StringComparison.OrdinalIgnoreCase)) continue;
                return _ourBundle.LoadAsset<GameObject>(name);
            }
            return null;
        }

        private static void AuditAgainstKeyTable()
        {
            var haveMounts = new HashSet<string>(
                _mounts.Where(m => m != null && !string.IsNullOrEmpty(m.jsonKey)).Select(m => m.jsonKey));
            var haveDefs = new HashSet<string>(
                _defs.Where(d => d != null && !string.IsNullOrEmpty(d.jsonKey)).Select(d => d.jsonKey));

            foreach (PluginInfo.Weapon w in PluginInfo.Weapons)
            {
                foreach (string key in w.MountKeys)
                {
                    if (haveMounts.Contains(key)) continue;
                    Plugin.Log.LogError(
                        $"[Meridian] {w.Designation}: the bundle has no WeaponMount with jsonKey " +
                        $"'{key}'. That rack will not appear in any loadout, and nothing else will " +
                        "say so. Tick the WeaponMount assets into the bundle FIRST - nothing " +
                        "references them, so nothing drags them in as a dependency.");
                }

                if (!haveDefs.Contains(w.MissileKey))
                    Plugin.Log.LogError(
                        $"[Meridian] {w.Designation}: the bundle has no MissileDefinition with jsonKey " +
                        $"'{w.MissileKey}'. The round has no Encyclopedia entry.");
            }

            foreach (string key in haveMounts.Where(k => !PluginInfo.IsOurMountKey(k))
                                             .Concat(haveDefs.Where(k => !PluginInfo.IsOurMissileKey(k))))
                Plugin.Log.LogError(
                    $"[Meridian] Resolved an asset keyed '{key}' that is NOT ours. The bundle and key " +
                    "filters in LoadOurs are not doing their job; registering another mod's asset can " +
                    "duplicate a key and throw inside Encyclopedia.AfterLoad, which takes every weapon " +
                    "in the game down with it.");

            var seen = new HashSet<string>();
            foreach (WeaponMount mount in _mounts)
            {
                string key = mount.jsonKey ?? "";
                if (!string.IsNullOrEmpty(key) && !seen.Add(key))
                    Plugin.Log.LogError(
                        $"[Meridian] Two WeaponMounts in the bundle share the jsonKey '{key}'. " +
                        "Encyclopedia.AfterLoad adds them to a dictionary by key and will throw on the " +
                        "second, taking every weapon in the game with it. Fix one in Unity.");
            }

            Plugin.Log.LogInfo(
                $"[Meridian] Bundle audit: {_mounts.Count} of {PluginInfo.ExpectedMountCount} expected " +
                $"mount(s), {_defs.Count} of {PluginInfo.Weapons.Length} expected missile definition(s).");
        }

        private static void NormalizeJsonKey(UnityEngine.Object asset, ref string key)
        {
            if (string.IsNullOrEmpty(key)) return;
            string trimmed = key.Trim();
            if (trimmed == key) return;

            Plugin.Log.LogWarning(
                $"[Meridian] Asset '{asset.name}' has a jsonKey with stray whitespace: '{key}' -> " +
                $"'{trimmed}'. Trimming it so Encyclopedia lookups match. Fix it at the source in " +
                "Unity and re-export the bundle.");
            key = trimmed;
        }

        private static AssetBundle? FindOurLoadedBundle()
        {
            AssetBundle? byContent = null;

            foreach (AssetBundle bundle in AssetBundle.GetAllLoadedAssetBundles())
            {
                if (bundle == null || bundle.isStreamedSceneAssetBundle) continue;

                if (bundle.name != null &&
                    bundle.name.IndexOf("meridianworks", StringComparison.OrdinalIgnoreCase) >= 0)
                    return bundle;

                if (byContent != null) continue;

                string[] names;
                try { names = bundle.GetAllAssetNames(); }
                catch { continue; }

                foreach (string name in names)
                {
                    foreach (PluginInfo.Weapon w in PluginInfo.Weapons)
                    {
                        if (name.IndexOf(w.JsonKey, StringComparison.OrdinalIgnoreCase) < 0) continue;
                        byContent = bundle;
                        break;
                    }
                    if (byContent != null) break;
                }
            }

            return byContent;
        }

        private static IEnumerable<T> LoadOurs<T>(AssetBundle bundle, string nameFragment)
            where T : UnityEngine.Object
        {
            var found = new List<T>();

            string[] names;
            try { names = bundle.GetAllAssetNames(); }
            catch { return found; }

            foreach (string name in names)
            {
                if (name.IndexOf(nameFragment, StringComparison.OrdinalIgnoreCase) < 0) continue;

                var asset = bundle.LoadAsset<T>(name);
                if (asset == null) continue;

                string? key = (asset as WeaponMount)?.jsonKey
                              ?? (asset as MissileDefinition)?.jsonKey;
                string trimmed = key?.Trim() ?? "";

                if (PluginInfo.IsOurMountKey(trimmed) || PluginInfo.IsOurMissileKey(trimmed))
                {
                    found.Add(asset);
                    continue;
                }

                Plugin.Log.LogWarning(
                    $"[Meridian] Skipped '{name}' (key '{trimmed}') from bundle '{bundle.name}': it is " +
                    "not in the key table in PluginInfo.cs, so it is not ours to register. If this IS " +
                    "one of ours the generator's naming moved and the table needs updating.");
            }

            return found;
        }

        private static AssetBundle? TryLoadBundleFromDisk()
        {
            try
            {
                string root = Paths.PluginPath;
                string? file = Directory
                    .EnumerateFiles(root, "*.nobp", SearchOption.AllDirectories)
                    .FirstOrDefault(p => Path.GetFileName(p)
                        .IndexOf("meridianworks", StringComparison.OrdinalIgnoreCase) >= 0);

                if (file == null)
                {
                    Plugin.Log.LogWarning($"[Meridian] No {PluginInfo.BundleName} found under '{root}'.");
                    return null;
                }
                return AssetBundle.LoadFromFile(file);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[Meridian] Disk bundle load failed: {ex.Message}");
                return null;
            }
        }

        private static void DumpLoadedBundleNames()
        {
            foreach (AssetBundle bundle in AssetBundle.GetAllLoadedAssetBundles())
            {
                if (bundle == null) continue;
                int count;
                try { count = bundle.GetAllAssetNames().Length; }
                catch { continue; }
                Plugin.Diag($"[Meridian]   loaded bundle '{bundle.name}' with {count} asset(s).");
            }
        }
    }
}
