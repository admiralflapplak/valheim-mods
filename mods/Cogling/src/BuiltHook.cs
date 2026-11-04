using HarmonyLib;

namespace Cogling
{
    /// <summary>A player placed a piece (on that player's game): a curious Cogling may come and look at it (CoglingSlack, Inspect).</summary>
    [HarmonyPatch(typeof(Piece), nameof(Piece.SetCreator))]
    internal static class BuiltHook
    {
        private static void Postfix(Piece __instance) => CoglingSlack.Built(__instance);
    }
}
