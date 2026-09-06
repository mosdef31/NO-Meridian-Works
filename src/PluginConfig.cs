using BepInEx.Configuration;

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

        internal static ConfigEntry<bool>? CloseMountsToSkinEntry;

        internal static bool CloseMountsToSkin => CloseMountsToSkinEntry?.Value ?? true;

        internal static void Bind(ConfigFile config)
        {
            DiagnosticsEntry = config.Bind(
                Section,
                "Diagnostics",
                false,
                "Write detailed lines to the BepInEx log describing how each Meridian Works store " +
                "is assembled and mounted. Off by default. Turn it on if you are reporting a " +
                "problem with how a weapon looks or where it sits, then send the log.");

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

            CloseMountsToSkinEntry = config.Bind(
                "Appearance",
                "Close mounts onto the wing",
                true,
                "Raise a Meridian Works mount until it touches the aircraft skin above it, on " +
                "hardpoints where the game draws no pylon to hang from. Only ever raises by the " +
                "gap it measures, never more than 10 cm, and never on a mount that already has " +
                "something to sit against. Turn it off to leave every mount where the game puts it.");
        }
    }
}
