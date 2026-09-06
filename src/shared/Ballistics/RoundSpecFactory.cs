using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Logging;
using UnityEngine;

namespace Shared.Ballistics
{

    public static class RoundSpecFactory
    {
        private const BindingFlags Instance = BindingFlags.Instance |
                                              BindingFlags.NonPublic |
                                              BindingFlags.Public;

        private static FieldInfo? _motors;
        private static FieldInfo? _dragCurve;
        private static FieldInfo? _finArea;
        private static FieldInfo? _supersonicDrag;
        private static FieldInfo? _motorThrust;
        private static FieldInfo? _motorBurnTime;
        private static FieldInfo? _motorFuelMass;
        private static FieldInfo? _motorTopSpeed;
        private static bool _resolved;
        private static bool _usable;

        public static bool Resolve(ManualLogSource log)
        {
            if (_resolved)
            {
                return _usable;
            }
            _resolved = true;

            Type missile = typeof(Missile);
            _motors = missile.GetField("motors", Instance);
            _dragCurve = missile.GetField("dragCurve", Instance);
            _finArea = missile.GetField("finArea", Instance);
            _supersonicDrag = missile.GetField("supersonicDrag", Instance);

            var missingOnMissile = new List<string>();
            if (_motors == null) missingOnMissile.Add("motors");
            if (_dragCurve == null) missingOnMissile.Add("dragCurve");
            if (_finArea == null) missingOnMissile.Add("finArea");
            if (_supersonicDrag == null) missingOnMissile.Add("supersonicDrag");

            if (missingOnMissile.Count > 0)
            {
                log.LogError("[RocketPod] FAIL: Missile is missing " +
                             string.Join(", ", missingOnMissile.ToArray()) +
                             ". The solver cannot mirror a simulation it cannot read. " +
                             "Re-check the decompile against this game build.");
                _usable = false;
                return false;
            }

            Type? motorType = _motors!.FieldType.GetElementType();
            if (motorType == null)
            {
                log.LogError("[RocketPod] FAIL: could not reach Missile+Motor's type.");
                _usable = false;
                return false;
            }

            _motorThrust = motorType.GetField("thrust", Instance);
            _motorBurnTime = motorType.GetField("burnTime", Instance);
            _motorFuelMass = motorType.GetField("fuelMass", Instance);
            _motorTopSpeed = motorType.GetField("topSpeed", Instance);

            var missingOnMotor = new List<string>();
            if (_motorThrust == null) missingOnMotor.Add("thrust");
            if (_motorBurnTime == null) missingOnMotor.Add("burnTime");
            if (_motorFuelMass == null) missingOnMotor.Add("fuelMass");

            if (missingOnMotor.Count > 0)
            {
                log.LogError("[RocketPod] FAIL: Missile+Motor is missing " +
                             string.Join(", ", missingOnMotor.ToArray()) + ".");
                _usable = false;
                return false;
            }

            _usable = true;
            return true;
        }

        public static TrajectorySolver.RoundSpec? FromMissile(Missile missile, ManualLogSource log)
        {
            if (missile == null || !Resolve(log))
            {
                return null;
            }

            try
            {
                var spec = new TrajectorySolver.RoundSpec
                {
                    DragCurve = _dragCurve!.GetValue(missile) as AnimationCurve,
                    SupersonicDrag = (float)_supersonicDrag!.GetValue(missile),
                    AirDensity = GameAirDensity,
                    SpeedOfSound = GameSpeedOfSound,
                };

                spec.FinArea = (float)_finArea!.GetValue(missile);

                var motors = _motors!.GetValue(missile) as Array;
                var stages = new List<TrajectorySolver.MotorStage>();
                float fuel = 0f;
                float topSpeed = float.MaxValue;

                if (motors != null)
                {
                    foreach (object motor in motors)
                    {
                        if (motor == null)
                        {
                            continue;
                        }
                        float thrust = (float)_motorThrust!.GetValue(motor);
                        float burnTime = (float)_motorBurnTime!.GetValue(motor);
                        float fuelMass = (float)_motorFuelMass!.GetValue(motor);
                        stages.Add(new TrajectorySolver.MotorStage(thrust, fuelMass, burnTime));
                        fuel += fuelMass;

                        if (_motorTopSpeed != null)
                        {
                            topSpeed = Mathf.Min(topSpeed, (float)_motorTopSpeed.GetValue(motor));
                        }
                    }
                }

                spec.Motors = stages.ToArray();
                spec.TopSpeed = topSpeed;

                Rigidbody rb = missile.GetComponent<Rigidbody>();

                float declared = 0f;
                object? declaredMass = typeof(Missile)
                    .GetField("mass", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?.GetValue(missile);
                if (declaredMass is float dm) declared = dm;

                spec.LaunchMass = declared > 0f
                    ? declared
                    : (rb != null ? rb.mass : missile.GetPrefabMass());

                if (spec.LaunchMass <= 0f)
                {
                    log.LogWarning($"[RocketPod] {missile.name}: launch mass is " +
                                   $"{spec.LaunchMass}, which makes the solver meaningless.");
                }

                return spec;
            }
            catch (Exception ex)
            {
                log.LogError($"[RocketPod] Reading the flight model failed: {ex.Message}");
                return null;
            }
        }

        private static float GameAirDensity(float altitudeKm)
        {
            try
            {
                AnimationCurve? curve = GameAssets.i?.airDensityAltitude;
                if (curve != null)
                {
                    return curve.Evaluate(altitudeKm);
                }
            }
            catch
            {

            }
            return TrajectorySolver.DefaultAirDensity(altitudeKm);
        }

        private static float GameSpeedOfSound(float altitudeMetres)
        {
            try
            {
                return LevelInfo.GetSpeedOfSound(altitudeMetres);
            }
            catch
            {
                return TrajectorySolver.DefaultSpeedOfSound(altitudeMetres);
            }
        }
    }
}
