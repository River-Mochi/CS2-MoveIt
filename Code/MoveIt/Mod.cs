// <copyright>
// Copyright (c) Yenyang. MIT License See LICENSE.txt
// Forked with permission from Quboid's CS2-MoveIt project.
// </copyright>

// File: Mod.cs
// Purpose: Mod entry point (register settings, localization sources, and ECS update systems).

// #define EXPORT_EN_US
namespace MoveIt
{
    using Colossal;     // appears not used
    using Colossal.Localization;
    using Game;
    using Game.Net;
    using Game.SceneFlow;
    using Game.Tools;
    using MoveIt.Settings;  // appears not used
    using MoveIt.Systems;
    using MoveIt.Tool;      // appears not used
    using Newtonsoft.Json;  // appears not used
    using QCommonLib;
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Reflection;

    /// <summary>
    /// Mod entry point.
    /// </summary>
    public class Mod : Game.Modding.IMod
    {
        // UI and display names used in bindings and UI strings.
        public const string MOD_NAME = "Move It";
        public const string MOD_UI = "MoveIt";

        // Version formatting is intentionally different per configuration:
        // - Debug / Release: full 4-part version
        // - Stable: 3-part version (matches original behavior)
        //
        // Note: RELEASE is not an MSBuild default constant; it must be defined in the .csproj.
#if IS_DEBUG
        public const bool IS_BETA = true;
        public static string Version => Assembly.GetExecutingAssembly().GetName().Version.ToString(4);
#else
#if RELEASE
        public const bool IS_BETA = false;
        public static string Version => Assembly.GetExecutingAssembly().GetName().Version.ToString(4);
#else
        public const bool IS_BETA = false;
        public static string Version => Assembly.GetExecutingAssembly().GetName().Version.ToString(3);
#endif
#endif

        private static bool s_BannerLogged; // one-time banner in QLog.

        // Settings instance used by Options UI and other systems.
        public static Settings.Settings Settings;

        public void OnLoad(UpdateSystem updateSystem)
        {
            // Debug logging is enabled for beta builds.
            if (IS_BETA) QLog.Init(true);

   
            if (!s_BannerLogged)
            {
                s_BannerLogged = true;  // One-time log banner
                MoveItToolSystem.Log.Info($"[{MOD_UI}] OnLoad v{Version} (beta={IS_BETA})");
            }

            // Create and register settings for Options UI + keybindings.
            Settings = new Settings.Settings(this);
            Settings.RegisterKeyBindings();
            Settings.RegisterInOptionsUI();

            // LocalizationManager is expected to exist when mods load, but guard anyway to avoid hard crashes.
            GameManager gameManager = GameManager.instance;
            LocalizationManager localizationManager = gameManager?.localizationManager;

            if (localizationManager == null)
            {
                QLog.Error($"{nameof(OnLoad)} LocalizationManager was null; skipping localization registration.");
            }
            else
            {
                // Register English strings (code-driven locale).
                QLog.Info($"{nameof(OnLoad)} Initializing en-US localization.");
                localizationManager.AddSource("en-US", new Settings.LocaleEN(Settings));

                // Register other languages (embedded JSON resources).
                QLog.Info($"[{nameof(Mod)}] {nameof(OnLoad)} Initializing localization for other languages.");
                LoadNonEnglishLocalizations();
            }

#if DEBUG && EXPORT_EN_US
            // Developer-only export helper. Not used in normal builds.
            QLog.Info($"{nameof(Mod)}.{nameof(OnLoad)} Exporting localization");
            var localeDict = new LocaleEN(Settings).ReadEntries(new List<IDictionaryEntryError>(), new Dictionary<string, int>())
                .ToDictionary(pair => pair.Key, pair => pair.Value);
            var str = JsonConvert.SerializeObject(localeDict, Newtonsoft.Json.Formatting.Indented);

            try
            {
                File.WriteAllText($"C:\\Users\\TJ\\source\\repos\\CS2-MoveIt\\UI\\src\\lang\\en-US.json", str);
            }
            catch (Exception ex)
            {
                QLog.Error(ex.ToString());
            }

            try
            {
                File.WriteAllText($"C:\\Users\\TJ\\source\\repos\\CS2-MoveIt\\Code\\MoveIt\\l10n\\en-US.json", str);
            }
            catch (Exception ex)
            {
                QLog.Error(ex.ToString());
            }
#endif

            // Load persisted settings.
            // Note: original code passes a new Settings instance as defaults; kept unchanged.
            Colossal.IO.AssetDatabase.AssetDatabase.global.LoadSettings(nameof(MoveIt), Settings, new Settings.Settings(this));

            // Register systems and their update phases.
            // This controls when Move It runs relative to game simulation, tools, and rendering.
            updateSystem.UpdateAt<Tool.MoveItToolSystem>(SystemUpdatePhase.ToolUpdate);
            updateSystem.UpdateAt<MIT_InputSystem>(SystemUpdatePhase.PreTool);
            updateSystem.UpdateAt<MIT_PostToolSystem>(SystemUpdatePhase.PostTool);
            updateSystem.UpdateAt<Overlays.MIT_OverlaySystem>(SystemUpdatePhase.Rendering);
            updateSystem.UpdateAt<MIT_UISystem>(SystemUpdatePhase.UIUpdate);
            updateSystem.UpdateAt<MIT_ToolTipSystem>(SystemUpdatePhase.UITooltip);
            updateSystem.UpdateAfter<ApplyMoveItSystem, ApplyNetSystem>(SystemUpdatePhase.ApplyTool);
            updateSystem.UpdateBefore<RemoveSaveInstanceSystem, ApplyPrefabsSystem>(SystemUpdatePhase.ModificationEnd);
            updateSystem.UpdateBefore<TempNodeSystem, CompositionSelectSystem>(SystemUpdatePhase.Modification3);
            updateSystem.UpdateBefore<FindOriginalSystem, TempNodeSystem>(SystemUpdatePhase.Modification3);
        }

        public void OnDispose()
        {
            // Breadcrumb that mod is shutting down.
            Tool.MoveItToolSystem.Log?.Info(nameof(OnDispose));

            if (Settings != null)
            {
                Settings.UnregisterInOptionsUI();
                Settings = null;
            }
        }

        private void LoadNonEnglishLocalizations()
        {
            Assembly thisAssembly = Assembly.GetExecutingAssembly();
            string[] resourceNames = thisAssembly.GetManifestResourceNames();

            // LocalizationManager is required to register MemorySource translations.
            GameManager gameManager = GameManager.instance;
            LocalizationManager localizationManager = gameManager?.localizationManager;
            if (localizationManager == null)
            {
                QLog.Error($"{nameof(LoadNonEnglishLocalizations)} LocalizationManager was null; skipping embedded localization load.");
                return;
            }

            try
            {
                QLog.Debug("Reading localizations");

                foreach (string localeID in localizationManager.GetSupportedLocales())
                {
                    // Embedded resource naming convention:
                    //   <AssemblyName>.l10n.<localeID>.json
                    string resourceName = $"{thisAssembly.GetName().Name}.l10n.{localeID}.json";

                    if (resourceNames.Contains(resourceName))
                    {
                        QLog.Debug($"Found localization file {resourceName}");

                        try
                        {
                            QLog.Debug($"Reading embedded translation file {resourceName}");

                            // GetManifestResourceStream can return null if the name exists in list but stream is unavailable.
                            Stream stream = thisAssembly.GetManifestResourceStream(resourceName);
                            if (stream == null)
                            {
                                QLog.Error($"Localization stream was null for embedded file {resourceName}");
                                continue;
                            }

                            // Read embedded JSON and register as MemorySource.
                            using StreamReader reader = new(stream);
                            string entireFile = reader.ReadToEnd();

                            Colossal.Json.Variant variant = Colossal.Json.JSON.Load(entireFile);
                            Dictionary<string, string> translations = variant.Make<Dictionary<string, string>>();

                            localizationManager.AddSource(localeID, new MemorySource(translations));
                        }
                        catch (Exception e)
                        {
                            // Don't let a single locale failure block loading the mod.
                            QLog.Error(e, $"Exception reading localization from embedded file {resourceName}");
                        }
                    }
                    else
                    {
                        QLog.Debug($"Did not find localization file {resourceName}");
                    }
                }
            }
            catch (Exception e)
            {
                QLog.Error(e, "Exception reading embedded settings localization files");
            }
        }
    }
}
