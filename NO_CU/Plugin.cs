using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;

namespace NO_CU;

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
public class Plugin : BaseUnityPlugin
{
    internal static PanelConfig GeneralChat = null!;
    internal static PanelConfig KillFeed = null!;
    
    internal static ConfigEntry<bool> CombineKillFeedWithGeneral = null!;
    
    internal static ConfigEntry<float> GeneralMessageBaseDuration = null!;
    internal static ConfigEntry<float> KillFeedBaseDuration = null!;
    internal static ConfigEntry<float> DurationPerCharacter = null!;
    
    internal static ConfigEntry<bool> HistoryEnabled = null!;
    internal static ConfigEntry<int> HistorySize = null!;
    
    internal new static ManualLogSource Logger { get; private set; } = null!;
    
    private Harmony? Harmony { get; set; }
    
    private void Awake()
    {
        Logger = base.Logger;
        
        GeneralChat = new PanelConfig(Config, "General Chat Messages", "general chat", ScreenCorner.TopLeft);
        
        KillFeed = new PanelConfig(Config, "Kill Feed", "kill feed", ScreenCorner.TopRight, defaultOffsetY: 150f);
        CombineKillFeedWithGeneral = Config.Bind("Kill Feed", "Combine With General Messages", false,
            "Put Kill Feed back inside the General Messages panel.\n" +
            "When enabled, Kill Feed specific position/sizing/visibility settings are ignored.");
        
        GeneralMessageBaseDuration = Config.Bind("Message Timing", "General Base Duration", 8f,
            new ConfigDescription("Base duration of general messages in seconds.",
                new AcceptableValueRange<float>(0f, 600f)));
        KillFeedBaseDuration = Config.Bind("Message Timing", "Kill Feed Base Duration", 5f,
            new ConfigDescription("Base duration of kill feed messages in seconds.",
                new AcceptableValueRange<float>(0f, 600f)));
        DurationPerCharacter = Config.Bind("Message Timing", "Duration Per Character", 0.1f,
            new ConfigDescription("Extra duration per character in message.",
                new AcceptableValueRange<float>(0f, 10f)));
        
        HistoryEnabled = Config.Bind("History", "Enabled", true,
            "Add scrollable history functionality to general chat and kill feed.");
        HistorySize = Config.Bind("History", "Size", 500,
            new ConfigDescription("Maximum number of history (in lines) to store for each panel.",
                new AcceptableValueRange<int>(10, 5000)));
        
        GeneralChat.OnChanged(ChatUI.ApplyLayout);
        KillFeed.OnChanged(ChatUI.ApplyLayout);
        
        WatchSettingChanges(CombineKillFeedWithGeneral, ChatUI.ApplyLayout);
        WatchSettingChanges(GeneralMessageBaseDuration, ChatUI.ApplyMessageDurations);
        WatchSettingChanges(KillFeedBaseDuration, ChatUI.ApplyMessageDurations);
        WatchSettingChanges(DurationPerCharacter, ChatUI.ApplyMessageDurations);
        WatchSettingChanges(HistoryEnabled, ChatUI.ApplyHistoryConfig);
        WatchSettingChanges(HistorySize, ChatUI.ApplyHistoryConfig);
        GeneralChat.OnVisibilityChanged(ChatUI.RefreshVisibility);
        KillFeed.OnVisibilityChanged(ChatUI.RefreshVisibility);
        
        Harmony = new Harmony(MyPluginInfo.PLUGIN_GUID);
        Repatch();
        
        Logger.LogInfo($"Plugin {MyPluginInfo.PLUGIN_GUID} is loaded!");
    }
    
    private void OnDestroy()
    {
        Harmony?.UnpatchSelf();
    }
    
    private void Repatch()
    {
        Harmony?.UnpatchSelf();
        Harmony?.PatchAll();
        Logger.LogInfo("Patching done!");
    }
    
    private static void WatchSettingChanges<T>(ConfigEntry<T> setting, Action action)
    {
        setting.SettingChanged += (_, _) => action();
    }
    
    internal enum ScreenCorner
    {
        TopLeft,
        TopRight,
        BottomLeft,
        BottomRight
    }
    
    internal class PanelConfig
    {
        internal readonly ConfigEntry<ScreenCorner> Corner;
        internal readonly ConfigEntry<bool> HideInAircraftSelection;
        internal readonly ConfigEntry<float> MaxWidth;
        internal readonly ConfigEntry<float> OffsetX;
        internal readonly ConfigEntry<float> OffsetY;
        internal readonly ConfigEntry<float> Scale;
        internal readonly ConfigEntry<bool> ToggleWithChatHold;
        internal readonly ConfigEntry<float> Width;
        
        internal PanelConfig(ConfigFile config, string section, string descriptionName, ScreenCorner defaultCorner,
            float defaultOffsetX = 0f, float defaultOffsetY = 0f)
        {
            Corner = config.Bind(section, "Corner", defaultCorner,
                $"Corner used by {descriptionName}.");
            OffsetX = config.Bind(section, "Offset X", defaultOffsetX,
                "Horizontal distance from screen edge at set corner position.");
            OffsetY = config.Bind(section, "Offset Y", defaultOffsetY,
                "Vertical distance from screen edge at set corner position.");
            Scale = config.Bind(section, "Scale", 1f,
                new ConfigDescription($"Scale of the {descriptionName} panel.",
                    new AcceptableValueRange<float>(0f, 5f)));
            Width = config.Bind(section, "Width", 0f,
                new ConfigDescription(
                    $"Width of the {descriptionName} panel. 0 means auto width based on current messages' length.",
                    new AcceptableValueRange<float>(0f, 8000f)));
            MaxWidth = config.Bind(section, "Max Width", 600f,
                new ConfigDescription(
                    "Maximum width used only while Width is 0 (auto). Setting this Max Width to 0 means no limit on auto width.",
                    new AcceptableValueRange<float>(0f, 8000f)));
            HideInAircraftSelection = config.Bind(section, "Hide In Aircraft Selection", false,
                $"Hide the {descriptionName} panel while aircraft selection/loadout menu is open.");
            ToggleWithChatHold = config.Bind(section, "Toggle With Chat Hold", false,
                $"Long pressing the Open Chat keybind toggles the {descriptionName} panel's visibility.");
        }
        
        internal void OnChanged(Action action)
        {
            WatchSettingChanges(Corner, action);
            WatchSettingChanges(OffsetX, action);
            WatchSettingChanges(OffsetY, action);
            WatchSettingChanges(Scale, action);
            WatchSettingChanges(Width, action);
            WatchSettingChanges(MaxWidth, action);
        }
        
        internal void OnVisibilityChanged(Action action)
        {
            WatchSettingChanges(HideInAircraftSelection, action);
            WatchSettingChanges(ToggleWithChatHold, action);
        }
    }
}

[HarmonyPatch]
internal static class HarmonyPatches
{
    [HarmonyPatch(typeof(MessageUI), nameof(MessageUI.Awake))]
    [HarmonyPostfix]
    // ReSharper disable once InconsistentNaming
    private static void MessageUIAwakePostfix(MessageUI __instance)
    {
        ChatUI.Setup(__instance);
    }
    
    [HarmonyPatch(typeof(MessageUI), nameof(MessageUI.OnDestroy))]
    [HarmonyPostfix]
    // ReSharper disable once InconsistentNaming
    private static void MessageUIOnDestroyPostfix(MessageUI __instance)
    {
        ChatUI.Destroy(__instance);
    }
    
    [HarmonyPatch(typeof(MessageUI), nameof(MessageUI.LateUpdate))]
    [HarmonyPostfix]
    // ReSharper disable once InconsistentNaming
    private static void MessageUILateUpdatePostfix(MessageUI __instance)
    {
        ChatUI.UpdatePanels(__instance);
    }
    
    [HarmonyPatch(typeof(MessageFeed), nameof(MessageFeed.Enqueue))]
    [HarmonyPostfix]
    // ReSharper disable once InconsistentNaming
    private static void MessageFeedEnqueuePostfix(MessageFeed __instance, string text)
    {
        ChatUI.Record(__instance, text);
    }
    
    [HarmonyPatch(typeof(MessageFeed), nameof(MessageFeed.RefreshUI))]
    [HarmonyPostfix]
    // ReSharper disable once InconsistentNaming
    private static void MessageFeedRefreshUIPostfix(MessageFeed __instance)
    {
        ChatUI.RefreshFeed(__instance);
    }
    
    [HarmonyPatch(typeof(ChatBox), nameof(ChatBox.OnEnable))]
    [HarmonyPostfix]
    // ReSharper disable once InconsistentNaming
    private static void ChatBoxOnEnablePostfix(ChatBox __instance)
    {
        if (__instance.gameObject.activeSelf)
            ChatUI.ChatOpened();
    }
    
    [HarmonyPatch(typeof(ChatBox), nameof(ChatBox.OnDisable))]
    [HarmonyPostfix]
    private static void ChatBoxOnDisablePostfix()
    {
        ChatUI.ChatClosed();
    }
    
    [HarmonyPatch(typeof(MessageUI), nameof(MessageUI.SetFixedBoxSize))]
    [HarmonyPostfix]
    private static void SetFixedBoxSizePostfix()
    {
        ChatUI.ForcePanelSize();
    }
    
    [HarmonyPatch(typeof(MessageUI), nameof(MessageUI.SetDynamicBoxSize))]
    [HarmonyPostfix]
    private static void SetDynamicBoxSizePostfix()
    {
        ChatUI.ForcePanelSize();
    }
    
    [HarmonyPatch(typeof(AircraftSelectionMenu), nameof(AircraftSelectionMenu.OnEnable))]
    [HarmonyPostfix]
    private static void AircraftSelectionMenuOnEnablePostfix()
    {
        ChatUI.AircraftSelectionChanged(true);
    }
    
    [HarmonyPatch(typeof(AircraftSelectionMenu), nameof(AircraftSelectionMenu.OnDestroy))]
    [HarmonyPostfix]
    private static void AircraftSelectionMenuOnDestroyPostfix()
    {
        ChatUI.AircraftSelectionChanged(false);
    }
    
    [HarmonyPatch(typeof(MessageUI), nameof(MessageUI.CheckChatBox))]
    [HarmonyPrefix]
    private static bool MessageUICheckChatBoxPrefix()
    {
        return !ChatUI.CheckOpenChat();
    }
}