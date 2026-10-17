using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using New = UnturnedCommandAutocomplete;
using Old = LegacyCommandAutocomplete;

class SearchParityTests
{
    static int comparisons, cycles;
    static void Require(bool ok, string reason) { if (!ok) throw new Exception(reason); }

    static Old.Suggestion Copy(New.Suggestion s)
    {
        var copy = new Old.Suggestion((Old.Kind)(int)s.Kind, s.Value, s.Label, s.Description);
        copy.Number = s.Number; copy.Aliases = (string[])s.Aliases.Clone();
        return copy;
    }
    static Old.CompletionEngine Copy(New.CompletionEngine current)
    {
        var old = new Old.CompletionEngine();
        foreach (var pair in current.Catalog) old.Catalog[(Old.Kind)(int)pair.Key].AddRange(pair.Value.Select(Copy));
        foreach (var c in current.Commands)
        {
            var command = new Old.CommandSpec(c.Name, c.Canonical, c.Syntax, c.Description);
            foreach (var pair in c.Choices) command.Choices[pair.Key] = pair.Value.Select(Copy).ToList();
            old.Commands.Add(command);
        }
        old.IsPlayer = current.IsPlayer;
        return old;
    }
    static void Equal(New.CompletionResult a, Old.CompletionResult b, string context)
    {
        comparisons++;
        Require(a.Start == b.Start && a.Length == b.Length && a.Total == b.Total && a.Hint == b.Hint && a.Rows.Count == b.Rows.Count,
            "Result metadata differs: " + context);
        for (int i = 0; i < a.Rows.Count; i++)
        {
            var x = a.Rows[i]; var y = b.Rows[i];
            Require((int)x.Kind == (int)y.Kind && x.Value == y.Value && x.Label == y.Label && x.Description == y.Description &&
                x.Number == y.Number && x.Aliases.SequenceEqual(y.Aliases), "Ranked row differs: " + context + " row " + i);
        }
    }
    static void Compare(New.CompletionEngine current, Old.CompletionEngine old, string input)
    {
        var a = current.Complete(input); var b = old.Complete(input);
        Equal(a, b, input);
        for (int i = 0; i < a.Rows.Count; i++) Require(a.Apply(input, i) == b.Apply(input, i), "Replacement differs: " + input);
    }
    static New.CompletionEngine Fixture()
    {
        var e = new New.CompletionEngine();
        foreach (string name in new[] { "give", "vehicle", "animal", "quest", "weather", "teleport", "experience", "reputation", "flag", "kick", "ban", "permit", "help", "loadout", "time", "day", "night" })
            e.Commands.Add(new New.CommandSpec(name, name, name + " syntax", "说明 " + name));
        e.Catalog[New.Kind.Player].Add(new New.Suggestion(New.Kind.Player, "100", "Alice", "", "Alice", "队友"));
        e.Catalog[New.Kind.Player].Add(new New.Suggestion(New.Kind.Player, "200", "Bob", "", "Bob"));
        e.IsPlayer = name => name == "Alice" || name == "100" || name == "Bob" || name == "200";
        var random = new Random(20261017);
        var allKinds = new List<New.Suggestion>();
        for (int i = 0; i < 900; i++)
        {
            New.Kind kind = (New.Kind)(i % 9);
            string value = i % 4 == 0 ? (i / 4).ToString() : (i % 181).ToString();
            byte[] bytes = new byte[16]; random.NextBytes(bytes); var guid = new Guid(bytes);
            string label = new[] { "物品 ", "Maple ", "ALPHA ", "alpha ", "CAFÉ ", "丙 " }[i % 6] + i;
            var row = new New.Suggestion(kind, value, label, "row=" + i, "alias_" + i, "全角ＡＢＣ", guid.ToString("N"), guid.ToString());
            row.Number = i % 41 == 0 ? int.MinValue : i % 43 == 0 ? int.MaxValue : (i % 13) - 6;
            e.Catalog[kind].Add(row); allKinds.Add(row);
        }
        // Mixed kinds and duplicates exercise all sorting levels in a real Complete path.
        e.Commands.First(c => c.Canonical == "give").Choices[0] = allKinds;
        var items = e.Catalog[New.Kind.Item];
        items.Add(new New.Suggestion(New.Kind.Item, "late", "same", "earlier", "tie"));
        items.Add(new New.Suggestion(New.Kind.Item, "late", "SAME", "later", "tie"));
        items.Add(new New.Suggestion(New.Kind.Item, "best", "Z first", "first duplicate", "containsneedle"));
        items.Add(new New.Suggestion(New.Kind.Item, "best", "A later", "better duplicate", "needle"));
        items.Add(new New.Suggestion(New.Kind.Item, "nullLabel", null, "", "tie"));
        items.Add(new New.Suggestion(New.Kind.Item, null, "null value", "", "tie"));
        items.Add(new New.Suggestion(New.Kind.Item, "", "empty value", "", "tie"));
        e.Catalog[New.Kind.Location].Add(new New.Suggestion(New.Kind.Location, "Town Hall", "Town Hall", "", "市政厅"));
        return e;
    }
    static void DifferentialChecks()
    {
        var e = Fixture(); var old = Copy(e);
        string[] queries = { "", "6", "63", "633", "6333", "63335", "0", "1", "９", "ＡＢＣ", "全角", "maple", "MAPLE", "CAFÉ", "café", "物品", "丙", "alias_", "needle", "tie", "Alice", "100", "Town", "zz_no_match", "\t", " 6 ", "a", "-", "000" };
        foreach (var command in e.Commands)
            foreach (string query in queries)
                foreach (string prefix in new[] { "/", "@" }) Compare(e, old, prefix + command.Name + " " + query);
        foreach (string input in new[] { null, "", "hello", "/", "@", "/GIV", "/unknown 6", "/give Alice/6", "/give 100/6/2", "/give 6/2", "/give  6", "/teleport Alice/Town", "/loadout 1/6", "/give 6/", "/give ６", "/give needle", "/give tie" })
            Compare(e, old, input);
        foreach (var row in e.Catalog.Values.SelectMany(x => x).Where(x => x.Aliases.Length >= 6).Take(12))
        {
            string guid = row.Aliases[row.Aliases.Length - 1];
            foreach (string query in new[] { guid, guid.Substring(0, 6), guid.Substring(9, 7) }) Compare(e, old, "/give " + query);
        }
        Compare(e, old, "/give cafe\u0301");
        var random = new Random(413);
        var suggestions = e.Catalog.Values.SelectMany(x => x).ToArray();
        for (int i = 0; i < 1200; i++)
        {
            string alias = suggestions[random.Next(suggestions.Length)].Aliases[random.Next(2)];
            int start = random.Next(alias.Length + 1);
            string query = alias.Substring(start, random.Next(alias.Length - start + 1));
            Compare(e, old, "/give " + query);
        }
        foreach (string query in queries)
            foreach (var row in suggestions)
                Require(row.Score(New.Suggestion.Normalize(query)) == Copy(row).Score(Old.Suggestion.Normalize(query)), "Alias scoring differs");

        // A better duplicate appears after >256 earlier matches; both counts and winner must survive.
        var many = new New.CompletionEngine(); many.Commands.Add(new New.CommandSpec("give", "give", "give", ""));
        for (int i = 0; i < 1500; i++)
        {
            var row = new New.Suggestion(New.Kind.Item, (i % 1100).ToString(), "row " + i, "", "match");
            row.Number = 1500 - i; many.Catalog[New.Kind.Item].Add(row);
        }
        many.Catalog[New.Kind.Item].Add(new New.Suggestion(New.Kind.Item, "0", "A exact", "late best", "match"));
        old = Copy(many);
        Compare(many, old, "/give match"); Compare(many, old, "/give ");
        Require(many.Complete("/give match").Total == 1100, "Unique count changed at candidate limit");
        foreach (var row in many.Catalog[New.Kind.Item]) { row.Number = 0; row.Label = "same"; }
        old = Copy(many); Compare(many, old, "/give match");

        var a = new New.CompletionSession(); var b = new Old.CompletionSession();
        e = Fixture(); old = Copy(e);
        foreach (string input in new[] { "/give ", "/give 6", "/give needle", "/give tie", "/teleport Town", "hello", "/give Alice/6" })
        {
            a.Reset(); b.Reset(); a.Observe(e, input); b.Observe(old, input); Equal(a.Result, b.Result, "session " + input);
            string current = input;
            for (int i = 0; i < 600; i++)
            {
                bool backwards = i % 5 == 0;
                string next = a.Cycle(current, backwards), expected = b.Cycle(current, backwards);
                Require(next == expected && a.Selected == b.Selected, "Tab behavior differs"); cycles++;
                a.Observe(e, next); b.Observe(old, expected); current = next;
            }
        }
        Console.WriteLine("PARITY: " + comparisons + " result comparisons passed; " + cycles + " forward/reverse Tab steps passed; all alias scoring comparisons passed.");
    }
    static double Median(Action action)
    {
        var samples = new double[21];
        for (int i = 0; i < samples.Length; i++)
        {
            long begin = Stopwatch.GetTimestamp(); action();
            samples[i] = (Stopwatch.GetTimestamp() - begin) * 1000.0 / Stopwatch.Frequency;
        }
        Array.Sort(samples); return samples[10];
    }
    static void PerformanceChecks()
    {
        var e = new New.CompletionEngine(); e.Commands.Add(new New.CommandSpec("give", "give", "give", ""));
        var random = new Random(20261017);
        for (int i = 1; i <= 5276; i++)
        {
            int id = i == 5276 ? 63335 : i;
            byte[] bytes = new byte[16]; random.NextBytes(bytes); var guid = new Guid(bytes);
            string number = id.ToString(), chinese = "测试物品" + number, english = "Synthetic Item " + number, internalName = "Synthetic_Item_" + number;
            var row = new New.Suggestion(New.Kind.Item, number, "物品 · " + chinese + " · " + english + "  [" + number + "]", internalName,
                chinese, english, internalName, number, guid.ToString("N"), guid.ToString());
            row.Number = id; e.Catalog[New.Kind.Item].Add(row);
        }
        var old = Copy(e);
        e.Complete("/give 6"); old.Complete("/give 6");
        Console.WriteLine("PERFORMANCE: 5276 synthetic items, 21 warm samples/query, same full alias set.");
        foreach (string query in new[] { "", "6", "63", "633", "6333", "63335", "测试", "Synthetic", "zz_no_match" })
        {
            string text = "/give " + query; Compare(e, old, text);
            double before = Median(() => old.Complete(text));
            double after = Median(() => e.Complete(text));
            Console.WriteLine("Query=" + query + "; Matches=" + e.Complete(text).Total + "; OldMedianMs=" + before.ToString("F3") + "; NewMedianMs=" + after.ToString("F3") + "; Speedup=" + (before / after).ToString("F2"));
            if (query == "6") Require(after < before / 2, "Broad numeric search did not improve enough");
        }
    }
    static void Main(string[] args) { DifferentialChecks(); if (!args.Contains("--parity-only")) PerformanceChecks(); }
}
