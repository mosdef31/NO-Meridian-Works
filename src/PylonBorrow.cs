using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace MeridianWorks
{

    internal static class PylonBorrow
    {
        private const string ContainerName = "Meridian_BorrowedPylon";

        private const string PreferredDonorWeapon = "AGM-68";

        private const float SafetyMargin = 0.06f;

        private static readonly HashSet<string> _logged = new HashSet<string>();

        internal static void Apply(Hardpoint hardpoint, WeaponMount mount, GameObject spawned)
        {
            if (mount == null || spawned == null) return;
            if (!PluginInfo.IsOurMountKey(mount.jsonKey)) return;

            if (mount.jsonKey.Contains("internal"))
            {
                SeatInBay(hardpoint, mount, spawned);
                return;
            }

            if (spawned.transform.Find(ContainerName) != null) return;

            var rounds = spawned.GetComponentsInChildren<MountedMissile>(true);
            if (rounds.Length == 0) return;

            PluginInfo.Weapon? weapon = PluginInfo.WeaponForMountKey(mount.jsonKey);
            if (weapon == null) return;
            float radius = weapon.BodyRadiusM;

            List<Renderer> existing = Structure(spawned.transform);
            bool borrowedRack = spawned.transform.Find(RackBorrow.ContainerName) != null;

            if (existing.Count > 0 && !borrowedRack)
            {
                if (_logged.Add(Where(hardpoint) + "|" + mount.jsonKey))
                    Plugin.Log.LogInfo(
                        $"[Meridian] {Where(hardpoint)} {mount.jsonKey}: the mounted prefab carries its own structure, " +
                        "so no pylon was borrowed and nothing was reseated.");
                return;
            }

            Donor? pick = ChooseDonor();
            if (pick == null)
            {
                if (_logged.Add(Where(hardpoint) + "|" + mount.jsonKey))
                    Plugin.Log.LogWarning(
                        $"[Meridian] {Where(hardpoint)} {mount.jsonKey}: no stock single mount with pylon geometry was found, " +
                        "so this mount keeps the aircraft's generic stub.");
                return;
            }

            Donor donor = pick.Value;
            Transform root = spawned.transform;

            var container = new GameObject(ContainerName);
            container.transform.SetParent(root, false);

            int renderers = 0;
            foreach (Transform piece in donor.Structure)
            {
                GameObject clone = UnityEngine.Object.Instantiate(piece.gameObject, container.transform);
                clone.transform.localPosition = piece.localPosition;
                clone.transform.localRotation = piece.localRotation;
                clone.transform.localScale = piece.localScale;

                clone.SetActive(true);
                foreach (Renderer r in clone.GetComponentsInChildren<Renderer>(true))
                {
                    r.gameObject.SetActive(true);
                    r.enabled = true;
                    renderers++;
                }
            }

            string stubNote;
            if (Extent(root, PylonRenderers(container.transform), out _, out float pylonTop))
            {
                bool haveStub = StubBottom(hardpoint, root, out float stubBottom);
                float top = haveStub ? stubBottom : 0f;

                container.transform.localPosition += new Vector3(0f, top - pylonTop, 0f);

                stubNote = haveStub
                    ? $"hung from the aircraft's stub at y={stubBottom:0.000}"
                    : "no stub found, so hung from the hardpoint's own face at y=0.000";
            }
            else
            {
                stubNote = "the borrowed pylon reported no bounds, so it kept the donor's own height";
            }

            Transform? rack = borrowedRack ? root.Find(RackBorrow.ContainerName) : null;
            if (rack != null &&
                Extent(root, PylonRenderers(container.transform), out float pylonBottom, out _) &&
                Extent(root, existing, out _, out float rackTop))
            {
                rack.localPosition += new Vector3(0f, pylonBottom - rackTop, 0f);
            }

            List<Renderer> hangFrom = borrowedRack ? existing : PylonRenderers(container.transform);
            if (!Extent(root, hangFrom, out float hangBottom, out _))
            {
                if (_logged.Add(Where(hardpoint) + "|" + mount.jsonKey))
                    Plugin.Log.LogWarning(
                        $"[Meridian] {Where(hardpoint)} {mount.jsonKey}: the borrowed structure reported no bounds, so the " +
                        "rounds were left where they were.");
                return;
            }

            float seatY = hangBottom - radius + SafetyMargin;
            foreach (MountedMissile r in rounds)
            {
                Transform t = r.transform;
                Vector3 local = root.InverseTransformPoint(t.position);
                local.y = seatY;
                t.position = root.TransformPoint(local);
            }

            if (!_logged.Add(Where(hardpoint) + "|" + mount.jsonKey)) return;

            Plugin.Log.LogInfo(
                $"[Meridian] {Where(hardpoint)} {mount.jsonKey}: borrowed the pylon from '{donor.Key}' " +
                $"({renderers} renderer(s)), {stubNote}" +
                (rack != null ? ", with the borrowed rack slid under it" : "") +
                $". {rounds.Length} round(s) hang from y={hangBottom:0.000} at a body radius of " +
                $"{radius:0.000} m, tucked up by a {SafetyMargin:0.000} m margin, so their centrelines " +
                $"sit at y={seatY:0.000}.");
        }

        private static bool StubBottom(Hardpoint hardpoint, Transform root, out float bottom)
        {
            bottom = 0f;
            if (hardpoint == null) return false;

            if (hardpoint.Pylon != null)
            {
                bool drawn = hardpoint.Pylon.enabled &&
                             hardpoint.Pylon.gameObject.activeInHierarchy;

                if (drawn && Extent(root, new List<Renderer> { hardpoint.Pylon }, out bottom, out _))
                {
                    return true;
                }

                if (!drawn && _logged.Add(Where(hardpoint) + "|stub-hidden"))
                {
                    Plugin.Log.LogInfo(
                        $"[Meridian] {Where(hardpoint)}: this hardpoint carries a stub renderer " +
                        $"('{hardpoint.Pylon.name}') that is NOT being drawn " +
                        $"(enabled={hardpoint.Pylon.enabled}, " +
                        $"active={hardpoint.Pylon.gameObject.activeInHierarchy}), so it was ignored " +
                        "and the hardpoint's own face was used instead.");
                }
            }

            if (FPylonOptions?.GetValue(hardpoint) is not Array options) return false;

            foreach (object entry in options)
            {
                if (FEntryCargo?.GetValue(entry) is true) continue;
                if (FEntryMount?.GetValue(entry) is WeaponMount) continue;
                if (FEntryRenderer?.GetValue(entry) is not Renderer r) continue;

                return Extent(root, new List<Renderer> { r }, out bottom, out _);
            }

            return false;
        }

        private static readonly System.Reflection.FieldInfo? FPylonOptions =
            AccessTools.Field(typeof(Hardpoint), "pylonOptions");

        private static readonly System.Reflection.FieldInfo? FEntryMount =
            AccessTools.Field(AccessTools.Inner(typeof(Hardpoint), "HardpointPylon"), "mount");

        private static readonly System.Reflection.FieldInfo? FEntryCargo =
            AccessTools.Field(AccessTools.Inner(typeof(Hardpoint), "HardpointPylon"), "cargo");

        private static readonly System.Reflection.FieldInfo? FEntryRenderer =
            AccessTools.Field(AccessTools.Inner(typeof(Hardpoint), "HardpointPylon"), "renderer");

        private static void SeatInBay(Hardpoint hardpoint, WeaponMount mount, GameObject spawned)
        {

            const float Clearance = 0.05f;

            Transform root = spawned.transform;
            var rounds = spawned.GetComponentsInChildren<MountedMissile>(true);
            if (rounds.Length == 0) return;

            if (rounds.Length > 1)
            {
                if (_logged.Add(Where(hardpoint) + "|" + mount.jsonKey + "|multi"))
                    Plugin.Log.LogInfo(
                        $"[Meridian] {Where(hardpoint)} {mount.jsonKey}: {rounds.Length} round(s) left " +
                        "exactly where the aircraft's hardpoint puts them. Bay doors are shared " +
                        "between sets, so they cannot say which end of the bay this set belongs at.");
                return;
            }

            if (hardpoint?.bayDoors == null || hardpoint.bayDoors.Length == 0)
            {
                if (_logged.Add(Where(hardpoint) + "|" + mount.jsonKey + "|nodoors"))
                    Plugin.Log.LogWarning(
                        $"[Meridian] {Where(hardpoint)} {mount.jsonKey}: this hardpoint carries no bay " +
                        $"doors, so the front of the bay could not be found and {rounds.Length} " +
                        "round(s) were left where the prefab put them.");
                return;
            }

            var doorRenderers = new List<Renderer>();
            foreach (BayDoor door in hardpoint.bayDoors)
            {
                if (door == null) continue;
                doorRenderers.AddRange(door.GetComponentsInChildren<Renderer>(true));
            }

            if (!ForwardExtent(root, doorRenderers, out _, out float bayFront))
            {
                if (_logged.Add(Where(hardpoint) + "|" + mount.jsonKey + "|nobounds"))
                    Plugin.Log.LogWarning(
                        $"[Meridian] {Where(hardpoint)} {mount.jsonKey}: the bay doors reported no " +
                        $"bounds, so its {rounds.Length} round(s) were left where the prefab put them.");
                return;
            }

            if (!ForwardExtent(root, RoundRenderers(rounds), out _, out float noseNow)) return;

            float shift = bayFront - Clearance - noseNow;

            if (shift <= 0f)
            {

                if (_logged.Add(Where(hardpoint) + "|" + mount.jsonKey + "|already"))
                    Plugin.Log.LogInfo(
                        $"[Meridian] {Where(hardpoint)} {mount.jsonKey}: the bay doors reach " +
                        $"z={bayFront:0.000} and the round(s) already reach z={noseNow:0.000}, so " +
                        "nothing was moved.");
                return;
            }

            foreach (MountedMissile r in rounds)
            {
                Transform t = r.transform;
                Vector3 local = root.InverseTransformPoint(t.position);
                local.z += shift;
                t.position = root.TransformPoint(local);
            }

            if (!_logged.Add(Where(hardpoint) + "|" + mount.jsonKey + "|seated")) return;

            Plugin.Log.LogInfo(
                $"[Meridian] {Where(hardpoint)} {mount.jsonKey}: the bay doors reach z={bayFront:0.000} " +
                $"and its round(s) only reached z={noseNow:0.000}, so {rounds.Length} round(s) moved " +
                $"forward together by {shift:0.000} m, keeping the arrangement the prefab authored.");
        }

        private static List<Renderer> RoundRenderers(MountedMissile[] rounds)
        {
            var list = new List<Renderer>();
            foreach (MountedMissile r in rounds)
                list.AddRange(r.GetComponentsInChildren<Renderer>(true));
            return list;
        }

        private static bool Box(Transform root, List<Renderer> renderers, out Bounds box)
        {
            box = default;
            bool any = false;

            foreach (Renderer r in renderers)
            {
                Bounds b;
                if (r is MeshRenderer && r.TryGetComponent(out MeshFilter mf) && mf.sharedMesh != null)
                    b = mf.sharedMesh.bounds;
                else if (r is SkinnedMeshRenderer smr && smr.sharedMesh != null)
                    b = smr.sharedMesh.bounds;
                else
                    continue;

                for (int i = 0; i < 8; i++)
                {
                    var corner = new Vector3(
                        (i & 1) == 0 ? b.min.x : b.max.x,
                        (i & 2) == 0 ? b.min.y : b.max.y,
                        (i & 4) == 0 ? b.min.z : b.max.z);

                    Vector3 p = root.InverseTransformPoint(r.transform.TransformPoint(corner));
                    if (!any) { box = new Bounds(p, Vector3.zero); any = true; }
                    else box.Encapsulate(p);
                }
            }

            return any;
        }

        private static bool ForwardExtent(Transform root, List<Renderer> renderers,
                                          out float min, out float max)
        {
            min = float.MaxValue;
            max = float.MinValue;

            foreach (Renderer r in renderers)
            {
                Bounds b;
                if (r is MeshRenderer && r.TryGetComponent(out MeshFilter mf) && mf.sharedMesh != null)
                    b = mf.sharedMesh.bounds;
                else if (r is SkinnedMeshRenderer smr && smr.sharedMesh != null)
                    b = smr.sharedMesh.bounds;
                else
                    continue;

                for (int i = 0; i < 8; i++)
                {
                    var corner = new Vector3(
                        (i & 1) == 0 ? b.min.x : b.max.x,
                        (i & 2) == 0 ? b.min.y : b.max.y,
                        (i & 4) == 0 ? b.min.z : b.max.z);

                    float z = root.InverseTransformPoint(r.transform.TransformPoint(corner)).z;
                    if (z < min) min = z;
                    if (z > max) max = z;
                }
            }

            return min <= max;
        }

        private static List<Renderer> Structure(Transform root) =>
            root.GetComponentsInChildren<Renderer>(true)
                .Where(r => r.GetComponentInParent<MountedMissile>() == null)
                .ToList();

        private static string Where(Hardpoint? hardpoint) =>
            hardpoint?.transform != null ? hardpoint.transform.root.name : "unknown airframe";

        private static List<Renderer> PylonRenderers(Transform container) =>
            container.GetComponentsInChildren<Renderer>(true).ToList();

        private static bool Extent(Transform root, List<Renderer> renderers, out float min, out float max)
        {
            min = float.MaxValue;
            max = float.MinValue;

            foreach (Renderer r in renderers)
            {
                Bounds b;
                if (r is MeshRenderer && r.TryGetComponent(out MeshFilter mf) && mf.sharedMesh != null)
                    b = mf.sharedMesh.bounds;
                else if (r is SkinnedMeshRenderer smr && smr.sharedMesh != null)
                    b = smr.sharedMesh.bounds;
                else
                    continue;

                for (int i = 0; i < 8; i++)
                {
                    var corner = new Vector3(
                        (i & 1) == 0 ? b.min.x : b.max.x,
                        (i & 2) == 0 ? b.min.y : b.max.y,
                        (i & 4) == 0 ? b.min.z : b.max.z);

                    float y = root.InverseTransformPoint(r.transform.TransformPoint(corner)).y;
                    if (y < min) min = y;
                    if (y > max) max = y;
                }
            }

            return min <= max;
        }

        private readonly struct Donor
        {
            internal Donor(string key, string weaponName, List<Transform> structure)
            {
                Key = key;
                WeaponName = weaponName;
                Structure = structure;
            }

            internal string Key { get; }
            internal string WeaponName { get; }

            internal List<Transform> Structure { get; }
        }

        private static List<Donor>? _donors;
        private static bool _donorsLogged;

        private static Donor? ChooseDonor()
        {
            _donors ??= FindDonors();
            if (_donors.Count == 0) return null;

            Donor preferred = _donors.FirstOrDefault(d => d.WeaponName == PreferredDonorWeapon);
            if (preferred.Structure != null) return preferred;

            Donor other = _donors[0];
            Plugin.Log.LogWarning(
                $"[Meridian] No single mount for '{PreferredDonorWeapon}' was found, so the pylon came " +
                $"from '{other.Key}' instead. If the AGM-68 has been renamed, update PreferredDonorWeapon.");
            return other;
        }

        private static List<Donor> FindDonors()
        {
            var found = new List<Donor>();

            Encyclopedia? enc = GameData.EncyclopediaOrNull();
            if (enc?.weaponMounts == null) return found;

            foreach (WeaponMount m in enc.weaponMounts)
            {
                if (m == null || m.prefab == null) continue;
                if (PluginInfo.IsOurMountKey(m.jsonKey)) continue;

                var rounds = m.prefab.GetComponentsInChildren<MountedMissile>(true);
                if (rounds.Length != 1) continue;

                if (m.jsonKey != null && m.jsonKey.Contains("internal")) continue;

                var structure = new List<Transform>();
                foreach (Transform child in m.prefab.transform)
                {
                    if (child.GetComponentInParent<MountedMissile>() != null) continue;
                    if (child.GetComponentInChildren<MountedMissile>(true) != null) continue;
                    if (child.GetComponentInChildren<Renderer>(true) == null) continue;
                    structure.Add(child);
                }
                if (structure.Count == 0) continue;

                found.Add(new Donor(
                    m.jsonKey ?? m.name,
                    m.info != null ? m.info.weaponName : "?",
                    structure));
            }

            if (_donorsLogged || found.Count == 0) return found;
            _donorsLogged = true;

            Plugin.Log.LogInfo(
                $"[Meridian] Pylon donors found: {found.Count}, " +
                $"including {string.Join(", ", found.Take(6).Select(d => $"'{d.Key}' ({d.WeaponName})").ToArray())}.");
            return found;
        }
    }
}
