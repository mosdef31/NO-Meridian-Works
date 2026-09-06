using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace MeridianWorks
{
    internal static class LaserLockHold
    {

        private const float HoldSeconds = 3f;

        private static readonly Dictionary<int, (int target, float since)> _held = new();

        private static bool FlownByAI(Unit owner)
        {
            if (owner is not Aircraft aircraft || aircraft.pilots == null) return false;

            foreach (Pilot? p in aircraft.pilots)
                if (p != null && p.playerControlled && !p.dead && !p.ejected)
                    return false;

            return true;
        }

        internal static bool ShouldHold(MissileLauncher launcher, Unit owner, Unit target,
                                        out string why)
        {
            why = "";

            if (launcher == null || owner == null || target == null) return false;
            if (launcher.info == null || !launcher.info.laserGuided) return false;
            if (launcher.missile == null) return false;
            if (!PluginInfo.IsOurMissileKey(launcher.missile.jsonKey)) return false;
            if (!FlownByAI(owner)) return false;

            var designators = owner.GetComponentsInChildren<LaserDesignator>(true);
            if (designators == null || designators.Length == 0) return false;

            foreach (LaserDesignator? d in designators)
                if (d != null && d.IsLased(target)) return false;

            int key = launcher.GetInstanceID();
            int tid = target.GetInstanceID();
            float now = Time.timeSinceLevelLoad;

            if (!_held.TryGetValue(key, out var state) || state.target != tid)
            {
                _held[key] = (tid, now);
                why = "the target is not lased yet, so the release is held";
                return true;
            }

            float waited = now - state.since;
            if (waited < HoldSeconds)
            {
                why = $"the target is not lased yet, so the release is held ({waited:0.0}s)";
                return true;
            }

            _held.Remove(key);
            why = $"the target was still not lased after {waited:0.0}s, so the shot was released "
                  + "unlased rather than held any longer";
            return false;
        }

        internal static void Clear(MissileLauncher launcher)
        {
            if (launcher != null) _held.Remove(launcher.GetInstanceID());
        }
    }

    [HarmonyPatch(typeof(MissileLauncher), nameof(MissileLauncher.Fire))]
    internal static class MissileLauncher_Fire_LaserLockPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(MissileLauncher __instance, Unit owner, Unit target)
        {
            try
            {
                bool hold = LaserLockHold.ShouldHold(__instance, owner, target, out string why);

                if (why.Length > 0)
                    Plugin.Diag($"[Meridian] LASE {__instance.missile?.jsonKey}: {why}.");

                if (hold) return false;

                LaserLockHold.Clear(__instance);
                return true;
            }
            catch (Exception ex)
            {

                Plugin.Log.LogWarning($"[Meridian] Laser lock hold failed: {ex.Message}");
                return true;
            }
        }
    }
}
