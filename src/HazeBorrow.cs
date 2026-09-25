using System;
using UnityEngine;

namespace MeridianWorks
{
    internal static class HazeBorrow
    {

        private const int DistortionQueue = 2975;

        private static bool _lookupDone;
        private static Material? _stockDistortion;

        private static bool _said;
        private static bool _saidMissing;

        internal static void Apply(ParticleSystem ps)
        {
            if (ps == null) return;

            try
            {
                ParticleSystemRenderer renderer = ps.GetComponent<ParticleSystemRenderer>();
                if (renderer == null) return;

                Material? stock = StockDistortionMaterial();
                if (stock == null) return;

                Material? ours = renderer.sharedMaterial;
                float strength = -1f;
                if (ours != null && ours.HasProperty("_DistortionStrengthScaled"))
                    strength = ours.GetFloat("_DistortionStrengthScaled");

                var borrowed = new Material(stock) { name = "M_Meridian_Haze_Borrowed" };
                if (strength >= 0f)
                    borrowed.SetFloat("_DistortionStrengthScaled", strength);
                borrowed.renderQueue = DistortionQueue;

                renderer.sharedMaterial = borrowed;

                if (!_said)
                {
                    _said = true;
                    Plugin.Log.LogInfo(
                        "[Meridian] Haze: borrowing the game's '" + stock.name + "' (shader "
                        + (stock.shader != null ? stock.shader.name : "none") + ") at strength "
                        + (strength >= 0f ? strength.ToString("0.000") : "stock's own")
                        + ", in place of our authored FxWorkshop/HeatHaze material.");
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning(
                    "[Meridian] Haze: could not borrow the game's distortion material, so the "
                    + "authored one stands: " + ex.Message);
            }
        }

        private static Material? StockDistortionMaterial()
        {
            if (_lookupDone) return _stockDistortion;
            _lookupDone = true;

            try
            {
                foreach (Material m in Resources.FindObjectsOfTypeAll<Material>())
                {
                    if (m == null || m.shader == null) continue;

                    if (m.name != null && m.name.StartsWith("M_", StringComparison.Ordinal)) continue;

                    if (!m.HasProperty("_DistortionEnabled")) continue;
                    if (m.GetFloat("_DistortionEnabled") < 0.5f) continue;

                    _stockDistortion = m;
                    return _stockDistortion;
                }

                if (!_saidMissing)
                {
                    _saidMissing = true;
                    Plugin.Log.LogWarning(
                        "[Meridian] Haze: no material in the game has _DistortionEnabled set, so "
                        + "there is nothing to borrow and the authored FxWorkshop/HeatHaze material "
                        + "stands. That material is the one the owner reported broken, so if this "
                        + "line is in the log the haze has NOT been fixed - re-check the property "
                        + "against the current game build before tuning anything.");
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning(
                    "[Meridian] Haze: could not sweep for the game's distortion material: "
                    + ex.Message);
            }

            return _stockDistortion;
        }
    }
}
