using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace MeridianWorks
{

    [HarmonyPatch(typeof(ARHSeeker), "Seek")]
    internal static class ArhLead
    {
        private static FieldInfo? _missileField;
        private static bool _looked;

        public static Vector3 Lead(GlobalPosition targetPos, GlobalPosition platformPos,
                                   Vector3 targetVel, Vector3 platformVel, Vector3 targetAccel,
                                   float maxLead, ARHSeeker seeker)
        {
            Missile? ours = MissileOf(seeker);
            if (ours == null)
                return TargetCalc.GetLeadVectorWithAccel(
                    targetPos, platformPos, targetVel, platformVel, targetAccel, maxLead);

            Vector3 rawAccel = Vector3.ClampMagnitude(targetAccel, 100f);
            Vector3 smooth = SmoothAccel(ours, rawAccel);
            Vector3 vDir = targetVel.sqrMagnitude > 1f ? targetVel.normalized : Vector3.zero;
            Vector3 aAlong = Vector3.Dot(smooth, vDir) * vDir;
            Vector3 aTurn = smooth - aAlong;
            targetAccel = aAlong + 9.81f * Vector3.up;

            Vector3 self = ours.rb != null ? ours.rb.velocity : platformVel;
            float speed = self.magnitude;
            if (speed < 1f) { self = platformVel; speed = Mathf.Max(self.magnitude, 1f); }

            float flown = EffectiveSpeed(ours, speed);

            Vector3 toTarget = targetPos - platformPos;
            float range = toTarget.magnitude;

            if (Mathf.Abs(range - 1000f) < 0.5f &&
                Vector3.Angle(toTarget, ours.transform.forward) < 0.05f)
            {
                LostReturnGuard.Note(ours);
                return Vector3.zero;
            }

            Vector3 closingVel = self.sqrMagnitude > 1f ? self.normalized * flown : self;
            float closing = Vector3.Dot(toTarget.normalized, closingVel - targetVel);
            float t = range / Mathf.Max(closing, 10f);

            for (int i = 0; i < 2; i++)
            {
                float guess = Mathf.Max(t, 0f);
                Vector3 future = toTarget + TurnLead(targetVel, aTurn, guess);
                t = future.magnitude / Mathf.Max(flown, 10f);
                if (float.IsNaN(t) || float.IsInfinity(t)) { t = guess; break; }
            }

            float tof = Mathf.Max(t, 0f);
            float lead = Mathf.Clamp(tof, 0f, maxLead);

            Vector3 answer = TurnLead(targetVel, aTurn, lead)
                             + Mathf.Min(lead * lead, 1f) * 0.5f * targetAccel;

            float loftAmount = LoftAmountOf(seeker);
            if (loftAmount > 0f)
                answer += Mathf.Min(tof * tof * StockHalfG * loftAmount, range * loftAmount)
                          * Vector3.up;

            return answer;
        }

        private const float StockHalfG = 4.905f;

        private const float MaxTurnPredict = Mathf.PI * 0.5f;

        private const float AccelSmoothing = 0.12f;

        private static readonly System.Collections.Generic.Dictionary<int, Vector3> _accel =
            new System.Collections.Generic.Dictionary<int, Vector3>();

        internal static Vector3 SmoothAccel(Missile ours, Vector3 raw)
        {
            int id = ours.GetInstanceID();
            if (_accel.Count > 512) _accel.Clear();
            Vector3 v = _accel.TryGetValue(id, out Vector3 prev)
                ? Vector3.Lerp(prev, raw, AccelSmoothing)
                : raw;
            _accel[id] = v;
            return v;
        }

        internal static Vector3 TurnLead(Vector3 vel, Vector3 aTurn, float t)
        {
            float speed = vel.magnitude;
            float a = aTurn.magnitude;
            if (speed < 1f || a < 0.5f || t <= 0f) return vel * t;

            Vector3 vHat = vel / speed;
            Vector3 nHat = aTurn / a;
            float w = a / speed;
            float theta = Mathf.Min(w * t, MaxTurnPredict);
            float arcTime = theta / w;

            Vector3 arc = vHat * (speed * Mathf.Sin(theta) / w)
                          + nHat * (speed * (1f - Mathf.Cos(theta)) / w);
            Vector3 after = (vHat * Mathf.Cos(theta) + nHat * Mathf.Sin(theta)) * speed;
            return arc + after * (t - arcTime);
        }

        private static FieldInfo? _loftField;
        private static bool _loftLooked;

        private static float LoftAmountOf(ARHSeeker seeker)
        {
            try
            {
                if (seeker == null) return 0f;

                if (!_loftLooked)
                {
                    _loftLooked = true;
                    _loftField = AccessTools.Field(typeof(ARHSeeker), "loftAmount");
                    if (_loftField == null)
                        Plugin.Log.LogWarning(
                            "[Meridian] Active-radar loft: ARHSeeker carries no 'loftAmount' field "
                            + "in this build, so our rounds fly FLAT - the engine's loft is "
                            + "cancelled for them and we cannot compute a replacement. Long shots "
                            + "will fall short. This is a game-update break, not a tuning fault.");
                }

                if (_loftField == null) return 0f;
                object loft = _loftField.GetValue(seeker);
                if (!(loft is float f) || float.IsNaN(f) || float.IsInfinity(f)) return 0f;
                return Mathf.Max(f, 0f);
            }
            catch { return 0f; }
        }

        public static float LoftHalfG(ARHSeeker seeker)
        {
            try
            {
                return MissileOf(seeker) != null ? 0f : StockHalfG;
            }
            catch
            {

                return StockHalfG;
            }
        }

        private static float EffectiveSpeed(Missile ours, float speed)
        {
            try
            {
                float remaining = ours.GetRemainingDeltaV();
                if (float.IsNaN(remaining) || float.IsInfinity(remaining) || remaining <= 0f)
                    return speed;

                float ceiling = ours.GetTopSpeed(0f, 0f);
                float flown = speed + 0.5f * remaining;

                if (ceiling > speed && !float.IsNaN(ceiling) && !float.IsInfinity(ceiling))
                    flown = Mathf.Min(flown, ceiling);

                return Mathf.Max(flown, speed);
            }
            catch
            {

                return speed;
            }
        }

        internal static Missile? MissileOf(MissileSeeker seeker)
        {
            try
            {
                if (seeker == null) return null;

                if (!_looked)
                {
                    _looked = true;
                    _missileField = AccessTools.Field(typeof(MissileSeeker), "missile")
                                    ?? AccessTools.Field(typeof(ARHSeeker), "missile");
                }

                if (!(_missileField?.GetValue(seeker) is Missile m)) return null;

                string key = (m.definition as MissileDefinition)?.jsonKey ?? m.name;
                return PluginInfo.IsOurMissileKey(key) ? m : null;
            }
            catch { return null; }
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var code = new List<CodeInstruction>(instructions);

            MethodInfo? stock = AccessTools.Method(
                typeof(TargetCalc), nameof(TargetCalc.GetLeadVectorWithAccel));
            MethodInfo ours = AccessTools.Method(typeof(ArhLead), nameof(Lead));
            MethodInfo loft = AccessTools.Method(typeof(ArhLead), nameof(LoftHalfG));

            int hits = 0;
            if (stock != null)
            {
                for (int i = 0; i < code.Count; i++)
                {
                    if (code[i].opcode != OpCodes.Call) continue;
                    if (!(code[i].operand is MethodInfo m) || m != stock) continue;

                    var load = new CodeInstruction(OpCodes.Ldarg_0);
                    load.labels.AddRange(code[i].labels);
                    load.blocks.AddRange(code[i].blocks);

                    code[i] = load;
                    code.Insert(i + 1, new CodeInstruction(OpCodes.Call, ours));
                    i++;
                    hits++;
                }
            }

            int loftHits = 0;
            for (int i = 0; i < code.Count; i++)
            {
                if (code[i].opcode != OpCodes.Ldc_R4) continue;
                if (!(code[i].operand is float f)) continue;
                if (Mathf.Abs(f - StockHalfG) > 0.0001f) continue;

                var load = new CodeInstruction(OpCodes.Ldarg_0);
                load.labels.AddRange(code[i].labels);
                load.blocks.AddRange(code[i].blocks);

                code[i] = load;
                code.Insert(i + 1, new CodeInstruction(OpCodes.Call, loft));
                i++;
                loftHits++;
            }

            if (hits != 1 || loftHits != 1)
            {
                Plugin.Log.LogWarning(
                    "[Meridian] Active-radar guidance: expected exactly one call to "
                    + "TargetCalc.GetLeadVectorWithAccel and exactly one 4.905 constant in "
                    + "ARHSeeker.Seek, and found " + hits + " and " + loftHits + ". The method "
                    + "has changed, so NOTHING was patched and our ARH rounds keep the engine's "
                    + "own lead and loft - which is where they were before 2026-09-12.");
                return instructions;
            }

            Plugin.Log.LogInfo(
                "[Meridian] Active-radar guidance: ARHSeeker.Seek now solves the time of flight "
                + "by iteration and off the missile's real velocity for Meridian Works rounds, "
                + "and the LOFT is computed off that same time of flight instead of the engine's "
                + "1 Hz timeToTarget sample - which is what put the aimpoint above and below the "
                + "target by turns. Stock and third-party missiles keep the engine's own lead "
                + "and loft, unchanged.");

            return code;
        }
    }

    internal static class LostReturnGuard
    {
        private static readonly HashSet<int> _seen = new HashSet<int>();

        internal static void Note(Missile m)
        {
            if (!Plugin.Diagnostics) return;
            if (!_seen.Add(m.GetInstanceID())) return;
            Plugin.Diag($"[Meridian] LOSTRETURN {m.name}: seeker lost its return off boresight, "
                + $"lead withheld so the round flies straight instead of circling (t+{m.timeSinceSpawn:0.0}s).");
        }
    }

    [HarmonyPatch(typeof(IRSeeker), "Seek")]
    internal static class IrLead
    {
        public static Vector3 Lead(GlobalPosition targetPos, GlobalPosition platformPos,
                                   Vector3 targetVel, Vector3 platformVel, Vector3 targetAccel,
                                   float maxLead, IRSeeker seeker)
        {
            Missile? ours = ArhLead.MissileOf(seeker);
            if (ours == null)
                return TargetCalc.GetLeadVectorWithAccel(
                    targetPos, platformPos, targetVel, platformVel, targetAccel, maxLead);

            Vector3 smooth = ArhLead.SmoothAccel(ours, Vector3.ClampMagnitude(targetAccel, 100f));
            Vector3 vDir = targetVel.sqrMagnitude > 1f ? targetVel.normalized : Vector3.zero;
            Vector3 aAlong = Vector3.Dot(smooth, vDir) * vDir;
            Vector3 aTurn = smooth - aAlong;

            Vector3 toTarget = targetPos - platformPos;
            float closing = Vector3.Dot(toTarget.normalized, platformVel - targetVel);
            float t = Mathf.Clamp(toTarget.magnitude / Mathf.Max(closing, 10f), 0f, maxLead);

            return ArhLead.TurnLead(targetVel, aTurn, t)
                   + Mathf.Min(t * t, 1f) * 0.5f * (aAlong + 9.81f * Vector3.up);
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var code = new List<CodeInstruction>(instructions);
            MethodInfo? stock = AccessTools.Method(typeof(TargetCalc), nameof(TargetCalc.GetLeadVectorWithAccel));
            MethodInfo ours = AccessTools.Method(typeof(IrLead), nameof(Lead));
            int hits = 0;
            for (int i = 0; stock != null && i < code.Count; i++)
            {
                if (code[i].opcode != OpCodes.Call || !(code[i].operand is MethodInfo m) || m != stock) continue;
                var load = new CodeInstruction(OpCodes.Ldarg_0);
                load.labels.AddRange(code[i].labels);
                load.blocks.AddRange(code[i].blocks);
                code[i] = load;
                code.Insert(i + 1, new CodeInstruction(OpCodes.Call, ours));
                i++;
                hits++;
            }
            if (hits == 1)
                Plugin.Log.LogInfo("[Meridian] IR guidance: IRSeeker.Seek now leads a turning target over the whole flight for Meridian Works rounds.");
            else
                Plugin.Log.LogWarning($"[Meridian] IR guidance: expected 1 lead call in IRSeeker.Seek, found {hits}. IR rounds keep stock lead.");
            return code;
        }
    }
}
