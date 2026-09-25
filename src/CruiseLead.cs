using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using System.Reflection.Emit;
using UnityEngine;

namespace MeridianWorks
{

    [HarmonyPatch(typeof(OpticalSeekerCruiseMissile), "TerrainWaypoint")]
    internal static class CruiseLead
    {

        private const float StockClamp = 0.17453292f;

        private const float StockLeadSeconds = 6f;

        private const float OurClamp = 0.5235988f;

        private const float MinLeadSeconds = 1f;

        private static FieldInfo? _missileField;
        private static bool _missileFieldLooked;

        public static float Clamp(OpticalSeekerCruiseMissile seeker)
        {
            try { return Ours(seeker) ? OurClamp : StockClamp; }
            catch { return StockClamp; }
        }

        public static float LeadSeconds(OpticalSeekerCruiseMissile seeker, GlobalPosition destination)
        {
            try
            {
                if (!Ours(seeker)) return StockLeadSeconds;

                Missile? missile = MissileOn(seeker);
                if (missile == null) return StockLeadSeconds;

                float speed = Mathf.Max(missile.speed, 100f);

                Vector3 flat = destination - missile.GlobalPosition();
                flat.y = 0f;

                float seconds = flat.magnitude / speed;
                return Mathf.Clamp(seconds, MinLeadSeconds, StockLeadSeconds);
            }
            catch
            {
                return StockLeadSeconds;
            }
        }

        private static bool Ours(OpticalSeekerCruiseMissile seeker)
        {
            if (seeker == null) return false;

            Missile? missile = MissileOn(seeker);
            if (missile == null) return false;

            string key = (missile.definition as MissileDefinition)?.jsonKey ?? missile.name;
            return PluginInfo.IsOurMissileKey(key);
        }

        private static Missile? MissileOn(OpticalSeekerCruiseMissile seeker)
        {
            if (!_missileFieldLooked)
            {
                _missileFieldLooked = true;
                _missileField = AccessTools.Field(typeof(MissileSeeker), "missile")
                                ?? AccessTools.Field(typeof(OpticalSeekerCruiseMissile), "missile");
            }

            return _missileField?.GetValue(seeker) as Missile;
        }

        private static void Postfix(OpticalSeekerCruiseMissile __instance,
                                    GlobalPosition destination,
                                    ref GlobalPosition __result)
        {
            try
            {
                float lead = LeadSeconds(__instance, destination);

                if (lead >= StockLeadSeconds) return;

                Missile? missile = MissileOn(__instance);
                if (missile == null) return;

                GlobalPosition here = missile.GlobalPosition();

                Vector3 guide = __result - here;
                guide.y *= lead / StockLeadSeconds;

                __result = here + guide;
            }
            catch
            {

            }
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var code = new List<CodeInstruction>(instructions);

            MethodInfo clamp = AccessTools.Method(typeof(CruiseLead), nameof(Clamp));
            MethodInfo lead = AccessTools.Method(typeof(CruiseLead), nameof(LeadSeconds));

            int clampHits = 0, leadHits = 0;

            for (int i = 0; i < code.Count; i++)
            {
                if (code[i].opcode != OpCodes.Ldc_R4) continue;
                if (!(code[i].operand is float f)) continue;

                bool isClamp = Mathf.Abs(f - StockClamp) < 0.0001f;
                bool isLead = Mathf.Abs(f - StockLeadSeconds) < 0.0001f;
                if (!isClamp && !isLead) continue;

                var load = new CodeInstruction(OpCodes.Ldarg_0);
                load.labels.AddRange(code[i].labels);
                load.blocks.AddRange(code[i].blocks);

                code[i] = load;

                if (isClamp)
                {
                    code.Insert(i + 1, new CodeInstruction(OpCodes.Call, clamp));
                    i += 1;
                    clampHits++;
                }
                else
                {

                    code.Insert(i + 1, new CodeInstruction(OpCodes.Ldarg_1));
                    code.Insert(i + 2, new CodeInstruction(OpCodes.Call, lead));
                    i += 2;
                    leadHits++;
                }
            }

            if (clampHits == 1 && leadHits == 1)
            {
                Plugin.Log.LogInfo(
                    "[Meridian] Cruise lead: OpticalSeekerCruiseMissile.TerrainWaypoint now reads "
                    + "its steering clamp and its lead time per missile. Our sea-skimmers aim AT a "
                    + "near waypoint instead of six seconds past it, and their turn is set by "
                    + "gLimit rather than by the engine's 10-degree governor. Stock cruise "
                    + "missiles are unchanged.");
                return code;
            }

            Plugin.Log.LogWarning(
                "[Meridian] Cruise lead: expected exactly one 0.1745 and one 6.0 constant in "
                + "OpticalSeekerCruiseMissile.TerrainWaypoint and found " + clampHits + " and "
                + leadHits + ". The method has changed, so NOTHING was patched and our "
                + "sea-skimmers keep the stock lead. Mid-flight waypoints still work; near legs "
                + "will be overflown as they were before 2026-09-14.");
            return instructions;
        }
    }
}
