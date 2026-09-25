using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace MeridianWorks
{

    internal static class LiveryMirror
    {

        private static readonly string[] TintSlots =
            { "_BaseColor", "_Color", "_MainColor", "_TintColor" };

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
            Color tint = Brighten(colors[0].Color);

            foreach (Renderer r in _ours)
            {
                if (r == null) continue;

                Material[] mats = r.materials;
                if (mats == null) continue;

                foreach (Material m in mats)
                {
                    if (m == null) continue;
                    bool wrote = false;
                    foreach (string slot in TintSlots)
                    {
                        if (!m.HasProperty(slot)) continue;
                        m.SetColor(slot, tint);
                        wrote = true;
                    }

                    if (!wrote && m.shader != null && _unpainted.Add(m.shader.name))
                        Plugin.Log.LogWarning(
                            "[Meridian] Livery: shader '" + m.shader.name + "' on one of our "
                            + "mount materials declares none of _BaseColor, _Color, _MainColor "
                            + "or _TintColor, so that mount cannot take the aircraft's livery "
                            + "colour and stays factory grey. Add the slot to the shader or "
                            + "name it in LiveryMirror.TintSlots.");
                }
            }
        }

        internal static Color Brighten(Color livery)
        {
            Color.RGBToHSV(livery, out float h, out float s, out float v);
            v = 1f;

            Color lit = Color.HSVToRGB(h, s, v);
            lit.a = livery.a;
            return lit;
        }

        private static readonly HashSet<string> _unpainted = new HashSet<string>();
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
