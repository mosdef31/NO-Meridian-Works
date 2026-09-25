using System;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

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

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            HazeOpaqueTexture.RunForScene(scene.name);
        }

        private void Awake()
        {
            Instance = this;
            Log = Logger;

            try
            {
                PluginConfig.Bind(Config);
            }
            catch (Exception ex)
            {
                Log.LogError(
                    $"[Meridian] Config binding failed, so some toggles are on their " +
                    $"defaults this run. The rest of the pack is unaffected: {ex.Message}");
            }

            _harmony = new Harmony(PluginInfo.GUID);
            _harmony.PatchAll();

            new GameObject(nameof(StartupRunner), typeof(StartupRunner))
            {
                hideFlags = HideFlags.HideAndDontSave
            };

            SceneManager.sceneLoaded += OnSceneLoaded;

            new GameObject(nameof(HairpinPlacer), typeof(HairpinPlacer))
            {
                hideFlags = HideFlags.HideAndDontSave
            };

            new GameObject(nameof(EventGatePlacer), typeof(EventGatePlacer))
            {
                hideFlags = HideFlags.HideAndDontSave
            };

            Log.LogInfo(
                "[Meridian] Mount tweaks read from '" + MountTweak.Path + "'.");

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

            if (_attempts > 120)
            {
                Plugin.Log.LogError("[Meridian] Gave up after 120 attempts, about four minutes.");
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

                if (EncyclopediaRegistration.ResolvedMounts.Count == 0) return;

                NetworkHash.RunOnce();

                WarheadEffects.RunOnce();
                NameGate.RunOnce();

                AvailabilityGate.RunOnce();

                EventGate.Run();

                HairpinPod.Place();
                Pab125HdMirror.Place();

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
