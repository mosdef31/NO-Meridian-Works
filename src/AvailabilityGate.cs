using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace MeridianWorks
{

    internal static class AvailabilityGate
    {
        private static bool _ran;

        private const BindingFlags Inst =
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

        internal static void RunOnce()
        {
            if (_ran) return;
            _ran = true;

            try
            {
                Check();
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[Meridian] Availability gate failed: {ex.Message}");
            }
        }

        private static void Check()
        {
            FieldInfo? fDisabled = typeof(WeaponMount).GetField("disabled", Inst);
            FieldInfo? fEvent = typeof(WeaponMount).GetField("isEventContent", Inst);

            if (fDisabled == null || fEvent == null)
            {
                Plugin.Log.LogWarning(
                    "[Meridian] Availability gate: WeaponMount no longer has both 'disabled' and "
                    + "'isEventContent'. The game's loadout filter has changed shape - re-read "
                    + "WeaponChecker.GetAvailableWeaponsNonAlloc before trusting this gate again.");
                return;
            }

            var repaired = new List<string>();
            var eventOnly = new List<string>();

            int checked_ = 0;
            foreach (WeaponMount mount in EncyclopediaRegistration.AllOurMounts())
            {
                checked_++;
                if (mount == null) continue;

                string key = string.IsNullOrEmpty(mount.jsonKey) ? mount.name : mount.jsonKey;

                if ((bool)fDisabled.GetValue(mount))
                {
                    fDisabled.SetValue(mount, false);
                    repaired.Add(key);
                }

                if ((bool)fEvent.GetValue(mount))
                    eventOnly.Add(key);
            }

            if (repaired.Count > 0)
                Plugin.Log.LogWarning(
                    $"[Meridian] Availability gate: {repaired.Count} mount(s) shipped with "
                    + "WeaponMount.disabled set, so the loadout dropdown was filtering them out "
                    + "however well they registered. Cleared at runtime, but FIX THE ASSET: "
                    + string.Join(", ", repaired.ToArray()) + ".");

            if (eventOnly.Count > 0)
                Plugin.Log.LogWarning(
                    $"[Meridian] Availability gate: {eventOnly.Count} mount(s) are marked "
                    + "isEventContent, so they only appear when the mission allows event content: "
                    + string.Join(", ", eventOnly.ToArray()) + ".");

            if (repaired.Count == 0 && eventOnly.Count == 0)
                Plugin.Diag(
                    $"[Meridian] Availability gate clean: all {checked_} mount(s) pass "
                    + "WeaponMount.IsAllowed, so none of them is being hidden from the loadout "
                    + "dropdown by the disabled or event-content flag.");
        }
    }
}
