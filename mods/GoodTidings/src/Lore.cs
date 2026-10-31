using System.Collections.Generic;
using System.Linq;

namespace GoodTidings
{
    /// <summary>
    /// Muninn's old riddles: a hand-written set from the Eddas (Grímnismál, Vafþrúðnismál, Völuspá, Gylfaginning) and Valheim's own story,
    /// asked now and then instead of a "do you remember" one. Each is four translation keys: goodtidings_lore{n}_q (the riddle),
    /// _a (the right answer), _b and _c (the wrong ones). The server picks one when he lands (the same for everyone), and the world
    /// remembers which have been asked (a global key each), so none comes again until all have; then they start over.
    /// </summary>
    internal static class Lore
    {
        // ---- the world's wisdom: how many of Muninn's riddles have been answered rightly in this world (a global key for each, up to 10:
        // past that nothing changes), shared by everyone and saved with the world

        private const string WisdomKey = "goodtidings_wisdom_";
        internal const int WisdomMax = 10;

        internal static int Wisdom()
        {
            ZoneSystem zones = ZoneSystem.instance;
            return zones == null ? 0 : Enumerable.Range(1, WisdomMax).Count(n => zones.GetGlobalKey(WisdomKey + n));
        }

        /// <summary>One more riddle answered rightly (the game sends a global key to the server from any game).</summary>
        internal static void AddWisdom()
        {
            int w = Wisdom();
            if (w < WisdomMax) ZoneSystem.instance.SetGlobalKey(WisdomKey + (w + 1));
        }

        /// <summary>How many answers a made riddle offers: 3, 4 from five right answers, 5 from ten (harder, as a wrong one calls the beasts).</summary>
        internal static int Choices() => Wisdom() >= 10 ? 5 : Wisdom() >= 5 ? 4 : 3;

        /// <summary>The reward's extra goods: one for every five right answers, at most two.</summary>
        internal static int ExtraReward() => System.Math.Min(2, Wisdom() / 5);

        internal const int Count = 26;
        private const string AskedKey = "goodtidings_lore_";

        /// <summary>One not yet asked in this world, marked as asked now (server); -1 if none can be.</summary>
        internal static int PickUnasked()
        {
            ZoneSystem zones = ZoneSystem.instance;
            if (zones == null) return -1;
            var left = Enumerable.Range(1, Count).Where(n => !zones.GetGlobalKey(AskedKey + n)).ToList();
            if (left.Count == 0)
            {
                // all asked: start over
                for (int n = 1; n <= Count; n++) zones.RemoveGlobalKey(AskedKey + n);
                left = Enumerable.Range(1, Count).ToList();
            }
            int pick = left[UnityEngine.Random.Range(0, left.Count)];
            zones.SetGlobalKey(AskedKey + pick);
            return pick;
        }

        /// <summary>Lore riddle <paramref name="n"/> in this player's language, its answers in an order of their own.</summary>
        internal static Riddle Riddle(int n, System.Random rnd)
        {
            string L(string k) => Localization.instance.Localize("$goodtidings_lore" + n + "_" + k);
            string right = L("a");
            var answers = new List<string> { right, L("b"), L("c") }.OrderBy(_ => rnd.Next()).ToList();
            return new Riddle { Text = L("q"), Answers = answers.ToArray(), Correct = answers.IndexOf(right), IsLore = true };
        }
    }
}
