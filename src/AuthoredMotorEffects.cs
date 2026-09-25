using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace MeridianWorks
{

    internal static class AuthoredMotorEffects
    {
        private const string ContainerName = "Meridian_AuthoredEffects";

        private const string BakedEffectPrefix = "AuthoredFX_Stage";

        private static readonly FieldInfo? _fMotors = AccessTools.Field(typeof(Missile), "motors");
        private static readonly FieldInfo? _fEffectsTransform =
            AccessTools.Field(typeof(Missile), "effectsTransform");

        private static readonly HashSet<string> _logged = new HashSet<string>();

        internal static void Apply(Missile ours)
        {
            if (ours == null) return;
            if (ours.definition is not MissileDefinition def) return;

            if (ours.transform.Find(ContainerName) != null) return;

            if (_fMotors?.GetValue(ours) is not Array motors || motors.Length == 0) return;

            List<Transform> nozzles = FindNozzles(ours);
            if (nozzles.Count == 0) return;

            bool anyBaked = false;
            foreach (Transform n in nozzles)
                if (n.Find(BakedEffectPrefix + "0") != null) { anyBaked = true; break; }
            if (!anyBaked) return;

            var container = new GameObject(ContainerName);
            container.transform.SetParent(ours.transform, false);

            if (_fEffectsTransform?.GetValue(ours) as Transform == null)
                _fEffectsTransform?.SetValue(ours, nozzles[0]);

            int seated = SeatFromBake(nozzles, motors, 0, def.jsonKey);
            if (motors.Length > 1)
                seated += SeatFromBake(nozzles, motors, 1, def.jsonKey);

            if (seated == 0)
            {

                UnityEngine.Object.Destroy(container);
                if (_logged.Add(def.jsonKey))
                    Plugin.Log.LogWarning(
                        $"[Meridian] {def.jsonKey}: a baked authored effect was found but nothing could be "
                        + "seated from it - the round falls back to a borrowed plume.");
                return;
            }

            if (_logged.Add(def.jsonKey))
                Plugin.Log.LogInfo(
                    $"[Meridian] {def.jsonKey}: seated {seated} baked authored effect(s) from the prefab "
                    + $"itself on {nozzles.Count} nozzle(s). The borrowed plume is not used for this round.");
        }

        private static int SeatFromBake(List<Transform> nozzles, Array motors, int stage, string key)
        {
            object? motor = motors.GetValue(stage);
            if (motor == null) return 0;

            var systems = new List<ParticleSystem>();
            var lights = new List<Light>();
            var emitters = new List<MonoBehaviour>();
            int rebased = 0;
            string childName = BakedEffectPrefix + stage;

            Rigidbody? body = nozzles.Count > 0 ? nozzles[0].GetComponentInParent<Rigidbody>() : null;

            float burn = 10f;
            FieldInfo? fBurn = AccessTools.Field(motor.GetType(), "burnTime");
            if (fBurn != null && fBurn.GetValue(motor) is float t && t > 0f) burn = t + 1f;

            foreach (Transform nozzle in nozzles)
            {
                Transform? clone = nozzle.Find(childName);
                if (clone == null) continue;

                foreach (ParticleSystem ps in clone.GetComponentsInChildren<ParticleSystem>(true))
                {
                    ParticleSystem.MainModule main = ps.main;

                    if (main.simulationSpace == ParticleSystemSimulationSpace.World && Datum.origin != null)
                    {
                        main.simulationSpace = ParticleSystemSimulationSpace.Custom;
                        main.customSimulationSpace = Datum.origin;
                        rebased++;
                    }

                    systems.Add(ps);

                    if (ps.name == "Smoke" && PluginConfig.RibbonTrail)
                    {
                        MonoBehaviour? te = RibbonTrail.Convert(ps, body, burn);
                        if (te != null) emitters.Add(te);
                    }

                    if (ps.name == "Haze" && PluginConfig.HazeBorrow)
                        HazeBorrow.Apply(ps);
                }

                ShaderRebind.RebindTree(clone.gameObject);

                foreach (Light l in clone.GetComponentsInChildren<Light>(true))
                {
                    l.enabled = false;
                    lights.Add(l);
                }
            }

            if (systems.Count == 0 && lights.Count == 0) return 0;

            Assign(motor, "particleSystems", systems.ToArray());
            Assign(motor, "lights", lights.ToArray());

            if (emitters.Count > 0) Assign(motor, "trailEmitters", emitters.ToArray());

            Plugin.Diag(
                $"[Meridian] {key} stage {stage}: {emitters.Count} smoke system(s) converted to stock's "
                + $"carrier-and-ribbon trail, emitting one carrier every 30 m for {burn:0.0} s"
                + (emitters.Count == 0
                    ? " - NONE, so this stage still emits billboards on rateOverDistance and can still fork."
                    : ". rateOverDistance is off, so an origin shift can no longer spray a second arm."));

            if (rebased > 0 || Datum.origin == null)
                Plugin.Diag(
                    $"[Meridian] {key} stage {stage}: {rebased} world-space system(s) rebased onto Datum.origin"
                    + (Datum.origin == null
                        ? " - BUT Datum.origin IS NULL, so none of them could be, and the trail will be left "
                          + "behind by the next floating-origin shift."
                        : ", so the trail rides the floating-origin shift instead of being left behind by it."));

            return systems.Count + lights.Count;
        }

        private static void Assign(object motor, string field, Array values)
        {
            try
            {
                FieldInfo? f = AccessTools.Field(motor.GetType(), field);
                if (f == null) return;

                Type element = f.FieldType.GetElementType();
                if (element == null) return;

                Array typed = Array.CreateInstance(element, values.Length);
                for (int i = 0; i < values.Length; i++)
                    typed.SetValue(values.GetValue(i), i);

                f.SetValue(motor, typed);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning(
                    $"[Meridian] Could not write the authored effects into the motor's '{field}': {ex.Message}");
            }
        }

        private static List<Transform> FindNozzles(Missile ours)
        {
            var found = new List<(int Index, Transform T)>();

            foreach (Transform t in ours.GetComponentsInChildren<Transform>(true))
            {
                if (!t.name.StartsWith("Exhaust", StringComparison.OrdinalIgnoreCase)) continue;

                string suffix = t.name.Substring("Exhaust".Length);
                if (suffix.Length > 0 && !int.TryParse(suffix, out _)) continue;
                int index = int.TryParse(suffix, out int n) ? n : 0;
                found.Add((index, t));
            }

            found.Sort((a, b) => a.Index.CompareTo(b.Index));

            var ordered = new List<Transform>();
            foreach ((int _, Transform t) in found) ordered.Add(t);
            return ordered;
        }
    }
}
