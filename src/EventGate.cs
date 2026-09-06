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

        private static bool _done;

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

        internal static void RunOnce()
        {
            if (_done) return;
            _done = true;

            if (FEvent == null)
            {
                Plugin.Log.LogWarning(
                "[Meridian] The event-content gate is OFF: WeaponMount has no "
                + "'isEventContent' field any more.");
                return;
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

            Encyclopedia? enc = GameData.EncyclopediaOrNull();
            if (enc?.weaponMounts != null)
                foreach (WeaponMount twin in _clones.Values)
                    if (twin != null && !enc.weaponMounts.Contains(twin))
                        enc.weaponMounts.Add(twin);

            if (airframes == 0)
            {
                Plugin.Log.LogWarning(
                "[Meridian] The event-content gate found NO Darkreach and NO Chimera "
                + "in this session, so nothing was gated.");
                return;
            }

            Plugin.Diag(
                $"[Meridian] Event gate: {swapped} air-to-air fitting(s) on {airframes} "
                + $"weapon manager(s) ({string.Join(", ", names.ToArray())}) were replaced "
                + $"by event-content twins, from {_clones.Count} cloned mount(s).");
        }
    }
}
