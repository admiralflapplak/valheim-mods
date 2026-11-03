# Cogling

A little copperwood robot that keeps your base in order, like Minecraft's copper golem: it walks between chests and sorts items into
them. Built with the hammer, taken down with the hammer to move it, as the game's training dummy (TWIG).

## What he does (decided with the user, 2026-11-01)

The feel: peaceful, not fast. A little helper with a personality, pottering about the base.

- **Antenna (home station)**: its own piece, built with the hammer. Its radius is his working area. **Antennas link** when their
  radii overlap: the linked network is one working area he can roam. With nothing to do he goes to the **nearest** antenna of his
  network (not a fixed home) and idles there.
- **Antenna light**: one lamp in the dish showing the status of the Cogling **closest to that antenna**, in his (state) eye colour.
- **Building him**: a separate piece. It can't be placed unless an antenna's area covers the spot (the build menu says why). Torn
  down with the hammer to move him (refund), as TWIG.
- **Tidying**: every chest in the working area. For each item spread over several chests, he carries it, one stack per trip, to the
  chest already holding the most of it (Quick Stack Store matches by the item's name, `m_shared.m_name`, and skips empty chests; Smart
  Containers adds item groups by name patterns: a later idea). Never: graves, carts/ships, chests a player has open, chests the ward or
  a lock keeps from the antenna's owner (ContainerAccess template; valheim-pitfalls on chests: the owner gives, no duplication).
- **Floor items**: picks up items lying in the working area and carries them to a chest holding that item (none: leaves them, and
  remembers that for a while). Never touched (agreed with the user 2026-11-01):
  - **eggs that are warming** (EggGrow.CanGrow, private: near a fire and under a roof). A **cold** egg is remembered from when he first
    saw it cold; still cold after "Cold egg wait" (default 3 min), he collects it; moved somewhere warm meanwhile, he leaves it.
    Toggle "Collect cold eggs" (default on).
  - **freshly dropped** items: a timer from when an item appears (default 60 s) before he touches it;
  - items resting on **furniture** (decorations on tables, shelves: the piece under it is in the Furniture category), and items
    that are placed **pieces** (ItemDrop.IsPiece: feasts and the like);
  - items **next to a player** (within ~2 m: about to be picked up);
  - items **in water** or that he can't path to; items over a **weight** limit (the dragon egg);
  - items in a **ward** this game's player can't use; drops **during a fight** (enemies near: waits, ties in with step 8);
  - an item **another Cogling** has claimed (a reservation, so two never race).
  Collected gladly: smelter / kiln output, loot after a fight.
- **Where an item goes** (floor items and tidying both, one rulebook; decided with the user 2026-11-01, after reading Smart
  Containers' cascade: same item, its item groups, name prefixes/postfixes, item type, fuzzy):
  1. the chest already holding the most of that item;
  2. a chest holding something of the **same group** (his own groups, "5 - Sorting": one setting per group, `name = Item, Item...`;
     defaults: ores, ingots, wood, stone, mushrooms, berries, vegetables, raw meat, cooked meat, food, valuables; editable);
  3. name patterns: Trophy* with trophies, *Seeds with seeds;
  4. a chest holding the same **item type** (the game's ItemType: materials, consumables, ammo...);
  5. else an **empty chest** (a new kind starts its own pile), else the chest with the most room.
  No reliance on Smart Containers: a one-time "Copy groups from Smart Containers" toggle copies its [ItemGroup] entries from its
  .cfg into ours when it's installed, then switches itself off.
- **Carrying**: visibly, the item's own model in his hands, arms in a carry pose (set on the robot after the Dverger's pose is copied:
  CoglingBody can override any muscle).
- **Repairing**: both damaged buildings in the area (as the hammer, free, with a hammering animation) while wandering, and worn
  tools/gear in the chests he tidies.
- **Animations** fitting each job: walking, carrying, opening a chest (the chest's own lid opens), hammering, idling at the antenna.
- **Voice** (changed 2026-11-01): the Dverger's voice lines (sfx_dverger_vo_idle / alerted / hurt / attack / death: their ZSFX
  clips) played from his own AudioSource on the game's SFX mixer, pitched up (~1.5-1.8) and made robotic (ring modulation, a little
  crunch): idle babble while working, hurt when hit, alerted when he flees. 0.6.x (CoglingVoice.cs, RobotFilter): the user's voice is
  Pitch 2, Robot 0.6, Robot tone 90 Hz, Crunch 0.2 (F1 "4 - Voice", each player's own); Babble every default 30 s (the user plays
  with 2). Eye colour default is the user's cyan 00CAFF.
- **Name**: renamed like a tamed animal (hover text shows it).
- **Danger**: enemies can attack him. He flees and hides at the **nearest cover** (out of the enemy's sight: behind a wall, inside a
  building), then goes back to work. At 0 health he **breaks down** into parts; after a while his antenna rebuilds him, free.
- **Pathfinding must be robust**: around buildings, up stairs, through doors (the game's AI can't open doors: he will have to, and
  close them behind him), and give up gracefully on unreachable chests instead of walking into walls.

## Build order (each step seen working in the game before the next)

1. Staff animations (0.3.1, built) and the look; make the mod reloadable in a world (PieceReload) to stop the trips to the main menu.
2. The antenna piece and its area (linked antennas; the Cogling refuses to be placed outside any area). 0.4.6: Antenna.cs, the user's choice: the Dvergr wisp light
   (dverger_demister) with its dark stand (the "torch" material) in plain wood (woodwall, from wood_beam: the pole's texture has pale
   corners) and the wisp, its light and glow green; no mist clearing (Demister and its force field removed); Piece fields from
   piece_dvergr_lantern_pole; usage tag Misc (the menu lists by tags: "Lighting" hid it). Misc, workbench, "Copper:5, FineWood:2,
   Resin:3", Radius 20 m; the ward's AreaMarker shows on real antennas and the ghost while building an antenna or a Cogling (on render
   copies a just-made circle showed as a pale slab). Antenna.NetworkAt / InArea for the later steps.
3. Walking: going to a spot reliably (pathfinding, doors, stuck detection), home and idle. 0.7.x: CoglingAI (a BaseAI in place of
   the Dverger's MonsterAI; Dverger's BaseAI fields copied in Awake, RPCs unregistered first and on destroy, Character.m_baseAI
   repointed: so it survives hot reloads and old Coglings are given it) and CoglingNavigator (Pathfinding.GetPath full path, else
   partial; doors: a chest-height ray along his way and, when stuck, the nearest closed door within 2.5 m, opened by the door's own
   UseDoor RPC unless keyed / never-closing / warded from this game's player, closed again once he's 2.5 m past and no player stands
   in it; stuck = under 0.5 m in 3 s: door, else a sidestep, gives up after 4). Rests 1.3 m in front of the nearest antenna of his
   network; testing: strolls ("Stroll every" 25 s), `cogling goto` / `cogling home`, "Log what he does". Walk speed 1.4 (server).
4. Floor items: pick up, carry visibly, put in a matching chest (the first complete job, smallest chest risk). 0.8.x, working in
   the user's base (seen: test wood to the wood chest, 16 coal scooped in one trip, a boar trophy to the trophy chest, a leather
   scrap): CoglingJob (item -> chest), Carried (what he carries saved in his ZDO as a one-slot inventory, so nothing is lost; dropped
   if he's taken down), Chests (own list via Container.Awake; Wanted's board excluded in ContainerAccess.SpecialPrefabs), FloorItems
   (the rules; `cogling items` explains each item), Sorting (the cascade). Picks up the game's way (RequestOwn, taken once owned),
   scoops the same items within 1.2 m up to a stack, picks up from within 1.6 m when the pathfinder can't get closer, leaves
   unreachable items 5 min (bars fallen inside the smelter's base). Chest: taken over and reloaded, lid opened with SetInUse.
   Carry pose (muscles, "Carry:" sliders in 9 - Testing) and the item's own model between his hands; a bend to pick up; all read
   from his ZDO so every player sees them. Note: a hot reload resets the "first seen" times (60 s wait again).
5. Tidying chests (consolidating stacks), the multiplayer-safe way.
6. Repairing buildings, then gear in chests.
7. Personality: babble, rename, hover text.
8. Danger: fleeing to cover, breaking down and being rebuilt.

## How it is made (0.1.0, the scaffold)

- **Model**: the user's own (Meshy "Copperwood Automaton", rigged by Mixamo): `reference/robotguy/Robot Hip Hop Dance.fbx` and its
  texture. `tools/convert_robot.py` (Blender 5.0, headless) writes `assets/cogling.bin` (rest T-pose skeleton, 25 Mixamo bones; one
  skinned mesh, 5462 vertices / 5956 triangles; Unity axes, feet on y = 0; the model is 0.853 m tall) and `assets/cogling.png`
  (1024 px). The FBX's dance is ignored. Re-run after changing the model:
  `blender -b --factory-startup -P tools/convert_robot.py -- <fbx> <texture.png> <abs path>/assets/cogling.bin <abs path>/assets/cogling.png`
  (Blender wants absolute paths for the texture.)
- **Body** (`CoglingPrefab.cs`): a copy of the game's `Dverger` (Humanoid, MonsterAI, its animator), scaled to Height (0.85 m; the
  Dverger is 1.45), no gear, drops, NpcTalk, voice, ragdoll; TWIG's wooden hit effects; sees and hears nothing (view/hear range 0).
- **Piece**: a `Piece` with every field copied from `piece_TrainingDummy` (TWIG: category Misc), our name, cost and station from
  settings (Costs.cs: "Copper:10, BronzeNails:10, FineWood:5", workbench). The game handles the rest: the hammer kills a creature-piece
  (`Player.RemovePiece` -> `Damage(1e10)`), and a creature's death drops its Piece's cost (`Character.OnDeath`).
- **Look** (`CoglingBody.cs`): the Dverger's renderers hidden; the robot's own skeleton built from the rest pose with the skinned mesh
  on it, made a humanoid with AvatarBuilder; each LateUpdate the Dverger's human pose is copied onto it (HumanPoseHandler), as
  GoodTidings' Retarget does. Material: a copy of the Dverger's body material with the robot's texture (normal/metal maps cleared).
  The Dverger's animator is set to AlwaysAnimate (its renderers are off, and its bones are what the robot copies).

## Stance (0.2.0)

- A Dverger's stance comes from what it holds, not from the creature: Humanoid.SetupAnimationState puts the held item's
  m_animationState into the animator's statef/statei (rogues: Crossbow; mages: Staves; empty hands: the unarmed weapon's).
  GoodTidings' note "the Dverger's animations are all crossbow ones" was about the rogue it copied; the mages show otherwise (the user).
- CoglingStance.cs sets the stance from F1 "9 - Testing" / "Stance" (default Staves) after the game does, and CoglingBody.Start sets it
  for the build ghost and pictures (no network there). The user chooses which looks best.

## Animations (0.6.x)

- CoglingAnimations.cs: the hidden Dverger body's animator plays another creature's controller (humanoid clips retarget onto the
  Dverger avatar; all six offered are humanoid: DvergerMage, Dverger, Goblin, GoblinShaman, GoblinBrute, Player). The user's choice
  for walking: **Fuling** (goblin_animator), Stance **Unarmed** (0.9.6; Atgeir before). Standing still: his own idle
  (reference/Idle.fbx -> assets/idle.anim by tools/convert_anim.py, CoglingClip/ClipPlayer on a hidden copy of his skeleton,
  blended in by speed, feet put back on the ground: the clip's hips floated him). Carrying: arms and shoulders from the idle even
  walking (the Fuling walk swung them apart), then the carry muscles on top; the user's values are the defaults (0.9.6): arms up
  0.193, forward -0.425 (on his rig -1 is forward), elbows 0.799, item out 0.126, height 0.045, item size 0.6 of his height, turn
  82.6 degrees. Item held between his hands. Jobs (carrying, hammering) will
  need their own poses or clips later.

## Lessons (0.2.x)

- Hammer-built Coglings stood at double the aimed point (+50 m up): the game places a ghost by its enabled colliders' nearest point,
  and switches off ghost colliders on layers it doesn't place against (the Dverger's capsule is on "character"; TWIG's on "piece").
  CoglingGhost.cs switches it back on (0.2.4). Console spawns were never affected.
- The creature's root isn't scaled any more (the game never does); the Dverger stays full size and hidden, only the capsule and EyePos
  shrink, and the robot model is built at Plugin.Height (CoglingModel scales it as it loads). With a scaled root the pose copy
  also drew the robot in the wrong place.
- Don't move the body to the origin around HumanPoseHandler calls (tried in 0.2.1): the handler mixes the old and new place and the
  robot drew kilometres away. A plain GetHumanPose/SetHumanPose on unscaled roots gives a local pose (body 0.04, 0.85, 0.11) and is right.
- CoglingBody logs "Cogling report" three times per Cogling (positions): testing only, remove before release.

## Look and feel (0.5.x)

- Taken down or broken: TWIG's hit and death effects (wood chips, smoke, its breaking sound; the Dverger's blood and ragdoll gone),
  and he falls apart (CoglingCollapse.cs: the effect prefab cogling_collapse, networked, not saved; his baked pose split by bone into
  ~12 pieces with physics, shrinking away after 4 s). Untested in game as of 0.5.1.
- Eye colour (F1 "3 - Looks", each player's own, default the painted green 0.35/1/0.55) and Eye glow: the glow map is white where
  the eyes are and the texture's painted green is made dark glass; each Cogling's colour is set on his own renderer
  (MaterialPropertyBlock) through CoglingBody.SetEyes(colour or null), meant for showing what he's doing later. The antennas' wisp,
  light, glow and sparks took the same colour until 0.5.3 removed the wisp.
- Antenna Size default 0.75 (the user's). 0.5.3 (the user: the wisp and its effects were too much): the wisp (`_enabled`: ball,
  light, sparks, sound, mist clearing) is gone, and the stand's mesh (readable) is split at y 1.16 (mesh units) into the post and the
  dish, which leans toward the antenna's front by "Antenna dish tilt" (F1 3 - Looks, default 35, live). Standing antennas reshape
  themselves in Antenna.Awake.

## Slacking off and personality (0.16.x, decided with the user 2026-11-03)

- Now and then a Cogling takes a break, **even with work to do**, never while frightened or wary (danger ends a break at once). What he
  carries he **drops on the ground**; his job is let go. Breaks: stare into the distance (at his area's edge facing out, or where he
  is; his head drifts, Dagny's head turn), look around, bored, dance (Robot Hip Hop Dance, facing a player if one is near), sit on the
  ground (the user's sitting clips are a floor sit, not for chairs): by a fire burning in his area facing it, else at its edge
  looking out, or where he is (sit down, sitting, stand up).
- **Personality is hidden**: traits drawn once from a saved number (diligence, curiosity, playfulness, restfulness, dreaminess) only
  change how often and what he does. The user wants it emergent ("oh, Frank is lazy"), never shown. `cogling status` shows it (testing).
- Clips: reference/RobotAnime (Mixamo), converted to assets/*.anim. The sit-down clip ("falling down") came from another rig: no
  right arm (his left arm is mirrored onto it) and it goes on to the floor, so only its first part is used (Sit: down clip until).
- Next: chatting between two Coglings (talking, talking2), joining a dance, reacting to the player; maybe the game's own emotes
  (the Fuling Heckler's stagger-swap trick works on his hidden Fuling body).

## Open questions

- Faction: `Players` for now (calm toward players), but monsters hunt that faction. Should monsters attack it? Should it take damage
  at all (the hammer removes it by damage, so full invulnerability would block removal)?
- The organising: which chests (range from its spot?), what rule (an item goes to a chest that already has it, as the copper golem),
  how it carries items (visibly in its hands?). Must use the ContainerAccess template and the chest pitfalls.
- Its build-menu icon (TWIG's for now), sounds, and whether it can be named.
