using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using SDG.Framework.Modules;
using SDG.Unturned;
using TMPro;
using UnityEngine;

[assembly: AssemblyVersion("0.1.7.0")]

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
            Debug.Log("[CommandAutocomplete] " + typeof(Nexus).Assembly.GetName().Version + " initialized (client only, no network messages)");
        }
        public void shutdown() { if (host != null) UnityEngine.Object.Destroy(host); }
    }

    public sealed class AutocompleteBehaviour : MonoBehaviour
    {
        private const float RowLeft = 10, RowTop = 68, RowHeight = 26;
        private const float TrackWidth = 12, TrackGap = 4;
        private static readonly Color ClearRowBackground = new Color(0f, 0f, 0f, 0f);
        private static readonly Color HoverRowBackground = new Color(0.38f, 0.26f, 0.11f, 0.96f);
        private CompletionEngine engine;
        private IEnumerator catalogBuild;
        private readonly CompletionResult loadingCatalog = new CompletionResult { Hint = "正在准备指令和资源候选…", IsPending = true };
        private readonly HashSet<Kind> catalogWarnings = new HashSet<Kind>();
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
        private int lastTabFrame = -1;
        private string pendingCaretText;
        private int pendingCaretUntil;
        private bool checkedCaretControl;
        private Action<int> setCaret;
        private CompletionResult paintedResult;
        private int paintedSelection = -2, paintedStart = -1, paintedCount = -1;
        private float paintedRowWidth = -1;
        private int hoveredRow = -1;
        private bool? paintedPin;
        private CompletionResult pressedResult;
        private int pressedStart, pressedViewRevision;

        private void Awake()
        {
            Assets.onAssetsRefreshed += AssetsRefreshed;
        }

        private void AssetsRefreshed()
        {
            dirty = true;
            catalogBuild = null;
            session.Reset();
        }
        private void OnDestroy()
        {
            Assets.onAssetsRefreshed -= AssetsRefreshed;
            Detach();
        }
        private void Detach()
        {
            placement.EndDrag();
            viewport.Reset();
            session.Reset();
            var oldPanel = panel;
            var oldHandle = dragHandle;
            var oldPin = pinButton;
            var oldRows = rows;
            attached = null; panel = null; dragHandle = null; pinButton = null; parentTransform = null;
            rows = null; rowText = null; track = null; thumb = null; pressedResult = null;
            paintedResult = null; paintedSelection = -2; paintedStart = paintedCount = -1; paintedPin = null;
            paintedRowWidth = -1;
            hoveredRow = -1;
            wasChatting = false;
            pendingCaretText = null;
            checkedCaretControl = false;
            setCaret = null;
            if (oldHandle != null) TryCleanup(() => oldHandle.OnClicked -= RefocusSafely);
            if (oldPin != null) TryCleanup(() => oldPin.OnClicked -= TogglePin);
            if (oldRows != null)
                foreach (var row in oldRows)
                    if (row != null) TryCleanup(() => row.OnClicked -= CandidateClickedSafely);
            if (oldPanel != null)
            {
                TryCleanup(() => oldPanel.IsVisible = false);
                TryCleanup(() => { if (oldPanel.Parent != null) oldPanel.Parent.RemoveChild(oldPanel); });
            }
        }

        private static void TryCleanup(System.Action cleanup)
        {
            try { cleanup(); }
            catch (Exception e) { Debug.LogWarning("[CommandAutocomplete] UI cleanup: " + e.Message); }
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
            try
            {
                var property = element.GetType().GetProperty("transform", BindingFlags.Public | BindingFlags.Instance);
                return property == null ? null : property.GetValue(element, null) as RectTransform;
            }
            catch (AmbiguousMatchException) { return null; }
            catch (TargetInvocationException) { return null; }
        }

        private void Attach(ISleekField field)
        {
            Detach();
            var parent = field.Parent;
            if (parent == null) return;
            parentTransform = FindTransform(parent);
            panel = Glazier.Get().CreateBox();
            panel.IsVisible = false;
            panel.PositionOffset_Y = 38;
            panel.SizeOffset_X = 500; panel.SizeOffset_Y = 320;
            panel.AllowRichText = false;
            panel.BackgroundColor = new Color(0.04f, 0.07f, 0.11f, 0.97f);
            dragHandle = Glazier.Get().CreateButton();
            dragHandle.PositionOffset_X = 10; dragHandle.PositionOffset_Y = 6;
            dragHandle.SizeScale_X = 1; dragHandle.SizeOffset_X = -140; dragHandle.SizeOffset_Y = 28;
            dragHandle.TextAlignment = TextAnchor.MiddleLeft; dragHandle.FontStyle = FontStyle.Bold;
            dragHandle.AllowRichText = false;
            dragHandle.OnClicked += RefocusSafely;
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
                row.OnClicked += CandidateClickedSafely;
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
            parent.AddChild(panel);
            panel.ForceLayoutUpdate();
            attached = field;
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
                var localPlayer = Player.LocalPlayer;
                if (!ReferenceEquals(lastPlayer, localPlayer))
                {
                    lastPlayer = localPlayer;
                    engine = null;
                    AssetsRefreshed();
                    Detach();
                }
                var field = PlayerLifeUI.chatField;
                bool active = PlayerLifeUI.chatting && field != null && localPlayer != null && IsCommand(field.Text);
                if (!active)
                {
                    placement.EndDrag();
                    viewport.Reset();
                    pressedResult = null;
                    if (panel != null && panel.IsVisible) panel.IsVisible = false;
                    SetHoveredRow(-1);
                    if (wasChatting) session.Reset();
                    wasChatting = false;
                    pendingCaretText = null;
                    return;
                }
                if (!ReferenceEquals(attached, field)) Attach(field);
                if (attached == null) return;
                CaptureCandidatePress();
                if (dirty && !Assets.isLoading) AdvanceCatalog();
                bool newChat = !wasChatting; wasChatting = true;
                if (!dirty)
                {
                    if (newChat || Time.realtimeSinceStartup >= nextPeopleRefresh) RefreshPeople();
                    session.Observe(engine, attached.Text, true);
                    if (CanInteract() && Input.GetKeyDown(KeyCode.Tab) && string.IsNullOrEmpty(Input.compositionString) &&
                        !Input.GetKey(KeyCode.LeftAlt) && !Input.GetKey(KeyCode.RightAlt) && !Input.GetKey(KeyCode.LeftControl) && !Input.GetKey(KeyCode.RightControl))
                        ApplyTab(Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift));
                }
                RenderPanel();
            }
            catch (Exception e)
            {
                Recover(e);
            }
        }

        private static bool IsCommand(string text)
        {
            return !string.IsNullOrEmpty(text) && (text[0] == '/' || text[0] == '@');
        }

        private void Recover(Exception error)
        {
            retryAt = Time.realtimeSinceStartup + 5;
            catalogBuild = null;
            Detach();
            Debug.LogWarning("[CommandAutocomplete] UI/catalog error: " + error);
        }

        private bool CanInteract()
        {
            return !dirty && Time.realtimeSinceStartup >= retryAt && PlayerLifeUI.chatting &&
                attached != null && ReferenceEquals(attached, PlayerLifeUI.chatField) && panel != null && panel.IsVisible;
        }

        private void CandidateClickedSafely(ISleekElement clicked)
        {
            try { if (CanInteract()) OnCandidateClicked(clicked); }
            catch (Exception e) { Recover(e); }
        }

        private bool ApplyTab(bool previous)
        {
            if (lastSelectionFrame == Time.frameCount) return lastTabFrame == Time.frameCount;
            if (attached == null) return false;
            string value;
            if (!session.TryCycle(attached.Text, previous, attached.MaxLength, out value)) return false;
            ApplyCompletion(value);
            lastTabFrame = Time.frameCount;
            return true;
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
                rows == null || placement.Dragging || viewport.Dragging || !string.IsNullOrEmpty(Input.compositionString)) return;
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
            try
            {
                if (!CanInteract() || !string.IsNullOrEmpty(Input.compositionString)) return;
                if (pendingCaretText != null && attached.Text == pendingCaretText && Time.frameCount <= pendingCaretUntil)
                    MoveCaretToEnd(attached, pendingCaretText.Length);
                var ev = Event.current;
                if (ev != null && ev.type == EventType.KeyDown && ev.keyCode == KeyCode.Tab && !ev.alt && !ev.control &&
                    ApplyTab(ev.shift)) ev.Use();
            }
            catch (Exception e) { Recover(e); }
        }

        private void MoveCaretToEnd(ISleekField field, int length)
        {
            try
            {
                if (!checkedCaretControl)
                {
                    checkedCaretControl = true;
                    setCaret = FindCaretSetter(field);
                }
                if (setCaret != null) { setCaret(length); return; }
                if (Event.current != null && GUIUtility.keyboardControl != 0)
                {
                    var editor = GUIUtility.GetStateObject(typeof(TextEditor), GUIUtility.keyboardControl) as TextEditor;
                    if (editor != null && editor.text == field.Text) { editor.cursorIndex = length; editor.selectIndex = length; }
                }
            }
            catch (Exception e)
            {
                setCaret = delegate { };
                Debug.LogWarning("[CommandAutocomplete] Caret positioning unavailable: " + e.Message);
            }
        }

        private static Action<int> FindCaretSetter(ISleekField field)
        {
            for (Type type = field.GetType(); type != null; type = type.BaseType)
            {
                foreach (FieldInfo member in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    var input = member.GetValue(field);
                    var tmp = input as TMP_InputField;
                    if (tmp != null) return length =>
                    {
                        tmp.stringPosition = length;
                        tmp.selectionStringAnchorPosition = length;
                        tmp.selectionStringFocusPosition = length;
                    };
                    var ugui = input as UnityEngine.UI.InputField;
                    if (ugui != null) return length =>
                    {
                        ugui.caretPosition = length;
                        ugui.selectionAnchorPosition = length;
                        ugui.selectionFocusPosition = length;
                    };
                    var toolkit = input as UnityEngine.UIElements.TextField;
                    if (toolkit != null) return length => toolkit.SelectRange(length, length);
                }
            }
            return null;
        }

        private void RenderPanel()
        {
            bool command = IsCommand(attached.Text);
            if (panel.IsVisible != command) panel.IsVisible = command;
            if (!command) { placement.EndDrag(); viewport.Reset(); SetHoveredRow(-1); return; }
            var result = dirty ? loadingCatalog : session.Result;
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
            string footerText = result.IsPending ? "" : result.Rows.Count == 0 ? "无匹配候选；数值和自由文本请自行填写" :
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

        private void RefocusSafely(ISleekElement clicked)
        {
            if (!ReferenceEquals(clicked, dragHandle) && !ReferenceEquals(clicked, pinButton)) return;
            try { RefocusChat(clicked); }
            catch (Exception e) { Recover(e); }
        }

        private void TogglePin(ISleekElement clicked)
        {
            if (!ReferenceEquals(clicked, pinButton)) return;
            placement.TogglePin();
            RefocusSafely(clicked);
        }

        private void RefreshPeople()
        {
            var list = new List<Suggestion>();
            if (Provider.clients != null) foreach (var p in Provider.clients)
            {
                if (p == null) continue;
                var id = p.playerID;
                string value = id.steamID.ToString();
                list.Add(new Suggestion(Kind.Player, value, "玩家 · " + id.characterName + "  [" + value + "]", "", id.characterName, id.playerName, id.nickName));
            }
            engine.ReplacePlayers(list);
            nextPeopleRefresh = Time.realtimeSinceStartup + 1;
        }

        private static string EnglishName(Asset a)
        {
            return a.Localization == null ? "" : a.Localization.FormatEnglishOrEmpty("Name");
        }
        private static void AddAsset(CompletionEngine catalog, Kind kind, Asset a, string display, string value)
        {
            if (a == null || string.IsNullOrEmpty(value)) return;
            string english = EnglishName(a);
            string type = kind == Kind.Item ? "物品" : kind == Kind.Vehicle ? "载具" : kind == Kind.Animal ? "动物" : kind == Kind.Quest ? "任务" : "天气";
            string name = string.IsNullOrEmpty(display) ? a.name : display;
            string suffix = !string.IsNullOrEmpty(english) && english != name ? " · " + english : "";
            var row = new Suggestion(kind, value, type + " · " + name + suffix + "  [" + (a.id == 0 ? a.name : a.id.ToString()) + "]", a.name, name, english, a.name, a.id.ToString(), a.GUID.ToString("N"), a.GUID.ToString());
            row.Number = a.id; catalog.Catalog[kind].Add(row);
        }

        private void AdvanceCatalog()
        {
            if (catalogBuild == null)
            {
                catalogWarnings.Clear();
                catalogBuild = BuildCatalog().GetEnumerator();
            }
            var currentBuild = catalogBuild;
            var budget = System.Diagnostics.Stopwatch.StartNew();
            do
            {
                if (currentBuild.MoveNext()) continue;
                catalogBuild = null;
                return;
            } while (ReferenceEquals(catalogBuild, currentBuild) && budget.Elapsed.TotalMilliseconds < 2);
        }

        private IEnumerable<object> BuildCatalog()
        {
            var next = new CompletionEngine();
            next.IsPlayer = delegate(string value) { SteamPlayer p; return !string.IsNullOrEmpty(value) && PlayerTool.tryGetSteamPlayer(value, out p); };
            var commands = Commander.commands == null ? new Command[0] :
                Commander.commands.Where(c => c != null && c.GetType().Assembly == typeof(Command).Assembly).ToArray();
            if (commands.Length > 0)
            {
                foreach (Command command in commands)
                {
                    AddCommand(next, command);
                    yield return null;
                }
            }
            else
            {
                // Pure clients have no registered commands. Each metadata constructor is optional.
                foreach (Type type in GetCommandTypes())
                {
                    try
                    {
                        var ctor = type.GetConstructor(new[] { typeof(Local) });
                        if (ctor != null)
                        {
                            var locale = Localization.read("/Server/Server" + type.Name + ".dat");
                            AddCommand(next, (Command)ctor.Invoke(new object[] { locale }));
                        }
                    }
                    catch (Exception error) { WarnCatalog(Kind.Command, type.Name, error); }
                    yield return null;
                }
            }
            foreach (var step in AddCatalogAssets<ItemAsset>(next, Kind.Item, a => a.itemName,
                a => NumericId(a, EAssetType.ITEM))) yield return step;
            foreach (var step in AddCatalogAssets<VehicleAsset>(next, Kind.Vehicle, a => a.vehicleName,
                a => NumericId(a, EAssetType.VEHICLE) ?? a.GUID.ToString("N"))) yield return step;
            foreach (var step in AddCatalogAssets<AnimalAsset>(next, Kind.Animal, a => a.FriendlyName,
                a => NumericId(a, EAssetType.ANIMAL))) yield return step;
            foreach (var step in AddCatalogAssets<QuestAsset>(next, Kind.Quest, a => a.FriendlyName,
                a => a.GUID.ToString("N"))) yield return step;
            foreach (var step in AddCatalogAssets<WeatherAssetBase>(next, Kind.Weather, a => a.FriendlyName,
                a => a.GUID.ToString("N"))) yield return step;
            foreach (var node in GetLocationNodes())
            {
                if (node != null && !string.IsNullOrWhiteSpace(node.locationName) && node.locationName.IndexOf('/') < 0)
                    next.Catalog[Kind.Location].Add(new Suggestion(Kind.Location, node.locationName, "地点 · " + node.locationName, "", node.locationName));
                yield return null;
            }
            engine = next;
            dirty = false;
            nextPeopleRefresh = 0;
            session.Reset();
            Debug.Log("[CommandAutocomplete] Catalog ready: commands=" + engine.Commands.Count + ", items=" + engine.Catalog[Kind.Item].Count + ", vehicles=" + engine.Catalog[Kind.Vehicle].Count + ", locations=" + engine.Catalog[Kind.Location].Count);
        }

        private Type[] GetCommandTypes()
        {
            Type[] types;
            try { types = typeof(Command).Assembly.GetTypes(); }
            catch (ReflectionTypeLoadException error)
            {
                WarnCatalog(Kind.Command, "type discovery", error);
                types = error.Types;
            }
            return types.Where(type => type != null && !type.IsAbstract &&
                type.IsSubclassOf(typeof(Command)) && type.Name.StartsWith("Command", StringComparison.Ordinal)).ToArray();
        }

        private LocationDevkitNode[] GetLocationNodes()
        {
            try
            {
                var locations = LocationDevkitNodeSystem.Get();
                return locations == null ? new LocationDevkitNode[0] : locations.GetAllNodes().ToArray();
            }
            catch (Exception error)
            {
                WarnCatalog(Kind.Location, "location discovery", error);
                return new LocationDevkitNode[0];
            }
        }

        private void AddCommand(CompletionEngine catalog, Command command)
        {
            string typeName = command.GetType().Name;
            if (!typeName.StartsWith("Command", StringComparison.Ordinal) || string.IsNullOrEmpty(command.command)) return;
            string canonical = typeName.Substring(7);
            var spec = new CommandSpec(command.command, canonical, command.info, command.help);
            try { AddChoices(spec, canonical); }
            catch (Exception error) { WarnCatalog(Kind.Command, typeName, error); }
            catalog.Commands.Add(spec);
        }

        private IEnumerable<object> AddCatalogAssets<T>(CompletionEngine catalog, Kind kind,
            Func<T, string> display, Func<T, string> value) where T : Asset
        {
            var assets = new List<T>();
            try { Assets.find(assets); }
            catch (Exception error)
            {
                WarnCatalog(kind, "asset discovery", error);
                assets.Clear();
            }
            yield return null;
            foreach (T asset in assets)
            {
                if (asset == null) continue;
                try
                {
                    string token = value(asset);
                    if (token != null) AddAsset(catalog, kind, asset, display(asset), token);
                }
                catch (Exception error) { WarnCatalog(kind, asset.GetType().Name, error); }
                yield return null;
            }
        }

        private static string NumericId(Asset asset, EAssetType type)
        {
            return asset.id != 0 && ReferenceEquals(Assets.find(type, asset.id), asset) ? asset.id.ToString() : null;
        }

        private void WarnCatalog(Kind kind, string context, Exception error)
        {
            if (catalogWarnings.Add(kind))
                Debug.LogWarning("[CommandAutocomplete] Skipped " + kind + " metadata (" + context + "): " + error);
        }

        private static void AddChoices(CommandSpec spec, string canonical)
        {
            switch (canonical.ToLowerInvariant())
            {
                case "weather": case "mode": case "camera": case "teleport": case "log": case "votify": break;
                default: return;
            }
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
