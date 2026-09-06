using System;
using UnityEngine;

namespace Shared.Ballistics
{

    public static class TrajectorySolver
    {

        public readonly struct MotorStage
        {
            public readonly float Thrust;
            public readonly float FuelMass;
            public readonly float BurnRate;

            public MotorStage(float thrust, float fuelMass, float burnTime)
            {
                Thrust = thrust;
                FuelMass = fuelMass;
                BurnRate = burnTime > 0f ? fuelMass / burnTime : 0f;
            }

            public float BurnTime => BurnRate > 0f ? FuelMass / BurnRate : 0f;
        }

        public sealed class RoundSpec
        {
            public float LaunchMass;
            public float FinArea;
            public float SupersonicDrag;
            public float TopSpeed = float.MaxValue;
            public MotorStage[] Motors = Array.Empty<MotorStage>();

            public AnimationCurve? DragCurve;

            public Func<float, float> AirDensity = DefaultAirDensity;

            public Func<float, float> SpeedOfSound = DefaultSpeedOfSound;

            public float DragCoefficientAtZeroAlpha =>
                DragCurve != null ? DragCurve.Evaluate(0f) : 0.2f;
        }

        public readonly struct Result
        {
            public readonly bool Hit;
            public readonly Vector3 ImpactPoint;
            public readonly float TimeOfFlight;
            public readonly float ImpactSpeed;
            public readonly float PeakAltitude;
            public readonly float BurnoutSpeed;
            public readonly float PeakSpeed;
            public readonly int Steps;

            public Result(bool hit, Vector3 impact, float tof, float impactSpeed,
                          float peakAltitude, float burnoutSpeed, float peakSpeed, int steps)
            {
                Hit = hit;
                ImpactPoint = impact;
                TimeOfFlight = tof;
                ImpactSpeed = impactSpeed;
                PeakAltitude = peakAltitude;
                BurnoutSpeed = burnoutSpeed;
                PeakSpeed = peakSpeed;
                Steps = steps;
            }
        }

        public const float PhysicsStep = 0.02f;

        public const int MaxSteps = 12000;

        public static Result Integrate(
            RoundSpec spec,
            Vector3 position,
            Vector3 velocity,
            float groundY,
            Vector3 wind = default,
            float stepScale = 1f,
            Action<float, Vector3, Vector3>? sample = null,
            float sampleInterval = 1f,
            float dragScale = 1f)
        {
            if (spec == null)
            {
                throw new ArgumentNullException(nameof(spec));
            }

            float dt = PhysicsStep * Mathf.Max(1f, stepScale);
            float mass = Mathf.Max(spec.LaunchMass, 0.001f);
            float t = 0f;
            float peak = position.y;
            float burnoutSpeed = 0f;
            float peakSpeed = velocity.magnitude;

            int stage = 0;
            float stageFuel = spec.Motors.Length > 0 ? spec.Motors[0].FuelMass : 0f;
            float nextSample = 0f;

            int step = 0;
            for (; step < MaxSteps; step++)
            {

                if (sample != null && t >= nextSample)
                {
                    nextSample += sampleInterval;
                    sample(t, position, velocity);
                }

                Vector3 thrustForce = Vector3.zero;
                if (stage < spec.Motors.Length)
                {
                    MotorStage motor = spec.Motors[stage];

                    if (stageFuel <= 0f)
                    {
                        stage++;
                        if (burnoutSpeed <= 0f)
                        {
                            burnoutSpeed = velocity.magnitude;
                        }
                        stageFuel = stage < spec.Motors.Length ? spec.Motors[stage].FuelMass : 0f;
                    }
                    else
                    {

                        float burned = motor.BurnRate * dt;
                        stageFuel -= burned;
                        mass = Mathf.Max(mass - burned, 0.001f);

                        if (velocity.magnitude < spec.TopSpeed)
                        {
                            thrustForce = velocity.sqrMagnitude > 1e-6f
                                ? velocity.normalized * motor.Thrust
                                : Vector3.zero;
                        }
                    }
                }

                Vector3 vRel = velocity - wind;
                float vSqr = vRel.sqrMagnitude;
                Vector3 dragForce = Vector3.zero;

                if (vSqr > 1e-6f)
                {

                    float rho = spec.AirDensity(position.y * 0.001f);
                    float cd = spec.DragCoefficientAtZeroAlpha;
                    float dragMag = cd * rho * vSqr * 0.5f * spec.FinArea;

                    if (spec.SupersonicDrag > 0f)
                    {
                        dragMag *= SupersonicMultiplier(
                            spec, Mathf.Sqrt(vSqr), spec.SpeedOfSound(position.y));
                    }

                    dragForce = -vRel.normalized * dragMag * dragScale;
                }

                Vector3 accel = (thrustForce + dragForce) / mass + Physics.gravity;
                velocity += accel * dt;
                Vector3 next = position + velocity * dt;
                t += dt;

                if (next.y > peak)
                {
                    peak = next.y;
                }

                float sp = velocity.magnitude;
                if (sp > peakSpeed)
                {
                    peakSpeed = sp;
                }

                if (next.y <= groundY && velocity.y < 0f)
                {

                    float span = position.y - next.y;
                    float frac = span > 1e-6f
                        ? Mathf.Clamp01((position.y - groundY) / span)
                        : 0f;
                    Vector3 impact = Vector3.Lerp(position, next, frac);
                    return new Result(
                        true, impact, t - dt * (1f - frac),
                        velocity.magnitude, peak, burnoutSpeed, peakSpeed, step + 1);
                }

                position = next;
            }

            return new Result(false, position, t, velocity.magnitude, peak,
                              burnoutSpeed, peakSpeed, step);
        }

        private static float SupersonicMultiplier(RoundSpec spec, float speed, float speedOfSound)
        {
            const float band = 0.1f;

            if (speed > (1f + band) * speedOfSound)
            {
                return 1f + spec.SupersonicDrag;
            }

            if (speed > (1f - band) * speedOfSound)
            {
                float peak = spec.SupersonicDrag + 0.15f;
                float distance = Mathf.Min(Mathf.Abs((speedOfSound - speed) / speedOfSound), band);
                float blend = (band - distance) / band;
                return 1f + blend * blend * blend * peak;
            }

            return 1f;
        }

        public static float? SolveLaunchElevation(
            RoundSpec spec,
            Vector3 position,
            Vector3 launchHeading,
            float launchSpeed,
            float range,
            float groundY,
            bool loft,
            Vector3 wind = default,
            int iterations = 18)
        {
            Vector3 flat = new Vector3(launchHeading.x, 0f, launchHeading.z);
            if (flat.sqrMagnitude < 1e-6f)
            {
                return null;
            }
            flat.Normalize();

            float lo = loft ? 0f : -20f;
            float hi = loft ? 80f : 20f;

            float ErrorAt(float degrees)
            {
                Vector3 dir = Quaternion.AngleAxis(
                    -degrees, Vector3.Cross(Vector3.up, flat)) * flat;
                Result r = Integrate(spec, position, dir * launchSpeed, groundY, wind);
                if (!r.Hit)
                {
                    return float.MaxValue;
                }
                Vector3 d = r.ImpactPoint - position;
                return new Vector2(d.x, d.z).magnitude - range;
            }

            float errLo = ErrorAt(lo);
            float errHi = ErrorAt(hi);

            if (errLo > 0f && errHi > 0f)
            {
                return null;
            }
            if (errLo < 0f && errHi < 0f)
            {
                return null;
            }

            for (int i = 0; i < iterations; i++)
            {
                float mid = (lo + hi) * 0.5f;
                float err = ErrorAt(mid);
                if (Mathf.Abs(err) < 5f)
                {
                    return mid;
                }
                if ((err < 0f) == (errLo < 0f))
                {
                    lo = mid;
                    errLo = err;
                }
                else
                {
                    hi = mid;
                }
            }

            return (lo + hi) * 0.5f;
        }

        public static float MaxRange(
            RoundSpec spec,
            Vector3 position,
            Vector3 launchHeading,
            float launchSpeed,
            float groundY,
            Vector3 wind = default,
            float stepDegrees = 5f)
        {
            Vector3 flat = new Vector3(launchHeading.x, 0f, launchHeading.z);
            if (flat.sqrMagnitude < 1e-6f)
            {
                return 0f;
            }
            flat.Normalize();
            Vector3 axis = Vector3.Cross(Vector3.up, flat);

            float best = 0f;
            for (float deg = 0f; deg <= 70f; deg += stepDegrees)
            {
                Vector3 dir = Quaternion.AngleAxis(-deg, axis) * flat;
                Result r = Integrate(spec, position, dir * launchSpeed, groundY, wind, stepScale: 2f);
                if (!r.Hit)
                {
                    continue;
                }
                Vector3 d = r.ImpactPoint - position;
                best = Mathf.Max(best, new Vector2(d.x, d.z).magnitude);
            }
            return best;
        }

        public static float DefaultAirDensity(float altitudeKm)
        {
            return 1.225f * Mathf.Exp(-altitudeKm / 8.5f);
        }

        public static float DefaultSpeedOfSound(float altitudeMetres)
        {
            float t = 288.15f - 0.0065f * Mathf.Clamp(altitudeMetres, 0f, 11000f);
            return 20.05f * Mathf.Sqrt(t);
        }
    }
}
