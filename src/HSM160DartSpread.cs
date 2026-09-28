using System.Collections.Generic;
using UnityEngine;

namespace MeridianWorks
{

    internal static class HSM160DartSpread
    {
        internal const int CoveredN = 3;
        internal const float NearM = 3000f;
        internal const float ConeDeg = 25f;
        internal const float LateS = 3f;
        internal const int MaxMoves = 2;
        private const float MarginM = 500f;

        private static readonly Dictionary<Unit, int> Count = new Dictionary<Unit, int>();
        private static readonly List<Unit> Scratch = new List<Unit>();
        private static int _countedFrame = -1;
        private static FactionHQ? _countedHq;
        private static bool _said;

        internal static Unit? Better(Missile m, HSM160Dart.State st, Unit current)
        {
            if (st.Retargets >= MaxMoves || m.rb == null || m.NetworkHQ == null) return null;
            Vector3 vel = m.rb.velocity;
            float speed = vel.magnitude;
            if (speed < 50f) return null;
            Vector3 toCur = current.GlobalPosition() - m.GlobalPosition();
            if (toCur.magnitude / speed < LateS) return null;

            Recount(m.NetworkHQ);
            int curN = On(current) - 1;
            if (curN <= CoveredN) return null;

            float cosCone = Mathf.Cos(ConeDeg * Mathf.Deg2Rad);
            Vector3 fwd = vel / speed;
            bool wantShip = current is Ship;
            Unit? best = null;
            int bestN = int.MaxValue;
            float bestD = float.MaxValue;
            bool bestShip = false;

            Scratch.Clear();
            BattlefieldGrid.GetUnitsInRangeNonAlloc(st.Centre, NearM, Scratch);
            foreach (Unit u in Scratch)
            {
                if (u == current || u.disabled || u is Missile || u.NetworkHQ == null || u.NetworkHQ == m.NetworkHQ) continue;
                Vector3 to = u.GlobalPosition() - m.GlobalPosition();
                float dist = to.magnitude;
                if (dist < MarginM || Vector3.Dot(to / dist, fwd) < cosCone) continue;
                int n = On(u);
                if (n > curN - 2) continue;
                bool ship = u is Ship;
                float d = FastMath.SquareDistance(u.GlobalPosition(), st.Centre);
                bool take = best == null
                    || (wantShip && ship != bestShip ? ship
                        : n != bestN ? n < bestN : d < bestD);
                if (!take) continue;
                best = u; bestN = n; bestD = d; bestShip = ship;
            }
            Scratch.Clear();
            if (best == null) return null;

            Count[current] = On(current) - 1;
            Count[best] = bestN + 1;
            m.SetTarget(best);
            if (!_said)
            {
                _said = true;
                Plugin.Log.LogInfo($"[Meridian] Needle spread: {current.unitName} had {curN} other rounds on it, "
                    + $"moved to {best.unitName} ({bestN}).");
            }
            return best;
        }

        private static int On(Unit u) => Count.TryGetValue(u, out int n) ? n : 0;

        private static void Recount(FactionHQ hq)
        {
            if (_countedFrame == Time.frameCount && _countedHq == hq) return;
            _countedFrame = Time.frameCount;
            _countedHq = hq;
            Count.Clear();
            foreach (Unit u in UnitRegistry.allUnits)
            {
                if (u is not Missile r || r.disabled || r.NetworkHQ != hq) continue;
                if (!r.targetID.TryGetUnit(out Unit t) || t == null) continue;
                Count[t] = On(t) + 1;
            }
        }
    }
}
