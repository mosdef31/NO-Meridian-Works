using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace MeridianWorks
{

    internal static class LiveryMirror
    {

        private const string BaseColor = "_BaseColor";

        private static readonly HashSet<Renderer> _ours = new HashSet<Renderer>();

        internal static void Track(Renderer renderer)
        {
            if (renderer != null) _ours.Add(renderer);
        }

        internal static void Apply(LiveryData? livery)
        {
            if (livery == null) return;
            LiveryData.TextureColor[] colors = livery.Colors;
            if (colors == null || colors.Length == 0) return;
            Color tint = colors[0].Color;

            foreach (Renderer r in _ours)
            {
                if (r == null) continue;
                Material m = r.material;
                if (m == null || !m.HasProperty(BaseColor)) continue;
                m.SetColor(BaseColor, tint);
            }
        }
    }

    [HarmonyPatch(typeof(WeaponManager), nameof(WeaponManager.UpdateColorables))]
    internal static class WeaponManager_UpdateColorables_LiveryMirrorPatch
    {
        private static void Postfix(LiveryData liveryData)
        {
            if (!PluginConfig.LiveryMounts) return;
            LiveryMirror.Apply(liveryData);
        }
    }
}
