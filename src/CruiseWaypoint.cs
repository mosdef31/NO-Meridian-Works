using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using NuclearOption.Networking;
using UnityEngine;

namespace MeridianWorks
{

    internal static class CruiseWaypoint
    {

        private static readonly FieldInfo? _fUnitCommand =
            AccessTools.Field(typeof(Missile), "unitCommand");

        private static readonly FieldInfo? _fMissile =
            AccessTools.Field(typeof(MissileSeeker), "missile");

        private static readonly FieldInfo? _fAimPos =
            AccessTools.Field(typeof(OpticalSeekerCruiseMissile), "aimPos");

        private static readonly HashSet<int> _subscribed = new HashSet<int>();

        private static bool _warned;

        private sealed class Route
        {
            internal readonly List<GlobalPosition> Legs = new List<GlobalPosition>();
            internal int Index;

            internal bool Live => Index < Legs.Count;
            internal GlobalPosition Current => Legs[Index];
        }

        private static readonly Dictionary<int, Route> _routes = new Dictionary<int, Route>();

        private static readonly Dictionary<int, Player> _launchedBy = new Dictionary<int, Player>();

        private const float SameLeg = 1f;

        private const float TooClose = 250f;

        private static bool Ready =>
            _fUnitCommand != null && _fMissile != null && _fAimPos != null;

        internal static void Apply(IList<MissileDefinition> defs)
        {
            if (!Ready)
            {
                Plugin.Log.LogWarning(
                    "[Meridian] Cruise waypoints: a field this needs was not found on this "
                    + "game version, so the sea-skimmers stay uncommandable. Missing: "
                    + (_fUnitCommand == null ? "Missile.unitCommand " : "")
                    + (_fMissile == null ? "MissileSeeker.missile " : "")
                    + (_fAimPos == null ? "OpticalSeekerCruiseMissile.aimPos" : ""));
                return;
            }

            int added = 0, already = 0, skipped = 0;

            foreach (MissileDefinition def in defs)
            {
                if (def == null) continue;
                if (!PluginInfo.IsOurMissileKey(def.jsonKey)) continue;

                GameObject? prefab = def.unitPrefab;
                if (prefab == null) continue;

                Missile? missile = prefab.GetComponent<Missile>();
                if (missile == null) continue;

                if (prefab.GetComponent<OpticalSeekerCruiseMissile>() == null)
                {
                    skipped++;
                    continue;
                }

                UnitCommand? command = prefab.GetComponent<UnitCommand>();
                if (command != null)
                {

                    if (_fUnitCommand!.GetValue(missile) == null)
                        _fUnitCommand.SetValue(missile, command);
                    already++;
                    continue;
                }

                command = prefab.AddComponent<UnitCommand>();
                if (command == null)
                {
                    Plugin.Log.LogWarning(
                        "[Meridian] Cruise waypoints: '" + def.jsonKey + "' would not take a "
                        + "UnitCommand, so it cannot be given a waypoint from the map.");
                    continue;
                }

                _fUnitCommand!.SetValue(missile, command);
                added++;

                Plugin.Diag(
                    "[Meridian] Cruise waypoints: '" + def.jsonKey + "' now carries a "
                    + "UnitCommand, bound to its Missile. It can be selected on the map and "
                    + "given a destination like a stock AShM.");
            }

            if (added > 0 || already > 0)
                Plugin.Log.LogInfo(
                    "[Meridian] Cruise waypoints: " + added + " cruise prefab(s) given a "
                    + "UnitCommand (" + already + " already had one, " + skipped
                    + " of our rounds are not cruise rounds and were left alone). The engine "
                    + "never subscribes the seeker's own destination handler, so the "
                    + "subscription is ours - see CruiseWaypoint.");
        }

        internal static void Attach(OpticalSeekerCruiseMissile seeker)
        {
            if (!Ready || seeker == null) return;

            if (_fMissile!.GetValue(seeker) is not Missile missile || missile == null) return;

            string key = (missile.definition as MissileDefinition)?.jsonKey ?? "";
            if (!PluginInfo.IsOurMissileKey(key)) return;

            if (!missile.IsServer) return;

            UnitCommand? command = missile.UnitCommand;
            if (command == null)
            {
                if (!_warned)
                {
                    _warned = true;
                    Plugin.Log.LogWarning(
                        "[Meridian] Cruise waypoints: '" + key + "' reached the air with no "
                        + "UnitCommand bound, so it cannot be given a destination. The prefab "
                        + "pass must run before the first round spawns - see "
                        + "EncyclopediaRegistration.");
                }
                return;
            }

            int id = seeker.GetInstanceID();
            if (!_subscribed.Add(id)) return;

            Remember(id, missile);

            OpticalSeekerCruiseMissile captured = seeker;
            Missile capturedMissile = missile;
            UnitCommand.ProcessCommand handler =
                (ref UnitCommand.Command c) => ApplyTo(captured, capturedMissile, c);

            command.ProcessSetDestination += handler;

            missile.onDisableUnit += _ =>
            {
                command.ProcessSetDestination -= handler;
                _subscribed.Remove(id);
                _routes.Remove(id);
                _launchedBy.Remove(id);
            };

            Plugin.Diag(
                "[Meridian] Cruise waypoints: '" + key + "' subscribed its destination "
                + "handler. The engine declares one on the seeker and never wires it.");
        }

        internal static void ApplyTo(OpticalSeekerCruiseMissile seeker,
                                     Missile missile,
                                     UnitCommand.Command command)
        {
            if (!Ready || seeker == null || missile == null) return;

            if (command.FromPlayer && !MayCommand(seeker, missile, command.player))
            {
                Plugin.Diag(
                    "[Meridian] Cruise waypoints: refused a destination for '"
                    + missile.unitName + "' - the player who gave it did not launch the round.");
                return;
            }

            GlobalPosition here = missile.GlobalPosition();

            if (FastMath.InRange(here, command.position, TooClose))
            {
                Plugin.Diag(
                    "[Meridian] Cruise waypoints: refused a destination for '"
                    + missile.unitName + "' - it is inside " + TooClose
                    + " m of the round and means nothing at that range.");
                return;
            }

            Route route = Adopt(seeker, command);

            GlobalPosition aim = route.Live ? route.Current : command.position;

            aim.y = Mathf.Max(aim.y, 1f);

            _fAimPos!.SetValue(seeker, aim);

            Plugin.Diag(
                "[Meridian] Cruise waypoints: '" + missile.unitName + "' is running to "
                + aim + (route.Legs.Count > 1
                    ? " - leg " + (route.Index + 1) + " of " + route.Legs.Count + "."
                    : ".")
                + " It reverts to its target on its own once the last leg is reached or "
                + "overflown - PreTerminalMode, CheckWaypoint.");
        }

        private static void Remember(int id, Missile missile)
        {
            if (missile == null) return;
            if (_launchedBy.ContainsKey(id)) return;

            if (missile.owner is Aircraft aircraft && aircraft != null)
            {
                Player? player = aircraft.Player;
                if (player != null) _launchedBy[id] = player;
            }
        }

        private static bool MayCommand(OpticalSeekerCruiseMissile seeker, Missile missile, Player player)
        {
            if (player == null) return false;

            int id = seeker.GetInstanceID();
            Remember(id, missile);

            if (_launchedBy.TryGetValue(id, out Player launcher) && launcher != null)
                return launcher == player;

            return player.Aircraft != null && player.Aircraft == missile.owner;
        }

        private static Route Adopt(OpticalSeekerCruiseMissile seeker, UnitCommand.Command command)
        {
            int id = seeker.GetInstanceID();
            if (!_routes.TryGetValue(id, out Route route))
            {
                route = new Route();
                _routes[id] = route;
            }

            List<GlobalPosition>? drawn = DrawnRoute(command);

            if (drawn == null || drawn.Count == 0)
            {

                route.Legs.Clear();
                route.Legs.Add(command.position);
                route.Index = 0;
                return route;
            }

            bool extension = route.Legs.Count > 0 && drawn.Count >= route.Legs.Count;
            if (extension)
            {
                for (int i = 0; i < route.Legs.Count; i++)
                {
                    if ((drawn[i] - route.Legs[i]).sqrMagnitude <= SameLeg * SameLeg) continue;
                    extension = false;
                    break;
                }
            }

            int keep = extension ? route.Index : 0;

            route.Legs.Clear();
            route.Legs.AddRange(drawn);
            route.Index = Mathf.Clamp(keep, 0, route.Legs.Count - 1);

            return route;
        }

        private static List<GlobalPosition>? DrawnRoute(UnitCommand.Command command)
        {
            if (!command.FromPlayer) return null;

            if (!GameManager.GetLocalPlayer<Player>(out Player? local)
                || local == null || local != command.player) return null;

            DynamicMap? map = SceneSingleton<DynamicMap>.i;
            return map != null ? map.constructWaypoints : null;
        }

        internal static void Advance(OpticalSeekerCruiseMissile seeker)
        {
            if (!Ready || seeker == null) return;
            if (!_routes.TryGetValue(seeker.GetInstanceID(), out Route route)) return;
            if (!route.Live) return;

            if (route.Index >= route.Legs.Count - 1) return;

            if (_fMissile!.GetValue(seeker) is not Missile missile || missile == null) return;
            if (missile.rb == null) return;

            GlobalPosition here = missile.GlobalPosition();
            GlobalPosition leg = route.Current;

            bool reached = FastMath.InRange(here, leg, 1000f)
                           || Vector3.Dot(leg - here, missile.rb.velocity) < 0f;
            if (!reached) return;

            route.Index++;

            GlobalPosition next = route.Current;
            next.y = Mathf.Max(next.y, 1f);
            _fAimPos!.SetValue(seeker, next);

            Plugin.Diag(
                "[Meridian] Cruise waypoints: '" + missile.unitName + "' reached leg "
                + route.Index + " and is running to leg " + (route.Index + 1) + " of "
                + route.Legs.Count + ", " + next + ".");
        }
    }

    [HarmonyPatch(typeof(OpticalSeekerCruiseMissile), "PreTerminalMode")]
    internal static class OpticalSeekerCruiseMissile_PreTerminalMode_RoutePatch
    {
        private static void Prefix(OpticalSeekerCruiseMissile __instance)
        {
            try { CruiseWaypoint.Advance(__instance); }
            catch (Exception e)
            {

                Plugin.Log.LogWarning(
                    "[Meridian] Cruise waypoint route step threw and was skipped: "
                    + e.Message);
            }
        }
    }

    [HarmonyPatch(typeof(OpticalSeekerCruiseMissile), "Initialize")]
    internal static class OpticalSeekerCruiseMissile_Initialize_WaypointPatch
    {
        private static void Postfix(OpticalSeekerCruiseMissile __instance)
        {
            try { CruiseWaypoint.Attach(__instance); }
            catch (Exception e)
            {

                Plugin.Log.LogWarning(
                    "[Meridian] Cruise waypoint subscription threw and was skipped: "
                    + e.Message);
            }
        }
    }
}
