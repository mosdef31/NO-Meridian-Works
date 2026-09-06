using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;
using NuclearOption.UIStyleSystem;

namespace MeridianWorks
{
    internal sealed class OffBoresightCue : MonoBehaviour
    {

        private const int Size = 72;

        private const float RingWidth = 1.25f;

        internal const float MinOffBoresightDegrees = 20f;

        private static Texture2D? _tex;
        private static OffBoresightCue? _instance;

        private Image? _image;
        private Camera? _cam;

        private static Texture2D BuildTexture()
        {
            var t = new Texture2D(Size, Size, TextureFormat.RGBA32, false)
            {
                name = "Meridian_OffBoresightCue",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };

            float c = (Size - 1) * 0.5f;
            float radius = c - RingWidth;

            float gap = radius * 0.35f;

            var px = new Color[Size * Size];
            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    float dx = x - c, dy = y - c;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);

                    float ring = 1f - Mathf.Clamp01((Mathf.Abs(r - radius) - RingWidth * 0.5f)
                                                    / Mathf.Max(RingWidth * 0.5f, 0.001f));

                    float bar = 0f;
                    if (r > gap && r < radius)
                    {
                        float h = 1f - Mathf.Clamp01((Mathf.Abs(dy) - RingWidth * 0.5f)
                                                     / Mathf.Max(RingWidth * 0.5f, 0.001f));
                        float v = 1f - Mathf.Clamp01((Mathf.Abs(dx) - RingWidth * 0.5f)
                                                     / Mathf.Max(RingWidth * 0.5f, 0.001f));
                        bar = Mathf.Max(h, v);
                    }

                    float a = Mathf.Clamp01(Mathf.Max(ring, bar));
                    px[y * Size + x] = new Color(1f, 1f, 1f, a);
                }
            }

            t.SetPixels(px);
            t.Apply(false, false);
            return t;
        }

        internal static void Show(Aircraft aircraft, Unit target, bool canFire)
        {
            if (_instance == null) Create();
            if (_instance == null) return;

            _instance._fedOnFrame = Time.frameCount;
            _instance.Place(aircraft, target, canFire);
        }

        internal static void Hide()
        {
            if (_instance != null && _instance._image != null)
                _instance._image.enabled = false;
        }

        private int _fedOnFrame = -1;

        private void LateUpdate()
        {
            if (_image == null) return;

            if (transform.parent == null)
            {
                _instance = null;
                UnityEngine.Object.Destroy(gameObject);
                return;
            }

            if (!InPlay() || Time.frameCount - _fedOnFrame > 1)
                _image.enabled = false;
        }

        private static bool InPlay()
        {
            try
            {
                if (GameplayUI.GameIsPaused) return false;

                GameState state = GameManager.gameState;
                if (state != GameState.SinglePlayer && state != GameState.Multiplayer &&
                    state != GameState.Editor)
                    return false;

                GameplayUI? ui = SceneSingleton<GameplayUI>.i;
                if (ui != null && ui.gameplayCanvas != null && !ui.gameplayCanvas.enabled)
                    return false;
            }
            catch
            {
                return false;
            }
            return true;
        }

        private static void Create()
        {
            Transform? anchor = SceneSingleton<FlightHud>.i == null
                ? null
                : SceneSingleton<FlightHud>.i.GetHUDCenter();
            if (anchor == null) return;

            _tex ??= BuildTexture();

            var go = new GameObject("Meridian_OffBoresightCue", typeof(RectTransform));
            go.transform.SetParent(anchor, worldPositionStays: false);

            var img = go.AddComponent<Image>();
            img.sprite = Sprite.Create(_tex, new Rect(0, 0, Size, Size),
                                       new Vector2(0.5f, 0.5f));
            img.raycastTarget = false;
            img.enabled = false;

            var rt = (RectTransform)go.transform;
            rt.sizeDelta = new Vector2(Size, Size);

            _instance = go.AddComponent<OffBoresightCue>();
            _instance._image = img;

            Plugin.Diag("[Meridian] CUE: the off-boresight feasibility ring is on the HUD.");
        }

        private static readonly Dictionary<WeaponInfo, bool> _infraRed =
            new Dictionary<WeaponInfo, bool>();

        internal static bool IsInfraRed(WeaponInfo info)
        {
            if (info == null) return false;
            if (_infraRed.TryGetValue(info, out bool known)) return known;

            bool ir = false;
            GameObject? prefab = info.weaponPrefab;
            if (prefab != null)
                ir = prefab.GetComponentInChildren<IRSeeker>(true) != null;

            _infraRed[info] = ir;
            return ir;
        }

        internal static float OffBoresightDegrees(Aircraft aircraft, Unit target)
        {
            if (aircraft == null || target == null) return 0f;

            Vector3 to = target.transform.position - aircraft.transform.position;
            if (to.sqrMagnitude < 1f) return 0f;

            return Vector3.Angle(aircraft.transform.forward, to);
        }

        private void Place(Aircraft aircraft, Unit target, bool canFire)
        {
            if (_image == null) return;

            ColorTheme theme = ThemeManager.Active.ColorTheme;
            Color c = canFire ? theme.Alert : theme.AllClear;

            _image.color = new Color(c.r, c.g, c.b, 0.85f);

            if (_cam == null) _cam = Camera.main;
            if (_cam == null || target == null) { _image.enabled = false; return; }

            Vector3 sp = _cam.WorldToScreenPoint(target.transform.position);

            if (sp.z <= 0f) { _image.enabled = false; return; }

            _image.enabled = true;
            _image.rectTransform.position = new Vector3(sp.x, sp.y, 0f);
        }
    }

    [HarmonyPatch(typeof(HUDMissileState), nameof(HUDMissileState.UpdateWeaponDisplay))]
    internal static class HUDMissileState_UpdateWeaponDisplay_CuePatch
    {
        private static bool _warnedMissingField;

        private static readonly FieldInfo? FMet =
            AccessTools.Field(typeof(HUDMissileState), "allRequirementsMet");

        private static readonly FieldInfo? FStation =
            AccessTools.Field(typeof(HUDMissileState), "weaponStation");

        [HarmonyPostfix]
        private static void Postfix(HUDMissileState __instance, Aircraft aircraft,
                                    List<Unit> targetList)
        {
            try
            {
                if (!PluginConfig.OffBoresightCue) { OffBoresightCue.Hide(); return; }

                if (FMet == null || FStation == null)
                {
                    if (!_warnedMissingField)
                    {
                        _warnedMissingField = true;
                        Plugin.Log.LogWarning("[Meridian] Off-boresight cue is OFF.");
                    }
                    return;
                }

                if (FStation.GetValue(__instance) is not WeaponStation station
                    || station.WeaponInfo == null
                    || !PluginInfo.IsOurWeaponName(station.WeaponInfo.weaponName))
                {
                    OffBoresightCue.Hide();
                    return;
                }

                if (targetList == null || targetList.Count == 0
                    || FMet.GetValue(__instance) is not bool met)
                {
                    OffBoresightCue.Hide();
                    return;
                }

                Unit target = targetList[0];
                if (target == null || target.disabled) { OffBoresightCue.Hide(); return; }

                if (!OffBoresightCue.IsInfraRed(station.WeaponInfo))
                {
                    OffBoresightCue.Hide();
                    return;
                }

                if (OffBoresightCue.OffBoresightDegrees(aircraft, target)
                    < OffBoresightCue.MinOffBoresightDegrees)
                {
                    OffBoresightCue.Hide();
                    return;
                }

                OffBoresightCue.Show(aircraft, target, met);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[Meridian] Off-boresight cue failed: {ex.Message}");
            }
        }
    }
}
