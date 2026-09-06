using System;
using System.Collections.Generic;
using HarmonyLib;

namespace MeridianWorks
{
    [HarmonyPatch(typeof(Encyclopedia), "AfterLoad", new Type[0])]
    internal static class Encyclopedia_AfterLoad_StockRevivalPatch
    {
        [HarmonyPostfix]
        private static void Postfix(Encyclopedia __instance)
        {

            try
            {
                StockRevival.Apply(__instance);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError(
                    "[Meridian] Stock revival postfix threw (non-fatal, the PAB-125HD may be " +
                    $"absent this run): {ex}");
            }
        }
    }

    internal static class StockRevival
    {

        private static readonly Dictionary<string, string> Revive = new Dictionary<string, string>
        {

            { "bomb_125HD_triple", "PAB-125HD x3, the stock high-drag 125 kg bomb" },
        };

        private const string DisabledField = "disabled";

        private static bool _reported;

        internal static void Apply(Encyclopedia encyclopedia)
        {
            var mounts = encyclopedia?.weaponMounts;
            if (mounts == null)
            {
                Plugin.Log.LogWarning(
                    "[Meridian] Encyclopedia.weaponMounts is null; nothing to revive this pass.");
                return;
            }

            var found = new HashSet<string>();

            foreach (var mount in mounts)
            {
                if (mount == null || !Revive.ContainsKey(mount.jsonKey))
                {
                    continue;
                }

                found.Add(mount.jsonKey);

                var probe = Traverse.Create(mount).Field(DisabledField);
                if (!probe.FieldExists())
                {

                    if (!_reported)
                    {
                        Plugin.Log.LogError(
                            $"[Meridian] WeaponMount has no '{DisabledField}' field any more. " +
                            $"{mount.jsonKey} cannot be revived; re-read WeaponMount.cs in the " +
                            "current decompile.");
                    }
                    continue;
                }

                var field = Traverse.Create(mount).Field<bool>(DisabledField);
                if (!field.Value)
                {
                    continue;
                }

                field.Value = false;

                if (!_reported)
                {
                    Plugin.Log.LogInfo(
                        $"[Meridian] Revived stock mount {mount.jsonKey} ({Revive[mount.jsonKey]}).");
                }
            }

            if (!_reported)
            {
                foreach (var pair in Revive)
                {
                    if (!found.Contains(pair.Key))
                    {
                        Plugin.Log.LogWarning(
                            $"[Meridian] Stock mount {pair.Key} ({pair.Value}) is not in " +
                            "Encyclopedia.weaponMounts. It was removed by a game update, or the " +
                            "jsonKey changed.");
                    }
                }

                _reported = true;
            }
        }
    }
}
