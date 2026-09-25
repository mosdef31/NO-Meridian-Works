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

        internal static ConfigEntry<bool>? RibbonTrailEntry;

        internal static bool RibbonTrail => RibbonTrailEntry?.Value ?? true;

        internal static ConfigEntry<bool>? HazeBorrowEntry;

        internal static bool HazeBorrow => HazeBorrowEntry?.Value ?? true;

        internal static ConfigEntry<bool>? GhostSweepEntry;

        internal static bool GhostSweep => GhostSweepEntry?.Value ?? true;

        internal static void Bind(ConfigFile config)
        {
            DiagnosticsEntry = config.Bind(
                Section,
                "Diagnostics",
                false,
                "Write detailed lines to the BepInEx log describing how each Meridian Works store " +
                "is assembled and mounted. Off by default. Turn it on if you are reporting a " +
                "problem with how a weapon looks or where it sits, then send the log.");

            RibbonTrailEntry = config.Bind(
                "Effects",
                "Ribbon smoke trail",
                true,
                "Draw missile smoke the way the game's own missiles do: one connected ribbon " +
                "through carriers dropped every 30 m, instead of a stream of separate puffs. " +
                "Turn it off to go back to the puff trail, which is softer close up but can " +
                "fork into a second trail when the world re-centres itself.");

            HazeBorrowEntry = config.Bind(
                "Effects",
                "Borrow the game heat haze",
                true,
                "Draw the exhaust heat haze with the game's own distortion material rather than " +
                "the pack's. Turn it off to use the pack's own haze, which can look like flat pale " +
                "sheets on some scenes.");

            GhostSweepEntry = config.Bind(
                "Effects",
                "Sweep inert borrowed effects",
                true,
                "Remove the unused parts of stock exhaust effects the pack borrows. This saves " +
                "memory and does not change what you see. Turn it off if exhaust effects go " +
                "missing, and report it.");

            OffBoresightCueEntry = config.Bind(
                "HUD",
                "Off-boresight ring",
                true,
                "Draw a crossed circle over the target while a Meridian Works heat-seeking " +
                "missile can still be launched at it. It appears only on wide shots, where " +
                "the target is out at the edge of the canopy and the usual SHOOT cue is in " +
                "the middle of the screen. Turn it off if you would rather have a clean HUD.");

            LiveryMountsEntry = config.Bind(
                "Appearance",
                "Livery on mounts",
                true,
                "Paint Meridian Works pylons and racks in the aircraft's livery colour, the " +
                "way stock drop tanks and rocket pods are painted. The weapons themselves " +
                "keep their own finish. Turn it off to leave every mount in its factory grey.");

        }
    }
}
