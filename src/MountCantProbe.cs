using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace MeridianWorks
{

    internal static class MountCantProbe
    {
        private static readonly HashSet<string> _reported = new HashSet<string>();

        private const float QuadrantFraction = 0.45f;

        internal const float CleanGap = 0.005f;

        internal const float MaxSkinLift = 0.10f;

        internal const float BlindLift = 0.05f;

        internal const float PlausibleStandoff = 0.10f;

        internal static void Report(Hardpoint? hardpoint, WeaponMount? mount, GameObject? spawned)
        {
            if (!Plugin.Diagnostics) return;
            if (mount == null || spawned == null) return;
            if (!PluginInfo.IsOurMountKey(mount.jsonKey)) return;

            string where = hardpoint != null && hardpoint.transform != null
                ? hardpoint.transform.root.name
                : "unknown airframe";
            if (!_reported.Add(where + "|" + mount.jsonKey)) return;

            try
            {
                Measure(where, hardpoint, mount, spawned);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning(
                    "[Meridian] Mount cant probe failed on " + mount.jsonKey + ": " + ex.Message);
            }
        }

        private static void Measure(string where, Hardpoint? hardpoint, WeaponMount mount,
                                    GameObject spawned)
        {
            Transform root = spawned.transform;

            var ours = new List<Renderer>();
            Collect(root, "Meridian_BorrowedPylon", ours);
            Collect(root, RackBorrow.ContainerName, ours);
            bool borrowed = ours.Count > 0;
            if (!borrowed)
            {

                foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
                    if (r.GetComponentInParent<MountedMissile>() == null) ours.Add(r);
            }

            if (ours.Count == 0)
            {
                Plugin.Diag("[Meridian] CANT " + where + " " + mount.jsonKey +
                            ": no structure to measure.");
                return;
            }

            Renderer? stub = PylonBorrow.DrawnStub(hardpoint);
            bool stubDrawn = stub != null;

            Vector2 min, max;
            if (!Footprint(root, ours, out min, out max))
            {
                Plugin.Diag("[Meridian] CANT " + where + " " + mount.jsonKey +
                            ": structure reported no bounds.");
                return;
            }

            float cz = (min.y + max.y) * 0.5f, hz = (max.y - min.y) * QuadrantFraction;
            float cx = (min.x + max.x) * 0.5f, hx = (max.x - min.x) * QuadrantFraction;

            float ourFore = TopIn(root, ours, cx - hx, cx + hx, cz, cz + hz);
            float ourAft = TopIn(root, ours, cx - hx, cx + hx, cz - hz, cz);
            float ourLeft = TopIn(root, ours, cx - hx, cx, cz - hz, cz + hz);
            float ourRight = TopIn(root, ours, cx, cx + hx, cz - hz, cz + hz);

            var sb = new StringBuilder();
            sb.Append("[Meridian] CANT ").Append(where).Append(" ").Append(mount.jsonKey)
              .Append(": ").Append(borrowed ? "borrowed structure" : "authored structure")
              .Append(", footprint ").Append((max.x - min.x).ToString("0.000"))
              .Append(" x ").Append((max.y - min.y).ToString("0.000")).Append(" m. ");

            if (!stubDrawn)
            {

                Transform? air = hardpoint != null ? hardpoint.transform.root : null;

                float ourTop = Mathf.Max(Mathf.Max(ourFore, ourAft), Mathf.Max(ourLeft, ourRight));
                float floorY = ourTop - CleanGap;

                float skinFore = SkinAbove(root, air, cx - hx, cx + hx, cz, cz + hz, floorY);
                float skinAft = SkinAbove(root, air, cx - hx, cx + hx, cz - hz, cz, floorY);
                float skinLeft = SkinAbove(root, air, cx - hx, cx, cz - hz, cz + hz, floorY);
                float skinRight = SkinAbove(root, air, cx, cx + hx, cz - hz, cz + hz, floorY);

                bool haveSkin = !float.IsPositiveInfinity(skinFore)
                             && !float.IsPositiveInfinity(skinAft)
                             && !float.IsPositiveInfinity(skinLeft)
                             && !float.IsPositiveInfinity(skinRight);

                if (!haveSkin)
                {

                    sb.Append("No stub is drawn and no airframe surface sits above this mount " +
                              "in all four quadrants, so THE JOINT WAS NOT MEASURED. This is " +
                              "not a pass. Quadrants that found a surface: ")
                      .Append(float.IsPositiveInfinity(skinFore) ? "" : "fore ")
                      .Append(float.IsPositiveInfinity(skinAft) ? "" : "aft ")
                      .Append(float.IsPositiveInfinity(skinLeft) ? "" : "left ")
                      .Append(float.IsPositiveInfinity(skinRight) ? "" : "right ")
                      .Append("- the mount may simply hang in free air here.");
                    Plugin.Diag(sb.ToString());
                    return;
                }

                sb.Append("No stub is drawn, so the datum is the airframe skin above the mount. ");
                Verdict(sb, skinFore - ourFore, skinAft - ourAft,
                        skinLeft - ourLeft, skinRight - ourRight, hz * 2f, hx * 2f);
                Plugin.Diag(sb.ToString());
                return;
            }

            var stubOnly = new List<Renderer> { stub! };
            float stubFore = BottomIn(root, stubOnly, cx - hx, cx + hx, cz, cz + hz);
            float stubAft = BottomIn(root, stubOnly, cx - hx, cx + hx, cz - hz, cz);
            float stubLeft = BottomIn(root, stubOnly, cx - hx, cx, cz - hz, cz + hz);
            float stubRight = BottomIn(root, stubOnly, cx, cx + hx, cz - hz, cz + hz);

            sb.Append("stub '").Append(stub!.name).Append("'. ");
            Verdict(sb, stubFore - ourFore, stubAft - ourAft,
                    stubLeft - ourLeft, stubRight - ourRight, hz * 2f, hx * 2f);
            Plugin.Diag(sb.ToString());
        }

        private static void Verdict(StringBuilder sb, float fore, float aft, float left,
                                    float right, float dz, float dx)
        {
            float pitch = dz > 1e-4f ? Mathf.Atan2(aft - fore, dz) * Mathf.Rad2Deg : 0f;
            float roll = dx > 1e-4f ? Mathf.Atan2(right - left, dx) * Mathf.Rad2Deg : 0f;
            float mean = (fore + aft + left + right) * 0.25f;
            float spread = Mathf.Max(Mathf.Max(fore, aft), Mathf.Max(left, right)) -
                           Mathf.Min(Mathf.Min(fore, aft), Mathf.Min(left, right));

            sb.Append("Gap fore=").Append(fore.ToString("0.0000"))
              .Append(" aft=").Append(aft.ToString("0.0000"))
              .Append(" left=").Append(left.ToString("0.0000"))
              .Append(" right=").Append(right.ToString("0.0000"))
              .Append(" m; mean ").Append(mean.ToString("0.0000"))
              .Append(", spread ").Append(spread.ToString("0.0000")).Append(". ");
            sb.Append("Wedge: pitch ").Append(pitch.ToString("0.00"))
              .Append(" deg, roll ").Append(roll.ToString("0.00")).Append(" deg. ");

            bool wedged = spread > CleanGap;
            bool gapped = Mathf.Abs(mean) > CleanGap;

            if (!wedged && !gapped)
                sb.Append("VERDICT clean - the joint butts flat.");
            else if (wedged && gapped)
                sb.Append("VERDICT WEDGE ON A GAP - out of plane AND standing off, so a " +
                          "rotation alone will not close it.");
            else if (wedged)
                sb.Append("VERDICT WEDGE - the faces are out of plane. This is the angle fault.");
            else if (mean > 0f)
                sb.Append("VERDICT UNIFORM GAP - parallel but standing off. NOT an angle " +
                          "fault; no rotation will close it.");
            else
                sb.Append("VERDICT UNIFORM OVERLAP - parallel and buried. Intended on the " +
                          "authored mounts.");
        }

        private static void Collect(Transform root, string containerName, List<Renderer> into)
        {
            Transform? c = root.Find(containerName);
            if (c == null) return;
            into.AddRange(c.GetComponentsInChildren<Renderer>(true));
        }

        internal static bool SkinGap(Hardpoint? hardpoint, Transform root, List<Renderer> ours,
                                     out float minGap, out int quadrants)
        {
            minGap = 0f;
            quadrants = 0;
            if (hardpoint == null || hardpoint.transform == null) return false;
            if (root == null || ours == null || ours.Count == 0) return false;
            if (!Footprint(root, ours, out Vector2 min, out Vector2 max)) return false;

            float cz = (min.y + max.y) * 0.5f, hz = (max.y - min.y) * QuadrantFraction;
            float cx = (min.x + max.x) * 0.5f, hx = (max.x - min.x) * QuadrantFraction;

            float ourFore = TopIn(root, ours, cx - hx, cx + hx, cz, cz + hz);
            float ourAft = TopIn(root, ours, cx - hx, cx + hx, cz - hz, cz);
            float ourLeft = TopIn(root, ours, cx - hx, cx, cz - hz, cz + hz);
            float ourRight = TopIn(root, ours, cx, cx + hx, cz - hz, cz + hz);

            Transform air = hardpoint.transform.root;

            float ourTop = Mathf.Max(Mathf.Max(ourFore, ourAft), Mathf.Max(ourLeft, ourRight));
            float floorY = ourTop - CleanGap;

            float best = float.PositiveInfinity;
            Consider(root, air, cx - hx, cx + hx, cz, cz + hz, floorY, ourFore, ref best, ref quadrants);
            Consider(root, air, cx - hx, cx + hx, cz - hz, cz, floorY, ourAft, ref best, ref quadrants);
            Consider(root, air, cx - hx, cx, cz - hz, cz + hz, floorY, ourLeft, ref best, ref quadrants);
            Consider(root, air, cx, cx + hx, cz - hz, cz + hz, floorY, ourRight, ref best, ref quadrants);

            if (quadrants < 2 || float.IsPositiveInfinity(best)) return false;
            minGap = best;
            return true;
        }

        private static void Consider(Transform root, Transform? air,
                                     float x0, float x1, float z0, float z1,
                                     float floorY, float ourTopHere,
                                     ref float best, ref int quadrants)
        {
            float skinY = SkinAbove(root, air, x0, x1, z0, z1, floorY);
            if (float.IsPositiveInfinity(skinY)) return;
            quadrants++;
            float gap = skinY - ourTopHere;
            if (gap < best) best = gap;
        }

        private static bool Footprint(Transform root, List<Renderer> renderers,
                                      out Vector2 min, out Vector2 max)
        {
            min = new Vector2(float.MaxValue, float.MaxValue);
            max = new Vector2(float.MinValue, float.MinValue);
            bool any = false;
            foreach (Vector3 p in Corners(root, renderers))
            {
                any = true;
                min = new Vector2(Mathf.Min(min.x, p.x), Mathf.Min(min.y, p.z));
                max = new Vector2(Mathf.Max(max.x, p.x), Mathf.Max(max.y, p.z));
            }
            return any;
        }

        private static float TopIn(Transform root, List<Renderer> renderers,
                                   float x0, float x1, float z0, float z1)
        {
            float best = float.MinValue;
            foreach (Vector3 p in Corners(root, renderers))
                if (p.x >= x0 && p.x <= x1 && p.z >= z0 && p.z <= z1 && p.y > best) best = p.y;
            return best == float.MinValue ? 0f : best;
        }

        private static float SkinAbove(Transform root, Transform? air,
                                       float x0, float x1, float z0, float z1, float floorY)
        {
            if (air == null) return float.PositiveInfinity;

            Vector3 up = root.TransformDirection(Vector3.up).normalized;
            float best = float.PositiveInfinity;

            for (int ix = 0; ix < 3; ix++)
            {
                for (int iz = 0; iz < 3; iz++)
                {
                    float x = Mathf.Lerp(x0, x1, ix * 0.5f);
                    float z = Mathf.Lerp(z0, z1, iz * 0.5f);
                    Vector3 from = root.TransformPoint(new Vector3(x, floorY, z));

                    RaycastHit[] hits = Physics.RaycastAll(
                        from, up, PlausibleStandoff, ~0, QueryTriggerInteraction.Ignore);

                    foreach (RaycastHit hit in hits)
                    {
                        Transform t = hit.collider.transform;
                        if (t.IsChildOf(root)) continue;
                        if (t.GetComponentInParent<MountedMissile>() != null) continue;
                        if (t.root != air) continue;

                        float y = root.InverseTransformPoint(hit.point).y;
                        if (y >= floorY && y < best) best = y;
                    }
                }
            }

            return best;
        }

        private static float BottomAbove(Transform root, List<Renderer> renderers,
                                         float x0, float x1, float z0, float z1, float floorY)
        {
            float best = float.PositiveInfinity;
            foreach (Vector3 p in Corners(root, renderers))
                if (p.x >= x0 && p.x <= x1 && p.z >= z0 && p.z <= z1 &&
                    p.y >= floorY && p.y < best) best = p.y;
            return best;
        }

        private static float BottomIn(Transform root, List<Renderer> renderers,
                                      float x0, float x1, float z0, float z1)
        {
            float best = float.MaxValue;
            foreach (Vector3 p in Corners(root, renderers))
                if (p.x >= x0 && p.x <= x1 && p.z >= z0 && p.z <= z1 && p.y < best) best = p.y;
            return best == float.MaxValue ? 0f : best;
        }

        private static IEnumerable<Vector3> Corners(Transform root, List<Renderer> renderers)
        {
            foreach (Renderer r in renderers)
            {
                Mesh? mesh = null;
                if (r is MeshRenderer && r.TryGetComponent(out MeshFilter mf) && mf != null)
                    mesh = mf.sharedMesh;
                else if (r is SkinnedMeshRenderer smr) mesh = smr.sharedMesh;
                if (mesh == null) continue;

                Bounds b = mesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var corner = new Vector3(
                        (i & 1) == 0 ? b.min.x : b.max.x,
                        (i & 2) == 0 ? b.min.y : b.max.y,
                        (i & 4) == 0 ? b.min.z : b.max.z);
                    yield return root.InverseTransformPoint(r.transform.TransformPoint(corner));
                }
            }
        }
    }
}
