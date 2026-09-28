using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace MeridianWorks
{

    [HarmonyPatch(typeof(VLSBooster), "Burnout")]
    internal static class HSM160Separation
    {
        private static readonly FieldInfo? FMissile = AccessTools.Field(typeof(VLSBooster), "missile");
        private static readonly FieldInfo? FDryMass = AccessTools.Field(typeof(VLSBooster), "dryMass");

        private static bool _logged;

        private static void Postfix(VLSBooster __instance)
        {
            try
            {
                if (FMissile?.GetValue(__instance) is not Missile m) return;
                if ((m.definition as MissileDefinition)?.jsonKey is not string key || !HSM160Rounds.Ours.Contains(key))
                    return;
                if (FDryMass?.GetValue(__instance) is not float dry || dry <= 0f) return;
                if (m.rb == null) return;

                if (m.boosterIsAttached) return;

                m.rb.mass = Math.Max(1f, m.rb.mass - dry);
                Recentre(m);

                if (!_logged)
                {
                    _logged = true;
                    Plugin.Log.LogInfo($"[Meridian] {key}: booster ({dry} kg) dropped at separation, stage 2 now {m.rb.mass} kg.");
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("[Meridian] HSM-160 separation mass drop failed: " + e.Message);
            }
        }

        private const float Stage2CentreZ = 1.516f;
        private const float Stage2Length = 1.759f;
        private static bool _saidBody;

        private static void Recentre(Missile m)
        {
            try
            {
                Transform root = m.transform;
                Vector3 shift = root.TransformVector(new Vector3(0f, 0f, Stage2CentreZ));

                var children = new Transform[root.childCount];
                for (int i = 0; i < children.Length; i++) children[i] = root.GetChild(i);
                foreach (Transform c in children)
                    c.localPosition -= new Vector3(0f, 0f, Stage2CentreZ);

                if (root.GetComponent<MeshFilter>() is MeshFilter mf && mf.sharedMesh != null
                    && root.GetComponent<MeshRenderer>() is MeshRenderer mr && mr.enabled)
                {
                    var body = new GameObject("HSM160_Stage2Body");
                    body.layer = root.gameObject.layer;
                    body.transform.SetParent(root, false);
                    body.transform.localPosition = new Vector3(0f, 0f, -Stage2CentreZ);
                    body.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
                    var r = body.AddComponent<MeshRenderer>();
                    r.sharedMaterials = mr.sharedMaterials;
                    r.shadowCastingMode = mr.shadowCastingMode;
                    r.receiveShadows = mr.receiveShadows;
                    mr.enabled = false;
                    if (!_saidBody)
                    {
                        _saidBody = true;
                        Plugin.Log.LogInfo("[Meridian] HSM-160 stage 2 body moved onto a child with the pivot shift.");
                    }
                }

                root.position += shift;
                m.rb.position += shift;

                if (root.GetComponent<CapsuleCollider>() is CapsuleCollider cc)
                {
                    cc.center = Vector3.zero;
                    cc.height = Stage2Length;
                }
                m.rb.centerOfMass = Vector3.zero;
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("[Meridian] HSM-160 stage 2 re-root failed, pivot stays on the whole round: " + e.Message);
            }
        }
    }
}
