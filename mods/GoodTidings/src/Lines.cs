using System.Collections.Generic;
using UnityEngine;

namespace GoodTidings
{
    /// <summary>
    /// Dagny's lines: for each moment a few to choose from at random, as translation keys "goodtidings_trader_greet1"... (the count of each
    /// is here; the words are in translations/*.json). Petting Atlas has just the one ("Atlas shrugs.").
    /// </summary>
    internal static class Lines
    {
        private static readonly Dictionary<string, int> Counts = new Dictionary<string, int>
        {
            { "goodtidings_trader_greet", 5 }, { "goodtidings_trader_hello", 5 }, { "goodtidings_trader_bought", 5 },
            { "goodtidings_trader_sold", 4 }, { "goodtidings_trader_goodbye", 4 }, { "goodtidings_trader_leaving", 3 },
            { "goodtidings_trader_talk", 10 }, { "goodtidings_atlas_munch", 3 }, { "goodtidings_atlas_fed", 3 }, { "goodtidings_atlas_full", 2 }, { "goodtidings_bountiful", 3 }, { "goodtidings_muninn_right", 3 }, { "goodtidings_muninn_wrong", 3 },
            { "goodtidings_dverger_helped", 3 }, { "goodtidings_dverger_nofire", 2 }, { "goodtidings_dverger_rest", 2 }, { "goodtidings_dverger_fed", 3 },
            { "goodtidings_dverger_full", 2 }, { "goodtidings_dverger_thanks", 3 }, { "goodtidings_dverger_done", 2 },
            { "goodtidings_skald_risen", 3 }, { "goodtidings_skald_begin", 3 }, { "goodtidings_skald_between", 3 }, { "goodtidings_skald_bow", 3 }, { "goodtidings_dverger_neglected", 3 }, { "goodtidings_dverger_cold", 2 }, { "goodtidings_dverger_firefound", 2 },
        };

        /// <summary>One of a moment's lines at random, as a "$key" for the game to translate (where it's shown, in that player's language).</summary>
        internal static string Pick(string moment) =>
            Counts.TryGetValue(moment, out int n) ? $"${moment}{Random.Range(1, n + 1)}" : "$" + moment;
    }
}
