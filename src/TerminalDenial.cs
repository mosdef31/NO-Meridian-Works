using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace MeridianWorks
{
    internal static class TerminalDenial
    {

        private const float TerminalSeconds = 4f;

        private const float MinClosing = 50f;

        private static readonly List<Missile> _live = new List<Missile>();

        private static readonly FieldInfo? FTarget =
            AccessTools.Field(typeof(Missile), "target");

        private static readonly FieldInfo? FAircraft =
            AccessTools.Field(typeof(PilotBaseState), "aircraft");

        internal static void Register(Missile missile, string jsonKey)
        {
            if (!PluginInfo.TerminalDenialKeys.Contains(jsonKey)) return;
            _live.Add(missile);
        }

        internal static bool TerminalAgainst(Unit aircraft)
        {
            if (aircraft == null) return false;

            bool terminal = false;

            for (int i = _live.Count - 1; i >= 0; i--)
            {
                Missile m = _live[i];
                if (m == null || m.disabled) { _live.RemoveAt(i); continue; }
                if (terminal) continue;

                Unit? t = FTarget?.GetValue(m) as Unit;
                if (t == null || t != aircraft) continue;

                Vector3 toTarget = aircraft.transform.position - m.transform.position;
                float closing = Vector3.Dot(m.rb.velocity - aircraft.rb.velocity,
                                            toTarget.normalized);
                float timeToGo = toTarget.magnitude / Mathf.Max(closing, MinClosing);

                if (timeToGo <= TerminalSeconds) terminal = true;
            }

            return terminal;
        }

        internal static Aircraft? AircraftOf(PilotBaseState state) =>
            FAircraft?.GetValue(state) as Aircraft;
    }

    [HarmonyPatch(typeof(AIPilotCombatModes), "AIPilotCombatState_On1sInterval")]
    internal static class AIPilotCombatModes_On1sInterval_TerminalDenialPatch
    {
        private static bool _logged;

        [HarmonyPrefix]
        private static bool Prefix(AIPilotCombatModes __instance)
        {
            try
            {
                Aircraft? ac = TerminalDenial.AircraftOf(__instance);
                if (ac == null) return true;
                if (!TerminalDenial.TerminalAgainst(ac)) return true;

                if (!_logged)
                {
                    _logged = true;
                    Plugin.Diag("[Meridian] TERMINAL: an AI aircraft's flare pop was held "
                                + "while an IRM-L7 was inside its last seconds. This line "
                                + "is printed once per session.");
                }

                return false;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[Meridian] Terminal denial failed: {ex.Message}");
                return true;
            }
        }
    }

    [HarmonyPatch(typeof(Missile), "OnStartClient")]
    internal static class Missile_OnStartClient_TerminalDenialPatch
    {
        [HarmonyPostfix]
        private static void Postfix(Missile __instance)
        {
            try
            {
                if (__instance.definition is not MissileDefinition def) return;
                TerminalDenial.Register(__instance, def.jsonKey);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[Meridian] Terminal denial register failed: {ex.Message}");
            }
        }
    }
}
