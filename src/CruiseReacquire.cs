using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace MeridianWorks
{

    internal static class CruiseReacquire
    {

        private static readonly List<Unit> _near = new List<Unit>(64);

        private static readonly FieldInfo? _fTargetUnit =
            AccessTools.Field(typeof(MissileSeeker), "targetUnit");

        private static readonly FieldInfo? _fMissile =
            AccessTools.Field(typeof(MissileSeeker), "missile");

        private static readonly FieldInfo? _fSearchRadius =
            AccessTools.Field(typeof(OpticalSeekerCruiseMissile), "terminalSearchRadius");

        private static readonly FieldInfo? _fKnownPos =
            AccessTools.Field(typeof(OpticalSeekerCruiseMissile), "knownPos");

        private static readonly FieldInfo? _fTargetPart =
            AccessTools.Field(typeof(OpticalSeekerCruiseMissile), "targetPart");

        private static readonly HashSet<string> _logged = new HashSet<string>();

        private static bool Ready =>
            _fTargetUnit != null && _fMissile != null && _fSearchRadius != null
            && _fKnownPos != null && _fTargetPart != null;

        internal static void Consider(OpticalSeekerCruiseMissile seeker)
        {
            if (!Ready || seeker == null) return;

            if (_fMissile!.GetValue(seeker) is not Missile missile || missile == null) return;

            string key = (missile.definition as MissileDefinition)?.jsonKey ?? "";
            if (!PluginInfo.IsOurMissileKey(key)) return;

            if (!missile.IsServer) return;

            Unit? current = _fTargetUnit!.GetValue(seeker) as Unit;
            if (current != null && !current.disabled) return;

            if (missile.timeSinceSpawn < 10f) return;

            float radius = (float)_fSearchRadius!.GetValue(seeker);
            if (radius <= 0f) return;

            if (_fKnownPos!.GetValue(seeker) is not GlobalPosition known) return;

            Unit? pick = Nearest(missile, known, radius);
            if (pick == null) return;

            _fTargetUnit.SetValue(seeker, pick);
            _fTargetPart!.SetValue(seeker, pick.GetRandomPart());

            if (_logged.Add(key))
                Plugin.Diag(
                    $"[Meridian] {key}: its target was gone, so it took "
                    + $"'{pick.unitName}' instead - the nearest thing it is allowed to hit "
                    + $"within terminalSearchRadius ({radius:0} m) of where the first one "
                    + "was. Without this the round detonates itself in SlowChecks.");
        }

        private static Unit? Nearest(Missile missile, GlobalPosition known, float radius)
        {
            WeaponInfo? info = missile.GetWeaponInfo();
            if (info == null) return null;
            TargetRequirements need = info.targetRequirements;

            _near.Clear();
            try { BattlefieldGrid.GetUnitsInRangeNonAlloc(known, radius, _near); }
            catch { return null; }

            Unit? best = null;
            float bestSq = float.MaxValue;

            foreach (Unit u in _near)
            {
                if (u == null || u.disabled) continue;

                if (u.NetworkHQ == null || u.NetworkHQ == missile.NetworkHQ) continue;

                if (u.speed > need.maxSpeed) continue;
                if (u.radarAlt > need.maxAltitude) continue;
                if (u.definition != null
                    && info.armorTierEffectiveness < u.definition.armorTier) continue;

                float d = (u.GlobalPosition() - known).sqrMagnitude;
                if (d >= bestSq) continue;

                bestSq = d;
                best = u;
            }

            return best;
        }
    }

    [HarmonyPatch]
    internal static class OpticalSeekerCruiseMissile_SlowChecks_ReacquirePatch
    {

        private static MethodBase? TargetMethod()
        {
            MethodInfo? m = AccessTools.Method(
                typeof(OpticalSeekerCruiseMissile), "SlowChecks");

            if (m == null)
                Plugin.Log.LogWarning(
                    "[Meridian] OpticalSeekerCruiseMissile.SlowChecks was not found, so our "
                    + "cruise rounds keep the stock behaviour of detonating themselves when "
                    + "their target is gone. See CruiseReacquire.");

            return m;
        }

        private static bool Prepare() => TargetMethod() != null;

        private static void Prefix(OpticalSeekerCruiseMissile __instance)
        {
            try { CruiseReacquire.Consider(__instance); }
            catch (Exception e)
            {

                Plugin.Log.LogWarning(
                    "[Meridian] Cruise re-acquisition threw and was skipped: " + e.Message);
            }
        }
    }
}
