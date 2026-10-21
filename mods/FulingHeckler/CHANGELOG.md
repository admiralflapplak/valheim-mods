## 1.8.6
- On a server, his news (where he is, his moves, lines and sounds) only goes to players near him, not to everyone in the world.
- The Campfire Squatter sits on a seat by the fire when there is one (chairs, stools, benches, log benches, thrones, other mods' seats), in the player's sitting pose, and never brings a torch to a fire. He only picks real fires (campfire, hearth, bonfire, braziers), not sconces or standing torches, and no longer ends up on the roof over an indoor fire; the Tombstone Dancer no longer dances on the roof over an indoor grave.
- New Advanced settings (6 - Scenes): Seat Height and Seat Forward, to fit him to the seat.
- The F1 sections now show in order, 1 to 9.
- New deaths: lava and the incinerator now have quips of their own, in every language, and he acts them out.
- The settings are reorganised: General, Deaths, Look, Hat Fitting, Visits, Scenes, Thief & Gifter, Smoke & Sounds, Tools, with the fine-tuning (distances, speeds, animations, effects, diagnostics) behind "Advanced settings". Every value you set comes along to its new place, and the leftovers of older layouts are cleared out of the settings file.
- He finds his way like a real Fuling: he follows the game's own routes round walls and through doors, steps up no higher than a Fuling can, and appears only where he can walk to you. Before, he ran straight up walls, over pillars and onto roofs.
- When he doesn't come in person, his line goes in the chat window instead of the middle of the screen (setting "Text Position": Chat, TopLeft, Center or Off).
- On a server with the mod, the server decides the scenes that use real items: whether the Gifter comes, how many gifts, its loot table, and the Coin Thief's fee. Before, each player's own settings and loot file were used, so anyone could give themselves anything.
- Those settings are marked [Synced with Server] in F1: a server's admins change them from their own game, other players see them read-only, and your own values come back when you leave.
- On someone else's server, the Gifter's Test button needs the game's cheats on (devcommands).
- A quips file an older version wrote counts as unedited, so it follows the game's language too.
- He speaks German, French, Spanish, Brazilian Portuguese, Russian, Polish, Turkish and Chinese: built-in quips in the game's language, as long as you haven't edited your quips file (your own lines always win). The mod's messages are translated too.
- The Campfire Squatter no longer searches every object in the world for fires every few seconds at night (a stutter in big bases); the mod keeps its own list of fires.
- Calls the game's code directly instead of looking methods up by name, so a game update that changes them shows up at once instead of quietly switching features off.
- The mod's id is now nick.valheim.fulingheckler, like its sister mods. Your settings are copied over from nick.FulingHeckler.cfg the first time.

## 1.5.0
- The settings menu is tidied. Each scene's on/off switch, Test button and options now sit together, and the diagnostics (torch, outfit, puff and emote tests) are under Advanced. No setting was renamed, so your saved values carry over.

## 1.4.2
- Replaced a deprecated Unity call in the Campfire Squatter's fire search. Build files now use the shared project settings.
