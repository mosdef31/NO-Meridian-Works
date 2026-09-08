using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Mirage;
using UnityEngine;

namespace MeridianWorks
{

    internal static class NetworkHash
    {

        private const int BandBase = 0x40000000;

        private const int BandSize = 0x3FFFFFFF;

        private static bool _ran;

        internal static void RunOnce()
        {
            if (_ran) return;
            _ran = true;

            try { Assign(); }
            catch (Exception ex)
            {
                Plugin.Log.LogError(
                    "[Meridian] Deterministic PrefabHash assignment threw, so this session keeps "
                    + "Blueprinter's counter-derived hashes and MULTIPLAYER JOINS WILL FAIL: "
                    + ex);
            }
        }

        private static void Assign()
        {

            var found = new SortedDictionary<string, NetworkIdentity>(StringComparer.Ordinal);
            int duplicateKeys = 0;

            foreach (GameObject root in OurNetworkedPrefabs())
            {
                foreach (NetworkIdentity id in root.GetComponentsInChildren<NetworkIdentity>(true))
                {
                    if (id == null) continue;
                    string key = StableKey(root, id);

                    if (found.ContainsKey(key)) { duplicateKeys++; continue; }
                    found[key] = id;
                }
            }

            if (found.Count == 0)
            {
                Plugin.Log.LogWarning(
                    "[Meridian] No networked prefabs of ours were found, so nothing was rehashed. "
                    + "If this pack's weapons spawn at all, multiplayer joins are still at risk.");
                return;
            }

            var used = new HashSet<int>();
            int detours = 0;

            foreach (KeyValuePair<string, NetworkIdentity> pair in found)
            {
                int hash = HashInBand(pair.Key);

                for (int salt = 1; used.Contains(hash); salt++)
                {
                    hash = HashInBand(
                        pair.Key + "#" + salt.ToString(CultureInfo.InvariantCulture));
                    detours++;
                }

                used.Add(hash);
                pair.Value.PrefabHash = hash;
            }

            int outOfBand = 0;
            foreach (int h in used)
                if (h < BandBase) outOfBand++;

            Plugin.Log.LogInfo(
                "[Meridian] " + used.Count + " networked prefab(s) given a deterministic "
                + "PrefabHash in [0x" + BandBase.ToString("X", CultureInfo.InvariantCulture)
                + ", 0x" + (BandBase + BandSize).ToString("X", CultureInfo.InvariantCulture)
                + "], " + detours + " collision detour(s), " + outOfBand + " out of band, "
                + duplicateKeys + " duplicate key(s). Every machine running this build computes "
                + "the same numbers, which is what lets a client join a host that has this pack "
                + "loaded.");

            if (outOfBand > 0 || duplicateKeys > 0)
                Plugin.Log.LogError(
                    "[Meridian] The PrefabHash self-check did NOT come back clean, so multiplayer "
                    + "joins may still fail. Report the line above.");
        }

        private static IEnumerable<GameObject> OurNetworkedPrefabs()
        {
            var seen = new HashSet<int>();

            foreach (MissileDefinition def in EncyclopediaRegistration.AllOurMissiles())
            {
                GameObject? prefab = def == null ? null : def.unitPrefab;
                if (prefab != null && seen.Add(prefab.GetInstanceID())) yield return prefab;
            }

            foreach (WeaponMount mount in EncyclopediaRegistration.AllOurMounts())
            {
                GameObject? prefab = mount == null ? null : mount.prefab;
                if (prefab != null && seen.Add(prefab.GetInstanceID())) yield return prefab;
            }
        }

        private static string StableKey(GameObject root, NetworkIdentity id)
        {
            var path = new List<string>();
            Transform? t = id.transform;
            Transform rootT = root.transform;

            while (t != null)
            {
                path.Add(t.name);
                if (t == rootT) break;
                t = t.parent;
            }
            path.Reverse();

            var sb = new StringBuilder("MeridianWorks|");
            sb.Append(root.name).Append('|');
            for (int i = 0; i < path.Count; i++)
            {
                if (i > 0) sb.Append('/');
                sb.Append(path[i]);
            }
            return sb.ToString();
        }

        private static int HashInBand(string key)
        {
            unchecked
            {
                const uint offset = 2166136261u;
                const uint prime = 16777619u;

                uint h = offset;
                byte[] bytes = Encoding.UTF8.GetBytes(key);
                foreach (byte b in bytes)
                {
                    h ^= b;
                    h *= prime;
                }

                return BandBase + (int)(h % (uint)BandSize);
            }
        }
    }
}
