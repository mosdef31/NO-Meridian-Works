using System;
using System.Collections.Generic;
using HarmonyLib;
using NuclearOption.SavedMission;
using UnityEngine;

namespace MeridianWorks
{

    internal static class StubTipPreclusion
    {

        private sealed class Rule
        {
            internal Rule(int trigger, int[] blocked, string[] wide)
            {
                Trigger = trigger;
                Blocked = blocked;
                Wide = new HashSet<string>(wide);
            }

            internal int Trigger { get; }
            internal int[] Blocked { get; }
            internal HashSet<string> Wide { get; }
        }

        private static readonly Dictionary<string, Rule> Rules = new Dictionary<string, Rule>
        {

            {
                "AttackHelo1",
                new Rule(
                    trigger: 2,
                    blocked: new[] { 3, 4 },
                    wide: new[] { "MeridianAGM33L_triple", "MeridianAGM57L_x2" })
            },
        };

        private static Dictionary<HardpointSet, (string Manager, int Index)>? _sets;

        private static bool _reported;

        internal static bool ShouldBlock(HardpointSet set, Loadout loadout)
        {
            if (set == null || loadout?.weapons == null) return false;

            Index();
            if (_sets == null || !_sets.TryGetValue(set, out var where)) return false;
            if (!Rules.TryGetValue(where.Manager, out Rule rule)) return false;
            if (Array.IndexOf(rule.Blocked, where.Index) < 0) return false;

            if (rule.Trigger >= loadout.weapons.Count) return false;
            WeaponMount onTrigger = loadout.weapons[rule.Trigger];

            return onTrigger != null && onTrigger.jsonKey != null && rule.Wide.Contains(onTrigger.jsonKey);
        }

        private static void Index()
        {
            if (_sets != null && _sets.Count > 0) return;

            Encyclopedia? enc = GameData.EncyclopediaOrNull();
            if (enc?.aircraft == null) return;

            var found = new Dictionary<HardpointSet, (string, int)>();

            foreach (AircraftDefinition def in enc.aircraft)
            {
                if (def == null || def.unitPrefab == null) continue;

                var wm = def.unitPrefab.GetComponent<WeaponManager>();
                if (wm == null || wm.hardpointSets == null) continue;
                if (!Rules.ContainsKey(wm.name) && !Rules.ContainsKey(def.unitPrefab.name)) continue;

                string manager = Rules.ContainsKey(wm.name) ? wm.name : def.unitPrefab.name;

                for (int i = 0; i < wm.hardpointSets.Length; i++)
                {
                    HardpointSet s = wm.hardpointSets[i];
                    if (s != null) found[s] = (manager, i);
                }
            }

            if (found.Count == 0) return;
            _sets = found;

            if (_reported) return;
            _reported = true;

            foreach (var kv in Rules)
            {
                Plugin.Log.LogInfo(
                    $"[Meridian] Stub-tip rule armed on '{kv.Key}': set(s) " +
                    $"{string.Join(", ", Array.ConvertAll(kv.Value.Blocked, b => b.ToString()))} are blocked " +
                    $"while set {kv.Value.Trigger} carries {string.Join(" or ", new List<string>(kv.Value.Wide).ToArray())}.");
            }
        }
    }

    [HarmonyPatch(typeof(HardpointSet), nameof(HardpointSet.BlockedByOtherHardpoint))]
    internal static class HardpointSet_BlockedByOtherHardpoint_StubTipPatch
    {
        [HarmonyPostfix]
        private static void Postfix(HardpointSet __instance, Loadout loadout, ref bool __result)
        {
            try
            {
                if (__result) return;
                if (StubTipPreclusion.ShouldBlock(__instance, loadout)) __result = true;
            }
            catch (Exception ex)
            {

                Plugin.Log.LogError($"[Meridian] The stub-tip rule threw: {ex.Message}");
            }
        }
    }
}
