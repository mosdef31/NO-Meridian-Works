using System.Reflection;
using HarmonyLib;

namespace MeridianWorks
{

    internal static class GameData
    {
        private static bool _resolved;
        private static object? _loader;
        private static PropertyInfo? _isLoaded;
        private static bool _warned;

        internal static Encyclopedia? EncyclopediaOrNull()
        {
            try
            {
                if (!Resolve()) return Encyclopedia.i;
                if (_isLoaded!.GetValue(_loader) is not true) return null;
                return Encyclopedia.i;
            }
            catch
            {

                return null;
            }
        }

        private static bool Resolve()
        {
            if (_resolved) return _loader != null && _isLoaded != null;
            _resolved = true;

            FieldInfo? fLoader = AccessTools.Field(typeof(Encyclopedia), "loader");
            _loader = fLoader?.GetValue(null);

            if (_loader != null)
                _isLoaded = AccessTools.Property(_loader.GetType(), "IsLoaded");

            if ((_loader == null || _isLoaded == null) && !_warned)
            {
                _warned = true;
                Plugin.Log.LogWarning(
                "[Meridian] Could not reach Encyclopedia's loader to test whether it is ready, "
                + "so early lookups fall back to reading it directly.");
            }

            return _loader != null && _isLoaded != null;
        }
    }
}
