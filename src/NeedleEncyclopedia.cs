using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace MeridianWorks
{

    internal static class NeedleEncyclopedia
    {
        private const string StockKey = "submunition1";
        internal const string NeedleKey = "MeridianSD6Needle_Encyclopedia";
        internal const string NeedleNKey = "MeridianSD6NNeedle_Encyclopedia";
        private const string PrefabFile = "/meridianfx_hsm160cdart.prefab";
        private const string NuclearPrefabFile = "/meridianfx_hsm160mdart.prefab";

        private static readonly List<MissileDefinition> _defs = new List<MissileDefinition>();
        private static bool _built, _failed;
        private static Transform? _park;

        internal static IList<MissileDefinition> Definitions => _defs;

        internal static IList<MissileDefinition> Build(Encyclopedia enc)
        {
            if (_built || _failed) return _defs;
            try
            {
                MissileDefinition? donor = null;
                if (enc?.missiles != null)
                    foreach (MissileDefinition d in enc.missiles)
                        if (d != null && string.Equals(d.jsonKey?.Trim(), StockKey, StringComparison.OrdinalIgnoreCase)
                            && StockContent.IsStock(d)) { donor = d; break; }
                AssetBundle? bundle = EncyclopediaRegistration.OurBundle();
                if (donor == null || donor.unitPrefab == null || bundle == null) return _defs;

                GameObject? dart = null, dartN = null;
                foreach (string name in bundle.GetAllAssetNames())
                {
                    if (name.EndsWith(PrefabFile, StringComparison.OrdinalIgnoreCase)) dart = bundle.LoadAsset<GameObject>(name);
                    else if (name.EndsWith(NuclearPrefabFile, StringComparison.OrdinalIgnoreCase)) dartN = bundle.LoadAsset<GameObject>(name);
                }
                if (dart == null) { _failed = true; Plugin.Log.LogWarning("[Meridian] Needle encyclopedia: no dart prefab, not listed."); return _defs; }
                _built = true;

                Missile stockMissile = donor.unitPrefab.GetComponent<Missile>();
                float stockYield = FYield?.GetValue(stockMissile) is float y ? y : 250f;

                _defs.Add(Clone(donor, NeedleKey, dart, HSM160Submunition.DartName, HSM160Submunition.DartShort,
                    "A finned dart carried six to a round by the HSM-160C Scatter. Released during the carrier's "
                    + "Mach 5 dive, each dart steers onto its own target, prefers ships, and moves off a target "
                    + "that already has enough rounds on it. Hardened nose for ship armour.",
                    stockYield * 2f, nuclear: false));
                _defs.Add(Clone(donor, NeedleNKey, dartN ?? dart, HSM160NuclearSubmunition.DartName, HSM160NuclearSubmunition.DartShort,
                    "The nuclear dart of the HSM-160M Hailstorm, four to a round. Each carries a 5 kt warhead "
                    + "and steers onto a separate target in the group, so one launch can break up a whole "
                    + "formation of ships.",
                    HSM160NuclearSubmunition.YieldKg, nuclear: true));

                Plugin.Log.LogInfo($"[Meridian] Needle encyclopedia: {_defs.Count} dart page(s) built from {donor.jsonKey}.");
            }
            catch (Exception e)
            {
                _failed = true;
                Plugin.Log.LogWarning("[Meridian] Needle encyclopedia failed, not listed: " + e.Message);
            }
            return _defs;
        }

        private static readonly System.Reflection.FieldInfo? FYield = AccessTools.Field(typeof(Missile), "blastYield");
        private static readonly System.Reflection.FieldInfo? FPierce = AccessTools.Field(typeof(Missile), "pierceDamage");
        private static readonly System.Reflection.FieldInfo? FDisabled = AccessTools.Field(typeof(UnitDefinition), "disabled");
        private static readonly System.Reflection.FieldInfo? FEvent = AccessTools.Field(typeof(UnitDefinition), "isEventContent");
        private static readonly System.Reflection.FieldInfo? FDefinition = AccessTools.Field(typeof(Missile), "definition");

        private static MissileDefinition Clone(MissileDefinition donor, string key, GameObject dart, string name, string shortName,
            string description, float yieldKg, bool nuclear)
        {
            MissileDefinition def = UnityEngine.Object.Instantiate(donor);
            def.name = key;
            UnityEngine.Object.DontDestroyOnLoad(def);
            def.jsonKey = key;
            def.unitName = name;
            def.code = shortName;
            def.description = description;
            FDisabled?.SetValue(def, false);
            FEvent?.SetValue(def, false);

            GameObject prefab = UnityEngine.Object.Instantiate(donor.unitPrefab, Park());
            prefab.name = key;
            UnityEngine.Object.DontDestroyOnLoad(prefab);

            Missile m = prefab.GetComponent<Missile>();
            FDefinition?.SetValue(m, def);

            bool sd6 = key == NeedleKey;
            if (sd6) yieldKg = HSM160Submunition.SD6YieldKg;
            FYield?.SetValue(m, yieldKg);
            if (sd6) FPierce?.SetValue(m, HSM160Submunition.SD6Pierce);
            else if (FPierce?.GetValue(m) is float p) FPierce.SetValue(m, p * 2f);

            if (HSM160Submunition.FInfo?.GetValue(m) is WeaponInfo stock)
            {
                WeaponInfo info = UnityEngine.Object.Instantiate(stock);
                info.name = key + "_info";
                UnityEngine.Object.DontDestroyOnLoad(info);
                info.weaponName = name;
                info.shortName = shortName;
                info.description = description;
                info.pierceDamage = sd6 ? HSM160Submunition.SD6Pierce : info.pierceDamage * 2f;
                info.blastDamage = yieldKg;
                info.nuclear = nuclear;
                info.strategic = false;
                HSM160Submunition.FInfo.SetValue(m, info);
            }

            foreach (MeshRenderer r in prefab.GetComponentsInChildren<MeshRenderer>(true)) r.enabled = false;
            GameObject look = UnityEngine.Object.Instantiate(dart, prefab.transform, false);
            look.transform.localPosition = Vector3.zero;
            look.transform.localRotation = Quaternion.identity;
            Transform? folded = look.transform.Find("Folded");
            Transform? open = look.transform.Find("Open");
            if (folded != null) folded.gameObject.SetActive(false);
            if (open != null) open.gameObject.SetActive(true);

            def.unitPrefab = prefab;
            return def;
        }

        private static Transform Park()
        {
            if (_park != null) return _park;
            var holder = new GameObject("MeridianWorks_NeedlePrefabs") { hideFlags = HideFlags.HideAndDontSave };
            UnityEngine.Object.DontDestroyOnLoad(holder);
            holder.SetActive(false);
            _park = holder.transform;
            return _park;
        }
    }
}
