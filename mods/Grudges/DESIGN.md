# Grudges: design notes

## Built so far (0.9.1)

- **Promotion:** creatures that kill you, or watch you die a silly death, can become named nemeses. Each has traits, a strength, a weakness and a personality.
- **Per-biome lists:** each biome keeps its own list. When a list is full, a newcomer fights the weakest for its place.
- **Champions:** each biome's strongest nemesis becomes its Champion.
- **Cheating death:** a nemesis can survive what should have killed it. It comes back scarred by whatever nearly killed it, with a resistance or a fear, an epithet, a prop and a new skin.
- **Raids:** nemeses can join raids on your base.
- **Hunting:** a nemesis with a grudge can follow you into other biomes.
- **Named trophies:** a nemesis that dies for good drops a trophy with its name and story.
- **The death reveal:** a cinematic when a nemesis rises, with player emotes on creatures whose skeleton allows it.
- **Personalities:** each has its own lines, emotes, prop, size and colours. The Hoarder is Fuling-only.
- **Creature families:** Greydwarf, Greyling, Fuling, Skeleton and the Charred so far. Names, lines, looks and fits are shared by family and can be overridden per creature type.

## Future: abilities

**Goal:** a nemesis should fight differently from an ordinary creature, not just look different. You should learn its tricks, and its personality and history should shape them. This is the half of Shadow of Mordor's Nemesis that Grudges doesn't have yet: the strengths and weaknesses you plan around.

### What the engine allows

We can't make new animations, so every ability has to reuse an animation the creature already has (attack, throw, jump, stagger), or a player emote on creatures that can do them. That still leaves a lot of room. Roughly from cheapest to dearest:

1. **Swapping what an attack does.** A creature's attacks are items. A Greyling's rock throw is an attack item with a rock projectile. We can copy the item and swap the projectile for an ooze bomb's, a fire projectile, or a frost one. The animation, timing and aim stay the same; only what lands changes. *Cheap and robust: this is the main tool.*
2. **Adding to an attack.** Extra damage types (fire, poison, frost), a status effect on hit (burning, poisoned, slowed), more knockback or stagger. *Cheap.*
3. **Changing behaviour.** The game's AI settings: flee health, how far it chases, how far it sees, whether it circles you or keeps its distance. Coward and Relentless already work this way. *Cheap.*
4. **Scripted abilities.** Our own code fires these on a cooldown or when something happens: low health, you come into view, an ally dies. Examples: vanishing in smoke, calling allies, healing, a leap. *Medium. They need care in multiplayer: only the game that controls the creature runs them, and everyone sees the result.*
5. **Anything that needs a new animation.** *Out of reach.*

### Ideas by personality

Each gives the player a tell and a counter, which is what makes it Nemesis rather than just a harder enemy.

| Personality | Ability | Tell / counter |
|---|---|---|
| **Trickster** | **Ooze bomb:** its throw becomes an ooze bomb (Greydwarfs and Greylings already throw, so this is a projectile swap). At low health, it vanishes in a puff of smoke and reappears a few metres behind you. | Cackles before throwing. Poison resistance counters the bombs; after a vanish, turn around. |
| **Braggart** | **Showboating:** after landing a hit, it sometimes stops to flex or laugh mid-fight. Each landed hit makes it a little stronger for a while. | The flex is an opening; punish it. Ignore the bait and it never builds up. |
| **Brute** | **Shrugs off blows:** much harder to stagger, more knockback. Charges when you back away. | Don't trade blows; dodge the charge and hit it from the side. |
| **Coward** | **Calls for help:** when hurt, it flees toward others of its kind and shrieks, alerting everything nearby. Drops a smoke cloud as it runs. | Finish it fast, or chase it into an ambush of your own making (a trap, or fire). |
| **Zealot** | **Rallies the faithful:** heals or shields nearby allies of its kind (like a Shaman, even if it isn't one). The Elder's Zealots might call up roots; Fulings, fire for Yagluth. | Kill it first, or pull it away from its friends. |
| **Glutton** | **Eats to heal:** at low health it stops to eat (the "eat" emote), healing a chunk. It's slow and greedy and goes for food you've dropped. | Interrupt the meal. Bait it with food. |
| **Hoarder** (Fulings) | **Thief:** a hit can take something from your inventory (coins, then a random item). Kill it and the trophy drop carries it back. If it escapes, it's gone until you find that Fuling again. | Guard your coins; it now has a reason to be hunted. This is the most Nemesis-like idea here. |

### Ideas by history

These are unlocked by what has happened to the nemesis, on top of its personality:

- **Learns from its scars.** It already gets a resistance or a fear. Next: arrow-scarred ones keep cover between you and them; fire-scarred ones flee from your torch; frost-scarred ones throw frost.
- **Learns from its kills.** One that has killed you three or more times copies how it did it: killed you with a poisoned throw? It always poisons now.
- **Champions get one extra ability** from another personality's list. That's what makes them bosses.
- **Grudge rage:** the first time it sees you after you scarred it, it ignores its usual behaviour (a Coward won't flee, a Glutton won't stop to eat) for a short burst.

### How it would be built

Data-driven, like everything else:

- **`Grudges.abilities.txt`** defines each ability once: what it is (a projectile swap, an extra damage type, an AI change or a scripted ability), its numbers, its cooldown, its trigger and its tell.
  ```
  [ooze-throw]
  kind = projectile
  attack = throw
  projectile = oozebomb_projectile    (to confirm in game)
  cooldown = 8
  ```
- **The personalities file says which abilities each personality gets**, with `abilities = ooze-throw, smoke-vanish`. Like every other setting, this can be overridden per family or per creature (`[Trickster:Fuling]` throws fire instead of ooze).
- **A creature that has no matching attack simply doesn't get that ability.** A Greyling has no melee slam, so it gets no Brute charge. Nothing breaks, so new creatures stay a data job.
- **Testing button:** "Give nearest nemesis ability ___" (a dropdown), the same pattern as Test Personality.

### Open questions

- How much harder should a nemesis get? Today they're stronger mostly through levels and traits. Abilities stack on top, so the defaults should be conservative, with an intensity setting.
- Should abilities be visible to the player before the fight (Mordor had intel), for example in the trophy story, in taunts, or in a future Grudge Board?
- Multiplayer: does the Hoarder's theft hit whoever it strikes, or only the player it holds a grudge against?

### Suggested order

1. **The ability framework plus projectile swaps** (Trickster's ooze bomb). Proves the idea cheaply on a creature that already throws.
2. **AI and behaviour abilities:** Brute, Coward, Braggart's showboating.
3. **Scripted abilities:** smoke vanish, Zealot's rally, Glutton eating.
4. **The Hoarder's theft and history-based abilities.** These need the most care.
