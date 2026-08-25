using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace MeridianWorks
{

    internal static class WarheadEffects
    {
        private const BindingFlags Inst =
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

        private static readonly string[] EffectFields =
        {
            "terrainEffect",
            "armorEffect",
            "underwaterEffect",
            "airEffect",
            "waterSurfaceEffect",
            "fizzleEffect",
        };

        private static bool _ran;

        internal static void RunOnce()
        {
            if (_ran) return;
            _ran = true;

            try
            {
                Apply();
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[Meridian] Warhead effect borrowing failed: {ex.Message}");
            }
        }

        private static void Apply()
        {
            FieldInfo? fWarhead = typeof(Missile).GetField("warhead", Inst);
            if (fWarhead == null)
            {
                Plugin.Log.LogError(
                    "[Meridian] Warhead effects: Missile has no 'warhead' field in this build. " +
                    "Re-check the decompile. Every round in this pack will throw on impact.");
                return;
            }

            IList<MissileDefinition> ours = EncyclopediaRegistration.ResolvedMissiles;
            if (ours.Count == 0)
            {
                Plugin.Log.LogWarning(
                    "[Meridian] Warhead effects: no missile definition is resolved yet, so there is " +
                    "nothing to fill. Expected if the bundle failed to load.");
                return;
            }

            foreach (MissileDefinition def in ours)
            {
                object? donor = FindDonor(fWarhead, OurYield(def, fWarhead), out string donorName);
                ApplyTo(def, fWarhead, donor, donorName);
            }
        }

        private static void ApplyTo(MissileDefinition? def, FieldInfo fWarhead,
                                    object? donorWarhead, string donorName)
        {
            string who = def?.jsonKey ?? "(null definition)";

            GameObject? prefab = def != null ? def.unitPrefab : null;
            if (prefab == null)
            {
                Plugin.Log.LogWarning($"[Meridian] Warhead effects: {who} has no unitPrefab.");
                return;
            }

            var missile = prefab.GetComponent<Missile>();
            if (missile == null)
            {
                Plugin.Log.LogWarning($"[Meridian] Warhead effects: {who}'s prefab has no Missile.");
                return;
            }

            object? warhead = fWarhead.GetValue(missile);
            if (warhead == null)
            {
                Plugin.Log.LogWarning($"[Meridian] Warhead effects: {who}'s warhead is null.");
                return;
            }

            FieldInfo[] fields = EffectFields
                .Select(n => warhead.GetType().GetField(n, Inst))
                .Where(f => f != null)
                .Cast<FieldInfo>()
                .ToArray();

            if (fields.Length == 0)
            {
                Plugin.Log.LogError(
                    $"[Meridian] Warhead effects: none of the effect fields resolved on {who}.");
                return;
            }

            string[] missing = fields
                .Where(f => f.GetValue(warhead) as GameObject == null)
                .Select(f => f.Name)
                .ToArray();

            if (missing.Length == 0)
            {
                Plugin.Log.LogInfo($"[Meridian] {who}: warhead effects all set in the bundle.");
                return;
            }

            if (donorWarhead == null)
            {
                Plugin.Log.LogError(
                    $"[Meridian] {who}: no stock warhead to borrow from and {missing.Length} field(s) " +
                    $"are unset ({string.Join(", ", missing)}). terrainEffect, armorEffect and " +
                    "underwaterEffect are NOT null-checked by Missile+Warhead.Detonate, so this round " +
                    "will throw on impact and do nothing at all. Assign them in Unity.");
                return;
            }

            int filled = 0;
            foreach (FieldInfo f in fields)
            {
                if (f.GetValue(warhead) as GameObject != null) continue;
                var donated = f.GetValue(donorWarhead) as GameObject;
                if (donated == null) continue;
                f.SetValue(warhead, donated);
                filled++;
            }

            fWarhead.SetValue(missile, warhead);

            Plugin.Log.LogInfo(
                $"[Meridian] {who}: filled {filled} of {missing.Length} unset warhead effect(s) " +
                $"({string.Join(", ", missing)}) from '{donorName}'.");

            string[] stillNull = fields
                .Take(3)
                .Where(f => f.GetValue(warhead) as GameObject == null)
                .Select(f => f.Name)
                .ToArray();

            if (stillNull.Length > 0)
                Plugin.Log.LogError(
                    $"[Meridian] {who}: STILL UNSET after borrowing: {string.Join(", ", stillNull)}. " +
                    "The game does not null-check these, so the round will throw on impact and do no " +
                    "damage. Assign them in Unity.");
        }

        private static object? FindDonor(FieldInfo fWarhead, float ourYield, out string donorName)
        {
            donorName = "(none)";

            Encyclopedia? enc = GameData.EncyclopediaOrNull();
            if (enc?.missiles == null) return null;

            object? best = null;
            float bestScore = float.MinValue;

            foreach (MissileDefinition d in enc.missiles)
            {
                if (d == null || d.unitPrefab == null) continue;
                if (PluginInfo.IsOurMissileKey(d.jsonKey)) continue;

                var m = d.unitPrefab.GetComponent<Missile>();
                if (m == null) continue;

                object? w = fWarhead.GetValue(m);
                if (w == null) continue;

                int set = EffectFields.Count(n =>
                    w.GetType().GetField(n, Inst)?.GetValue(w) as GameObject != null);
                if (set == 0) continue;

                string match = $"{d.jsonKey} {d.unitName}";

                float theirs = Yield(w);
                float closeness = (ourYield > 0f && theirs > 0f)
                    ? Mathf.Min(ourYield, theirs) / Mathf.Max(ourYield, theirs)
                    : 0f;

                float score = closeness * 100f
                            + set
                            + (match.IndexOf("AGM", StringComparison.OrdinalIgnoreCase) >= 0 ? 5f : 0f);

                if (score <= bestScore) continue;
                bestScore = score;
                best = w;
                donorName = !string.IsNullOrEmpty(d.unitName) ? d.unitName : d.jsonKey;
            }

            return best;
        }

        private static float OurYield(MissileDefinition? def, FieldInfo fWarhead)
        {
            if (def == null || def.unitPrefab == null) return 0f;
            var m = def.unitPrefab.GetComponent<Missile>();
            return m == null ? 0f : Yield(fWarhead.GetValue(m));
        }

        private static float Yield(object? warhead)
        {
            if (warhead == null) return 0f;
            return warhead.GetType().GetField("blastYield", Inst)?.GetValue(warhead) as float? ?? 0f;
        }
    }
}
