using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace MeridianWorks
{

    [HarmonyPatch(typeof(Missile), "DetectCollisions")]
    internal static class Missile_DetectCollisions_ImpactWalkProbe
    {

        private const float RelativeSpeedWindow = 100f;

        private static readonly Dictionary<int, int> _walking = new Dictionary<int, int>();

        private static readonly HashSet<int> _announced = new HashSet<int>();

        private static readonly System.Reflection.FieldInfo? FImpactFuse =
            AccessTools.Field(typeof(Missile), "impactFuse");

        [HarmonyPrefix]
        private static void Prefix(Missile __instance)
        {
            if (!Plugin.Diagnostics) return;

            try
            {
                Missile m = __instance;
                if (m == null || m.rb == null) return;
                if (m.definition is not MissileDefinition def) return;
                if (!PluginInfo.IsOurMissileKey(def.jsonKey)) return;

                int id = m.GetInstanceID();

                Vector3 from = m.transform.position;
                Vector3 to = from + 1.1f * Time.fixedDeltaTime * m.rb.velocity;

                if (!Physics.Linecast(from, to, out RaycastHit hit))
                {
                    Leaving(id);
                    return;
                }

                Rigidbody? other = hit.collider != null ? hit.collider.attachedRigidbody : null;

                if (_announced.Add(id))
                {
                    string what = hit.collider != null ? hit.collider.name : "something unnamed";
                    float closing = other == null
                        ? m.rb.velocity.magnitude
                        : (other.velocity - m.rb.velocity).magnitude;
                    bool live = FImpactFuse?.GetValue(m) is bool f && f;

                    Plugin.Log.LogInfo(
                        $"[Meridian] CONTACT {def.jsonKey}: touched '{what}' at "
                        + $"{m.rb.velocity.magnitude:0} m/s, closing {closing:0} m/s, "
                        + $"armed={m.IsArmed()} impactFuse={live}, "
                        + $"other={(other == null ? "no rigidbody" : (other.isKinematic ? "kinematic" : "dynamic"))}. "
                        + (other != null && !other.isKinematic && closing <= RelativeSpeedWindow
                            ? "INSIDE the engine's window, so it declined to fuse - this is the walk, "
                              + "and nothing in this pack answers it. ContactFuse did, and was removed "
                              + "on 2026-09-08 for making the AGM-92 worse."
                            : !m.IsArmed()
                                ? "NOT ARMED, so the engine zeroes the round's velocity and leaves it on the "
                                  + "surface without fusing. That is a stop, not a walk, and nothing in this "
                                  + "pack currently answers it."
                                : "Outside the engine's window and armed, so this contact should have fused."));
                }

                if (other == null || other.isKinematic)
                {
                    Leaving(id);
                    return;
                }

                float relative = (other.velocity - m.rb.velocity).magnitude;
                if (relative > RelativeSpeedWindow)
                {

                    Leaving(id);
                    return;
                }

                _walking.TryGetValue(id, out int steps);
                _walking[id] = steps + 1;

                if (steps == 0)
                {
                    string what = hit.collider != null ? hit.collider.name : "something unnamed";
                    Plugin.Log.LogInfo(
                        $"[Meridian] WALK {def.jsonKey}: touched '{what}' at "
                        + $"{m.rb.velocity.magnitude:0} m/s while it moved at {other.velocity.magnitude:0} m/s "
                        + $"- {relative:0} m/s apart, inside the engine's {RelativeSpeedWindow:0} m/s window, "
                        + "so Missile.DetectCollisions returned without fusing. The round is being "
                        + "snapped onto the surface and will walk along it until a step falls outside "
                        + "that window. THIS IS THE BOUNCE.");
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[Meridian] The impact-walk probe threw: {ex.Message}");
            }
        }

        private static void Leaving(int id)
        {
            if (!_announced.Remove(id)) return;

            _walking.TryGetValue(id, out int steps);
            _walking.Remove(id);

            Plugin.Log.LogInfo(
                $"[Meridian] WALK ended after {steps} physics step(s), about "
                + $"{steps * Time.fixedDeltaTime:0.00} s of the round sliding on the surface.");
        }
    }
}
