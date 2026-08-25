using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace MeridianWorks
{

    internal sealed class PlumeCut : MonoBehaviour
    {
        private Missile? _missile;
        private bool _wasBurning;
        private bool _done;

        private static readonly FieldInfo? FMotors = AccessTools.Field(typeof(Missile), "motors");

        internal static void Attach(Missile m)
        {
            if (m == null || m.GetComponent<PlumeCut>() != null) return;
            m.gameObject.AddComponent<PlumeCut>()._missile = m;
        }

        private void Start()
        {

            if (GameManager.gameState == GameState.Encyclopedia) Destroy(this);
        }

        private void FixedUpdate()
        {
            Missile? m = _missile;
            if (m == null || m.disabled || _done) { enabled = false; return; }

            bool burning = m.EngineOn();
            if (burning) { _wasBurning = true; return; }
            if (!_wasBurning) return;

            _done = true;
            enabled = false;
            Cut(m);
        }

        private static void Cut(Missile m)
        {
            try
            {
                if (FMotors?.GetValue(m) is not Array motors) return;

                foreach (object motor in motors)
                {
                    if (motor == null) continue;
                    AccessTools.Method(motor.GetType(), "Burnout", new[] { typeof(bool) })
                              ?.Invoke(motor, new object[] { true });
                }
            }
            catch (Exception ex)
            {

                Plugin.Log.LogWarning($"[Meridian] Could not force the motor plume to cut: {ex.Message}");
            }
        }
    }

    [HarmonyPatch(typeof(Missile), "OnStartClient")]
    internal static class Missile_OnStartClient_RoundSetupPatch
    {
        [HarmonyPostfix]
        private static void Postfix(Missile __instance)
        {
            try
            {
                MotorEffects.Apply(__instance);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[Meridian] Motor effect cloning threw: {ex.Message}");
            }

            try
            {
                if (__instance.definition is MissileDefinition def &&
                    PluginInfo.IsOurMissileKey(def.jsonKey))
                {
                    PlumeCut.Attach(__instance);
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[Meridian] Attaching the plume cut threw: {ex.Message}");
            }
        }
    }
}
