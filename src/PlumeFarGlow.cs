using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace MeridianWorks
{

    [DefaultExecutionOrder(100)]
    internal sealed class PlumeFarGlow : MonoBehaviour
    {

        private const float RamjetFloor = 0.0033f;
        private const float BoosterFloor = 0.0016f;
        private const float FarLength = 1.5f;
        private const float MinThin = 0.08f;
        private const float Tick = 0.25f;
        private const float ScanWindow = 4f;
        private const string StagePrefix = "AuthoredFX_Stage";

        private static readonly Dictionary<string, int> RamjetStage = new Dictionary<string, int>
        {
            { "MeridianYashma_Missile", 1 },
            { "MeridianScreamer_Missile", 1 },
            { "MeridianAAM41_Missile", 1 },
            { "MeridianARAD72_Missile", 1 },
        };

        private sealed class Flame
        {
            public ParticleSystem Ps = null!;
            public ParticleSystemRenderer? R;
            public float Size;
            public float Length;
            public float Floor;
            public float Rate;
            public bool Far;
        }

        private Missile? _missile;
        private string _key = "";
        private readonly List<Flame> _flames = new List<Flame>();
        private bool _scanned;
        private float _next;
        private static bool _logged;

        internal static void Attach(Missile missile, string key)
        {
            if (!key.StartsWith("Meridian", StringComparison.Ordinal)) return;

            if (HSM160Rounds.Ours.Contains(key)) return;
            if (missile.GetComponent<PlumeFarGlow>() != null) return;
            PlumeFarGlow g = missile.gameObject.AddComponent<PlumeFarGlow>();
            g._missile = missile;
            g._key = key;
        }

        private void Scan()
        {
            _flames.Clear();
            _skippedAlpha = 0;
            RamjetStage.TryGetValue(_key, out int ramjet);
            bool hasRamjet = RamjetStage.ContainsKey(_key);
            foreach (ParticleSystem ps in GetComponentsInChildren<ParticleSystem>(true))
            {
                if (ps.name != "Flame" && ps.name != "FlameCore") continue;
                int stage = StageOf(ps.transform);
                if (stage < 0) continue;
                ParticleSystemRenderer? r = ps.GetComponent<ParticleSystemRenderer>();

                if (!IsAdditive(r)) { _skippedAlpha++; continue; }
                _flames.Add(new Flame
                {
                    Ps = ps,
                    R = r,
                    Size = ps.main.startSizeMultiplier,
                    Length = r != null ? r.lengthScale : 1f,
                    Rate = ps.emission.rateOverTimeMultiplier,
                    Floor = hasRamjet && stage == ramjet ? RamjetFloor : BoosterFloor,
                });
            }
            _scanned = _flames.Count > 0 || (_missile != null && _missile.timeSinceSpawn > ScanWindow);
            if (_scanned && !_logged && _flames.Count > 0)
            {
                _logged = true;
                Plugin.Log.LogInfo($"[Meridian] Far glow additive-only: {_flames.Count} on, {_skippedAlpha} alpha left ({_key}).");
            }
            if (_scanned && _flames.Count == 0) enabled = false;
        }

        private int _skippedAlpha;

        private static bool IsAdditive(ParticleSystemRenderer? r)
        {
            Material? m = r != null ? r.sharedMaterial : null;
            if (m == null) return false;
            if (m.HasProperty("_DstBlend")) return Mathf.Approximately(m.GetFloat("_DstBlend"), 1f);
            return false;
        }

        private static int StageOf(Transform t)
        {
            for (Transform? p = t; p != null; p = p.parent)
                if (p.name.StartsWith(StagePrefix, StringComparison.Ordinal)
                    && int.TryParse(p.name.Substring(StagePrefix.Length), out int n))
                    return n;
            return -1;
        }

        private void Update()
        {
            Missile? m = _missile;
            if (m == null || m.disabled) { enabled = false; return; }

            if (_scanned) return;
            if (Time.time < _next) return;
            _next = Time.time + Tick;
            try
            {
                Scan();
            }
            catch (Exception e)
            {
                enabled = false;
                Plugin.Log.LogWarning("[Meridian] Plume far glow stopped: " + e.Message);
            }
        }

        private void LateUpdate()
        {
            Missile? m = _missile;
            if (m == null || m.disabled || !_scanned) return;
            TryApply();
        }

        private void TryApply()
        {
            try
            {
                Apply();
            }
            catch (Exception e)
            {
                enabled = false;
                Plugin.Log.LogWarning("[Meridian] Plume far glow stopped: " + e.Message);
            }
        }

        private void Apply()
        {
            Camera? cam = Camera.main;
            if (cam == null) return;
            float fovTan = 2f * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            float d = Vector3.Distance(cam.transform.position, transform.position);
            foreach (Flame f in _flames)
            {
                if (f.Ps == null) continue;
                float floorWorld = f.Floor * fovTan * d;

                bool far = f.Far ? floorWorld > f.Size * 0.75f : floorWorld > f.Size;

                if (f.Far && !far) ClampLiveSize(f.Ps, f.Size);
                f.Far = far;
                ParticleSystem.MainModule main = f.Ps.main;
                main.startSizeMultiplier = far ? floorWorld : f.Size;

                ParticleSystem.EmissionModule em = f.Ps.emission;
                em.rateOverTimeMultiplier = f.Rate * (far ? Mathf.Clamp(f.Size / floorWorld, MinThin, 1f) : 1f);
                if (f.R != null) f.R.lengthScale = far ? Mathf.Max(f.Length, FarLength) : f.Length;
            }
        }

        private static ParticleSystem.Particle[] _buf = new ParticleSystem.Particle[256];

        internal static void ClampLiveSize(ParticleSystem ps, float maxSize)
        {
            int cap = ps.main.maxParticles;
            if (_buf.Length < cap) _buf = new ParticleSystem.Particle[cap];
            int n = ps.GetParticles(_buf);
            bool changed = false;
            for (int i = 0; i < n; i++)
            {
                if (_buf[i].startSize <= maxSize) continue;
                _buf[i].startSize = maxSize;
                changed = true;
            }
            if (changed) ps.SetParticles(_buf, n);
        }
    }

    [HarmonyPatch(typeof(Missile), "OnStartClient")]
    internal static class Missile_OnStartClient_PlumeFarGlow
    {
        [HarmonyPostfix]
        private static void Postfix(Missile __instance)
        {
            try
            {
                if (__instance.definition is not MissileDefinition def || def.jsonKey == null) return;
                PlumeFarGlow.Attach(__instance, def.jsonKey);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[Meridian] Plume far glow attach failed: {ex.Message}");
            }
        }
    }
}
