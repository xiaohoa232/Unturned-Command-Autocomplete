using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using SDG.Framework.Modules;
using SDG.Unturned;
using TMPro;
using UnityEngine;

[assembly: AssemblyVersion("0.1.6.0")]

namespace UnturnedCommandAutocomplete
{
    public sealed class Nexus : IModuleNexus
    {
        private GameObject host;
        public void initialize()
        {
            host = new GameObject("UnturnedCommandAutocomplete");
            UnityEngine.Object.DontDestroyOnLoad(host);
            host.AddComponent<AutocompleteBehaviour>();
            Debug.Log("[CommandAutocomplete] 0.1.6 initialized (client only, no network messages)");
        }
        public void shutdown() { if (host != null) UnityEngine.Object.Destroy(host); }
    }

    public sealed class AutocompleteBehaviour : MonoBehaviour
    {
        private const float RowLeft = 10, RowTop = 68, RowHeight = 26;
        private const float TrackWidth = 12, TrackGap = 4;
        private static readonly Color ClearRowBackground = new Color(0f, 0f, 0f, 0f);
        private static readonly Color HoverRowBackground = new Color(0.38f, 0.26f, 0.11f, 0.96f);
        private readonly CompletionEngine engine = new CompletionEngine();
        private readonly CompletionSession session = new CompletionSession();
        private readonly PanelPlacement placement = new PanelPlacement();
        private readonly CandidateViewport viewport = new CandidateViewport();
        private ISleekField attached;
        private ISleekBox panel;
        private ISleekLabel hint, footer;
        private ISleekButton dragHandle, pinButton;
        private RectTransform parentTransform;
        private ISleekButton[] rows;
        private TextMeshProUGUI[] rowText;
        private ISleekButton track;
        private ISleekImage thumb;
        private bool dirty = true, wasChatting;
        private Player lastPlayer;
        private float nextPeopleRefresh, retryAt;
        private int lastSelectionFrame = -1;
        private string pendingCaretText;
        private int pendingCaretUntil;
        private CompletionResult paintedResult;
        private int paintedSelection = -2, paintedStart = -1, paintedCount = -1;
        private float paintedRowWidth = -1;
        private int hoveredRow = -1;
        private bool? paintedPin;
        private CompletionResult pressedResult;
        private int pressedStart, pressedViewRevision;

        private void Awake()
        {
            engine.IsPlayer = delegate(string value) { SteamPlayer p; return !string.IsNullOrEmpty(value) && PlayerTool.tryGetSteamPlayer(value, out p); };
            Assets.onAssetsRefreshed += AssetsRefreshed;
        }
        private void AssetsRefreshed() { dirty = true; }
        private void OnDestroy()
        {
            Assets.onAssetsRefreshed -= AssetsRefreshed;
            Detach();
        }
        private void Detach()
        {
            placement.EndDrag();
            viewport.Reset();
            if (dragHandle != null) dragHandle.OnClicked -= RefocusChat;
            if (pinButton != null) pinButton.OnClicked -= TogglePin;
            if (rows != null) foreach (var row in rows) if (row != null) row.OnClicked -= OnCandidateClicked;
            if (panel != null && panel.Parent != null) { try { panel.Parent.RemoveChild(panel); } catch { } }
            attached = null; panel = null; dragHandle = null; pinButton = null; parentTransform = null;
            rows = null; rowText = null; track = null; thumb = null; pressedResult = null;
            paintedResult = null; paintedSelection = -2; paintedStart = paintedCount = -1; paintedPin = null;
            paintedRowWidth = -1;
            hoveredRow = -1;
        }

        private ISleekLabel MakeLabel(float y, float height)
        {
            var label = Glazier.Get().CreateLabel();
            label.PositionOffset_X = 10; label.PositionOffset_Y = y;
            label.SizeScale_X = 1; label.SizeOffset_X = -20; label.SizeOffset_Y = height;
            label.TextAlignment = TextAnchor.MiddleLeft;
            label.AllowRichText = false;
            panel.AddChild(label);
            return label;
        }

        private static RectTransform FindTransform(ISleekElement element)
        {
            // The internal uGUI wrappers expose this property; the legacy wrappers do not.
            var property = element.GetType().GetProperty("transform", BindingFlags.Public | BindingFlags.Instance);
            return property == null ? null : property.GetValue(element, null) as RectTransform;
        }

        private void Attach(ISleekField field)
        {
            Detach(); attached = field;
            parentTransform = FindTransform(attached.Parent);
            panel = Glazier.Get().CreateBox();
            panel.PositionOffset_Y = 38;
            panel.SizeOffset_X = 500; panel.SizeOffset_Y = 320;
            panel.AllowRichText = false;
            panel.BackgroundColor = new Color(0.04f, 0.07f, 0.11f, 0.97f);
            dragHandle = Glazier.Get().CreateButton();
            dragHandle.PositionOffset_X = 10; dragHandle.PositionOffset_Y = 6;
            dragHandle.SizeScale_X = 1; dragHandle.SizeOffset_X = -140; dragHandle.SizeOffset_Y = 28;
            dragHandle.TextAlignment = TextAnchor.MiddleLeft; dragHandle.FontStyle = FontStyle.Bold;
            dragHandle.AllowRichText = false;
            dragHandle.OnClicked += RefocusChat;
            panel.AddChild(dragHandle);
            pinButton = Glazier.Get().CreateButton();
            pinButton.PositionScale_X = 1; pinButton.PositionOffset_X = -130; pinButton.PositionOffset_Y = 6;
            pinButton.SizeOffset_X = 120; pinButton.SizeOffset_Y = 28;
            pinButton.AllowRichText = false;
            pinButton.OnClicked += TogglePin;
            panel.AddChild(pinButton);
            hint = MakeLabel(34, 30);
            rows = new ISleekButton[8];
            rowText = new TextMeshProUGUI[rows.Length];
            for (int i = 0; i < rows.Length; i++)
            {
                var row = Glazier.Get().CreateButton();
                row.PositionOffset_X = RowLeft; row.PositionOffset_Y = RowTop + i * RowHeight;
                row.SizeScale_X = 1; row.SizeOffset_X = -2 * RowLeft; row.SizeOffset_Y = RowHeight;
                row.TextAlignment = TextAnchor.MiddleLeft;
                row.AllowRichText = false;
                row.TooltipText = "";
                row.BackgroundColor = ClearRowBackground;
                row.IsClickable = true; row.IsRaycastTarget = true;
                row.IsVisible = false;
                row.OnClicked += OnCandidateClicked;
                rows[i] = row;
                panel.AddChild(row);
                var rowTransform = FindTransform(row);
                if (rowTransform != null) rowText[i] = rowTransform.GetComponentInChildren<TextMeshProUGUI>(true);
            }
            track = Glazier.Get().CreateButton();
            track.IsClickable = false;
            track.IsRaycastTarget = true;
            track.BackgroundColor = new Color(0.19f, 0.25f, 0.30f, 1f);
            AddScrollbarElement(track);
            thumb = Glazier.Get().CreateImage();
            thumb.SetTextureAndShouldDestroy(Texture2D.whiteTexture, false);
            thumb.TintColor = new Color(0.46f, 0.70f, 0.80f, 1f);
            AddScrollbarElement(thumb);
            footer = MakeLabel(282, 30);
            // Add after the HUD siblings so health bars do not cover candidate text.
            attached.Parent.AddChild(panel);
            panel.ForceLayoutUpdate();
            panel.IsVisible = false;
            Debug.Log("[CommandAutocomplete] Chat renderer: " + field.GetType().FullName);
        }

        private void AddScrollbarElement(ISleekElement element)
        {
            element.PositionScale_X = 1;
            element.PositionOffset_X = -RowLeft - TrackWidth;
            element.SizeOffset_X = TrackWidth;
            element.IsVisible = false;
            panel.AddChild(element);
        }

        private void LateUpdate()
        {
            if (Time.realtimeSinceStartup < retryAt) return;
            try
            {
                bool active = PlayerLifeUI.chatting && PlayerLifeUI.chatField != null && Player.LocalPlayer != null;
                if (!active)
                {
                    placement.EndDrag();
                    viewport.Reset();
                    pressedResult = null;
                    if (panel != null && panel.IsVisible) panel.IsVisible = false;
                    SetHoveredRow(-1);
                    if (wasChatting) session.Reset();
                    wasChatting = false;
                    return;
                }
                if (!ReferenceEquals(lastPlayer, Player.LocalPlayer)) { lastPlayer = Player.LocalPlayer; dirty = true; }
                if (!ReferenceEquals(attached, PlayerLifeUI.chatField)) Attach(PlayerLifeUI.chatField);
                CaptureCandidatePress();
                if (dirty && !Assets.isLoading) { RebuildCatalog(); dirty = false; }
                bool newChat = !wasChatting; wasChatting = true;
                if (newChat || Time.realtimeSinceStartup >= nextPeopleRefresh)
                {
                    RefreshPeople(); nextPeopleRefresh = Time.realtimeSinceStartup + 1;
                    // Observe refreshes unselected results after a change, preserving an active Tab cycle.
                    if (newChat) session.Reset();
                }
                session.Observe(engine, attached.Text);
                if (Input.GetKeyDown(KeyCode.Tab) && Input.compositionString.Length == 0 && !Input.GetKey(KeyCode.LeftAlt) && !Input.GetKey(KeyCode.RightAlt) && !Input.GetKey(KeyCode.LeftControl) && !Input.GetKey(KeyCode.RightControl))
                    ApplyTab(Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift));
                RenderPanel();
            }
            catch (Exception e)
            {
                placement.EndDrag();
                viewport.EndPointerInteraction();
                if (panel != null) panel.IsVisible = false;
                retryAt = Time.realtimeSinceStartup + 5;
                Debug.LogWarning("[CommandAutocomplete] UI/catalog error: " + e.GetType().Name + ": " + e.Message);
            }
        }

        private void ApplyTab(bool previous)
        {
            if (lastSelectionFrame == Time.frameCount || attached == null) return;
            string value;
            if (session.TryCycle(attached.Text, previous, attached.MaxLength, out value)) ApplyCompletion(value);
        }

        private void CaptureCandidatePress()
        {
            if (!Input.GetMouseButtonDown(0)) return;
            // Native buttons are reused between pages and player-list refreshes.
            pressedResult = panel.IsVisible ? paintedResult : null;
            pressedStart = paintedStart;
            pressedViewRevision = viewport.Revision;
        }

        private void OnCandidateClicked(ISleekElement clicked)
        {
            if (lastSelectionFrame == Time.frameCount || !PlayerLifeUI.chatting || attached == null ||
                !ReferenceEquals(attached, PlayerLifeUI.chatField) || panel == null || !panel.IsVisible ||
                rows == null || placement.Dragging || viewport.Dragging || Input.compositionString.Length != 0) return;
            // A native button can also submit via the keyboard; accept only a left-button release.
            var ev = Event.current;
            if (!Input.GetMouseButtonUp(0) && (ev == null || ev.rawType != EventType.MouseUp || ev.button != 0)) return;
            if (viewport.CanScroll && Input.mouseScrollDelta.y != 0) return;
            if (pressedResult != null && !Input.GetMouseButtonDown(0) &&
                (!ReferenceEquals(pressedResult, paintedResult) || pressedStart != paintedStart ||
                pressedViewRevision != viewport.Revision)) return;
            int slot = Array.IndexOf<ISleekElement>(rows, clicked);
            if (slot < 0 || slot >= paintedCount || !rows[slot].IsVisible) return;
            string value;
            if (session.TrySelect(attached.Text, paintedResult, paintedStart + slot, attached.MaxLength, out value))
                ApplyCompletion(value);
        }

        private void ApplyCompletion(string value)
        {
            lastSelectionFrame = Time.frameCount;
            attached.Text = value;
            RefocusChat(null);
        }

        // Prevent IMGUI from treating Tab as focus navigation while completing. Never consumes Enter.
        private void OnGUI()
        {
            if (!PlayerLifeUI.chatting || attached == null) return;
            if (pendingCaretText != null && attached.Text == pendingCaretText && Time.frameCount <= pendingCaretUntil)
                MoveCaretToEnd(attached, pendingCaretText.Length);
            if (session.Result.Rows.Count == 0 || Input.compositionString.Length != 0) return;
            var ev = Event.current;
            if (ev != null && ev.type == EventType.KeyDown && ev.keyCode == KeyCode.Tab && !ev.alt && !ev.control)
            { ApplyTab(ev.shift); ev.Use(); }
        }

        private static void MoveCaretToEnd(ISleekField field, int length)
        {
            // uGUI exposes its InputField inside the concrete Sleek wrapper.
            for (Type type = field.GetType(); type != null; type = type.BaseType)
            {
                foreach (FieldInfo member in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    var input = member.GetValue(field);
                    if (input == null || input.GetType().Name.IndexOf("InputField",StringComparison.Ordinal) < 0) continue;
                    foreach (string propertyName in new[] { "caretPosition", "selectionAnchorPosition", "selectionFocusPosition", "stringPosition", "selectionStringAnchorPosition", "selectionStringFocusPosition" })
                    {
                        var property = input.GetType().GetProperty(propertyName, BindingFlags.Public|BindingFlags.Instance);
                        if (property != null && property.CanWrite && property.PropertyType == typeof(int)) property.SetValue(input,length,null);
                    }
                    return;
                }
            }
            // Legacy renderer uses the focused TextEditor state.
            if (Event.current != null && GUIUtility.keyboardControl != 0)
            {
                var editor = GUIUtility.GetStateObject(typeof(TextEditor), GUIUtility.keyboardControl) as TextEditor;
                if (editor != null && editor.text == field.Text) { editor.cursorIndex = length; editor.selectIndex = length; }
            }
        }

        private void RenderPanel()
        {
            bool command = !string.IsNullOrEmpty(attached.Text) && (attached.Text[0] == '/' || attached.Text[0] == '@');
            if (panel.IsVisible != command) panel.IsVisible = command;
            if (!command) { placement.EndDrag(); viewport.Reset(); SetHoveredRow(-1); return; }
            var result = session.Result;
            Vector2 parentSize = parentTransform != null ? parentTransform.rect.size : attached.Parent.GetAbsoluteSize();
            if (parentSize.x <= 0 || parentSize.y <= 0)
            { panel.IsVisible = false; viewport.EndPointerInteraction(); SetHoveredRow(-1); return; }
            float width = Math.Min(500, parentSize.x);
            if (panel.SizeOffset_X != width) panel.SizeOffset_X = width;
            float defaultX = attached.PositionScale_X * parentSize.x + attached.PositionOffset_X;
            float defaultY = attached.PositionScale_Y * parentSize.y + attached.PositionOffset_Y
                + attached.SizeScale_Y * parentSize.y + attached.SizeOffset_Y + 8;
            // Reserve space for the title, hints, at least one row, footer and bottom margin.
            placement.Place(parentSize.x, parentSize.y, panel.SizeOffset_X, 144, defaultX, defaultY);
            Vector2 normalized = attached.Parent.GetNormalizedCursorPosition();
            float cursorX = normalized.x * parentSize.x, cursorY = normalized.y * parentSize.y;
            HandlePointer(cursorX, cursorY);
            placement.Place(parentSize.x, parentSize.y, panel.SizeOffset_X, 144, defaultX, defaultY);
            if (panel.PositionScale_X != 0) panel.PositionScale_X = 0;
            if (panel.PositionScale_Y != 0) panel.PositionScale_Y = 0;
            if (panel.PositionOffset_X != placement.X) panel.PositionOffset_X = placement.X;
            if (panel.PositionOffset_Y != placement.Y) panel.PositionOffset_Y = placement.Y;
            if (paintedPin != placement.Pinned)
            {
                dragHandle.Text = placement.Pinned ? "指令自动补全 · 已固定" : "指令自动补全 · 拖动移动";
                dragHandle.TooltipText = placement.Pinned ? "点击右侧图钉解锁位置" : "按住鼠标左键拖动标题栏";
                pinButton.Text = placement.Pinned ? "图钉：开启" : "图钉：关闭";
                pinButton.TooltipText = placement.Pinned ? "点击解锁，允许拖动" : "点击固定当前位置，禁止拖动";
                pinButton.BackgroundColor = placement.Pinned ? new Color(0.15f, 0.4f, 0.55f, 1f) : new Color(0.12f, 0.16f, 0.2f, 1f);
                paintedPin = placement.Pinned;
            }
            float availableHeight = parentSize.y - placement.Y - 8;
            int capacity = Math.Max(1, Math.Min(rows.Length, (int)((availableHeight - 110) / RowHeight)));
            bool newResult = !ReferenceEquals(paintedResult, result);
            if (newResult)
            {
                if (viewport.Dragging) attached.FocusControl();
                viewport.Reset();
            }
            viewport.Resize(result.Rows.Count, capacity);
            int visibleCount = viewport.VisibleCount;
            if (visibleCount != paintedCount && paintedStart >= 0 &&
                session.Selected >= paintedStart && session.Selected < paintedStart + paintedCount)
                viewport.Reveal(session.Selected);
            bool showTrack = viewport.CanScroll && width > 2 * RowLeft + TrackWidth + TrackGap;
            float rowEnd = width - RowLeft - (showTrack ? TrackWidth + TrackGap : 0);
            float rowWidth = rowEnd - RowLeft;
            float localX = cursorX - placement.X, rowY = cursorY - placement.Y - RowTop;
            if (!newResult) HandleBrowsePointer(localX, rowY, width, showTrack);
            if (newResult || paintedSelection != session.Selected) viewport.Reveal(session.Selected);
            int start = viewport.First;
            float footerY = 74 + visibleCount * RowHeight, height = 110 + visibleCount * RowHeight;
            if (footer.PositionOffset_Y != footerY) footer.PositionOffset_Y = footerY;
            if (panel.SizeOffset_Y != height) panel.SizeOffset_Y = height;
            DrawScrollbar(showTrack);
            UpdateHover(localX, rowY, rowEnd, visibleCount);
            // Geometry still follows dragging/chat animation each frame; text only changes
            // when candidates, selection, pagination, or available row count changes.
            bool widthChanged = paintedRowWidth != rowWidth;
            if (!newResult && paintedSelection == session.Selected &&
                paintedStart == start && paintedCount == visibleCount && !widthChanged) return;
            if (hint.Text != result.Hint) hint.Text = result.Hint;
            for (int i = 0; i < rows.Length; i++)
            {
                int index = start + i;
                bool visible = i < visibleCount;
                var row = rows[i];
                if (row.IsVisible != visible) row.IsVisible = visible;
                if (!visible)
                {
                    if (!string.IsNullOrEmpty(row.TooltipText)) row.TooltipText = "";
                    continue;
                }
                var c = result.Rows[index]; bool selected = index == session.Selected;
                string label = c.Label ?? "";
                string text = (selected ? "▶ " : "   ") + label;
                bool textChanged = row.Text != text;
                if (textChanged) row.Text = text;
                if (widthChanged || textChanged || paintedCount <= i)
                {
                    row.SizeOffset_X = rowWidth - width;
                    // Native tooltips interpret rich text, whereas candidate names are plain text.
                    string tooltip = NeedsFullLabel(i, text, rowWidth) ? label.Replace("<", "<\u200B") : "";
                    if (row.TooltipText != tooltip) row.TooltipText = tooltip;
                }
                row.TextColor = selected ? new Color(0.4f, 0.85f, 1f, 1f) : new Color(0.94f, 0.95f, 0.97f, 1f);
            }
            string footerText = result.Rows.Count == 0 ? "无匹配候选；数值和自由文本请自行填写" :
                "显示 " + (start + 1) + "–" + (start + visibleCount) + " / " + result.Total +
                (result.Total > result.Rows.Count ? "  ·  仅前 " + result.Rows.Count + " 项" : viewport.CanScroll ? "  ·  滚轮浏览" : "");
            if (footer.Text != footerText) footer.Text = footerText;
            paintedResult = result; paintedSelection = session.Selected; paintedStart = start; paintedCount = visibleCount;
            paintedRowWidth = rowWidth;
        }

        private bool NeedsFullLabel(int slot, string text, float width)
        {
            var label = rowText[slot];
            // Legacy IMGUI does not expose its label metrics, so retain the full text on hover there.
            if (label == null) return true;
            float available = width - label.margin.x - label.margin.z;
            return label.GetPreferredValues(text, float.PositiveInfinity, float.PositiveInfinity).x > available;
        }

        private void HandleBrowsePointer(float localX, float rowY, float width, bool showTrack)
        {
            if (!Application.isFocused || placement.Dragging)
            { viewport.EndPointerInteraction(); return; }

            float trackHeight = viewport.VisibleCount * RowHeight;
            if (viewport.Dragging)
            {
                if (showTrack && (Input.GetMouseButton(0) || Input.GetMouseButtonUp(0)))
                    viewport.MoveDrag(rowY, trackHeight);
                if (!showTrack || !Input.GetMouseButton(0))
                {
                    viewport.EndPointerInteraction();
                    attached.FocusControl();
                }
                return;
            }
            if (localX < RowLeft || localX >= width - RowLeft || rowY < 0 || rowY >= trackHeight) return;
            if (showTrack && localX >= width - RowLeft - TrackWidth && Input.GetMouseButtonDown(0))
                viewport.BeginDrag(rowY, trackHeight);
            else
                viewport.ScrollWheel(Input.mouseScrollDelta.y);
        }

        private void DrawScrollbar(bool visible)
        {
            if (track.IsVisible != visible) track.IsVisible = thumb.IsVisible = visible;
            if (!visible) return;
            float height = viewport.VisibleCount * RowHeight;
            float thumbHeight = viewport.ThumbHeight(height);
            float thumbY = RowTop + viewport.ThumbTop(height);
            if (track.PositionOffset_Y != RowTop) track.PositionOffset_Y = RowTop;
            if (track.SizeOffset_Y != height) track.SizeOffset_Y = height;
            if (thumb.PositionOffset_Y != thumbY) thumb.PositionOffset_Y = thumbY;
            if (thumb.SizeOffset_Y != thumbHeight) thumb.SizeOffset_Y = thumbHeight;
        }

        private void UpdateHover(float localX, float rowY, float rowEnd, int visibleCount)
        {
            // Hover only paints a background; the selection arrow and text color belong to the completion session.
            bool overRow = Application.isFocused && !placement.Dragging && !viewport.Dragging &&
                localX >= RowLeft && localX < rowEnd &&
                rowY >= 0 && rowY < visibleCount * RowHeight;
            SetHoveredRow(overRow ? (int)(rowY / RowHeight) : -1);
        }

        private void SetHoveredRow(int row)
        {
            if (row == hoveredRow) return;
            if (hoveredRow >= 0) rows[hoveredRow].BackgroundColor = ClearRowBackground;
            hoveredRow = row;
            if (hoveredRow >= 0) rows[hoveredRow].BackgroundColor = HoverRowBackground;
        }

        private void HandlePointer(float cursorX, float cursorY)
        {
            if (placement.Dragging)
            {
                if (Input.GetMouseButton(0)) placement.MoveDrag(cursorX, cursorY);
                else { placement.EndDrag(); RefocusChat(null); }
            }
            else if (Input.GetMouseButtonDown(0) && !placement.Pinned)
            {
                float localX = cursorX - placement.X, localY = cursorY - placement.Y;
                // The pin button is outside this drag region and cannot start a drag.
                if (localX >= 10 && localX < panel.SizeOffset_X - 130 && localY >= 6 && localY < 34)
                    placement.BeginDrag(cursorX, cursorY);
            }
        }

        private void RefocusChat(ISleekElement clicked)
        {
            if (attached == null || !PlayerLifeUI.chatting) return;
            attached.FocusControl();
            pendingCaretText = attached.Text; pendingCaretUntil = Time.frameCount + 2;
            MoveCaretToEnd(attached, pendingCaretText.Length);
        }

        private void TogglePin(ISleekElement clicked)
        {
            placement.TogglePin();
            RefocusChat(clicked);
        }

        private void RefreshPeople()
        {
            var list = new List<Suggestion>();
            if (Provider.clients != null) foreach (var p in Provider.clients)
            {
                var id = p.playerID;
                string value = id.steamID.ToString();
                list.Add(new Suggestion(Kind.Player, value, "玩家 · " + id.characterName + "  [" + value + "]", "", id.characterName, id.playerName, id.nickName));
            }
            engine.ReplacePlayers(list);
        }

        private static string EnglishName(Asset a)
        {
            return a.Localization == null ? "" : a.Localization.FormatEnglishOrEmpty("Name");
        }
        private void AddAsset(Kind kind, Asset a, string display, string value)
        {
            if (a == null || string.IsNullOrEmpty(value)) return;
            string english = EnglishName(a);
            string type = kind == Kind.Item ? "物品" : kind == Kind.Vehicle ? "载具" : kind == Kind.Animal ? "动物" : kind == Kind.Quest ? "任务" : "天气";
            string name = string.IsNullOrEmpty(display) ? a.name : display;
            string suffix = !string.IsNullOrEmpty(english) && english != name ? " · " + english : "";
            var row = new Suggestion(kind, value, type + " · " + name + suffix + "  [" + (a.id == 0 ? a.name : a.id.ToString()) + "]", a.name, name, english, a.name, a.id.ToString(), a.GUID.ToString("N"), a.GUID.ToString());
            row.Number = a.id; engine.Catalog[kind].Add(row);
        }

        private void RebuildCatalog()
        {
            foreach (var list in engine.Catalog.Values) list.Clear();
            engine.Commands.Clear();
            // Pure clients do not initialize Commander.commands. Instantiate metadata-only
            // vanilla command objects without registering them or executing any command.
            IEnumerable<Command> commands = Commander.commands;
            if (commands == null || !commands.Any(c => c.GetType().Assembly == typeof(Command).Assembly))
            {
                var localCommands = new List<Command>();
                foreach (Type type in typeof(Command).Assembly.GetTypes())
                {
                    if (type.IsAbstract || !type.IsSubclassOf(typeof(Command)) || !type.Name.StartsWith("Command",StringComparison.Ordinal)) continue;
                    var ctor = type.GetConstructor(new[] { typeof(Local) });
                    if (ctor == null) continue;
                    string canonicalName = type.Name.Substring(7);
                    Local locale = Localization.read("/Server/ServerCommand" + canonicalName + ".dat");
                    localCommands.Add((Command)ctor.Invoke(new object[] { locale }));
                }
                commands = localCommands;
            }
            foreach (var cmd in commands)
            {
                if (cmd.GetType().Assembly != typeof(Command).Assembly) continue;
                string canonical = cmd.GetType().Name;
                if (!canonical.StartsWith("Command", StringComparison.Ordinal)) continue;
                canonical = canonical.Substring(7);
                if (string.IsNullOrEmpty(cmd.command)) continue;
                var spec = new CommandSpec(cmd.command, canonical, cmd.info, cmd.help);
                AddChoices(spec, canonical);
                engine.Commands.Add(spec);
            }
            var items = new List<ItemAsset>(); Assets.find(items);
            foreach (var a in items)
                if (a.id != 0 && ReferenceEquals(Assets.find(EAssetType.ITEM, a.id), a)) AddAsset(Kind.Item, a, a.itemName, a.id.ToString());
            var vehicles = new List<VehicleAsset>(); Assets.find(vehicles);
            foreach (var a in vehicles)
            {
                bool mapped = a.id != 0 && ReferenceEquals(Assets.find(EAssetType.VEHICLE, a.id), a);
                AddAsset(Kind.Vehicle, a, a.vehicleName, mapped ? a.id.ToString() : a.GUID.ToString("N"));
            }
            var animals = new List<AnimalAsset>(); Assets.find(animals);
            foreach (var a in animals) if (a.id != 0 && ReferenceEquals(Assets.find(EAssetType.ANIMAL, a.id), a)) AddAsset(Kind.Animal, a, a.FriendlyName, a.id.ToString());
            var quests = new List<QuestAsset>(); Assets.find(quests);
            foreach (var a in quests) AddAsset(Kind.Quest, a, a.FriendlyName, a.GUID.ToString("N"));
            var weathers = new List<WeatherAssetBase>(); Assets.find(weathers);
            foreach (var a in weathers) AddAsset(Kind.Weather, a, a.FriendlyName, a.GUID.ToString("N"));
            var locations = LocationDevkitNodeSystem.Get();
            if (locations != null)
            foreach (var node in locations.GetAllNodes())
                if (!string.IsNullOrWhiteSpace(node.locationName) && node.locationName.IndexOf('/') < 0)
                    engine.Catalog[Kind.Location].Add(new Suggestion(Kind.Location, node.locationName, "地点 · " + node.locationName, "", node.locationName));
            RefreshPeople(); session.Reset();
            Debug.Log("[CommandAutocomplete] Catalog ready: commands=" + engine.Commands.Count + ", items=" + engine.Catalog[Kind.Item].Count + ", vehicles=" + engine.Catalog[Kind.Vehicle].Count + ", locations=" + engine.Catalog[Kind.Location].Count);
        }

        private static void AddChoices(CommandSpec spec, string canonical)
        {
            Local locale = Localization.read("/Server/ServerCommand" + canonical + ".dat");
            Action<int, string, string, string> add = delegate(int index, string key, string chinese, string english)
            {
                string value = locale.FormatOrEmpty(key);
                if (string.IsNullOrEmpty(value)) return;
                List<Suggestion> list;
                if (!spec.Choices.TryGetValue(index, out list)) { list = new List<Suggestion>(); spec.Choices[index] = list; }
                list.Add(new Suggestion(Kind.Choice, value, chinese + " · " + value, "", chinese, english));
            };
            switch (canonical.ToLowerInvariant())
            {
                case "weather": add(0,"WeatherNone","恢复默认天气","none"); add(0,"WeatherDisable","禁用天气","disable"); add(0,"WeatherStorm","切换暴雨","storm"); add(0,"WeatherBlizzard","切换暴雪","blizzard"); break;
                case "mode": add(0,"ModeEasy","简单","easy"); add(0,"ModeNormal","普通","normal"); add(0,"ModeHard","困难","hard"); break;
                case "camera": add(0,"CameraFirst","第一人称","first"); add(0,"CameraThird","第三人称","third"); add(0,"CameraBoth","两种视角","both"); add(0,"CameraVehicle","载具视角","vehicle"); break;
                case "teleport": add(0,"WaypointCommand","地图标记点","wp"); add(0,"BedCommand","床铺","bed"); add(1,"WaypointCommand","地图标记点","wp"); add(1,"BedCommand","床铺","bed"); break;
                case "log": case "votify":
                    for (int i = 0; i < (canonical.Equals("Log",StringComparison.OrdinalIgnoreCase)?4:1); i++)
                        spec.Choices[i] = new List<Suggestion> { new Suggestion(Kind.Choice,"y","是 · y","","yes","是"), new Suggestion(Kind.Choice,"n","否 · n","","no","否") };
                    break;
            }
        }
    }
}
