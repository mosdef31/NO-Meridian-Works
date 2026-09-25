using BepInEx.Configuration;
using UnityEngine;

namespace MeridianWorks
{

    internal static class PluginConfig
    {
        internal const string Section = "Diagnostics";

        internal static ConfigEntry<bool>? DiagnosticsEntry;

        internal static ConfigEntry<bool>? OffBoresightCueEntry;

        internal static bool OffBoresightCue => OffBoresightCueEntry?.Value ?? true;

        internal static ConfigEntry<bool>? LiveryMountsEntry;

        internal static bool LiveryMounts => LiveryMountsEntry?.Value ?? true;

        internal static bool RibbonTrail => true;
        internal static bool HazeBorrow => true;
        internal static bool GhostSweep => true;

        internal static void Bind(ConfigFile config)
        {

            DiagnosticsEntry = config.Bind(Section, "Diagnostics", false);

            OffBoresightCueEntry = config.Bind("HUD", "Off-boresight ring", true);

            LiveryMountsEntry = config.Bind("Appearance", "Livery on mounts", true);
        }
    }
}
