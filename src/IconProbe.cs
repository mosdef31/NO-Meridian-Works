using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MeridianWorks
{

    internal static class IconProbe
    {
        private static bool _ran;

        internal static void RunOnce()
        {
            if (_ran) return;
            _ran = true;

            try
            {
                Report();
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[Meridian] Icon probe failed: {ex.Message}");
            }
        }

        private static void Report()
        {
            Encyclopedia? enc = GameData.EncyclopediaOrNull();
            if (enc?.weaponMounts == null) return;

            var sizes = new Dictionary<string, int>();

            foreach (WeaponMount m in enc.weaponMounts)
            {
                if (m == null || m.info == null) continue;
                if (PluginInfo.IsOurMountKey(m.jsonKey)) continue;

                Sprite? icon = m.info.weaponIcon;
                if (icon == null) continue;

                int w = Mathf.RoundToInt(icon.rect.width);
                int h = Mathf.RoundToInt(icon.rect.height);
                if (w <= 0 || h <= 0) continue;

                string key = $"{w}x{h} ({(float)w / h:0.00}:1)";
                sizes[key] = sizes.TryGetValue(key, out int n) ? n + 1 : 1;
            }

            if (sizes.Count == 0)
            {
                Plugin.Diag("[Meridian] Icon probe: no stock weapon icons carried a sprite.");
                return;
            }

            Plugin.Diag(
                "[Meridian] Stock weapon icon sizes, commonest first: " +
                string.Join(", ", sizes.OrderByDescending(kv => kv.Value)
                                       .Select(kv => $"{kv.Key} x{kv.Value}")
                                       .ToArray()) +
                ". Ours are 352x84 (4.19:1) - a mismatch here is the loadout stretching them.");
        }
    }
}
