using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace MeridianWorks
{
    internal sealed class ExhaustCover : MonoBehaviour
    {

        internal const string ChildName = "ExhaustCover";

        private const float EjectSpeed = 6f;

        private const float Lifetime = 3f;

        private const float TumbleRate = 7f;

        private static readonly FieldInfo? FIgnition =
            AccessTools.Field(typeof(Missile), "ignition");

        private Missile? _missile;
        private Transform? _cover;

        internal static void Attach(Missile missile, string? jsonKey)
        {
            if (missile == null || !PluginInfo.IsOurMissileKey(jsonKey)) return;
            if (missile.GetComponent<ExhaustCover>() != null) return;
            if (FIgnition == null) return;

            Transform? cover = missile.transform.Find(ChildName);
            if (cover == null) return;

            var c = missile.gameObject.AddComponent<ExhaustCover>();
            c._missile = missile;
            c._cover = cover;
        }

        private void FixedUpdate()
        {
            Missile m = _missile!;
            Transform? cover = _cover;

            if (m == null || cover == null) { enabled = false; return; }

            if (FIgnition!.GetValue(m) is not bool lit || !lit) return;

            enabled = false;
            _cover = null;

            try
            {
                Eject(m, cover);
            }
            catch (Exception ex)
            {

                Plugin.Log.LogWarning("[Meridian] The exhaust cover failed to eject: " + ex.Message);
            }
        }

        private static void Eject(Missile m, Transform cover)
        {
            Vector3 back = -m.transform.forward;

            cover.SetParent(null, true);

            var rb = cover.gameObject.AddComponent<Rigidbody>();
            rb.mass = 5f;
            rb.useGravity = true;
            rb.interpolation = RigidbodyInterpolation.Interpolate;

            rb.velocity = m.GetComponent<Rigidbody>() is Rigidbody mrb
                ? mrb.velocity + back * EjectSpeed
                : back * EjectSpeed;

            rb.angularVelocity = UnityEngine.Random.onUnitSphere * TumbleRate;

            cover.gameObject.AddComponent<CoverDrag>();

            UnityEngine.Object.Destroy(cover.gameObject, Lifetime);

            Plugin.Diag(
                "[Meridian] COVER " + m.name + ": ejected at t+"
                + m.timeSinceSpawn.ToString("0.##") + "s on the engine's own ignition flag, "
                + EjectSpeed.ToString("0.#") + " m/s aft of the round, then aerodynamically "
                + "braked, destroyed in " + Lifetime.ToString("0.#") + "s.");
        }
    }

    internal sealed class CoverDrag : MonoBehaviour
    {

        private const float Area = 0.1245f;

        private const float Cd = 1.0f;

        private const float Rho0 = 1.225f;

        private const float ScaleHeight = 8500f;

        private Rigidbody? _rb;

        private void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            if (_rb == null) enabled = false;
        }

        private void FixedUpdate()
        {
            Rigidbody rb = _rb!;
            if (rb == null) { enabled = false; return; }

            Vector3 v = rb.velocity;
            float speed = v.magnitude;
            if (speed < 0.01f) return;

            float rho = Rho0 * Mathf.Exp(-Mathf.Max(0f, transform.position.y) / ScaleHeight);

            float k = 0.5f * rho * Cd * Area / Mathf.Max(0.001f, rb.mass);

            rb.velocity = v / (1f + k * speed * Time.fixedDeltaTime);
        }
    }

    [HarmonyPatch(typeof(Missile), "OnStartClient")]
    internal static class Missile_OnStartClient_ExhaustCoverPatch
    {
        [HarmonyPostfix]
        private static void Postfix(Missile __instance)
        {
            try
            {
                if (__instance.definition is not MissileDefinition def) return;
                ExhaustCover.Attach(__instance, def.jsonKey);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[Meridian] Exhaust cover attach failed: " + ex.Message);
            }
        }
    }
}
