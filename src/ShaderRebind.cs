

using System;
using System.Collections.Generic;
using UnityEngine;

namespace MeridianWorks
{

    internal static class ShaderRebind
    {
        private static readonly HashSet<string> _reported = new HashSet<string>();
        private static readonly HashSet<int> _done = new HashSet<int>();

        private static bool _ran;

        internal static void Apply(IEnumerable<MissileDefinition> defs, IEnumerable<WeaponMount> mounts)
        {
            if (_ran) return;
            _ran = true;

            int rebound = 0;

            try
            {
                foreach (MissileDefinition d in defs)
                    if (d != null) rebound += RebindTree(d.unitPrefab);

                foreach (WeaponMount m in mounts)
                    if (m != null) rebound += RebindTree(m.prefab);

                Plugin.Diag(
                    $"[Meridian] Shader rebind: {rebound} material(s) moved onto the game's own shaders. "
                    + "This is what keeps the pack from drawing pink under Vulkan.");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[Meridian] The shader rebind threw: {ex.Message}");
            }
        }

        internal static int RebindTree(GameObject? root)
        {
            if (root == null) return 0;

            int count = 0;

            foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null) continue;

                foreach (Material m in r.sharedMaterials)
                {
                    if (m == null || m.shader == null) continue;
                    if (!_done.Add(m.GetInstanceID())) continue;

                    int authoredQueue = m.renderQueue;

                    if (Rebind(m))
                    {
                        count++;
                        if (m.renderQueue != authoredQueue)
                        {
                            m.renderQueue = authoredQueue;

                            if (!_saidQueueWasLost)
                            {
                                _saidQueueWasLost = true;
                                Plugin.Log.LogInfo(
                                    "[Meridian] Effect materials lost their authored render queue on the "
                                    + $"shader rebind and were put back (first was '{m.name}', queue "
                                    + $"{authoredQueue}). Without this our flames draw in the opaque pass "
                                    + "and the skybox paints over them.");
                            }
                        }
                    }

                    ReassertKeywords(m);
                }
            }

            return count;
        }

        private static bool Rebind(Material m)
        {
            string name = m.shader.name;
            Shader? game = Shader.Find(name);

            if (game == null)
            {
                if (_reported.Add(name))
                    Plugin.Log.LogWarning(
                        $"[Meridian] Shader rebind: the game has no shader called '{name}', so the "
                        + "bundle's own copy is being kept. If this weapon draws pink under Vulkan, "
                        + "this is the shader to change in the Unity project.");
                return false;
            }

            if (game == m.shader)
            {
                if (_reported.Add(name))
                    Plugin.Diag($"[Meridian] Shader rebind: '{name}' is already the game's own copy.");
                return false;
            }

            m.shader = game;

            if (_reported.Add(name))
                Plugin.Diag($"[Meridian] Shader rebind: '{name}' moved from the bundle's copy to the game's.");

            return true;
        }

        private const int DistortionQueue = 2975;

        private static bool _saidKeywordsWereLost;
        private static bool _saidQueueWasLost;

        private static void ReassertKeywords(Material m)
        {
            try
            {
                bool lost = false;

                if (Has(m, "_Surface") && m.GetFloat("_Surface") > 0.5f)
                    lost |= Need(m, "_SURFACE_TYPE_TRANSPARENT");

                if (Has(m, "_EmissionEnabled") && m.GetFloat("_EmissionEnabled") > 0.5f)
                    lost |= Need(m, "_EMISSION");

                if (Has(m, "_FlipbookBlending") && m.GetFloat("_FlipbookBlending") > 0.5f)
                    lost |= Need(m, "_FLIPBOOKBLENDING_ON");

                if (Has(m, "_DistortionEnabled") && m.GetFloat("_DistortionEnabled") > 0.5f)
                {
                    lost |= Need(m, "_DISTORTION_ON");
                    if (m.GetTexture("_BumpMap") != null) lost |= Need(m, "_NORMALMAP");

                    if (m.renderQueue != DistortionQueue) m.renderQueue = DistortionQueue;
                }

                if (lost && !_saidKeywordsWereLost)
                {
                    _saidKeywordsWereLost = true;
                    Plugin.Log.LogInfo(
                        "[Meridian] Effect materials lost their shader keywords and were fixed.");
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning(
                    $"[Meridian] Could not re-check an effect material's keywords: {ex.Message}");
            }
        }

        private static bool Has(Material m, string prop) => m.HasProperty(prop);

        private static bool Need(Material m, string keyword)
        {
            if (m.IsKeywordEnabled(keyword)) return false;
            m.EnableKeyword(keyword);
            return true;
        }
    }
}
