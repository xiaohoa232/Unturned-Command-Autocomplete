using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

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
            text = text ?? "";
            try { text = text.Normalize(NormalizationForm.FormKC); }
            catch (ArgumentException)
            {
                // A paste truncated at the chat limit can leave an unpaired surrogate.
                text = Encoding.UTF8.GetString(Encoding.UTF8.GetBytes(text)).Normalize(NormalizationForm.FormKC);
            }
            return text.Trim().ToLowerInvariant();
        }
        public int Score(string query)
        {
            if (query.Length == 0) return 3;
            int best = 99;
            foreach (string alias in Aliases)
            {
                if (alias == query) return 0;
                if (best > 1 && alias.StartsWith(query, StringComparison.Ordinal)) best = 1;
                else if (best > 2 && alias.IndexOf(query, StringComparison.Ordinal) >= 0) best = 2;
            }
            return best;
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
        internal bool IsPending;
        internal Func<string, bool> ResolveTeleportPlayer;
        internal string TeleportSource;

        internal bool CanSelect(Suggestion row)
        {
            return ResolveTeleportPlayer == null ||
                ((TeleportSource == null || ResolveTeleportPlayer(TeleportSource)) &&
                 (row.Kind == Kind.Player || !ResolveTeleportPlayer(row.Value)));
        }
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
            Catalog[Kind.Player] = next;
            PlayerRevision++;
            return true;
        }

        public CompletionResult Complete(string input)
        {
            return Prepare(input).Evaluate(CancellationToken.None);
        }

        // The live module publishes catalogs by replacement. A prepared query therefore
        // holds stable lists and never calls into Unity or resolves players on the worker.
        internal sealed class PreparedSearch
        {
            internal readonly CompletionResult Result = new CompletionResult();
            internal readonly List<ICollection<Suggestion>> Pools = new List<ICollection<Suggestion>>();
            internal string Input = "";
            internal int Count { get { return Pools.Sum(pool => pool.Count); } }

            internal CompletionResult Evaluate(CancellationToken cancellation)
            {
                if (Pools.Count > 0)
                    Search(Result, Pools.SelectMany(pool => pool), Input, cancellation);
                return Result;
            }
        }

        internal PreparedSearch Prepare(string input)
        {
            var search = new PreparedSearch();
            var r = search.Result;
            if (string.IsNullOrEmpty(input) || (input[0] != '/' && input[0] != '@')) return search;
            int space = input.IndexOf(' ');
            if (space < 0)
            {
                r.Start = 1; r.Length = input.Length - 1;
                r.Hint = "原版指令 · Tab 补全/切换 · Shift+Tab 上一项 · 空格进入参数";
                search.Pools.Add(Commands.Select(c => new Suggestion(Kind.Command, c.Name, c.Name, c.Syntax + "  " + c.Description, c.Canonical, c.Description)).ToArray());
                search.Input = input.Substring(1);
                return search;
            }
            string command = input.Substring(1, space - 1);
            var spec = Commands.FirstOrDefault(c => string.Equals(c.Name, command, StringComparison.OrdinalIgnoreCase));
            if (spec == null) { r.Hint = "未识别的原版指令；第一版不补全服务器插件指令"; return search; }
            int argStart = space + 1;
            while (argStart < input.Length && input[argStart] == ' ') argStart++;
            string[] parts = input.Substring(argStart).Split('/');
            int index = parts.Length - 1;
            r.Start = input.LastIndexOf('/') >= argStart ? input.LastIndexOf('/') + 1 : argStart;
            r.Length = input.Length - r.Start;
            string query = input.Substring(r.Start).Trim();
            r.Hint = spec.Syntax + " · 参数用 / 分隔";
            search.Input = query;
            var pools = search.Pools;
            string name = spec.Canonical;
            bool explicitPlayer = index > 0 && IsPlayer != null &&
                (name == "give" || name == "vehicle" || name == "animal" || name == "quest" || name == "teleport") &&
                IsPlayer(parts[0].Trim());
            if (name == "teleport")
            {
                if (index > 1) return search;
                if (index == 1 && !explicitPlayer)
                {
                    r.Hint = "两段传送指令的第一段必须是在线玩家";
                    return search;
                }
                r.ResolveTeleportPlayer = IsPlayer;
                r.TeleportSource = index == 1 ? parts[0].Trim() : null;
            }
            List<Suggestion> choices;
            if (spec.Choices.TryGetValue(index, out choices))
                pools.Add(name == "teleport" && IsPlayer != null
                    ? choices.Where(choice => !IsPlayer(choice.Value)).ToArray() : (ICollection<Suggestion>)choices);
            int valueIndex = explicitPlayer ? 1 : 0;
            switch (name)
            {
                case "give": case "vehicle": case "animal": case "quest":
                    if (index == 0) pools.Add(Catalog[Kind.Player]);
                    if (index == valueIndex)
                        pools.Add(Catalog[name == "give" ? Kind.Item : name == "vehicle" ? Kind.Vehicle : name == "animal" ? Kind.Animal : Kind.Quest]);
                    else if (name == "give" && index == valueIndex + 1) r.Hint = "数量：请自行填写整数（物品实际数量遵循原版规则）；不自动填数值";
                    break;
                case "teleport":
                    pools.Add(Catalog[Kind.Player]);
                    pools.Add(IsPlayer == null ? (ICollection<Suggestion>)Catalog[Kind.Location] :
                        Catalog[Kind.Location].Where(location => !IsPlayer(location.Value)).ToArray());
                    break;
                case "experience": case "reputation": case "flag":
                    if (index == 0) pools.Add(Catalog[Kind.Player]);
                    r.Hint = spec.Syntax + " · 数值请自行填写；玩家可按名称搜索";
                    break;
                case "admin": case "unadmin": case "owner": case "unban": case "unpermit": case "permit": case "kill": case "kick": case "slay": case "ban": case "spy":
                    if (index == 0) pools.Add(Catalog[Kind.Player]);
                    break;
                case "weather": if (index == 0) pools.Add(Catalog[Kind.Weather]); break;
                case "loadout": if (index >= 1) pools.Add(Catalog[Kind.Item]); break;
                case "help":
                    if (index == 0) pools.Add(Commands.Select(c => new Suggestion(Kind.Command, c.Name, c.Name, c.Description, c.Canonical)).ToArray());
                    break;
            }
            return search;
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

        private static void Search(CompletionResult result, IEnumerable<Suggestion> pool, string input, CancellationToken cancellation)
        {
            string q = Suggestion.Normalize(input);
            var unique = new Dictionary<SuggestionKey, RankedSuggestion>();
            int order = 0;
            foreach (var row in pool)
            {
                int position = order++;
                if ((position & 255) == 0) cancellation.ThrowIfCancellationRequested();
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
            foreach (var ranked in unique.Values)
            {
                if ((order++ & 255) == 0) cancellation.ThrowIfCancellationRequested();
                KeepBest(best, ranked);
            }
            best.Sort(RankComparer.Instance);
            foreach (var ranked in best) result.Rows.Add(ranked.Row);
        }
    }

    // Keeps the original replacement span when clicking or cycling candidates.
    public sealed class CompletionSession
    {
        public CompletionResult Result = new CompletionResult();
        public int Selected = -1;
        private string basis, lastApplied;
        private int observedPlayerRevision;
        private string pendingText;
        private int pendingRevision;
        private Task<CompletionResult> pending;
        private CancellationTokenSource pendingCancellation;

        public void Observe(CompletionEngine engine, string text)
        {
            Observe(engine, text, false);
        }

        internal void Observe(CompletionEngine engine, string text, bool deferLarge)
        {
            if (basis != null && (text == basis || text == lastApplied) &&
                (Selected >= 0 || observedPlayerRevision == engine.PlayerRevision)) return;
            int revision = engine.PlayerRevision;
            if (pending != null && text == pendingText && revision == pendingRevision)
            {
                if (!pending.IsCompleted) return;
                var completed = pending;
                ReleasePending(false);
                var result = completed.GetAwaiter().GetResult();
                Commit(text, revision, result);
                return;
            }

            Reset();
            var search = engine.Prepare(text);
            if (deferLarge && search.Count > 10000)
            {
                pendingText = text;
                pendingRevision = revision;
                pendingCancellation = new CancellationTokenSource();
                CancellationToken token = pendingCancellation.Token;
                pending = Task.Run(() => search.Evaluate(token), token);
                Result.Hint = "正在搜索候选…";
                Result.IsPending = true;
                return;
            }
            Commit(text, revision, search.Evaluate(CancellationToken.None));
        }

        private void Commit(string text, int revision, CompletionResult result)
        {
            basis = text;
            observedPlayerRevision = revision;
            Result = result;
        }

        private void ReleasePending(bool cancel)
        {
            if (pending == null) return;
            var source = pendingCancellation;
            if (cancel) source.Cancel();
            // Superseded searches may fault after the UI has moved on.
            pending.ContinueWith(task =>
            {
                if (task.IsFaulted) GC.KeepAlive(task.Exception);
                source.Dispose();
            }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            pending = null;
            pendingCancellation = null;
            pendingText = null;
        }
        public string Cycle(string text, bool backwards)
        {
            string completed;
            return TryCycle(text, backwards, int.MaxValue, out completed) ? completed : text;
        }

        internal bool TryCycle(string text, bool backwards, int maxLength, out string completed)
        {
            int count = Result.Rows.Count;
            int direction = backwards ? -1 : 1;
            int index = Selected < 0 ? (backwards ? 0 : -1) : Selected;
            completed = text;
            if (!CanSelect(text, Result)) return false;
            for (int attempt = 0; attempt < count; attempt++)
            {
                index = (index + direction + count) % count;
                if (TrySelect(text, Result, index, maxLength, out completed)) return true;
            }
            return false;
        }

        private bool CanSelect(string text, CompletionResult displayed)
        {
            return ReferenceEquals(displayed, Result) && basis != null && (text == basis || text == lastApplied);
        }

        internal bool TrySelect(string text, CompletionResult displayed, int index, int maxLength, out string completed)
        {
            completed = text;
            if (!CanSelect(text, displayed) || index < 0 || index >= Result.Rows.Count ||
                !Result.CanSelect(Result.Rows[index])) return false;
            string value = Result.Apply(basis, index);
            if (value.Length > maxLength) return false;
            Selected = index;
            completed = lastApplied = value;
            return true;
        }

        public void Reset()
        {
            ReleasePending(true);
            basis = lastApplied = null;
            Selected = -1;
            Result = new CompletionResult();
        }
    }
}
