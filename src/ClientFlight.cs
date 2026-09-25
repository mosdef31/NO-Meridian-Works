using System.Collections.Generic;
using NuclearOption.NetworkTransforms;
using UnityEngine;

namespace MeridianWorks
{

    internal static class ClientFlight
    {

        internal static void Apply(IList<MissileDefinition> defs)
        {
            int added = 0, already = 0, failed = 0;

            foreach (MissileDefinition def in defs)
            {
                if (def == null) continue;
                if (!PluginInfo.IsOurMissileKey(def.jsonKey)) continue;

                GameObject? prefab = def.unitPrefab;
                if (prefab == null) continue;

                Missile? missile = prefab.GetComponent<Missile>();
                if (missile == null) continue;

                MissileNetworkTransform? already_ =
                    prefab.GetComponent<MissileNetworkTransform>();
                if (already_ != null)
                {

                    if (already_.Missile == null) already_.Missile = missile;
                    already++;
                    continue;
                }

                MissileNetworkTransform? sync = prefab.AddComponent<MissileNetworkTransform>();
                if (sync == null)
                {
                    failed++;
                    Plugin.Log.LogWarning(
                        "[Meridian] Client flight: '" + def.jsonKey + "' would not take a "
                        + "MissileNetworkTransform, so on a multiplayer client this round will "
                        + "coast at its launch speed instead of flying.");
                    continue;
                }

                sync.Missile = missile;
                added++;
                Plugin.Diag(
                    "[Meridian] Client flight: '" + def.jsonKey + "' had no "
                    + "MissileNetworkTransform, so a client had nothing to apply the server's "
                    + "snapshots to. Added and bound.");
            }

            if (added > 0)
                Plugin.Log.LogInfo(
                    "[Meridian] Client flight: added a MissileNetworkTransform to " + added
                    + " flying prefab(s) (" + already + " already had one, " + failed
                    + " refused). Without it a round is inert on every machine that is not the "
                    + "server: no thrust, no guidance, and the launching aircraft's airspeed "
                    + "held to impact.");
            else
                Plugin.Diag(
                    "[Meridian] Client flight: all " + already + " flying prefab(s) already "
                    + "carry a MissileNetworkTransform.");
        }
    }
}
