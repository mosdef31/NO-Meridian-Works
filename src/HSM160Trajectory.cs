using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace MeridianWorks
{

    internal static class HSM160ReleaseCondition
    {

        internal const float LowSpeedFloorMps = 250f;

        internal const float CoverageMarginFactor = 1.25f;

        internal static bool ShouldRelease(float distanceToAimM, float speed, float massKg,
            in HSM160BoostPlanner.Physics ph, out string trigger)
        {
            if (speed < LowSpeedFloorMps) { trigger = $"speed<{LowSpeedFloorMps:F0}"; return true; }

            float accelMps2 = ph.Stage2ThrustN / Mathf.Max(massKg, 1f);
            float meanSpeed = Mathf.Min(ph.Stage2TopSpeed, speed + 0.5f * accelMps2 * ph.Stage2BurnS);
            meanSpeed = Mathf.Max(meanSpeed, LowSpeedFloorMps);
            float coverageM = meanSpeed * Mathf.Max(ph.Stage2BurnS, 1f) * CoverageMarginFactor;

            if (distanceToAimM <= coverageM) { trigger = $"range<={coverageM / 1000f:F1}km"; return true; }
            trigger = "";
            return false;
        }
    }

    internal static class HSM160BoostPlanner
    {
        private const float CoarseDt = 1.0f;
        private const int MaxCoarseSteps = 420;
        private const int BracketSamples = 8;
        private const int BisectIters = 12;

        private const float BoostAngleLoDeg = 0f;

        private const float SteerAngleLoDeg = -60f;
        private const float AngleHiDeg = 75f;

        private static readonly Type? MotorType = AccessTools.Inner(typeof(Missile), "Motor");
        private static readonly FieldInfo? FMotors = AccessTools.Field(typeof(Missile), "motors");
        private static readonly FieldInfo? FMotorThrust = MotorType != null ? AccessTools.Field(MotorType, "thrust") : null;
        private static readonly FieldInfo? FMotorBurnTime = MotorType != null ? AccessTools.Field(MotorType, "burnTime") : null;
        private static readonly FieldInfo? FMotorFuelMass = MotorType != null ? AccessTools.Field(MotorType, "fuelMass") : null;
        private static readonly FieldInfo? FMotorTopSpeed = MotorType != null ? AccessTools.Field(MotorType, "topSpeed") : null;
        private static readonly FieldInfo? FGLimit = AccessTools.Field(typeof(Missile), "gLimit");
        private static readonly FieldInfo? FSupersonicDrag = AccessTools.Field(typeof(Missile), "supersonicDrag");
        private static readonly FieldInfo? FMaxTurnRate = AccessTools.Field(typeof(Missile), "maxTurnRate");

        internal struct Physics
        {
            internal float FinArea, GLimit, MaxTurnRateDeg, SupersonicDrag;
            internal float Stage2ThrustN, Stage2BurnS, Stage2FuelKg, Stage2TopSpeed;
        }

        internal static bool TryReadPhysics(Missile m, out Physics ph)
        {
            ph = default;
            if (FGLimit?.GetValue(m) is not float gLimit) return false;
            if (FSupersonicDrag?.GetValue(m) is not float superDrag) return false;
            if (FMaxTurnRate?.GetValue(m) is not float maxTurn) return false;
            if (FMotors?.GetValue(m) is not Array motors || motors.Length == 0) return false;
            object? motor0 = motors.GetValue(0);
            if (motor0 == null) return false;
            if (FMotorThrust?.GetValue(motor0) is not float mThrust) return false;
            if (FMotorBurnTime?.GetValue(motor0) is not float mBurn) return false;
            if (FMotorFuelMass?.GetValue(motor0) is not float mFuel) return false;
            if (FMotorTopSpeed?.GetValue(motor0) is not float mTop) return false;

            ph.FinArea = m.GetFinArea();
            ph.GLimit = gLimit;
            ph.MaxTurnRateDeg = maxTurn;
            ph.SupersonicDrag = superDrag;
            ph.Stage2ThrustN = mThrust;
            ph.Stage2BurnS = mBurn;
            ph.Stage2FuelKg = mFuel;
            ph.Stage2TopSpeed = mTop;
            return true;
        }

        internal static bool TryComputeTurnLimitDegS(Missile m, float speed, out float turnLimitDegS)
        {
            turnLimitDegS = 0f;
            if (FGLimit?.GetValue(m) is not float gLimit) return false;
            if (FMaxTurnRate?.GetValue(m) is not float maxTurn) return false;
            float v = Mathf.Max(speed, 1f);
            turnLimitDegS = Mathf.Min(maxTurn, (9.81f * gLimit / v) * Mathf.Rad2Deg);
            return true;
        }

        private struct PredictState
        {
            internal float RangeM, AltM, Speed, GammaDeg, HeadingDeg, MassKg;
            internal float BoosterThrustN, BoosterBurnRateKgS, BoosterFuelKg, RemainingBoostS;
            internal bool BoosterDone, Stage2Lit;
            internal float Stage2FuelKg;
            internal float CoastElapsedS;
        }

        private const float LiftFullQ = 0.5f * 1.225f * 300f * 300f;

        private static void Tick(ref PredictState s, float commandedPitchDeg, bool holding, in Physics ph, Missile m)
        {
            float v = Mathf.Max(s.Speed, 1f);
            float gammaRad = s.GammaDeg * Mathf.Deg2Rad;

            float turnLimit = Mathf.Min(ph.MaxTurnRateDeg, (9.81f * ph.GLimit / v) * Mathf.Rad2Deg);
            float maxStep = turnLimit * CoarseDt;
            float delta = Mathf.Clamp(commandedPitchDeg - s.HeadingDeg, -maxStep, maxStep);
            s.HeadingDeg += delta;

            float aoaDeg = Mathf.DeltaAngle(s.GammaDeg, s.HeadingDeg);
            float aoaRad = aoaDeg * Mathf.Deg2Rad;

            float thrustN = 0f;
            if (!s.BoosterDone && s.RemainingBoostS > 0f)
            {
                thrustN = s.BoosterThrustN;
            }
            else if (s.Stage2Lit && s.Stage2FuelKg > 0f && v < ph.Stage2TopSpeed)
            {
                thrustN = ph.Stage2ThrustN;
            }

            float rho = GameAssets.i.airDensityAltitude.Evaluate(s.AltM * 0.001f);
            float q = 0.5f * rho * v * v;
            float cd = m.GetDragCoef(Mathf.Abs(aoaRad));
            float dragN = cd * q * ph.FinArea;

            float aSound = LevelInfo.GetSpeedOfSound(s.AltM);
            if (ph.SupersonicDrag > 0f && aSound > 0f)
            {
                float mach = v / aSound;
                const float band = 0.1f;
                if (mach > 1f + band) dragN *= 1f + ph.SupersonicDrag;
                else if (mach > 1f - band)
                {
                    float k = ph.SupersonicDrag + 0.15f;
                    float frac = Mathf.Min(Mathf.Abs(1f - mach), band);
                    float w = (band - frac) / band;
                    dragN *= 1f + w * w * w * k;
                }
            }

            float dvDt = (thrustN * Mathf.Cos(aoaRad) - dragN) / s.MassKg - 9.81f * Mathf.Sin(gammaRad);
            float gravityDegS = 9.81f * Mathf.Cos(gammaRad) / v * Mathf.Rad2Deg;

            s.RangeM += v * Mathf.Cos(gammaRad) * CoarseDt;
            s.AltM = Mathf.Max(0f, s.AltM + v * Mathf.Sin(gammaRad) * CoarseDt);
            s.Speed = Mathf.Max(0f, v + dvDt * CoarseDt);
            float thrustTurnDegS = thrustN * Mathf.Sin(aoaRad) / s.MassKg / v * Mathf.Rad2Deg;
            if (holding)
            {

                float liftCapDegS = turnLimit * Mathf.Clamp01(q / LiftFullQ);
                float wantDegS = aoaDeg / CoarseDt + gravityDegS - thrustTurnDegS;
                float liftDegS = Mathf.Clamp(wantDegS, -liftCapDegS, liftCapDegS);
                float turnDegS = Mathf.Clamp(thrustTurnDegS + liftDegS, -turnLimit, turnLimit);
                s.GammaDeg += (turnDegS - gravityDegS) * CoarseDt;
            }
            else
            {
                s.GammaDeg += (thrustTurnDegS - gravityDegS) * CoarseDt;
            }

            if (!s.BoosterDone)
            {
                float burn = Mathf.Min(s.BoosterFuelKg, s.BoosterBurnRateKgS * CoarseDt);
                s.BoosterFuelKg -= burn;
                s.MassKg -= burn;
                s.RemainingBoostS -= CoarseDt;
                if (s.RemainingBoostS <= 0f || s.BoosterFuelKg <= 0f) s.BoosterDone = true;
            }
            else
            {
                s.CoastElapsedS += CoarseDt;
                if (s.Stage2Lit && s.Stage2FuelKg > 0f && thrustN > 0f)
                {
                    float burn = Mathf.Min(s.Stage2FuelKg, (ph.Stage2FuelKg / Mathf.Max(ph.Stage2BurnS, 0.01f)) * CoarseDt);
                    s.Stage2FuelKg -= burn;
                    s.MassKg -= burn;
                }
            }
        }

        private static float PredictImpact(in PredictState start, float candidateDeg, float holdS,
            float targetAltM, float targetRangeToGoM, in Physics ph, Missile m) =>
            PredictImpact(start, candidateDeg, holdS, targetAltM, targetRangeToGoM, ph, m, out _);

        private static float PredictImpact(in PredictState start, float candidateDeg, float holdS,
            float targetAltM, float targetRangeToGoM, in Physics ph, Missile m, out float arrivalSpeed)
        {
            PredictState s = start;
            float holdRemaining = holdS;
            int steps = 0;
            bool wasAbove = s.AltM > targetAltM;
            while (steps < MaxCoarseSteps)
            {
                bool holding = holdRemaining > 0f;
                float cmd = holding ? candidateDeg : s.GammaDeg;
                holdRemaining -= CoarseDt;

                if (s.BoosterDone && !s.Stage2Lit)
                {
                    float remainingHorizM = targetRangeToGoM - s.RangeM;
                    float heightAboveAimM = s.AltM - targetAltM;
                    float distToAimM = Mathf.Sqrt(remainingHorizM * remainingHorizM + heightAboveAimM * heightAboveAimM);
                    if (HSM160ReleaseCondition.ShouldRelease(distToAimM, s.Speed, s.MassKg, ph, out _))
                        s.Stage2Lit = true;
                }

                Tick(ref s, cmd, holding, ph, m);
                steps++;

                bool descending = s.GammaDeg <= 0f;
                if (s.AltM > targetAltM) wasAbove = true;
                if (wasAbove && s.AltM <= targetAltM && descending) break;
                if (s.AltM <= 0f) break;
            }
            arrivalSpeed = s.Speed;
            return s.RangeM;
        }

        private static PredictState BuildSeed(float speed, float gammaDeg, float headingDeg, float altM, float massKg,
            HSM160Booster.BoosterState? booster, float remainingBoostS, bool stage2Lit, float coastElapsedS, Physics ph)
        {
            bool boosterDone = booster is null || remainingBoostS <= 0f;
            return new PredictState
            {
                RangeM = 0f,
                AltM = altM,
                Speed = speed,
                GammaDeg = gammaDeg,
                HeadingDeg = headingDeg,
                MassKg = massKg,
                BoosterThrustN = booster?.ThrustN ?? 0f,
                BoosterBurnRateKgS = booster?.BurnRateKgS ?? 0f,
                BoosterFuelKg = booster?.RemainingFuelKg ?? 0f,
                RemainingBoostS = remainingBoostS,
                BoosterDone = boosterDone,
                Stage2Lit = stage2Lit,
                Stage2FuelKg = ph.Stage2FuelKg,
                CoastElapsedS = coastElapsedS,
            };
        }

        private static float BisectRoot(in PredictState seed, float holdS, float targetAltM, float targetRangeToGoM,
            in Physics ph, Missile m, float lo, float hi, float rLo, out float predictedMissM)
        {
            float rHi = rLo;
            for (int i = 0; i < BisectIters; i++)
            {
                float mid = 0.5f * (lo + hi);
                float rMid = PredictImpact(seed, mid, holdS, targetAltM, targetRangeToGoM, ph, m);
                if ((rMid - targetRangeToGoM) * (rLo - targetRangeToGoM) <= 0f) { hi = mid; rHi = rMid; }
                else { lo = mid; rLo = rMid; }
            }
            predictedMissM = 0.5f * (rLo + rHi) - targetRangeToGoM;
            return 0.5f * (lo + hi);
        }

        private const float LoftSwitchGain = 1.15f;

        private static bool BracketAndBisect(in PredictState seed, float holdS, float targetAltM,
            float targetRangeToGoM, bool preferLoft, float preferNearDeg, float loDeg, float hiDeg, in Physics ph, Missile m,
            out float solvedDeg, out float predictedMissM)
        {
            solvedDeg = seed.GammaDeg;
            predictedMissM = 0f;

            var angles = new float[BracketSamples];
            var ranges = new float[BracketSamples];
            for (int i = 0; i < BracketSamples; i++)
            {
                angles[i] = loDeg + i * (hiDeg - loDeg) / (BracketSamples - 1);
                ranges[i] = PredictImpact(seed, angles[i], holdS, targetAltM, targetRangeToGoM, ph, m);
            }

            float maxRange = ranges[0];
            int maxIdx = 0;
            for (int i = 1; i < BracketSamples; i++) { if (ranges[i] > maxRange) { maxRange = ranges[i]; maxIdx = i; } }

            if (targetRangeToGoM <= ranges[0]) { solvedDeg = angles[0]; predictedMissM = ranges[0] - targetRangeToGoM; return true; }
            if (targetRangeToGoM >= maxRange) { solvedDeg = angles[maxIdx]; predictedMissM = maxRange - targetRangeToGoM; return true; }

            bool haveNear = !float.IsNaN(preferNearDeg);

            int onlyBracket = -1;
            if (haveNear && !preferLoft)
            {
                float bestGap = float.PositiveInfinity;
                for (int i = 0; i < BracketSamples - 1; i++)
                {
                    if ((ranges[i] - targetRangeToGoM) * (ranges[i + 1] - targetRangeToGoM) > 0f) continue;
                    float gap = Mathf.Abs(0.5f * (angles[i] + angles[i + 1]) - preferNearDeg);
                    if (gap < bestGap) { bestGap = gap; onlyBracket = i; }
                }
            }
            bool found = false;
            float bestV = float.NegativeInfinity;
            float nearDeg = 0f, nearMiss = 0f, nearV = 0f, nearDist = float.PositiveInfinity;
            for (int i = 0; i < BracketSamples - 1; i++)
            {
                if ((ranges[i] - targetRangeToGoM) * (ranges[i + 1] - targetRangeToGoM) > 0f) continue;
                if (onlyBracket >= 0 && i != onlyBracket) continue;
                float deg = BisectRoot(seed, holdS, targetAltM, targetRangeToGoM, ph, m,
                    angles[i], angles[i + 1], ranges[i], out float miss);
                PredictImpact(seed, deg, holdS, targetAltM, targetRangeToGoM, ph, m, out float arrive);
                if (!found || (preferLoft && arrive > bestV))
                {
                    found = true;
                    bestV = arrive;
                    solvedDeg = deg;
                    predictedMissM = miss;
                }
                if (haveNear && Mathf.Abs(deg - preferNearDeg) < nearDist)
                {
                    nearDist = Mathf.Abs(deg - preferNearDeg);
                    nearDeg = deg; nearMiss = miss; nearV = arrive;
                }
                if (!preferLoft && !haveNear) break;
            }
            if (found && haveNear && (!preferLoft || bestV < nearV * LoftSwitchGain))
            {
                solvedDeg = nearDeg;
                predictedMissM = nearMiss;
            }
            if (!found) { solvedDeg = angles[maxIdx]; predictedMissM = maxRange - targetRangeToGoM; }
            return true;
        }

        internal static bool SolveBoostClimb(Missile m, float currentSpeed, float currentGammaDeg, float currentHeadingDeg,
            float currentAltM, float targetAltM, float currentMassKg, float targetRangeToGoM,
            HSM160Booster.BoosterState booster, float remainingBoostS, bool preferLoft, float preferNearDeg,
            out float climbDeg, out float predictedMissM)
        {
            climbDeg = currentGammaDeg;
            predictedMissM = 0f;
            if (!TryReadPhysics(m, out Physics ph)) return false;
            var seed = BuildSeed(currentSpeed, currentGammaDeg, currentHeadingDeg, currentAltM, currentMassKg,
                booster, remainingBoostS, stage2Lit: false, coastElapsedS: 0f, ph);
            return BracketAndBisect(seed, remainingBoostS, targetAltM, targetRangeToGoM, preferLoft, preferNearDeg,
                BoostAngleLoDeg, AngleHiDeg, ph, m, out climbDeg, out predictedMissM);
        }

        internal static bool SolveSteerNow(Missile m, float currentSpeed, float currentGammaDeg, float currentHeadingDeg,
            float currentAltM, float targetAltM, float currentMassKg, float targetRangeToGoM,
            bool stage2AlreadyLit, float coastElapsedSoFarS, float holdS, bool preferLoft, float preferNearDeg,
            out float steerDeg, out float predictedMissM)
        {
            steerDeg = currentGammaDeg;
            predictedMissM = 0f;
            if (!TryReadPhysics(m, out Physics ph)) return false;
            var seed = BuildSeed(currentSpeed, currentGammaDeg, currentHeadingDeg, currentAltM, currentMassKg,
                booster: null, remainingBoostS: 0f, stage2Lit: stage2AlreadyLit, coastElapsedS: coastElapsedSoFarS, ph);
            return BracketAndBisect(seed, holdS, targetAltM, targetRangeToGoM, preferLoft, preferNearDeg,
                SteerAngleLoDeg, AngleHiDeg, ph, m, out steerDeg, out predictedMissM);
        }
    }

    internal sealed class HSM160TrajectoryState
    {
        internal bool haveLaunchRange;
        internal float launchRangeM;
        internal float climbDeg;
        internal float pendingClimbDeg = float.NaN;
        internal float nextAttitudeTraceTime;
        internal bool haveSolve;
        internal float rootLockUntil = -1f;
        internal float filteredDeg = float.NaN;
        internal float commandDeg = float.NaN;
        internal float lastTickTime = -1f;
        internal float predictedMissM;
        internal float nextSolveTime;
        internal bool boosterSpent;
        internal float boosterBurnoutTime = -1f;
        internal bool stage2Ignited;
        internal bool pastApex;

        internal bool directIntercept;
        internal int logId = -1;
        internal float nextTraceTime;
        internal bool terminalLogged;
    }

    internal static class HSM160TrajectoryStateTable
    {
        internal static readonly ConditionalWeakTable<Missile, HSM160TrajectoryState> States = new();
    }

    [HarmonyPatch]
    internal static class HSM160Trajectory
    {

        private const int LoggedRoundsPerKey = 12;
        private const float TraceSeconds = 2f;

        private const float BoostResolvePeriodSeconds = 1f;

        private const float DiveCoastResolvePeriodSeconds = 0.5f;

        private const float SteerHoldSeconds = 3f;
        private const float FallbackClimbDeg = 30f;

        private const float BoostSlewDegS = 6f;
        private const float SteerSlewDegS = 8f;
        private const float BoostOutlierDeg = 25f;
        private const float BoostConfirmDeg = 10f;

        private const float RootLockSeconds = 2f;
        private const float SolveFilterSeconds = 0.6f;

        private static bool RootLockAllows(HSM160TrajectoryState state, Missile m, float solvedDeg)
        {
            if (!state.haveSolve) return true;
            if (Mathf.Abs(solvedDeg - state.climbDeg) <= BoostConfirmDeg) return true;
            if (m.timeSinceSpawn < state.rootLockUntil) return false;
            state.rootLockUntil = m.timeSinceSpawn + RootLockSeconds;
            return true;
        }

        private static float Slew(HSM160TrajectoryState state, Missile m, float rateDegS)
        {
            float now = m.timeSinceSpawn;
            float dt = state.lastTickTime >= 0f ? Mathf.Clamp(now - state.lastTickTime, 0f, 0.5f) : 0f;
            state.lastTickTime = now;
            if (float.IsNaN(state.commandDeg)) state.commandDeg = PitchDeg(m.rb.velocity);

            if (float.IsNaN(state.filteredDeg)) state.filteredDeg = state.climbDeg;
            state.filteredDeg += (state.climbDeg - state.filteredDeg) * (1f - Mathf.Exp(-dt / SolveFilterSeconds));
            state.commandDeg = Mathf.MoveTowards(state.commandDeg, state.filteredDeg, rateDegS * dt);
            return state.commandDeg;
        }

        private const float DirectInterceptMarginFactor = 1.3f;

        private const float DirectInterceptMaxTimeToGoS = 12f;

        private const float FarShotM = 40000f;

        private const float TerminalLogRangeM = 2000f;

        private static readonly FieldInfo? FMissile = AccessTools.Field(typeof(MissileSeeker), "missile");
        private static readonly FieldInfo? FTargetUnit = AccessTools.Field(typeof(MissileSeeker), "targetUnit");
        private static readonly FieldInfo? FKnownPos = AccessTools.Field(typeof(BallisticMissileGuidance), "knownPos");
        private static readonly FieldInfo? FKnownVel = AccessTools.Field(typeof(BallisticMissileGuidance), "knownVel");
        private static readonly FieldInfo? FErrorOffset = AccessTools.Field(typeof(BallisticMissileGuidance), "errorOffset");

        private static readonly System.Collections.Generic.Dictionary<string, int> LoggedCount = new();

        [HarmonyTargetMethod]
        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(BallisticMissileGuidance), "SetTrajectory");
        }

        [HarmonyPrefix]
        private static bool Prefix(BallisticMissileGuidance __instance)
        {
            try
            {
                if (FMissile?.GetValue(__instance) is not Missile m) return true;
                if ((m.definition as MissileDefinition)?.jsonKey is not string key || !HSM160Rounds.Ours.Contains(key))
                    return true;

                var state = HSM160TrajectoryStateTable.States.GetValue(m, _ => new HSM160TrajectoryState());

                var targetUnit = FTargetUnit?.GetValue(__instance) as Unit;
                GlobalPosition knownPos = FKnownPos != null ? (GlobalPosition)FKnownPos.GetValue(__instance) : default;
                Vector3 knownVel = FKnownVel != null ? (Vector3)FKnownVel.GetValue(__instance) : Vector3.zero;
                if (targetUnit != null && !targetUnit.disabled && m.NetworkHQ != null
                    && m.NetworkHQ.TryGetKnownPosition(targetUnit, out var freshPos)
                    && m.NetworkHQ.IsTargetBeingTracked(targetUnit))
                {
                    knownPos = freshPos;
                    knownVel = targetUnit.rb == null ? Vector3.zero : targetUnit.rb.velocity;
                    FKnownPos?.SetValue(__instance, knownPos);
                    FKnownVel?.SetValue(__instance, knownVel);
                }
                Vector3 errorOffset = FErrorOffset != null ? (Vector3)FErrorOffset.GetValue(__instance) : Vector3.zero;

                GlobalPosition pos = m.GlobalPosition();
                Vector3 v = m.rb.velocity;
                Vector3 h = (knownPos - pos);
                h.y = 0f;
                float range = h.magnitude;
                Vector3 hn = range > 0.01f ? h / range : m.transform.forward;

                if (!state.haveLaunchRange)
                {
                    state.haveLaunchRange = true;
                    state.launchRangeM = range;
                    state.climbDeg = FallbackClimbDeg;
                    ClaimLogSlot(key, state);
                }

                bool boosterBurning = m.boosterIsAttached && !state.boosterSpent;
                float coastElapsedS = state.boosterBurnoutTime >= 0f ? m.timeSinceSpawn - state.boosterBurnoutTime : 0f;

                Vector3 aimDelta3 = knownPos - pos;
                float distanceToAimM = aimDelta3.magnitude;
                bool shouldIgnite = false;
                string igniteTrigger = "";
                if (!boosterBurning && !state.stage2Ignited)
                {
                    if (HSM160BoostPlanner.TryReadPhysics(m, out HSM160BoostPlanner.Physics igPh))
                    {
                        shouldIgnite = HSM160ReleaseCondition.ShouldRelease(distanceToAimM, v.magnitude, m.rb.mass, igPh, out igniteTrigger);
                    }
                    else if (v.y <= 0f || v.magnitude <= HSM160ReleaseCondition.LowSpeedFloorMps)
                    {
                        shouldIgnite = true;
                        igniteTrigger = "physics read failed, fallback";
                        Plugin.Log.LogWarning("[Meridian] HSM-160 physics read failed, using fallback ignition rule");
                    }
                }
                if (shouldIgnite)
                {
                    LogEvent(key, state, "ignite", m, range, hn, igniteTrigger);
                    HSM160TrajectoryRelease.Release(m, key);
                    state.stage2Ignited = true;
                    state.pastApex = true;
                    state.nextSolveTime = 0f;
                }

                if (!state.pastApex && v.y <= 0f) state.pastApex = true;

                if (state.logId >= 0 && !state.terminalLogged && range < TerminalLogRangeM)
                {
                    state.terminalLogged = true;
                    float heightAboveAimM = pos.y - knownPos.y;
                    Plugin.Log.LogInfo($"[Meridian] {key}#{state.logId}: TERMINAL range={range / 1000f:F2}km "
                        + $"hAim={heightAboveAimM:F0}m lastPred={(state.predictedMissM >= 0f ? "+" : "")}{state.predictedMissM / 1000f:F2}km");
                }

                GlobalPosition aimpoint;
                if (boosterBurning)
                {

                    if (m.timeSinceSpawn >= state.nextSolveTime)
                    {
                        state.nextSolveTime = m.timeSinceSpawn + BoostResolvePeriodSeconds;
                        var boosterState = HSM160Booster.ReadState(m);
                        if (boosterState is HSM160Booster.BoosterState bs && bs.BurnRateKgS > 0.001f)
                        {
                            float remainingBoostS = bs.RemainingFuelKg / bs.BurnRateKgS;
                            if (HSM160BoostPlanner.SolveBoostClimb(m, v.magnitude, PitchDeg(v), NoseHeadingDeg(m), pos.y, knownPos.y,
                                    m.rb.mass, range, bs, remainingBoostS, state.launchRangeM >= FarShotM, SolveAnchor(state),
                                    out float solvedClimb, out float solvedMiss))
                            {

                                bool jump = state.haveSolve && Mathf.Abs(solvedClimb - state.climbDeg) > BoostOutlierDeg;
                                bool confirmed = !float.IsNaN(state.pendingClimbDeg)
                                                 && Mathf.Abs(solvedClimb - state.pendingClimbDeg) <= BoostConfirmDeg;
                                if (jump && !confirmed)
                                {
                                    state.pendingClimbDeg = solvedClimb;
                                }
                                else if (!RootLockAllows(state, m, solvedClimb))
                                {
                                    state.pendingClimbDeg = float.NaN;
                                }
                                else
                                {
                                    state.pendingClimbDeg = float.NaN;
                                    state.climbDeg = solvedClimb;
                                    state.haveSolve = true;
                                    state.predictedMissM = solvedMiss;
                                }
                            }
                        }
                    }

                    float cmd = Slew(state, m, BoostSlewDegS);
                    HSM160AttitudeTrace.Sample(m, state, key, "boost", cmd);
                    if (state.logId >= 0 && m.timeSinceSpawn >= state.nextTraceTime)
                    {
                        state.nextTraceTime = m.timeSinceSpawn + TraceSeconds;

                        string tag = key.Replace("Meridian", "").Replace("_Missile", "");
                        Plugin.Log.LogInfo($"[Meridian] {tag}#{state.logId} boost t={m.timeSinceSpawn:F0} alt={pos.y:F0} "
                            + $"v={m.speed:F0} p={PitchDeg(v):F0} s={state.climbDeg:F0} c={cmd:F0} "
                            + $"pred={state.predictedMissM / 1000f:+0.0;-0.0}km r={range / 1000f:F0}km");
                    }
                    Vector3 dir = Vector3.RotateTowards(hn, Vector3.up, cmd * Mathf.Deg2Rad, 0f);
                    aimpoint = pos + dir * 1000f;
                }
                else
                {

                    if (state.pastApex)
                    {
                        Unit? held = HSM160Seeker.Look(m, key, targetUnit, ref knownPos, ref knownVel);
                        if (held != null)
                        {
                            FTargetUnit?.SetValue(__instance, held);
                            FKnownPos?.SetValue(__instance, knownPos);
                            FKnownVel?.SetValue(__instance, knownVel);
                        }
                    }

                    float fallTime = Kinematics.FallTime(pos.y - knownPos.y, v.y);
                    GlobalPosition baseAim = knownPos + knownVel * fallTime;
                    Vector3 leadVector = TargetCalc.GetLeadVector(baseAim, pos, knownVel, v, 30f);
                    GlobalPosition rawAim = baseAim + leadVector + errorOffset;

                    if (!state.directIntercept && state.pastApex)
                    {
                        Vector3 rawDelta = rawAim - pos;
                        float directPitchDeg = Mathf.Atan2(rawDelta.y, new Vector2(rawDelta.x, rawDelta.z).magnitude) * Mathf.Rad2Deg;
                        float requiredTurnDeg = Mathf.Abs(Mathf.DeltaAngle(PitchDeg(v), directPitchDeg));
                        float horizontalSpeed = new Vector2(v.x, v.z).magnitude;
                        float timeToGoS = Mathf.Max(fallTime, range / Mathf.Max(horizontalSpeed, 50f));

                        if (timeToGoS <= DirectInterceptMaxTimeToGoS
                            && HSM160BoostPlanner.TryComputeTurnLimitDegS(m, v.magnitude, out float turnLimitDegS)
                            && turnLimitDegS * timeToGoS >= requiredTurnDeg * DirectInterceptMarginFactor)
                        {
                            state.directIntercept = true;
                            LogEvent(key, state, "direct-intercept", m, range, hn, "turn budget sufficient");
                        }
                    }

                    if (state.directIntercept)
                    {
                        aimpoint = rawAim;
                    }
                    else
                    {
                        if (m.timeSinceSpawn >= state.nextSolveTime)
                        {
                            state.nextSolveTime = m.timeSinceSpawn + DiveCoastResolvePeriodSeconds;
                            if (HSM160BoostPlanner.SolveSteerNow(m, v.magnitude, PitchDeg(v), NoseHeadingDeg(m), pos.y, knownPos.y,
                                    m.rb.mass, range, stage2AlreadyLit: state.stage2Ignited, coastElapsedSoFarS: coastElapsedS,
                                    holdS: SteerHoldSeconds, preferLoft: state.launchRangeM >= FarShotM, preferNearDeg: SolveAnchor(state),
                                    out float solvedSteer, out float solvedMiss)
                                && RootLockAllows(state, m, solvedSteer))
                            {
                                state.climbDeg = solvedSteer;
                                state.haveSolve = true;
                                state.predictedMissM = solvedMiss;
                            }
                        }
                        float cmd = Slew(state, m, SteerSlewDegS);
                        HSM160AttitudeTrace.Sample(m, state, key, "coast", cmd);
                        Vector3 dir = Vector3.RotateTowards(hn, Vector3.up, cmd * Mathf.Deg2Rad, 0f);
                        aimpoint = pos + dir * 1000f;
                    }

                    if (state.logId >= 0 && m.timeSinceSpawn >= state.nextTraceTime)
                    {
                        state.nextTraceTime = m.timeSinceSpawn + TraceSeconds;
                        string stage = (state.stage2Ignited ? "stage2" : "coast") + (state.directIntercept ? "+direct" : "");
                        Plugin.Log.LogInfo($"[Meridian] {key}#{state.logId}: {stage} t={m.timeSinceSpawn:F1}s alt={pos.y:F0}m "
                            + $"spd={m.speed:F0}m/s pitch={PitchDeg(v):F0} hdgErr={HeadingErrDeg(v, hn):F0} "
                            + $"{(state.directIntercept ? "direct" : $"steer={state.climbDeg:F0} cmd={state.commandDeg:F0} pred={(state.predictedMissM >= 0f ? "+" : "")}{state.predictedMissM / 1000f:F2}km")} "
                            + $"range={range / 1000f:F2}km");
                    }
                }

                m.SetAimpoint(aimpoint, knownVel);
                return false;
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("[Meridian] HSM-160 trajectory shaping failed, stock SetTrajectory kept: " + e.Message);
                return true;
            }
        }

        internal static void MarkBoosterSpent(Missile m)
        {
            var state = HSM160TrajectoryStateTable.States.GetValue(m, _ => new HSM160TrajectoryState());
            if (state.boosterSpent) return;
            state.boosterSpent = true;
            state.boosterBurnoutTime = m.timeSinceSpawn;
        }

        internal static void LogEvent(string key, HSM160TrajectoryState state, string ev,
            Missile m, float rangeM, Vector3 hn, string trigger)
        {
            if (!ClaimLogSlot(key, state)) return;

            GlobalPosition pos = m.GlobalPosition();
            Vector3 v = m.rb.velocity;
            Plugin.Log.LogInfo($"[Meridian] {key}#{state.logId}: {ev} t={m.timeSinceSpawn:F1}s alt={pos.y:F0}m spd={m.speed:F0}m/s "
                + $"pitch={PitchDeg(v):F0} hdgErr={HeadingErrDeg(v, hn):F0} range={rangeM / 1000f:F1}km "
                + $"launch={state.launchRangeM / 1000f:F1}km cmd={state.climbDeg:F0} "
                + $"missPred={(state.predictedMissM >= 0f ? "+" : "")}{state.predictedMissM / 1000f:F2}km trigger={trigger}");
        }

        private static float SolveAnchor(HSM160TrajectoryState state) =>
            state.haveSolve ? state.climbDeg : float.NaN;

        private static bool ClaimLogSlot(string key, HSM160TrajectoryState state)
        {
            if (state.logId >= 0) return true;
            int count = LoggedCount.TryGetValue(key, out var c) ? c : 0;
            if (count >= LoggedRoundsPerKey) return false;
            LoggedCount[key] = count + 1;
            state.logId = count;
            return true;
        }

        private static float PitchDeg(Vector3 v) =>
            Mathf.Atan2(v.y, new Vector2(v.x, v.z).magnitude) * Mathf.Rad2Deg;

        private static float NoseHeadingDeg(Missile m) =>
            Mathf.Atan2(m.transform.forward.y, new Vector2(m.transform.forward.x, m.transform.forward.z).magnitude) * Mathf.Rad2Deg;

        internal static float HeadingErrDeg(Vector3 v, Vector3 hn)
        {
            var track = new Vector3(v.x, 0f, v.z);
            return track.sqrMagnitude < 1f ? 0f : Vector3.Angle(track, hn);
        }
    }

    internal static class HSM160TrajectoryRelease
    {
        internal static void Release(Missile m, string key)
        {
            try
            {
                if (!HSM160ApexState.Released.TryGetValue(m, out _))
                {
                    HSM160ApexState.Released.Add(m, new object());
                }

                if (m == null || m.disabled) return;

                var booster = m.GetComponentInChildren<VLSBooster>(true);
                if (booster == null) return;

                HSM160TrajectoryBurnout.AllowRealBurnout(booster);
                booster.Burnout();
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("[Meridian] HSM-160 apex release/separation failed: " + e.Message);
            }
        }
    }

    [HarmonyPatch(typeof(VLSBooster), "Burnout")]
    internal static class HSM160TrajectoryBurnout
    {
        private static readonly FieldInfo? FMissile = AccessTools.Field(typeof(VLSBooster), "missile");
        private static readonly FieldInfo? FParticles = AccessTools.Field(typeof(VLSBooster), "particleSystems");
        private static readonly FieldInfo? FAudio = AccessTools.Field(typeof(VLSBooster), "audioSources");
        private static readonly FieldInfo? FLights = AccessTools.Field(typeof(VLSBooster), "lights");

        private static readonly ConditionalWeakTable<VLSBooster, object> AllowedOnce = new();

        internal static void AllowRealBurnout(VLSBooster booster)
        {
            if (!AllowedOnce.TryGetValue(booster, out _))
            {
                AllowedOnce.Add(booster, new object());
            }
        }

        private static bool Prefix(VLSBooster __instance)
        {
            try
            {
                if (AllowedOnce.TryGetValue(__instance, out _)) return true;

                if (FMissile?.GetValue(__instance) is not Missile m) return true;
                if ((m.definition as MissileDefinition)?.jsonKey is not string key || !HSM160Rounds.Ours.Contains(key))
                    return true;

                if (FParticles?.GetValue(__instance) is ParticleSystem[] particles)
                {
                    foreach (var p in particles) p?.Stop();
                }
                if (FAudio?.GetValue(__instance) is AudioSource[] audio)
                {
                    foreach (var a in audio) { if (a != null && a.loop) a.Stop(); }
                }
                if (FLights?.GetValue(__instance) is Light[] lights)
                {
                    foreach (var l in lights) { if (l != null) l.enabled = false; }
                }

                HSM160Trajectory.MarkBoosterSpent(m);
                return false;
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("[Meridian] HSM-160 held-Burnout failed, stock separation kept: " + e.Message);
                return true;
            }
        }
    }
}
