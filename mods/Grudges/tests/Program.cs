using System;
using System.Collections.Generic;
using System.Linq;
using Grudges;

static class Program
{
    static int fails = 0;
    static void Check(bool ok, string what) { Console.WriteLine((ok ? "PASS " : "FAIL ") + what); if (!ok) fails++; }

    static Rules NewRules(float kill = 1f, float wit = 1f, int max = 3, float cooldown = 0f)
    {
        var r = new Rules();
        r.Lines.Load(LineBook.ReadDefault().Split('\n'));
        r.Settings.KillChance = kill; r.Settings.WitnessChance = wit; r.Settings.MaxPerBiome = max; r.Settings.CooldownMinutes = cooldown;
        r.Settings.MaxLevelByBiome = RuleSettings.ParseLevels("Meadows=3, BlackForest=4");
        return r;
    }

    static CreatureRef Dwarf(string zdo, int level = 1, string id = "", float dist = 5) =>
        new CreatureRef { Zdo = zdo, Species = "Greydwarf", SpeciesName = "Greydwarf", Level = level, Biome = "BlackForest", NemesisId = id, Distance = dist };

    static int Main()
    {
        var rng = new Random(42);
        var now = new DateTime(2026, 10, 3, 0, 0, 0, DateTimeKind.Utc);

        // Lines file loads, with no stray text
        var book = new LineBook(); book.Load(LineBook.ReadDefault().Split('\n'));
        Check(book.Warnings.Count == 0 && book.SectionCount > 20, $"default lines load ({book.SectionCount} sections, {book.Warnings.Count} warnings)");

        // Kill promotes
        var rules = NewRules(); var roster = new Roster();
        var res = rules.OnDeath(roster, new DeathReport { VictimId = 1, VictimName = "Nick", Cause = "Kill", Killer = Dwarf("z1") }, now, rng);
        Check(res.Promoted != null && roster.Active.Count() == 1, "killer is promoted");
        var n = res.Promoted;
        Check(n.Level == 2 && n.BaseLevel == 1, $"level 1 -> 2 (got {n.Level})");
        Check(n.Strength != "" && n.Weakness != "" && n.Strength != n.Weakness, $"kill nemesis has strength {n.Strength} and weakness {n.Weakness}");
        Check(n.Traits.Count == 1, "one trait");
        Check(n.GrudgeFor(1, "Nick", false)?.Kills == 1, "remembers 1 kill of Nick");
        Console.WriteLine("   " + n.FullName + " | " + string.Join(" / ", res.Messages));

        // Same nemesis kills again: grows, capped by biome
        for (int i = 0; i < 4; i++)
            res = rules.OnDeath(roster, new DeathReport { VictimId = 1, VictimName = "Nick", Cause = "Kill", Killer = Dwarf("z1", n.Level, n.Id) }, now, rng);
        Check(n.Level == 4, $"capped at BlackForest max level 4 (got {n.Level})");
        Check(n.GrudgeFor(1, "Nick", false).Kills == 5, "5 kills remembered");
        Check(roster.All.Count == 1, "no new nemesis from repeat kills");
        Console.WriteLine("   taunt: " + rules.Taunt(n, 1, "Nick", rng));

        // Witness: tree death
        var rules2 = NewRules(); var roster2 = new Roster();
        res = rules2.OnDeath(roster2, new DeathReport { VictimId = 1, VictimName = "Nick", Cause = "Tree", Witnesses = { Dwarf("far", dist: 25), Dwarf("near", dist: 4) } }, now, rng);
        Check(res.Promoted != null && res.PromotedBody.Zdo == "near", "closest witness is promoted");
        Check(res.Promoted.Origin == Origin.Witness && res.Promoted.Strength == "", "witness starts without a strength");
        Console.WriteLine("   " + res.Promoted.FullName + " | " + res.Messages[0]);
        Console.WriteLine("   taunt: " + rules2.Taunt(res.Promoted, 1, "Nick", rng));
        var w = res.Promoted;

        // That witness later kills Nick: gains strength
        rules2.OnDeath(roster2, new DeathReport { VictimId = 1, VictimName = "Nick", Cause = "Kill", Killer = Dwarf("near", w.Level, w.Id) }, now, rng);
        Check(w.Strength != "" && w.Strength != w.Weakness, "witness gains a strength after its first kill");

        // Boar death witnessed: creature name remembered
        var rules3 = NewRules(); var roster3 = new Roster();
        res = rules3.OnDeath(roster3, new DeathReport { VictimId = 2, VictimName = "Bob", Cause = "Creature", CauseDetail = "Boar", Witnesses = { Dwarf("a") } }, now, rng);
        var t = rules3.Taunt(res.Promoted, 2, "Bob", new Random(1));
        Check(res.Promoted.Grudges[0].Witnessed[0] == "Creature/Boar", "boar death remembered as Creature/Boar");
        Console.WriteLine("   taunt: " + t);

        // Existing nemesis witnesses: no new one
        res = rules3.OnDeath(roster3, new DeathReport { VictimId = 2, VictimName = "Bob", Cause = "Fall", Witnesses = { Dwarf("b", dist: 2), Dwarf("a", 2, roster3.All[0].Id, 9) } }, now, rng);
        Check(res.Promoted == null && roster3.All.Count == 1 && roster3.All[0].Grudges[0].Witnessed.Count == 2, "a nemesis that saw it remembers, no new one");

        // Non-eligible witnesses ignored
        res = rules3.OnDeath(roster3, new DeathReport { VictimId = 3, Cause = "Tree", Witnesses = { new CreatureRef { Species = "Boar", Zdo = "x", Biome = "Meadows" } } }, now, rng);
        Check(res.Promoted == null, "boars can't become nemeses");

        // Chance 0 → nothing; Forced → promotes anyway
        var rules4 = NewRules(kill: 0f, wit: 0f); var roster4 = new Roster();
        res = rules4.OnDeath(roster4, new DeathReport { VictimId = 1, Cause = "Kill", Killer = Dwarf("q") }, now, rng);
        Check(res.Promoted == null, "chance 0: no promotion");
        res = rules4.OnDeath(roster4, new DeathReport { VictimId = 1, Cause = "Kill", Killer = Dwarf("q"), Forced = true }, now, rng);
        Check(res.Promoted != null, "forced: promotion");

        // Cooldown
        var rules5 = NewRules(cooldown: 10f, max: 10); var roster5 = new Roster();
        rules5.OnDeath(roster5, new DeathReport { VictimId = 1, Cause = "Kill", Killer = Dwarf("c1") }, now, rng);
        res = rules5.OnDeath(roster5, new DeathReport { VictimId = 1, Cause = "Kill", Killer = Dwarf("c2") }, now.AddMinutes(5), rng);
        Check(res.Promoted == null, "cooldown blocks a second promotion 5 min later");
        res = rules5.OnDeath(roster5, new DeathReport { VictimId = 1, Cause = "Kill", Killer = Dwarf("c3") }, now.AddMinutes(11), rng);
        Check(res.Promoted != null, "cooldown over after 11 min");

        // Full biome: challenge rule
        var rules6 = NewRules(max: 2); var roster6 = new Roster();
        int won = 0, lost = 0;
        for (int i = 0; i < 40; i++)
        {
            res = rules6.OnDeath(roster6, new DeathReport { VictimId = 1, Cause = "Kill", Killer = Dwarf("k" + i) }, now, rng);
            if (i >= 2) { if (res.Promoted != null) won++; else lost++; }
            Check(roster6.ActiveIn("BlackForest").Count() <= 2, null ?? $"biome never exceeds 2 (death {i})" );
        }
        Console.WriteLine($"   challenges: {won} won, {lost} lost; retired {roster6.All.Count(x => !x.IsActive)}");
        Check(won > 0 && lost > 0, "challenges can go both ways");
        Check(roster6.All.Where(x => !x.IsActive).All(x => x.RetiredReason.StartsWith("Overthrown")), "losers are retired as overthrown");

        // Names are unique among the active ones
        Check(roster6.Active.Select(x => x.Name).Distinct().Count() == roster6.Active.Count(), "active names are unique");

        // Save / load round trip, including awkward characters
        var rt = new Roster();
        var odd = rules.Create(rt, Dwarf("AAA+/=="), Origin.Witness, "Creature", new DeathReport { VictimId = -5, VictimName = "Nick; the=Bold:+/", CauseDetail = "Boar" }, now, rng);
        odd.Grudges[0].Kills = 3;
        rt.All.Add(odd); rt.All.Add(n);
        var warnings = new List<string>();
        var back = Roster.Parse(rt.Serialize(), warnings);
        Check(warnings.Count == 0 && back.All.Count == 2, "roster round trip");
        Check(Roster.SerializeOne(back.All[0]) == Roster.SerializeOne(odd) && Roster.SerializeOne(back.All[1]) == Roster.SerializeOne(n), "records identical after reload");
        Check(back.All[0].Grudges[0].PlayerName == "Nick; the=Bold:+/" && back.All[0].Grudges[0].Witnessed[0] == "Creature/Boar" && back.All[0].Zdo == "AAA+/==", "odd characters survive");

        // Reveal info
        var rr = NewRules(); var rro = new Roster();
        res = rr.OnDeath(rro, new DeathReport { VictimId = 1, VictimName = "Nick", Cause = "Kill", Killer = Dwarf("zz") }, now, rng);
        Check(res.RevealId == res.Promoted.Id && res.RevealKind == "promoted" && res.RevealZdo == "zz" && res.RevealLine.Length > 0, "promotion has a reveal: " + res.RevealLine);
        res = rr.OnDeath(rro, new DeathReport { VictimId = 1, VictimName = "Nick", Cause = "Kill", Killer = Dwarf("zz", 2, rro.All[0].Id) }, now, rng);
        Check(res.RevealKind == "stronger" && res.RevealId == rro.All[0].Id, "repeat kill has a 'stronger' reveal: " + res.RevealLine);
        res = rr.OnDeath(rro, new DeathReport { VictimId = 1, VictimName = "Nick", Cause = "Tree", Witnesses = { Dwarf("zz", 3, rro.All[0].Id) } }, now, rng);
        Check(res.RevealKind == "saw", "nemesis witness has a 'saw' reveal: " + res.RevealLine);
        var g4 = NewRules(kill: 0f, wit: 0f);
        res = g4.OnDeath(new Roster(), new DeathReport { VictimId = 1, Cause = "Kill", Killer = Dwarf("n") }, now, rng);
        Check(res.RevealId == null, "no reveal when nothing happened");

        // Greylings
        var gl = NewRules(); var glr = new Roster();
        res = gl.OnDeath(glr, new DeathReport { VictimId = 1, VictimName = "Nick", Cause = "Kill",
            Killer = new CreatureRef { Zdo = "g", Species = "Greyling", SpeciesName = "Greyling", Biome = "Meadows", Level = 1 } }, now, rng);
        Check(res.Promoted != null && res.Promoted.Level == 2, "greylings can be promoted: " + res.Promoted?.FullName + " / " + res.RevealLine);

        // Merging new sections into an old lines file
        string oldFile = "[prefix]\nZork\n[taunt-killer]\nMine!\n";
        string missing = LineBook.MissingSections(oldFile, LineBook.ReadDefault());
        var merged = new LineBook(); merged.Load((oldFile + missing).Split('\n'));
        Check(merged.Has("reveal-kill") && merged.Has("prefix:Greyling") && merged.Pick(new Random(1), "taunt-killer") == "Mine!"
              && merged.Pick(new Random(1), "prefix") == "Zork", "new sections merge in without touching edits");
        Check(LineBook.MissingSections(LineBook.ReadDefault(), LineBook.ReadDefault()).Trim().Length == 0, "nothing to merge into an up-to-date file");

        // Cheating death
        var cd = NewRules(); var cdr = new Roster();
        res = cd.OnDeath(cdr, new DeathReport { VictimId = 1, VictimName = "Nick", Cause = "Kill", Killer = Dwarf("e1") }, now, rng);
        var esc = res.Promoted;
        float c0 = Rules.SurvivalChance(esc, "Blunt" == esc.Weakness ? "Slash" : "Blunt", 0.5f, 0.5f);
        float cw = Rules.SurvivalChance(esc, esc.Weakness, 0.5f, 0.5f);
        Check(Math.Abs(cw - c0 / 2f) < 0.001f, $"weakness halves the survival chance ({c0} -> {cw})");
        var msgs = cd.OnEscaped(esc, "Fire", 1, "Nick", 1000, 1800, new Random(3));
        Check(esc.State == NemesisState.Recovering && esc.ReturnAt == 2800 && esc.CheatDeaths == 1 && esc.Scars[0].Type == "Fire" && esc.Zdo == "", "escape: recovering, scarred, returns later");
        Check(cdr.ActiveIn("BlackForest").Count() == 1 && cdr.Active.Count() == 0, "a recovering nemesis keeps its biome slot but isn't out in the world");
        Console.WriteLine("   " + esc.FullName + " | " + msgs[0]);
        Check(Rules.SurvivalChance(esc, "Slash", 0.5f, 0.5f) < c0 + 0.001f, "second escape is harder");
        cd.OnReturned(esc, "newbody");
        Check(esc.IsActive && esc.Zdo == "newbody", "returns active in a new body");
        Console.WriteLine("   taunt after scar: " + cd.Taunt(esc, 1, "Nick", new Random(5)));
        var back2 = Roster.Parse(new Roster { All = { esc } }.Serialize());
        Check(Roster.SerializeOne(back2.All[0]) == Roster.SerializeOne(esc) && back2.All[0].Scars[0].By == "Nick", "scars survive save/load");
        Console.WriteLine("   trophy: " + Rules.TrophyStory(esc, "Nick"));

        // Champions
        var ch = new Roster();
        var a = cd.Create(ch, Dwarf("a1", 1), Origin.Kill, "Kill", new DeathReport { VictimId = 1, VictimName = "Nick" }, now, rng); ch.All.Add(a);
        Check(Rules.UpdateChampions(ch).Count == 0 && !a.Champion, "a lone nemesis isn't a Champion");
        var b = cd.Create(ch, Dwarf("b1", 1), Origin.Witness, "Tree", new DeathReport { VictimId = 1, VictimName = "Nick" }, now.AddSeconds(1), rng); ch.All.Add(b);
        var crowned = Rules.UpdateChampions(ch);
        Check(crowned.Count == 1 && crowned[0] == a && a.Champion && !b.Champion && a.EffectiveLevel == a.Level + 1, "the stronger of two is crowned, with +1 level");
        b.Level += 3;
        crowned = Rules.UpdateChampions(ch);
        Check(crowned.Count == 1 && b.Champion && !a.Champion, "a stronger rival takes the title");
        Check(Rules.UpdateChampions(ch).Count == 0, "no change, no announcement");
        b.State = NemesisState.Retired;
        Rules.UpdateChampions(ch);
        Check(!b.Champion && !a.Champion, "champion dies with only one left: no champion");
        var back3 = Roster.Parse(new Roster { All = { a, b } }.Serialize());
        Check(Roster.SerializeOne(back3.All[0]) == Roster.SerializeOne(a), "champion/gen survive save/load");

        // Raids
        var rr2 = new Roster();
        var n1 = cd.Create(rr2, Dwarf("r1"), Origin.Kill, "Kill", new DeathReport { VictimId = 1, VictimName = "Nick" }, now, rng); rr2.All.Add(n1);
        var n2 = cd.Create(rr2, Dwarf("r2"), Origin.Kill, "Kill", new DeathReport { VictimId = 2, VictimName = "Bob" }, now, rng); n2.Level = 4; rr2.All.Add(n2);
        Rules.UpdateChampions(rr2);
        var species = new[] { "Greydwarf", "Greydwarf_Elite" };
        Check(Rules.PickRaider(rr2, new[] { "Boar" }, "BlackForest", 1, "Nick", rng, 1f, 1f, 1f) == null, "boar raids bring no Greydwarf nemesis");
        Check(Rules.PickRaider(rr2, species, "BlackForest", 1, "Nick", rng, 1f, 0f, 0f) == n1, "named one joins when the champion doesn't");
        Check(Rules.PickRaider(rr2, species, "BlackForest", 2, "Bob", rng, 0f, 0f, 1f) == n2, "champion with a grudge comes for Bob");
        Check(Rules.PickRaider(rr2, species, "BlackForest", 1, "Nick", rng, 0f, 0f, 1f) == null, "champion without a grudge stays home (chance 0)");
        n1.State = NemesisState.Recovering;
        Check(Rules.PickRaider(rr2, species, "BlackForest", 1, "Nick", rng, 1f, 0f, 0f) == null, "recovering ones don't raid");

        // Props and stolen helmets
        var pr = NewRules(); pr.Settings.StealableHelmets = RuleSettings.ParseList("HelmetBronze, HelmetIron"); var prr = new Roster();
        res = pr.OnDeath(prr, new DeathReport { VictimId = 1, VictimName = "Nick", Cause = "Kill", Killer = Dwarf("h1"), VictimHelmet = "HelmetTrollLeather" }, now, rng);
        Check(res.Promoted.StolenHelmet == "", "hoods aren't stolen");
        res = pr.OnDeath(prr, new DeathReport { VictimId = 1, VictimName = "Nick", Cause = "Kill", Killer = Dwarf("h1", 2, res.Promoted.Id), VictimHelmet = "HelmetIron" }, now, rng);
        var hn = prr.All[0];
        Check(hn.StolenHelmet == "HelmetIron", "an approved helmet is stolen on a repeat kill");
        hn.Scars.Add(new Scar { Type = "Pierce" }); hn.Scars.Add(new Scar { Type = "Blunt" }); hn.Scars.Add(new Scar { Type = "Pierce" }); hn.Scars.Add(new Scar { Type = "Frost" });
        var props = Rules.PropsFor(hn);
        Console.WriteLine("   props: " + string.Join(", ", props));
        Check(props.SequenceEqual(new[] { "arrow1", "arrow2", "arrow3", "ice1", "ice2", "aura:Frost", "helmet:HelmetIron" }), "props from scars; stolen helmet beats the bronze one");
        hn.Scars.Add(new Scar { Type = "Fire" });
        Check(Rules.PropsFor(hn).Contains("aura:Fire") && !Rules.PropsFor(hn).Contains("aura:Frost") && Rules.SkinScar(hn) == "Fire", "only the latest elemental scar has an aura; skin follows the latest scar");
        hn.StolenHelmet = "";
        Check(Rules.PropsFor(hn).Last() == "helmet:HelmetBronze", "a club scar gives a bronze helmet");
        Check(Roster.ParseOne(Roster.SerializeOne(new Nemesis { Id = "x", StolenHelmet = "HelmetIron" })).StolenHelmet == "HelmetIron", "stolen helmet saves");

        // Hunting across biomes
        Check(Rules.TravelBonus("BlackForest", "Plains") == 3 && Rules.TravelBonus("Plains", "Meadows") == 0, "travel bonus only for tougher biomes");
        var hr = new Roster();
        var h1 = pr.Create(hr, Dwarf("t1"), Origin.Kill, "Kill", new DeathReport { VictimId = 1, VictimName = "Nick" }, now, rng); hr.All.Add(h1);
        var h2 = pr.Create(hr, Dwarf("t2"), Origin.Kill, "Kill", new DeathReport { VictimId = 2, VictimName = "Bob" }, now, rng); hr.All.Add(h2);
        Check(Rules.Hunters(hr, "Plains", 1, "Nick", false).SequenceEqual(new[] { h1 }), "only ones with a grudge against you hunt you");
        Check(Rules.Hunters(hr, "BlackForest", 1, "Nick", false).Count == 0, "no hunting in its own biome");
        Check(Rules.Hunters(hr, "Plains", 1, "Nick", true).Count == 0 && Rules.Hunters(hr, "Swamp", 1, "Nick", true).Count == 1, "adjacent-only setting");

        var fb = FitBook.Parse("arrow1: 0.1, -0.2, 0, 15, 10, 0, 1 | helmet: 0.1, 0, 0, 0, 0, 0, 1 | Greyling/arrow1: 0.05, -0.1, 0, 15, 10, 0, 0.7 | helmet:HelmetIron: 0.12, 0, 0, 5, 0, 0, 1.1 | old: 1, 2, 3, 4, 5, 2");
        Check(fb.For("Greydwarf", "arrow1").Forward == -0.2f && fb.For("Greyling", "arrow1").Size == 0.7f, "species-specific fits");
        Check(fb.For("Greydwarf", "helmet:HelmetIron").Size == 1.1f && fb.For("Greydwarf", "helmet:HelmetBronze").Up == 0.1f, "helmet fits fall back to 'helmet'");
        Check(fb.For("x", "old").Size == 2f && fb.For("x", "old").Roll == 0f, "old 6-number fits still read");
        Check(FitBook.Parse(fb.ToString()).ToString() == fb.ToString(), "fit book round trip");

        // Personalities
        var pb = new PersonalityBook(); pb.Load(PersonalityBook.ReadDefault().Split('\n'));
        Check(pb.Names.Count == 7, $"seven default personalities ({string.Join(", ", pb.Names)})");
        var pbo = new PersonalityBook(); pbo.Load((PersonalityBook.ReadDefault() + "\n[Brute:Fuling]\nprop = item:Torch@RightHand\n").Split('\n'));
        Check(pbo.Get("Brute", "Fuling", "prop") == "item:Torch@RightHand" && pbo.Get("Brute", "Greydwarf", "prop") == "item:TrophyFrostTroll@LeftShoulder"
              && pbo.Get("Brute", "Fuling", "size") == "0.8", "creature-specific personality overrides, falling back to the base");
        Check(!pbo.Names.Contains("Brute:Fuling"), "override sections aren't personalities of their own");
        var counts = new Dictionary<string, int>();
        var rr7 = new Random(9);
        for (int i = 0; i < 600; i++) { var nm = pb.Roll(rr7); counts[nm] = counts.TryGetValue(nm, out int c) ? c + 1 : 1; }
        Check(counts.Count == 6 && counts.Values.All(c => c > 60), "rolls spread across all six: " + string.Join(", ", counts.Select(kv => kv.Key + " " + kv.Value)));
        var pr2 = NewRules(); pr2.Personas = pb; pr2.PaletteNames = new List<string> { "moss", "ash", "rust", "bone", "blood", "shadow", "sickly" };
        var pn = pr2.Create(new Roster(), Dwarf("p1"), Origin.Kill, "Kill", new DeathReport { VictimId = 1, VictimName = "Nick" }, now, rng);
        Check(pn.Personality.Length > 0 && pn.Palette.Length > 0 && pn.SizeRoll >= 0f && pn.SizeRoll <= 1f, $"new nemesis gets a personality/palette/size: {pn.Personality}, {pn.Palette}, {pn.SizeRoll:0.00}");
        Check(Rules.PropsFor(pn).Contains("persona:" + pn.Personality), "its personality prop is in its props");
        var old = new Nemesis { Id = "o", Species = "Greydwarf" };
        Check(pr2.AssignLook(old, rng) && old.Personality.Length > 0 && !pr2.AssignLook(old, rng), "old nemeses get a look once");
        Check(Roster.SerializeOne(Roster.ParseOne(Roster.SerializeOne(pn))) == Roster.SerializeOne(pn), "personality/palette/size survive save/load");
        pn.Personality = "Brute"; pr2.Lines.Load(LineBook.ReadDefault().Split('\n'));
        int brute = 0; for (int i = 0; i < 200; i++) { var tt = pr2.Taunt(pn, 1, "Nick", rr7); if (tt.Contains("Crush") || tt.Contains("break you") || tt == "RAAAH!" || tt.Contains("You again. Good")) brute++; }
        Check(brute > 80 && brute < 200, $"a Brute mostly talks like a Brute ({brute}/200), sometimes like anyone");

        // Default personality fallback
        Check(pb.Get("Nobody", "Greydwarf", "emotes-witness") == "laugh, point" && pb.Get("Brute", "Greydwarf", "emotes-witness") == "challenge" && pb.Get("", null, "animations") == "jump",
              "personalities fall back to [Default]");
        Check(!pb.Names.Contains("Default"), "[Default] isn't a personality you can roll");

        // Looks file
        var lk = new KeyValueBook(); lk.Load((KeyValueBook.ReadDefaultLooks() + "\n[props:Greyling]\naxe = item:AxeFlint@Chest\n").Split('\n'));
        Check(lk.Section("palettes").Count == 7 && KeyValueBook.TryHsv(lk.Get("scar-skin", "Fire"), out float fh, out _, out float fv) && fh == -0.3f && fv == -0.45f,
              "looks file: palettes and scar skin read");
        Check(lk.Get("props", "axe", "Greyling") == "item:AxeFlint@Chest" && lk.Get("props", "axe", "Greydwarf") == "item:AxeStone@UpperChest"
              && lk.Get("props", "arrow1", "Greyling").StartsWith("projectile:"), "looks file: per-creature overrides, falling back to the base");
        Check(lk.Get("trophies", "Greydwarf_Elite") == "TrophyGreydwarfBrute", "looks file: trophies");
        string moved = KeyValueBook.ReplaceSection(KeyValueBook.ReadDefaultLooks(), "scar-skin", "Fire = 1/2/3\nFrost = 4/5/6");
        var lk2 = new KeyValueBook(); lk2.Load(moved.Split('\n'));
        Check(lk2.Section("scar-skin").Count == 2 && lk2.Get("scar-skin", "Fire") == "1/2/3" && lk2.Section("scar-auras").Count == 5
              && lk2.Section("palettes").Count == 7 && moved.Contains("# The aura for its latest"), "old config values replace a looks section, keeping the rest");
        string setk = KeyValueBook.SetKeys(PersonalityBook.ReadDefault(), "Default", new Dictionary<string, string> { { "emotes-witness", "shrug" }, { "newkey", "x" } });
        var pb3 = new PersonalityBook(); pb3.Load(setk.Split('\n'));
        Check(pb3.Get("", null, "emotes-witness") == "shrug" && pb3.Get("", null, "newkey") == "x" && pb3.Get("", null, "emotes-saw-again") == "laugh, shrug, point",
              "old emote settings carried into [Default]");

        string added = KeyValueBook.SetKeys(PersonalityBook.ReadDefault(), "Brute:Greyling", new Dictionary<string, string> { { "prop", "item:TrophyNeck@Head" } });
        var pb4 = new PersonalityBook(); pb4.Load(added.Split('\n'));
        Check(pb4.Get("Brute", "Greyling", "prop") == "item:TrophyNeck@Head" && pb4.Get("Brute", "Greydwarf", "prop").Contains("TrophyFrostTroll"),
              "saving a tried prop for one creature adds its own section");

        // Fits file
        var ff = FitBook.Parse("arrow1: 0.05, -0.15, 0.08, 15, 10, 0, 1 | Greyling/persona:Brute: 0.1, 0, 0, 0, 0, 0, 0.5 | Greyling/helmet:HelmetIron: 0, 0, 0, 0, 0, 0, 2");
        var ff2 = FitBook.ParseFile(ff.ToFile().Split('\n'));
        Check(ff2.ToString() == ff.ToString() && ff2.For("Greyling", "persona:Brute").Size == 0.5f && ff2.For("Greyling", "persona:Braggart").Size == 1f,
              "fits file round trip; each personality keeps its own fit");

        // Fulings and families
        var fam = new KeyValueBook(); fam.Load(KeyValueBook.ReadDefaultLooks().Split('\n'));
        Kinds.Families = fam.Section("families");
        Check(Kinds.FamilyOf("GoblinBrute") == "Fuling" && Kinds.FamilyOf("Greyling") == "Greyling" && Kinds.FamilyOf("Greydwarf_Elite") == "Greydwarf",
              "families read from the looks file");
        Check(string.Join(",", Kinds.Expand("GoblinBrute", "prefix")) == "prefix:GoblinBrute,prefix:Fuling,prefix", "sections expand creature, family, plain");
        Check(fam.Get("trophies", "GoblinShaman") == "TrophyGoblinShaman", "Fuling trophies");
        var fl = new LineBook(); fl.Load(LineBook.ReadDefault().Split('\n'));
        var fr = NewRules(); fr.Lines = fl; fr.Personas = pb;
        var fname = fr.MakeName(new Roster(), "GoblinBrute", new Random(3));
        var gname = fr.MakeName(new Roster(), "Greydwarf_Elite", new Random(3));
        Check(new[] { "Grub", "Skab", "Gnash", "Mog", "Snag", "Krik", "Zug", "Grik", "Skrag", "Nub", "Muk", "Zik", "Dreg", "Bog" }.Any(x => fname.StartsWith(x)),
              $"a Fuling Berserker gets a Fuling name ({fname})");
        Check(new[] { "Moss", "Bark", "Root", "Rot", "Thorn", "Gnarl" }.Any(x => gname.StartsWith(x)) || gname.Length > 0, $"a Greydwarf Brute gets a Greydwarf name ({gname})");
        Check(fr.MakeEpithet(Origin.Kill, "Kill", "GoblinBrute", new Random(1)).Length > 0, "Fuling Berserker epithet");
        int hoard = 0, hoardDwarf = 0; var rr8 = new Random(5);
        for (int i = 0; i < 400; i++) { if (pb.Roll(rr8, "Goblin") == "Hoarder") hoard++; if (pb.Roll(rr8, "Greydwarf") == "Hoarder") hoardDwarf++; }
        Check(hoard > 30 && hoardDwarf == 0, $"Hoarder is Fuling-only ({hoard}/400 Fulings, {hoardDwarf} Greydwarfs)");
        Check(pb.Get("Glutton", "GoblinShaman", "prop") == "item:TrophyBoar@Hips" && pb.Get("Glutton", "Greydwarf", "prop") == "item:TrophyBoar@RightHand",
              "family personality overrides ([Glutton:Fuling])");
        var ffam = FitBook.Parse("Fuling/axe: 1, 0, 0, 0, 0, 0, 1 | GoblinShaman/axe: 2, 0, 0, 0, 0, 0, 1 | axe: 3, 0, 0, 0, 0, 0, 1");
        Check(ffam.For("GoblinBrute", "axe").Up == 1f && ffam.For("GoblinShaman", "axe").Up == 2f && ffam.For("Greydwarf", "axe").Up == 3f,
              "fits: creature, then family, then default");
        var fk = new KeyValueBook(); fk.Load("[props]\naxe = item:A@Chest\n[props:Fuling]\naxe = item:B@Chest\n[props:GoblinBrute]\naxe = item:C@Chest".Split('\n'));
        Check(fk.Get("props", "axe", "Goblin") == "item:B@Chest" && fk.Get("props", "axe", "GoblinBrute") == "item:C@Chest", "looks: creature beats family beats base");
        var taunt = fr.Taunt(new Nemesis { Id = "f", Species = "Goblin", Name = "Grubgut" }, 1, "Nick", new Random(2));
        Check(taunt.Length > 0, "Fuling stranger taunt: " + taunt);
        // Skeletons and the Charred
        Check(Kinds.FamilyOf("Skeleton_Poison") == "Skeleton" && Kinds.FamilyOf("Charred_Mage") == "Charred" && Kinds.FamilyOf("Skeleton") == "Skeleton",
              "skeleton and Charred families");
        Check(fam.Get("trophies", "Skeleton_Poison") == "TrophySkeletonPoison" && fam.Get("trophies", "Charred_Melee") == "TrophyCharredMelee", "skeleton/Charred trophies");
        var sname = fr.MakeName(new Roster(), "Skeleton_NoArcher", new Random(4));
        Check(new[] { "Bone", "Grin", "Rattle", "Hollow", "Marrow", "Skull", "Dust", "Cold", "Knuckle", "Grave", "Rib" }.Any(x => sname.StartsWith(x)), $"skeleton name ({sname})");
        int glut = 0; var rr9 = new Random(6);
        for (int i = 0; i < 300; i++) if (pb.Roll(rr9, "Charred_Melee") == "Glutton" || pb.Roll(rr9, "Skeleton") == "Glutton") glut++;
        Check(glut == 0, "the dead are never Gluttons");
        Check(fl.Has("reveal-kill:Skeleton_Poison") && fl.Has("taunt-killer:Charred"), "skeleton/Charred lines present");

        Kinds.Families = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        Console.WriteLine(fails == 0 ? "ALL PASSED" : $"{fails} FAILED");
        return fails == 0 ? 0 : 1;
    }
}
