using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace MeridianWorks
{
    [HarmonyPatch(typeof(Missile), "DetectCollisions")]
    internal static class HSM160GroundEnd
    {
        private static readonly FieldInfo? FImpactFuse =
            AccessTools.Field(typeof(Missile), "impactFuse");

        private static bool _logged;

        internal static bool IsHSM160(Missile m) =>
            m.definition is MissileDefinition def
            && def.jsonKey != null
            && def.jsonKey.StartsWith("MeridianHSM160", StringComparison.Ordinal)
            && PluginInfo.IsOurMissileKey(def.jsonKey);

        [HarmonyPrefix]
        [HarmonyPriority(Priority.Last)]
        private static bool Prefix(Missile __instance)
        {
            try
            {
                Missile m = __instance;
                if (m == null || m.rb == null || m.disabled) return true;
                if (!IsHSM160(m)) return true;

                bool fuseLive = FImpactFuse?.GetValue(m) is bool f && f;
                if (fuseLive && m.IsArmed()) return true;

                Vector3 from = m.transform.position;
                Vector3 normal;
                bool terrain;
                Vector3 at;

                if (from.y < Datum.LocalSeaY)
                {
                    normal = Vector3.up;
                    terrain = false;
                    at = new Vector3(from.x, Datum.LocalSeaY, from.z);
                }
                else
                {
                    Vector3 to = from + 1.1f * Time.fixedDeltaTime * m.rb.velocity;
                    if (!Physics.Linecast(from, to, out RaycastHit hit, PhysicsLayers.StaticsMask.value))
                        return true;
                    normal = hit.normal;
                    terrain = hit.collider != null
                        && hit.collider.sharedMaterial == GameAssets.i.terrainMaterial;
                    at = hit.point - m.rb.velocity.normalized * 0.2f;
                }

                m.transform.position = at;
                m.rb.MovePosition(at);
                if (!m.rb.isKinematic) m.rb.velocity = Vector3.zero;
                m.Detonate(normal, hitArmor: false, hitTerrain: terrain);

                if (!_logged)
                {
                    _logged = true;
                    Plugin.Log.LogInfo($"[Meridian] HSM-160 ground contact ended the round (armed={m.IsArmed()}).");
                }
                return false;
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("[Meridian] HSM-160 ground end failed: " + e.Message);
                return true;
            }
        }
    }
}
