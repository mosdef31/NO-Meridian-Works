using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace MeridianWorks
{

    internal static class MountFitProbe
    {
        private static readonly HashSet<string> _reported = new HashSet<string>();

        internal static void Report(Hardpoint? hardpoint, WeaponMount? mount, GameObject? spawned)
        {
            if (!Plugin.Diagnostics) return;
            if (mount == null || spawned == null) return;
            if (!PluginInfo.IsOurMountKey(mount.jsonKey)) return;

            string where = hardpoint?.transform != null
                ? hardpoint.transform.root.name
                : "unknown airframe";
            if (!_reported.Add(where + "|" + mount.jsonKey)) return;

            try
            {
                Measure(where, mount, spawned);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[Meridian] Mount fit probe failed on {mount.jsonKey}: {ex.Message}");
            }
        }

        private static void Measure(string where, WeaponMount mount, GameObject spawned)
        {
            Transform root = spawned.transform;

            var rounds = spawned.GetComponentsInChildren<MountedMissile>(true);
            if (rounds.Length == 0)
            {
                Plugin.Diag($"[Meridian] FIT {where} {mount.jsonKey}: no rounds on this mount.");
                return;
            }

            var structure = new List<Renderer>();
            AddRenderers(root, "Meridian_BorrowedPylon", structure);
            AddRenderers(root, RackBorrow.ContainerName, structure);

            var sb = new StringBuilder();
            sb.Append($"[Meridian] FIT {where} {mount.jsonKey}: {rounds.Length} round(s).");

            var boxes = new List<Bounds>();
            foreach (MountedMissile r in rounds)
            {
                if (LocalBounds(root, Renderers(r.transform), out Bounds b)) boxes.Add(b);
            }

            if (boxes.Count == 0)
            {
                Plugin.Diag(sb.Append(" None of them reported bounds.").ToString());
                return;
            }

            Bounds first = boxes[0];
            sb.Append($" Round box {first.size.x:0.000} wide x {first.size.y:0.000} tall x " +
                      $"{first.size.z:0.000} long, centre y={first.center.y:0.000}.");

            if (boxes.Count > 1)
            {
                boxes.Sort((a, b) => a.center.x.CompareTo(b.center.x));
                var gaps = new List<string>();
                for (int i = 1; i < boxes.Count; i++)
                {
                    float gap = (boxes[i].center.x - boxes[i].extents.x)
                              - (boxes[i - 1].center.x + boxes[i - 1].extents.x);
                    gaps.Add($"{gap:0.000}");
                }
                sb.Append($" Lateral gaps between rounds: {string.Join(", ", gaps)} m" +
                          " (negative means they intersect).");

                float spreadY = 0f;
                for (int i = 1; i < boxes.Count; i++)
                    spreadY = Mathf.Max(spreadY, Mathf.Abs(boxes[i].center.y - boxes[0].center.y));
                sb.Append($" Height spread across the stations: {spreadY:0.000} m.");
            }

            if (!LocalBounds(root, structure, out Bounds s))
            {

                sb.Append(mount.jsonKey.Contains("internal")
                    ? " No borrowed structure, which is correct for a bay mount."
                    : " NO BORROWED STRUCTURE on this mount, and it is not a bay mount," +
                      " so the rounds hang from nothing measurable.");
                Plugin.Diag(sb.ToString());
                return;
            }

            sb.Append($" Structure box {s.size.x:0.000} wide x {s.size.y:0.000} tall x " +
                      $"{s.size.z:0.000} long, bottom y={(s.center.y - s.extents.y):0.000}.");

            float roundTop = float.MinValue;
            foreach (Bounds b in boxes) roundTop = Mathf.Max(roundTop, b.center.y + b.extents.y);
            sb.Append($" Vertical gap structure-bottom to round-top: " +
                      $"{(s.center.y - s.extents.y) - roundTop:0.000} m.");

            float roundsWidth = 0f;
            float lo = float.MaxValue, hi = float.MinValue;
            foreach (Bounds b in boxes)
            {
                lo = Mathf.Min(lo, b.center.x - b.extents.x);
                hi = Mathf.Max(hi, b.center.x + b.extents.x);
            }
            roundsWidth = hi - lo;

            sb.Append($" Structure spans {s.size.x:0.000} m across against {roundsWidth:0.000} m " +
                      $"of rounds ({(s.size.x - roundsWidth):0.000} m of margin).");

            if (boxes.Count > 1)
            {
                float spacing = (boxes[boxes.Count - 1].center.x - boxes[0].center.x)
                                / (boxes.Count - 1);
                float span = first.size.x;
                sb.Append($" Station spacing {spacing:0.000} m against a {span:0.000} m fin span" +
                          $" ({(spacing - span):0.000} m of fin clearance).");

                if (spacing < span * 1.15f)
                    sb.Append(" THE RACK IS SPACED FOR A NARROWER ROUND THAN THIS ONE.");
            }

            Plugin.Diag(sb.ToString());
        }

        private static void AddRenderers(Transform root, string containerName, List<Renderer> into)
        {
            Transform? c = root.Find(containerName);
            if (c != null) into.AddRange(c.GetComponentsInChildren<Renderer>(true));
        }

        private static List<Renderer> Renderers(Transform t) =>
            new List<Renderer>(t.GetComponentsInChildren<Renderer>(true));

        private static bool LocalBounds(Transform root, List<Renderer> renderers, out Bounds bounds)
        {
            bounds = default;
            bool any = false;

            foreach (Renderer r in renderers)
            {
                if (r == null) continue;

                Mesh? mesh = (r as MeshRenderer) != null
                    ? r.GetComponent<MeshFilter>()?.sharedMesh
                    : (r as SkinnedMeshRenderer)?.sharedMesh;
                if (mesh == null) continue;

                Bounds local = mesh.bounds;
                Vector3 c = local.center, e = local.extents;

                for (int i = 0; i < 8; i++)
                {
                    var corner = new Vector3(
                        c.x + ((i & 1) == 0 ? -e.x : e.x),
                        c.y + ((i & 2) == 0 ? -e.y : e.y),
                        c.z + ((i & 4) == 0 ? -e.z : e.z));

                    Vector3 p = root.InverseTransformPoint(r.transform.TransformPoint(corner));

                    if (!any) { bounds = new Bounds(p, Vector3.zero); any = true; }
                    else bounds.Encapsulate(p);
                }
            }

            return any;
        }
    }
}
