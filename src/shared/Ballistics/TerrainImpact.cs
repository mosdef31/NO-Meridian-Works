using System.Collections.Generic;
using UnityEngine;

namespace Shared.Ballistics
{

    internal static class TerrainImpact
    {

        private const float PathSampleSeconds = 1f;

        private const int BisectionSteps = 4;

        private const int MaxPathSamples = 64;

        private static readonly List<Vector3> _path = new List<Vector3>(MaxPathSamples);

        public static TrajectorySolver.Result Solve(
            TrajectorySolver.RoundSpec spec,
            Vector3 launch,
            Vector3 velocity,
            float stepScale,
            SampleGround sampleGround,
            bool sampleTerrain)
        {
            _path.Clear();

            TrajectorySolver.Result r = TrajectorySolver.Integrate(
                spec, launch, velocity, groundY: 0f, wind: default, stepScale: stepScale,
                sample: (t, pos, vel) => { if (_path.Count < MaxPathSamples) _path.Add(pos); },
                sampleInterval: PathSampleSeconds);

            if (!r.Hit || !sampleTerrain || _path.Count < 2) return r;

            int hitIndex = -1;
            for (int i = 1; i < _path.Count; i++)
            {
                if (!sampleGround(_path[i], out float g)) continue;
                if (_path[i].y <= g) { hitIndex = i; break; }
            }

            if (hitIndex < 0) return r;

            Vector3 above = _path[hitIndex - 1];
            Vector3 below = _path[hitIndex];

            for (int i = 0; i < BisectionSteps; i++)
            {
                Vector3 mid = (above + below) * 0.5f;
                if (!sampleGround(mid, out float g)) break;
                if (mid.y <= g) below = mid; else above = mid;
            }

            TrajectorySolver.Result onTerrain = TrajectorySolver.Integrate(
                spec, launch, velocity, groundY: below.y, wind: default, stepScale: stepScale);

            return onTerrain.Hit ? onTerrain : r;
        }

        internal delegate bool SampleGround(Vector3 globalPoint, out float height);
    }
}
