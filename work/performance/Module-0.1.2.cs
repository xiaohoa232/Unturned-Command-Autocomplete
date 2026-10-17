using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using SDG.Framework.Modules;
using SDG.Unturned;
using UnityEngine;

[assembly: AssemblyVersion("0.1.2.0")]

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
            Debug.Log("[CommandAutocomplete] 0.1.2 initialized (client only, no network messages)");
        }
        public void shutdown() { if (host != null) UnityEngine.Object.Destroy(host); }
    }

    public sealed class AutocompleteBehaviour : MonoBehaviour
    {
        private readonly CompletionEngine engine = new CompletionEngine();
        private readonly CompletionSession session = new CompletionSession();
        private readonly PanelPlacement placement = new PanelPlacement();
        private ISleekField attached;
        private ISleekBox panel;
        private ISleekLabel hint, footer;
        private ISleekButton dragHandle, pinButton;
        private RectTransform parentTransform;
        private ISleekLabel[] rows;
        private bool dirty = true, wasChatting;
        private Player lastPlayer;
        private float nextPeopleRefresh, retryAt;
        private int lastTabFrame = -1;
        private string pendingCaretText;
        private int pendingCaretUntil;

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
            if (dragHandle != null) dragHandle.OnClicked -= RefocusChat;
            if (pinButton != null) pinButton.OnClicked -= TogglePin;
            if (panel != null && panel.Parent != null) { try { panel.Parent.RemoveChild(panel); } catch { } }
            attached = null; panel = null; dragHandle = null; pinButton = null; parentTransform = null;
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
        private void Attach(ISleekField field)
        {
            Detach(); attached = field;
            // The uGUI wrapper class is internal, but exposes a public transform property.
            // Cache its RectTransform once, without taking a dependency on that concrete class.
            var transformProperty = attached.Parent.GetType().GetProperty("transform", BindingFlags.Public | BindingFlags.Instance);
            if (transformProperty != null) parentTransform = transformProperty.GetValue(attached.Parent, null) as RectTransform;
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
            rows = new ISleekLabel[8];
            for (int i = 0; i < rows.Length; i++) rows[i] = MakeLabel(68 + i * 26, 26);
            footer = MakeLabel(282, 30);
            // Add after the HUD siblings so health bars do not cover candidate text.
            attached.Parent.AddChild(panel);
            panel.ForceLayoutUpdate();
            panel.IsVisible = false;
            Debug.Log("[CommandAutocomplete] Chat renderer: " + field.GetType().FullName);
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
                    if (panel != null) panel.IsVisible = false;
                    if (wasChatting) session.Reset();
                    wasChatting = false;
                    return;
                }
                if (!ReferenceEquals(lastPlayer, Player.LocalPlayer)) { lastPlayer = Player.LocalPlayer; dirty = true; }
                if (!ReferenceEquals(attached, PlayerLifeUI.chatField)) Attach(PlayerLifeUI.chatField);
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
                if (panel != null) panel.IsVisible = false;
                retryAt = Time.realtimeSinceStartup + 5;
                Debug.LogWarning("[CommandAutocomplete] UI/catalog error: " + e.GetType().Name + ": " + e.Message);
            }
        }

        private void ApplyTab(bool previous)
        {
            if (lastTabFrame == Time.frameCount || session.Result.Rows.Count == 0 || attached == null) return;
            string value = session.Cycle(attached.Text, previous);
            if (value.Length > attached.MaxLength) return;
            lastTabFrame = Time.frameCount;
            attached.Text = value;
            attached.FocusControl();
            pendingCaretText = value; pendingCaretUntil = Time.frameCount + 2;
            MoveCaretToEnd(attached, value.Length);
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
            panel.IsVisible = command;
            if (!command) { placement.EndDrag(); return; }
            var result = session.Result;
            Vector2 parentSize = parentTransform != null ? parentTransform.rect.size : attached.Parent.GetAbsoluteSize();
            if (parentSize.x <= 0 || parentSize.y <= 0) { panel.IsVisible = false; return; }
            panel.SizeOffset_X = Math.Min(500, parentSize.x);
            float defaultX = attached.PositionScale_X * parentSize.x + attached.PositionOffset_X;
            float defaultY = attached.PositionScale_Y * parentSize.y + attached.PositionOffset_Y
                + attached.SizeScale_Y * parentSize.y + attached.SizeOffset_Y + 8;
            // Reserve space for the title, hints, at least one row, footer and bottom margin.
            placement.Place(parentSize.x, parentSize.y, panel.SizeOffset_X, 144, defaultX, defaultY);
            HandlePointer(parentSize);
            placement.Place(parentSize.x, parentSize.y, panel.SizeOffset_X, 144, defaultX, defaultY);
            panel.PositionScale_X = 0; panel.PositionScale_Y = 0;
            panel.PositionOffset_X = placement.X; panel.PositionOffset_Y = placement.Y;
            dragHandle.Text = placement.Pinned ? "指令自动补全 · 已固定" : "指令自动补全 · 拖动移动";
            dragHandle.TooltipText = placement.Pinned ? "点击右侧图钉解锁位置" : "按住鼠标左键拖动标题栏";
            pinButton.Text = placement.Pinned ? "图钉：开启" : "图钉：关闭";
            pinButton.TooltipText = placement.Pinned ? "点击解锁，允许拖动" : "点击固定当前位置，禁止拖动";
            pinButton.BackgroundColor = placement.Pinned ? new Color(0.15f, 0.4f, 0.55f, 1f) : new Color(0.12f, 0.16f, 0.2f, 1f);
            hint.Text = result.Hint;
            float availableHeight = parentSize.y - placement.Y - 8;
            int pageSize = Math.Max(1, Math.Min(rows.Length, (int)((availableHeight - 110) / 26)));
            int start = session.Selected < 0 ? 0 : session.Selected / pageSize * pageSize;
            int visibleCount = Math.Min(pageSize, Math.Max(0, result.Rows.Count - start));
            footer.PositionOffset_Y = 74 + visibleCount * 26;
            panel.SizeOffset_Y = 110 + visibleCount * 26;
            for (int i = 0; i < rows.Length; i++)
            {
                int index = start + i;
                rows[i].IsVisible = i < visibleCount;
                if (i >= visibleCount) continue;
                var c = result.Rows[index]; bool selected = index == session.Selected;
                rows[i].Text = (selected ? "▶ " : "   ") + c.Label;
                rows[i].TextColor = selected ? new Color(0.4f, 0.85f, 1f, 1f) : new Color(0.94f, 0.95f, 0.97f, 1f);
            }
            if (result.Rows.Count == 0) footer.Text = "无匹配候选；数值和自由文本请自行填写";
            else footer.Text = "Tab 下一项 · Shift+Tab 上一项 · Enter 发送  " + (session.Selected < 0 ? result.Total + " 个候选" : (session.Selected + 1) + "/" + result.Total) + (result.Total > CompletionEngine.Limit ? "（请缩小搜索）" : "");
        }

        private void HandlePointer(Vector2 parentSize)
        {
            Vector2 normalized = attached.Parent.GetNormalizedCursorPosition();
            float cursorX = normalized.x * parentSize.x, cursorY = normalized.y * parentSize.y;
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
