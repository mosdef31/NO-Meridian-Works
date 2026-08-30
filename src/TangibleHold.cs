using System;
using HarmonyLib;
using UnityEngine;

namespace MeridianWorks
{
    internal sealed class TangibleHold : MonoBehaviour
    {

        private const float BombSeconds = 2f;

        private const float MissileSeconds = 1.5f;

        private Missile? _missile;
        private float _hold;

        internal static void Attach(Missile missile, string? jsonKey)
        {
            if (missile == null || !PluginInfo.IsOurMissileKey(jsonKey)) return;
            if (missile.GetComponent<TangibleHold>() != null) return;

            var h = missile.gameObject.AddComponent<TangibleHold>();
            h._missile = missile;
            h._hold = BombRelease.IsOurBomb(missile) ? BombSeconds : MissileSeconds;
        }

        private void FixedUpdate()
        {
            Missile m = _missile!;
            if (m == null || m.disabled) { enabled = false; return; }

            if (m.timeSinceSpawn < _hold)
            {

                if (m.IsTangible()) m.SetTangible(false);
                return;
            }

            if (!m.IsTangible()) m.SetTangible(true);
            enabled = false;
        }
    }

    [HarmonyPatch(typeof(Missile), "OnStartClient")]
    internal static class Missile_OnStartClient_TangibleHoldPatch
    {
        [HarmonyPostfix]
        private static void Postfix(Missile __instance)
        {
            try
            {
                if (__instance.definition is not MissileDefinition def) return;
                TangibleHold.Attach(__instance, def.jsonKey);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[Meridian] Tangible hold attach failed: {ex.Message}");
            }
        }
    }
}
