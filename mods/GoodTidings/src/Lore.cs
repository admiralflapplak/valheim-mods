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
