using System.Collections.Generic;
using UnityEngine;

namespace MeridianWorks
{

    internal static class EncyclopediaFit
    {
        private static bool _done;

        internal static void Apply(IEnumerable<MissileDefinition> defs)
        {
            if (_done) return;
            _done = true;
            int n = 0;
            foreach (MissileDefinition d in defs)
            {
                if (d == null || d.unitPrefab == null) continue;
                if (!Measure(d.unitPrefab.transform, out Bounds b)) continue;
                float span = Mathf.Max(b.size.x, b.size.y);
                float drop = Mathf.Max(0f, -b.min.y);
                d.width = Mathf.Max(d.width, span);
                d.height = Mathf.Max(d.height, b.size.y);
                Vector3 off = d.spawnOffset;
                off.y = Mathf.Max(off.y, drop);
                d.spawnOffset = off;
                n++;
                Plugin.Log.LogDebug($"[Meridian] Encyclopedia fit {d.jsonKey}: width {d.width:F2} m, "
                    + $"height {d.height:F2} m, lift {d.spawnOffset.y:F2} m.");
            }
            Plugin.Log.LogInfo($"[Meridian] Encyclopedia fit: {n} round(s) sized from their meshes, fins included.");
        }

        private static bool Measure(Transform root, out Bounds b)
        {
            b = default;
            bool any = false;
            Matrix4x4 toRoot = root.worldToLocalMatrix;

            foreach (MeshFilter mf in root.GetComponentsInChildren<MeshFilter>(true))
            {
                MeshRenderer? r = mf.GetComponent<MeshRenderer>();
                if (mf.sharedMesh == null || r == null || !r.enabled || !ActiveUnder(mf.transform, root)) continue;
                Matrix4x4 m = toRoot * mf.transform.localToWorldMatrix;

                if (mf.sharedMesh.isReadable)
                {
                    foreach (Vector3 v in mf.sharedMesh.vertices)
                    {
                        Vector3 p = m.MultiplyPoint3x4(v);
                        if (!any) { b = new Bounds(p, Vector3.zero); any = true; }
                        else b.Encapsulate(p);
                    }
                    continue;
                }
                Bounds mb = mf.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 c = mb.center + Vector3.Scale(mb.extents,
                        new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    Vector3 p = m.MultiplyPoint3x4(c);
                    if (!any) { b = new Bounds(p, Vector3.zero); any = true; }
                    else b.Encapsulate(p);
                }
            }
            return any;
        }

        private static bool ActiveUnder(Transform t, Transform root)
        {
            for (Transform? p = t; p != null; p = p.parent)
            {
                if (!p.gameObject.activeSelf) return false;
                if (p == root) return true;
            }
            return true;
        }
    }
}
