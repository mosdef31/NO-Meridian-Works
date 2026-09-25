using System;
using UnityEngine;

namespace MeridianWorks
{

    internal static class HudIconBorrow
    {
        private static bool _said;

        internal static void Apply(Encyclopedia enc)
        {
            if (enc?.missiles == null) return;

            try
            {
                MissileDefinition? donor = null;
                foreach (MissileDefinition d in enc.missiles)
                {
                    if (d == null || PluginInfo.IsOurMissileKey(d.jsonKey)) continue;
                    if (d.friendlyIcon == null || d.hostileIcon == null) continue;
                    donor = d;
                    break;
                }
                if (donor == null)
                {
                    if (!_said)
                    {
                        _said = true;
                        Plugin.Log.LogWarning("[Meridian] HUD icon: no stock missile carries a friendly and a hostile icon, "
                            + "so our threat marks stay blank.");
                    }
                    return;
                }

                int filled = 0;
                foreach (MissileDefinition d in enc.missiles)
                {
                    if (d == null || !PluginInfo.IsOurMissileKey(d.jsonKey)) continue;
                    bool changed = false;
                    if (d.friendlyIcon == null) { d.friendlyIcon = donor.friendlyIcon; changed = true; }
                    if (d.hostileIcon == null) { d.hostileIcon = donor.hostileIcon; changed = true; }
                    if (d.iconSize <= 0f) { d.iconSize = donor.iconSize > 0f ? donor.iconSize : 1f; changed = true; }
                    if (changed) filled++;
                }

                if (filled > 0 && !_said)
                {
                    _said = true;
                    Plugin.Diag($"[Meridian] HUD icon: {filled} definition(s) had null friendly/hostile icons; "
                        + $"borrowed {donor.jsonKey}'s at iconSize {donor.iconSize}.");
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[Meridian] HUD icon borrow failed: {ex.Message}");
            }
        }
    }
}
