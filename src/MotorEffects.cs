using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace MeridianWorks
{

    internal static class MotorEffects
    {
        private const string ContainerName = "Meridian_BorrowedEffects";

        private const float ReferenceNozzleRadius = 0.16f;

        private const float MinPlumeScale = 0.45f;

        private static readonly FieldInfo? _fMotors = AccessTools.Field(typeof(Missile), "motors");
        private static readonly FieldInfo? _fEffectsTransform =
            AccessTools.Field(typeof(Missile), "effectsTransform");

        private static readonly HashSet<string> _appliedLogged = new HashSet<string>();

        internal static void Apply(Missile ours)
        {
            if (ours == null) return;
            if (!IsOurs(ours)) return;
            if (_fMotors?.GetValue(ours) is not Array motors || motors.Length == 0) return;

            if (ours.transform.Find(ContainerName) != null) return;

            object? motor = motors.GetValue(0);
            if (MotorHasEffects(motor)) return;

            List<Transform> nozzles = FindNozzles(ours);
            if (nozzles.Count == 0)
            {
                Plugin.Log.LogWarning(
                    $"[Meridian] {Key(ours)}: no Exhaust transform on the prefab, so the plume has " +
                    "nowhere to attach and none was borrowed. The generator writes one empty per " +
                    "nozzle - re-run Meridian Works > Build weapons and re-export.");
                return;
            }

            Donor? pick = ChooseDonor(Key(ours));
            if (pick == null)
            {
                Plugin.Log.LogWarning(
                    "[Meridian] No stock missile with an effectsTransform was found, so no exhaust or " +
                    "trail could be borrowed. Every round in this pack will fly silent and invisible.");
                return;
            }

            int silenced = 0;
            foreach (ParticleSystem ps in ours.GetComponentsInChildren<ParticleSystem>(true))
            {
                if (ps.gameObject == ours.gameObject) continue;
                ps.gameObject.SetActive(false);
                silenced++;
            }
            foreach (TrailEmitter te in ours.GetComponentsInChildren<TrailEmitter>(true))
            {
                te.enabled = false;
                te.gameObject.SetActive(false);
            }

            var container = new GameObject(ContainerName);
            container.transform.SetParent(ours.transform, false);
            if (_fEffectsTransform?.GetValue(ours) as Transform == null)
                _fEffectsTransform?.SetValue(ours, container.transform);

            string ourKey = Key(ours);
            Recipes.TryGetValue(ourKey, out Recipe recipe);

            CloneOntoNozzles(ours, motor, pick.Value, recipe,
                             nozzles, container.transform, silenced);
        }

        private static bool IsOurs(Missile m)
        {

            var def = m.definition as MissileDefinition;
            return def != null && PluginInfo.IsOurMissileKey(def.jsonKey);
        }

        private static string Key(Missile m) => (m.definition as MissileDefinition)?.jsonKey ?? m.name;

        private static bool MotorHasEffects(object? motor)
        {
            if (motor == null) return false;
            foreach (string field in new[] { "particleSystems", "trailEmitters", "lights" })
            {
                if (AccessTools.Field(motor.GetType(), field)?.GetValue(motor) is Array a && a.Length > 0)
                    return true;
            }
            return false;
        }

        private static List<Transform> FindNozzles(Missile ours)
        {
            var found = new List<(int Index, Transform T)>();

            foreach (Transform t in ours.GetComponentsInChildren<Transform>(true))
            {
                if (!t.name.StartsWith("Exhaust", StringComparison.OrdinalIgnoreCase)) continue;
                int index = int.TryParse(t.name.Substring("Exhaust".Length), out int n) ? n : 0;
                found.Add((index, t));
            }

            return found.OrderBy(f => f.Index).Select(f => f.T).ToList();
        }

        private readonly struct Donor
        {
            internal Donor(Missile missile, Transform fx, string key, float burn)
            {
                Missile = missile; Fx = fx; Key = key; Burn = burn;
            }

            internal Missile Missile { get; }
            internal Transform Fx { get; }
            internal string Key { get; }
            internal float Burn { get; }
        }

        private readonly struct FlameLayer
        {
            internal FlameLayer(string donor, float scale, string? obj = null)
            {
                Donor = donor; Scale = scale; Object = obj;
            }

            internal string Donor { get; }
            internal float Scale { get; }

            internal string? Object { get; }
        }

        private readonly struct Recipe
        {
            internal Recipe(string plume, float splayDegrees, params FlameLayer[] flames)
            {
                Plume = plume; SplayDegrees = splayDegrees; Flames = flames;
            }

            internal string Plume { get; }

            internal float SplayDegrees { get; }

            internal FlameLayer[] Flames { get; }

            internal bool HasFlames => Flames != null && Flames.Length > 0;
        }

        private static readonly Dictionary<string, Recipe> Recipes =
            new Dictionary<string, Recipe>
            {

                { "MeridianAGM84_Missile",
                    new Recipe("Tusko-B (HE)", 0f) },

                { "MeridianAGM57L_Missile",
                    new Recipe("AGM-68", 0f,
                        new FlameLayer("AGM-48", 0.6f)) },

                { "MeridianAGM33L_Missile",
                    new Recipe("AGM-68", 8f,
                        new FlameLayer("AAM-29", 1f, "FireParticlesBooster")) },
            };

        private static readonly string[] FlameWords =
            { "fire", "flame", "muzzle", "ramjet", "fwoosh", "flash", "spark" };

        private static readonly string[] SmokeWords =
            { "smoke", "trail", "linger", "dust", "cloud", "ribbon" };

        private static bool Mentions(string name, string[] words)
        {
            foreach (string w in words)
                if (name.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        private static void KeepRole(GameObject clone, bool keepFlame, string? onlyNamed)
        {
            foreach (Transform t in clone.GetComponentsInChildren<Transform>(true))
            {
                if (t == clone.transform) continue;
                if (t.GetComponent<ParticleSystem>() == null &&
                    t.GetComponent<TrailEmitter>() == null) continue;

                bool keep;
                if (onlyNamed != null)
                {
                    keep = t.name.IndexOf(onlyNamed, StringComparison.OrdinalIgnoreCase) >= 0;
                }
                else
                {
                    bool flame = Mentions(t.name, FlameWords);
                    bool smoke = Mentions(t.name, SmokeWords);
                    keep = (flame == smoke) || (flame == keepFlame);
                }

                if (!keep) t.gameObject.SetActive(false);
            }
        }

        private static void SeatFlame(GameObject clone, Transform nozzle,
                                      Quaternion splay, float scale)
        {
            clone.transform.localRotation = splay;
            clone.transform.localScale = Vector3.one * scale;

            Vector3 origin = Vector3.zero;
            float back = float.MaxValue;
            foreach (ParticleSystem ps in clone.GetComponentsInChildren<ParticleSystem>(true))
            {
                if (!ps.gameObject.activeInHierarchy) continue;
                Vector3 local = clone.transform.InverseTransformPoint(ps.transform.position);
                if (local.z < back) { back = local.z; origin = local; }
            }

            clone.transform.localPosition =
                nozzle.localPosition - (splay * (origin * scale));
        }

        private static Quaternion SplayForFlame(Transform nozzle, int index, float degrees) =>
            Splay(nozzle, index, -degrees);

        private static Quaternion Splay(Transform nozzle, int index, float degrees)
        {
            if (Mathf.Abs(degrees) < 0.01f) return Quaternion.identity;

            float side = Mathf.Abs(nozzle.localPosition.x) > 0.001f
                ? Mathf.Sign(nozzle.localPosition.x)
                : (index % 2 == 0 ? 1f : -1f);

            return Quaternion.Euler(0f, side * degrees, 0f);
        }

        private static readonly HashSet<string> _donorLoggedFor = new HashSet<string>();

        private static Donor? ChooseDonor(string ourKey)
        {
            return ChooseDonor(ourKey, WantedPlume(ourKey), "plume");
        }

        private static string? WantedPlume(string ourKey) =>
            Recipes.TryGetValue(ourKey, out Recipe r) ? r.Plume : null;

        private static Donor? ChooseDonor(string ourKey, string? wanted, string role)
        {
            List<Donor> donors = FindDonors();
            if (donors.Count == 0) return null;

            Donor? named = null;
            if (!string.IsNullOrEmpty(wanted))
            {
                foreach (Donor d in donors)
                {
                    if (d.Key.IndexOf(wanted, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    named = d;
                    break;
                }

                if (named == null)
                    Plugin.Log.LogWarning(
                        $"[Meridian] {ourKey}: the {role} donor '{wanted}' the owner picked was not " +
                        "found, so one was chosen by burn time instead. Check whether that weapon " +
                        "has been renamed.");
            }

            const float wantedBurn = 4f;

            Donor best = named ?? donors
                .OrderByDescending(d => d.Key.IndexOf("AGM", StringComparison.OrdinalIgnoreCase) >= 0)
                .ThenBy(d => d.Burn > 0f ? Mathf.Abs(d.Burn - wantedBurn) : float.MaxValue)
                .First();

            if (_donorLoggedFor.Add(ourKey + "/" + role))
            {
                Plugin.Log.LogInfo(
                    $"[Meridian] {ourKey}: {role} donor '{best.Key}' (burn {best.Burn:0.#}s) " +
                    $"{(named != null ? "as picked by the owner" : "by burn time")}, out of " +
                    $"{donors.Count} candidate(s) with effects.");
            }

            return best;
        }

        private static List<Donor> FindDonors()
        {
            var found = new List<Donor>();

            Encyclopedia? enc = GameData.EncyclopediaOrNull();
            if (enc?.missiles == null) return found;

            foreach (MissileDefinition d in enc.missiles)
            {
                if (d == null || d.unitPrefab == null) continue;
                if (PluginInfo.IsOurMissileKey(d.jsonKey)) continue;

                var m = d.unitPrefab.GetComponent<Missile>();
                if (m == null) continue;
                if (_fEffectsTransform?.GetValue(m) is not Transform fx || fx == null) continue;

                found.Add(new Donor(m, fx, $"{d.jsonKey} {d.unitName}".Trim(), m.GetTotalBurnTime()));
            }

            return found;
        }

        private static void CloneOntoNozzles(Missile ours, object? motor, Donor donor,
                                             Recipe recipe,
                                             List<Transform> nozzles, Transform parent, int silenced)
        {
            if (motor == null) return;

            string ourKeyForLog = Key(ours);
            var particles = new List<ParticleSystem>();
            var trails = new List<TrailEmitter>();
            var lights = new List<Light>();
            var audio = new List<AudioSource>();

            var resolvedFlames = new List<string>();

            FieldInfo? fTrailSystem = AccessTools.Field(typeof(TrailEmitter), "trailSystem");
            FieldInfo? fEmitTransform = AccessTools.Field(typeof(TrailEmitter), "emitTransform");

            for (int i = 0; i < nozzles.Count; i++)
            {
                Transform nozzle = nozzles[i];

                Quaternion splay = Splay(nozzle, i, recipe.SplayDegrees);
                Quaternion flameSplay = SplayForFlame(nozzle, i, recipe.SplayDegrees);

                GameObject clone = UnityEngine.Object.Instantiate(donor.Fx.gameObject, parent);
                clone.name = $"Nozzle{i}_Plume";

                if (recipe.HasFlames) KeepRole(clone, keepFlame: false, onlyNamed: null);

                clone.transform.localPosition = nozzle.localPosition;
                clone.transform.localRotation = nozzle.localRotation * splay;
                clone.transform.localScale = Vector3.one * PlumeScale(nozzle);
                clone.SetActive(true);

                NormalizeSimulationSpace(clone, donor.Key);

                var cloneParticles = clone.GetComponentsInChildren<ParticleSystem>(true)
                    .Where(x => x.gameObject.activeInHierarchy).ToList();
                var cloneTrails = clone.GetComponentsInChildren<TrailEmitter>(true)
                    .Where(x => x.gameObject.activeInHierarchy).ToList();

                var trailOwned = new HashSet<ParticleSystem>();
                foreach (TrailEmitter te in cloneTrails)
                {
                    if (fTrailSystem?.GetValue(te) is ParticleSystem ts) trailOwned.Add(ts);

                    te.rb = ours.rb;
                    if (fEmitTransform?.GetValue(te) as Transform == null)
                        fEmitTransform?.SetValue(te, te.transform);
                    te.enabled = false;
                }
                cloneParticles.RemoveAll(p => trailOwned.Contains(p));

                particles.AddRange(cloneParticles);
                trails.AddRange(cloneTrails);

                var cloneLights = clone.GetComponentsInChildren<Light>(true).ToList();
                ScaleWorldSpaceEffects(clone, cloneLights, PlumeScale(nozzle));
                lights.AddRange(cloneLights);

                FlameLayer[] layers = recipe.Flames ?? Array.Empty<FlameLayer>();
                for (int f = 0; f < layers.Length; f++)
                {
                    FlameLayer layer = layers[f];
                    Donor? fd = ChooseDonor(ourKeyForLog, layer.Donor, $"flame{f}");
                    if (fd == null) continue;

                    float flameScale = PlumeScale(nozzle) * layer.Scale;

                    if (i == 0) resolvedFlames.Add(fd.Value.Key);

                    GameObject fc = UnityEngine.Object.Instantiate(fd.Value.Fx.gameObject, parent);
                    fc.name = $"Nozzle{i}_Flame{f}";
                    KeepRole(fc, keepFlame: true, onlyNamed: layer.Object);
                    fc.SetActive(true);

                    SeatFlame(fc, nozzle, flameSplay, flameScale);
                    NormalizeSimulationSpace(fc, fd.Value.Key);

                    var flameParticles = fc.GetComponentsInChildren<ParticleSystem>(true)
                        .Where(x => x.gameObject.activeInHierarchy).ToList();
                    var flameTrails = fc.GetComponentsInChildren<TrailEmitter>(true)
                        .Where(x => x.gameObject.activeInHierarchy).ToList();

                    var flameOwned = new HashSet<ParticleSystem>();
                    foreach (TrailEmitter te in flameTrails)
                    {
                        if (fTrailSystem?.GetValue(te) is ParticleSystem ts) flameOwned.Add(ts);
                        te.rb = ours.rb;
                        if (fEmitTransform?.GetValue(te) as Transform == null)
                            fEmitTransform?.SetValue(te, te.transform);
                        te.enabled = false;
                    }
                    flameParticles.RemoveAll(x => flameOwned.Contains(x));

                    particles.AddRange(flameParticles);
                    trails.AddRange(flameTrails);

                    var flameLights = fc.GetComponentsInChildren<Light>(true).ToList();
                    ScaleWorldSpaceEffects(fc, flameLights, flameScale);
                    lights.AddRange(flameLights);
                }

                if (i == 0)
                {
                    foreach (AudioSource src in DonorMotorAudio(donor.Missile))
                    {
                        AudioSource? dst = RebuildAudio(src, clone.transform, $"MotorAudio_{audio.Count}");
                        if (dst == null) continue;

                        ours.RegisterDopplerSound(dst);
                        audio.Add(dst);
                    }
                }
            }

            SetMotorArray(motor, "particleSystems", particles.ToArray());
            SetMotorArray(motor, "trailEmitters", trails.ToArray());
            SetMotorArray(motor, "lights", lights.ToArray());
            SetMotorArray(motor, "audioSources", audio.ToArray());

            string key = Key(ours);
            if (!_appliedLogged.Add(key)) return;

            Plugin.Log.LogInfo(
                $"[Meridian] {key}: borrowed a plume from '{donor.Key}'" +
                (recipe.HasFlames
                    ? " and " + recipe.Flames!.Length + " flame layer(s): " +
                      string.Join(", ", recipe.Flames!.Select((x, n) =>
                          (n < resolvedFlames.Count && !resolvedFlames[n].Contains(x.Donor)
                              ? $"{x.Donor} FELL BACK TO '{resolvedFlames[n]}'"
                              : x.Donor) +
                          (x.Object != null ? $" ({x.Object})" : "") +
                          $" x{x.Scale:0.00}")) +
                      (Mathf.Abs(recipe.SplayDegrees) > 0.01f
                          ? $", splayed {recipe.SplayDegrees:0.#} deg/nozzle" : "")
                    : "") +
                $" onto {nozzles.Count} nozzle(s) " +
                $"- {particles.Count} particle system(s), {trails.Count} trail emitter(s), " +
                $"{lights.Count} light(s), {audio.Count} sound(s)" +
                (silenced > 0 ? $"; silenced {silenced} authored system(s)." : "."));
        }

        private static float PlumeScale(Transform nozzle)
        {
            float radius = nozzle.localScale.x;
            if (radius <= 0.0001f) return 1f;
            return Mathf.Clamp(radius / ReferenceNozzleRadius, MinPlumeScale, 1f);
        }

        private static bool _scaleLogged;

        private static void ScaleWorldSpaceEffects(GameObject clone, List<Light> lights, float scale)
        {
            if (scale >= 0.999f) return;

            float lightScale = Mathf.Sqrt(scale);

            foreach (Light l in lights)
            {
                if (l == null) continue;
                l.range *= scale;
                l.intensity *= lightScale;
            }

            var ribbons = clone.GetComponentsInChildren<TrailRenderer>(true);
            foreach (TrailRenderer t in ribbons)
            {
                if (t == null) continue;
                t.widthMultiplier *= scale;
            }

            if (lights.Count == 0 && ribbons.Length == 0) return;
            if (_scaleLogged) return;
            _scaleLogged = true;

            Plugin.Log.LogInfo(
                $"[Meridian] The borrowed plume is drawn at x{scale:0.00}, so {lights.Count} light(s) " +
                $"were brought down with it - range x{scale:0.00}, intensity x{lightScale:0.00} - " +
                $"along with {ribbons.Length} trail ribbon(s). Both are world-space and ignore the " +
                "transform scale that shrinks the particles, which is why the glow used to outgrow " +
                "the flame.");
        }

        private static bool _spaceLogged;

        private static void NormalizeSimulationSpace(GameObject clone, string donorKey)
        {
            ParticleSystem[] systems = clone.GetComponentsInChildren<ParticleSystem>(true);
            int rebased = 0;

            foreach (ParticleSystem ps in systems)
            {
                if (ps == null) continue;

                ParticleSystem.MainModule main = ps.main;
                if (main.simulationSpace != ParticleSystemSimulationSpace.Custom) continue;

                main.simulationSpace = IsFlame(ps)
                    ? ParticleSystemSimulationSpace.Local
                    : ParticleSystemSimulationSpace.World;

                main.customSimulationSpace = null;
                rebased++;
            }

            if (_spaceLogged || rebased == 0) return;
            _spaceLogged = true;
            Plugin.Log.LogInfo(
                $"[Meridian] {rebased} of the {systems.Length} borrowed system(s) from '{donorKey}' " +
                "simulated in CUSTOM space, anchored to a transform on the donor rather than to our " +
                "round, and were re-based. That is the plume that starts behind the aircraft.");
        }

        private static bool IsFlame(ParticleSystem ps)
        {
            ParticleSystem.MainModule main = ps.main;
            return main.startLifetime.constantMax <= 0.5f;
        }

        private static AudioSource[] DonorMotorAudio(Missile donor)
        {
            if (_fMotors?.GetValue(donor) is not Array motors || motors.Length == 0)
                return Array.Empty<AudioSource>();

            object? m = motors.GetValue(0);
            if (m == null) return Array.Empty<AudioSource>();

            return AccessTools.Field(m.GetType(), "audioSources")?.GetValue(m) as AudioSource[]
                   ?? Array.Empty<AudioSource>();
        }

        private static AudioSource? RebuildAudio(AudioSource src, Transform parent, string name)
        {
            if (src == null || src.clip == null) return null;

            var go = new GameObject(name);
            go.transform.SetParent(parent, false);

            AudioSource dst = go.AddComponent<AudioSource>();
            dst.clip = src.clip;
            dst.outputAudioMixerGroup = src.outputAudioMixerGroup;
            dst.loop = src.loop;
            dst.volume = src.volume;
            dst.pitch = src.pitch;
            dst.spatialBlend = src.spatialBlend;
            dst.rolloffMode = src.rolloffMode;
            dst.minDistance = src.minDistance;
            dst.maxDistance = src.maxDistance;
            dst.dopplerLevel = src.dopplerLevel;
            dst.priority = src.priority;

            dst.playOnAwake = false;
            return dst;
        }

        private static void SetMotorArray(object motor, string field, Array value)
        {
            if (value.Length == 0) return;
            AccessTools.Field(motor.GetType(), field)?.SetValue(motor, value);
        }
    }

}
