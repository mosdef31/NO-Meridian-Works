using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace MeridianWorks
{
    internal sealed class FinDeploy : MonoBehaviour
    {

        private const float DefaultGuidanceDelay = 1f;

        private static readonly FieldInfo? FGuidanceDelay =
            AccessTools.Field(typeof(ARHSeeker), "guidanceDelay");

        private static readonly FieldInfo? FGuidance =
            AccessTools.Field(typeof(ARHSeeker), "guidance");

        private static readonly FieldInfo? FFoldingFins =
            AccessTools.Field(typeof(Missile), "foldingFins");

        private Missile? _missile;
        private ARHSeeker? _seeker;
        private float _delay = DefaultGuidanceDelay;
        private bool _done;

        internal static void Attach(Missile missile, string? jsonKey)
        {
            if (missile == null || !PluginInfo.IsOurMissileKey(jsonKey)) return;
            if (missile.GetComponent<FinDeploy>() != null) return;

            if (missile.GetComponent<ARHSeeker>() is not ARHSeeker seeker) return;
            if (FFoldingFins?.GetValue(missile) is not Array fins || fins.Length == 0) return;

            var f = missile.gameObject.AddComponent<FinDeploy>();
            f._missile = missile;
            f._seeker = seeker;
            f._delay = FGuidanceDelay?.GetValue(seeker) as float? ?? DefaultGuidanceDelay;
        }

        private void FixedUpdate()
        {
            Missile m = _missile!;
            if (m == null || m.disabled || _done) { enabled = false; return; }

            if (m.timeSinceSpawn <= _delay) return;

            _done = true;
            enabled = false;

            if (FGuidance?.GetValue(_seeker) is bool guiding && guiding) return;

            m.DeployFins();

            Plugin.Diag(
                $"[Meridian] FINS {name}: deployed at t+{m.timeSinceSpawn:0.##}s with no "
                + "track. ARHSeeker.Seek returns on an invalid targetID before it reaches "
                + "DeployFins, so an unlocked shot would otherwise fly folded.");
        }
    }

    [HarmonyPatch(typeof(Missile), "OnStartClient")]
    internal static class Missile_OnStartClient_FinDeployPatch
    {
        [HarmonyPostfix]
        private static void Postfix(Missile __instance)
        {
            try
            {
                if (__instance.definition is not MissileDefinition def) return;
                FinDeploy.Attach(__instance, def.jsonKey);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[Meridian] Fin deploy attach failed: {ex.Message}");
            }
        }
    }
}
