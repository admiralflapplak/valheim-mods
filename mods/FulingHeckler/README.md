# Fuling Heckler

When you die, a Fuling runs over, laughs at you, and runs off.

- He keeps count of every way you've died, per character, and brings it up.
- Sometimes he wears something on his head. At night he sometimes brings a torch.
- He hops when he arrives, then laughs, points or flexes at you using the player emotes, or takes a mock swing. Now and then he acts out your death.

He isn't a real creature: he has no AI, nothing is saved in the world, and he can't hurt anyone. On your own world he's yours alone. On a server that has the mod, everyone with the mod shares one goblin and sees him heckle each other (see [Playing on a server](#playing-on-a-server)). Friends without the mod aren't affected either way.

(This mod used to be called Ravens Remember. Your death tallies carry over automatically.)

## Building

You need the [.NET SDK](https://dotnet.microsoft.com/download) and Valheim with BepInEx.

```
dotnet build -c Release -p:ValheimDir="<Valheim folder>" -p:BepInExDir="<profile>\BepInEx" -p:DeployToGame=true
```

- `ValheimDir`: the game folder, e.g. `C:\Program Files (x86)\Steam\steamapps\common\Valheim`.
- `BepInExDir`: needed if you use a mod manager (Gale, r2modman). Point it at the profile's BepInEx folder.
- `DeployToGame=true`: copies `FulingHeckler.dll` into `BepInEx\plugins\FulingHeckler\` after building.

## Installing

Copy `bin\Release\FulingHeckler.dll` into `BepInEx\plugins\FulingHeckler\` (or use `DeployToGame=true`).

**Upgrading from Ravens Remember:** delete the old `BepInEx\plugins\RavensRemember\` folder. Otherwise both mods run at once.

Files the mod writes in `BepInEx\config\`:
- `nick.FulingHeckler.cfg`: settings, also in the F1 Configuration Manager menu
- `FulingHeckler.quips.txt`: everything he can say. Edit it freely.
- `FulingHeckler.loot.txt`: what the Gifter slips into your pockets
- `FulingHeckler_memory\<character>.txt`: his death tallies

## Settings (F1 menu)

Everything is in the F1 Configuration Manager, grouped by what it's for. Tick **Advanced settings** at the top of the menu to see the fine-tuning options too.

| Section | What's in it |
|---|---|
| **1 - General** | On/off, when he visits (**Delivery**, **Chance To Comment**, **Cooldown Seconds**, **Delay After Respawn** = 7 s, **Milestones**), how long his bubble stays up, and which kinds of death he comments on (**Comment On ...**) |
| **2 - Look** | His name (**Rocko**) and how often he uses it, his body, skin colour (**Recolour**, **Hue** 0.364, **Saturation** -0.158, **Brightness**), helmet (fishing hat), outfit (goblin legband), and the torch he sometimes brings at night |
| **3 - Hat Fitting** | Fitting player helmets onto his head by eye (see below) |
| **4 - Heckling** | How a death visit plays out: how close he stops, **Personal Space** (walk closer and he bolts), taunts (16 player animations: laughing, pointing, flexing, blowing kisses, bowing, toasting, *No no no*, Lisa Dance...), **Death Impressions** (he acts out how you died: a hard landing after a fall, swimming on dry land after drowning, despair after burning...), how far he runs before vanishing |
| **5 - Puffs & Sounds** | His smoke puff on arrival and exit (which game effects, how big), and your own sound recordings |
| **6 - Scenes** | Uninvited visits: how often, then each scene's on/off, **Test** button and options, grouped scene by scene (Lurker, Red Light Green Light, Copycat, Tombstone Dancer, Campfire Squatter) |
| **7 - Thief & Gifter** | The two scenes that use real items, both **off by default**, each with options and a **Test** button |
| **8 - Server** | **Share Goblin On My Server**, and **Echo Test** (advanced) for testing sharing alone |
| **9 - Tools** | Summon him and reload the quips file; *(adv)* test the torch, outfit, emotes and puffs, make him forget your deaths, debug logging |

## Fitting player helmets

Player helmets were made for a player's head, so on the Fuling they start out in the wrong place. Fix them by eye in F1 → **3 - Hat Fitting**:

1. Tick **Fitting Mode**. He walks up and stands still in front of you.
2. Pick a player helmet from the **Helmet** dropdown (not GoblinHelmet, which fits itself). He puts it on.
3. Drag **Up, Forward, Side, Tilt, Turn, Size** until it sits right. Changes save automatically, for that helmet.
4. Press **Use this fit for all other helmets**. Most player helmets are off by the same amount, so this fixes most of them at once.
5. Pick other helmets from the dropdown to check them. Adjust any that are still off (that saves a fit just for them), or press **Reset this helmet** to put one back on the shared fit.
6. Untick **Fitting Mode** and he runs off. Add the helmets you like to **Helmet Items**.

Fits are stored in **Saved Fits** (advanced), so you can copy that setting to friends. The mod's own fits (the fishing hat and a few others) always sit underneath yours, so emptying Saved Fits brings them back. **Reset this helmet** on one of those puts it back to the mod's fit. The dropdown lists the helmets in **Helmet Test List** (advanced); restart the game after changing that list.

## Your own sounds

F1 → **5 - Puffs & Sounds** → **Open sounds folder**. Drop `.ogg`, `.wav` or `.mp3` files (short, mono, trimmed tight) into these folders, then press **Reload sounds**:

| Folder | When | If empty, uses |
|---|---|---|
| `arrive` | he yells it as he runs over | silence |
| `taunt` | his bit (emote, swing, play dead) | silence |
| `poof` | as he vanishes | silence |
| `flee` | "Gotta go!" when you get too close | silence |
| `dance` | while he dances on your grave, clip after clip | silence |
| `caught` | you catch him dancing | `flee` |
| `spotted` | you spot the Lurker | `flee` |
| `bored` | the Lurker gives up | silence |
| `boo` | the Red Light BOO | `taunt` |
| `steal` | coin thief grabs your money | `taunt` |
| `coin_drop` | each dropped coin | silence |
| `gift` | the Gifter's giggle as he slips you something | silence |

Several files in a folder = he picks one at random. Sounds come from where he is and follow the game's effects volume.

## Skin colour

He comes recoloured by default (Hue 0.364, Saturation -0.158). Untick **Recolour** for the game's own Fuling colours.

F1 → **2 - Look** → tick **Recolour**, then drag **Hue**, **Saturation** and **Brightness**. Changes show live on any Fuling on screen, so tick **Fitting Mode** (3 - Hat Fitting) to keep him standing in front of you while you adjust. Only his body is recoloured, not his gear.

## Scenes (he turns up uninvited)

F1 → **6 - Scenes** (and **7 - Thief & Gifter**). Every 20-40 minutes of play (adjustable), one of these happens:

- **Lurker:** he stands off to your side, 20-30 m away, staring. Turn and look right at him and he flinches and bolts. Ignore him long enough and he gets bored and leaves.
- **Red Light Green Light:** he creeps up from behind, but only while you're not looking. Turn around and he freezes mid-pose. If he reaches you, he waits for you to turn around... BOO.

And separately:

- **Tombstone Dancer:** when you head back to where you died, there's a chance he's dancing on your grave. Get close and he runs off.
- **Copycat** (random): he appears in front of you and copies your emotes (/wave, /dance, /flex...). He always leaves on his own after 40 seconds with a wave. Point at him and he's offended and leaves.
- **Campfire Squatter:** at night, coming back to a lit fire, he might be sitting by it. He bolts when you get close.
- **Coin Thief** (random, **off by default**): if you carry at least 50 coins, he grabs 10 and runs, dropping them one by one so you can follow the trail and get them all back. A coin is only taken from you after one has been dropped, so nothing is ever lost. The dropped coins are real items: other players can see and pick them up. **Coin Thief Fee** (default 0) lets him keep a few.

- **Gifter** (random, **off by default**): he sneaks up from behind and slips 1-3 things into your pockets, then vanishes. Your bag just says "Your pockets feel heavier...", so you find out what you got later. Mostly junk (Pukeberries, bone fragments, stones), sometimes something nice, rarely treasure. Cheap equipment he gives you (rags, a club, a wooden shield) says **Crafted by Fuling**. What he says as he leaves hints at what you got. Turn around and catch him and he drops it all on the ground and bolts.
  - He only comes if you have at least 5 empty slots (**Gifter Free Slots Needed**), never takes your last slot, never pushes you over your carry weight, and stays away while you're heading back to your grave.
  - What he gives is in `BepInEx\config\FulingHeckler.loot.txt`: `item | weight | amount | tier`, one per line. Edit it, then press **Reload loot table**.
  - The gifts are real items, the same as anything you pick up.

Scenes never happen during fights, on boats, in water, in dungeons, in bed, or while he's already out. Each has a test button. The lines he says are in the quips file under `spotted`, `bored`, `boo`, `tombstone`, `copycat_bye`, `copycat_rude`, `campfire`, `thief_...` and `gift_...`.

## Playing on a server

**Server without the mod** (or your own world): nothing changes. Your goblin, your settings, only you see him. Friends without the mod aren't affected either.

**Server with the mod:** everyone who has the mod shares **one goblin**:
- **His look is the server's:** name, body, skin colour, hats (with the server's hat fits), outfit, torch, and how often he says his name. Your own look settings aren't touched; they come back on any other world.
- **Everyone sees his visits.** When someone dies, players nearby watch him roast them. Scenes (Lurker, Red Light, Copycat, Tombstone, Campfire, Coin Thief, Gifter) show up for everyone nearby too.
- **Anyone can mess with him:**
  - anyone can spot the Lurker;
  - anyone watching freezes him in Red Light;
  - anyone walking up makes him bolt (heckles, the Tombstone, the Campfire and the Copycat);
  - anyone who catches the Gifter makes him drop his gifts.
- Only one goblin turns up uninvited near a group at a time. Deaths still always get their heckle.
- Death tallies, quips, sounds and the other settings stay on each player's PC. His lines are sent as he says them, so everyone sees the same words. Sounds play from each player's own sounds folder.

To set it up:
1. Put `FulingHeckler.dll` in the server's `BepInEx\plugins\FulingHeckler\` folder.
2. Start the server once so it writes its config, then stop it.
3. Copy your own `BepInEx\config\nick.FulingHeckler.cfg` over the server's (the easiest way to give everyone your goblin), or edit the server's file directly.
4. **Share Goblin On My Server** (8 - Server) must be on in the server's config (it is by default). Turn it off and everyone keeps their own private goblin.

Hosting from your own game (not a dedicated server) works the same way. Players who join get your goblin, and you see theirs.

Players without the mod can still join. They never see him and are never sent anything. Players on a different version get a message to update, and keep their own private goblin until they do.

**How it works:** the player he's visiting runs him, exactly as on your own. Their game tells the server what he's doing (where he is, his moves, lines, sounds, puffs), about ten times a second. The server passes that on to the other players with the mod, and their games draw a copy that follows along. He's still never a real creature in the world: nothing is saved, and if a player leaves mid-scene, their goblin just fades out for everyone else.

Server messages in the log start with `Server sync:` or `Shared goblin:`.

### Testing alone (echo test)

1. Run Valheim's dedicated server on your own PC, with the mod installed (Steam → Library → Tools → **Valheim Dedicated Server**, plus BepInEx), and join it at `127.0.0.1:2456`.
2. In F1, tick **Advanced settings**, then **8 - Server → Echo Test**.
3. Press **Summon Fuling** or any scene test button. A second goblin marked **(echo)** appears beside him, doing exactly what your friends would see. It's built from what came back through the server.
4. Looking at or walking up to the echo copy counts as a friend doing it. For example, stare at the echo Lurker and the real one should flinch and bolt.
5. Turn **Echo Test** off when you're done. While it's on, real friends don't see your goblin.

## Writing quips

Each line in `FulingHeckler.quips.txt` looks like this:

```
event | when | text
enemy:troll | repeat | Troll again! Fuling bet on troll.
```

Write `{me}` where he talks about himself: it becomes his name or plain "Fuling" (by **Say His Name Chance**), the same one for the whole line. Plain "Fuling" always stays "Fuling", for goblins in general ("Fuling village"). The file's header lists every event, `when` value and placeholder. Press **Reload quips file** in F1 after editing.

## How he works

He's a copy of the goblin's 3D model and animations only. The mod strips every script off the copy and moves it around itself. Nothing about him is sent over the network. If the copy ever still had network parts, the mod refuses to spawn him and logs an error. Helmets and the torch are copied the same way.

He appears about 18 m away, in the direction you're facing, runs up to you, says his line, does his bit, and runs off. If there's no dry ground nearby, his line appears as a text message instead.

## Things to report after testing

- **Emotes:** the log line `Player emotes the Fuling can use:` lists every emote name. The emote test button has him wave.
- **Gear:** the log line `Goblin can carry:` lists everything a real Fuling can wear or hold. Those names work in **Helmet Items** and **Night Item**.
- **Moves:** play-dead uses the animator's `dead` and `wakeup`, which were made for other purposes. If he doesn't fall over or get up properly, report it.
