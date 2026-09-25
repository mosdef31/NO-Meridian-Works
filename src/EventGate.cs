using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace MeridianWorks
{
    internal static class EventGate
    {

        private static readonly string[] Airframes =
            { "darkreach", "mc260", "chimera", "cargo1" };

        private static readonly Dictionary<WeaponMount, WeaponMount> _clones =
            new Dictionary<WeaponMount, WeaponMount>();

        private static readonly FieldInfo? FEvent =
            AccessTools_Field("isEventContent");

        private static FieldInfo? AccessTools_Field(string name) =>
            HarmonyLib.AccessTools.Field(typeof(WeaponMount), name);

        private static bool _announced;

        internal static int LastAirframesSeen;

        private static readonly HashSet<string> _unindexedReported = new HashSet<string>();

        private static bool IsOurAirToAir(WeaponMount m)
        {
            if (m == null || !PluginInfo.IsOurMountKey(m.jsonKey)) return false;
            if (m.info == null) return false;
            return m.info.effectiveness.antiAir > 0f;
        }

        private static bool IsGatedAirframe(WeaponManager wm)
        {
            string a = wm.name != null ? wm.name.ToLowerInvariant() : string.Empty;
            string b = wm.transform != null && wm.transform.root != null
                ? wm.transform.root.name.ToLowerInvariant()
                : string.Empty;

            foreach (string token in Airframes)
                if (a.Contains(token) || b.Contains(token)) return true;
            return false;
        }

        internal static IEnumerable<WeaponMount> BuildTwins()
        {
            if (FEvent == null) yield break;

            var plains = new List<WeaponMount>();
            foreach (WeaponMount m in EncyclopediaRegistration.AllOurMounts())
                if (IsOurAirToAir(m) && !_clones.ContainsKey(m)) plains.Add(m);

            foreach (WeaponMount plain in plains)
            {
                WeaponMount? twin = EventTwin(plain);
                if (twin != null) yield return twin;
            }
        }

        private static WeaponMount? EventTwin(WeaponMount plain)
        {
            if (_clones.TryGetValue(plain, out WeaponMount cached)) return cached;

            var twin = UnityEngine.Object.Instantiate(plain);
            twin.name = plain.name + "_Event";
            UnityEngine.Object.DontDestroyOnLoad(twin);

            HarmonyLib.AccessTools.Field(typeof(WeaponMount), "jsonKey")
                ?.SetValue(twin, plain.jsonKey + "_Event");

            FEvent?.SetValue(twin, true);

            _clones[plain] = twin;
            return twin;
        }

        internal static int Run()
        {
            if (FEvent == null)
            {
                Plugin.Log.LogWarning(
                "[Meridian] The event-content gate is OFF: WeaponMount has no "
                + "'isEventContent' field any more.");
                return 0;
            }

            int swapped = 0;
            int airframes = 0;
            var names = new List<string>();

            foreach (WeaponManager wm in Resources.FindObjectsOfTypeAll<WeaponManager>())
            {
                if (wm == null || !IsGatedAirframe(wm)) continue;
                airframes++;
                names.Add(wm.name ?? "?");

                HardpointSet[] sets = wm.hardpointSets;
                if (sets == null) continue;

                foreach (HardpointSet set in sets)
                {
                    if (set == null || set.weaponOptions == null) continue;

                    for (int i = 0; i < set.weaponOptions.Count; i++)
                    {
                        WeaponMount m = set.weaponOptions[i];
                        if (!IsOurAirToAir(m)) continue;

                        WeaponMount? twin = EventTwin(m);
                        if (twin == null) continue;

                        set.weaponOptions[i] = twin;
                        swapped++;
                    }
                }
            }

            foreach (WeaponMount twin in _clones.Values)
            {
                if (twin == null) continue;
                if (((INetworkDefinition)twin).LookupIndex != null) continue;
                if (!_unindexedReported.Add(twin.jsonKey ?? "?")) continue;
                Plugin.Log.LogError(
                    "[Meridian] The event-content twin '" + (twin.jsonKey ?? "?")
                    + "' has no LookupIndex, so selecting it would fail to spawn the "
                    + "aircraft. It was built after the Encyclopedia was indexed, which "
                    + "should not be possible - see EventGate.BuildTwins.");
            }

            LastAirframesSeen = airframes;

            if (airframes == 0)
            {

                return 0;
            }

            if (swapped > 0 || !_announced)
            {
                _announced = true;
                Plugin.Diag(
                    $"[Meridian] Event gate: {swapped} air-to-air fitting(s) on {airframes} "
                    + $"weapon manager(s) ({string.Join(", ", names.ToArray())}) were replaced "
                    + $"by event-content twins, from {_clones.Count} cloned mount(s).");
            }

            return swapped;
        }
    }

    internal sealed class EventGatePlacer : MonoBehaviour
    {
        private const float FastInterval = 4f;
        private const float SlowInterval = 30f;
        private const int SettledAfter = 3;

        private float _next;
        private int _lastAirframes = -1;
        private int _steady;
        private bool _saidNothingFound;

        private void Update()
        {
            if (Time.unscaledTime < _next) return;

            int swapped;
            try
            {
                swapped = EventGate.Run();
            }
            catch (Exception ex)
            {

                Plugin.Log.LogWarning("[Meridian] Event gate: a walk failed, and it "
                                      + "will be retried: " + ex.Message);
                _next = Time.unscaledTime + SlowInterval;
                return;
            }

            int airframes = EventGate.LastAirframesSeen;
            if (airframes != _lastAirframes)
            {
                _lastAirframes = airframes;
                _steady = 0;
            }
            else if (swapped == 0)
            {
                _steady++;
            }

            if (_steady >= SettledAfter && airframes == 0 && !_saidNothingFound)
            {
                _saidNothingFound = true;
                Plugin.Log.LogWarning(
                    "[Meridian] The event-content gate has settled without finding a "
                    + "Darkreach or a Chimera, so nothing was gated this session.");
            }

            _next = Time.unscaledTime + (_steady >= SettledAfter ? SlowInterval : FastInterval);
        }
    }
}
