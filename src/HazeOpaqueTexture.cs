using System;
using System.Reflection;
using UnityEngine.Rendering;

namespace MeridianWorks
{
    internal static class HazeOpaqueTexture
    {
        private static bool _ran;

        private const string PropertyName = "supportsCameraOpaqueTexture";
        private const string FieldName = "m_RequireOpaqueTexture";

        internal static void RunForScene(string sceneName)
        {
            try
            {
                RenderPipelineAsset? asset = GraphicsSettings.currentRenderPipeline;
                if (asset == null) return;

                int id = asset.GetInstanceID();
                bool sameAsset = id == _lastAssetId;
                bool first = _lastAssetId == 0;
                _lastAssetId = id;

                if (sameAsset && ReadsCorrect(asset))
                    return;

                string why = first
                    ? "the first one seen"
                    : sameAsset
                        ? "the SAME asset as before, but its settings have DRIFTED BACK - "
                          + "something in this scene rewrote them, and that is the "
                          + "first-launch fault"
                        : "NOT the one already checked";

                Plugin.Log.LogInfo(
                    $"[Meridian] Scene '{sceneName}' is running render pipeline asset "
                    + $"'{asset.name}' ({id}), which is {why}. "
                    + "Re-checking the opaque texture.");

                Apply();
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning(
                    $"[Meridian] Could not re-check the camera opaque texture for scene "
                    + $"'{sceneName}': {ex.Message}");
            }
        }

        private static bool ReadsCorrect(RenderPipelineAsset asset)
        {
            try
            {
                Type t = asset.GetType();

                PropertyInfo? prop = t.GetProperty(PropertyName,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                FieldInfo? field = t.GetField(FieldName,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

                bool? opaque = null;
                if (prop != null && prop.CanRead && prop.GetValue(asset) is bool pv) opaque = pv;
                else if (field != null && field.GetValue(asset) is bool fv) opaque = fv;

                if (opaque == false && PluginInfo.EnableCameraOpaqueTexture) return false;

                PropertyInfo? dprop = t.GetProperty(DownsamplingProperty,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                FieldInfo? dfield = t.GetField(DownsamplingField,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

                Type? enumType = dprop?.PropertyType ?? dfield?.FieldType;
                if (enumType == null || !enumType.IsEnum) return true;

                object? down = dprop != null && dprop.CanRead
                    ? dprop.GetValue(asset)
                    : dfield?.GetValue(asset);

                if (down != null && !down.Equals(Enum.ToObject(enumType, 0))) return false;

                return true;
            }
            catch (Exception)
            {
                return true;
            }
        }

        private static int _lastAssetId;

        internal static void RunOnce()
        {
            if (_ran) return;
            _ran = true;

            _lastAssetId = GraphicsSettings.currentRenderPipeline != null
                ? GraphicsSettings.currentRenderPipeline.GetInstanceID()
                : 0;

            try
            {
                Apply();
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning(
                    $"[Meridian] Could not check the camera opaque texture, so the heat "
                    + $"haze may draw as a flat sheet: {ex.Message}");
            }
        }

        private static void Apply()
        {
            RenderPipelineAsset? asset = GraphicsSettings.currentRenderPipeline;
            if (asset == null)
            {
                Plugin.Log.LogWarning(
                    "[Meridian] No render pipeline asset, so the heat haze's opaque "
                    + "texture cannot be checked. The haze will draw flat if it is off.");
                return;
            }

            Type t = asset.GetType();

            PropertyInfo? prop = t.GetProperty(PropertyName,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            FieldInfo? field = t.GetField(FieldName,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

            bool? current = null;
            if (prop != null && prop.CanRead && prop.GetValue(asset) is bool pv) current = pv;
            else if (field != null && field.GetValue(asset) is bool fv) current = fv;

            if (current == null)
            {
                Plugin.Log.LogWarning(
                    $"[Meridian] '{t.Name}' has neither '{PropertyName}' nor '{FieldName}', so "
                    + "the heat haze's requirement cannot be checked on this URP version.");
                return;
            }

            if (current.Value)
            {

                Plugin.Log.LogInfo(
                    "[Meridian] Camera opaque texture: ALREADY ON. The heat haze has the "
                    + "scene colour it needs, so any 'paper trail' or flat-sheet look is "
                    + "NOT this - read HazeOpaqueTexture.cs before chasing it further.");

                FullResolution(asset, t);
                return;
            }

            if (!PluginInfo.EnableCameraOpaqueTexture)
            {
                Plugin.Log.LogWarning(
                    "[Meridian] Camera opaque texture: OFF, and this pack is configured not "
                    + "to turn it on. The heat haze will draw as flat hard-edged sheets that "
                    + "vanish against a matching sky. This is the cause, not a tuning fault.");
                return;
            }

            bool wrote = false;
            if (prop != null && prop.CanWrite)
            {
                prop.SetValue(asset, true);
                wrote = true;
            }
            else if (field != null)
            {
                field.SetValue(asset, true);
                wrote = true;
            }

            if (!wrote)
            {
                Plugin.Log.LogWarning(
                    "[Meridian] Camera opaque texture is OFF and could not be turned on - "
                    + "the property is read-only and the field is missing. The heat haze "
                    + "will draw as flat sheets.");
                return;
            }

            bool after = false;
            if (prop != null && prop.CanRead && prop.GetValue(asset) is bool av) after = av;
            else if (field != null && field.GetValue(asset) is bool af) after = af;

            if (after)
                Plugin.Log.LogInfo(
                    "[Meridian] Camera opaque texture was OFF and is now ON. This is what "
                    + "the heat haze needs to distort instead of drawing a flat sheet. It "
                    + "costs a full-screen colour copy per camera per frame; if the frame "
                    + "rate is worse this sortie, this is the change to blame first.");
            else
                Plugin.Log.LogWarning(
                    "[Meridian] Camera opaque texture refused the write and is still OFF. "
                    + "The heat haze will draw as flat sheets.");

            FullResolution(asset, t);
        }

        private const string DownsamplingProperty = "opaqueDownsampling";
        private const string DownsamplingField = "m_OpaqueDownsampling";

        private static void FullResolution(RenderPipelineAsset asset, Type t)
        {
            try
            {
                PropertyInfo? prop = t.GetProperty(DownsamplingProperty,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                FieldInfo? field = t.GetField(DownsamplingField,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

                Type? enumType = prop?.PropertyType ?? field?.FieldType;
                if (enumType == null || !enumType.IsEnum)
                {
                    Plugin.Log.LogWarning(
                        "[Meridian] No opaque-downsampling setting on this URP version, so "
                        + "anything seen through the heat haze may look pixelated.");
                    return;
                }

                object none = Enum.ToObject(enumType, 0);
                object? before = prop != null && prop.CanRead
                    ? prop.GetValue(asset)
                    : field?.GetValue(asset);

                if (before != null && before.Equals(none))
                {
                    Plugin.Log.LogInfo(
                        "[Meridian] Opaque texture downsampling: already None, so the haze "
                        + "reads the scene at full resolution.");
                    return;
                }

                if (prop != null && prop.CanWrite) prop.SetValue(asset, none);
                else if (field != null) field.SetValue(asset, none);
                else
                {
                    Plugin.Log.LogWarning(
                        "[Meridian] Opaque downsampling is read-only on this URP version, so "
                        + "the flame seen through the haze will stay pixelated.");
                    return;
                }

                Plugin.Log.LogInfo(
                    $"[Meridian] Opaque texture downsampling was '{before}' and is now None. "
                    + "That half-resolution copy is why anything seen THROUGH the heat haze "
                    + "looked pixelated - our own textures were never the cause.");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning(
                    $"[Meridian] Could not set the opaque downsampling: {ex.Message}");
            }
        }
    }
}
