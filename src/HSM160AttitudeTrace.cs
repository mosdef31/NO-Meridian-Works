using System.Runtime.CompilerServices;
using UnityEngine;

namespace MeridianWorks
{

    internal static class HSM160AttitudeTrace
    {
        private sealed class Window
        {
            internal float Until = -1f;
            internal float AoaMin = float.MaxValue, AoaMax = float.MinValue;
            internal float RateMin = float.MaxValue, RateMax = float.MinValue;
            internal int Signs, LastSign;
        }

        private static readonly ConditionalWeakTable<Missile, Window> Windows = new();

        internal static void Sample(Missile m, HSM160TrajectoryState state, string key, string phase, float cmdDeg)
        {
            if (state.logId != 0 || m.rb == null) return;
            Vector3 v = m.rb.velocity;
            if (v.sqrMagnitude < 1f) return;

            Transform t = m.transform;
            float pathPitch = Mathf.Atan2(v.y, new Vector2(v.x, v.z).magnitude) * Mathf.Rad2Deg;
            Vector3 f = t.forward;
            float nosePitch = Mathf.Atan2(f.y, new Vector2(f.x, f.z).magnitude) * Mathf.Rad2Deg;
            float aoa = nosePitch - pathPitch;
            float rate = Vector3.Dot(m.rb.angularVelocity, t.right) * -Mathf.Rad2Deg;

            Window w = Windows.GetOrCreateValue(m);
            if (w.Until < 0f) w.Until = m.timeSinceSpawn + 1f;
            w.AoaMin = Mathf.Min(w.AoaMin, aoa);
            w.AoaMax = Mathf.Max(w.AoaMax, aoa);
            w.RateMin = Mathf.Min(w.RateMin, rate);
            w.RateMax = Mathf.Max(w.RateMax, rate);
            int sign = rate > 0.2f ? 1 : rate < -0.2f ? -1 : 0;
            if (sign != 0 && sign != w.LastSign) { if (w.LastSign != 0) w.Signs++; w.LastSign = sign; }

            if (m.timeSinceSpawn < w.Until) return;

            string tag = key.Replace("Meridian", "").Replace("_Missile", "");
            Plugin.Log.LogInfo($"[Meridian] {tag}#0 att {phase} t={m.timeSinceSpawn:F0} "
                + $"aoa={w.AoaMin:F1}..{w.AoaMax:F1} q={w.RateMin:F1}..{w.RateMax:F1} flips={w.Signs} "
                + $"cmd={cmdDeg:F0} p={pathPitch:F0}");

            w.Until = m.timeSinceSpawn + 1f;
            w.AoaMin = w.RateMin = float.MaxValue;
            w.AoaMax = w.RateMax = float.MinValue;
            w.Signs = 0;
        }
    }
}
