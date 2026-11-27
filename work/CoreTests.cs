using System;
using System.Linq;
using System.Collections.Generic;
using UnturnedCommandAutocomplete;
class CoreTests
{
    static int tests;
    static void Check(bool ok, string title)
    {
        tests++;
        if (!ok)
        {
            Console.Error.WriteLine("FAIL " + title);
            Environment.Exit(1);
        }
        Console.WriteLine("PASS " + title);
    }
    static Suggestion Person(string id, string name, params string[] aliases)
    {
        return new Suggestion(Kind.Player, id, "Player " + name, "", new[] { name }.Concat(aliases).ToArray());
    }
    static void PlayerRefreshTests()
    {
        // Short IDs are synthetic fixtures, never real account identifiers.
        var e = new CompletionEngine();
        e.Commands.Add(new CommandSpec("kick", "kick", "kick", ""));
        e.Commands.Add(new CommandSpec("give", "give", "give", ""));
        e.Catalog[Kind.Item].Add(new Suggestion(Kind.Item, "363", "Maple", ""));
        e.IsPlayer = name => e.Catalog[Kind.Player].Any(p => p.Aliases.Contains(Suggestion.Normalize(name)));
        e.ReplacePlayers(new[] { Person("100", "Alice"), Person("200", "Bob") });
        var s = new CompletionSession(); s.Observe(e, "/kick "); var before = s.Result;
        e.ReplacePlayers(new[] { Person("200", "Bob"), Person("100", "Alice") });
        s.Observe(e, "/kick ");
        Check(Object.ReferenceEquals(before, s.Result), "provider reorder does not rescan unchanged candidates");
        e.ReplacePlayers(new[] { Person("100", "Alice"), Person("200", "Bob"), Person("300", "Charlie") });
        s.Observe(e, "/kick ");
        Check(s.Selected == -1 && s.Result.Rows.Count == 3 && s.Result.Rows.Any(p => p.Value == "300"), "joining player refreshes unchanged unselected input");
        e.ReplacePlayers(new[] { Person("200", "Bob"), Person("300", "Charlie") });
        s.Observe(e, "/kick ");
        Check(s.Result.Rows.Count == 2 && s.Result.Rows.All(p => p.Value != "100"), "leaving player disappears before first Tab");
        Check(s.Cycle("/kick ", false) == "/kick 200", "first Tab uses refreshed player list");
        s.Reset(); s.Observe(e, "/kick ");
        e.ReplacePlayers(new Suggestion[0]); s.Observe(e, "/kick ");
        Check(s.Result.Rows.Count == 0 && s.Cycle("/kick ", false) == "/kick ", "all players leaving clears unselected candidates");
        e.ReplacePlayers(new[] { Person("100", "Alice") }); s.Observe(e, "/kick ");
        Check(s.Result.Rows.Single().Value == "100" && s.Selected == -1, "empty results refresh when a player joins");

        s.Observe(e, "/kick Ali");
        e.ReplacePlayers(new[] { Person("100", "Alicia") }); s.Observe(e, "/kick Ali");
        Check(s.Result.Rows.Single().Label == "Player Alicia", "renamed player updates visible label with same account ID");
        s.Observe(e, "/kick Alice");
        Check(s.Result.Rows.Count == 0, "old name no longer matches after rename");
        s.Observe(e, "/kick Crew");
        e.ReplacePlayers(new[] { Person("100", "Alicia", "Crew") }); s.Observe(e, "/kick Crew");
        Check(s.Result.Rows.Single().Value == "100", "nickname change refreshes matching even when label is unchanged");
        e.ReplacePlayers(new[] { Person("100", "Alicia", "Squad") }); s.Observe(e, "/kick Crew");
        Check(s.Result.Rows.Count == 0, "removed nickname no longer matches unchanged input");
        e.ReplacePlayers(new[] { Person("100", "Alicia", "Squad", "Crew") }); s.Observe(e, "/kick Crew");
        before = s.Result;
        e.ReplacePlayers(new[] { Person("100", "Alicia", "Crew", "Squad") }); s.Observe(e, "/kick Crew");
        Check(Object.ReferenceEquals(before, s.Result), "equivalent alias order does not trigger rescan");
        e.ReplacePlayers(new[] { Person("100", "Alicia", "Crew", "Squad") }); s.Observe(e, "/kick Crew");
        Check(Object.ReferenceEquals(before, s.Result), "identical player refresh does not rescan each second");

        e.ReplacePlayers(new[] { Person("100", "Alice"), Person("200", "Bob"), Person("250", "Dale") });
        s.Reset(); s.Observe(e, "/kick "); before = s.Result;
        string first = s.Cycle("/kick ", false);
        e.ReplacePlayers(new[] { Person("200", "Bob"), Person("250", "Dale"), Person("300", "Charlie") }); s.Observe(e, first);
        Check(Object.ReferenceEquals(before, s.Result) && s.Selected == 0, "active Tab cycle retains original snapshot after player change");
        string second = s.Cycle(first, false); s.Observe(e, second);
        Check(first == "/kick 100" && second == "/kick 200", "forward Tab order stays stable during player change");
        string completed;
        Check(!s.TrySelect(second, before, 0, 512, out completed), "mouse click skips a player who left the displayed snapshot");
        string wrapped = s.Cycle(second, true);
        Check(wrapped == "/kick 250", "Shift Tab skips the departed player and wraps to the last still-online candidate");
        Check(s.Cycle(wrapped, false) == second, "forward wrap skips departed players and keeps the snapshot order");
        s.Observe(e, "/kick ");
        Check(s.Selected == -1 && !Object.ReferenceEquals(before, s.Result) &&
              s.Result.Rows.Select(row => row.Value).OrderBy(value => value).SequenceEqual(new[] { "200", "250", "300" }),
              "backspacing to the original query resets selection and rebuilds the player list");
        s.Observe(e, "/kick Ch");
        Check(s.Selected == -1 && s.Result.Rows.Single().Value == "300", "editing ends snapshot and searches current players");
        s.Reset(); s.Observe(e, "/kick ");
        Check(s.Selected == -1 && s.Result.Rows.Count == 3 && s.Result.Rows.All(p => p.Value != "100"), "chat reopen uses current players");
        s.Reset(); s.Observe(e, "/kick ");
        Check(s.Cycle("/kick ", true) == "/kick 250", "first Shift Tab starts from refreshed last candidate");

        s.Reset(); s.Observe(e, "/give Alice/");
        Check(s.Result.Rows.Count == 0, "absent player name is not treated as explicit player");
        e.ReplacePlayers(new[] { Person("100", "Alice") }); s.Observe(e, "/give Alice/");
        Check(s.Result.Rows.Single().Kind == Kind.Item, "player join refreshes explicit-player parameter interpretation");
        e.ReplacePlayers(new Suggestion[0]); s.Observe(e, "/give Alice/");
        Check(s.Result.Rows.Count == 0, "player leave refreshes explicit-player parameter interpretation");
    }
    static void Main()
    {
        var e = new CompletionEngine();
        foreach (string name in new[] {"give","vehicle","teleport","experience","reputation","flag","animal","quest","time","day","night","filter","loadout","weather","help","kick","permit"})
            e.Commands.Add(new CommandSpec(name,name,name,""));
        e.Catalog[Kind.Item].Add(new Suggestion(Kind.Item,"363","物品 · 枫木步枪 [363]","","枫木步枪","Maplestrike","Rifle_Maple"));
        e.Catalog[Kind.Item].Add(new Suggestion(Kind.Item,"6","物品 · 军用弹匣 [6]","","军用弹匣","Military Magazine"));
        e.Catalog[Kind.Vehicle].Add(new Suggestion(Kind.Vehicle,"1","载具 · 越野车 [1]","","Offroader","越野车"));
        e.Catalog[Kind.Player].Add(new Suggestion(Kind.Player,"76561190000000001","玩家 · Alice","","Alice"));
        e.Catalog[Kind.Location].Add(new Suggestion(Kind.Location,"欣海市","地点 · 欣海市","","欣海市"));
        e.Catalog[Kind.Location].Add(new Suggestion(Kind.Location,"Town Hall","地点 · Town Hall","","Town Hall"));
        e.IsPlayer = name => name == "Alice" || name == "76561190000000001";
        Check(e.Complete("hello").Rows.Count == 0,"normal chat untouched");
        Check(e.Complete("").Rows.Count == 0,"empty chat untouched");
        Check(e.Complete("/").Rows.Count == e.Commands.Count,"slash lists registered commands");
        Check(e.Complete("@gi").Rows.Single().Value == "give","at prefix");
        Check(e.Complete("/GIV").Rows.Single().Value == "give","case independent");
        Check(e.Complete("/give 枫木").Apply("/give 枫木",0) == "/give 363","Chinese name becomes ID");
        Check(e.Complete("/give maplestrike").Rows.Single().Value == "363","English name matching");
        Check(e.Complete("/give Rifle_Maple").Rows.Single().Value == "363","internal name matching");
        Check(e.Complete("/give 363").Rows.Single().Value == "363","numeric matching");
        Check(e.Complete("/give ３６３").Rows.Single().Value == "363","full-width numeric matching");
        var playerItems = e.Complete("/give Alice/").Rows;
        Check(playerItems.Count == 2 && playerItems.All(row => row.Kind == Kind.Item), "explicit player followed by a nonempty list of items");
        Check(e.Complete("/give 76561190000000001/枫木").Apply("/give 76561190000000001/枫木",0)=="/give 76561190000000001/363","preserves explicit player");
        Check(e.Complete("/give 363/").Rows.Count == 0,"self quantity not confused with item");
        Check(e.Complete("/give Alice/363/").Rows.Count == 0,"explicit quantity never filled");
        Check(e.Complete("/give 363/5").Rows.Count == 0,"existing quantity preserved");
        Check(e.Complete("/vehicle 越野").Rows.Single().Value == "1","vehicle search");
        Check(e.Complete("/vehicle 1/").Rows.Count == 0,"vehicle has no count argument");
        Check(e.Complete("/teleport 欣海").Apply("/teleport 欣海",0)=="/teleport 欣海市","location name insertion");
        Check(e.Complete("/teleport Alice/Town").Apply("/teleport Alice/Town",0)=="/teleport Alice/Town Hall","locations with spaces");
        Check(e.Complete("/kick Ali").Rows.Single().Value == "76561190000000001","player becomes unambiguous account ID");
        Check(e.Complete("/kick Alice/reason").Rows.Count == 0,"free text reason not replaced");
        Check(e.Complete("/permit Alice/note").Rows.Count == 0,"permit note not replaced");
        Check(e.Complete("/time day").Rows.Count == 0,"time does not suggest invalid day string");
        Check(e.Complete("/filter ").Rows.Count == 0,"toggle command gets no boolean argument");
        Check(e.Complete("/loadout 2/枫木").Rows.Single().Value == "363","loadout item parameter");
        foreach (string valid in new[] { "0", "10", "255", " 2 " })
            Check(e.Complete("/loadout " + valid + "/枫木").Rows.Single().Value == "363", "loadout accepts skillset " + valid);
        foreach (string invalid in new[] { "", "-1", "11", "254", "256", "x" })
            Check(e.Complete("/loadout " + invalid + "/枫木").Rows.Count == 0, "loadout rejects skillset [" + invalid + "]");
        Check(e.Complete("/help gi").Rows.Single().Value == "give","help command name completion");
        Check(e.Complete("/plugin anything").Rows.Count == 0,"unknown plugin untouched");
        Check(e.Complete("/give   枫木").Apply("/give   枫木",0)=="/give   363","whitespace preserved");
        var s = new CompletionSession(); s.Observe(e,"/give "); var original = s.Result;
        s.Observe(e,"/give "); Check(Object.ReferenceEquals(original,s.Result),"unchanged input does not rescan per frame");
        string first = s.Cycle("/give ",false); s.Observe(e,first);
        string second = s.Cycle(first,false); Check(first != second,"successive Tab cycles frozen original matches");
        s.Observe(e,second); Check(s.Cycle(second,true)==first,"Shift Tab reverses");
        s.Observe(e,"/give 枫木"); Check(s.Selected==-1 && s.Result.Rows.Count==1,"typing resets cycle");
        s.Reset(); s.Observe(e,"hello"); Check(s.Cycle("hello",false)=="hello","Tab on normal chat unchanged");
        var commandSession=new CompletionSession(); commandSession.Observe(e,"/");
        string a=commandSession.Cycle("/",false); commandSession.Observe(e,a);
        Check(commandSession.Cycle(a,false)!=a,"command cycling survives applied text");
        PlayerRefreshTests();
        TeleportTests();
        ReviewRegressionTests();
        var big=new CompletionEngine(); big.Commands.Add(new CommandSpec("give","give","give",""));
        for(int i=1;i<=5000;i++) big.Catalog[Kind.Item].Add(new Suggestion(Kind.Item,i.ToString(),"Item "+i,""));
        var watch=System.Diagnostics.Stopwatch.StartNew(); var many=big.Complete("/give ");watch.Stop();
        Check(many.Total==5000 && many.Rows.Count==CompletionEngine.Limit,"bounded candidate list retains full count");
        Console.WriteLine("RESULT: "+tests+" passed; 5000-entry search "+watch.ElapsedMilliseconds+"ms");
    }

    static void TeleportTests()
    {
        var engine = new CompletionEngine();
        var teleport = new CommandSpec("teleport", "teleport", "", "");
        teleport.Choices[0] = new List<Suggestion>
        {
            new Suggestion(Kind.Choice, "床", "Bed", ""),
            new Suggestion(Kind.Choice, "路标", "Waypoint", "")
        };
        teleport.Choices[1] = teleport.Choices[0];
        engine.Commands.Add(teleport);
        var names = new[] { "Town Hall", "Town", "North", "NORTH", "床", "路标", "欣海市", " With Padding", "Bad/Name" };
        foreach (var step in engine.AddTeleportLocations(names,
            (input, name) => name.IndexOf(input, StringComparison.InvariantCultureIgnoreCase) >= 0)) { }
        var locations = engine.Catalog[Kind.Location];
        Check(locations.Select(row => row.Value).SequenceEqual(new[] { "Town Hall", "床", "路标", "欣海市" }),
            "unaddressable and ambiguous map nodes are excluded");
        engine.ReplacePlayers(new[] { Person("100", "Alice"), Person("200", "Bob") });
        engine.IsPlayer = text => engine.Catalog[Kind.Player].Any(row => row.Aliases.Contains(Suggestion.Normalize(text)));

        Check(engine.Complete("/teleport 床").Rows.Single().Kind == Kind.Choice, "localized bed token shadows its same-name map location");
        Check(engine.Complete("/teleport Alice/ 路标").Rows.Single().Kind == Kind.Choice,
            "localized waypoint token shadows its same-name map location in the second argument");
        const string one = "/teleport   欣海";
        const string two = "/teleport   Alice  /  欣海";
        Check(engine.Complete(one).Apply(one, 0) == "/teleport 欣海市", "single-argument teleport removes extra separator whitespace");
        Check(engine.Complete(two).Apply(two, 0) == "/teleport Alice/欣海市", "two-argument teleport normalizes the source and destination separators");

        var session = new CompletionSession();
        const string toBob = "/teleport Alice/Bob";
        session.Observe(engine, toBob);
        var displayed = session.Result;
        Check(displayed.Rows.Single().Value == "200", "online teleport destination is offered");
        engine.ReplacePlayers(new[] { Person("100", "Alice") });
        string completed;
        Check(!session.TrySelect(toBob, displayed, 0, 512, out completed), "stale teleport destination cannot be selected");
        session.Observe(engine, "/teleport Alice/欣海");
        engine.ReplacePlayers(new Suggestion[0]);
        Check(!session.TryCycle("/teleport Alice/欣海", false, 512, out completed), "stale teleport source blocks destination selection");
    }

    static void ReviewRegressionTests()
    {
        var engine = new CompletionEngine();
        engine.Commands.Add(new CommandSpec("give", "give", "", ""));
        engine.Commands.Add(new CommandSpec("vehicle", "vehicle", "", ""));
        engine.Commands.Add(new CommandSpec("teleport", "teleport", "", ""));
        engine.Catalog[Kind.Item].Add(new Suggestion(Kind.Item, "363", "Maple", ""));
        engine.Catalog[Kind.Vehicle].Add(new Suggestion(Kind.Vehicle, "1", "First", "") { Number = 1 });
        engine.Catalog[Kind.Vehicle].Add(new Suggestion(Kind.Vehicle, new string('a', 32), "Long", "") { Number = 2 });
        engine.Catalog[Kind.Vehicle].Add(new Suggestion(Kind.Vehicle, "3", "Third", "") { Number = 3 });
        engine.Catalog[Kind.Location].Add(new Suggestion(Kind.Location, "Base", "Base", ""));
        engine.Catalog[Kind.Location].Add(new Suggestion(Kind.Location, "Town", "Town", ""));
        engine.Catalog[Kind.Player].Add(Person("100", "Alice"));
        var session = new CompletionSession();
        session.Observe(engine, "/give 36");
        var previous = session.Result;
        engine.IsPlayer = name => { throw new InvalidOperationException("Synthetic player lookup failure"); };
        bool threw = false;
        try { session.Observe(engine, "/teleport Alice/"); }
        catch (InvalidOperationException) { threw = true; }
        string completed;
        Check(threw && session.Result.Rows.Count == 0 && !session.TrySelect("/teleport Alice/", previous, 0, 512, out completed),
            "failed search invalidates the previous replacement span");
        bool collision = false;
        engine.IsPlayer = name => name == "Alice" || name == "100" || (collision && name == "Base");
        session.Observe(engine, "/teleport Alice/");
        Check(session.TryCycle("/teleport Alice/", false, 512, out completed) && completed.StartsWith("/teleport Alice/"),
            "same input is searched again after recovery");
        Check(engine.Complete("/give " + new string((char)0xD800, 1)).Rows.Count == 0, "truncated UTF-16 does not abort search");

        Check(engine.Complete("/teleport missing/").Rows.Count == 0, "invalid teleport source offers no destination");
        session.Observe(engine, "/teleport Base");
        var displayed = session.Result;
        Check(displayed.Rows.Single().Kind == Kind.Location, "unambiguous location is offered");
        collision = true;
        Check(!session.TrySelect("/teleport Base", displayed, 0, 512, out completed), "player collision after painting blocks the stale location");
        Check(engine.Complete("/teleport Base").Rows.All(row => row.Kind != Kind.Location), "player name takes precedence over a same-name location");

        string nearLimit = "/vehicle " + new string(' ', 502);
        session.Observe(engine, nearLimit);
        Check(session.TryCycle(nearLimit, false, 512, out completed) && completed.EndsWith("1"), "first short candidate fits");
        Check(session.TryCycle(completed, false, 512, out completed) && completed.EndsWith("3"), "forward Tab skips the overlong candidate");
        Check(session.TryCycle(completed, true, 512, out completed) && completed.EndsWith("1"), "reverse Tab skips the overlong candidate");
        Check(!session.TryCycle(completed, false, 511, out completed) && session.Selected == 0, "no fitting candidate preserves selection");

        for (int i = 0; i < 11000; i++)
            engine.Catalog[Kind.Item].Add(new Suggestion(Kind.Item, (1000 + i).ToString(), "Synthetic", ""));
        session.Observe(engine, "/give Synthetic", true);
        Check(session.Result.Rows.Count == 0 && !session.TryCycle("/give Synthetic", false, 512, out completed), "pending search exposes no stale candidates");
        session.Observe(engine, "/give Maple", true);
        bool ready = System.Threading.SpinWait.SpinUntil(() =>
        {
            session.Observe(engine, "/give Maple", true);
            return session.Result.Rows.Count > 0;
        }, 5000);
        Check(ready && session.Result.Rows.Single().Value == "363", "only the latest asynchronous search publishes a result");
        session.Reset();
        Check(session.Result.Rows.Count == 0, "reset clears asynchronous results");
    }
}
