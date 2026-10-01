using System;
using HarmonyLib;
using UnityEngine;

namespace MeridianWorks
{

    [HarmonyPatch(typeof(Missile), "OnStartClient")]
    internal static class HSM160Submunition
    {
        private const string StockKey = "submunition1";
        internal const string DispenserKey = "MeridianHSM160C_Missile";
        internal const string NuclearDispenserKey = "MeridianHSM160M_Missile";
        private const string PrefabFile = "/meridianfx_hsm160cdart.prefab";
        private const string NuclearPrefabFile = "/meridianfx_hsm160mdart.prefab";

        private static GameObject? _prefab;
        private static GameObject? _nuclearPrefab;
        private static bool _looked;
        private static bool _logged;

        [HarmonyPostfix]
        private static void Postfix(Missile __instance)
        {
            try
            {
                string? parentKey = ParentKey(__instance);
                if (parentKey != DispenserKey && parentKey != NuclearDispenserKey) return;

                GameObject? prefab = Prefab(parentKey == NuclearDispenserKey);
                if (prefab == null) return;

                foreach (MeshRenderer r in __instance.GetComponentsInChildren<MeshRenderer>(true))
                    r.enabled = false;

                if (parentKey == DispenserKey) Rename(__instance);

                GameObject dart = UnityEngine.Object.Instantiate(prefab, __instance.transform, false);
                dart.transform.localPosition = Vector3.zero;
                dart.transform.localRotation = Quaternion.identity;
                dart.transform.localScale = Vector3.one * HSM160Stow.Scale(parentKey!);
                dart.AddComponent<DartPop>();

                if (!_logged)
                {
                    _logged = true;
                    Plugin.Log.LogInfo("[Meridian] HSM-160 dart dressed on a released submunition "
                        + "(stock mesh hidden, fins pop after " + DartPop.PopSeconds + " s).");
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("[Meridian] HSM-160C dart dressing failed, stock look kept: " + e.Message);
            }
        }

        internal static string? ParentKey(Missile m)
        {
            if ((m.definition as MissileDefinition)?.jsonKey is not string key
                || !string.Equals(key.Trim(), StockKey, StringComparison.OrdinalIgnoreCase))
                return null;
            if (!UnitRegistry.TryGetUnit(m.ownerID, out Unit owner) || owner is not Missile parent)
                return null;
            return (parent.definition as MissileDefinition)?.jsonKey;
        }

        internal const string DartName = "SD-6 Needle";
        internal const string DartShort = "Needle";

        internal const float SD6YieldKg = 60f;
        internal const float SD6Pierce  = 1000f;
        internal static readonly System.Reflection.FieldInfo? FInfo = AccessTools.Field(typeof(Missile), "info");
        private static WeaponInfo? _renamed;

        private static void Rename(Missile m)
        {
            if (FInfo?.GetValue(m) is not WeaponInfo stock) return;
            if (_renamed == null)
            {
                _renamed = UnityEngine.Object.Instantiate(stock);
                _renamed.name = stock.name + "_HSM160C";
                _renamed.weaponName = DartName;
                _renamed.shortName = DartShort;
                _renamed.pierceDamage = SD6Pierce;
                _renamed.blastDamage  = SD6YieldKg;
                _renamed.description = "A finned dart released by the HSM-160C. Each one steers onto its own target.";
            }
            FInfo.SetValue(m, _renamed);
        }

        internal static GameObject? Prefab(bool nuclear)
        {
            if (!_looked)
            {
                AssetBundle? bundle = EncyclopediaRegistration.OurBundle();
                if (bundle == null) return null;
                _looked = true;
                foreach (string name in bundle.GetAllAssetNames())
                {
                    if (name.EndsWith(PrefabFile, StringComparison.OrdinalIgnoreCase))
                        _prefab = bundle.LoadAsset<GameObject>(name);
                    else if (name.EndsWith(NuclearPrefabFile, StringComparison.OrdinalIgnoreCase))
                        _nuclearPrefab = bundle.LoadAsset<GameObject>(name);
                }
                if (_prefab == null)
                    Plugin.Log.LogWarning("[Meridian] HSM-160C dart prefab not in the bundle, submunitions keep the stock look.");
                if (_nuclearPrefab == null)
                    Plugin.Log.LogWarning("[Meridian] HSM-160M dart prefab not in the bundle, its darts wear the HSM-160C's.");
            }
            return nuclear && _nuclearPrefab != null ? _nuclearPrefab : _prefab;
        }
    }

    [HarmonyPatch(typeof(Missile), "StartMissile")]
    internal static class HSM160NuclearSubmunition
    {
        internal const float YieldKg = 5e6f;
        internal const string DartName = "SD-6N Needle";
        internal const string DartShort = "Needle-N";

        private const System.Reflection.BindingFlags Inst =
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic
            | System.Reflection.BindingFlags.Public;
        private static readonly System.Reflection.FieldInfo? FYield = AccessTools.Field(typeof(Missile), "blastYield");
        private static readonly System.Reflection.FieldInfo? FWarhead = AccessTools.Field(typeof(Missile), "warhead");

        private static readonly string[] EffectFields =
        {
            "terrainEffect", "armorEffect", "underwaterEffect",
            "airEffect", "waterSurfaceEffect", "fizzleEffect",
        };

        private static object? _donor;
        private static string _donorName = "";
        private static bool _looked;
        private static WeaponInfo? _info;
        private static bool _logged;

        [HarmonyPrefix]
        private static void Prefix(Missile __instance)
        {
            try
            {
                if (HSM160Submunition.ParentKey(__instance) != HSM160Submunition.NuclearDispenserKey) return;
                if (FYield == null || FWarhead == null) return;

                object? donor = Donor();
                object? warhead = FWarhead.GetValue(__instance);

                if (donor == null || warhead == null) return;

                foreach (string n in EffectFields)
                {
                    System.Reflection.FieldInfo? f = warhead.GetType().GetField(n, Inst);
                    if (f?.GetValue(donor) is GameObject fx && fx != null) f.SetValue(warhead, fx);
                }
                FWarhead.SetValue(__instance, warhead);
                FYield.SetValue(__instance, YieldKg);
                Rename(__instance);

                if (!_logged)
                {
                    _logged = true;
                    Plugin.Log.LogInfo($"[Meridian] {DartName}: 5 kt nuclear dart, blast from {_donorName}.");
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("[Meridian] HSM-160M dart warhead setup failed: " + e.Message);
            }
        }

        private static void Rename(Missile m)
        {
            if (HSM160Submunition.FInfo?.GetValue(m) is not WeaponInfo stock) return;
            if (_info == null)
            {
                _info = UnityEngine.Object.Instantiate(stock);
                _info.name = stock.name + "_HSM160M";
                _info.weaponName = DartName;
                _info.shortName = DartShort;
                _info.nuclear = true;
                _info.strategic = false;
                _info.blastDamage = YieldKg;
                _info.pierceDamage *= 2f;
                _info.description = "A 5 kt nuclear dart released by the HSM-160M. Each one steers onto its own target.";
            }
            HSM160Submunition.FInfo.SetValue(m, _info);
        }

        private static object? Donor()
        {
            if (_looked) return _donor;
            Encyclopedia? enc = GameData.EncyclopediaOrNull();
            if (enc?.missiles == null || FWarhead == null || FYield == null) return null;
            _looked = true;

            float best = 0f;
            foreach (MissileDefinition d in enc.missiles)
            {
                if (d == null || d.unitPrefab == null || !StockContent.IsStock(d)) continue;
                Missile? m = d.unitPrefab.GetComponent<Missile>();
                if (m == null || FYield.GetValue(m) is not float y || y <= 1e5f) continue;
                object? w = FWarhead.GetValue(m);
                if (w?.GetType().GetField("airEffect", Inst)?.GetValue(w) is not GameObject air || air == null
                    || air.GetComponentInChildren<Shockwave>(true) == null)
                    continue;
                float closeness = Mathf.Min(y, YieldKg) / Mathf.Max(y, YieldKg);
                if (closeness <= best) continue;
                best = closeness;
                _donor = w;
                _donorName = !string.IsNullOrEmpty(d.unitName) ? d.unitName : d.jsonKey;
            }

            if (_donor == null)
                Plugin.Log.LogWarning($"[Meridian] {DartName}: no stock nuclear blast found, darts stay GS25.");
            return _donor;
        }
    }

    internal sealed class DartPop : MonoBehaviour
    {
        internal const float PopSeconds = 0.25f;
        private float _at;

        private void Start() => _at = Time.time + PopSeconds;

        private void Update()
        {
            if (Time.time < _at) return;
            Transform? folded = transform.Find("Folded");
            Transform? open = transform.Find("Open");
            if (folded != null) folded.gameObject.SetActive(false);
            if (open != null) open.gameObject.SetActive(true);
            Destroy(this);
        }
    }
}
