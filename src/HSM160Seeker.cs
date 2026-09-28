using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace MeridianWorks
{

    internal static class HSM160Seeker
    {
        private const float RadarRangeM = 60000f;
        private const float BasketRadiusM = 10000f;
        private const float EarthDiameterM = 12742000f;

        private sealed class State
        {
            public bool Locked;
            public bool Said;
        }

        private static readonly ConditionalWeakTable<Missile, State> States = new();
        private static readonly List<Unit> Scratch = new List<Unit>();
        private static int _lockLines;
        private const int LockLinesPerSession = 6;

        internal static bool HasLock(Missile m) => States.TryGetValue(m, out State s) && s.Locked;

        internal static Unit? Look(Missile m, string key, Unit? target, ref GlobalPosition knownPos, ref Vector3 knownVel)
        {
            State st = States.GetValue(m, _ => new State());
            GlobalPosition pos = m.GlobalPosition();

            Unit? held = null;
            if (target != null && !target.disabled && Sees(m, pos, target, knownPos))
                held = target;
            else if (target == null || target.disabled)
                held = FindShip(m, pos, knownPos);

            if (held == null)
            {
                st.Locked = false;
                return null;
            }

            if (held != target)
            {
                m.SetTarget(held);
            }
            knownPos = held.GlobalPosition();
            knownVel = held.rb == null ? Vector3.zero : held.rb.velocity;
            if (m.seekerMode != Missile.SeekerMode.activeLock)
                m.NetworkseekerMode = Missile.SeekerMode.activeLock;

            if (!st.Locked && !st.Said && _lockLines < LockLinesPerSession)
            {
                st.Said = true;
                _lockLines++;
                string how = held == target ? "held its target" : "found a new ship";
                Plugin.Log.LogInfo($"[Meridian] HSM-160 radar {how}: {held.unitName} at {FastMath.Distance(pos, held.GlobalPosition()) / 1000f:F1} km ({key}).");
            }
            st.Locked = true;
            return held;
        }

        private static bool Sees(Missile m, GlobalPosition pos, Unit u, GlobalPosition insAim)
        {
            GlobalPosition up = u.GlobalPosition();
            float slant = FastMath.Distance(pos, up);
            if (slant > RadarRangeM) return false;
            Vector3 off = up - insAim;
            off.y = 0f;
            if (off.magnitude > BasketRadiusM) return false;
            float horizon = Mathf.Sqrt(EarthDiameterM * Mathf.Max(0f, pos.y)) + Mathf.Sqrt(EarthDiameterM * Mathf.Max(0f, up.y));
            if (horizon < slant) return false;
            return TargetCalc.LineOfSight(m.transform, u.transform, 10f);
        }

        private static Unit? FindShip(Missile m, GlobalPosition pos, GlobalPosition insAim)
        {
            Scratch.Clear();
            BattlefieldGrid.GetUnitsInRangeNonAlloc(insAim, BasketRadiusM, Scratch);
            Unit? best = null;
            float bestD = float.MaxValue;
            foreach (Unit u in Scratch)
            {
                if (u is not Ship || u.disabled || u.NetworkHQ == m.NetworkHQ) continue;
                if (!Sees(m, pos, u, insAim)) continue;
                Vector3 d = u.GlobalPosition() - insAim;
                d.y = 0f;
                float dm = d.sqrMagnitude;
                if (dm < bestD) { bestD = dm; best = u; }
            }
            Scratch.Clear();
            return best;
        }
    }
}
