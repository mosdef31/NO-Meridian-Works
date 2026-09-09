using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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

        private const string UnderStubMarker = "MeridianUnderStub";

        private const string ReplaceStubMarker = "MeridianReplaceStub";

        private const string SkinCloseMarker = "MeridianSkinClosed";

        private static void SeatUnderStub(Hardpoint hardpoint, WeaponMount mount, Transform root,
                                          List<Renderer> structure)
        {

            if (root.Find(UnderStubMarker) != null) return;

            if (!Extent(root, structure, out _, out float ourTop))
            {
                Plugin.Diag(
                    $"[Meridian] {Where(hardpoint)} {mount.jsonKey}: the authored structure reported no bounds, " +
                    "so it was left where the game put it.");
                return;
            }

            bool haveStub = StubBottom(hardpoint, root, out float stubBottom);
            float delta = (haveStub ? Mathf.Max(stubBottom, 0f) : 0f) - ourTop;

            if (Mathf.Abs(delta) > 0.0005f)
            {
                var children = new List<Transform>();
                foreach (Transform child in root) children.Add(child);
                foreach (Transform child in children)
                    child.localPosition += new Vector3(0f, delta, 0f);
            }

            new GameObject(UnderStubMarker).transform.SetParent(root, false);

            if (_logged.Add("understub|" + Where(hardpoint) + "|" + mount.jsonKey))
                Plugin.Diag(
                    $"[Meridian] {Where(hardpoint)} {mount.jsonKey}: hung under " +
                    (haveStub
                        ? $"the aircraft's stub, whose underside is at y={stubBottom:0.000}"
                        : "the hardpoint's own face at y=0.000, because no stub is drawn here") +
                    $". The assembly moved {delta:0.000} m.");
        }

        private static readonly string[] ForcedLiftAirframes =
        {
            "Aryx_LightFighter1",
            "Aryx_PropAttacker1",
        };

        private static bool ForcedLift(Hardpoint hardpoint)
        {
            string where = Where(hardpoint);
            foreach (string name in ForcedLiftAirframes)
                if (where.StartsWith(name, System.StringComparison.Ordinal)) return true;
            return false;
        }

        private static void CloseToSkin(Hardpoint hardpoint, WeaponMount mount, GameObject spawned)
        {
            if (hardpoint == null || mount == null || spawned == null) return;
            if (!PluginInfo.IsOurMountKey(mount.jsonKey)) return;

            Transform root = spawned.transform;

            if (root.Find(SkinCloseMarker) != null) return;

            bool forced = ForcedLift(hardpoint);

            Renderer? drawn = DrawnStub(hardpoint);
            if (drawn != null && !forced)
            {

                if (_logged.Add("stubsrc|" + Where(hardpoint) + "|" + mount.jsonKey))
                    Plugin.Diag(
                        $"[Meridian] SKIN {Where(hardpoint)} {mount.jsonKey}: a stub IS drawn "
                        + $"('{drawn.name}', from "
                        + (ReferenceEquals(drawn, hardpoint.Pylon)
                            ? "Hardpoint.Pylon"
                            : "the pylonOptions array, which this method could not see before "
                              + "2026-09-07")
                        + "), so the seating already used it and no lift is applied.");
                return;
            }

            if (AuthoredMountStub.Replaces(hardpoint)) return;

            List<Renderer> structure = Structure(root);
            if (structure.Count == 0) return;

            float gap = 0f;
            int quadrants = 0;
            bool measured = !forced && MountCantProbe.SkinGap(
                hardpoint, root, structure, out gap, out quadrants);

            bool implausible = measured && gap > MountCantProbe.PlausibleStandoff;
            if (implausible) measured = false;

            float lift;
            string why;
            if (measured)
            {

                float raw = Mathf.Max(gap, 0f);
                lift = Mathf.Max(raw, MountCantProbe.BlindLift);
                why = raw < MountCantProbe.BlindLift
                    ? $"the airframe skin above it was found in {quadrants} of 4 quadrants, "
                      + $"nearest standoff {raw:0.0000} m, which is under the {MountCantProbe.BlindLift:0.00} m "
                      + "floor, so it took the floor instead"
                    : $"the airframe skin above it was found in {quadrants} of 4 quadrants, "
                      + $"nearest standoff {raw:0.0000} m, so it was raised onto it";
            }
            else
            {

                lift = MountCantProbe.BlindLift;
                why = forced
                    ? "this airframe is on the forced list, so the lift was applied without "
                      + "measuring - every probe here reads the joint as already flush and "
                      + "the owner can still see a gap on it"
                    : implausible
                    ? $"the sweep reported {gap:0.0000} m, which is too far to be this mount's "
                      + "own joint, so that reading was discarded and it took the blind lift"
                    : "no airframe surface sits above it in two or more quadrants, so it took "
                      + "the blind lift";
            }

            if (lift > MountCantProbe.CleanGap)
            {
                var children = new List<Transform>();
                foreach (Transform child in root) children.Add(child);
                foreach (Transform child in children)
                    child.localPosition += new Vector3(0f, lift, 0f);
            }

            new GameObject(SkinCloseMarker).transform.SetParent(root, false);

            if (_logged.Add("skin|" + Where(hardpoint) + "|" + mount.jsonKey))
                Plugin.Diag(
                    $"[Meridian] SKIN {Where(hardpoint)} {mount.jsonKey}: no stub is drawn, and "
                    + why + ". "
                    + (lift > MountCantProbe.CleanGap
                        ? $"Raised {lift:0.0000} m."
                        : "Already flush, so nothing moved."));
        }

        private static void SeatReplacingStub(Hardpoint hardpoint, WeaponMount mount, Transform root,
                                              List<Renderer> structure)
        {
            if (root.Find(ReplaceStubMarker) != null) return;

            if (!Extent(root, structure, out _, out float ourTop))
            {
                Plugin.Diag(
                    $"[Meridian] {Where(hardpoint)} {mount.jsonKey}: the authored structure reported no bounds, " +
                    "so it was left where the game put it.");
                return;
            }

            bool haveStub = StubExtent(hardpoint, root, out float stubBottom, out float stubTop);
            float delta = haveStub ? stubTop - ourTop : 0f;

            if (Mathf.Abs(delta) > 0.0005f)
            {
                var children = new List<Transform>();
                foreach (Transform child in root) children.Add(child);
                foreach (Transform child in children)
                    child.localPosition += new Vector3(0f, delta, 0f);
            }

            new GameObject(ReplaceStubMarker).transform.SetParent(root, false);

            if (_logged.Add("replacestub|" + Where(hardpoint) + "|" + mount.jsonKey))
                Plugin.Diag(
                    $"[Meridian] DATUM {Where(hardpoint)} {mount.jsonKey}: REPLACES the stub. " +
                    (haveStub
                        ? $"Stub spans y={stubBottom:0.000} to y={stubTop:0.000}; "
                        : "No stub is drawn here, so the hardpoint's own face at y=0.000 is the datum; ") +
                    $"our structure topped out at y={ourTop:0.000}. The assembly moved {delta:0.000} m.");
        }

        internal static void Apply(Hardpoint hardpoint, WeaponMount mount, GameObject spawned)
        {
            ApplyCore(hardpoint, mount, spawned);

            CloseToSkin(hardpoint, mount, spawned);

            ClearTheFlap(hardpoint, mount, spawned);

            Paint(hardpoint, mount, spawned);

            MountFitProbe.Report(hardpoint, mount, spawned);

            MountCantProbe.Report(hardpoint, mount, spawned);
        }

        private static void ApplyCore(Hardpoint hardpoint, WeaponMount mount, GameObject spawned)
        {
            if (mount == null || spawned == null) return;

            AuthoredMountStub.Mark(hardpoint, false);

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

                bool underStub = PluginInfo.HangsUnderStub(mount.jsonKey);
                AuthoredMountStub.Mark(hardpoint, !underStub);

                if (underStub) SeatUnderStub(hardpoint, mount, spawned.transform, existing);
                else SeatReplacingStub(hardpoint, mount, spawned.transform, existing);

                if (_logged.Add(Where(hardpoint) + "|" + mount.jsonKey))
                    Plugin.Diag(
                        $"[Meridian] {Where(hardpoint)} {mount.jsonKey}: the mounted prefab carries its own structure, " +
                        "so no pylon was borrowed. " +
                        (underStub
                            ? "It hangs UNDER the aircraft's stub, which stays drawn."
                            : "It REPLACES the aircraft's stub, which was hidden."));
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
                bool haveStub = StubExtent(hardpoint, root, out float stubBottom, out float stubTop);
                Extent(root, PylonRenderers(container.transform), out float donorBottom, out _);

                float top = haveStub ? Mathf.Max(stubBottom, 0f) : 0f;

                container.transform.localPosition += new Vector3(0f, top - pylonTop, 0f);

                stubNote = haveStub
                    ? $"hung from the aircraft's stub, which spans y={stubBottom:0.000} to " +
                      $"y={stubTop:0.000} ({(stubTop - stubBottom):0.000} m tall), at y={top:0.000}" +
                      (stubBottom < 0f
                          ? " - its underside is BELOW the hardpoint face, so the face was used instead"
                          : " by its underside")
                    : "no stub found, so hung from the hardpoint's own face at y=0.000";

                if (_logged.Add("datum|" + Where(hardpoint) + "|" + mount.jsonKey))
                    Plugin.Diag(
                        $"[Meridian] DATUM {Where(hardpoint)} {mount.jsonKey}: BORROWS a pylon. " +
                        (haveStub
                            ? $"Stub spans y={stubBottom:0.000} to y={stubTop:0.000}; "
                            : "No stub is drawn here, so the hardpoint's own face at y=0.000 is the datum; ") +
                        $"the borrowed pylon topped out at y={pylonTop:0.000} and moved " +
                        $"{(top - pylonTop):0.000} m. Donor pylon height " +
                        $"{(pylonTop - donorBottom):0.000} m, and THE ROUND HANGS BELOW ALL OF IT.");
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
                    Plugin.Log.LogWarning($"[Meridian] {Where(hardpoint)} {mount.jsonKey}: the borrowed structure reported no bounds.");
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

            Plugin.Diag(
                $"[Meridian] {Where(hardpoint)} {mount.jsonKey}: borrowed the pylon from '{donor.Key}' " +
                $"({renderers} renderer(s)), {stubNote}" +
                (rack != null ? ", with the borrowed rack slid under it" : "") +
                $". {rounds.Length} round(s) hang from y={hangBottom:0.000} at a body radius of " +
                $"{radius:0.000} m, tucked up by a {SafetyMargin:0.000} m margin, so their centrelines " +
                $"sit at y={seatY:0.000}.");
        }

        private static bool StubBottom(Hardpoint hardpoint, Transform root, out float bottom) =>
            StubExtent(hardpoint, root, out bottom, out _);

        private static bool StubExtent(Hardpoint hardpoint, Transform root,
                                       out float bottom, out float top)
        {
            bottom = 0f;
            top = 0f;
            if (hardpoint == null) return false;

            if (hardpoint.Pylon != null)
            {
                bool drawn = hardpoint.Pylon.enabled &&
                             hardpoint.Pylon.gameObject.activeInHierarchy;

                if (drawn && Extent(root, new List<Renderer> { hardpoint.Pylon }, out bottom, out top))
                {
                    return true;
                }

                if (!drawn && _logged.Add(Where(hardpoint) + "|stub-hidden"))
                {
                    Plugin.Diag(
                        $"[Meridian] {Where(hardpoint)}: this hardpoint carries a stub renderer " +
                        $"('{hardpoint.Pylon.name}') that is NOT being drawn " +
                        $"(enabled={hardpoint.Pylon.enabled}, " +
                        $"active={hardpoint.Pylon.gameObject.activeInHierarchy}), so it was ignored " +
                        "and the hardpoint's own face was used instead.");
                }
            }

            Renderer? drawnOption = DrawnStub(hardpoint);
            if (drawnOption != null && !ReferenceEquals(drawnOption, hardpoint.Pylon))
                return Extent(root, new List<Renderer> { drawnOption }, out bottom, out top);

            if (FPylonOptions?.GetValue(hardpoint) is not Array options) return false;

            foreach (object entry in options)
            {
                if (FEntryCargo?.GetValue(entry) is true) continue;
                if (FEntryMount?.GetValue(entry) is WeaponMount) continue;
                if (FEntryRenderer?.GetValue(entry) is not Renderer r) continue;

                return Extent(root, new List<Renderer> { r }, out bottom, out top);
            }

            return false;
        }

        internal static Renderer? DrawnStub(Hardpoint? hardpoint)
        {
            if (hardpoint == null) return null;

            if (hardpoint.Pylon != null
                && hardpoint.Pylon.enabled
                && hardpoint.Pylon.gameObject.activeInHierarchy)
                return hardpoint.Pylon;

            if (FPylonOptions?.GetValue(hardpoint) is not Array options) return null;

            foreach (object entry in options)
            {
                if (FEntryCargo?.GetValue(entry) is true) continue;
                if (FEntryRenderer?.GetValue(entry) is not Renderer r) continue;
                if (r == null || !r.enabled || !r.gameObject.activeInHierarchy) continue;
                return r;
            }

            return null;
        }

        private static readonly System.Reflection.FieldInfo? FPylonOptions =
            AccessTools.Field(typeof(Hardpoint), "pylonOptions");

        private static readonly System.Reflection.FieldInfo? FEntryMount =
            AccessTools.Field(AccessTools.Inner(typeof(Hardpoint), "HardpointPylon"), "mount");

        private static readonly System.Reflection.FieldInfo? FEntryCargo =
            AccessTools.Field(AccessTools.Inner(typeof(Hardpoint), "HardpointPylon"), "cargo");

        private static readonly System.Reflection.FieldInfo? FEntryRenderer =
            AccessTools.Field(AccessTools.Inner(typeof(Hardpoint), "HardpointPylon"), "renderer");

        private static void ClearTheFlap(Hardpoint hardpoint, WeaponMount mount, GameObject spawned)
        {
            if (mount == null || spawned == null || hardpoint == null) return;
            if (string.IsNullOrEmpty(mount.jsonKey)) return;
            if (!PluginInfo.IsOurMountKey(mount.jsonKey)) return;
            if (mount.missileBay || mount.jsonKey.Contains("internal")) return;

            Transform root = spawned.transform;

            var all = new List<Renderer>(root.GetComponentsInChildren<Renderer>(true));
            if (all.Count == 0) return;
            if (!LocalBounds(root, all, out Bounds ours)) return;

            Transform? air = AircraftRoot(hardpoint);
            if (air == null) return;

            float worst = 0f;
            string which = "";
            int flapsSeen = 0;
            int surfacesSeen = 0;
            List<ControlSurface> surfaces = SurfacesFor(hardpoint, air, out string route);
            foreach (ControlSurface cs in surfaces)
            {
                if (cs == null) continue;
                surfacesSeen++;
                if (IsFlap(cs)) flapsSeen++;

                GameObject? mesh = FlapMesh(cs);
                var flapRenderers = new List<Renderer>(
                    (mesh != null ? mesh : cs.gameObject).GetComponentsInChildren<Renderer>(true));
                if (flapRenderers.Count == 0) continue;
                if (!LocalBounds(root, flapRenderers, out Bounds flap)) continue;

                if (flap.min.x > ours.max.x || flap.max.x < ours.min.x) continue;

                if (flap.center.z > ours.center.z) continue;

                float overlap = flap.max.z - ours.min.z;
                if (overlap > worst)
                {
                    worst = overlap;
                    which = cs.gameObject.name;
                }
            }

            if (worst <= 0.001f)
            {

                if (_logged.Add(Where(hardpoint) + "|" + mount.jsonKey + "|noflap"))
                    Plugin.Diag(
                        $"[Meridian] {Where(hardpoint)} {mount.jsonKey}: {surfacesSeen} control "
                        + $"surface(s) found under '{air.name}' {route}, {flapsSeen} of them flagged "
                        + "flaps, and none overlaps this mount across the span while sitting behind "
                        + "it, so nothing was moved forward.");
                return;
            }

            float mountLength = ours.size.z;
            if (mountLength > 0.001f && worst > mountLength)
            {
                if (_logged.Add(Where(hardpoint) + "|" + mount.jsonKey + "|flap"))
                    Plugin.Log.LogWarning(
                        $"[Meridian] {Where(hardpoint)} {mount.jsonKey}: surface '{which}' overlaps this "
                        + $"mount by {worst:0.000} m, more than the mount's own {mountLength:0.000} m "
                        + "length. That is not a surface sitting behind a pylon, so NOTHING was moved - "
                        + "find out what that surface actually is before trusting the number.");
                return;
            }

            float ceiling = -ours.min.z;
            if (worst + 0.06f > ceiling)
            {
                if (_logged.Add(Where(hardpoint) + "|" + mount.jsonKey + "|flap"))
                    Plugin.Log.LogWarning(
                        $"[Meridian] CLEARANCE {Where(hardpoint)} {mount.jsonKey}: surface '{which}' "
                        + $"needs this mount {worst + 0.06f:0.000} m forward, but the hardpoint sits "
                        + $"{ceiling:0.000} m from its aft end, so that much would leave the rack "
                        + "hanging off the front of its own pylon. NOTHING was moved and this round "
                        + "still clips here. Remove this cell from loadout-table.md.");
                return;
            }

            float shift = worst + 0.06f;
            root.localPosition = root.localPosition + new Vector3(0f, 0f, shift);

            if (!_logged.Add(Where(hardpoint) + "|" + mount.jsonKey + "|flap")) return;

            Plugin.Diag(
                $"[Meridian] {Where(hardpoint)} {mount.jsonKey}: surface '{which}' reached "
                + $"z={ours.min.z + worst:0.000} against this mount's aft end at z={ours.min.z:0.000}, "
                + $"so the whole mount moved FORWARD {shift:0.000} m to clear it.");
        }

        private static readonly Dictionary<int, List<ControlSurface>> _surfaceCache =
            new Dictionary<int, List<ControlSurface>>();

        private static List<ControlSurface> SurfacesFor(Hardpoint hardpoint, Transform air, out string route)
        {
            var direct = new List<ControlSurface>(air.GetComponentsInChildren<ControlSurface>(true));
            if (direct.Count > 0)
            {
                route = "by hierarchy";
                return direct;
            }

            Unit? unit = UnitOf(hardpoint);
            if (unit == null)
            {
                route = "by hierarchy (and no unit to sweep the scene for)";
                return direct;
            }

            int key = unit.GetInstanceID();
            if (_surfaceCache.TryGetValue(key, out List<ControlSurface> cached))
            {
                route = $"by scene sweep, cached ({cached.Count})";
                return cached;
            }

            var mine = new List<ControlSurface>();
            foreach (ControlSurface cs in Resources.FindObjectsOfTypeAll<ControlSurface>())
            {
                if (cs == null) continue;
                if (!cs.gameObject.scene.IsValid()) continue;
                if (OwnerUnit(cs) != unit) continue;
                mine.Add(cs);
            }

            _surfaceCache[key] = mine;
            route = $"by scene sweep, nothing under the unit's own transform ({mine.Count} matched this unit)";
            return mine;
        }

        private static Unit? UnitOf(Hardpoint hardpoint)
        {
            if (hardpoint == null) return null;
            if (hardpoint.part != null && hardpoint.part.parentUnit != null) return hardpoint.part.parentUnit;
            if (hardpoint.transform != null) return hardpoint.transform.GetComponentInParent<Unit>();
            return null;
        }

        private static Unit? OwnerUnit(ControlSurface cs)
        {
            try
            {
                FieldInfo? f = typeof(ControlSurface).GetField(
                    "attachedSurface", BindingFlags.Instance | BindingFlags.NonPublic);
                var part = f?.GetValue(cs) as UnitPart;
                return part != null ? part.parentUnit : null;
            }
            catch { return null; }
        }

        private static void Paint(Hardpoint hardpoint, WeaponMount mount, GameObject spawned)
        {
            if (!PluginConfig.LiveryMounts) return;
            if (mount == null || spawned == null || hardpoint == null) return;
            if (!PluginInfo.IsOurMountKey(mount.jsonKey)) return;

            Unit? unit = UnitOf(hardpoint);
            WeaponManager? weapons = ManagerOf(unit, hardpoint);
            if (weapons == null)
            {
                if (_logged.Add("paint|" + Where(hardpoint) + "|" + mount.jsonKey))
                    Plugin.Diag(
                        $"[Meridian] {Where(hardpoint)} {mount.jsonKey}: no WeaponManager was found "
                        + $"on this airframe (unit {(unit != null ? unit.name : "not resolved")}), so "
                        + "the mount cannot be registered for the livery and stays factory grey.");
                return;
            }

            List<Renderer> structure = Structure(spawned.transform);
            if (structure.Count == 0)
            {
                if (_logged.Add("paint|" + Where(hardpoint) + "|" + mount.jsonKey))
                    Plugin.Diag(
                        $"[Meridian] {Where(hardpoint)} {mount.jsonKey}: this mount draws no structure "
                        + "of its own, only rounds, so there is nothing to paint in the livery.");
                return;
            }

            foreach (Renderer r in structure)
            {
                if (r == null) continue;
                weapons.RegisterColorable(r);

                LiveryMirror.Track(r);
            }

            object? livery = FLiveryData?.GetValue(weapons);
            if (livery != null)
            {
                try { weapons.UpdateColorables((LiveryData)livery); }
                catch (Exception ex)
                {
                    Plugin.Log.LogWarning(
                        $"[Meridian] {Where(hardpoint)} {mount.jsonKey}: the livery colour could not be "
                        + $"applied to the mount: {ex.Message}");
                    return;
                }
            }

            if (_logged.Add("paint|" + Where(hardpoint) + "|" + mount.jsonKey))
                Plugin.Diag(
                    $"[Meridian] {Where(hardpoint)} {mount.jsonKey}: {structure.Count} structure "
                    + "renderer(s) registered as colorables, and mirrored onto URP/Lit's _BaseColor, "
                    + "so the mount takes the aircraft's livery colour. The round keeps its own finish. "
                    + $"Livery {(livery != null ? "was already loaded and applied now" : "has not loaded yet, so it paints on load")}.");
        }

        private static WeaponManager? ManagerOf(Unit? unit, Hardpoint hardpoint)
        {
            if (unit is Aircraft aircraft && aircraft.weaponManager != null)
                return aircraft.weaponManager;

            if (unit != null)
            {
                WeaponManager? inChildren = unit.GetComponentInChildren<WeaponManager>(true);
                if (inChildren != null) return inChildren;
            }

            if (hardpoint != null && hardpoint.transform != null)
            {
                WeaponManager? up = hardpoint.transform.GetComponentInParent<WeaponManager>();
                if (up != null) return up;

                Transform root = hardpoint.transform.root;
                WeaponManager? swept = root.GetComponentInChildren<WeaponManager>(true);
                if (swept != null) return swept;
            }

            return null;
        }

        private static readonly System.Reflection.FieldInfo? FLiveryData =
            AccessTools.Field(typeof(WeaponManager), "liveryData");

        private static bool IsFlap(ControlSurface cs)
        {
            try
            {
                FieldInfo? f = typeof(ControlSurface).GetField(
                    "flap", BindingFlags.Instance | BindingFlags.NonPublic);
                object? v = f?.GetValue(cs);
                return v is bool b && b;
            }
            catch { return false; }
        }

        private static GameObject? FlapMesh(ControlSurface cs)
        {
            try
            {
                FieldInfo? f = typeof(ControlSurface).GetField(
                    "visibleMesh", BindingFlags.Instance | BindingFlags.NonPublic);
                return f?.GetValue(cs) as GameObject;
            }
            catch { return null; }
        }

        private static void SeatInBay(Hardpoint hardpoint, WeaponMount mount, GameObject spawned)
        {
            Transform root = spawned.transform;
            var rounds = spawned.GetComponentsInChildren<MountedMissile>(true);
            if (rounds.Length == 0) return;

            SeatInBayForeAft(hardpoint, mount, root, rounds);
            ClampToBayRoof(hardpoint, mount, root, rounds);
        }

        private static void SeatInBayForeAft(Hardpoint hardpoint, WeaponMount mount,
                                             Transform root, MountedMissile[] rounds)
        {

            const float Clearance = 0.05f;

            if (rounds.Length > 1)
            {
                MeasureBayFit(hardpoint, mount, root, rounds);

                if (_logged.Add(Where(hardpoint) + "|" + mount.jsonKey + "|multi"))
                    Plugin.Diag(
                        $"[Meridian] {Where(hardpoint)} {mount.jsonKey}: {rounds.Length} round(s) left " +
                        "exactly where the aircraft's hardpoint puts them. Bay doors are shared " +
                        "between sets, so they cannot say which end of the bay this set belongs at.");
                return;
            }

            if (hardpoint?.bayDoors == null || hardpoint.bayDoors.Length == 0)
            {
                if (_logged.Add(Where(hardpoint) + "|" + mount.jsonKey + "|nodoors"))
                    Plugin.Log.LogWarning(
                $"[Meridian] {Where(hardpoint)} {mount.jsonKey}: this hardpoint carries no bay "
                + $"doors.");
                return;
            }

            if (DoorsSharedWithAnotherHardpoint(hardpoint))
            {
                if (_logged.Add(Where(hardpoint) + "|" + mount.jsonKey + "|shared"))
                    Plugin.Diag(
                        $"[Meridian] {Where(hardpoint)} {mount.jsonKey}: its {rounds.Length} round(s) " +
                        "were left exactly where the aircraft's hardpoint puts them. Another hardpoint " +
                        "on this airframe shares these bay doors, so the door box cannot say which end " +
                        "of the bay this set belongs at.");
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

            float roundLength = RoundLength(root, rounds);

            if (roundLength > 0f && shift > roundLength)
            {
                if (_logged.Add(Where(hardpoint) + "|" + mount.jsonKey + "|overlong"))
                    Plugin.Log.LogWarning(
                        $"[Meridian] {Where(hardpoint)} {mount.jsonKey}: the bay doors reach " +
                        $"z={bayFront:0.000} and the round(s) reach z={noseNow:0.000}, which asks for a " +
                        $"{shift:0.000} m shove against a round only {roundLength:0.000} m long. A shove " +
                        "longer than the round means these doors span more than this one bay, so " +
                        "NOTHING WAS MOVED - the aircraft's own hardpoint is the better answer.");
                return;
            }

            if (shift <= 0f)
            {

                if (_logged.Add(Where(hardpoint) + "|" + mount.jsonKey + "|already"))
                    Plugin.Diag(
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

            if (_logged.Add(Where(hardpoint) + "|" + mount.jsonKey + "|seated"))
                Plugin.Diag(
                    $"[Meridian] {Where(hardpoint)} {mount.jsonKey}: the bay doors reach z={bayFront:0.000} " +
                    $"and its round(s) only reached z={noseNow:0.000}, so {rounds.Length} round(s) moved " +
                    $"forward together by {shift:0.000} m, keeping the arrangement the prefab authored.");
        }

        private static void ClampToBayRoof(Hardpoint hardpoint, WeaponMount mount,
                                           Transform root, MountedMissile[] rounds)
        {

            const float DoorClearance = 0.03f;

            if (hardpoint?.bayDoors == null || hardpoint.bayDoors.Length == 0)
            {
                if (_logged.Add(Where(hardpoint) + "|" + mount.jsonKey + "|roofnodoors"))
                    Plugin.Diag(
                        $"[Meridian] {Where(hardpoint)} {mount.jsonKey}: this bay hardpoint "
                        + "carries no doors, so there is no floor to seat the block onto and "
                        + "nothing was lowered.");
                return;
            }

            var doorRenderers = new List<Renderer>();
            foreach (BayDoor door in hardpoint.bayDoors)
            {
                if (door == null) continue;
                doorRenderers.AddRange(door.GetComponentsInChildren<Renderer>(true));
            }

            if (!LocalBounds(root, doorRenderers, out Bounds bay))
            {
                if (_logged.Add(Where(hardpoint) + "|" + mount.jsonKey + "|roofnobay"))
                    Plugin.Diag(
                        $"[Meridian] {Where(hardpoint)} {mount.jsonKey}: the bay doors reported "
                        + "no bounds, so nothing was lowered.");
                return;
            }

            if (!LocalBounds(root, RoundRenderers(rounds), out Bounds ours))
            {
                if (_logged.Add(Where(hardpoint) + "|" + mount.jsonKey + "|roofnoblock"))
                    Plugin.Diag(
                        $"[Meridian] {Where(hardpoint)} {mount.jsonKey}: its round(s) reported no "
                        + "renderer bounds, so the block's own height is unknown and nothing was "
                        + "lowered.");
                return;
            }

            float floor = bay.center.y + DoorClearance;
            float over = ours.min.y - floor;

            if (over <= 0.001f)
            {
                if (_logged.Add(Where(hardpoint) + "|" + mount.jsonKey + "|roof"))
                    Plugin.Diag(
                        $"[Meridian] {Where(hardpoint)} {mount.jsonKey}: the block's underside is "
                        + $"at y={ours.min.y:0.000} against a door plane of y={bay.center.y:0.000}, "
                        + "so it is already sitting in its bay and nothing was lowered.");
                return;
            }

            float blockHeight = ours.size.y;
            float bound = Mathf.Max(blockHeight, 0.5f) * 3f;
            if (over > bound)
            {
                if (_logged.Add(Where(hardpoint) + "|" + mount.jsonKey + "|roofoverlong"))
                    Plugin.Log.LogWarning(
                        $"[Meridian] {Where(hardpoint)} {mount.jsonKey}: the block's underside is "
                        + $"at y={ours.min.y:0.000} and the door plane is y={bay.center.y:0.000}, "
                        + $"which asks for a {over:0.000} m drop against a block only "
                        + $"{blockHeight:0.000} m tall. That is not one bay, so NOTHING WAS "
                        + "MOVED.");
                return;
            }

            foreach (MountedMissile r in rounds)
            {
                Transform t = r.transform;
                Vector3 local = root.InverseTransformPoint(t.position);
                local.y -= over;
                t.position = root.TransformPoint(local);
            }

            if (!_logged.Add(Where(hardpoint) + "|" + mount.jsonKey + "|roof")) return;

            Plugin.Diag(
                $"[Meridian] {Where(hardpoint)} {mount.jsonKey}: the block's underside stood at "
                + $"y={ours.min.y:0.000} above a door plane of y={bay.center.y:0.000}, so "
                + $"{rounds.Length} round(s) came DOWN together by {over:0.000} m onto the doors, "
                + "keeping the arrangement the prefab authored.");
        }

        private static Transform? AircraftRoot(Hardpoint hardpoint)
        {
            if (hardpoint == null) return null;

            if (hardpoint.part != null && hardpoint.part.parentUnit != null)
                return hardpoint.part.parentUnit.transform;

            if (hardpoint.transform != null)
            {
                WeaponManager? weapons = hardpoint.transform.GetComponentInParent<WeaponManager>();
                if (weapons != null) return weapons.transform.root;
                return hardpoint.transform.root;
            }

            return null;
        }

        private static HardpointSet? SetContaining(Hardpoint hardpoint)
        {
            WeaponManager? weapons = null;

            if (hardpoint.part != null && hardpoint.part.parentUnit != null)
                weapons = hardpoint.part.parentUnit.GetComponentInChildren<WeaponManager>(true);
            if (weapons == null && hardpoint.transform != null)
                weapons = hardpoint.transform.GetComponentInParent<WeaponManager>();
            if (weapons == null && hardpoint.transform != null)
                weapons = hardpoint.transform.root.GetComponentInChildren<WeaponManager>(true);

            if (weapons == null || weapons.hardpointSets == null) return null;

            foreach (HardpointSet? set in weapons.hardpointSets)
            {
                if (set == null || set.hardpoints == null) continue;
                foreach (Hardpoint? h in set.hardpoints)
                    if (ReferenceEquals(h, hardpoint)) return set;
            }
            return null;
        }

        private static float RoundLength(Transform root, MountedMissile[] rounds)
        {
            float min = float.PositiveInfinity;
            float max = float.NegativeInfinity;
            bool any = false;

            foreach (Renderer r in RoundRenderers(rounds))
            {
                if (r == null) continue;

                Bounds b = r.bounds;
                foreach (Vector3 corner in Corners(b))
                {
                    float z = root.InverseTransformPoint(corner).z;
                    if (z < min) min = z;
                    if (z > max) max = z;
                    any = true;
                }
            }

            return any ? max - min : 0f;
        }

        private static IEnumerable<Vector3> Corners(Bounds b)
        {
            Vector3 c = b.center, e = b.extents;
            for (int i = 0; i < 8; i++)
                yield return c + new Vector3(
                    ((i & 1) == 0 ? -e.x : e.x),
                    ((i & 2) == 0 ? -e.y : e.y),
                    ((i & 4) == 0 ? -e.z : e.z));
        }

        private static bool DoorsSharedWithAnotherHardpoint(Hardpoint hardpoint)
        {

            WeaponManager? weapons = null;

            if (hardpoint.part != null && hardpoint.part.parentUnit != null)
                weapons = hardpoint.part.parentUnit.GetComponentInChildren<WeaponManager>(true);

            if (weapons == null && hardpoint.transform != null)
                weapons = hardpoint.transform.GetComponentInParent<WeaponManager>();

            if (weapons == null && hardpoint.transform != null)
                weapons = hardpoint.transform.root.GetComponentInChildren<WeaponManager>(true);

            if (weapons == null || weapons.hardpointSets == null)
            {
                if (_logged.Add(Where(hardpoint) + "|noairframe"))
                    Plugin.Log.LogWarning(
                        $"[Meridian] {Where(hardpoint)}: could not reach this hardpoint's own "
                        + "WeaponManager, so whether its bay doors are shared with another hardpoint "
                        + "is UNKNOWN, not false. The overlong-shove bound is what protects the "
                        + "seating here.");
                return false;
            }

            foreach (HardpointSet set in weapons.hardpointSets)
            {
                if (set?.hardpoints == null) continue;

                foreach (Hardpoint other in set.hardpoints)
                {
                    if (other == null || ReferenceEquals(other, hardpoint)) continue;
                    if (other.bayDoors == null) continue;

                    foreach (BayDoor theirs in other.bayDoors)
                    {
                        if (theirs == null) continue;

                        foreach (BayDoor ours in hardpoint.bayDoors)
                            if (ReferenceEquals(ours, theirs))
                                return true;
                    }
                }
            }

            return false;
        }

        private static void MeasureBayFit(Hardpoint hardpoint, WeaponMount mount,
                                          Transform root, MountedMissile[] rounds)
        {
            if (!_logged.Add(Where(hardpoint) + "|" + mount.jsonKey + "|fit")) return;

            if (hardpoint?.bayDoors == null || hardpoint.bayDoors.Length == 0) return;

            var doorRenderers = new List<Renderer>();
            foreach (BayDoor door in hardpoint.bayDoors)
            {
                if (door == null) continue;
                doorRenderers.AddRange(door.GetComponentsInChildren<Renderer>(true));
            }

            if (!LocalBounds(root, doorRenderers, out Bounds bay)) return;
            if (!LocalBounds(root, RoundRenderers(rounds), out Bounds block)) return;

            bool tooWide = block.size.x > bay.size.x;

            Plugin.Diag(
                $"[Meridian] {Where(hardpoint)} {mount.jsonKey}: bay fit " +
                $"{(tooWide ? "TOO WIDE" : "width ok")}. " +
                $"Block {block.size.x:0.000} wide x {block.size.z:0.000} long over " +
                $"{rounds.Length} round(s), standing {block.size.y:0.000} tall; " +
                $"bay doors span {bay.size.x:0.000} wide x {bay.size.z:0.000} long. " +
                $"Block bottom y={block.min.y:0.000}, door plane y={bay.center.y:0.000}.");
        }

        private static bool LocalBounds(Transform root, List<Renderer> renderers, out Bounds bounds)
        {
            bounds = default;
            bool any = false;

            foreach (Renderer r in renderers)
            {
                if (r == null || !r.enabled) continue;

                Mesh? mesh = (r as MeshRenderer) != null
                    ? r.GetComponent<MeshFilter>()?.sharedMesh
                    : (r as SkinnedMeshRenderer)?.sharedMesh;
                if (mesh == null) continue;

                Bounds mb = mesh.bounds;
                Matrix4x4 toWorld = r.localToWorldMatrix;

                for (int c = 0; c < 8; c++)
                {
                    var corner = new Vector3(
                        (c & 1) == 0 ? mb.min.x : mb.max.x,
                        (c & 2) == 0 ? mb.min.y : mb.max.y,
                        (c & 4) == 0 ? mb.min.z : mb.max.z);

                    Vector3 local = root.InverseTransformPoint(toWorld.MultiplyPoint3x4(corner));
                    if (!any) { bounds = new Bounds(local, Vector3.zero); any = true; }
                    else bounds.Encapsulate(local);
                }
            }

            return any;
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
                $"[Meridian] No single mount for '{PreferredDonorWeapon}' was found, so the pylon came "
                + $"from '{other.Key}' instead.");
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
                if (!StockContent.IsStock(m)) continue;

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

            Plugin.Diag(
                $"[Meridian] Pylon donors found: {found.Count}, " +
                $"including {string.Join(", ", found.Take(6).Select(d => $"'{d.Key}' ({d.WeaponName})").ToArray())}.");
            return found;
        }
    }
}
