using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace MeridianWorks
{

    internal static class RackBorrow
    {

        internal const string ContainerName = "Meridian_BorrowedRack";

        private static readonly HashSet<string> _logged = new HashSet<string>();
        private static bool _donorsLogged;

        internal static void Apply(WeaponMount mount, GameObject spawned)
        {
            if (mount == null || spawned == null) return;
            if (!PluginInfo.IsOurMountKey(mount.jsonKey)) return;

            if (spawned.transform.Find(ContainerName) != null) return;

            List<Transform> stations = Stations(spawned.transform);
            if (stations.Count < 2) return;

            if (HasOwnStructure(spawned.transform))
            {
                if (_logged.Add(mount.jsonKey ?? spawned.name))
                    Plugin.Log.LogInfo(
                        $"[Meridian] {mount.jsonKey}: the mounted prefab carries its own rack geometry, " +
                        "so none was borrowed.");
                return;
            }

            Donor? pick = ChooseDonor(stations.Count, mount.info != null ? mount.info.massPerRound : 0f, mount.jsonKey);
            if (pick == null)
            {
                if (_logged.Add(mount.jsonKey ?? spawned.name))
                    Plugin.Log.LogWarning(
                        $"[Meridian] {mount.jsonKey}: no stock mount with {stations.Count} stations and " +
                        "rack geometry was found, so the rounds hang unsupported. Model a rack in Unity.");
                return;
            }

            Donor donor = pick.Value;

            var container = new GameObject(ContainerName);
            container.transform.SetParent(spawned.transform, false);

            int cloned = 0;
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

                cloned++;
            }

            int moved = 0;
            for (int i = 0; i < stations.Count && i < donor.Stations.Count; i++)
            {

                Transform ours = stations[i];
                Vector3 want = donor.Stations[i];
                ours.localPosition = ours.parent == spawned.transform
                    ? want
                    : ours.parent.InverseTransformPoint(spawned.transform.TransformPoint(want));
                moved++;
            }

            float need = OurWidth(mount) * 1.05f;
            if (need > 0f && stations.Count > 1)
            {
                var xs = stations.Select(t => t.localPosition.x).OrderBy(v => v).ToList();
                float tightest = float.MaxValue;
                for (int i = 1; i < xs.Count; i++) tightest = Mathf.Min(tightest, xs[i] - xs[i - 1]);

                if (tightest > 0.0001f && tightest < need)
                {
                    float spread = need / tightest;
                    float centre = stations.Average(t => t.localPosition.x);
                    foreach (Transform t in stations)
                    {
                        Vector3 p = t.localPosition;
                        p.x = centre + (p.x - centre) * spread;
                        t.localPosition = p;
                    }
                    Plugin.Log.LogInfo(
                        $"[Meridian] {mount.jsonKey}: the donor's stations were {tightest:0.00} m apart " +
                        $"and this round is {need:0.00} m across, so they were spread by x{spread:0.00}.");
                }
            }

            if (donor.Stations.Count > 1 &&
                (donor.Stations[0] - donor.Stations[donor.Stations.Count - 1]).sqrMagnitude < 0.0001f)
                Plugin.Log.LogWarning(
                    $"[Meridian] Rack donor '{donor.Key}' reports every station at the same point, so " +
                    "our rounds will sit on top of each other. Pick another donor.");

            if (!_logged.Add(mount.jsonKey ?? spawned.name)) return;

            Plugin.Log.LogInfo(
                $"[Meridian] {mount.jsonKey}: borrowed a {donor.Stations.Count}-station rack from " +
                $"'{donor.Key}' ({donor.MassPerRound:0} kg a round against our {(mount.info != null ? mount.info.massPerRound : 0f):0}) " +
                $"- {cloned} piece(s) of structure ({renderers} renderer(s)), {moved} round(s) moved " +
                "onto its stations.");
        }

        private static float OurWidth(WeaponMount mount)
        {
            PluginInfo.Weapon? w = PluginInfo.WeaponForMountKey(mount.jsonKey);
            if (w == null) return 0f;

            Encyclopedia? enc = GameData.EncyclopediaOrNull();
            MissileDefinition? def = enc?.missiles?
                .FirstOrDefault(d => d != null && d.jsonKey == w.MissileKey);

            return def != null ? Mathf.Max(def.width, def.height) : 0f;
        }

        private static List<Transform> Stations(Transform root) =>
            root.GetComponentsInChildren<MountedMissile>(true)
                .Select(m => m.transform)
                .OrderBy(t => root.InverseTransformPoint(t.position).x)
                .ThenBy(t => root.InverseTransformPoint(t.position).y)
                .ToList();

        private static bool HasOwnStructure(Transform root) =>
            root.GetComponentsInChildren<Renderer>(true)
                .Any(r => r.GetComponentInParent<MountedMissile>() == null);

        private readonly struct Donor
        {
            internal Donor(string key, float massPerRound, List<Transform> structure, List<Vector3> stations)
            {
                Key = key; MassPerRound = massPerRound; Structure = structure; Stations = stations;
            }

            internal string Key { get; }
            internal float MassPerRound { get; }

            internal List<Transform> Structure { get; }

            internal List<Vector3> Stations { get; }
        }

        private static List<Donor>? _donors;

        private static readonly Dictionary<string, string> PreferredDonors =
            new Dictionary<string, string>
            {
                { "MeridianAGM84_x2", "AShM1x2" },

                { "MeridianAGM33L_triple", "AGM_heavy_triple" },
            };

        private static Donor? ChooseDonor(int stationCount, float ourMassPerRound, string? ourKey)
        {
            _donors ??= FindDonors();

            var fits = _donors.Where(d => d.Stations.Count == stationCount).ToList();
            if (fits.Count == 0) return null;

            if (ourKey != null && PreferredDonors.TryGetValue(ourKey, out string wanted))
            {
                var named = fits.Where(d => d.Key == wanted).ToList();
                if (named.Count > 0) return named[0];

                Plugin.Log.LogWarning(
                    $"[Meridian] {ourKey}: the rack '{wanted}' the owner picked was not found with " +
                    $"{stationCount} station(s), so one was chosen by weight instead.");
            }

            if (ourMassPerRound <= 0f)
                return fits.OrderByDescending(d => d.MassPerRound).First();

            return fits.OrderBy(d => Mathf.Abs(d.MassPerRound - ourMassPerRound)).First();
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
                if (rounds.Length < 2) continue;

                var structure = new List<Transform>();
                foreach (Transform child in m.prefab.transform)
                {
                    if (child.GetComponentInParent<MountedMissile>() != null) continue;
                    if (child.GetComponentInChildren<MountedMissile>(true) != null) continue;
                    if (child.GetComponentInChildren<Renderer>(true) == null) continue;
                    structure.Add(child);
                }
                if (structure.Count == 0) continue;

                var stations = rounds
                    .Select(r => m.prefab.transform.InverseTransformPoint(r.transform.position))
                    .OrderBy(p => p.x)
                    .ThenBy(p => p.y)
                    .ToList();

                float perRound = m.info != null ? m.info.massPerRound : 0f;
                found.Add(new Donor(m.jsonKey ?? m.name, perRound, structure, stations));
            }

            if (_donorsLogged || found.Count == 0) return found;
            _donorsLogged = true;

            Plugin.Log.LogInfo(
                $"[Meridian] Rack donors found: " +
                string.Join(", ", found
                    .OrderBy(d => d.Stations.Count)
                    .Select(d => $"'{d.Key}' x{d.Stations.Count} at {d.MassPerRound:0} kg")
                    .ToArray()));
            return found;
        }
    }

    [HarmonyPatch(typeof(Hardpoint), nameof(Hardpoint.SpawnMount))]
    internal static class Hardpoint_SpawnMount_MountDressingPatch
    {
        [HarmonyPostfix]
        private static void Postfix(Hardpoint __instance, WeaponMount weaponMount, GameObject __result)
        {
            try
            {
                RackBorrow.Apply(weaponMount, __result);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[Meridian] Rack borrowing threw: {ex.Message}");
            }

            try
            {
                PylonBorrow.Apply(__instance, weaponMount, __result);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[Meridian] Pylon borrowing threw: {ex.Message}");
            }
        }
    }
}
