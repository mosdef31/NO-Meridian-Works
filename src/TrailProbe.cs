using System;
using System.Text;
using HarmonyLib;
using UnityEngine;

namespace MeridianWorks
{

    internal sealed class TrailProbe : MonoBehaviour
    {

        private static readonly float[] SampleAt = { 1.0f, 3.0f, 6.0f };

        private string _key = "?";
        private float _born;
        private int _next;

        internal static void Attach(Missile m, string key)
        {
            if (!Plugin.Diagnostics) return;
            var p = m.gameObject.AddComponent<TrailProbe>();
            p._key = key;
        }

        private void Start()
        {
            _born = Time.time;

            if (GameManager.gameState == GameState.Encyclopedia)
            {
                Destroy(this);
                return;
            }
        }

        private void Update()
        {
            if (_next >= SampleAt.Length) { Destroy(this); return; }
            if (Time.time - _born < SampleAt[_next]) return;

            float age = SampleAt[_next];
            _next++;

            try
            {
                Report(age);
            }
            catch (Exception ex)
            {

                Plugin.Log.LogWarning($"[Meridian] Trail probe failed: {ex.Message}");
            }
        }

        private static float NozzleZ(Transform node, Transform round)
        {
            for (Transform? t = node; t != null && t != round; t = t.parent)
                if (t.name.StartsWith("Exhaust", StringComparison.OrdinalIgnoreCase))
                    return round.InverseTransformPoint(t.position).z;
            return 0f;
        }

        private void Report(float age)
        {
            ParticleSystem[] all = GetComponentsInChildren<ParticleSystem>(true);

            var sb = new StringBuilder();
            sb.Append($"[Meridian] TRAIL ROSTER {_key} at {age:0.0} s: {all.Length} particle system(s).");

            int world = 0;
            int drawing = 0;

            foreach (ParticleSystem ps in all)
            {
                ParticleSystem.MainModule main = ps.main;
                ParticleSystem.EmissionModule em = ps.emission;
                var r = ps.GetComponent<ParticleSystemRenderer>();

                if (main.simulationSpace == ParticleSystemSimulationSpace.World) world++;

                bool visible = r != null && r.enabled
                    && r.renderMode != ParticleSystemRenderMode.None
                    && ps.particleCount > 0;
                if (visible) drawing++;

                string space = main.simulationSpace.ToString();
                if (main.simulationSpace == ParticleSystemSimulationSpace.Custom)
                    space += "(" + (main.customSimulationSpace == null
                        ? "NULL - so it is not riding the origin shift"
                        : main.customSimulationSpace.name) + ")";

                Material? mat = r == null ? null : r.sharedMaterial;

                sb.Append("\n  " + Path(ps.transform));
                sb.Append($" | live={ps.particleCount} playing={ps.isPlaying} emitting={ps.isEmitting}");
                sb.Append($" | space={space}");
                sb.Append($" | mode={(r == null ? "NO RENDERER" : r.renderMode.ToString())}");
                sb.Append($" enabled={(r == null ? false : r.enabled)}");

                if (r != null && r.renderMode == ParticleSystemRenderMode.Stretch)
                {
                    Vector3 pv = r.pivot;
                    sb.Append($" | len={r.lengthScale:0.###} vel={r.velocityScale:0.####}");
                    sb.Append($" pivot=({pv.x:0.##},{pv.y:0.##},{pv.z:0.##})");
                    sb.Append($" seatZ={ps.transform.localPosition.z:0.####}");
                }
                sb.Append($" | mat={(mat == null ? "NONE" : mat.name)}");
                sb.Append($" shader={(mat == null || mat.shader == null ? "NONE" : mat.shader.name)}");

                Transform? round = transform;
                if (round != null)
                {
                    Vector3 inRound = round.InverseTransformPoint(ps.transform.position);
                    sb.Append($" | onRound=({inRound.x:0.###},{inRound.y:0.###},{inRound.z:0.###}) m");
                    sb.Append($" worldScale={ps.transform.lossyScale.x:0.####}");

                    if (Mathf.Abs(ps.transform.lossyScale.x - 1f) > 0.01f)
                        sb.Append(" <<-- NOT 1: this authored effect is being resized by a "
                                  + "parent transform, so every size and offset on it is wrong "
                                  + "by this factor");

                    if (r != null && r.renderMode == ParticleSystemRenderMode.Stretch)
                    {

                        float size = main.startSize.constant * ps.transform.lossyScale.x;
                        float len = r.lengthScale * size + r.velocityScale * 0f;
                        sb.Append($" stretch={len:0.###} m needsSeat={-len / 2f:0.###} m");
                        sb.Append($" actualSeat={inRound.z - NozzleZ(ps.transform, round):0.###} m");
                    }
                }

                if (mat != null)
                {
                    bool hasMap = mat.HasProperty("_BaseMap") && mat.GetTexture("_BaseMap") != null;
                    bool hasMain = mat.HasProperty("_MainTex") && mat.GetTexture("_MainTex") != null;
                    if (!hasMap && !hasMain)
                        sb.Append(" | NO MAIN TEXTURE on this material, so it draws as a flat "
                                  + "untextured quad whatever the shader says");
                }
                sb.Append($" | rate={em.rateOverTime.constant:0.#}/s {em.rateOverDistance.constant:0.#}/m");

                ParticleSystem.TrailModule tr = ps.trails;
                sb.Append($" | trail={(tr.enabled ? tr.mode.ToString() : "DISABLED")}");
                if (tr.enabled)
                {
                    sb.Append($" ribbons={tr.ribbonCount} minVtx={tr.minVertexDistance:0.##}");
                    sb.Append($" world={tr.worldSpace} dieWith={tr.dieWithParticles}");
                    sb.Append($" sizeAffects={tr.sizeAffectsWidth} widthOver={tr.widthOverTrail.constant:0.##}");
                    Material? tm = r == null ? null : r.trailMaterial;
                    sb.Append($" trailMat={(tm == null ? "NONE - SO NOTHING CAN DRAW" : tm.name)}");
                    if (tm != null && tm.shader != null)
                        sb.Append($" trailShader={tm.shader.name}");

                    Gradient? g = tr.colorOverTrail.mode == ParticleSystemGradientMode.Gradient
                        ? tr.colorOverTrail.gradient
                        : null;
                    if (g != null)
                    {
                        float head = g.Evaluate(0f).a, tail = g.Evaluate(1f).a;
                        sb.Append($" colorOverTrail ENDS a={head:0.00}->a={tail:0.00}");
                        if (head > 0.01f || tail > 0.01f)
                            sb.Append(" <<-- NOT FADED, this ribbon draws as a slab");
                    }
                    else
                    {
                        sb.Append($" colorOverTrail=FLAT {tr.colorOverTrail.color.a:0.00}"
                                  + " <<-- NOT A GRADIENT, this ribbon draws as a slab");
                    }
                    if (ps.particleCount < 2)
                        sb.Append($" | ONLY {ps.particleCount} NODE(S), which is too few for a ribbon to have length");

                    if (tr.mode == ParticleSystemTrailMode.Ribbon)
                        sb.Append(RibbonTrail.EmitterState(ps));
                }

                if (r != null && ps.particleCount > 0)
                {
                    Vector3 c = r.bounds.center - transform.position;
                    sb.Append($" | bounds c=({c.x:0.0},{c.y:0.0},{c.z:0.0}) d={c.magnitude:0.0} m");
                    sb.Append($" size={r.bounds.size.magnitude:0.0} m");
                }
            }

            sb.Append($"\n  SUMMARY: {drawing} system(s) actually drawing, {world} still in World space.");
            Plugin.Diag(sb.ToString());
        }

        private string Path(Transform t)
        {
            string path = t.name;
            Transform cur = t.parent;
            while (cur != null && cur != transform)
            {
                path = cur.name + "/" + path;
                cur = cur.parent;
            }
            return path;
        }
    }

    [HarmonyPatch(typeof(Missile), "OnStartClient")]
    internal static class Missile_OnStartClient_TrailProbePatch
    {
        [HarmonyPostfix]
        private static void Postfix(Missile __instance)
        {
            if (!Plugin.Diagnostics) return;

            try
            {
                var def = __instance.definition as MissileDefinition;
                if (def == null || !PluginInfo.IsOurMissileKey(def.jsonKey)) return;
                TrailProbe.Attach(__instance, def.jsonKey);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[Meridian] Trail probe attach failed: {ex.Message}");
            }
        }
    }
}
