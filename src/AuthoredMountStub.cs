using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace MeridianWorks
{

    internal static class AuthoredMountStub
    {

        private static readonly HashSet<Hardpoint> _authored = new HashSet<Hardpoint>();

        private static readonly Dictionary<Renderer, bool> _original = new Dictionary<Renderer, bool>();

        private static readonly HashSet<string> _logged = new HashSet<string>();

        internal static void Mark(Hardpoint hardpoint, bool authored)
        {
            if (hardpoint == null) return;
            if (authored) _authored.Add(hardpoint);
            else _authored.Remove(hardpoint);
        }

        internal static bool Replaces(Hardpoint? hardpoint) =>
            hardpoint != null && _authored.Contains(hardpoint);

        internal static void Apply(Hardpoint hardpoint)
        {
            if (hardpoint == null) return;
            bool ours = _authored.Contains(hardpoint);

            int hidden = 0;
            hidden += Set(hardpoint.Pylon, ours);

            if (FPylonOptions?.GetValue(hardpoint) is Array options)
            {
                foreach (object entry in options)
                {

                    if (FEntryCargo?.GetValue(entry) is true) continue;
                    if (FEntryRenderer?.GetValue(entry) is not Renderer r) continue;
                    hidden += Set(r, ours);
                }
            }

            if (ours && _logged.Add(hardpoint.GetHashCode().ToString()))
            {
                Plugin.Diag(hidden > 0
                    ? $"[Meridian] authored mount: hid {hidden} stub renderer(s) on this hardpoint."
                    : "[Meridian] authored mount: this hardpoint draws no stub, so nothing was hidden.");
            }
        }

        private static int Set(Renderer r, bool hide)
        {
            if (r == null) return 0;

            if (hide)
            {
                if (!_original.ContainsKey(r)) _original[r] = r.enabled;
                bool was = r.enabled;
                r.enabled = false;
                return was ? 1 : 0;
            }

            if (_original.TryGetValue(r, out bool original))
            {
                r.enabled = original;
                _original.Remove(r);
            }
            return 0;
        }

        private static readonly System.Reflection.FieldInfo? FPylonOptions =
            AccessTools.Field(typeof(Hardpoint), "pylonOptions");

        private static readonly System.Reflection.FieldInfo? FEntryCargo =
            AccessTools.Field(AccessTools.Inner(typeof(Hardpoint), "HardpointPylon"), "cargo");

        private static readonly System.Reflection.FieldInfo? FEntryRenderer =
            AccessTools.Field(AccessTools.Inner(typeof(Hardpoint), "HardpointPylon"), "renderer");
    }

    [HarmonyPatch(typeof(Hardpoint), nameof(Hardpoint.ShowPylon))]
    internal static class Hardpoint_ShowPylon_AuthoredMountPatch
    {
        [HarmonyPostfix]
        private static void Postfix(Hardpoint __instance, bool weaponLoaded)
        {
            try
            {

                if (!weaponLoaded) AuthoredMountStub.Mark(__instance, false);
                AuthoredMountStub.Apply(__instance);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[Meridian] Hiding the stub threw: {ex.Message}");
            }
        }
    }
}
