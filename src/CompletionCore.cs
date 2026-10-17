using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace UnturnedCommandAutocomplete
{
    public enum Kind { Command, Item, Vehicle, Player, Location, Animal, Quest, Weather, Choice }

    public sealed class Suggestion
    {
        public Kind Kind;
        public string Value, Label, Description;
        public string[] Aliases;
        public int Number;
        public Suggestion(Kind kind, string value, string label, string description, params string[] aliases)
        {
            Kind = kind; Value = value; Label = label; Description = description ?? "";
            Aliases = new[] { value, label }.Concat(aliases ?? new string[0]).Where(s => !string.IsNullOrEmpty(s)).Select(Normalize).Distinct().ToArray();
        }
        public static string Normalize(string text)
        {
            return (text ?? "").Normalize(NormalizationForm.FormKC).Trim().ToLowerInvariant();
        }
        public int Score(string query)
        {
            if (query.Length == 0) return 3;
            // Keep the same exact/prefix/substring precedence without per-row closures.
            for (int i = 0; i < Aliases.Length; i++) if (Aliases[i] == query) return 0;
            for (int i = 0; i < Aliases.Length; i++) if (Aliases[i].StartsWith(query, StringComparison.Ordinal)) return 1;
            for (int i = 0; i < Aliases.Length; i++) if (Aliases[i].IndexOf(query, StringComparison.Ordinal) >= 0) return 2;
            return 99;
        }
    }

    public sealed class CommandSpec
    {
        public string Name, Canonical, Syntax, Description;
        public Dictionary<int, List<Suggestion>> Choices = new Dictionary<int, List<Suggestion>>();
        public CommandSpec(string name, string canonical, string syntax, string description)
        { Name = name; Canonical = canonical.ToLowerInvariant(); Syntax = syntax ?? ""; Description = description ?? ""; }
    }

    public sealed class CompletionResult
    {
        public int Start, Length, Total;
        public string Hint = "";
        public List<Suggestion> Rows = new List<Suggestion>();
        public string Apply(string input, int index)
        {
            if (index < 0 || index >= Rows.Count || Start < 0 || Start + Length > input.Length) return input;
            return input.Substring(0, Start) + Rows[index].Value + input.Substring(Start + Length);
        }
    }

    public sealed class CompletionEngine
    {
        public readonly List<CommandSpec> Commands = new List<CommandSpec>();
        public readonly Dictionary<Kind, List<Suggestion>> Catalog = new Dictionary<Kind, List<Suggestion>>();
        public Func<string, bool> IsPlayer;
        internal int PlayerRevision { get; private set; }
        public const int Limit = 256;
        public CompletionEngine() { foreach (Kind k in Enum.GetValues(typeof(Kind))) Catalog[k] = new List<Suggestion>(); }

        public bool ReplacePlayers(IEnumerable<Suggestion> players)
        {
            // Provider order is not meaningful; compare names and aliases as well as account IDs.
            var next = players.OrderBy(p => p.Value, StringComparer.Ordinal).ToList();
            var current = Catalog[Kind.Player];
            bool same = current.Count == next.Count && current.Zip(next, (a, b) =>
                a.Value == b.Value && a.Label == b.Label && a.Description == b.Description &&
                a.Aliases.OrderBy(x => x, StringComparer.Ordinal).SequenceEqual(b.Aliases.OrderBy(x => x, StringComparer.Ordinal))).All(equal => equal);
            if (same) return false;
            // Replace objects instead of mutating them so an active Tab cycle keeps its snapshot.
            current.Clear(); current.AddRange(next); PlayerRevision++;
            return true;
        }

        public CompletionResult Complete(string input)
        {
            var r = new CompletionResult();
            if (string.IsNullOrEmpty(input) || (input[0] != '/' && input[0] != '@')) return r;
            int space = input.IndexOf(' ');
            if (space < 0)
            {
                r.Start = 1; r.Length = input.Length - 1;
                r.Hint = "原版指令 · Tab 补全/切换 · Shift+Tab 上一项 · 空格进入参数";
                Search(r, Commands.Select(c => new Suggestion(Kind.Command, c.Name, c.Name, c.Syntax + "  " + c.Description, c.Canonical, c.Description)), input.Substring(1));
                return r;
            }
            string command = input.Substring(1, space - 1);
            var spec = Commands.FirstOrDefault(c => string.Equals(c.Name, command, StringComparison.OrdinalIgnoreCase));
            if (spec == null) { r.Hint = "未识别的原版指令；第一版不补全服务器插件指令"; return r; }
            int argStart = space + 1;
            while (argStart < input.Length && input[argStart] == ' ') argStart++;
            string[] parts = input.Substring(argStart).Split('/');
            int index = parts.Length - 1;
            r.Start = input.LastIndexOf('/') >= argStart ? input.LastIndexOf('/') + 1 : argStart;
            r.Length = input.Length - r.Start;
            string query = input.Substring(r.Start).Trim();
            r.Hint = spec.Syntax + " · 参数用 / 分隔";
            var pool = new List<Suggestion>();
            List<Suggestion> choices;
            if (spec.Choices.TryGetValue(index, out choices)) pool.AddRange(choices);
            string name = spec.Canonical;
            bool explicitPlayer = index > 0 && IsPlayer != null && IsPlayer(parts[0].Trim());
            int valueIndex = explicitPlayer ? 1 : 0;
            switch (name)
            {
                case "give": case "vehicle": case "animal": case "quest":
                    if (index == 0) pool.AddRange(Catalog[Kind.Player]);
                    if (index == valueIndex)
                        pool.AddRange(Catalog[name == "give" ? Kind.Item : name == "vehicle" ? Kind.Vehicle : name == "animal" ? Kind.Animal : Kind.Quest]);
                    else if (name == "give" && index == valueIndex + 1) r.Hint = "数量：请自行填写整数（物品实际数量遵循原版规则）；不自动填数值";
                    break;
                case "teleport":
                    if (index <= 1) { pool.AddRange(Catalog[Kind.Player]); pool.AddRange(Catalog[Kind.Location]); }
                    break;
                case "experience": case "reputation": case "flag":
                    if (index == 0) pool.AddRange(Catalog[Kind.Player]);
                    r.Hint = spec.Syntax + " · 数值请自行填写；玩家可按名称搜索";
                    break;
                case "admin": case "unadmin": case "owner": case "unban": case "unpermit": case "permit": case "kill": case "kick": case "slay": case "ban": case "spy":
                    if (index == 0) pool.AddRange(Catalog[Kind.Player]);
                    break;
                case "weather": if (index == 0) pool.AddRange(Catalog[Kind.Weather]); break;
                case "loadout": if (index >= 1) pool.AddRange(Catalog[Kind.Item]); break;
                case "help":
                    if (index == 0) pool.AddRange(Commands.Select(c => new Suggestion(Kind.Command, c.Name, c.Name, c.Description, c.Canonical)));
                    break;
            }
            Search(r, pool, query);
            return r;
        }

        private struct SuggestionKey : IEquatable<SuggestionKey>
        {
            private readonly Kind kind;
            private readonly string value;
            public SuggestionKey(Suggestion row) { kind = row.Kind; value = row.Value ?? ""; }
            public bool Equals(SuggestionKey other) { return kind == other.kind && value == other.value; }
            public override bool Equals(object other) { return other is SuggestionKey && Equals((SuggestionKey)other); }
            public override int GetHashCode() { unchecked { return ((int)kind * 397) ^ StringComparer.Ordinal.GetHashCode(value); } }
        }

        private struct RankedSuggestion
        {
            public Suggestion Row;
            public int Score, Order;
            public RankedSuggestion(Suggestion row, int score, int order) { Row = row; Score = score; Order = order; }
        }

        private sealed class RankComparer : IComparer<RankedSuggestion>
        {
            public static readonly RankComparer Instance = new RankComparer();
            public int Compare(RankedSuggestion a, RankedSuggestion b)
            {
                int c = a.Score.CompareTo(b.Score);
                if (c == 0) c = ((int)a.Row.Kind).CompareTo((int)b.Row.Kind);
                if (c == 0) c = a.Row.Number.CompareTo(b.Row.Number);
                if (c == 0) c = StringComparer.OrdinalIgnoreCase.Compare(a.Row.Label, b.Row.Label);
                // LINQ's old OrderBy was stable, including duplicate identities.
                return c != 0 ? c : a.Order.CompareTo(b.Order);
            }
        }

        private static void KeepBest(List<RankedSuggestion> heap, RankedSuggestion value)
        {
            // A max heap retains exactly the best Limit rows, not the first Limit matches.
            var comparer = RankComparer.Instance;
            if (heap.Count < Limit)
            {
                int index = heap.Count;
                heap.Add(value);
                while (index > 0)
                {
                    int parent = (index - 1) / 2;
                    if (comparer.Compare(heap[parent], value) >= 0) break;
                    heap[index] = heap[parent]; index = parent;
                }
                heap[index] = value;
                return;
            }
            if (comparer.Compare(value, heap[0]) >= 0) return;
            int at = 0;
            while (at * 2 + 1 < heap.Count)
            {
                int child = at * 2 + 1;
                if (child + 1 < heap.Count && comparer.Compare(heap[child + 1], heap[child]) > 0) child++;
                if (comparer.Compare(value, heap[child]) >= 0) break;
                heap[at] = heap[child]; at = child;
            }
            heap[at] = value;
        }

        private static void Search(CompletionResult result, IEnumerable<Suggestion> pool, string input)
        {
            string q = Suggestion.Normalize(input);
            var unique = new Dictionary<SuggestionKey, RankedSuggestion>();
            int order = 0;
            foreach (var row in pool)
            {
                int position = order++;
                int score = row.Score(q);
                if (score >= 99) continue;
                var key = new SuggestionKey(row);
                var ranked = new RankedSuggestion(row, score, position);
                RankedSuggestion prior;
                // Sorting before GroupBy selected the best duplicate, not the first one seen.
                if (!unique.TryGetValue(key, out prior) || RankComparer.Instance.Compare(ranked, prior) < 0)
                    unique[key] = ranked;
            }
            result.Total = unique.Count;
            var best = new List<RankedSuggestion>(Math.Min(Limit, unique.Count));
            foreach (var ranked in unique.Values) KeepBest(best, ranked);
            best.Sort(RankComparer.Instance);
            foreach (var ranked in best) result.Rows.Add(ranked.Row);
        }
    }

    // Keeps the original replacement span when Tab cycles already-applied candidates.
    public sealed class CompletionSession
    {
        public CompletionResult Result = new CompletionResult();
        public int Selected = -1;
        private string basis, lastApplied;
        private int observedPlayerRevision;
        public void Observe(CompletionEngine engine, string text)
        {
            if (basis != null && (text == basis || text == lastApplied) &&
                (Selected >= 0 || observedPlayerRevision == engine.PlayerRevision)) return;
            observedPlayerRevision = engine.PlayerRevision;
            basis = text; lastApplied = null; Selected = -1; Result = engine.Complete(text);
        }
        public string Cycle(string text, bool backwards)
        {
            if (Result.Rows.Count == 0 || basis == null) return text;
            Selected = Selected < 0 ? (backwards ? Result.Rows.Count - 1 : 0) : (Selected + (backwards ? -1 : 1) + Result.Rows.Count) % Result.Rows.Count;
            lastApplied = Result.Apply(basis, Selected);
            return lastApplied;
        }
        public void Reset() { basis = lastApplied = null; Selected = -1; Result = new CompletionResult(); }
    }
}
