using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace MeridianWorks
{

    [HarmonyPatch(typeof(OpticalSeekerCruiseMissile), "TerminalMode")]
    internal static class SkimmerDrop
    {
        private const float Stock = 4.905f;

        private static FieldInfo? _missileField;
        private static bool _missileFieldLooked;

        public static float HalfG(OpticalSeekerCruiseMissile seeker)
        {
            try
            {
                if (seeker == null) return Stock;

                if (!_missileFieldLooked)
                {
                    _missileFieldLooked = true;
                    _missileField = AccessTools.Field(typeof(MissileSeeker), "missile")
                                    ?? AccessTools.Field(typeof(OpticalSeekerCruiseMissile), "missile");
                }

                var missile = _missileField?.GetValue(seeker) as Missile;
                if (missile == null) return Stock;

                string key = (missile.definition as MissileDefinition)?.jsonKey ?? missile.name;
                return PluginInfo.IsOurMissileKey(key) ? 0f : Stock;
            }
            catch
            {

                return Stock;
            }
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var code = new List<CodeInstruction>(instructions);
            MethodInfo halfG = AccessTools.Method(typeof(SkimmerDrop), nameof(HalfG));

            int hits = 0;
            for (int i = 0; i < code.Count; i++)
            {
                if (code[i].opcode != OpCodes.Ldc_R4) continue;
                if (!(code[i].operand is float f)) continue;
                if (Mathf.Abs(f - Stock) > 0.0001f) continue;

                var load = new CodeInstruction(OpCodes.Ldarg_0);
                load.labels.AddRange(code[i].labels);
                load.blocks.AddRange(code[i].blocks);

                code[i] = load;
                code.Insert(i + 1, new CodeInstruction(OpCodes.Call, halfG));
                i++;
                hits++;
            }

            if (hits == 1)
            {
                Plugin.Log.LogInfo(
                    "[Meridian] Sea-skimmer terminal: the half-g drop compensation in "
                    + "OpticalSeekerCruiseMissile.TerminalMode is now read per missile, so our "
                    + "rounds hold their skim into the target instead of popping up over it. "
                    + "Stock cruise missiles are unchanged.");
            }
            else
            {
                Plugin.Log.LogWarning(
                    "[Meridian] Sea-skimmer terminal: expected exactly one 4.905 constant in "
                    + "OpticalSeekerCruiseMissile.TerminalMode and found " + hits + ". The method "
                    + "has changed, so NOTHING was patched and our sea-skimmers keep the stock "
                    + "terminal pop-up. This is cosmetic, not a fault.");

                if (hits > 1) return instructions;
            }

            return code;
        }
    }
}
