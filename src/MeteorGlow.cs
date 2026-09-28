using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace MeridianWorks
{

    internal sealed class MeteorGlow : MonoBehaviour
    {
        private const float BodyM = 4.8f;
        private const float ShowPx = 10f;
        private const float HidePx = 24f;

        private const float GlarePx = 10f;
        private const float HaloPx = 5f;
        private const float CorePx = 2.5f;
        private const float TailPx = 2.5f;
        private const float TailSeconds = 1.6f;
        private const int MaxSubSteps = 24;

        private static readonly Color Glare = new Color(1f, 0.14f, 0.02f) * 1.4f;
        private static readonly Color HaloCool = new Color(1f, 0.16f, 0.03f) * 1.6f;
        private static readonly Color HaloHot = new Color(1f, 0.20f, 0.04f) * 2.2f;
        private static readonly Color CoreCool = new Color(1f, 0.30f, 0.07f) * 2f;
        private static readonly Color CoreHot = new Color(1f, 0.38f, 0.10f) * 3f;
        private static readonly Color Tail = new Color(1f, 0.22f, 0.04f) * 2f;

        private Missile? _missile;
        private HSM160Fx? _fx;
        private ParticleSystem? _glare, _halo, _core, _tail;
        private Material? _mGlare, _mHalo, _mCore, _mTail;
        private string? _tintProp;
        private Vector3 _lastGlobal;
        private bool _haveLast;
        private float _spacingM = 2f;
        private bool _failed;

        private static Texture2D? _tex;
        private static bool _logged;

        internal static void Attach(Missile missile, HSM160Fx fx)
        {
            if (missile.GetComponent<MeteorGlow>() != null) return;
            MeteorGlow g = missile.gameObject.AddComponent<MeteorGlow>();
            g._missile = missile;
            g._fx = fx;
        }

        private void Start() => RenderPipelineManager.beginCameraRendering += OnCamera;

        private void OnDestroy()
        {
            RenderPipelineManager.beginCameraRendering -= OnCamera;
            foreach (ParticleSystem? p in new[] { _glare, _halo, _core, _tail })
                if (p != null) Destroy(p.gameObject);
            foreach (Material? m in new[] { _mGlare, _mHalo, _mCore, _mTail })
                if (m != null) Destroy(m);
        }

        private float Heat => _fx != null ? _fx.Heat : 0f;

        private void LateUpdate()
        {
            if (_failed) return;
            try
            {
                Missile? m = _missile;
                if (m == null || m.disabled) { _haveLast = false; return; }
                if (!Ready()) return;
                Vector3 now = transform.position.ToGlobalPosition().AsVector3();
                if (!_haveLast || Heat <= 0.01f) { _lastGlobal = now; _haveLast = true; return; }

                float step = Vector3.Distance(now, _lastGlobal);
                int n = Mathf.Clamp(Mathf.CeilToInt(step / Mathf.Max(_spacingM, 0.25f)), 1, MaxSubSteps);
                var ep = new ParticleSystem.EmitParams { applyShapeToPosition = false, velocity = Vector3.zero };
                for (int i = 1; i <= n; i++)
                {
                    ep.position = Vector3.Lerp(_lastGlobal, now, (float)i / n);
                    _tail!.Emit(ep, 1);
                }
                _lastGlobal = now;
            }
            catch (Exception e) { Fail(e); }
        }

        private void OnCamera(ScriptableRenderContext ctx, Camera cam)
        {
            if (_failed) return;
            try { Draw(cam); }
            catch (Exception e) { Fail(e); }
        }

        private void Fail(Exception e)
        {
            _failed = true;
            RenderPipelineManager.beginCameraRendering -= OnCamera;
            SetVisible(false);
            Plugin.Log.LogWarning("[Meridian] HSM-160 meteor glow stopped: " + e.Message);
        }

        private void Draw(Camera cam)
        {
            Missile? m = _missile;
            if (m == null || m.disabled || cam == null || _tail == null) { SetVisible(false); return; }

            float d = Vector3.Distance(cam.transform.position, transform.position);
            float lines = Mathf.Max(cam.pixelHeight, 1);
            float pxWorld = 2f * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * d / lines;
            float bodyPx = BodyM / Mathf.Max(pxWorld, 1e-6f);
            float show = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(HidePx, ShowPx, bodyPx));
            float heat = Heat;
            float a = show * heat;
            if (a <= 0.001f || (cam.cullingMask & (1 << gameObject.layer)) == 0) { SetVisible(false); return; }

            SetVisible(true);

            _spacingM = 0.5f * TailPx * pxWorld;
            Tint(_mGlare, Glare, a * heat);
            Tint(_mHalo, Color.Lerp(HaloCool, HaloHot, heat), a);
            Tint(_mCore, Color.Lerp(CoreCool, CoreHot, heat), a);
            Tint(_mTail, Tail, a);
        }

        private void Tint(Material? mat, Color hdr, float a)
        {
            if (mat == null || _tintProp == null) return;

            mat.SetColor(_tintProp, new Color(hdr.r * a, hdr.g * a, hdr.b * a, a));
        }

        private void SetVisible(bool on)
        {
            foreach (ParticleSystem? p in new[] { _glare, _halo, _core, _tail })
            {
                if (p == null) continue;
                Renderer r = p.GetComponent<ParticleSystemRenderer>();
                if (r.enabled != on) r.enabled = on;
            }
        }

        private bool Ready()
        {
            if (_tail != null) return true;
            Material? tpl = null;
            foreach (ParticleSystemRenderer r in GetComponentsInChildren<ParticleSystemRenderer>(true))
            {
                Material? sm = r.sharedMaterial;
                if (sm != null && sm.HasProperty("_DstBlend") && Mathf.Approximately(sm.GetFloat("_DstBlend"), 1f)) { tpl = sm; break; }
            }
            if (tpl == null) return false;

            foreach (string p in new[] { "_BaseColor", "_TintColor", "_Color" })
                if (tpl.HasProperty(p)) { _tintProp = p; break; }

            _glare = Head("HSM160_MeteorGlare", GlarePx, tpl, out _mGlare);
            _halo = Head("HSM160_MeteorHalo", HaloPx, tpl, out _mHalo);
            _core = Head("HSM160_MeteorCore", CorePx, tpl, out _mCore);
            _tail = TailSystem(tpl, out _mTail);
            SetVisible(false);

            if (!_logged)
            {
                _logged = true;
                Plugin.Log.LogInfo($"[Meridian] HSM-160 meteor glow: shader {tpl.shader?.name}, from {tpl.name}, "
                    + $"tint via {_tintProp ?? "NONE"}, tail space {(Datum.origin != null ? "Datum" : "World")}.");
            }
            return true;
        }

        private ParticleSystem NewSystem(string name, Transform? parent, Material tpl, out Material mat, float floorPx, int order)
        {
            var go = new GameObject(name) { layer = gameObject.layer };
            if (parent != null) go.transform.SetParent(parent, false);
            ParticleSystem ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ParticleSystem.MainModule main = ps.main;
            main.playOnAwake = false;
            main.loop = false;
            main.startSpeed = 0f;
            main.startSize = 0.01f;
            main.startColor = Color.white;
            main.gravityModifier = 0f;
            main.scalingMode = ParticleSystemScalingMode.Shape;
            ParticleSystem.EmissionModule em = ps.emission;
            em.enabled = false;
            ParticleSystem.ShapeModule sh = ps.shape;
            sh.enabled = false;

            mat = new Material(tpl) { name = name };
            Texture2D tex = Tex();
            foreach (string p in new[] { "_BaseMap", "_MainTex" })
                if (mat.HasProperty(p)) mat.SetTexture(p, tex);
            mat.mainTextureScale = Vector2.one;
            mat.mainTextureOffset = Vector2.zero;
            mat.DisableKeyword("_FLIPBOOKBLENDING_ON");
            mat.DisableKeyword("_SOFTPARTICLES_ON");
            mat.DisableKeyword("_FADING_ON");

            ParticleSystemRenderer r = go.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.sharedMaterial = mat;
            r.minParticleSize = floorPx / 1080f;
            r.maxParticleSize = 1f;
            r.sortingFudge = order;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            return ps;
        }

        private ParticleSystem Head(string name, float px, Material tpl, out Material mat)
        {
            ParticleSystem ps = NewSystem(name, transform, tpl, out mat, px, 0);
            ParticleSystem.MainModule main = ps.main;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.duration = 1f;
            main.startLifetime = 100000f;
            main.maxParticles = 1;
            ps.Play(false);
            ps.Emit(new ParticleSystem.EmitParams { position = Vector3.zero, applyShapeToPosition = false }, 1);
            return ps;
        }

        private ParticleSystem TailSystem(Material tpl, out Material mat)
        {
            ParticleSystem ps = NewSystem("HSM160_MeteorTail", null, tpl, out mat, TailPx, 1);
            ParticleSystem.MainModule main = ps.main;
            if (Datum.origin != null)
            {
                main.simulationSpace = ParticleSystemSimulationSpace.Custom;
                main.customSimulationSpace = Datum.origin;
            }
            else main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.duration = 1f;
            main.startLifetime = TailSeconds;
            main.maxParticles = MaxSubSteps * 50 * 2;

            ParticleSystem.ColorOverLifetimeModule col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(new Color(1f, 0.7f, 0.5f), 0f), new GradientColorKey(new Color(1f, 0.45f, 0.35f), 0.4f), new GradientColorKey(new Color(0.6f, 0.12f, 0.06f), 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.45f, 0.35f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            ps.Play(false);
            return ps;
        }

        private static Texture2D Tex()
        {
            if (_tex != null) return _tex;
            const int S = 64;
            _tex = new Texture2D(S, S, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "HSM160_MeteorSpot" };
            var px = new Color[S * S];
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float dx = (x + 0.5f) / S * 2f - 1f, dy = (y + 0.5f) / S * 2f - 1f;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float v = Mathf.Clamp01(1f - r);
                    v = v * v * (3f - 2f * v);
                    px[y * S + x] = new Color(v, v, v, v);
                }
            _tex.SetPixels(px);
            _tex.Apply(false, true);
            return _tex;
        }
    }
}
