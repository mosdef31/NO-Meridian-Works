using BepInEx.Configuration;

namespace MeridianWorks
{

    internal static class PluginConfig
    {
        internal const string Section = "Diagnostics";

        internal static ConfigEntry<bool>? DiagnosticsEntry;

        internal static void Bind(ConfigFile config)
        {
            DiagnosticsEntry = config.Bind(
                Section,
                "Diagnostics",
                false,
                "Write detailed lines to the BepInEx log describing how each Meridian Works store " +
                "is assembled and mounted. Off by default. Turn it on if you are reporting a " +
                "problem with how a weapon looks or where it sits, then send the log.");
        }
    }
}
