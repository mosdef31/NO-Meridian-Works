using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace MeridianWorks
{

    internal static class TurnRateCompat
    {

        internal const float DefaultMaxTurnRateDegPerSec = 20f;

        private static bool _applied;

        internal static void Apply(IEnumerable<MissileDefinition> definitions)
        {
            if (_applied) return;
            _applied = true;

            FieldInfo? fRate = AccessTools.Field(typeof(Missile), "maxTurnRate");
            FieldInfo? fLimit = AccessTools.Field(typeof(Missile), "gLimit");

            if (fRate == null)
            {

                Plugin.Diag(
                    "[Meridian] Missile has no 'maxTurnRate' field, so no turn rate was set. " +
                    "That is correct on 0.34 and earlier, where the field does not exist. On " +
                    "0.34.2 or later it means the field was renamed, and the rounds will not turn.");
                return;
            }

            foreach (MissileDefinition def in definitions)
            {
                if (def == null || def.jsonKey == null) continue;

                Missile? missile = MissileOn(def);
                if (missile == null) continue;

                float rate = (float)(fRate.GetValue(missile) ?? 0f);
                float limit = fLimit?.GetValue(missile) is float g ? g : 0f;

                if (rate > 0f) continue;
                if (limit <= 0f) continue;

                if (!PluginInfo.TurnRates.TryGetValue(def.jsonKey, out float want))
                {
                    want = DefaultMaxTurnRateDegPerSec;
                    Plugin.Log.LogWarning(
                        $"[Meridian] {def.jsonKey}: no turn rate listed in PluginInfo.TurnRates, " +
                        $"so it falls back to {want:0.#} deg/s. That is the heavy-AGM value and " +
                        "it is wrong for anything air-to-air. Add the round to the table.");
                }

                fRate.SetValue(missile, want);

                Plugin.Log.LogInfo(
                    $"[Meridian] {def.jsonKey}: maxTurnRate 0 -> {want:0.#} deg/s. " +
                    $"0.34.2 clamps the turn to min(maxTurnRate, 9.81 * gLimit / speed), so with " +
                    $"gLimit {limit:0.#} and no rate the allowance is zero and the round flies " +
                    "straight.");
            }
        }

        private static Missile? MissileOn(MissileDefinition def) =>
            def.unitPrefab != null ? def.unitPrefab.GetComponent<Missile>() : null;
    }
}
