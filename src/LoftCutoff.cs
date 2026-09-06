using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace MeridianWorks
{
    internal static class LoftCutoff
    {
        private static readonly FieldInfo? FLoft =
            AccessTools.Field(typeof(ARHSeeker), "loftAmount");

        private static readonly FieldInfo? FTerminal =
            AccessTools.Field(typeof(ARHSeeker), "terminalRange");

        private static readonly FieldInfo? FTimeToTarget =
            AccessTools.Field(typeof(ARHSeeker), "timeToTarget");

        private static readonly FieldInfo? FTargetUnit =
            AccessTools.Field(typeof(ARHSeeker), "targetUnit");

        private static readonly FieldInfo? FMissile =
            AccessTools.Field(typeof(ARHSeeker), "missile");

        private static readonly Dictionary<int, float> _authored = new();

        private static bool _warned;

        private static bool Usable()
        {
            if (FLoft != null && FTerminal != null && FTimeToTarget != null
                && FTargetUnit != null && FMissile != null)
                return true;

            if (!_warned)
            {
                _warned = true;
                Plugin.Log.LogWarning(
                    "[Meridian] ARHSeeker's loft fields have moved, so the loft cutoff is "
                    + "inactive and these rounds fly the engine's own loft.");
            }
            return false;
        }

        internal static void Apply(ARHSeeker seeker)
        {
            if (!Usable() || seeker == null) return;

            if (FTimeToTarget!.GetValue(seeker) is float t && t < 0f)
                FTimeToTarget.SetValue(seeker, 0f);

            if (FMissile!.GetValue(seeker) is not Missile missile || missile == null) return;
            if (missile.definition is not MissileDefinition def) return;
            if (!PluginInfo.IsOurMissileKey(def.jsonKey)) return;

            int id = seeker.GetInstanceID();

            if (!_authored.TryGetValue(id, out float authored))
            {
                if (FLoft!.GetValue(seeker) is not float l) return;
                authored = l;
                _authored[id] = authored;
            }

            if (authored <= 0f) return;

            if (FTargetUnit!.GetValue(seeker) is not Unit target || target == null) return;
            if (FTerminal!.GetValue(seeker) is not float terminal || terminal <= 0f) return;

            float range = Vector3.Distance(target.transform.position, missile.transform.position);
            bool endgame = range < terminal;

            float want = endgame ? 0f : authored;
            if (FLoft!.GetValue(seeker) is float now && Mathf.Approximately(now, want)) return;

            FLoft.SetValue(seeker, want);

            if (endgame && _logged.Add(id))
                Plugin.Diag(
                    $"[Meridian] LOFT {def.jsonKey}: inside the seeker's own {terminal:0} m "
                    + $"terminal range at {range:0} m, so the {authored:0.00} loft is off for the "
                    + "endgame. It was worth up to "
                    + $"{range * authored:0} m of aimpoint offset straight up.");
        }

        private static readonly HashSet<int> _logged = new();
    }

    [HarmonyPatch(typeof(ARHSeeker), nameof(ARHSeeker.Seek))]
    internal static class ARHSeeker_Seek_LoftCutoffPatch
    {
        [HarmonyPrefix]
        private static void Prefix(ARHSeeker __instance)
        {
            try
            {
                LoftCutoff.Apply(__instance);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[Meridian] Loft cutoff failed: {ex.Message}");
            }
        }
    }
}
