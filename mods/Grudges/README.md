# Grudges v0.9.1

A Nemesis system for Valheim. Creatures that kill you, or watch you die in a silly way, can earn a name, get stronger, and remember you.

Greydwarfs, Greylings, Fulings, Skeletons and the Charred so far: promotions, witnesses, traits, taunts, per-biome lists, and the death reveal.

## New in 0.9.1

- Built and checked against your real game files (Unity 6 era Valheim) instead of my stand-ins. Every game function and field Grudges looks up by name was checked against the real game: all present.
- Two deprecated Unity calls swapped for their faster replacements (finding wards and dropped trophies).
- Build with `dotnet build mods/Grudges -c Release` (it lands in `BepInEx/scripts` for the dev loader); there is no prebuilt copy in the folder any more.

## New in 0.9.0: Skeletons and the Charred

- **Skeletons** (`Skeleton`, `Skeleton_NoArcher`, `Skeleton_Poison` = Rancid Remains) can become nemeses. They're one family, so they share names, lines and fits; Rancid Remains have their own epithets and a couple of their own lines.
  - The Black Forest, Swamp and Mountain skeletons are the same creature in different places. Each biome keeps its own list, so a Mountain skeleton nemesis is part of the Mountain's list and can be its Champion.
  - The player's summoned skeletons and Brenna (Hildir's quest) are left out on purpose.
- **The Charred** (Ashlands: `Charred_Melee` Warrior, `Charred_Archer` Marksman, `Charred_Mage` Warlock, `Charred_Twitcher`) are their own family, with fire-themed names and lines.
- Undead voices: patient, cold mockery for Skeletons ("Your bones will make fine company"); fury and fire for the Charred.
- **The dead are never Gluttons** (`[Glutton:Skeleton] weight = 0`).
- Named trophies for all of them except the Twitcher (I don't know its trophy's name; add it under `[trophies]` if it has one).
- Your Creatures list gets them if it was still the default; your looks file gets their families and trophies, once.

## Testing 0.9.0 (Skeletons)

1. **Spawn Creature** = `Skeleton`, **Spawn a nemesis**. Check the name (Bonegrin, Rattleshank...), the reveal line, and whether it does player emotes (the log says `Player emotes on Skeleton: yes/no`).
2. Colour: skeletons may not have the colour controls Greydwarfs have. If it doesn't recolour, it gets the plain reddish tint; tell me.
3. Fit the family on a plain Skeleton (**Save For** = Whole family), then check `Skeleton_Poison` and only re-fit what's off. Then the same for `Charred_Melee`.
4. **Kill nearest nemesis for good**: named Skeleton / Rancid Remains / Warrior trophy.
5. If a Charred spawn says it couldn't find the creature, the prefab name is wrong; tell me which.

## New in 0.8.1

- **Save For** (F1 → 11 - Prop Fitting): **Whole family** (default) saves a fit for every Fuling at once; **This creature only** saves it just for the type you're next to (e.g. only Berserkers). Before this, Save fit always saved for the exact type, so the family sharing in 0.8.0 only worked by hand-editing the fits file. **Reset to default** now steps back one level per press: this creature's own fit, then the family's.

## New in 0.8.0: Fulings

- **Fulings, Fuling Berserkers, Fuling Shamans (and archers) can become nemeses** in the Plains. They have their own list there, their own Champion, and the Fuling raid can bring one to your base.
- **Their own voice:** Fuling names (Grubgut, Skabfang, Zugnik...), epithets per type (Berserkers are Skull-Splitters, Shamans Fire-Callers), and their own reveal lines and taunts. A Fuling Zealot worships Yagluth, not the Elder.
- **A Fuling-only personality: the Hoarder.** Greedy and gleeful, with a pouch of coins at its belt.
- **Their own trophies:** a named Fuling, Berserker or Shaman trophy when one dies for good.
- **Creature families**, so new creatures are mostly a data job. `[families]` in `Grudges.looks.txt` groups creature types (all Fulings are the `Fuling` family; Greydwarf Brutes and Shamans are in the `Greydwarf` family). Any section in any file can have a family version or a creature version:
  - lines: `[taunt-killer:Fuling]`, `[reveal-kill:GoblinBrute]`, `[reveal-kill/Zealot:Fuling]`
  - looks: `[props:Fuling]`, `[palettes:Fuling]` (palettes listed there are added to the ones Fulings can get)
  - personalities: `[Glutton:Fuling]`; a personality can be one family's only, with `weight = 0` plus `[Name:Fuling] weight = 1`
  - prop fits: a fit saved on a Fuling is used by every Fuling until one type gets its own.
  The order is always: exact creature, then family, then plain.
- **Fits are shared across a family.** Greydwarf Brutes and Shamans now use your Greydwarf fits until you fit them on their own (Brutes are bigger, so they may want their own). New Greydwarf Brute nemeses also get Greydwarf names now instead of the generic ones.
- Your Creatures list gets the Fulings added if it was still the default. If you'd edited it, add `Goblin, GoblinBrute, GoblinShaman, GoblinArcher` yourself. Your looks file gets the Fuling trophies.

## Testing 0.8.0 (Fulings)

1. In the Plains (or anywhere, for fitting), set **Spawn Creature** = `Goblin` and press **Spawn a nemesis**. It should get a Fuling name and a Fuling line in the reveal, and do player emotes.
   - **If the Fuling looks unchanged in colour:** its skin may not have the colour controls Greydwarfs have; it then gets a plain reddish tint. Tell me, and send the `Grudges` log lines.
2. Fit props on it: Fitting Mode, every prop, Save. Then spawn a `GoblinBrute`: it should use the same fits (family), and you can re-fit just the Berserker.
3. **Kill nearest nemesis for good:** it should drop a named Fuling trophy.
4. Raid: `event army_goblin` near your base (devcommands) with a Fuling nemesis out in the world.
5. Fulings often carry a weapon and sometimes wear their own helmet. Props in their hands, or on a helmeted head, may overlap. Tell me what clashes.

## New in 0.7.3

- **Spawn Creature** (F1 → 10 - Testing): pick which kind **Spawn a nemesis** makes (any creature in the Creatures list).

## New in 0.7.2

- **Hats in Try Item:** Midsummer crown, Yule hat, Celebratory cap, Dverger circlet, Pointy hat, Root mask, Hildir's caps, headscarves, straw hat and headband (HelmetHat1 to 10), and the regular helmets and hoods, at the top of the list. They're copied as their rigid model, so caps, crowns and masks look right; hoods keep their ground shape instead of wrapping the head.
- More trophies (Bear, Asksvin, Fader, Draugr (female), Brenna...).
- **Add your own** to the list: `try-items = ...` under `[fitting]` in `Grudges.looks.txt` (restart to see them).

## New in 0.7.1

- **Colour Preview** (F1 → 12 - Colour Preview): turn on **Preview** and the nearest nemesis wears the Hue / Saturation / Brightness sliders live (-10 to 10; type exact numbers in the box). **Load nearest nemesis's colour** starts you from what it's wearing now. **Copy line** puts `name = h/s/v` on the clipboard; **Save to looks file** writes it into `[palettes]` or `[scar-skin]` (pick with **Save To**, name it with **Name**) and reloads. The preview shows the raw numbers, without scar darkening or the Champion boost.
- **Trophy props for personalities.** New defaults: Braggart wears deer antlers, Trickster has a Neck on its shoulder, Brute a troll skull pauldron, Coward a greydwarf head at its belt, Zealot a skeleton skull on its chest, Glutton gnaws a boar head. Trophies sit on the outside of the body, so they change the silhouette more and clip less.
- **Try Item** (F1 → 11 - Prop Fitting, Prop = `persona`): audition any creature's trophy (or the old items) on the chosen personality, on any **Try Bone**, live. Fit it, press **Make it this personality's prop** (writes it into the personalities file; tick **Only For This Creature** to save it as `[Brute:Greyling]`), then **Save fit**.
- A personality prop on the head is left off while the nemesis wears a helmet (they'd sit inside each other).
- Changing a prop's item or bone in a file now swaps the model on reload (before, the old model stayed until the creature reloaded).
- Your existing personalities file keeps your old props (Grudges never rewrites what you have). To get the trophy defaults, rename `Grudges.personalities.txt` (e.g. to `.old`) and Grudges writes a fresh one next launch; or switch one personality at a time with Try Item.

## New in 0.7.0: tidier settings

- **Sections are numbered 01 to 11** and appear in that order in F1: General, Custom Intensity, Nemesis Power, Looks, Taunts & Emotes, Death Reveal, Cheating Death & Trophies, Champions & Raids, Hunting, Testing, Prop Fitting. Rarely-touched settings (camera, witness distances, fallback tint, max level by biome...) only show with **Advanced settings** ticked.
- **Your tuned values come with you.** The first launch of 0.7.0 moves every setting from its old place into the new layout and tidies away the old sections.
- **Long text settings moved into files**, where they're easy to read and edit, one entry per line:
  - `Grudges.looks.txt`: colour palettes, scar skins, scar auras, which item and bone each prop uses, and which trophy each creature drops. Your old values (including your Fire scar skin) were copied in.
  - `Grudges.propfits.txt`: your saved prop placements, one per line (`Greydwarf/persona:Brute = ...`).
  - The per-moment emote lists and taunt animations are now the `[Default]` section of `Grudges.personalities.txt`. A personality that doesn't set one uses `[Default]`.
- **Dropdowns**: Test Personality, the fitting tool's Personality and Helmet are picked from a list (filled from the personalities file and Stealable Helmets; restart to see newly added names).
- **Fix: personality props no longer share one placement.** Switching Personality (or Helmet) in the fitting tool now loads that one's own saved fit. Before, the sliders kept the previous one's numbers, so pressing Save copied them over. Re-fit any personality prop that got a copied fit.
- **One "Reload data files" button** re-reads the lines, personalities, looks and fits files.
- **Ready for more creatures.** Every section of the looks file can be overridden for one creature type with `[section:Creature]` (e.g. `[props:Greyling]`, `[palettes:Fuling]`), the same way personalities do (`[Brute:Fuling]`). Adding a new kind means: add it to **Creatures** (01 - General), give it a line under `[trophies]`, and fit its props.

## New in 0.6.0

- **Quiet escapes.** A nemesis that cheats death just vanishes, with no message, so its return is a surprise. (F1 → 8 → Announce Escapes to bring the message back.)
- **Personalities.** Every nemesis gets one: **Braggart, Trickster, Brute, Coward, Zealot, Glutton**. Each has its own emotes for every moment (reveal, killing you again, taunting on sight...), its own taunt and reveal lines, its own animations for creatures that can't do player emotes, a prop (bone necklace, mushroom cap, club on the back, coin pouch, Elder seed, leg of raw meat), a size lean (Brutes big, Cowards small) and favourite colourings. A Coward personality usually comes with the Coward (flees) trait.
- **Personalities file** (`BepInEx\config\Grudges.personalities.txt`): edit, add or remove personalities. `[Brute:Fuling]`-style sections override any setting for one creature type, so this carries over to new creatures.
- **Random looks.** Each nemesis rolls its own size (**Size Min / Size Max**, F1 → 4) and a colour palette (**Palettes**: moss, ash, rust, bone, blood, shadow, sickly). Its latest elemental scar's skin still takes over.
- Existing nemeses get a personality, palette and size the first time the world loads, so their look changes once.
- New test tools: **Test Personality** + **Set personality of nearest nemesis**, **Reload personalities file**, and a **persona** slot in the fitting tool (pick which personality's prop with **Personality**).
- 0.6.0 works with 0.5.x saves; update everyone together anyway (the roster has new fields).

## New in 0.5.5

- In Fitting Mode, the nemesis being fitted is held in its neutral pose (animation off, every bone set from the game's template), the same pose placements are measured from. What you fit is what you get. It goes back to normal when you turn fitting off or move to another nemesis.

## New in 0.5.4

- Fixed: props could sit at slightly different angles on different nemeses, depending on the pose the creature was in (mid-run, mid-swing) when the prop went on. Placement is now measured from the creature's neutral pose, so it's the same every time. Fits saved before this were measured from a frozen pose and may need a small touch-up.
- Works with 0.5.x.

## New in 0.5.3

- **Elemental auras replace the torch stub.** A nemesis's latest elemental scar gives it a lingering aura, borrowed from the game's own status-effect visuals: embers pouring off a burned one (Burning), frost mist (Frost), fumes (Poison), sparks (Lightning), spirit wisps (Spirit). Turned down to a trickle (**Aura Strength**, F1 → 8d).
- **Skin shows the latest scar.** Fire = charred black-red, Frost = pale blue, Poison = sickly green, Lightning = pale and washed out, Spirit = ghostly. Arrow, blade and club scars keep the normal nemesis tint. Tunable in **Scar Skin** (hue/saturation/brightness per type).
- Works with 0.5.x.

## New in 0.5.2

- Fixed: the torch and ice props failed to build ("Object reference not set..."), so they never showed, even in Fitting Mode.
- **Test Damage Type** (F1 → 9 - Testing) picks the scar type for the test buttons (it was always fire).
- **Add a scar to nearest nemesis**: gives a scar of that type instantly, with its prop. No death or recovery wait.
- Fitting Mode freezes every creature within 40 m that your game controls (0.5.1).
- Works with 0.5.0.

## New in 0.5.0

- **Props from scars.** Arrow scar: arrows sticking out of it (two, then a third). Blade scar: an axe in its back. Frost: ice on its shoulders. Fire: a burnt torch stub. Club: a dented bronze helmet. Copies of real game items, purely visual, pinned to its bones.
- **Stolen helmets (allowlist only).** If a nemesis kills you while you're wearing a helmet from **Stealable Helmets** (bronze, iron, drake, padded by default), it wears a copy. Hoods and other cloth-like helmets are never copied.
- **Prop fitting tool** (F1 → 10 - Prop Fitting): turn on Fitting Mode near a nemesis, it holds still and shows every prop; pick one, move the sliders, Save. Fits are saved per creature type.
- **Cross-biome hunting.** A nemesis with a grudge against you (killed you, watched you die, or you scarred it) may follow you into any other biome: a warning ("You hear laughing somewhere behind you..."), then it arrives with a short camera shot. It gets +1 level per step of biome toughness above its own, and can't be hurt by fire, smoke or water on the way. Rare by default: 30% per hour outside its biome (doubled at night), 45 min cooldown, never inside a ward or a dungeon (F1 → 8c).
- 0.5.0 can't talk to 0.4.x: update the server and everyone together.

## New in 0.4.1

- Fixed: the base check for returning/raiding nemeses couldn't find the game's ward function, so one could appear inside your base. It now checks wards directly (their radius plus 8 m). Works with 0.4.0.

## New in 0.4.0

- **Champions.** Once a biome has 2+ nemeses, its strongest becomes its Champion: +1 level, bigger, more vivid, "(Champion)" after its name, its own taunts, and an escort of ordinary creatures when it returns or raids. Everyone is told when a biome gets a new Champion. A rival has to be strictly stronger to take the title; if the Champion dies, the next strongest takes over.
- **Raids.** When a raid that brings a nemesis's kind hits a base (the Elder's "the forest is moving" brings Greydwarfs), a nemesis may join: 25% for an ordinary one, 10% for the Champion, 50% if the Champion has a grudge against you. Its name is announced, it appears ~30 m away after a short delay and comes for you. Nemeses from the base's own biome are picked first.
- A nemesis that turns up somewhere new (raid or return) leaves its old body behind as an ordinary creature.
- Settings: F1 → 8b - Champions & Raids. Test: **Fake a raid**.
- 0.4.0 can't talk to 0.3.x: update the server and everyone together.

## New in 0.3.1

- **Each named trophy is its own item:** named "Murkspine's Head", with its story as the item description, and it never stacks with other trophies. (Ones already merged into a stack before this version can't be split back out.)
- Story no longer shows twice or with odd line breaks.
- **Item stands:** hover the stand to see the name and story; taking the trophy down keeps its story.
- Works with 0.3.0.

## New in 0.3.0

- **Matched emotes.** The reveal picks an emote that fits the moment: Flex/ChallengeAccepted/Headbang for a new killer, Laugh/Point for a witness, Cheer/Kickstep for killing you again, Laugh/Shrug/Point for watching you die again. Each list is editable in F1 → 6 - Reveal.
- **Emotes on sight.** A nemesis that taunts you may point, laugh or wave you over (F1 → 5 - Taunts). Its own animations go back on when you can't see it, or when you hit it (the flinch hides the swap).
- **Cheating death.** A killing blow on a nemesis may not finish it: it vanishes in a puff and goes off to recover (50% the first time, halved for each escape after; a blow from its weakness halves it again; Cowards get more).
- **Scars.** It comes back marked by what nearly killed it: a new epithet ("the Charred", "the Frostbitten"...), resistance to that damage (or, for fire, sometimes a fear of fire), a darker look, and taunts about it.
- **Returns.** After about a day of in-game time (F1 → 8), it finds the player who scarred it while they're in its biome: it appears ~30 m away, the camera shows it for a few seconds with an emote and a line, then it hunts you.
- **Named trophies.** A nemesis that dies for good drops its species' trophy carrying its name and story (who it killed, what it saw, its scars, who finally killed it). The story shows in the item tooltip, on the ground, and on an item stand.
- 0.3.0 can't talk to 0.2.x: update the server and everyone together.

## New in 0.2.4

- No more snap after the emote: the creature finishes its emote before it walks off, and its own animations are only put back behind the fade to black.

## New in 0.2.3

- **Player emotes on creatures**, the same trick as the Fuling Heckler's Copycat, for creatures whose skeleton matches the player's. Each creature type is checked once; look for `Player emotes on Greyling: yes/no (...)` in the log. If it's "no", it falls back to the double hop. Settings: F1 → 6 - Reveal → Player Emotes / Player Emote Names.

## New in 0.2.2

- Taunt animation: Greydwarfs and Greylings now do a double hop (their `jump` animation) as they gloat. Change it in F1 → 6 - Reveal → Taunt Animations (`attack` and `throw` also exist) and Taunt Animation Repeats.

## New in 0.2.1

- The big biome-style title is gone from the reveal (too busy). The name still shows in the speech bubble and the message.
- **Swell:** the creature puffs up and settles back as it transforms. Works on any creature, whatever animations it has (F1 → 6 - Reveal → Swell).
- Works with 0.2.0 players and servers.

## New in 0.2.0

- **Greylings** can become nemeses (with their own names, like "Twiglet the Ankle-Biter"). If you kept the old default Species list, they're added automatically.
- **Death reveal.** After you die, the camera turns to the creature. It transforms on screen (effects, its alert shriek, a taunt animation if it has one), its name comes up as a big title, it says a line, then turns and walks off as the screen fades to black. A nemesis that kills you again, or watches you die again, gets a shorter version (no transformation).
- New lines for the reveal are added to your existing `Grudges.lines.txt` automatically; nothing you edited is touched.
- Settings in **F1 → 6 - Reveal**; a **Replay reveal** test button.
- Note: 0.2.0 and 0.1.0 can't talk to each other, so update the server and every player together.

## What's in v1

- **Kill promotions.** A Greydwarf that kills you may become a nemesis. It gets a name and epithet ("Mossjaw the Rot-Tongued"), +1 star, a strength, a weakness and one trait, and it heals to full.
- **Witness promotions.** Die to a tree, a fall, drowning, a boar and so on with a Greydwarf nearby, and it may become a nemesis with a mocking epithet ("Knotback, Who Saw the Tree Fall"). Witnesses start with no strength; they earn one the first time they actually kill you.
- **Memory.** Each nemesis tracks how many times it has killed each player and which embarrassing deaths it saw. A nemesis that kills you again gains a level, up to the biome's cap.
- **Per-biome lists.** Each biome holds a limited number of nemeses. When it's full, a newcomer has to beat the weakest one, on paper, for its slot.
- **Traits:** Swift, Brutal, Regenerating, Coward, Relentless. **Strengths and weaknesses** use the game's damage types.
- **Look.** Nemeses are slightly bigger and recoloured, and show their name when you aim at them.
- **Taunts.** A nemesis that spots you says something in a speech bubble, based on its history with you.
- **Intensity preset:** Subtle (default), Standard, Mordor, or Custom.
- **Death.** Kill a nemesis and it's gone for good. The grudge is settled.

- **Death reveal:** see above.

Not yet (later phases): cheating death and scars, the Champion, raids, cross-biome hunting, props, the Army screen, trophies, Hugin and Haldor.

## Install

1. Unzip into a **new** folder, e.g. `Desktop\Grudges`, and open PowerShell there.
2. Build with the same paths you use for Fuling Heckler:
   ```
   dotnet build -c Release -p:ValheimDir="C:\Program Files (x86)\Steam\steamapps\common\Valheim" -p:BepInExDir="<profile>\BepInEx" -p:DeployToGame=true
   ```
3. Start the game through Gale. In the BepInEx console you should see `Grudges 0.1.0 loaded. The forest is keeping score.`

It runs fine alongside Fuling Heckler.

**Multiplayer:** Grudges must be on the server and on every player's game. The server decides everything and saves the roster. In single player, your game is the server.

## Files it makes

- `BepInEx\config\nick.Grudges.cfg`: settings (also in the F1 menu).
- `BepInEx\config\Grudges.lines.txt`: every name, epithet, message and taunt.
- `BepInEx\config\Grudges.personalities.txt`: the personalities, plus `[Default]` emotes and animations.
- `BepInEx\config\Grudges.looks.txt`: palettes, scar skins and auras, prop items and bones, trophies. Leave a section's `[header]` with nothing under it to turn that section off (e.g. an empty `[scar-auras]` means no auras).
- `BepInEx\config\Grudges.propfits.txt`: prop placements, written by the fitting tool.
- After editing any of these, press **Reload data files** in F1 → 10 - Testing. No restart needed. Newer versions add any sections they're missing to the end of a file; they never change what you wrote.
- `BepInEx\config\Grudges\rosters\<world>.txt`: the nemeses in each world. Delete it to wipe that world's nemeses.

## Testing (single player)

Use a throwaway world, near some Greydwarfs in the Black Forest. All the test buttons are in **F1 → Grudges → 10 - Testing**. Turn on **Debug Logging** there too.

1. **Promote nearest creature.** Stand near a Greydwarf and press it.
   - **If it grows a little, changes colour, and shows a new name when you aim at it:** the core works.
   - **If the log says it was promoted but the creature looks unchanged:** the tagging step failed. Send me the log lines that start with `Grudges`.
   - **If nothing happens:** check the console for a warning and send it.
2. **Taunt me.** Press it near the nemesis. You should get a speech bubble over its head.
3. **Show roster.** You get a short list on screen and the full details in the log.
4. **Fake a tree death.** Press it near another ordinary Greydwarf. It becomes a witness nemesis with a tree-themed name.
5. **Save check.** Quit to the menu, reload the world, and walk back. The nemeses should still have their names and colours.
6. **Real death.** Set Intensity to **Mordor** (or Custom with Kill Chance 1), turn off god mode, and let a Greydwarf kill you. After you respawn, you get a message about its new name.
7. **Kill it.** Kill a nemesis. You get "You have slain...", and **Show roster** lists it under Fallen.
8. **Clear roster.** Every nemesis turns back into an ordinary Greydwarf.

If a nemesis has 3 or more stars, the game shows no star icons (vanilla only draws 1 and 2 stars). It's still that strong.

If you aim at a creature while it gets promoted, its old name can stick on screen until you look away and back.

## Testing the reveal

1. **Promote nearest creature** now plays the reveal while you're alive: the camera swings to the creature, it changes, says a line and walks off (no fade on test buttons).
   - **If the camera doesn't move and you just see the old messages:** the camera hook failed. Look for a log line starting `Reveal:` and send it.
   - **If the camera moves but the creature changes before the camera gets there:** tell me, the hold-back step isn't working.
   - **If it doesn't walk off:** look for `Reveal: no way to steer creatures` in the log.
2. Check the log for **`Animations on Greydwarf: ...`**. That's the list of animations the creature has. If none of the names in **Taunt Animations** match, it just shrieks; send me the list and I'll pick a good one.
3. **Replay reveal** plays it again on the nearest nemesis without changing anything.
4. Then the real thing: Mordor intensity, let a Greydwarf kill you, and watch.
   - **If you respawn before it finishes:** the reveal cuts short cleanly. Tell me and I'll tighten the timing (or lower **Start Delay**).

## Testing 0.3.0

All in F1 → 9 - Testing, near a nemesis (make one with **Promote nearest creature**).

1. **Nearest nemesis cheats death.** It takes a fire killing blow from you and vanishes in a puff. You get "...crawls away, scarred by fire" (after a moment).
   - **If it dies normally instead:** the death hook isn't catching it. Send the log lines starting with `Grudges`.
   - **If it vanishes but Show roster doesn't say RECOVERING:** the server didn't get the report; send the log.
2. **Show roster.** It should be listed as recovering, with its fire scar and new epithet.
3. **Bring a scarred nemesis back.** It appears ~30 m behind you, the camera swings to it, it emotes and says something about the fire, then comes for you. It should look darker.
   - **If you get a message but no camera:** check for a `Reveal:` line in the log.
   - **If it doesn't come at you:** the hunting call didn't take; tell me and I'll find another way.
4. **Kill nearest nemesis for good.** It dies and drops a trophy. Hover it on the ground (its name shows), pick it up and check the tooltip (its story), then hang it on an item stand and hover the stand.
   - **If the trophy has no name:** look for `Trophies:` lines in the log.
5. **Emotes on sight:** walk up to a nemesis (not too close). It should sometimes point or laugh as it taunts you.
6. **For real:** kill nemeses with fire, weapons and arrows to see different scars. Leave a scarred one alone for a day of game time and wander its biome.

## Testing 0.4.0

1. Make two nemeses in the same biome (**Promote nearest creature** twice). The stronger should be crowned (you get a message) and look bigger with "(Champion)" after its name. **Show roster** marks it.
2. **Fake a raid.** You get "... leads the attack on your home!", then after ~12 s it appears ~30 m away, says a line and comes for you. A Champion brings an escort.
   - **If you get the message but it never appears:** look for `Raids:` lines in the log.
3. The real thing: with devcommands on, open the console and type `event army_theelder` near your base. With Debug Logging on, you should see `Raids: 'army_theelder' started, brings Greydwarf, ...`.
   - **If that line never appears:** the mod can't see the raid. Send me the log.
   - **If it appears but no nemesis joins:** that's the 25% roll. Set **Raid Chance** to 1 to force it.
4. After a nemesis has joined a raid, go back to where it used to be. The old body there should now be an ordinary Greydwarf.

## Testing 0.5.0

**Props and fitting.** The default placements are guesses, so expect to fit them.
1. Promote a creature, then press **Nearest nemesis cheats death**. It comes back with a fire scar, so expect a torch stub.
2. Open F1 → **10 - Prop Fitting** and turn on **Fitting Mode** next to it. It stops moving and shows every prop at once.
   - **If some props are missing:** look for `Props:` warnings in the log (e.g. "no item called ...", "has no 'UpperChest' bone"). Send them to me.
3. Pick a **Prop**, move the sliders until it looks right, then press **Save fit**. Do this for each prop, once on a Greydwarf and once on a Greyling.
4. Turn Fitting Mode off. Then send me the **Prop Fits** line from the config file and I'll make your fits the defaults.

**Stolen helmet.** Wear a bronze helmet and let a nemesis kill you (Mordor intensity). It should come back wearing a copy.

**Hunting.** Walk into a different biome from a nemesis that has killed you, and press **A nemesis hunts me now**. You get the warning, then it appears and the camera shows it.

## Testing 0.7.0

1. Start the game once and open F1. Sections should run 01 to 11 in order. Check a few values you'd changed (Aura Strength, Intensity, Debug Logging): they should have carried over. The log says `Moved settings from an older Grudges into the new layout`.
2. Open `Grudges.looks.txt`: your Fire scar skin should be in `[scar-skin]`. Open `Grudges.propfits.txt`: your saved fits should be there.
3. Fitting tool: Prop = `persona`, then switch **Personality** between two. The sliders should jump to each one's own numbers (zeros if it was never fitted).
4. Change a palette in the looks file, press **Reload data files**, and the nemeses should recolour.

## Testing 0.6.0

1. **Show roster** (or the log) lists each nemesis's personality. Older ones should have been given one.
2. Near a nemesis, set **Test Personality** (e.g. `Brute`) and press **Set personality of nearest nemesis**. It changes size and colour and gets its prop. Then press **Replay reveal** to see its emotes, and walk up to it to hear it taunt.
   - **If the prop is missing:** look for `Props:` lines (an item name I guessed may be wrong: BoneFragments, Mushroom, Club, Coins, AncientSeed, RawMeat).
   - **If it plays the wrong emote** (e.g. a death kneel instead of a kneel): tell me which. Emote names are matched by part of the name.
3. Fit each personality prop: Fitting Mode on, **Prop** = `persona`, pick the **Personality**, slide, Save.
4. Let a nemesis escape: you should only see the puff, no message.

## What to send back if something breaks

- Build errors: paste them as they are. I compiled against stand-in files, not the real game, so a wrong guess at a game function shows up here first.
- In-game problems: the lines in the BepInEx console starting with `[Warning:Grudges]` or `[Info   :Grudges]`.

## How it works (short version)

- **Roster vs. body.** The server keeps the roster (the truth). The creature in the world just carries the nemesis id in its saved data, so it survives zone unloads and restarts. Every game that has the creature loaded applies the name, size, colour and stats itself.
- **Your death** is reported to the server with the killer and any creatures within 30 m that saw it (within 12 m they always see it; further away, they must have noticed you).
- **The rules** (`src/Core.cs`) have no game code in them and are tested outside the game: `cd tests` then `dotnet run`.
