using System;
using System.Collections.Generic;
using UnityEngine;

namespace MeridianWorks
{

    internal static class SeekerAimProbe
    {
        private static readonly HashSet<string> _reported = new HashSet<string>();

        internal static void Report(GameObject round, string ourKey)
        {
            if (!Plugin.Diagnostics) return;
            if (round == null || string.IsNullOrEmpty(ourKey)) return;
            if (!_reported.Add(ourKey)) return;

            try
            {
                Measure(round, ourKey);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[Meridian] Seeker aim probe failed on " + ourKey
                                      + ": " + ex.Message);
            }
        }

        private static void Measure(GameObject round, string ourKey)
        {
            var seeker = round.GetComponentInChildren<MissileSeeker>(true);
            if (seeker == null)
            {
                Plugin.Diag($"[Meridian] SEEKERAIM {ourKey}: no Seeker component found on the "
                            + "round at all, which is a different and larger problem.");
                return;
            }

            Transform st = seeker.transform;
            bool onRoot = ReferenceEquals(st, round.transform);

            Vector3 fwdLocal = round.transform.InverseTransformDirection(st.forward);
            float offNose = Vector3.Angle(round.transform.forward, st.forward);

            Vector3 posLocal = round.transform.InverseTransformPoint(st.position);

            string path = st.name;
            for (Transform t = st.parent; t != null && t != round.transform; t = t.parent)
                path = t.name + "/" + path;

            Plugin.Diag(
                $"[Meridian] SEEKERAIM {ourKey}: {seeker.GetType().Name} on "
                + (onRoot ? "the missile root" : $"child '{path}'")
                + $", forward in missile frame ({fwdLocal.x:0.00},{fwdLocal.y:0.00},{fwdLocal.z:0.00}), "
                + $"offset ({posLocal.x:0.00},{posLocal.y:0.00},{posLocal.z:0.00}) m, "
                + $"**{offNose:0.0} deg OFF THE NOSE**. "
                + (offNose < 1f
                    ? "That is down the nose, so GetRadarReturn's maxTrackingAngle gate is "
                      + "measuring what it should and the drift is NOT this."
                    : "GetRadarReturn measures its tracking angle from THIS transform's "
                      + "forward, so this many degrees is subtracted from the round's whole "
                      + "tracking cone - and past maxTrackingAngle the return is 0 on every "
                      + "frame, the lock never establishes, and the round flies its launch "
                      + "prediction until the target is dropped at 2 km of drift."));
        }
    }
}
