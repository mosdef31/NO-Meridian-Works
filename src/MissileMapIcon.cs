using System;
using HarmonyLib;
using UnityEngine;

namespace MeridianWorks
{

    [HarmonyPatch(typeof(UnitMapIcon), "SetIcon")]
    internal static class UnitMapIcon_SetIcon_MissileIconPatch
    {

        private const float IconSize = 0.9f;

        private static readonly System.Reflection.FieldInfo? F_orient
            = AccessTools.Field(typeof(UnitMapIcon), "orient");

        private static Sprite? _sprite;
        private static bool _logged;

        [HarmonyPostfix]
        private static void Postfix(UnitMapIcon __instance, Unit unit)
        {
            try
            {
                UnitDefinition? def = unit == null ? null : unit.definition;
                if (def == null) return;
                if (!PluginInfo.IsOurMissileKey(def.jsonKey)) return;

                Sprite dart = MissileSprite();

                float hadSize = def.mapIconSize;

                if (!_logged)
                {
                    _logged = true;
                    Sprite? had = def.mapIcon;
                    Plugin.Diag(
                        "[Meridian] Round map icon: the definition had " +
                        (had == null ? "NO sprite, which Unity draws as a filled white square"
                                     : $"sprite '{had.name}'") +
                        $" at mapIconSize {hadSize:0.##}, mapOrient {def.mapOrient}. Replaced with a " +
                        $"generated dart at {IconSize}, oriented to the round's heading. Logged once.");
                }

                def.mapIcon = dart;
                def.mapIconSize = IconSize;

                def.mapOrient = true;
                F_orient?.SetValue(__instance, true);

                if (__instance.iconImage != null)
                {
                    __instance.iconImage.sprite = dart;
                    __instance.iconImage.transform.localScale =
                        IconScale(__instance.iconImage.transform.localScale, hadSize);
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[Meridian] Map icon patch failed: {ex.Message}");
            }
        }

        private static Vector3 IconScale(Vector3 current, float hadSize)
        {
            if (hadSize <= 0.001f) return current;
            return current * (IconSize / hadSize);
        }

        private static Sprite MissileSprite()
        {
            if (_sprite != null) return _sprite;

            const int size = 64;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, mipChain: false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };

            var pixels = new Color32[size * size];
            const float halfWidth = 12f;
            const float noseY = 54f;
            const float tailY = 10f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float px = x + 0.5f - size * 0.5f;
                    float py = y + 0.5f;

                    float t = Mathf.InverseLerp(noseY, tailY, py);
                    float allowed = halfWidth * t;

                    float alpha = 0f;
                    if (py <= noseY && py >= tailY)
                    {

                        alpha = Mathf.Clamp01(allowed - Mathf.Abs(px) + 1f);
                    }

                    pixels[y * size + x] =
                        new Color32(255, 255, 255, (byte)Mathf.RoundToInt(Mathf.Clamp01(alpha) * 255f));
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply(updateMipmaps: false);

            _sprite = Sprite.Create(tex, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f));
            return _sprite;
        }
    }
}
