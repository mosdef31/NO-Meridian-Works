using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace MeridianWorks
{
    internal sealed class FinHatchClose : MonoBehaviour
    {

        private static readonly string[] DoorNames = { "Fin6", "Fin7" };

        private static readonly FieldInfo? FFoldingFins =
            AccessTools.Field(typeof(Missile), "foldingFins");

        private static readonly FieldInfo? FFin =
            AccessTools.Field(typeof(Missile.FoldingFin), "fin");

        private static readonly FieldInfo? FFoldAngle =
            AccessTools.Field(typeof(Missile.FoldingFin), "foldAngle");

        private static readonly FieldInfo? FDeployAngle =
            AccessTools.Field(typeof(Missile.FoldingFin), "deployAngle");

        private static readonly FieldInfo? FDeploySpeed =
            AccessTools.Field(typeof(Missile.FoldingFin), "deploySpeed");

        private static readonly FieldInfo? FDeployedAmount =
            AccessTools.Field(typeof(Missile.FoldingFin), "deployedAmount");

        private sealed class Door
        {
            internal Transform Fin = null!;
            internal Vector3 FoldAngle;
            internal Vector3 DeployAngle;
            internal float DeploySpeed = 1f;
            internal object FinRaw = null!;
            internal bool OpenLatched;
            internal float CloseAmount;
            internal bool Done;
        }

        private Missile? _missile;
        private Door[] _doors = Array.Empty<Door>();

        internal static void Attach(Missile missile, string? jsonKey)
        {
            if (missile == null || !PluginInfo.IsOurMissileKey(jsonKey)) return;
            if (missile.GetComponent<FinHatchClose>() != null) return;
            if (FFoldingFins?.GetValue(missile) is not Array fins || fins.Length == 0) return;

            var doors = new System.Collections.Generic.List<Door>();
            foreach (object? entry in fins)
            {
                if (entry == null) continue;
                if (FFin?.GetValue(entry) is not Transform t) continue;
                if (Array.IndexOf(DoorNames, t.name) < 0) continue;

                doors.Add(new Door
                {
                    Fin = t,
                    FinRaw = entry,
                    FoldAngle = FFoldAngle?.GetValue(entry) as Vector3? ?? Vector3.zero,
                    DeployAngle = FDeployAngle?.GetValue(entry) as Vector3? ?? Vector3.zero,
                    DeploySpeed = FDeploySpeed?.GetValue(entry) as float? ?? 1f,
                });
            }

            if (doors.Count == 0) return;

            var c = missile.gameObject.AddComponent<FinHatchClose>();
            c._missile = missile;
            c._doors = doors.ToArray();
        }

        private void FixedUpdate()
        {
            Missile m = _missile!;
            if (m == null || m.disabled) { enabled = false; return; }

            bool anyPending = false;
            foreach (Door d in _doors)
            {
                if (d.Done) continue;
                anyPending = true;

                if (!d.OpenLatched)
                {

                    float amount = FDeployedAmount?.GetValue(d.FinRaw) as float? ?? 0f;
                    if (amount < 1f) continue;

                    d.OpenLatched = true;
                    d.CloseAmount = 1f;
                }

                d.CloseAmount = Mathf.Max(0f, d.CloseAmount - Time.fixedDeltaTime * d.DeploySpeed);
                d.Fin.localEulerAngles = Vector3.Lerp(d.FoldAngle, d.DeployAngle, d.CloseAmount);

                if (d.CloseAmount == 0f) d.Done = true;
            }

            if (!anyPending) enabled = false;
        }
    }

    [HarmonyPatch(typeof(Missile), "OnStartClient")]
    internal static class Missile_OnStartClient_FinHatchClosePatch
    {
        [HarmonyPostfix]
        private static void Postfix(Missile __instance)
        {
            try
            {
                if (__instance.definition is not MissileDefinition def) return;
                FinHatchClose.Attach(__instance, def.jsonKey);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[Meridian] Fin hatch close attach failed: {ex.Message}");
            }
        }
    }
}
