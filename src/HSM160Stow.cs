using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace MeridianWorks
{

    internal static class HSM160Stow
    {
        private sealed class Layout
        {
            public int Count;
            public float Scale, Ring, Centre, HalfLength, DartR;
        }

        private static readonly Layout C = new Layout { Count = 6, Scale = 0.69f, Ring = 0.108f, Centre = 1.61f, HalfLength = 0.414f, DartR = 0.048f };
        private static readonly Layout M = new Layout { Count = 4, Scale = 0.74f, Ring = 0.111f, Centre = 1.56f, HalfLength = 0.370f, DartR = 0.051f };

        private static readonly FieldInfo? FSubs = AccessTools.Field(typeof(SubmunitionDispenser), "submunitions");
        private const string Marker = "MeridianStow";
        private static bool _logged;

        internal static float Scale(string carrierKey) =>
            carrierKey == HSM160Submunition.NuclearDispenserKey ? M.Scale
            : carrierKey == HSM160Submunition.DispenserKey ? C.Scale : 1f;

        private static Layout? For(Missile m)
        {
            string? key = (m.definition as MissileDefinition)?.jsonKey;
            return key == HSM160Submunition.DispenserKey ? C : key == HSM160Submunition.NuclearDispenserKey ? M : null;
        }

        internal static void Apply(Missile m, bool dress)
        {
            if (m == null || For(m) is not Layout L) return;
            SubmunitionDispenser? d = m.GetComponentInChildren<SubmunitionDispenser>(true);
            if (d == null || FSubs?.GetValue(d) is not GameObject[] subs || subs.Length == 0) return;

            for (int i = 0; i < subs.Length; i++)
            {
                if (subs[i] == null) continue;
                float a = i * 2f * Mathf.PI / subs.Length;
                subs[i].transform.localPosition = new Vector3(Mathf.Cos(a) * L.Ring, Mathf.Sin(a) * L.Ring, L.Centre);
                subs[i].transform.localRotation = Quaternion.identity;
            }

            if (!dress) return;
            Transform holder = subs[0].transform.parent;
            if (holder == null || holder.Find(Marker) != null) return;
            new GameObject(Marker).transform.SetParent(holder, false);

            GameObject? prefab = HSM160Submunition.Prefab(L == M);
            int seated = 0;
            if (prefab != null)
            {
                foreach (GameObject st in subs)
                {
                    if (st == null) continue;
                    GameObject dart = UnityEngine.Object.Instantiate(prefab, st.transform, false);
                    dart.transform.localPosition = Vector3.zero;
                    dart.transform.localRotation = Quaternion.identity;
                    dart.transform.localScale = Vector3.one * L.Scale;
                    Transform? folded = dart.transform.Find("Folded");
                    Transform? open = dart.transform.Find("Open");
                    if (folded != null) folded.gameObject.SetActive(true);
                    if (open != null) open.gameObject.SetActive(false);
                    foreach (Collider c in dart.GetComponentsInChildren<Collider>(true)) UnityEngine.Object.Destroy(c);
                    seated++;
                }
            }

            if (!_logged)
            {
                _logged = true;
                Plugin.Log.LogInfo($"[Meridian] HSM-160 bay stowed {seated} dart(s) on an even ring "
                    + $"(r {L.Ring}, scale {L.Scale}).");
            }
        }

    }

    [HarmonyPatch(typeof(Missile), "OnStartServer")]
    internal static class HSM160StowServer
    {
        [HarmonyPostfix]
        private static void Postfix(Missile __instance)
        {
            try { HSM160Stow.Apply(__instance, dress: false); }
            catch (Exception e) { Plugin.Log.LogWarning("[Meridian] HSM-160 stow (server) failed: " + e.Message); }
        }
    }

    [HarmonyPatch(typeof(Missile), "OnStartClient")]
    internal static class HSM160StowClient
    {
        [HarmonyPostfix]
        private static void Postfix(Missile __instance)
        {
            try { HSM160Stow.Apply(__instance, dress: true); }
            catch (Exception e) { Plugin.Log.LogWarning("[Meridian] HSM-160 stow failed, stock stations kept: " + e.Message); }
        }
    }
}
