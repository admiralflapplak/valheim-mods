using System.Collections.Generic;

namespace WeaponOils
{
    /// <summary>What an oil does to the weapon it coats. The first six add a damage type; the rest change how the weapon behaves.</summary>
    internal enum OilEffect
    {
        Fire, Frost, Poison, Lightning, Spirit, Physical,
        StaminaSaving,   // attacks cost less stamina
        Durability,      // the weapon wears slower
        LifeSteal,       // part of the damage dealt heals the wielder
        Stagger,         // hits stagger harder
        Guard,           // whatever the wielder blocks with (shield or weapon) blocks with more power
        Pure,            // untyped damage, which no armour or resistance turns aside (the game's m_damage)
        Gathering,       // chops trees and breaks rock and ore harder (chop and pickaxe damage)
    }

    /// <summary>
    /// One oil. DRAFT table: the effects, amounts, ingredients and gates are a first guess to be tuned in the game (and moved to settings
    /// once they settle). The press takes one item, like the fermenter, so the cauldron recipe holds every ingredient. Ingredient text uses the Costs.cs format ("Item:amount, Item:amount") with the game's prefab names (all checked
    /// against the running game, 2026-10-17, with `prefabs <text>`).
    ///
    /// Each oil is made from what its biome gives you, and biomes have as many oils as make sense: every biome gets at least one, and
    /// some get more because their creatures and plants suggest more. To add one: add a line, add its translation keys
    /// (<c>nick_oil_&lt;id&gt;</c>, <c>nick_oil_&lt;id&gt;_desc</c>), and an effect if none of the above fits.
    /// </summary>
    internal sealed class OilDef
    {
        public string Id;            // short id, saved on the weapon ("ember"); prefab is "nick_oil_<id>", base is "nick_oilbase_<id>"
        public string Biome;         // where its ingredients come from
        public string GlobalKey;     // boss kill that opens its biome ("" = from the start); only enforced when the setting says so
        public OilEffect Effect;
        public float Amount;         // damage types: added damage as a part of the weapon's own (0.25 = +25%); the rest: the part changed
        public OilEffect? Extra;     // a second effect, for the oils whose rare ingredients should buy more than one thing
        public float ExtraAmount;

        public bool Has(OilEffect effect) => Effect == effect || Extra == effect;
        public float AmountOf(OilEffect effect) => (Effect == effect ? Amount : 0f) + (Extra == effect ? ExtraAmount : 0f);
        public string BaseCost;      // the whole cost, at the cauldron (as with mead, everything goes in there; the press only matures it)
        public int CauldronLevel;    // the cauldron's level (its extensions)
    }

    internal static class OilDefs
    {
        // Gate keys are the boss before each biome: Eikthyr opens the Black Forest, the Elder the Swamp, Bonemass the Mountain,
        // Moder the Plains, Yagluth the Mistlands, the Queen the Ashlands.
        public static readonly List<OilDef> All = new List<OilDef>
        {
            // Costs (balance pass, 0.7.5): a base makes 4 oils of 20 minutes each, so it costs about what a mead base does (~15 items).
            // Each asks for one thing the biome makes you go and get (a creature's drop, a find), a binder you gather in bulk (resin,
            // tar, sap) and something farmed or picked. Rare and gear materials (flametal, black marble, crystal) only one at a time,
            // or not at all. Cauldron levels climb with the biomes (the game's cauldron goes to 7 with its six extensions).

            // Strengths (balance pass, 0.7.6): every amount is a part of the weapon's own, so each oil keeps its worth as weapons get
            // better; the later oils are stronger, and are chosen to work against their own biome's creatures (checked against their
            // damage modifiers in the game: the Mistlands are all immune to spirit and resist every physical type, the Ashlands' Charred
            // and valkyries are weak to spirit and resist fire, the Swamp's undead and blobs are immune to poison, which pays off later).

            // Meadows: resin burns, flint is the first edge a Viking has. Cheap, and still worth a slot at the end (greydwarves, wraiths,
            // abominations, lox, necks and hatchlings are weak to fire).
            new OilDef { Id = "ember",     Biome = "Meadows",      GlobalKey = "",                    Effect = OilEffect.Fire,          Amount = 0.20f, BaseCost = "Resin:8, Honey:4, Dandelion:3", CauldronLevel = 1 },
            new OilDef { Id = "keen",      Biome = "Meadows",      GlobalKey = "",                    Effect = OilEffect.Physical,      Amount = 0.12f, BaseCost = "Flint:6, Resin:5, Feathers:4", CauldronLevel = 1 },

            // Black Forest: troll fat is slick and tiring to swing through; pine bark and core wood toughen an edge, and an axe or a
            // pickaxe with it bites deeper into trees and rock all game long
            new OilDef { Id = "trollfat",  Biome = "Black Forest", GlobalKey = "defeated_eikthyr",    Effect = OilEffect.StaminaSaving, Amount = 0.25f, BaseCost = "TrollHide:2, Resin:6, Thistle:4", CauldronLevel = 1 },
            new OilDef { Id = "barkhide",  Biome = "Black Forest", GlobalKey = "defeated_eikthyr",    Effect = OilEffect.Durability,    Amount = 0.60f,
                Extra = OilEffect.Gathering, ExtraAmount = 0.30f, BaseCost = "RoundLog:10, Resin:6, GreydwarfEye:4", CauldronLevel = 1 },

            // Swamp: the leeches and blobs, and what rots in the crypts. Poison is lost on the swamp's own undead, but nothing living
            // after it shrugs it off (trolls, wolves, goblins, seekers), so it is strong for its price.
            new OilDef { Id = "venom",     Biome = "Swamp",        GlobalKey = "defeated_gdking",     Effect = OilEffect.Poison,        Amount = 0.35f, BaseCost = "Ooze:6, Guck:3, Entrails:4", CauldronLevel = 2 },
            new OilDef { Id = "leech",     Biome = "Swamp",        GlobalKey = "defeated_gdking",     Effect = OilEffect.LifeSteal,     Amount = 0.06f, BaseCost = "Bloodbag:6, Entrails:4, Turnip:3", CauldronLevel = 2 },

            // Mountain: one oil, for the cold, with a chill that slows (lava blobs and voltures in the Ashlands are weak to frost)
            new OilDef { Id = "rime",      Biome = "Mountain",     GlobalKey = "defeated_bonemass",   Effect = OilEffect.Frost,         Amount = 0.35f, BaseCost = "FreezeGland:4, Onion:4, Resin:4, Crystal:1", CauldronLevel = 3 },

            // Plains: the deathsquito's needle and tar for a storm (almost nothing resists lightning); lox are heavy and shove things about
            new OilDef { Id = "storm",     Biome = "Plains",       GlobalKey = "defeated_dragon",     Effect = OilEffect.Lightning,     Amount = 0.40f, BaseCost = "Needle:4, Tar:4, Barley:6", CauldronLevel = 4 },
            new OilDef { Id = "loxfat",    Biome = "Plains",       GlobalKey = "defeated_dragon",     Effect = OilEffect.Stagger,       Amount = 0.50f, BaseCost = "LoxMeat:3, Tar:3, Cloudberry:6", CauldronLevel = 4 },

            // Mistlands: wisp light passes through any hide, so its damage is the kind no armour or resistance turns aside (seekers resist
            // every physical type and gjall resist fire); the seekers' royal jelly steadies the guard, whatever it is held up with
            new OilDef { Id = "wisp",      Biome = "Mistlands",    GlobalKey = "defeated_goblinking", Effect = OilEffect.Pure,          Amount = 0.45f, BaseCost = "Wisp:2, Sap:6, MushroomJotunPuffs:4", CauldronLevel = 5 },
            new OilDef { Id = "royal",     Biome = "Mistlands",    GlobalKey = "defeated_goblinking", Effect = OilEffect.Guard,         Amount = 0.40f, BaseCost = "RoyalJelly:4, Sap:6, Carapace:2", CauldronLevel = 5 },

            // Ashlands: molten fire that also burns the dead: fire for the living, spirit for the Charred, the valkyries and Morgen
            // (who all resist fire), so it is the best oil there and still a strong one anywhere
            new OilDef { Id = "cinder",    Biome = "Ashlands",     GlobalKey = "defeated_queen",      Effect = OilEffect.Fire,          Amount = 0.40f,
                Extra = OilEffect.Spirit, ExtraAmount = 0.40f, BaseCost = "SulfurStone:6, CharredBone:6, MoltenCore:1", CauldronLevel = 6 },
        };

        public static OilDef Get(string id) => All.Find(o => o.Id == id);
    }
}
