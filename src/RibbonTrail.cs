using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace MeridianWorks
{

    internal static class RibbonTrail
    {

        private const float EmitFrequency = 10f;
        private const float OpacityVariation = 0.8f;
        private const float ScaleVariation = 0.5f;

        private const float SegmentLength = 5f;

        private const string HolderName = "MeridianRibbonEmitter";

        private const int StockMaxNodes = 1500;

        private const float SmokeLifeMult = 1.5f;
        private const float SmokeWidthMult = 1.3f;

        private const float HeadFadeMetres = 4f;

        private const float StockNozzleWidthFraction = 0.22f;

        private static readonly Type? TTrailEmitter = AccessTools.TypeByName("TrailEmitter");

        private static readonly FieldInfo? FTrailSystem = Field("trailSystem");
        private static readonly FieldInfo? FEmitFrequency = Field("emitFrequency");
        private static readonly FieldInfo? FOpacityVariation = Field("opacityVariation");
        private static readonly FieldInfo? FScaleVariation = Field("scaleVariation");
        private static readonly FieldInfo? FSegmentLength = Field("segmentLength");
        private static readonly FieldInfo? FRb = Field("rb");
        private static readonly FieldInfo? FEmitTransform = Field("emitTransform");
        private static readonly FieldInfo? FEmitDelay = Field("emitDelay");
        private static readonly FieldInfo? FEmitLifetime = Field("emitLifetime");

        private static FieldInfo? Field(string name) =>
            TTrailEmitter == null ? null : AccessTools.Field(TTrailEmitter, name);

        internal static MonoBehaviour? Convert(ParticleSystem smoke, Rigidbody? rb, float emitLifetime)
        {
            if (TTrailEmitter == null || rb == null) return null;

            if (FTrailSystem == null || FRb == null || FEmitTransform == null || FSegmentLength == null)
            {
                Plugin.Log.LogWarning(
                    "[Meridian] Ribbon trail: the engine's TrailEmitter no longer has the fields this expects, "
                    + "so the smoke keeps its billboard emission. Re-read TrailEmitter.cs against the current build.");
                return null;
            }

            var renderer = smoke.GetComponent<ParticleSystemRenderer>();
            if (renderer == null) return null;

            Material? stockRibbon = StockRibbonMaterial();

            Material source = renderer.sharedMaterial;
            if (stockRibbon != null)
            {
                renderer.trailMaterial = stockRibbon;
            }
            else if (source != null)
            {
                var ribbonMat = new Material(source) { name = source.name + "_RibbonCell" };

                const float Cell = 1f / 3f;
                ribbonMat.mainTextureScale = new Vector2(Cell, Cell);
                ribbonMat.mainTextureOffset = new Vector2(Cell, Cell);

                if (ribbonMat.HasProperty("_BaseMap"))
                {
                    ribbonMat.SetTextureScale("_BaseMap", new Vector2(Cell, Cell));
                    ribbonMat.SetTextureOffset("_BaseMap", new Vector2(Cell, Cell));
                }

                renderer.trailMaterial = ribbonMat;
            }
            else
            {
                renderer.trailMaterial = renderer.sharedMaterial;
            }

            renderer.renderMode = ParticleSystemRenderMode.None;

            var trails = smoke.trails;
            trails.enabled = true;
            trails.mode = ParticleSystemTrailMode.Ribbon;
            trails.ribbonCount = 1;
            trails.splitSubEmitterRibbons = false;

            trails.worldSpace = false;

            trails.sizeAffectsWidth = true;
            trails.sizeAffectsLifetime = false;
            trails.inheritParticleColor = true;
            trails.dieWithParticles = true;

            trails.textureMode = ParticleSystemTrailTextureMode.RepeatPerSegment;

            float nodeLife = Mathf.Max(smoke.main.startLifetime.constantMax, 0.1f) * SmokeLifeMult;
            float topSpeed = 300f;
            try
            {
                var missile = rb.GetComponent<Missile>();
                if (missile != null) topSpeed = Mathf.Max(missile.GetTopSpeed(0f, 0f), 100f);
            }
            catch (Exception) {  }
            float headKey = Mathf.Clamp(HeadFadeMetres / (topSpeed * nodeLife), 0.0005f, 0.0182f);

            var trailAlpha = new Gradient();
            trailAlpha.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f),
                        new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f,          0f),
                        new GradientAlphaKey(1f,          headKey),
                        new GradientAlphaKey(0.8817726f,  0.7077f),
                        new GradientAlphaKey(0f,          1f) });
            trails.colorOverTrail = new ParticleSystem.MinMaxGradient(trailAlpha);

            trails.widthOverTrail = new ParticleSystem.MinMaxCurve(1f);

            trails.ratio                    = 1f;
            trails.lifetime                 = new ParticleSystem.MinMaxCurve(1f);
            trails.minVertexDistance        = 0.2f;
            trails.textureScale             = Vector2.one;
            trails.shadowBias               = 0.5f;
            trails.generateLightingData     = true;

            trails.attachRibbonsToTransform = false;

            var emission = smoke.emission;

            emission.rateOverTime = 0f;
            emission.rateOverDistance = 0f;
            emission.enabled = false;

            var main = smoke.main;

            main.startLifetime = new ParticleSystem.MinMaxCurve(nodeLife);

            var limit = smoke.limitVelocityOverLifetime;
            limit.enabled = true;
            limit.separateAxes = false;
            limit.limit = new ParticleSystem.MinMaxCurve(2f);
            limit.dampen = 1f;
            limit.space = ParticleSystemSimulationSpace.World;

            float mid = (main.startSize.constantMin + main.startSize.constantMax) * 0.5f;
            if (mid <= 0f) mid = Mathf.Max(main.startSize.constant, 0.01f);

            var size = smoke.sizeOverLifetime;
            float finalWidth = size.enabled
                ? mid * Mathf.Max(size.size.Evaluate(1f), 1f)
                : mid;

            main.startSize = new ParticleSystem.MinMaxCurve(finalWidth * SmokeWidthMult);

            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(
                1f, AnimationCurve.Linear(0f, StockNozzleWidthFraction, 1f, 1f));

            main.maxParticles = StockMaxNodes;

            smoke.Clear(true);

            var holder = new GameObject(HolderName);
            holder.SetActive(false);
            holder.transform.SetParent(smoke.transform, worldPositionStays: false);

            var te = (MonoBehaviour)holder.AddComponent(TTrailEmitter);

            FTrailSystem.SetValue(te, smoke);
            FRb.SetValue(te, rb);

            FEmitTransform.SetValue(te, smoke.transform);
            FSegmentLength.SetValue(te, SegmentLength);
            FEmitFrequency?.SetValue(te, EmitFrequency);
            FOpacityVariation?.SetValue(te, OpacityVariation);
            FScaleVariation?.SetValue(te, ScaleVariation);

            FEmitDelay?.SetValue(te, 0f);

            FEmitLifetime?.SetValue(te, emitLifetime);

            te.enabled = false;
            holder.SetActive(true);

            return te;
        }

        private static bool _ribbonLookupDone;
        private static Material? _stockRibbonMaterial;

        private static Material? StockRibbonMaterial()
        {
            if (_ribbonLookupDone) return _stockRibbonMaterial;
            _ribbonLookupDone = true;

            try
            {
                foreach (Material m in Resources.FindObjectsOfTypeAll<Material>())
                {
                    if (m == null || m.name == null) continue;
                    if (m.name.IndexOf("smokeRibbonScatter", StringComparison.OrdinalIgnoreCase) < 0)
                        continue;
                    _stockRibbonMaterial = m;
                    Plugin.Log.LogInfo($"[Meridian] Ribbon trail: borrowing stock's '{m.name}' "
                        + $"(shader {m.shader?.name}) for the smoke ribbon, matching AAM2.");
                    return _stockRibbonMaterial;
                }

                Plugin.Log.LogWarning(
                    "[Meridian] Ribbon trail: stock's 'smokeRibbonScatter' was not found, so the "
                    + "smoke ribbon falls back to a cropped cell of our own puff atlas. That is "
                    + "the pre-1.0.3 look and it reads flatter than stock. Re-check the material "
                    + "name against the current build.");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[Meridian] Ribbon trail: could not look up stock's ribbon "
                    + $"material, falling back to the atlas crop: {ex.Message}");
            }

            return _stockRibbonMaterial;
        }

        internal static string EmitterState(ParticleSystem smoke)
        {
            Transform? holder = smoke.transform.Find(HolderName);
            if (holder == null)
                return " | NO RIBBON EMITTER CHILD, so nothing is feeding this ribbon at all";

            if (TTrailEmitter == null || holder.GetComponent(TTrailEmitter) is not Behaviour te)
                return " | THE EMITTER CHILD EXISTS BUT CARRIES NO TrailEmitter";

            var sb = new System.Text.StringBuilder();
            sb.Append($" | emitter enabled={te.enabled} active={holder.gameObject.activeInHierarchy}");

            if (FRb?.GetValue(te) == null)
                sb.Append(" rb=NULL - FixedUpdate returns on its first line every frame");

            if (FEmitDelay?.GetValue(te) is float delay && delay > 0f)
                sb.Append($" emitDelay={delay:0.##}s STILL COUNTING DOWN, nothing emits until it reaches 0");

            if (FEmitLifetime?.GetValue(te) is float life)
                sb.Append($" emitLifetime={life:0.#}s"
                    + (life <= 0f ? " - EXPIRED, so the emitter has already switched itself off" : string.Empty));

            if (FSegmentLength?.GetValue(te) is float segment)
                sb.Append($" segment={segment:0.#}m");

            if (!te.enabled && holder.gameObject.activeInHierarchy)
            {
                bool stageIsBurning = false;
                Transform? stage = smoke.transform.parent;
                if (stage != null)
                {
                    foreach (ParticleSystem sibling in stage.GetComponentsInChildren<ParticleSystem>(true))
                    {
                        if (sibling == smoke) continue;
                        if (!sibling.isPlaying) continue;
                        stageIsBurning = true;
                        break;
                    }
                }

                sb.Append(stageIsBurning
                    ? " - DISABLED WHILE ITS OWN STAGE IS BURNING, so Motor.Activate started this stage's "
                      + "particle systems and did NOT call StartTrail on its emitter. That is a wiring fault."
                    : " - disabled, but nothing else on this stage is playing either, so the stage has simply "
                      + "not lit yet and this is the correct state");
            }

            return sb.ToString();
        }
    }
}
