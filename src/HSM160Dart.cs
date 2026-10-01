using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace MeridianWorks
{

    internal static class HSM160Dart
    {
        internal const float MassKg = 60f;
        internal const float DragScale = 0.5f;
        internal const float SeeM = 20000f;
        internal const float ProxM = 25f;
        internal const float SpreadM = HSM160DispenseEarly.DetectM;

        internal sealed class State
        {
            public Unit? Target;
            public Unit? Main;
            public Transform? Part;
            public GlobalPosition Centre;
            public GlobalPosition KnownPos;
            public Vector3 KnownVel;
            public bool Chosen;
            public bool Fins;
            public bool Armed;
            public bool Visual;
            public bool EverVisual;
            public bool ContactOnly;
            public float NextLook;
            public int Retargets;
        }

        internal static readonly ConditionalWeakTable<Missile, State> Darts = new();
        private static readonly ConditionalWeakTable<Unit, StrongBox<int>> DartsOn = new();
        private static readonly List<Unit> Scratch = new List<Unit>();

        internal static readonly FieldInfo? FSeekerMissile = AccessTools.Field(typeof(MissileSeeker), "missile");
        private static readonly FieldInfo? FSupersonic = AccessTools.Field(typeof(Missile), "supersonicDrag");
        private static readonly FieldInfo? FMass = AccessTools.Field(typeof(Missile), "mass");
        private static readonly FieldInfo? FDragCurve = AccessTools.Field(typeof(Missile), "dragCurve");
        private static readonly FieldInfo? FPierce = AccessTools.Field(typeof(Missile), "pierceDamage");
        private static readonly FieldInfo? FYield = AccessTools.Field(typeof(Missile), "blastYield");

        private static bool _saidOn;
        private static bool _saidShip;

        internal static void Dress(Missile m, string parentKey)
        {
            string name = parentKey == HSM160Submunition.NuclearDispenserKey
                ? HSM160NuclearSubmunition.DartName
                : HSM160Submunition.DartName;
            if (m.IsServer) m.NetworkunitName = name;
            else m.unitName = name;

            FSupersonic?.SetValue(m, 0f);
            FMass?.SetValue(m, MassKg);
            if (m.rb != null) m.rb.mass = MassKg;
            if (FDragCurve?.GetValue(m) is AnimationCurve drag)
            {
                Keyframe[] keys = drag.keys;
                for (int i = 0; i < keys.Length; i++)
                {
                    keys[i].value *= DragScale;
                    keys[i].inTangent *= DragScale;
                    keys[i].outTangent *= DragScale;
                }
                FDragCurve.SetValue(m, new AnimationCurve(keys) { preWrapMode = drag.preWrapMode, postWrapMode = drag.postWrapMode });
            }

            if (parentKey == HSM160Submunition.DispenserKey)
            {
                FPierce?.SetValue(m, HSM160Submunition.SD6Pierce);
                FYield?.SetValue(m, HSM160Submunition.SD6YieldKg);
            }
            else if (FPierce?.GetValue(m) is float pierce) FPierce.SetValue(m, pierce * 2f);

            State st = Darts.GetValue(m, _ => new State());
            st.ContactOnly = parentKey == HSM160Submunition.DispenserKey;
            if (m.targetID.TryGetUnit(out Unit u))
            {
                st.Target = u;
                st.Main = u;
                st.Centre = u.GlobalPosition();
                st.KnownPos = st.Centre;
            }
            else
            {
                st.Centre = m.GlobalPosition() + m.transform.forward * 10000f;
                st.KnownPos = st.Centre;
            }

            if (!_saidOn)
            {
                _saidOn = true;
                Plugin.Log.LogInfo($"[Meridian] {name} dressed: own name, drag and guidance.");
            }
        }

        internal static void Seek(Missile m, State st)
        {
            float t = m.timeSinceSpawn;
            if (!st.Fins && t > 0.5f) { m.DeployFins(); st.Fins = true; }
            if (!m.IsTangible() && t > 1f) m.SetTangible(true);
            if (!st.Armed && t > 2f) { m.Arm(); st.Armed = true; }

            if (!st.Chosen)
            {
                st.Chosen = true;

                {
                    Unit? ship = LeastTargetedShip(m, st.Centre, st.Main);
                    if (ship != null && ship != st.Target)
                    {
                        st.Target = ship;
                        if (!_saidShip)
                        {
                            _saidShip = true;
                            Plugin.Log.LogInfo($"[Meridian] Needle moved onto a ship: {ship.unitName}.");
                        }
                    }
                }
                Claim(st.Target);
                st.Part = Part(st.Target);
            }

            if (st.Target == null || st.Target.disabled)
            {
                st.Target = LeastTargetedShip(m, st.Centre, st.Main);
                Claim(st.Target);
                st.Part = Part(st.Target);
                st.Visual = false;
            }

            Unit? tgt = st.Target;
            if (tgt != null)
            {
                if (Time.timeSinceLevelLoad >= st.NextLook)
                {
                    st.NextLook = Time.timeSinceLevelLoad + 0.25f;
                    Unit? spread = HSM160DartSpread.Better(m, st, tgt);
                    if (spread != null)
                    {
                        st.Target = tgt = spread;
                        Claim(spread);
                        st.Part = Part(tgt);
                        st.Visual = false;
                        st.Retargets++;
                    }
                    st.Visual = FastMath.InRange(tgt.GlobalPosition(), m.GlobalPosition(), SeeM)
                        && TargetCalc.LineOfSight(m.transform, tgt.transform, 10f);
                    if (!m.targetID.TryGetUnit(out Unit cur) || cur != tgt) m.SetTarget(tgt);
                }

                if (st.Visual)
                {
                    st.KnownPos = (st.Part != null ? st.Part : tgt.transform).GlobalPosition();
                    st.KnownVel = tgt.rb != null ? tgt.rb.velocity : Vector3.zero;
                    st.EverVisual = true;
                }
                else if (m.NetworkHQ != null && m.NetworkHQ.TryGetKnownPosition(tgt, out GlobalPosition hq))
                {
                    st.KnownPos = hq;
                    st.KnownVel = tgt.rb != null ? tgt.rb.velocity : Vector3.zero;
                    st.EverVisual = true;
                }
                else
                {
                    st.KnownPos += st.KnownVel * Time.fixedDeltaTime;
                }
            }

            Vector3 to = st.KnownPos - m.GlobalPosition();
            float dist = to.magnitude;
            float closing = Mathf.Max(Vector3.Dot(to.normalized, m.rb.velocity), 300f);
            float tgo = dist / closing;

            bool passedClose = st.EverVisual && Vector3.Dot(to, m.rb.velocity) < 0f && dist < 4f * ProxM;
            if (st.Armed && !st.ContactOnly && (dist < ProxM || passedClose))
            {
                m.Detonate(m.rb.velocity, hitArmor: false, hitTerrain: false);
                return;
            }

            Vector3 lead = st.KnownVel * tgo + Mathf.Min(tgo * tgo, 25f) * 4.905f * Vector3.up;
            m.SetAimpoint(st.KnownPos + lead, st.KnownVel);
        }

        private static Transform? Part(Unit? u)
        {
            if (u == null) return null;
            return u.maxRadius > 20f ? u.GetRandomPart() : u.transform;
        }

        private static void Claim(Unit? u)
        {
            if (u != null) DartsOn.GetValue(u, _ => new StrongBox<int>(0)).Value++;
        }

        internal static int DartsOnUnit(Unit u) => DartsOn.TryGetValue(u, out StrongBox<int> box) ? box.Value : 0;

        private static Unit? LeastTargetedShip(Missile m, GlobalPosition centre, Unit? main)
        {
            Scratch.Clear();
            BattlefieldGrid.GetUnitsInRangeNonAlloc(centre, SpreadM, Scratch);
            if (main != null && !main.disabled && !Scratch.Contains(main)) Scratch.Add(main);
            Unit? best = null;
            int bestN = int.MaxValue;
            float bestD = float.MaxValue;
            foreach (Unit u in Scratch)
            {
                if ((u is not Ship && u != main) || u.disabled || u.NetworkHQ == m.NetworkHQ) continue;
                int n = DartsOn.TryGetValue(u, out StrongBox<int> box) ? box.Value : 0;
                float d = FastMath.SquareDistance(u.GlobalPosition(), centre);
                if (n < bestN || (n == bestN && d < bestD)) { best = u; bestN = n; bestD = d; }
            }
            Scratch.Clear();
            return best;
        }
    }

    [HarmonyPatch(typeof(Missile), "StartMissile")]
    internal static class HSM160DartStart
    {
        [HarmonyPrefix]
        private static void Prefix(Missile __instance)
        {
            try
            {
                string? parent = HSM160Submunition.ParentKey(__instance);
                if (parent != HSM160Submunition.DispenserKey && parent != HSM160Submunition.NuclearDispenserKey) return;
                HSM160Dart.Dress(__instance, parent);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("[Meridian] Needle setup failed, stock round kept: " + e.Message);
            }
        }
    }

    [HarmonyPatch(typeof(OpticalSeeker), nameof(OpticalSeeker.Seek))]
    internal static class HSM160DartSeek
    {
        private static bool _failed;

        [HarmonyPrefix]
        private static bool Prefix(OpticalSeeker __instance)
        {
            if (HSM160Dart.FSeekerMissile?.GetValue(__instance) is not Missile m) return true;
            if (!HSM160Dart.Darts.TryGetValue(m, out HSM160Dart.State st)) return true;
            try
            {
                HSM160Dart.Seek(m, st);
            }
            catch (Exception e)
            {
                if (!_failed) { _failed = true; Plugin.Log.LogWarning("[Meridian] Needle guidance failed: " + e.Message); }
            }
            return false;
        }
    }

    [HarmonyPatch(typeof(OpticalSeeker), "SlowChecks")]
    internal static class HSM160DartSlowChecks
    {
        [HarmonyPrefix]
        private static bool Prefix(OpticalSeeker __instance)
        {
            if (HSM160Dart.FSeekerMissile?.GetValue(__instance) is not Missile m) return true;
            return !HSM160Dart.Darts.TryGetValue(m, out _);
        }
    }
}
