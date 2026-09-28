using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace MeridianWorks
{

    internal static class HSM160Rounds
    {
        internal static readonly HashSet<string> Ours = new HashSet<string>
        {
            "MeridianHSM160_Missile", "MeridianHSM160N_Missile", "MeridianHSM160C_Missile",
            "MeridianHSM160M_Missile",
        };
    }

    [HarmonyPatch(typeof(VLSBooster), "VLSBooster_OnInitialize")]
    internal static class HSM160Booster
    {

        private const float HSM160BurnTimeSeconds = 24f;
        private const float HSM160ThrustNewtons = 55000f;

        private static readonly FieldInfo? FMissile = AccessTools.Field(typeof(VLSBooster), "missile");
        private static readonly MethodInfo? MOnInit = AccessTools.Method(typeof(VLSBooster), "VLSBooster_OnInitialize");
        private static readonly FieldInfo? FBurnTime = AccessTools.Field(typeof(VLSBooster), "burnTime");
        private static readonly FieldInfo? FThrust = AccessTools.Field(typeof(VLSBooster), "thrust");
        private static readonly FieldInfo? FFuelMass = AccessTools.Field(typeof(VLSBooster), "fuelMass");
        private static readonly FieldInfo? FBurnRate = AccessTools.Field(typeof(VLSBooster), "burnRate");
        private static readonly FieldInfo? FDryMass = AccessTools.Field(typeof(VLSBooster), "dryMass");

        internal readonly struct BoosterState
        {
            internal readonly float ThrustN;
            internal readonly float BurnRateKgS;
            internal readonly float RemainingFuelKg;
            internal readonly float DryMassKg;
            internal BoosterState(float thrustN, float burnRateKgS, float remainingFuelKg, float dryMassKg)
            {
                ThrustN = thrustN; BurnRateKgS = burnRateKgS; RemainingFuelKg = remainingFuelKg; DryMassKg = dryMassKg;
            }
        }

        internal static BoosterState? ReadState(Missile m)
        {
            VLSBooster? b = m.GetComponentInChildren<VLSBooster>(true);
            if (b == null || FThrust == null || FFuelMass == null || FBurnRate == null || FDryMass == null) return null;
            if (FThrust.GetValue(b) is not float thrust) return null;
            if (FFuelMass.GetValue(b) is not float fuel) return null;
            if (FBurnRate.GetValue(b) is not float burnRate) return null;
            if (FDryMass.GetValue(b) is not float dry) return null;
            return new BoosterState(thrust, burnRate, fuel, dry);
        }

        private static bool _logged;
        private static bool _burnLogged;

        private static void Unsubscribe(VLSBooster b, Missile m)
        {
            if (MOnInit == null) return;
            var handler = (Action)Delegate.CreateDelegate(typeof(Action), b, MOnInit);
            m.onInitialize -= handler;
        }

        private static bool Prefix(VLSBooster __instance)
        {
            try
            {
                if (FMissile?.GetValue(__instance) is not Missile m) return true;
                if ((m.definition as MissileDefinition)?.jsonKey is not string key || !HSM160Rounds.Ours.Contains(key))
                    return true;

                if (GameManager.gameState == GameState.Encyclopedia)
                {
                    Unsubscribe(__instance, m);
                    m.boosterIsAttached = false;
                    __instance.enabled = false;
                    return false;
                }
                if (m.owner is not Aircraft) return true;

                Unsubscribe(__instance, m);
                m.boosterIsAttached = true;

                FBurnTime?.SetValue(__instance, HSM160BurnTimeSeconds);
                FThrust?.SetValue(__instance, HSM160ThrustNewtons);
                if (FFuelMass?.GetValue(__instance) is float fuelMass && FBurnRate != null)
                {
                    float burnRate = fuelMass / HSM160BurnTimeSeconds;
                    FBurnRate.SetValue(__instance, burnRate);
                    if (!_burnLogged)
                    {
                        _burnLogged = true;
                        Plugin.Log.LogInfo($"[Meridian] {key}: booster retuned - burnTime={HSM160BurnTimeSeconds:F0}s thrust={HSM160ThrustNewtons:F0}N fuelMass={fuelMass:F0}kg burnRate={burnRate:F2}kg/s");
                    }
                }

                if (!_logged)
                {
                    _logged = true;
                    Plugin.Log.LogInfo($"[Meridian] {key}: booster kept attached at launch (aircraft owner).");
                }
                return false;
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("[Meridian] HSM-160 booster air-launch check failed, stock behaviour kept: " + e.Message);
                return true;
            }
        }
    }
}
