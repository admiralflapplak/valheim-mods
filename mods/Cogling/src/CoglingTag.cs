using HarmonyLib;
using Nick.Shared;
using Splatform;
using UnityEngine;

namespace Cogling
{
    /// <summary>
    /// Talking to a Cogling, as to a tame: looking at him shows his name with what he's doing under it in grey (as the Good Tidings
    /// skald's prompt); E greets him (he turns to you, chirps and does his happy idle); the alt key + E renames him (the game's rename
    /// box, the name filtered as the game filters tames' names). His name, its author and his task are saved on him, so every player
    /// sees the same; the owner of the Cogling writes them (renaming and greeting go to him by RPC).
    /// </summary>
    internal sealed class CoglingTag : MonoBehaviour, Interactable, TextReceiver
    {
        internal const string NameKey = "cogling_tag", AuthorKey = "cogling_tag_author", TaskKey = "cogling_task";
        private const string RenameRpc = "cogling_rename", GreetRpc = "cogling_greet";
        private ZNetView _view;

        private void Awake()
        {
            _view = GetComponent<ZNetView>();
            if (_view == null || _view.GetZDO() == null) return;   // (the build ghost, pictures)
            _view.Unregister(RenameRpc);
            _view.Unregister(GreetRpc);
            _view.Register<string, string>(RenameRpc, RPC_Rename);
            _view.Register<Vector3>(GreetRpc, RPC_Greet);
        }

        private void OnDestroy()
        {
            if (_view == null || !_view.IsValid()) return;
            _view.Unregister(RenameRpc);
            _view.Unregister(GreetRpc);
        }

        public bool Interact(Humanoid user, bool hold, bool alt)
        {
            if (hold || _view == null || !_view.IsValid()) return false;
            if (alt)
            {
                // (as Tameable.SetName: names are other players' writing, so the platform's permission is asked first)
                var privilege = PlatformManager.DistributionPlatform.PrivilegeProvider.CheckPrivilege(Privilege.ViewUserGeneratedContent);
                if (!privilege.IsGranted()) { PlatformManager.DistributionPlatform.UIProvider.ResolvePrivilege?.Open(Privilege.ViewUserGeneratedContent); return true; }
                TextInput.instance.RequestText(this, "$hud_rename", 20);
                return true;
            }
            _view.InvokeRPC(ZNetView.Everybody, GreetRpc, user.transform.position);
            return true;
        }

        public bool UseItem(Humanoid user, ItemDrop.ItemData item) => false;

        public string GetText()
        {
            if (_view == null || !_view.IsValid()) return "";
            string name = _view.GetZDO().GetString(NameKey);
            string author = _view.GetZDO().GetString(AuthorKey, null);
            return string.IsNullOrEmpty(author) || author == "host"
                ? CensorShittyWords.FilterUGC(name, UGCType.Text, default(PlatformUserID), 0L)
                : CensorShittyWords.FilterUGC(name, UGCType.Text, new PlatformUserID(author), 0L);
        }

        public void SetText(string text)
        {
            if (_view == null || !_view.IsValid()) return;
            var user = PlatformManager.DistributionPlatform.LocalUser;
            _view.InvokeRPC(RenameRpc, text, user.IsSignedIn ? user.PlatformUserID.ToString() : "host");
        }

        private void RPC_Rename(long sender, string name, string author)
        {
            if (!_view.IsValid() || !_view.IsOwner()) return;
            _view.GetZDO().Set(NameKey, (name ?? "").Trim());
            _view.GetZDO().Set(AuthorKey, author ?? "host");
        }

        /// <summary>Greeted (on every player's game): his chirp and happy idle; his owner turns him toward the greeter.</summary>
        private void RPC_Greet(long sender, Vector3 from)
        {
            GetComponentInChildren<CoglingBody>()?.Greet();   // (his happy idle: the user liked it better than a wave)
            GetComponent<CoglingVoice>()?.Greet();
            if (_view.IsValid() && _view.IsOwner()) GetComponent<CoglingAI>()?.Greeted(from);
        }

        // ---- what the game shows: his name (name plate, hover) and the hover text

        internal static string NameOf(Character c)
        {
            string name = c.GetComponent<CoglingTag>()?.GetText().RemoveRichTextTags();
            return string.IsNullOrEmpty(name) ? Localization.instance.Localize(c.m_name) : name;
        }

        /// <summary>What he's doing, in this player's language (saved on him as a key and its words, as his owner set it).</summary>
        internal static string TaskOf(Character c)
        {
            string task = c.GetComponent<ZNetView>()?.GetZDO()?.GetString(TaskKey) ?? "";
            if (task.Length == 0) return "";
            string[] f = task.Split('|');
            string a = f.Length > 1 ? Localization.instance.Localize(f[1]) : "", b = f.Length > 2 ? Localization.instance.Localize(f[2]) : "";
            return Translations.T(f[0], a, b);
        }
    }

    [HarmonyPatch(typeof(Character), nameof(Character.GetHoverName))]
    internal static class CoglingHoverName
    {
        private static void Postfix(Character __instance, ref string __result)
        {
            if (CoglingStance.IsCogling(__instance)) __result = CoglingTag.NameOf(__instance);
        }
    }

    [HarmonyPatch(typeof(Character), nameof(Character.GetHoverText))]
    internal static class CoglingHoverText
    {
        private static void Postfix(Character __instance, ref string __result)
        {
            if (!CoglingStance.IsCogling(__instance)) return;
            string task = CoglingTag.TaskOf(__instance);
            __result = CoglingTag.NameOf(__instance)
                       + (task.Length > 0 ? $"\n<color=#bdb7a9>{task}</color>" : "")
                       + Localization.instance.Localize("\n[<color=yellow><b>$KEY_Use</b></color>] $cogling_greet"
                                                        + "\n[<color=yellow><b>$KEY_AltPlace</b></color> + <color=yellow><b>$KEY_Use</b></color>] $hud_rename");
        }
    }
}
