using System;
using System.Collections.Generic;
using System.Text;
using Rewired;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace NO_CU;

internal static class ChatUI
{
    private const float FallbackPanelWidth = 600f;
    private const float HistoryLeaveDelay = 1.5f;
    private const float ScrollDeadzone = 0.1f;
    private const float ScrollRepeatDelay = 0.35f;
    private const float ScrollRepeatRate = 0.08f;
    
    private static Canvas? _canvas;
    private static Panel _generalChat = null!;
    private static Panel _killFeed = null!;
    
    private static MessageUI _ui = null!;
    private static Player? _player;
    
    private static int _originalKillFeedSiblingIndex;
    private static bool _combinedPanels;
    private static bool _chatOpen;
    private static bool _setup;
    private static bool _historyEnabled;
    
    private static long _sequence;
    
    private static int _heldScrollDirection;
    private static float _nextScrollRepeat;
    
    private static readonly List<HistoryEntry> HistoryRecord = [];
    private static readonly StringBuilder Builder = new();
    
    private static FeedType GeneralChatHistoryType => _combinedPanels ? FeedType.Combined : FeedType.General;
    
    internal static void Setup(MessageUI ui)
    {
        if (_setup)
            Destroy(_ui);
        
        var chatCanvas = FindParentTransform(ui.transform, "ChatCanvas") as RectTransform;
        if (chatCanvas == null)
        {
            var parentCanvas = ui.GetComponentInParent<Canvas>();
            chatCanvas = parentCanvas != null ? parentCanvas.transform as RectTransform : null;
        }
        
        if (chatCanvas == null)
        {
            Plugin.Logger.LogError("Could not find ChatCanvas (or a parent Canvas). Chat layout was not changed.");
            return;
        }
        
        var generalChatRoot = ui.messageBackground.transform as RectTransform;
        var killFeedTextRect = ui.killFeedText.rectTransform;
        
        if (generalChatRoot == null || killFeedTextRect == null)
        {
            Plugin.Logger.LogError("Could not find the right transforms in MessageUI. Chat layout was not changed.");
            return;
        }
        
        _ui = ui;
        _canvas = chatCanvas.GetComponentInParent<Canvas>();
        _player = ReInput.players.GetPlayer(0);
        
        var generalChatParentName = generalChatRoot.parent != null ? generalChatRoot.parent.name : "(null)";
        var killFeedParentName = killFeedTextRect.parent != null ? killFeedTextRect.parent.name : "(null)";
        var generalChatFallbackWidth = CaptureWidth(generalChatRoot);
        var killFeedFallbackWidth = CaptureWidth(killFeedTextRect);
        _originalKillFeedSiblingIndex = killFeedTextRect.GetSiblingIndex();
        
        // Move ChatBackground so ChatPanel and HQMessages stay in same group/panel/canvas
        generalChatRoot.SetParent(chatCanvas, false);
        generalChatRoot.localRotation = Quaternion.identity;
        generalChatRoot.SetAsLastSibling();
        
        // Separate Kill Feed panel from Chat Panel
        var killFeedRootObject = new GameObject("NOChatUI_KillFeedPanel",
            typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        var killFeedRoot = killFeedRootObject.GetComponent<RectTransform>();
        killFeedRoot.SetParent(chatCanvas, false);
        killFeedRoot.localRotation = Quaternion.identity;
        killFeedRoot.SetAsLastSibling();
        
        // Duplicate the Darkener background of Chat Canvas so that separate Kill Feed panel also gets one
        var killFeedBackground = killFeedRootObject.GetComponent<Image>();
        CopyBackgroundAppearance(ui.messageBackground, killFeedBackground);
        killFeedBackground.raycastTarget = false;
        
        var killFeedLayout = killFeedRootObject.GetComponent<VerticalLayoutGroup>();
        killFeedLayout.padding = new RectOffset(8, 8, 5, 5);
        killFeedLayout.spacing = 0f;
        killFeedLayout.childAlignment = TextAnchor.UpperLeft;
        killFeedLayout.childControlWidth = true;
        killFeedLayout.childControlHeight = true;
        killFeedLayout.childForceExpandWidth = true;
        killFeedLayout.childForceExpandHeight = false;
        
        var killFeedSizeFitter = killFeedRootObject.GetComponent<ContentSizeFitter>();
        killFeedSizeFitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        killFeedSizeFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        
        killFeedTextRect.SetParent(killFeedRoot, false);
        killFeedTextRect.localRotation = Quaternion.identity;
        killFeedTextRect.localScale = Vector3.one;
        
        MakeClickThrough(ui, killFeedBackground);
        
        var capacity = Mathf.Max(1, Plugin.HistorySize.Value);
        var generalHistoryText = CreateHistoryText(ui.messageText, generalChatRoot, "NOChatUI_GeneralHistory");
        var killHistoryText = CreateHistoryText(ui.killFeedText, killFeedRoot, "NOChatUI_KillFeedHistory");
        
        _generalChat = new Panel(Plugin.GeneralChat, generalChatRoot, ui.messageText, generalHistoryText,
            ui.messageFeed, ui.contentSizeFitter, generalChatRoot.GetComponent<HorizontalOrVerticalLayoutGroup>(),
            generalChatFallbackWidth, capacity);
        _killFeed = new Panel(Plugin.KillFeed, killFeedRoot, ui.killFeedText, killHistoryText, ui.killFeed,
            killFeedSizeFitter, killFeedLayout, killFeedFallbackWidth, capacity);
        
        _sequence = 0;
        _combinedPanels = false;
        _chatOpen = false;
        _historyEnabled = Plugin.HistoryEnabled.Value;
        _heldScrollDirection = 0;
        _nextScrollRepeat = 0f;
        _setup = true;
        _killFeed.Root.gameObject.SetActive(false);
        
        ApplyMessageDurations();
        ApplyLayout();
        
        Plugin.Logger.LogDebug(
            $"Initialised changed MessageUI.\nChatCanvas: {GetPath(chatCanvas)}" +
            $"\nGeneral chat original parent: {generalChatParentName}\nKillFeed original parent: {killFeedParentName}" +
            $"\nCaptured General chat width: {generalChatFallbackWidth:F1}\n Captured Kill Feed width: {killFeedFallbackWidth:F1}");
    }
    
    internal static void Destroy(MessageUI ui)
    {
        if (!_setup || _ui != ui)
            return;
        
        _setup = false;
        _chatOpen = false;
        _combinedPanels = false;
        _historyEnabled = false;
        _ui = null!;
        _generalChat = null!;
        _killFeed = null!;
        _canvas = null;
        _player = null!;
        HistoryRecord.Clear();
        Builder.Clear();
        ResetScrollRepeat();
    }
    
    internal static void UpdatePanels(MessageUI ui)
    {
        if (!_setup || _ui != ui)
            return;
        UpdateHistoryInteraction();
        RefreshPanelVisibility();
        RefreshChangedWidths();
    }
    
    internal static void ApplyLayout()
    {
        if (!_setup)
            return;
        
        var combineChanged = Plugin.CombineKillFeedWithGeneral.Value != _combinedPanels;
        if (combineChanged)
            HideAllHistories();
        
        ApplyCombinedState();
        ApplyPanelLocation(_generalChat, Plugin.GeneralChat.Corner.Value, Plugin.GeneralChat.OffsetX.Value,
            Plugin.GeneralChat.OffsetY.Value, Plugin.GeneralChat.Scale.Value);
        
        var generalChatRight = IsRightCorner(Plugin.GeneralChat.Corner.Value);
        SetTextAlignment(_generalChat, generalChatRight);
        
        if (_combinedPanels)
        {
            _killFeed.LiveText.alignment =
                generalChatRight ? TextAlignmentOptions.TopRight : TextAlignmentOptions.TopLeft;
        }
        else
        {
            ApplyPanelLocation(_killFeed, Plugin.KillFeed.Corner.Value, Plugin.KillFeed.OffsetX.Value,
                Plugin.KillFeed.OffsetY.Value, Plugin.KillFeed.Scale.Value);
            
            var killFeedRight = IsRightCorner(Plugin.KillFeed.Corner.Value);
            SetTextAlignment(_killFeed, killFeedRight);
            
            if (_killFeed.Layout != null)
                _killFeed.Layout.childAlignment = killFeedRight ? TextAnchor.UpperRight : TextAnchor.UpperLeft;
        }
        
        ApplyPanelWidth(_generalChat);
        
        if (!_combinedPanels)
            ApplyPanelWidth(_killFeed);
        
        MarkLayout(_generalChat);
        if (!_combinedPanels)
            MarkLayout(_killFeed);
        
        if (combineChanged && _chatOpen && _historyEnabled)
            EnterHistory(_generalChat, GeneralChatHistoryType);
        
        RefreshPanelVisibility();
    }
    
    internal static void ApplyMessageDurations()
    {
        if (!_setup)
            return;
        
        _ui.messageRemoveDelayBase = Plugin.GeneralMessageBaseDuration.Value;
        _ui.killFeedRemoveDelayBase = Plugin.KillFeedBaseDuration.Value;
        _ui.removeDelayPerCharacter = Plugin.DurationPerCharacter.Value;
    }
    
    internal static void ApplyHistoryConfig()
    {
        if (!_setup)
            return;
        
        var historyEnabled = Plugin.HistoryEnabled.Value;
        var historySize = Mathf.Max(1, Plugin.HistorySize.Value);
        _generalChat.History.Resize(historySize);
        _killFeed.History.Resize(historySize);
        
        if (!historyEnabled)
        {
            HideAllHistories();
            _generalChat.History.Clear();
            _killFeed.History.Clear();
        }
        else if (!_historyEnabled)
        {
            _generalChat.History.Clear();
            _killFeed.History.Clear();
        }
        
        _historyEnabled = historyEnabled;
        
        if (!_historyEnabled)
            return;
        
        if (_generalChat.HistoryActive)
            RenderHistory(_generalChat);
        
        if (_killFeed.HistoryActive)
            RenderHistory(_killFeed);
        
        if (_chatOpen)
            EnterHistory(_generalChat, GeneralChatHistoryType);
    }
    
    // AircraftSelectionMenu changes MessageUI's width, force resize it otherwise canvas parent defaults to 0
    // (which'd show one character per line)
    internal static void ForcePanelSize()
    {
        if (!_setup)
            return;
        
        ApplyPanelWidth(_generalChat);
        
        if (!_combinedPanels)
            ApplyPanelWidth(_killFeed);
        
        MarkLayout(_generalChat);
        if (!_combinedPanels)
            MarkLayout(_killFeed);
    }
    
    // Record a split line when it enters either vanilla MessageFeed
    internal static void Record(MessageFeed feed, string text)
    {
        if (!_setup || !_historyEnabled || string.IsNullOrEmpty(text))
            return;
        
        var panel = GetPanel(feed);
        if (panel == null)
            return;
        
        panel.History.Add(new HistoryEntry(++_sequence, text));
        
        // Show history if new message arrives while typing, in case there's no messages in history yet
        // this was an annoying edge case to find when testing it with only one (first) message >:c
        if (_chatOpen && !_generalChat.HistoryActive && (ReferenceEquals(panel, _generalChat) || _combinedPanels))
            EnterHistory(_generalChat, GeneralChatHistoryType);
        
        if (_generalChat is { HistoryActive: true, ScrollToLatest: true } &&
            (_generalChat.ActiveHistoryType & TypeFor(panel)) != 0)
            RenderHistory(_generalChat);
        
        if (_killFeed is { HistoryActive: true, ScrollToLatest: true } &&
            (_killFeed.ActiveHistoryType & TypeFor(panel)) != 0)
            RenderHistory(_killFeed);
    }
    
    // MessageFeed.RefreshUI triggers this on new message in queue, expiration, or trimming
    internal static void RefreshFeed(MessageFeed feed)
    {
        if (!_setup)
            return;
        
        if (ReferenceEquals(feed, _generalChat.Feed))
        {
            _generalChat.WidthChanged = true;
        }
        else if (ReferenceEquals(feed, _killFeed.Feed))
        {
            if (_combinedPanels)
                _generalChat.WidthChanged = true;
            else
                _killFeed.WidthChanged = true;
        }
    }
    
    internal static void ChatOpened()
    {
        if (!_setup)
            return;
        
        _chatOpen = true;
        _generalChat.WidthChanged = true;
        
        if (!_historyEnabled) return;
        
        _generalChat.LastOpenedTime = Time.unscaledTime;
        EnterHistory(_generalChat, GeneralChatHistoryType);
    }
    
    internal static void ChatClosed()
    {
        if (!_setup)
            return;
        
        _chatOpen = false;
        _generalChat.WidthChanged = true;
        
        if (_generalChat.HistoryActive)
            _generalChat.LastOpenedTime = Time.unscaledTime;
    }
    
    private static void UpdateHistoryInteraction()
    {
        if (!_historyEnabled)
            return;
        
        if (PlayerSettings.cinematicMode)
        {
            HideAllHistories();
            return;
        }
        
        var cursorVisible = CursorManager.Visible;
        var generalChatType = GeneralChatHistoryType;
        var hoverGeneralChat = cursorVisible && HasEntries(generalChatType) && ContainsPointer(_generalChat.Root);
        var hoverKillFeed = !_combinedPanels && !_chatOpen && cursorVisible && HasEntries(FeedType.KillFeed) &&
                            ContainsPointer(_killFeed.Root);
        
        if (!_chatOpen && hoverGeneralChat && hoverKillFeed)
        {
            if (_killFeed.Root.GetSiblingIndex() >= _generalChat.Root.GetSiblingIndex())
                hoverGeneralChat = false;
            else
                hoverKillFeed = false;
        }
        
        // Show general chat history both when hovering or when actively typing in chat
        var interactGeneralHistory = _chatOpen || hoverGeneralChat;
        
        UpdateHistoryView(_generalChat, generalChatType, interactGeneralHistory);
        UpdateHistoryView(_killFeed, FeedType.KillFeed, hoverKillFeed);
        
        Panel? scrollTarget = null;
        
        if ((_chatOpen && _generalChat.HistoryActive) || (hoverGeneralChat && _generalChat.HistoryActive))
            scrollTarget = _generalChat;
        else if (hoverKillFeed && _killFeed.HistoryActive)
            scrollTarget = _killFeed;
        
        if (scrollTarget == null)
        {
            ResetScrollRepeat();
            return;
        }
        
        var direction = ReadScrollDirection();
        if (direction != 0)
            ScrollHistory(scrollTarget, direction);
    }
    
    private static void UpdateHistoryView(Panel panel, FeedType feedType, bool interacting)
    {
        if ((ReferenceEquals(panel, _killFeed) && _combinedPanels) || !HasEntries(feedType))
        {
            HideHistory(panel);
            return;
        }
        
        if (interacting)
        {
            panel.LastOpenedTime = Time.unscaledTime;
            
            if (!panel.HistoryActive || panel.ActiveHistoryType != feedType)
                EnterHistory(panel, feedType);
            
            if (panel is { HistoryActive: true, ScrollToLatest: true } && panel.RenderedVersion != GetVersion(feedType))
                RenderHistory(panel);
            
            return;
        }
        
        if (panel.HistoryActive && Time.unscaledTime - panel.LastOpenedTime >= HistoryLeaveDelay)
            HideHistory(panel);
    }
    
    private static void EnterHistory(Panel panel, FeedType feedType)
    {
        if (!HasEntries(feedType))
            return;
        
        if (panel.HistoryActive && panel.ActiveHistoryType == feedType)
            return;
        
        if (panel.HistoryActive)
            HideHistory(panel);
        
        panel.HistoryActive = true;
        panel.ActiveHistoryType = feedType;
        panel.ScrollToLatest = true;
        panel.EndSequence = 0;
        panel.RenderedVersion = -1;
        panel.LastOpenedTime = Time.unscaledTime;
        SetLiveTextActive(feedType, false);
        panel.HistoryText.gameObject.SetActive(true);
        RenderHistory(panel);
        panel.WidthChanged = true;
    }
    
    private static void HideHistory(Panel? panel)
    {
        if (panel is not { HistoryActive: true })
            return;
        
        panel.HistoryText.gameObject.SetActive(false);
        SetLiveTextActive(panel.ActiveHistoryType, true);
        panel.HistoryActive = false;
        panel.ActiveHistoryType = default;
        panel.ScrollToLatest = true;
        panel.EndSequence = 0;
        panel.RenderedVersion = -1;
        panel.WidthChanged = true;
        MarkLayout(panel);
    }
    
    private static void HideAllHistories()
    {
        if (!_setup)
            return;
        
        HideHistory(_generalChat);
        HideHistory(_killFeed);
    }
    
    private static void SetLiveTextActive(FeedType feedType, bool active)
    {
        if ((feedType & FeedType.General) != 0)
            _generalChat.LiveText.gameObject.SetActive(active);
        
        if ((feedType & FeedType.KillFeed) != 0)
            _killFeed.LiveText.gameObject.SetActive(active);
    }
    
    // Reuse Zoom View and Field of View rewired binds
    // Put in some time gating so that a binary button down doesn't scroll it way too fast every frame
    private static int ReadScrollDirection()
    {
        if (_player == null)
            return 0;
        
        var axis = Mathf.Clamp(_player.GetAxis("Zoom View") + _player.GetAxis("FOV"), -1f, 1f);
        
        if (Mathf.Abs(axis) < ScrollDeadzone)
        {
            ResetScrollRepeat();
            return 0;
        }
        
        var direction = axis > 0f ? 1 : -1;
        var now = Time.unscaledTime;
        
        if (_heldScrollDirection != direction)
        {
            _heldScrollDirection = direction;
            _nextScrollRepeat = now + ScrollRepeatDelay;
            return direction;
        }
        
        if (now < _nextScrollRepeat)
            return 0;
        
        _nextScrollRepeat = now + ScrollRepeatRate;
        return direction;
    }
    
    private static void ResetScrollRepeat()
    {
        _heldScrollDirection = 0;
        _nextScrollRepeat = 0f;
    }
    
    private static void ScrollHistory(Panel panel, int direction)
    {
        BuildHistory(panel.ActiveHistoryType);
        if (HistoryRecord.Count == 0)
            return;
        
        var window = GetHistoryWindowSize();
        var minimumEnd = Mathf.Min(HistoryRecord.Count - 1, window - 1);
        var end = panel.ScrollToLatest ? HistoryRecord.Count - 1 : FindEndIndex(panel.EndSequence);
        end -= direction;
        end = Mathf.Clamp(end, minimumEnd, HistoryRecord.Count - 1);
        panel.ScrollToLatest = end == HistoryRecord.Count - 1;
        panel.EndSequence = HistoryRecord[end].Sequence;
        
        RenderBuiltHistory(panel, end);
    }
    
    private static void RenderHistory(Panel panel)
    {
        BuildHistory(panel.ActiveHistoryType);
        if (HistoryRecord.Count == 0)
            return;
        
        var end = panel.ScrollToLatest ? HistoryRecord.Count - 1 : FindEndIndex(panel.EndSequence);
        
        if (end < 0)
            end = HistoryRecord.Count - 1;
        
        panel.EndSequence = HistoryRecord[end].Sequence;
        RenderBuiltHistory(panel, end);
    }
    
    private static void RenderBuiltHistory(Panel panel, int end)
    {
        var window = GetHistoryWindowSize();
        var start = Mathf.Max(0, end - window + 1);
        
        Builder.Clear();
        
        for (var i = start; i <= end; i++)
        {
            if (Builder.Length > 0)
                Builder.Append('\n');
            
            Builder.Append(HistoryRecord[i].Text);
        }
        
        panel.HistoryText.SetText(Builder);
        panel.RenderedVersion = GetVersion(panel.ActiveHistoryType);
        panel.WidthChanged = true;
        
        MarkLayout(panel);
    }
    
    private static void BuildHistory(FeedType feedType)
    {
        HistoryRecord.Clear();
        
        switch (feedType)
        {
            case FeedType.General:
                AddHistory(_generalChat.History);
                return;
            case FeedType.KillFeed:
                AddHistory(_killFeed.History);
                return;
        }
        
        // Merge histories for combined panel mode
        var generalChatIndex = 0;
        var killFeedIndex = 0;
        
        while (generalChatIndex < _generalChat.History.Count || killFeedIndex < _killFeed.History.Count)
        {
            if (killFeedIndex >= _killFeed.History.Count)
            {
                HistoryRecord.Add(_generalChat.History[generalChatIndex++]);
                continue;
            }
            
            if (generalChatIndex >= _generalChat.History.Count)
            {
                HistoryRecord.Add(_killFeed.History[killFeedIndex++]);
                continue;
            }
            
            var generalChat = _generalChat.History[generalChatIndex];
            var killFeed = _killFeed.History[killFeedIndex];
            
            if (generalChat.Sequence <= killFeed.Sequence)
            {
                HistoryRecord.Add(generalChat);
                generalChatIndex++;
            }
            else
            {
                HistoryRecord.Add(killFeed);
                killFeedIndex++;
            }
        }
    }
    
    private static void AddHistory(HistoryBuffer history)
    {
        for (var i = 0; i < history.Count; i++)
            HistoryRecord.Add(history[i]);
    }
    
    private static int FindEndIndex(long sequence)
    {
        for (var i = HistoryRecord.Count - 1; i >= 0; i--)
            if (HistoryRecord[i].Sequence <= sequence)
                return i;
        
        return 0;
    }
    
    private static int GetHistoryWindowSize() => Mathf.Max(1, _ui.maxLines);
    
    private static bool HasEntries(FeedType feedType)
    {
        return feedType switch
        {
            FeedType.General => _generalChat.History.Count > 0,
            FeedType.KillFeed => _killFeed.History.Count > 0,
            _ => _generalChat.History.Count > 0 || _killFeed.History.Count > 0
        };
    }
    
    private static long GetVersion(FeedType feedType)
    {
        return feedType switch
        {
            FeedType.General => _generalChat.History.Version,
            FeedType.KillFeed => _killFeed.History.Version,
            _ => _generalChat.History.Version + _killFeed.History.Version
        };
    }
    
    private static FeedType TypeFor(Panel panel) =>
        ReferenceEquals(panel, _generalChat) ? FeedType.General : FeedType.KillFeed;
    
    private static Panel? GetPanel(MessageFeed feed)
    {
        if (ReferenceEquals(feed, _generalChat.Feed))
            return _generalChat;
        
        if (ReferenceEquals(feed, _killFeed.Feed))
            return _killFeed;
        
        return null;
    }
    
    private static void RefreshPanelVisibility()
    {
        var cinematic = PlayerSettings.cinematicMode;
        var generalChatVisible = !cinematic && (_ui.chat.gameObject.activeSelf || !_ui.messageFeed.NoText ||
                                                (_combinedPanels && !_ui.killFeed.NoText) ||
                                                _generalChat.HistoryActive);
        var killFeedVisible = !_combinedPanels && !cinematic && (!_ui.killFeed.NoText || _killFeed.HistoryActive);
        SetPanelVisible(_generalChat, generalChatVisible);
        SetPanelVisible(_killFeed, killFeedVisible);
    }
    
    private static void SetPanelVisible(Panel panel, bool visible)
    {
        if (panel.Root.gameObject.activeSelf == visible)
            return;
        
        panel.Root.gameObject.SetActive(visible);
        
        if (visible)
            LayoutRebuilder.ForceRebuildLayoutImmediate(panel.Root);
    }
    
    private static void ApplyCombinedState()
    {
        var combine = Plugin.CombineKillFeedWithGeneral.Value;
        
        if (combine)
        {
            if (_killFeed.LiveText.transform.parent != _generalChat.Root)
            {
                _killFeed.LiveText.rectTransform.SetParent(_generalChat.Root, false);
                var sibling = Mathf.Clamp(_originalKillFeedSiblingIndex, 0, _generalChat.Root.childCount - 1);
                _killFeed.LiveText.transform.SetSiblingIndex(sibling);
                _killFeed.LiveText.transform.localRotation = Quaternion.identity;
                _killFeed.LiveText.transform.localScale = Vector3.one;
            }
            
            _killFeed.Root.gameObject.SetActive(false);
        }
        else
        {
            if (_killFeed.LiveText.transform.parent != _killFeed.Root)
            {
                _killFeed.LiveText.rectTransform.SetParent(_killFeed.Root, false);
                _killFeed.LiveText.transform.localRotation = Quaternion.identity;
                _killFeed.LiveText.transform.localScale = Vector3.one;
            }
        }
        
        _combinedPanels = combine;
        _generalChat.WidthChanged = true;
        _killFeed.WidthChanged = true;
    }
    
    private static void ApplyPanelLocation(Panel panel, Plugin.ScreenCorner corner, float offsetX, float offsetY,
        float scale)
    {
        ApplyCorner(panel.Root, corner, offsetX, offsetY);
        panel.Root.localScale = Vector3.one * scale;
    }
    
    private static void SetTextAlignment(Panel panel, bool right)
    {
        var alignment = right ? TextAlignmentOptions.TopRight : TextAlignmentOptions.TopLeft;
        panel.LiveText.alignment = alignment;
        panel.HistoryText.alignment = alignment;
    }
    
    private static void RefreshChangedWidths()
    {
        if (_generalChat.WidthChanged)
            ApplyPanelWidth(_generalChat);
        
        if (!_combinedPanels && _killFeed.WidthChanged)
            ApplyPanelWidth(_killFeed);
    }
    
    private static void ApplyPanelWidth(Panel panel)
    {
        if (panel.SizeFitter != null)
            panel.SizeFitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        
        var fixedWidth = panel.Config.Width.Value;
        var maxAutoWidth = panel.Config.MaxWidth.Value;
        float width;
        
        if (fixedWidth > 0f)
        {
            width = fixedWidth;
        }
        else
        {
            width = CalculatePreferredWidth(panel);
            
            if (width <= 1f)
                width = panel.Root.rect.width > 1f ? panel.Root.rect.width : panel.FallbackWidth;
            
            if (maxAutoWidth > 0f)
                width = Mathf.Min(width, maxAutoWidth);
        }
        
        if (width <= 1f)
            width = panel.FallbackWidth;
        
        if (Mathf.Abs(panel.Root.rect.width - width) > 0.1f)
            panel.Root.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
        
        panel.WidthChanged = false;
        MarkLayout(panel);
    }
    
    private static float CalculatePreferredWidth(Panel panel)
    {
        var preferred = 0f;
        
        for (var i = 0; i < panel.Root.childCount; i++)
        {
            var child = panel.Root.GetChild(i);
            if (!child.gameObject.activeSelf)
                continue;
            
            var childRect = child as RectTransform;
            if (childRect == null)
                continue;
            
            var element = child.GetComponent<LayoutElement>();
            if (element != null && element.ignoreLayout)
                continue;
            
            preferred = Mathf.Max(preferred, LayoutUtility.GetPreferredWidth(childRect));
        }
        
        if (panel.Layout != null)
            preferred += panel.Layout.padding.left + panel.Layout.padding.right;
        
        return preferred;
    }
    
    private static void MarkLayout(Panel panel)
    {
        LayoutRebuilder.MarkLayoutForRebuild(panel.Root);
    }
    
    private static bool ContainsPointer(RectTransform panel)
    {
        Camera? camera = null;
        
        if (_canvas != null && _canvas.renderMode != RenderMode.ScreenSpaceOverlay)
            camera = _canvas.worldCamera;
        
        return RectTransformUtility.RectangleContainsScreenPoint(panel, Input.mousePosition, camera);
    }
    
    private static TextMeshProUGUI CreateHistoryText(TextMeshProUGUI source, RectTransform parent, string name)
    {
        var history = Object.Instantiate(source, parent);
        history.name = name;
        history.SetText(string.Empty);
        history.raycastTarget = false;
        history.transform.localRotation = Quaternion.identity;
        history.transform.localScale = Vector3.one;
        history.gameObject.SetActive(false);
        return history;
    }
    
    private static void MakeClickThrough(MessageUI ui, Image killBackground)
    {
        ui.messageText.raycastTarget = false;
        ui.killFeedText.raycastTarget = false;
        killBackground.raycastTarget = false;
        
        var generalBackground = ui.messageBackground.GetComponent<Image>();
        if (generalBackground != null)
            generalBackground.raycastTarget = false;
        
        var darkener = ui.messageBackground.transform.Find("Darkener");
        if (darkener == null)
            return;
        
        var darkenerImage = darkener.GetComponent<Image>();
        if (darkenerImage != null)
            darkenerImage.raycastTarget = false;
    }
    
    private static float CaptureWidth(RectTransform rect)
    {
        var width = rect.rect.width;
        
        if (width <= 1f)
            width = rect.sizeDelta.x;
        
        return width > 1f ? width : FallbackPanelWidth;
    }
    
    private static bool IsRightCorner(Plugin.ScreenCorner corner) =>
        corner is Plugin.ScreenCorner.TopRight or Plugin.ScreenCorner.BottomRight;
    
    private static void ApplyCorner(RectTransform rect, Plugin.ScreenCorner corner, float offsetX, float offsetY)
    {
        Vector2 anchor;
        Vector2 position;
        
        switch (corner)
        {
            case Plugin.ScreenCorner.TopRight:
                anchor = new Vector2(1f, 1f);
                position = new Vector2(-offsetX, -offsetY);
                break;
            
            case Plugin.ScreenCorner.BottomLeft:
                anchor = new Vector2(0f, 0f);
                position = new Vector2(offsetX, offsetY);
                break;
            
            case Plugin.ScreenCorner.BottomRight:
                anchor = new Vector2(1f, 0f);
                position = new Vector2(-offsetX, offsetY);
                break;
            
            case Plugin.ScreenCorner.TopLeft:
            default:
                anchor = new Vector2(0f, 1f);
                position = new Vector2(offsetX, -offsetY);
                break;
        }
        
        rect.anchorMin = anchor;
        rect.anchorMax = anchor;
        rect.pivot = anchor;
        rect.anchoredPosition = position;
    }
    
    private static Transform FindParentTransform(Transform start, string name)
    {
        for (var current = start; current != null; current = current.parent)
            if (current.name == name)
                return current;
        
        return null!;
    }
    
    private static void CopyBackgroundAppearance(GameObject sourceObject, Image destination)
    {
        var source = sourceObject.GetComponent<Image>();
        
        if (source == null)
        {
            var darkener = sourceObject.transform.Find("Darkener");
            if (darkener != null)
                source = darkener.GetComponent<Image>();
        }
        
        if (source == null)
        {
            destination.enabled = false;
            Plugin.Logger.LogWarning(
                "Could not find the vanilla chat background image. Kill feed panel will have no darkener background.");
            return;
        }
        
        destination.sprite = source.sprite;
        destination.overrideSprite = source.overrideSprite;
        destination.type = source.type;
        destination.preserveAspect = source.preserveAspect;
        destination.fillCenter = source.fillCenter;
        destination.fillMethod = source.fillMethod;
        destination.fillAmount = source.fillAmount;
        destination.fillClockwise = source.fillClockwise;
        destination.fillOrigin = source.fillOrigin;
        destination.color = source.color;
        destination.material = source.material;
    }
    
    private static string GetPath(Transform transform)
    {
        var path = transform.name;
        
        for (var parent = transform.parent; parent != null; parent = parent.parent)
            path = parent.name + "/" + path;
        
        return path;
    }
    
    [Flags]
    private enum FeedType
    {
        General = 1,
        KillFeed = 2,
        Combined = General | KillFeed
    }
    
    private readonly struct HistoryEntry
    {
        internal readonly long Sequence;
        internal readonly string Text;
        
        internal HistoryEntry(long sequence, string text)
        {
            Sequence = sequence;
            Text = text;
        }
    }
    
    private class HistoryBuffer
    {
        private HistoryEntry[] _entry;
        private int _start;
        
        internal HistoryBuffer(int capacity)
        {
            _entry = new HistoryEntry[Mathf.Max(1, capacity)];
        }
        
        internal int Count { get; private set; }
        internal long Version { get; private set; }
        
        internal HistoryEntry this[int index] => _entry[(_start + index) % _entry.Length];
        
        internal void Add(HistoryEntry entry)
        {
            if (Count < _entry.Length)
            {
                _entry[(_start + Count) % _entry.Length] = entry;
                Count++;
            }
            else
            {
                _entry[_start] = entry;
                _start = (_start + 1) % _entry.Length;
            }
            
            Version++;
        }
        
        internal void Clear()
        {
            _start = 0;
            Count = 0;
            Version++;
        }
        
        internal void Resize(int capacity)
        {
            capacity = Mathf.Max(1, capacity);
            if (capacity == _entry.Length)
                return;
            
            var keep = Mathf.Min(Count, capacity);
            var resized = new HistoryEntry[capacity];
            var first = Count - keep;
            
            for (var i = 0; i < keep; i++)
                resized[i] = this[first + i];
            
            _entry = resized;
            _start = 0;
            Count = keep;
            Version++;
        }
    }
    
    private class Panel
    {
        internal readonly Plugin.PanelConfig Config;
        internal readonly float FallbackWidth;
        internal readonly MessageFeed Feed;
        internal readonly HistoryBuffer History;
        internal readonly TextMeshProUGUI HistoryText;
        internal readonly HorizontalOrVerticalLayoutGroup? Layout;
        internal readonly TextMeshProUGUI LiveText;
        internal readonly RectTransform Root;
        internal readonly ContentSizeFitter? SizeFitter;
        internal FeedType ActiveHistoryType;
        internal long EndSequence;
        internal bool HistoryActive;
        internal float LastOpenedTime;
        internal long RenderedVersion = -1;
        internal bool ScrollToLatest = true;
        internal bool WidthChanged;
        
        internal Panel(Plugin.PanelConfig config, RectTransform root, TextMeshProUGUI liveText,
            TextMeshProUGUI historyText, MessageFeed feed, ContentSizeFitter? fitter,
            HorizontalOrVerticalLayoutGroup? layout, float fallbackWidth, int historyCapacity)
        {
            Config = config;
            Root = root;
            LiveText = liveText;
            HistoryText = historyText;
            Feed = feed;
            SizeFitter = fitter;
            Layout = layout;
            FallbackWidth = fallbackWidth;
            History = new HistoryBuffer(historyCapacity);
        }
    }
}