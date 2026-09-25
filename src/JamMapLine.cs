using HarmonyLib;
using UnityEngine;

namespace MeridianWorks
{

    internal static class JamMapLine
    {
        private static bool _logged;

        private static bool IsOurRound(Unit? unit)
        {
            return unit != null
                && unit.definition is MissileDefinition def
                && PluginInfo.IsOurMissileKey(def.jsonKey);
        }

        internal static void EnsureIcon(Unit? maybeJammer)
        {
            if (!IsOurRound(maybeJammer)) return;

            Unit jammer = maybeJammer!;

            DynamicMap map = SceneSingleton<DynamicMap>.i;
            if (map == null) return;

            if (DynamicMap.TryGetMapIcon(jammer, out _)) return;

            try
            {
                map.AddIcon(jammer.persistentID);
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogWarning($"[Meridian] JAM: could not give the jamming round a map icon: {ex.Message}");
                return;
            }

            if (!_logged)
            {
                _logged = true;
                bool got = DynamicMap.TryGetMapIcon(jammer, out _);
                Plugin.Diag(got
                    ? "[Meridian] JAM: the jamming round now carries a map icon, which is what the "
                      + "game's jam line is drawn between. The line should appear from this jam on."
                    : "[Meridian] JAM: the map REFUSED an icon for the jamming round, so the jam line "
                      + "still cannot be drawn. DynamicMap.SpawnQueuedIcons declines a unit that is "
                      + "disabled or whose definition has mapIconSize <= 0 - check the definition.");
            }
        }
    }

    [HarmonyPatch(typeof(JammedMarker), "Setup")]
    internal static class JammedMarker_Setup_JamLinePatch
    {
        private static void Prefix(Unit jammedBy)
        {
            JamMapLine.EnsureIcon(jammedBy);
        }
    }

    [HarmonyPatch(typeof(JammedMarker), "JammedMarker_OnUnitJammed")]
    internal static class JammedMarker_OnUnitJammed_JamLinePatch
    {
        private static void Prefix(Unit.JamEventArgs jam)
        {
            JamMapLine.EnsureIcon(jam.jammingUnit);
        }
    }
}
