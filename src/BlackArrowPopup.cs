using System;
using System.Reflection;
using HarmonyLib;

namespace MeridianWorks
{

    [HarmonyPatch(typeof(OpticalSeekerCruiseMissile), "Initialize")]
    internal static class BlackArrowPopup
    {
        private const string Key = "MeridianBlackArrow_Missile";

        private static readonly FieldInfo? FTopAttack =
            AccessTools.Field(typeof(OpticalSeekerCruiseMissile), "topAttack");
        private static readonly FieldInfo? FMissile =
            AccessTools.Field(typeof(MissileSeeker), "missile");

        private static bool _logged;

        private static void Prefix(OpticalSeekerCruiseMissile __instance, out float __state)
        {
            __state = 0f;
            try
            {
                if (FTopAttack?.GetValue(__instance) is TopAttack t && IsOurs(__instance))
                    __state = t.Amount;
            }
            catch (Exception) { }
        }

        private static void Postfix(OpticalSeekerCruiseMissile __instance, Unit target, float __state)
        {
            if (__state <= 0f || target == null) return;
            try
            {
                if (FTopAttack?.GetValue(__instance) is not TopAttack t || t.Amount > 0f) return;

                if (target.maxRadius >= 20f) return;
                if (FMissile?.GetValue(__instance) is not Missile m) return;
                if (FastMath.InRange(target.GlobalPosition(), m.GlobalPosition(), t.TooCloseRange)) return;

                t.Amount = __state;
                if (!_logged)
                {
                    _logged = true;
                    Plugin.Log.LogInfo($"[Meridian] Black Arrow pop-up kept against a vehicle (Amount {__state}).");
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("[Meridian] Black Arrow pop-up skipped: " + e.Message);
            }
        }

        private static bool IsOurs(OpticalSeekerCruiseMissile seeker)
        {
            return FMissile?.GetValue(seeker) is Missile m
                && (m.definition as MissileDefinition)?.jsonKey == Key;
        }
    }
}
