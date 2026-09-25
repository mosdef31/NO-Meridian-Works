using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using NuclearOption.Networking;
using UnityEngine;

namespace MeridianWorks
{

    internal sealed class KalibrJammer : MonoBehaviour
    {

        private const string Key = "MeridianKalibrEW";

        private const float BiteRangeM = 30000f;

        private const float ReferenceTolerance = 0.60f;

        private const float MaxRangeM =
            BiteRangeM / (1f - (ReferenceTolerance * ReferenceTolerance / 5f) / JamAtZero);

        private const float JamAtZero = 0.25f;

        private const float TickSeconds = 0.2f;

        private const float LostAfterSeconds = 1.5f;

        private Missile? _missile;
        private float _lastTick;

        private static FieldInfo? _seekerTargetField;
        private static bool _seekerTargetFieldResolved;

        private static Unit? SeekerTarget(Missile m)
        {
            try
            {
                var seeker = m.GetComponent<MissileSeeker>();
                if (seeker == null) return null;

                if (!_seekerTargetFieldResolved)
                {
                    _seekerTargetFieldResolved = true;
                    _seekerTargetField = typeof(MissileSeeker).GetField(
                        "targetUnit",
                        BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                    if (_seekerTargetField == null)
                    {
                        Plugin.Log.LogWarning(
                            "[Meridian] MissileSeeker.targetUnit not found - the AGM-102E "
                            + "cannot jam. The engine's seeker layout changed; see KalibrJammer.cs.");
                    }
                }

                return _seekerTargetField?.GetValue(seeker) as Unit;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[Meridian] Kalibr jam target read failed: {ex.Message}");
                return null;
            }
        }

        private bool _jamming;
        private bool _saidNoTarget;
        private float _lastJamAt;
        private int _ticksJammed;
        private float _peakAmount;

        internal static void Attach(Missile missile, string? jsonKey)
        {
            if (missile == null) return;

            if (!PluginInfo.IsRound(jsonKey, Key)) return;

            if (missile.GetComponent<KalibrJammer>() != null) return;

            var j = missile.gameObject.AddComponent<KalibrJammer>();
            j._missile = missile;

            Plugin.Diag($"[Meridian] JAM {jsonKey}: jammer attached.");
        }

        private void FixedUpdate()
        {
            Missile m = _missile!;
            if (m == null || m.disabled) { enabled = false; return; }

            var net = NetworkManagerNuclearOption.i;
            if (net == null || net.Server == null || !net.Server.Active) return;

            if (Time.timeSinceLevelLoad - _lastTick < TickSeconds) return;
            _lastTick = Time.timeSinceLevelLoad;

            Unit? target = SeekerTarget(m);
            if (target != null && (target.disabled || target == m.owner)) target = null;
            if (target == null && !_saidNoTarget && m.timeSinceSpawn > 5f)
            {

                _saidNoTarget = true;
                Plugin.Diag(
                    $"[Meridian] JAM {Key}: no seeker target {m.timeSinceSpawn:0.0} s after "
                    + "launch. The jammer IS attached; it will still jam corridor radars.");
            }

            Vector3 here = m.transform.position;
            Vector3 ahead = (m.rb != null && m.rb.velocity.sqrMagnitude > 1f)
                ? m.rb.velocity.normalized : m.transform.forward;
            FactionHQ? ourHq = m.owner != null ? m.owner.NetworkHQ : null;

            _victims.Clear();
            foreach (Unit u in UnitRegistry.allUnits)
            {
                if (u == null || u.disabled || u == m || u == target) continue;
                if (!(u.radar is Radar)) continue;
                if (ourHq == null || u.NetworkHQ == null || u.NetworkHQ == ourHq) continue;
                Vector3 to = u.transform.position - here;
                float d = to.magnitude;
                if (d > MaxRangeM || d < 1f) continue;
                if (Vector3.Angle(ahead, to) > CorridorHalfAngle) continue;
                _victims.Add((u, d));
            }
            _victims.Sort((x, y) => x.dist.CompareTo(y.dist));
            if (_victims.Count > CorridorMaxRadars) _victims.RemoveRange(CorridorMaxRadars, _victims.Count - CorridorMaxRadars);

            float targetDist = target != null ? (target.transform.position - here).magnitude : float.MaxValue;
            if (!_emitting)
            {
                bool worth = targetDist <= BiteRangeM || (_victims.Count > 0 && _victims[0].dist <= BiteRangeM);
                if (!worth) return;
                _emitting = true;
                Announce(m, "AGM-102E jammer active", playsound: true);
                Plugin.Diag($"[Meridian] JAM {Key}: EMCON ended at {m.timeSinceSpawn:0.0} s, "
                    + $"target {(target != null ? $"{targetDist / 1000f:0.0} km" : "none")}, "
                    + $"{_victims.Count} corridor radar(s) in the cone.");
            }

            Unit? pick = null;
            float pickDist = 0f;
            bool ownOk = target != null && targetDist <= MaxRangeM && Visible(here, target);
            if (ownOk && !HeldByOther(target!)) { pick = target; pickDist = targetDist; }
            if (pick == null)
            {
                foreach (var (u, d) in _victims)
                {
                    if (HeldByOther(u) || !Visible(here, u)) continue;
                    pick = u; pickDist = d; break;
                }
            }
            if (pick == null && ownOk) { pick = target; pickDist = targetDist; }
            if (pick == null)
            {
                foreach (var (u, d) in _victims)
                {
                    if (!Visible(here, u)) continue;
                    pick = u; pickDist = d; break;
                }
            }

            Claim(pick);
            if (pick == null || !JamOne(m, pick, JamAtZero * (1f - pickDist / MaxRangeM))) { Lapsed(); return; }

            _lastJamAt = Time.timeSinceLevelLoad;
            _ticksJammed++;
            if (!_jamming || pick != _lastPick)
            {
                Announce(m, $"AGM-102E jamming {NameOf(pick)}", playsound: !_jamming);
                _jamming = true;
                _lastPick = pick;
                Plugin.Diag($"[Meridian] JAM {Key}: jamming '{NameOf(pick)}' at {pickDist / 1000f:0.0} km, "
                    + $"{(pick == target ? "its own target" : "a corridor radar")}, "
                    + $"own target {(target != null ? $"{targetDist / 1000f:0.0} km{(HeldByOther(target) ? " (held by another round)" : "")}" : "none")}.");
            }
        }

        private static bool Visible(Vector3 here, Unit u) =>
            !Physics.Linecast(here, u.transform.position, out _, PhysicsLayers.StaticsMask);

        private static readonly Dictionary<Unit, KalibrJammer> _claims = new Dictionary<Unit, KalibrJammer>();

        private Unit? _claimed;
        private Unit? _lastPick;

        private bool HeldByOther(Unit u) =>
            _claims.TryGetValue(u, out KalibrJammer other) && other != null && other != this
            && other.isActiveAndEnabled && other._missile != null && !other._missile.disabled
            && Time.timeSinceLevelLoad - other._lastTick < 1f;

        private void Claim(Unit? u)
        {
            if (_claimed == u) { if (u != null) _claims[u] = this; return; }
            Release();
            _claimed = u;
            if (u != null && !HeldByOther(u)) _claims[u] = this;
        }

        private void Release()
        {
            if (_claimed != null && _claims.TryGetValue(_claimed, out KalibrJammer h) && h == this)
                _claims.Remove(_claimed);
            _claimed = null;
        }

        private void OnDisable() => Release();
        private void OnDestroy() => Release();

        private bool JamOne(Missile m, Unit u, float amount)
        {
            if (amount <= 0f) return false;
            u.Jam(new Unit.JamEventArgs { jamAmount = amount, jammingUnit = m });
            if (amount > _peakAmount) _peakAmount = amount;
            return true;
        }

        private const float CorridorHalfAngle = 60f;

        private const int CorridorMaxRadars = 6;

        private bool _emitting;
        private readonly List<(Unit u, float dist)> _victims = new List<(Unit u, float dist)>();

        private void Lapsed()
        {
            if (!_jamming) return;
            if (Time.timeSinceLevelLoad - _lastJamAt < LostAfterSeconds) return;

            _jamming = false;
            Missile m = _missile!;
            Announce(m, "AGM-102E jamming lost", playsound: false);
            Plugin.Diag(
                $"[Meridian] JAM MeridianKalibrEW_Missile: lapsed after {_ticksJammed} tick(s), "
                + $"peak amount {_peakAmount:0.000}.");
        }

        private static void Announce(Missile m, string message, bool playsound)
        {
            try
            {
                if (m == null || m.owner == null) return;
                FactionHQ hq = m.owner.NetworkHQ;
                if (hq == null) return;
                MissionMessages.ShowMessage(message, playsound, hq, sendToClients: true);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[Meridian] Kalibr jam cue failed: {ex.Message}");
            }
        }

        private static string NameOf(Unit u)
        {
            try { return string.IsNullOrEmpty(u.unitName) ? "target" : u.unitName; }
            catch { return "target"; }
        }
    }

    [HarmonyPatch(typeof(Missile), "OnStartClient")]
    internal static class Missile_OnStartClient_KalibrJammerPatch
    {
        [HarmonyPostfix]
        private static void Postfix(Missile __instance)
        {
            try
            {
                if (__instance.definition is not MissileDefinition def) return;
                KalibrJammer.Attach(__instance, def.jsonKey);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[Meridian] Kalibr jammer attach failed: {ex.Message}");
            }
        }
    }

    [HarmonyPatch(typeof(Missile), "OnStartServer")]
    internal static class Missile_OnStartServer_KalibrJammerPatch
    {
        [HarmonyPostfix]
        private static void Postfix(Missile __instance)
        {
            try
            {
                if (__instance.definition is not MissileDefinition def) return;
                KalibrJammer.Attach(__instance, def.jsonKey);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[Meridian] Kalibr jammer attach failed: {ex.Message}");
            }
        }
    }
}
