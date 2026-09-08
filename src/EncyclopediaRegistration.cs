using System;
using System.Collections.Generic;
using System.Diagnostics;
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

        private static int _resolveAttempts;

        private static bool _resolveFailureLogged;
        private static bool _addedLogged;

        internal static IList<WeaponMount> ResolvedMounts => _mounts;

        internal static IList<MissileDefinition> ResolvedMissiles => _defs;

        private static readonly List<WeaponMount> _extraMounts = new List<WeaponMount>();
        private static readonly List<MissileDefinition> _extraDefs = new List<MissileDefinition>();

        internal static IList<WeaponMount> ExtraMounts => _extraMounts;

        internal static IList<MissileDefinition> ExtraMissiles => _extraDefs;

        internal static IEnumerable<WeaponMount> AllOurMounts()
        {
            foreach (WeaponMount m in _mounts) yield return m;
            foreach (WeaponMount m in _extraMounts) yield return m;
        }

        internal static IEnumerable<MissileDefinition> AllOurMissiles()
        {
            foreach (MissileDefinition d in _defs) yield return d;
            foreach (MissileDefinition d in _extraDefs) yield return d;
        }

        internal static void EnsureInLists(Encyclopedia enc)
        {
            if (enc == null) return;
            if (!TryResolveAssets()) return;

            TurnRateCompat.Apply(_defs);

            StatOverrides.ApplyIfPresent(_defs);

            ShaderRebind.Apply(_defs, _mounts);

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
                if (HairpinPod.Definition is MissileDefinition hdef)
                {
                    if (!_extraDefs.Contains(hdef)) _extraDefs.Add(hdef);
                    if (enc.missiles != null && !ContainsMissile(enc, hdef))
                    {
                        enc.missiles.Add(hdef);
                        added = true;
                    }
                }

                if (HairpinPod.Mount is WeaponMount hmount)
                {
                    if (!_extraMounts.Contains(hmount)) _extraMounts.Add(hmount);
                    if (enc.weaponMounts != null && !ContainsMount(enc, hmount))
                    {
                        enc.weaponMounts.Add(hmount);
                        added = true;
                    }
                }

                if (HairpinPod.MountX12 is WeaponMount hmount12)
                {
                    if (!_extraMounts.Contains(hmount12)) _extraMounts.Add(hmount12);
                    if (enc.weaponMounts != null && !ContainsMount(enc, hmount12))
                    {
                        enc.weaponMounts.Add(hmount12);
                        added = true;
                    }
                }

                HairpinPod.Place();
            }

            if (!added || _addedLogged) return;

            _addedLogged = true;
            Plugin.Diag(
                $"[Meridian] Registered into Encyclopedia - {_mounts.Count} mount(s): " +
                string.Join(", ", _mounts.Where(m => m != null).Select(m => $"'{m.jsonKey}'").ToArray()) +
                $"; {_defs.Count} missile(s): " +
                string.Join(", ", _defs.Where(d => d != null).Select(d => $"'{d.jsonKey}'").ToArray()) +
                (_extraMounts.Count == 0 ? "" :
                    $"; and {_extraMounts.Count} mount(s) built at runtime from stock parts rather " +
                    "than loaded from the bundle: " +
                    string.Join(", ", _extraMounts.Where(m => m != null).Select(m => $"'{m.jsonKey}'").ToArray())) +
                ". AfterLoad's rebuild will index them.");
        }

        private static bool _forcedRebuildThrew;

        private static bool _handIndexReported;

        private static string Blame(Exception ex)
        {
            try
            {
                StackFrame[] frames = new StackTrace(ex, false).GetFrames() ?? new StackFrame[0];
                foreach (StackFrame frame in frames)
                {
                    Type? owner = frame.GetMethod()?.DeclaringType;
                    if (owner == null) continue;
                    if (owner.Namespace != null && owner.Namespace.StartsWith("MeridianWorks")) continue;
                    return owner.FullName + " (" + owner.Assembly.GetName().Name + ")";
                }
            }
            catch
            {

            }

            return "the stack does not say which assembly";
        }

        private static void AppendToIndexLookup(Encyclopedia enc, INetworkDefinition def)
        {
            if (enc == null || enc.IndexLookup == null || def == null) return;

            int at = enc.IndexLookup.IndexOf(def);
            if (at < 0)
            {
                at = enc.IndexLookup.Count;
                enc.IndexLookup.Add(def);
            }
            def.LookupIndex = at;
        }

        private static void IndexOurContentByHand(Encyclopedia enc)
        {
            int mounts = 0, defs = 0;

            try
            {
                foreach (WeaponMount mount in AllOurMounts())
                {
                    if (mount == null || string.IsNullOrEmpty(mount.jsonKey)) continue;

                    try { mount.Initialize(); } catch { }

                    if (Encyclopedia.WeaponLookup == null) continue;
                    Encyclopedia.WeaponLookup[mount.jsonKey] = mount;

                    AppendToIndexLookup(enc, mount);
                    mounts++;
                }

                foreach (MissileDefinition def in AllOurMissiles())
                {
                    if (def == null || string.IsNullOrEmpty(def.jsonKey)) continue;

                    try { def.CacheMass(); } catch { }

                    if (Encyclopedia.Lookup != null)
                        Encyclopedia.Lookup[def.jsonKey] = def;

                    AppendToIndexLookup(enc, def);

                    defs++;
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError("[Meridian] Indexing our content by hand failed: " + ex.Message);
                return;
            }

            if (mounts == 0 && defs == 0) return;

            if (_handIndexReported)
            {
                Plugin.Diag("[Meridian] Indexed " + mounts + " mount(s) and " + defs
                            + " missile definition(s) by hand again.");
                return;
            }

            _handIndexReported = true;
            Plugin.Log.LogWarning(
                "[Meridian] Indexed " + mounts + " mount(s) and " + defs + " missile definition(s) by hand "
                + "after the forced rebuild was abandoned. The pack should work; network index positions are "
                + "appended rather than rebuilt, so in multiplayer remove the conflicting mod instead.");
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

                if (_forcedRebuildThrew) afterLoad = null;

                if (!AllOurMounts().Any(m => m != null)) afterLoad = null;

                if (afterLoad != null)
                {

                    try
                    {
                        afterLoad.Invoke(enc, null);
                        Plugin.Diag("[Meridian] Forced Encyclopedia.AfterLoad() to index late registration.");
                    }
                    catch (Exception ex)
                    {
                        Exception inner = ex is TargetInvocationException tie && tie.InnerException != null
                            ? tie.InnerException
                            : ex;

                        Plugin.Log.LogWarning(
                            "[Meridian] Encyclopedia.AfterLoad() threw while we were forcing a rebuild, and "
                            + "it was not our code: " + Blame(inner) + ". That abandons the game's own lookup "
                            + "rebuild, so this pack's weapons are indexed by hand instead. The exception was: "
                            + inner.Message);

                        _forcedRebuildThrew = true;
                        IndexOurContentByHand(enc);
                    }
                }
                else if (_forcedRebuildThrew)
                {

                    IndexOurContentByHand(enc);
                }
                else if (AllOurMounts().Any(m => m != null))
                {
                    Plugin.Log.LogWarning("[Meridian] Could not find Encyclopedia.AfterLoad() to force a rebuild.");
                }
            }

            bool ok = _mounts.Count > 0;
            foreach (WeaponMount mount in AllOurMounts())
            {
                if (mount == null || string.IsNullOrEmpty(mount.jsonKey)) continue;
                bool present = Encyclopedia.WeaponLookup != null &&
                               Encyclopedia.WeaponLookup.ContainsKey(mount.jsonKey);
                if (!present)
                    Plugin.Log.LogError($"[Meridian] WeaponLookup does NOT contain '{mount.jsonKey}'.");
                ok &= present;

                int? at = ((INetworkDefinition)mount).LookupIndex;
                bool indexed = at.HasValue &&
                               enc != null && enc.IndexLookup != null &&
                               at.Value >= 0 && at.Value < enc.IndexLookup.Count &&
                               ReferenceEquals(enc.IndexLookup[at.Value], mount);
                if (!indexed)
                    Plugin.Log.LogError($"[Meridian] '{mount.jsonKey}' has no usable LookupIndex, so any "
                                        + "aircraft carrying it will fail to spawn.");
                ok &= indexed;
            }

            if (ok)
                Plugin.Diag($"[Meridian] All {_mounts.Count + _extraMounts.Count} mount(s) are in "
                            + "WeaponLookup and carry a LookupIndex that resolves back to them.");
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

            _resolveAttempts++;

            _ourBundle = FindOurLoadedBundle();

            if (_ourBundle == null && _resolveAttempts >= 3)
            {
                _ourBundle = TryLoadBundleFromDisk();
                if (_ourBundle != null)
                    Plugin.Log.LogWarning(
                        "[Meridian] Blueprinter never made our bundle resident, so it was loaded "
                        + "from disk as a last resort. Check that Blueprinter is installed and "
                        + "up to date - this path is not the supported one.");
            }

            if (_ourBundle != null)
            {
                _mounts.AddRange(LoadOurs<WeaponMount>(_ourBundle, PluginInfo.MountAssetFragment));
                _defs.AddRange(LoadOurs<MissileDefinition>(_ourBundle, PluginInfo.MissileDefAssetFragment));
            }

            if (_mounts.Count == 0)
            {

                if (_resolveAttempts < 3 || _resolveFailureLogged) return false;
                _resolveFailureLogged = true;

                Plugin.Log.LogError(
                "[Meridian] Could not resolve a single WeaponMount from any loaded bundle or from "
                + "disk, so this pack is INERT this session. On a multiplayer client that also "
                + "means you cannot join a host who has it working: the host's mission carries "
                + "our weapons and this game has no definitions to match them to.");
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
                $"[Meridian] '{def.jsonKey}': the prefab '{prefab.name}' has no Unit component "
                + "(its Missile is missing).");
                    prefab = null;
                }

                if (prefab != null)
                {
                    def.unitPrefab = prefab;
                    Plugin.Log.LogWarning(
                $"[Meridian] '{def.jsonKey}' shipped with a NULL unitPrefab and was repaired at "
                + $"load from the bundle's '{prefab.name}'.");
                    continue;
                }

                _defs.RemoveAt(i);
                Plugin.Log.LogError(
                $"[Meridian] '{def.jsonKey}' has a NULL unitPrefab and no prefab for it could be "
                + "found in the bundle.");
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
                $"[Meridian] '{def.jsonKey}': the flying prefab's Missile.definition was NULL and "
                + "was pointed back at its definition.");
            }
        }

        private static void EnsureWeaponInfoBackLink()
        {
            FieldInfo? fInfo = AccessTools.Field(typeof(Missile), "info");
            if (fInfo == null)
            {
                Plugin.Log.LogError("[Meridian] Missile.info could not be reached by reflection.");
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
                $"[Meridian] '{info.weaponName}': the flying prefab's Missile.info was NULL and was "
                + "pointed back at its WeaponInfo.");
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
                    $"[Meridian] Mount '{(mount != null ? mount.jsonKey : "(null)")}' was not " +
                    $"registered: {why}.");
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
                $"[Meridian] '{def.jsonKey}' shipped with visibleRange 0, so the round would never "
                + $"be drawn as a contact.");
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
                $"[Meridian] {w.Designation}: the bundle has no WeaponMount with jsonKey "
                + $"'{key}'.");
                }

                if (!haveDefs.Contains(w.MissileKey))
                    Plugin.Log.LogError(
                $"[Meridian] {w.Designation}: the bundle has no MissileDefinition with jsonKey "
                + $"'{w.MissileKey}'.");
            }

            foreach (string key in haveMounts.Where(k => !PluginInfo.IsOurMountKey(k))
                                             .Concat(haveDefs.Where(k => !PluginInfo.IsOurMissileKey(k))))
                Plugin.Log.LogError($"[Meridian] Resolved an asset keyed '{key}' that is NOT ours.");

            var seen = new HashSet<string>();
            foreach (WeaponMount mount in _mounts)
            {
                string key = mount.jsonKey ?? "";
                if (!string.IsNullOrEmpty(key) && !seen.Add(key))
                    Plugin.Log.LogError($"[Meridian] Two WeaponMounts in the bundle share the jsonKey '{key}'.");
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
                $"[Meridian] Asset '{asset.name}' has a jsonKey with stray whitespace: '{key}' -> "
                + $"'{trimmed}'.");
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
                $"[Meridian] Skipped '{name}' from bundle '{bundle.name}': not a Meridian asset.");
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
