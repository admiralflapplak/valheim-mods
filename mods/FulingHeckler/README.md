# Fuling Heckler

![Rocko the Fuling laughing, Hildir's camp behind him](thunderstore/screenshots/00-laugh.png)

A comedy mod. Valheim's deaths can sting: the troll that flattened you, the cliff you misjudged, the swim you shouldn't have tried. Fuling Heckler adds a little levity to them, and to the quiet stretches in between, in the shape of one cheeky goblin who thinks your misfortunes are the funniest thing in the world. Out of the box he changes nothing about how the game plays (the two pranks that touch your items are off until you switch them on): he's only there to make you laugh, or groan.

Meet Rocko. When you die, he runs over, laughs at you, and runs off.

- He keeps count of every way you've died, per character, and brings it up. First times and milestones (your 5th, 10th, 25th troll death...) always get a visit.
- He never goes anywhere without his fishing hat, and on some nights he brings a torch.
- He laughs, points or flexes at you, or takes a mock swing. Now and then he acts out your death: a hard landing after a fall, swimming on dry land after drowning.
- He has his own voice, and over a hundred lines, from "Hah! New way to die!" to "Troll again! Troll not even remember you. Fuling remember. Fuling ALWAYS remember."
- He also turns up uninvited: lurking at the edge of your camp, creeping up while you aren't looking, dancing on your grave, copying your emotes.

![Rocko by Hildir's camp, his speech bubble over him: "Wolf-man get you at night! Fuling sleep at night. Smart Fuling."](thunderstore/screenshots/01-heckle.png)

He isn't a real creature. He has no AI, nothing about him is saved in the world, and he can't hurt anyone. He finds his way around like a real Fuling, though: round walls and through doors, never up onto your roof.

## He turns up uninvited

Every 20-40 minutes of play, one of these happens:

- **Lurker:** he stands off to your side, 20-30 m away, staring. Turn and look right at him and he flinches and bolts. Ignore him long enough and he gets bored and leaves.

  ![The Lurker at the edge of the trees, staring](thunderstore/screenshots/03-lurker.png)

- **Red Light Green Light:** he creeps up from behind, but only while you're not looking. Turn around and he freezes mid-pose. If he reaches you, he waits for you to turn around... BOO.

  ![Red Light Green Light: he creeps up behind you while your back is turned](thunderstore/screenshots/02-red-light.png)

- **Copycat:** he appears in front of you and copies your emotes (/wave, /dance, /flex...). After 40 seconds he waves and leaves. Point at him and he takes offence and leaves.

And at the right moments:

- **Tombstone Dancer:** when you head back to where you died, he might be dancing on your grave. Get close and he runs off.
- **Campfire Squatter:** at night, coming back to a lit fire, he might be sitting by it, on a chair or bench if there's one nearby. He bolts when you get close.

  ![The Campfire Squatter on a log seat by Hildir's fire at night](thunderstore/screenshots/04-campfire.png)

Two pranks use real items, so both are **off by default** (F1 → 7 - Thief & Gifter):

- **Coin Thief:** if you carry at least 50 coins, he grabs 10 and runs, dropping them one by one so you can follow the trail and get them all back. A coin only leaves your pocket once it has been dropped, so nothing is lost. The dropped coins are real items: other players can pick them up too.

  ![The Coin Thief running off, a coin dropping behind him](thunderstore/screenshots/05-coin-thief.png)

- **Gifter:** he sneaks up from behind and slips 1-3 things into your pockets, then vanishes. All you're told is "Your pockets feel heavier...". Mostly junk, sometimes something nice, rarely treasure. Turn around and catch him and he drops it all on the ground and bolts. He only comes when you have room in your bag and your carry weight.

He never turns up during fights, on boats, in water, in dungeons, in bed, or while he's already out.

## Settings (F1)

Everything is in the F1 Configuration Manager; tick **Advanced settings** at the top for the fine-tuning.

| Section | What's in it |
|---|---|
| **1 - General** | On/off; when he comes in person (**Delivery**: first-time deaths and milestones, or **Always**); where his line goes otherwise (**Text Position**: the chat window, a screen corner, mid-screen, or off); how often he comments on repeat deaths |
| **2 - Deaths** | Which kinds of death he comments on |
| **3 - Look**, **4 - Hat Fitting**, **5 - Visits** | His look and how a visit plays out |
| **6 - Scenes** | How often he turns up uninvited, and each scene on or off |
| **7 - Thief & Gifter** | The two pranks with real items |
| **8 - Smoke & Sounds** | His smoke puff and his voice |
| **9 - Tools** | Summon him, make him forget your deaths; a **Test** button for every scene is in its own section |

His death tallies are kept per character in `BepInEx\config\FulingHeckler_memory\`.

## Console commands

For trying him out (F5 console; turn the console on in Settings → Gameplay):

| Command | What it does |
|---|---|
| `heckler summon` | He comes and heckles you now |
| `heckler lurker` / `redlight` / `copycat` / `tombstone` / `campfire` / `thief` / `gifter` | Start that scene now |

## Playing on a server

**A server without the mod** (or your own world): he's yours alone; only you see him. Friends without the mod aren't affected.

**A server with the mod:** everyone who has it shares **one goblin**. When someone dies, players nearby watch him roast them, and his scenes show up for everyone nearby. Anyone can spot the Lurker, freeze him in Red Light, make him bolt by walking up, or catch the Gifter. The server decides the two pranks with real items (and the Gifter's loot), so nobody can hand themselves anything. Install the mod on the server too; hosting from your own game works the same way. Players without the mod can still join: they never see him and are never sent anything.

## Other mods and known clashes

- No clashes are known. He's a copy of the Fuling's model and animations with every script stripped off, moved by the mod itself: no creature, nothing saved, nothing in the world's network data.
- It patches only a few game methods, and lets the game's code run in each: `Player.OnDeath` (to see how you died), `Player.StartEmote` (for the Copycat), `Fireplace.Awake` (to know where the fires are) and the game's start-up (for the server sync), plus BepInEx's `ConfigFile.Save` (so the server's values never overwrite your own settings file).
- Mods that change player emotes or Fuling models may change how he looks or moves.

## Languages

English, German, French, Spanish, Brazilian Portuguese, Russian, Polish, Turkish and Chinese: his lines and the mod's messages follow the game's language. The translations beyond English were written with the help of an AI and haven't been checked by native speakers: corrections are welcome. The F1 settings are in English.

## Made with AI

Made with the help of Claude (Anthropic).
