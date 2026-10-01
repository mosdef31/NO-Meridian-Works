using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace MeridianWorks
{

    internal sealed class HSM160Spent : MonoBehaviour
    {
        internal const float SpentYieldKg = 120f;
        private const float FadeFromMinS = 0f, FadeFromMaxS = 0.3f;
        private const float FadeLenMinS = 1f, FadeLenMaxS = 2.5f;
        private const float Unstable = 6f;
        private const float AsymMin = 0.15f, AsymMax = 0.6f;
        private const float RollMax = 0.8f;
        private const float QRefSpeed = 1000f;
        private const float MaxMoment = 14f;
        private const float BoostFullS = 6f;
        private const float MaxLifeS = 180f;

        private const float MaxBleed = 18f;
        private const float SpeedOfSound = 340f;
        private const float MaxPathTurn = 12f;
        private Vector3 _vPrev;

        private const float GustMin = 1.2f, GustMax = 3.2f;
        private const float GustTauMin = 0.25f, GustTauMax = 0.9f;
        private const float Damp = 1.6f;
        private const float RollDamp = 0.5f;
        private float _gust, _gustTau;
        private Vector3 _gustState;

        private static readonly FieldInfo? FTorque = AccessTools.Field(typeof(Missile), "torque");
        private static readonly FieldInfo? FYield = AccessTools.Field(typeof(Missile), "blastYield");
        private static readonly FieldInfo? FMotors = AccessTools.Field(typeof(Missile), "motors");
        private static readonly FieldInfo? FImpactFuse = AccessTools.Field(typeof(Missile), "impactFuse");

        private Missile _m = null!;
        private HSM160Fx? _fx;
        private float _t0;
        private float _torque;
        private float _fadeFrom, _fadeTo;
        private Vector3 _asym;
        private float _floor = -1f;
        private static bool _logged;

        internal static void Begin(Missile m, string key)
        {
            try
            {
                if (m == null || m.disabled || m.GetComponent<HSM160Spent>() != null) return;
                HSM160Spent s = m.gameObject.AddComponent<HSM160Spent>();
                s._m = m;
                s._fx = m.GetComponent<HSM160Fx>();
                s._t0 = Time.time;
                s._torque = FTorque?.GetValue(m) is float f ? f : 0f;
                s._fadeFrom = UnityEngine.Random.Range(FadeFromMinS, FadeFromMaxS);
                s._fadeTo = s._fadeFrom + UnityEngine.Random.Range(FadeLenMinS, FadeLenMaxS);
                Vector2 side = UnityEngine.Random.insideUnitCircle.normalized * UnityEngine.Random.Range(AsymMin, AsymMax);
                s._asym = new Vector3(side.x, side.y, UnityEngine.Random.Range(-RollMax, RollMax));
                s._gust = UnityEngine.Random.Range(GustMin, GustMax);
                s._gustTau = UnityEngine.Random.Range(GustTauMin, GustTauMax);

                int cut = CutMotor(m);
                FYield?.SetValue(m, SpentYieldKg);
                FImpactFuse?.SetValue(m, true);
                if (m.IsServer) m.Arm();

                if (!_logged)
                {
                    _logged = true;
                    Plugin.Log.LogInfo($"[Meridian] {key}: empty carrier flies on (departing, {SpentYieldKg} kg on impact, {cut} motor(s) cut).");
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("[Meridian] HSM-160 spent carrier failed, ending it the old way: " + e.Message);
                if (m != null && !m.disabled && m.IsServer)
                {
                    m.Networkdisabled = true;
                    Destroy(m.gameObject, 5f);
                }
            }
        }

        private static int CutMotor(Missile m)
        {
            if (FMotors?.GetValue(m) is not Array motors) return 0;
            int cut = 0;
            foreach (object? motor in motors)
            {
                if (motor == null) continue;
                Traverse t = Traverse.Create(motor);
                t.Field("fuelMass").SetValue(0f);
                t.Method("Burnout", new[] { typeof(bool) }).GetValue(true);
                cut++;
            }
            m.SetThrottle(0f);
            return cut;
        }

        private void FixedUpdate()
        {
            if (_m == null || _m.disabled) { enabled = false; return; }
            if (!_m.LocalSim || _m.rb == null) return;

            float t = Time.time - _t0;
            if (t > MaxLifeS)
            {
                _m.Detonate(Vector3.up, hitArmor: false, hitTerrain: false);
                return;
            }

            float lost = Smooth(_fadeFrom, _fadeTo, t);
            FTorque?.SetValue(_m, _torque * (1f - lost));

            Vector3 v = _m.rb.velocity;
            float speed = v.magnitude;
            if (speed < 1f) { _vPrev = v; return; }

            if (_vPrev.sqrMagnitude > 1f)
            {
                float dt = Time.fixedDeltaTime;
                Vector3 g = Physics.gravity * dt;
                Vector3 dv = v - _vPrev - g;
                Vector3 dir = _vPrev.normalized;
                Vector3 along = Vector3.Dot(dv, dir) * dir;
                Vector3 across = Vector3.ClampMagnitude(dv - along, MaxPathTurn * dt);
                v = _vPrev + g + along + across;
                speed = v.magnitude;
            }

            _floor = _floor < 0f ? speed : Mathf.Max(speed, _floor - MaxBleed * Time.fixedDeltaTime);
            if (speed < _floor)
            {
                v *= _floor / speed;
                speed = _floor;
            }
            _m.rb.velocity = v;
            _vPrev = v;
            if (lost <= 0f) return;

            float q = Mathf.Min((speed / QRefSpeed) * (speed / QRefSpeed), 4f);

            float dtf = Time.fixedDeltaTime;
            Vector3 flow = v / speed;
            float sinAoA = Vector3.Cross(flow, transform.forward).magnitude;

            Vector3 widen = Vector3.Cross(flow, transform.forward) * (Unstable * lost * q);
            Vector3 asym = transform.TransformDirection(_asym) * (lost * q * (0.25f + sinAoA));

            float decay = Mathf.Exp(-dtf / _gustTau);
            _gustState = _gustState * decay + UnityEngine.Random.insideUnitSphere * (Mathf.Sqrt(1f - decay * decay) * 1.7f);
            Vector3 gust = _gustState * (_gust * lost * q * (0.4f + sinAoA));

            float qd = Mathf.Sqrt(q);
            Vector3 w = transform.InverseTransformDirection(_m.rb.angularVelocity);
            Vector3 damp = transform.TransformDirection(new Vector3(-w.x * Damp, -w.y * Damp, -w.z * RollDamp) * (qd * lost));

            _m.rb.AddTorque(Vector3.ClampMagnitude(widen + asym + gust, MaxMoment) + damp, ForceMode.Acceleration);
        }

        private void Update()
        {
            if (_m == null || _m.disabled) { enabled = false; return; }
            float t = Time.time - _t0;
            if (_fx != null) _fx.SpentBoost = Smooth(0f, BoostFullS, t);
        }

        private static float Smooth(float lo, float hi, float x) =>
            Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((x - lo) / (hi - lo)));
    }
}
