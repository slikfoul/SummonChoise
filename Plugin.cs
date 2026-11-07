using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;

namespace SkeletonChoice
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    [BepInIncompatibility("local.valheim.skeletonselector")]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "Slikfoul.SkeletonChoice";
        public const string PluginName = "Summon Choise";
        public const string PluginVersion = "0.4.0";
        internal static Plugin Instance;
        internal ManualLogSource Log { get { return Logger; } }
        private Harmony harmony;
        private ConfigEntry<bool> modEnabled;
        private ConfigEntry<SummonMode> mode;
        private ConfigEntry<bool> debug;
        private ConfigEntry<bool> showSummonBuff;
        private ConfigEntry<SpiritMode> spirit;
        private ConfigEntry<bool> showSpiritBuff;
        private ConfigEntry<int> spiritFollowDistance;
        internal bool ModEnabled { get { return modEnabled.Value; } }
        internal SummonMode SelectedMode { get { return Enum.IsDefined(typeof(SummonMode), mode.Value) ? mode.Value : SummonMode.Random; } }
        internal bool DebugLogging { get { return debug.Value; } }
        internal bool ShowSummonBuff { get { return showSummonBuff.Value; } }
        internal SpiritMode SelectedSpirit { get { return Enum.IsDefined(typeof(SpiritMode), spirit.Value) ? spirit.Value : SpiritMode.Missing; } }
        internal bool ShowSpiritBuff { get { return showSpiritBuff.Value; } }
        internal int SpiritFollowDistanceMeters { get { return spiritFollowDistance.Value; } }

        private void Awake()
        {
            Instance = this;
            Config.SaveOnConfigSet = false;
            modEnabled = Config.Bind("1 - General", "Enabled", true, "Enable summon selection, spirit follow distance, and status indicators.");
            mode = Config.Bind("1 - General", "Skeleton type", SummonMode.Random, "Random: vanilla selection. Warrior: sword without a shield. Archer: bow without a shield. ShieldWarrior: sword and shield. Applies to future summons and persists across restarts.");
            showSummonBuff = Config.Bind("2 - Display", "Show summon buff", true, "Show a status icon with your active loaded skeleton count. Excludes skeletons that are dead, unloading, unsummoning, or beyond their vanilla follow-distance limit.");
            spirit = Config.Bind("1 - General", "Spirit Caller type", SpiritMode.Missing, "Missing: fill spirit slots in rounds (Bear, Moose, Wolf, Boar), up to the staff's limit per species. Random: vanilla selection. Select a species to summon only that spirit.");
            spiritFollowDistance = Config.Bind("1 - General", "Spirit follow distance", 6,
                new ConfigDescription("Distance in meters at which your Spirit Caller spirits stop following you outside combat. 3 uses the game's usual distance.", new AcceptableValueRange<int>(3, 10)));
            showSpiritBuff = Config.Bind("2 - Display", "Show spirit buff", true, "Show a separate status icon with your active Spirit Caller summon count.");
            debug = Config.Bind("3 - Diagnostics", "Log summons", false, "Log equipment selection for every summon.");
            RemoveLegacySetting("2 - Interface", "Menu key");
            RemoveLegacySetting("2 - Interface", "Show hint");
            Config.SaveOnConfigSet = true;
            Config.Save();
            try
            {
                SummonHooks.VerifyHooks();
                harmony = new Harmony(PluginGuid);
                harmony.PatchAll(typeof(Plugin).Assembly);
                Logger.LogInfo(PluginName + " " + PluginVersion + " loaded. Dead Raiser and Spirit Caller hooks verified.");
            }
            catch (Exception error)
            {
                if (harmony != null) harmony.UnpatchSelf();
                modEnabled.Value = false;
                Logger.LogError(PluginName + " disabled: summoning hooks are incompatible. " + error);
            }
        }

        private void RemoveLegacySetting(string section, string key)
        {
            // Binding consumes an orphaned old value; removing it keeps it out of future saves.
            var definition = new ConfigDefinition(section, key);
            Config.Bind<string>(definition, "", new ConfigDescription(""));
            Config.Remove(definition);
        }

        private void Update()
        {
            SummonIndicator.Update();
        }

        private void OnDestroy()
        {
            SummonIndicator.Clear();
            if (harmony != null) harmony.UnpatchSelf();
            SummonHooks.Clear();
            if (Instance == this) Instance = null;
        }
    }
}
