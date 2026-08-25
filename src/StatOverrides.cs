using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace MeridianWorks
{

    internal static class StatOverrides
    {

        internal const string FileName = "meridian_tuning.json";

        private static bool _applied;

        private static readonly string[] Allowed =
        {
            "mass", "finArea", "torque", "supersonicDrag", "maxSpeed",
            "burnTime", "thrust", "impactFuseDelay", "blastYield", "pierceDamage",
        };

        internal static void ApplyIfPresent(IEnumerable<MissileDefinition> definitions)
        {
            if (_applied) return;
            _applied = true;

            string dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? ".";
            string path = Path.Combine(dir, FileName);

            if (!File.Exists(path))
            {
                Plugin.Log.LogDebug(
                    $"[Meridian] No {FileName} beside the DLL, so every flight number is the one the " +
                    "bundle shipped.");
                return;
            }

            Dictionary<string, Dictionary<string, float>> wanted;
            try
            {
                wanted = Parse(File.ReadAllText(path));
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError(
                    $"[Meridian] {FileName} could not be read, so NOTHING was overridden and the " +
                    $"bundle's own numbers stand: {ex.Message}");
                return;
            }

            Plugin.Log.LogWarning(
                $"[Meridian] TUNING FILE IN USE: {path}. The flight numbers below are NOT the ones " +
                "the bundle shipped with. This file must not be present in a release.");

            foreach (MissileDefinition def in definitions)
            {
                if (def == null || def.jsonKey == null) continue;
                if (!wanted.TryGetValue(def.jsonKey, out var fields)) continue;

                Missile? missile = MissileOn(def);
                if (missile == null)
                {
                    Plugin.Log.LogWarning(
                        $"[Meridian] {def.jsonKey}: the tuning file names it but its prefab carries no " +
                        "Missile component, so none of its values were applied.");
                    continue;
                }

                foreach (var kv in fields) ApplyOne(def.jsonKey, missile, kv.Key, kv.Value);
            }
        }

        private static void ApplyOne(string key, Missile missile, string field, float value)
        {
            if (Array.IndexOf(Allowed, field) < 0)
            {
                Plugin.Log.LogWarning(
                    $"[Meridian] {key}: '{field}' is not a tunable field, so it was ignored. " +
                    $"Tunable: {string.Join(", ", Allowed)}.");
                return;
            }

            foreach (object target in Targets(missile))
            {
                FieldInfo? f = AccessTools.Field(target.GetType(), field);
                if (f == null || (f.FieldType != typeof(float) && f.FieldType != typeof(int))) continue;

                object before = f.GetValue(target);

                if (f.FieldType == typeof(int)) f.SetValue(target, Mathf.RoundToInt(value));
                else f.SetValue(target, value);

                Plugin.Diag(
                    $"[Meridian] {key}: {field} {before} -> {f.GetValue(target)} " +
                    $"(on {target.GetType().Name}, from {FileName}).");
                return;
            }

            Plugin.Log.LogWarning(
                $"[Meridian] {key}: no float or int field called '{field}' on the Missile or its " +
                "motors, so it was ignored. Check the name against the field dump.");
        }

        private static IEnumerable<object> Targets(Missile missile)
        {
            yield return missile;

            if (AccessTools.Field(typeof(Missile), "motors")?.GetValue(missile) is Array motors)
            {
                foreach (object m in motors)
                    if (m != null) yield return m;
            }
        }

        private static Missile? MissileOn(MissileDefinition def)
        {

            FieldInfo? f = AccessTools.Field(typeof(MissileDefinition), "prefab");
            if (f?.GetValue(def) is GameObject go) return go.GetComponent<Missile>();
            return null;
        }

        private static Dictionary<string, Dictionary<string, float>> Parse(string text)
        {
            var result = new Dictionary<string, Dictionary<string, float>>();
            int i = 0;

            SkipTo(text, ref i, '{');
            i++;

            while (true)
            {
                string? key = NextString(text, ref i);
                if (key == null) break;

                SkipTo(text, ref i, ':');
                i++;
                SkipTo(text, ref i, '{');
                i++;

                var fields = new Dictionary<string, float>();
                while (true)
                {
                    int save = i;
                    string? field = NextStringBefore(text, ref i, '}');
                    if (field == null) { i = save; break; }

                    SkipTo(text, ref i, ':');
                    i++;

                    while (i < text.Length && (char.IsWhiteSpace(text[i]))) i++;
                    int start = i;
                    while (i < text.Length && (char.IsDigit(text[i]) || text[i] == '-' ||
                                               text[i] == '+' || text[i] == '.' ||
                                               text[i] == 'e' || text[i] == 'E')) i++;

                    string raw = text.Substring(start, i - start);
                    if (!float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out float v))
                        throw new FormatException($"'{raw}' after \"{field}\" is not a number");

                    fields[field] = v;
                }

                SkipTo(text, ref i, '}');
                i++;
                result[key] = fields;

                int peek = i;
                while (peek < text.Length && char.IsWhiteSpace(text[peek])) peek++;
                if (peek >= text.Length || text[peek] != ',') break;
                i = peek + 1;
            }

            if (result.Count == 0) throw new FormatException("no weapons found in the file");
            return result;
        }

        private static void SkipTo(string s, ref int i, char c)
        {
            while (i < s.Length && s[i] != c) i++;
            if (i >= s.Length) throw new FormatException($"expected '{c}' and reached the end");
        }

        private static string? NextString(string s, ref int i)
        {
            while (i < s.Length && s[i] != '"' && s[i] != '}') i++;
            if (i >= s.Length || s[i] == '}') return null;

            int start = ++i;
            while (i < s.Length && s[i] != '"') i++;
            return s.Substring(start, i++ - start);
        }

        private static string? NextStringBefore(string s, ref int i, char stop)
        {
            int j = i;
            while (j < s.Length && s[j] != '"' && s[j] != stop) j++;
            if (j >= s.Length || s[j] == stop) return null;

            i = j;
            return NextString(s, ref i);
        }
    }
}
