using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace MeridianWorks
{

    internal static class StockContent
    {
        private static readonly HashSet<int> _stock = new HashSet<int>();

        private static bool _taken;
        private static bool _warned;

        internal static bool IsStock(UnityEngine.Object? asset)
        {
            if (asset == null) return false;
            if (!_taken)
            {
                if (!_warned)
                {
                    _warned = true;
                    Plugin.Log.LogWarning(
                        "[Meridian] No stock-content snapshot was taken, so every Encyclopedia entry "
                        + "is being treated as the game's own. Effects, pylons and racks may be "
                        + "borrowed from another mod's weapons if one is installed.");
                }
                return true;
            }

            return _stock.Contains(asset.GetInstanceID());
        }

        internal static void Snapshot(Encyclopedia enc)
        {
            if (_taken || enc == null) return;
            _taken = true;

            if (enc.missiles != null)
                foreach (MissileDefinition d in enc.missiles)
                    if (d != null) _stock.Add(d.GetInstanceID());

            if (enc.weaponMounts != null)
                foreach (WeaponMount m in enc.weaponMounts)
                    if (m != null) _stock.Add(m.GetInstanceID());

            Plugin.Diag(
                $"[Meridian] Stock content snapshot: {enc.missiles?.Count ?? 0} missile definitions and "
                + $"{enc.weaponMounts?.Count ?? 0} weapon mounts were in the Encyclopedia before any mod "
                + "added to it. Only these are eligible as donors.");
        }
    }

    [HarmonyPatch(typeof(Encyclopedia), "AfterLoad", new Type[0])]
    internal static class Encyclopedia_AfterLoad_StockSnapshot
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static void Prefix(Encyclopedia __instance)
        {
            try
            {
                StockContent.Snapshot(__instance);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[Meridian] The stock-content snapshot threw: {ex.Message}");
            }
        }
    }
}
