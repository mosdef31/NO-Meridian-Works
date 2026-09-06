using UnityEngine;

namespace Shared.Ballistics
{

    internal static class TerrainMasking
    {

        private static int Mask =>
            (int)PhysicsLayers.StaticsMask | (int)PhysicsLayers.ShipsMask;

        internal static bool IsHidden(Camera cam, Vector3 localPoint)
        {
            if (cam == null) return false;
            try
            {
                Vector3 from = cam.transform.position;
                Vector3 to = localPoint;

                Vector3 d = to - from;
                float len = d.magnitude;
                if (len < 1f) return false;
                to = from + d * ((len - 2f) / len);

                return Physics.Linecast(from, to, Mask);
            }
            catch
            {
                return false;
            }
        }
    }
}
