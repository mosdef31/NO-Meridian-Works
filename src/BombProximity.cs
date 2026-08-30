using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace MeridianWorks
{
    internal sealed class BombProximity : MonoBehaviour
    {

        private const float ArmRadius = 6f;

        private Missile? _missile;
        private bool _set;

        private static readonly FieldInfo? FTargetUnit =
            AccessTools.Field(typeof(MissileSeeker), "targetUnit");

        internal static void Attach(Missile missile, string? jsonKey)
        {
            if (missile == null || !PluginInfo.IsOurMissileKey(jsonKey)) return;
            if (!BombRelease.IsOurBomb(missile)) return;
            if (missile.GetComponent<BombProximity>() != null) return;

            missile.gameObject.AddComponent<BombProximity>()._missile = missile;
        }

        private void FixedUpdate()
        {
            Missile m = _missile!;
            if (m == null || m.disabled) { enabled = false; return; }
            if (_set) { enabled = false; return; }
            if (!m.IsArmed()) return;

            MissileSeeker? seeker = m.GetComponentInChildren<MissileSeeker>(true);
            if (seeker == null) return;
            if (FTargetUnit?.GetValue(seeker) is not Unit target || target == null) return;

            Transform t = target.maxRadius > 20f && target.GetRandomPart() != null
                ? target.GetRandomPart().transform
                : target.transform;
            if (t == null) return;

            if (!FastMath.InRange(t.position, m.transform.position, ArmRadius)) return;

            m.SetProxyFuse(t, target.rb);
            _set = true;
            enabled = false;

            Plugin.Diag(
                $"[Meridian] PROX {name}: within {ArmRadius:0} m of '{target.name}', so the "
                + "engine's closest-approach fuse was armed. It fires on the frame this "
                + "round stops closing, which is the frame a bounce would have started.");
        }
    }

    [HarmonyPatch(typeof(Missile), "OnStartClient")]
    internal static class Missile_OnStartClient_BombProximityPatch
    {
        [HarmonyPostfix]
        private static void Postfix(Missile __instance)
        {
            try
            {
                if (__instance.definition is not MissileDefinition def) return;
                BombProximity.Attach(__instance, def.jsonKey);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[Meridian] Bomb proximity fuse attach failed: {ex.Message}");
            }
        }
    }
}
