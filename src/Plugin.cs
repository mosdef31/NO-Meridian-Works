using System;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace MeridianWorks
{

    [BepInPlugin(PluginInfo.GUID, PluginInfo.Name, PluginInfo.Version)]
    [BepInProcess("NuclearOption.exe")]

    [BepInDependency(PluginInfo.BlueprinterGUID)]
    public sealed class Plugin : BaseUnityPlugin
    {
        public static Plugin? Instance { get; private set; }
        public static ManualLogSource Log { get; private set; } = null!;

        internal static bool Diagnostics =>
            PluginConfig.DiagnosticsEntry != null && PluginConfig.DiagnosticsEntry.Value;

        internal static void Diag(string message)
        {
            if (Diagnostics) Log.LogInfo(message);
        }

        private Harmony? _harmony;

        private void Awake()
        {
            Instance = this;
            Log = Logger;

            PluginConfig.Bind(Config);

            _harmony = new Harmony(PluginInfo.GUID);
            _harmony.PatchAll();

            new GameObject(nameof(StartupRunner), typeof(StartupRunner))
            {
                hideFlags = HideFlags.HideAndDontSave
            };

            Log.LogInfo($"[Meridian] {PluginInfo.Name} {PluginInfo.Version} loaded.");
        }

        private void OnDestroy()
        {
            _harmony?.UnpatchSelf();
        }
    }

    internal sealed class StartupRunner : MonoBehaviour
    {
        private float _next = 2f;
        private int _attempts;

        private void Update()
        {
            if (_attempts > 30)
            {
                Plugin.Log.LogError(
                    "[Meridian] Gave up waiting for the Encyclopedia after 30 attempts. Nothing in " +
                    "this pack is registered, and no weapon will appear in any loadout.");
                enabled = false;
                return;
            }

            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + 2f;
            _attempts++;

            if (GameData.EncyclopediaOrNull() == null) return;

            try
            {
                EncyclopediaRegistration.EnsureRegisteredAndRebuild();

                WarheadEffects.RunOnce();
                NameGate.RunOnce();

                if (Plugin.Diagnostics)
                {
                    IconProbe.RunOnce();
                    LoadoutProbe.RunOnce();
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[Meridian] Startup runner failed: {ex}");
            }

            enabled = false;
        }
    }
}
