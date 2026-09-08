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

        private static int RebindTree(GameObject? root)
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

                    string name = m.shader.name;
                    Shader? game = Shader.Find(name);

                    if (game == null)
                    {
                        if (_reported.Add(name))
                            Plugin.Log.LogWarning(
                                $"[Meridian] Shader rebind: the game has no shader called '{name}', so the "
                                + "bundle's own copy is being kept. If this weapon draws pink under Vulkan, "
                                + "this is the shader to change in the Unity project.");
                        continue;
                    }

                    if (game == m.shader)
                    {
                        if (_reported.Add(name))
                            Plugin.Diag($"[Meridian] Shader rebind: '{name}' is already the game's own copy.");
                        continue;
                    }

                    m.shader = game;
                    count++;

                    if (_reported.Add(name))
                        Plugin.Diag($"[Meridian] Shader rebind: '{name}' moved from the bundle's copy to the game's.");
                }
            }

            return count;
        }
    }
}
