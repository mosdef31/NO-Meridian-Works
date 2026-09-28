using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace MeridianWorks
{

    internal sealed class HSM160Fx : MonoBehaviour
    {

        private const float FanLength = 1.6f;
        private const float FanAngle = 10f;
        private const float FanLowM = 5000f;
        private const float FanHighM = 30000f;

        private const float GlowOffMach = 2.5f;
        private const float GlowFullMach = 5f;
        private const float SpeedOfSound = 340f;

        private const float Tick = 0.25f;
        private const float ScanWindow = 3f;

        private const string SeparationName = "HSM160_Separation";
        private const string NoseGlowName = "HSM160_NoseGlow";

        private sealed class Plume
        {
            public ParticleSystem Ps = null!;
            public float Speed;
            public float Angle;
        }

        private sealed class Glow
        {
            public ParticleSystem Ps = null!;
            public float Rate;
            public float Size;
        }

        private Missile? _missile;
        private readonly List<Plume> _plume = new List<Plume>();
        private readonly List<Glow> _glow = new List<Glow>();
        private Transform? _separation;
        private bool _scanned;
        private bool _glowing;
        private bool _sheathFolded;
        private float _next;
        private static bool _logged;

        internal static void Attach(Missile missile, string? jsonKey)
        {
            if (missile == null || jsonKey == null || !HSM160Rounds.Ours.Contains(jsonKey)) return;
            if (missile.GetComponent<HSM160Fx>() != null) return;
            HSM160Fx fx = missile.gameObject.AddComponent<HSM160Fx>();
            fx._missile = missile;
            fx.ArmBoosterRibbon();
            MeteorGlow.Attach(missile, fx);
        }

        private const float BoosterRibbonSeconds = 41f;

        private MonoBehaviour? _boosterRibbon;

        private void ArmBoosterRibbon()
        {
            if (!PluginConfig.RibbonTrail || _missile == null) return;
            VLSBooster? booster = GetComponentInChildren<VLSBooster>(true);
            if (booster == null) return;
            foreach (ParticleSystem ps in booster.GetComponentsInChildren<ParticleSystem>(true))
            {
                if (ps.name != "Smoke") continue;
                _boosterRibbon = RibbonTrail.Convert(ps, _missile.rb, BoosterRibbonSeconds);
                break;
            }
            if (!_ribbonLogged)
            {
                _ribbonLogged = true;
                Plugin.Log.LogInfo("[Meridian] HSM-160 booster smoke: "
                    + (_boosterRibbon != null ? "converted to a ribbon trail." : "NOT converted, no Smoke system or no emitter."));
            }
        }

        private static bool _ribbonLogged;

        internal void SetBoosterRibbon(bool on)
        {
            if (_boosterRibbon != null) _boosterRibbon.enabled = on;
        }

        internal void PlaySeparation()
        {
            if (_separation == null) _separation = FindDeep(transform, SeparationName);
            if (_separation == null) return;
            foreach (ParticleSystem ps in _separation.GetComponentsInChildren<ParticleSystem>(true))
                ps.Play(false);
        }

        private void Scan()
        {
            _plume.Clear();
            _glow.Clear();

            Transform? nose = FindDeep(transform, NoseGlowName);
            if (nose != null && !_sheathFolded) { BoostSheathTint(nose); _sheathFolded = true; }
            if (nose != null)
                foreach (ParticleSystem ps in nose.GetComponentsInChildren<ParticleSystem>(true))
                {
                    _glow.Add(new Glow
                    {
                        Ps = ps,
                        Rate = ps.emission.rateOverTimeMultiplier,
                        Size = ps.main.startSizeMultiplier,
                    });
                }

            foreach (ParticleSystem ps in GetComponentsInChildren<ParticleSystem>(true))
            {
                string n = ps.name;
                if (n != "Flame" && n != "FlameCore") continue;

                if (ps.GetComponentInParent<VLSBooster>() != null) continue;
                if (IsUnder(ps.transform, SeparationName) || IsUnder(ps.transform, NoseGlowName)) continue;
                _plume.Add(new Plume
                {
                    Ps = ps,
                    Speed = ps.main.startSpeedMultiplier,
                    Angle = ps.shape.angle,
                });
            }

            _scanned = _plume.Count > 0 || (_missile != null && _missile.timeSinceSpawn > ScanWindow);
            if (_plume.Count > 0) BoostCoreTint();

            if (_scanned && !_logged)
            {
                _logged = true;
                Plugin.Log.LogInfo($"[Meridian] HSM-160 FX driver: {_plume.Count} stage 2 plume system(s), "
                    + $"{_glow.Count} nose glow system(s), separation flash "
                    + (FindDeep(transform, SeparationName) != null ? "found" : "MISSING") + ".");
            }
        }

        private void Update()
        {
            Missile? m = _missile;
            if (m == null || m.disabled) { enabled = false; return; }

            try
            {
                if (Time.time < _next) return;
                _next = Time.time + Tick;
                if (!_scanned) Scan();
                ApplyFan(Smooth(FanLowM, FanHighM, m.GlobalPosition().y));
                ApplyGlow(m.rb != null ? m.rb.velocity.magnitude / SpeedOfSound : 0f);
            }
            catch (Exception e)
            {
                enabled = false;
                Plugin.Log.LogWarning("[Meridian] HSM-160 FX driver stopped: " + e.Message);
            }
        }

        private void ApplyFan(float f)
        {
            float k = Mathf.Lerp(1f, FanLength, f);
            foreach (Plume p in _plume)
            {
                if (p.Ps == null) continue;
                ParticleSystem.MainModule main = p.Ps.main;
                main.startSpeedMultiplier = p.Speed * k;
                ParticleSystem.ShapeModule shape = p.Ps.shape;
                shape.angle = Mathf.Lerp(p.Angle, FanAngle, f);
            }
        }

        private static readonly Color GlowCold = new Color(1f, 0.28f, 0.10f, 0.30f);
        private static readonly Color GlowHot = new Color(1f, 0.34f, 0.08f, 1f);
        private const float GlowRateHot = 2.0f;
        private const float GlowSizeHot = 1.3f;
        private const float GlowTintBoost = 2.4f;

        private const float CoreTintBoost = 1.6f;
        private static bool _sheathLogged;

        private void ApplyGlow(float mach)
        {
            float k = Smooth(GlowOffMach, GlowFullMach, mach);
            bool on = k > 0.01f;
            Color tint = Color.Lerp(GlowCold, GlowHot, k);
            foreach (Glow g in _glow)
            {
                if (g.Ps == null) continue;
                ParticleSystem.EmissionModule em = g.Ps.emission;
                em.rateOverTimeMultiplier = g.Rate * Mathf.Lerp(0.5f, GlowRateHot, k);
                ParticleSystem.MainModule main = g.Ps.main;
                main.startSizeMultiplier = g.Size * Mathf.Lerp(0.8f, GlowSizeHot, k);
                main.startColor = tint;
                if (on && !_glowing) g.Ps.Play(false);
                else if (!on && _glowing) g.Ps.Stop(false, ParticleSystemStopBehavior.StopEmitting);
            }
            _glowing = on;
            Heat = k;
        }

        internal float Heat { get; private set; }

        private static void BoostSheathTint(Transform nose)
        {
            int floored = 0;
            string boosted = "none";
            foreach (ParticleSystemRenderer r in nose.GetComponentsInChildren<ParticleSystemRenderer>(true))
            {
                floored++;
                Material? mat = r.material;
                if (mat == null) continue;
                foreach (string prop in new[] { "_TintColor", "_Color" })
                {
                    if (!mat.HasProperty(prop)) continue;
                    mat.SetColor(prop, mat.GetColor(prop) * GlowTintBoost);
                    boosted = prop;
                    break;
                }
            }

            if (!_sheathLogged)
            {
                _sheathLogged = true;
                Plugin.Log.LogInfo($"[Meridian] HSM-160 sheath: {floored} system(s), material tint boosted via {boosted}.");
            }
        }

        private void BoostCoreTint()
        {
            string boosted = "none";
            foreach (Plume p in _plume)
            {
                if (p.Ps == null || p.Ps.name != "FlameCore") continue;
                Material? mat = p.Ps.GetComponent<ParticleSystemRenderer>()?.material;
                if (mat == null) continue;
                foreach (string prop in new[] { "_TintColor", "_BaseColor", "_Color" })
                {
                    if (!mat.HasProperty(prop)) continue;
                    mat.SetColor(prop, mat.GetColor(prop) * CoreTintBoost);
                    boosted = prop;
                    break;
                }
            }
            if (!_coreLogged)
            {
                _coreLogged = true;
                Plugin.Log.LogInfo($"[Meridian] HSM-160 flame core glare: tint boosted via {boosted}.");
            }
        }

        private static bool _coreLogged;

        private static float Smooth(float lo, float hi, float x) =>
            Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(lo, hi, x));

        private static bool IsUnder(Transform t, string name)
        {
            for (Transform? p = t; p != null; p = p.parent)
                if (p.name == name) return true;
            return false;
        }

        private static Transform? FindDeep(Transform root, string name)
        {
            if (root.name == name) return root;
            foreach (Transform c in root)
            {
                Transform? hit = FindDeep(c, name);
                if (hit != null) return hit;
            }
            return null;
        }
    }

    [HarmonyPatch(typeof(Missile), "OnStartClient")]
    internal static class Missile_OnStartClient_HSM160FxPatch
    {
        [HarmonyPostfix]
        private static void Postfix(Missile __instance)
        {
            try
            {
                if (__instance.definition is not MissileDefinition def) return;
                HSM160Fx.Attach(__instance, def.jsonKey);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[Meridian] HSM-160 FX attach failed: {ex.Message}");
            }
        }
    }

    [HarmonyPatch(typeof(VLSBooster), "Burnout")]
    internal static class HSM160SeparationFlash
    {
        private static readonly FieldInfo? FMissile = AccessTools.Field(typeof(VLSBooster), "missile");

        private static void Postfix(VLSBooster __instance)
        {
            try
            {
                if (FMissile?.GetValue(__instance) is not Missile m) return;

                m.GetComponent<HSM160Fx>()?.SetBoosterRibbon(false);

                if (m.boosterIsAttached) return;
                m.GetComponent<HSM160Fx>()?.PlaySeparation();
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("[Meridian] HSM-160 separation flash failed: " + e.Message);
            }
        }
    }

    [HarmonyPatch(typeof(VLSBooster), "Activate")]
    internal static class HSM160BoosterRibbonStart
    {
        private static readonly FieldInfo? FMissile = AccessTools.Field(typeof(VLSBooster), "missile");

        private static void Postfix(VLSBooster __instance)
        {
            try
            {
                if (FMissile?.GetValue(__instance) is not Missile m) return;
                m.GetComponent<HSM160Fx>()?.SetBoosterRibbon(true);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("[Meridian] HSM-160 booster ribbon start failed: " + e.Message);
            }
        }
    }
}
