using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace MeridianWorks
{

    internal static class Pab125HdMirror
    {
        internal const string PlainPrefix = "bomb_125_";
        internal const string HdTripleKey = "bomb_125HD_triple";
        private const string HdPrefix = "bomb_125HD_";

        private static readonly Dictionary<string, WeaponMount> _twins = new Dictionary<string, WeaponMount>(StringComparer.Ordinal);
        private static Transform? _park;
        private static bool _built;
        private static bool _saidPlace;

        internal static IEnumerable<WeaponMount> Twins => _twins.Values;

        internal static bool Build(Encyclopedia enc)
        {
            if (_built || enc?.weaponMounts == null) return false;

            WeaponMount? hdTriple = enc.weaponMounts.FirstOrDefault(m => m != null && m.jsonKey == HdTripleKey);
            if (hdTriple?.info == null)
            {
                Plugin.Log.LogWarning("[Meridian] PAB-125HD mirror: no bomb_125HD_triple in the Encyclopedia, nothing mirrored.");
                _built = true;
                return false;
            }

            WeaponMount? plainTriple = enc.weaponMounts.FirstOrDefault(m => m != null && m.jsonKey == PlainPrefix + "triple");
            Dictionary<Mesh, (Mesh mesh, Material[] mats)> swap = MeshSwap(plainTriple, hdTriple);

            var plains = enc.weaponMounts
                .Where(m => m != null && m.jsonKey != null && m.jsonKey.StartsWith(PlainPrefix, StringComparison.Ordinal)
                            && m.info != null && plainTriple != null && m.info == plainTriple.info)
                .ToList();

            bool added = false;
            foreach (WeaponMount plain in plains)
            {
                string suffix = plain.jsonKey.Substring(PlainPrefix.Length);
                if (suffix == "triple") { _twins[plain.jsonKey] = hdTriple; continue; }

                string key = HdPrefix + suffix;
                WeaponMount? existing = enc.weaponMounts.FirstOrDefault(m => m != null && m.jsonKey == key);
                WeaponMount twin = existing ?? Clone(plain, key, hdTriple.info, swap);
                _twins[plain.jsonKey] = twin;
                if (existing == null)
                {
                    enc.weaponMounts.Add(twin);
                    added = true;
                }
            }

            _built = true;
            Plugin.Diag($"[Meridian] PAB-125HD mirror: {_twins.Count} fitting(s) twinned "
                + $"({string.Join(", ", _twins.Keys.OrderBy(k => k).ToArray())}), {swap.Count} round mesh(es) swapped.");
            return added;
        }

        private static WeaponMount Clone(WeaponMount plain, string key, WeaponInfo hdInfo,
                                         Dictionary<Mesh, (Mesh mesh, Material[] mats)> swap)
        {
            var mount = UnityEngine.Object.Instantiate(plain);
            mount.name = key;
            UnityEngine.Object.DontDestroyOnLoad(mount);
            mount.jsonKey = key;
            mount.info = hdInfo;

            var fName = AccessTools.Field(typeof(WeaponMount), "mountName");
            if (fName?.GetValue(mount) is string n)
                fName.SetValue(mount, n.Replace("PAB-125", "PAB-125HD"));

            AccessTools.Field(typeof(WeaponMount), "disabled")?.SetValue(mount, false);

            if (mount.prefab != null)
            {
                GameObject store = UnityEngine.Object.Instantiate(mount.prefab, Park());
                store.name = key;
                UnityEngine.Object.DontDestroyOnLoad(store);
                foreach (Weapon w in store.GetComponentsInChildren<Weapon>(true)) w.info = hdInfo;
                foreach (MeshFilter mf in store.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (mf.sharedMesh == null || !swap.TryGetValue(mf.sharedMesh, out var to)) continue;
                    mf.sharedMesh = to.mesh;
                    var r = mf.GetComponent<MeshRenderer>();
                    if (r != null && to.mats.Length > 0) r.sharedMaterials = to.mats;
                }
                mount.prefab = store;
            }
            return mount;
        }

        private static Dictionary<Mesh, (Mesh, Material[])> MeshSwap(WeaponMount? plain, WeaponMount hd)
        {
            var map = new Dictionary<Mesh, (Mesh, Material[])>();
            if (plain?.prefab == null || hd.prefab == null) return map;

            List<MeshFilter> a = plain.prefab.GetComponentsInChildren<MeshFilter>(true).Where(f => f.sharedMesh != null).ToList();
            List<MeshFilter> b = hd.prefab.GetComponentsInChildren<MeshFilter>(true).Where(f => f.sharedMesh != null).ToList();
            var aMeshes = new HashSet<Mesh>(a.Select(f => f.sharedMesh));
            var bMeshes = new HashSet<Mesh>(b.Select(f => f.sharedMesh));

            List<Mesh> onlyA = aMeshes.Where(m => !bMeshes.Contains(m)).OrderByDescending(m => m.vertexCount).ToList();
            List<MeshFilter> onlyB = b.Where(f => !aMeshes.Contains(f.sharedMesh))
                                      .GroupBy(f => f.sharedMesh).Select(g => g.First())
                                      .OrderByDescending(f => f.sharedMesh.vertexCount).ToList();

            for (int i = 0; i < onlyA.Count && i < onlyB.Count; i++)
            {
                var r = onlyB[i].GetComponent<MeshRenderer>();
                map[onlyA[i]] = (onlyB[i].sharedMesh, r != null ? r.sharedMaterials : Array.Empty<Material>());
            }
            return map;
        }

        internal static int Place()
        {
            if (_twins.Count == 0) return 0;
            int added = 0;

            foreach (WeaponManager wm in Resources.FindObjectsOfTypeAll<WeaponManager>())
            {
                if (wm?.hardpointSets == null) continue;
                foreach (HardpointSet set in wm.hardpointSets)
                {
                    List<WeaponMount>? opts = set?.weaponOptions;
                    if (opts == null) continue;

                    foreach (WeaponMount m in opts.ToArray())
                    {
                        if (m?.jsonKey == null || !_twins.TryGetValue(m.jsonKey, out WeaponMount twin)) continue;
                        if (opts.Contains(twin)) continue;
                        opts.Add(twin);
                        added++;
                    }
                }
            }

            if (added > 0 && !_saidPlace)
            {
                _saidPlace = true;
                Plugin.Diag($"[Meridian] PAB-125HD mirror: {added} twin fitting(s) offered beside their PAB-125 originals.");
            }
            return added;
        }

        private static Transform Park()
        {
            if (_park != null) return _park;
            var holder = new GameObject("MeridianWorks_Pab125HdPrefabs") { hideFlags = HideFlags.HideAndDontSave };
            UnityEngine.Object.DontDestroyOnLoad(holder);
            holder.SetActive(false);
            _park = holder.transform;
            return _park;
        }
    }
}
