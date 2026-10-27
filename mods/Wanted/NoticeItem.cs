using System;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;
using static Nick.Shared.Translations;

namespace Wanted
{
    /// <summary>
    /// A notice taken off a board: an item that carries its contract in its custom data (and later its progress), shown with its kind's
    /// coloured mark. One prefab; the icon is picked by the item's variant (one per kind). A copy of the game's leather scraps (a light,
    /// plain material) wearing a sheet of our paper. Registered by name through ItemReload (safe to repeat; a hot reload replaces it without
    /// the game ever missing it). It carries data, so it never stacks, and it is worth nothing at the trader.
    /// </summary>
    internal static class NoticeItem
    {
        public const string PrefabName = "nick_wanted_notice";
        public const string ItemName = "$wanted_notice";
        private const string KeyContract = "Wanted.contract";   // m_customData: the contract, as Contract.Write

        internal static GameObject Prefab;
        private static GameObject _holder;

        public static void Register(ZNetScene scene, ObjectDB db)
        {
            if (scene == null && db == null) return;
            scene = scene != null ? scene : ZNetScene.instance;
            if (Prefab == null && scene != null) Prefab = Build(scene);
            if (Prefab == null) return;
            Prefab.GetComponent<ItemDrop>().m_itemData.m_dropPrefab = Prefab;   // (ItemDrop.Awake sets it, which a prefab under an inactive holder never runs)
            ItemReload.Replace(scene, db, new[] { Prefab });
        }

        public static void Forget()
        {
            Prefab = null;   // the prefab and its holder stay alive for notices already out there; the next copy replaces them by name
            _holder = null;
        }

        private const int Kinds = 4;

        private const string KeyDone = "Wanted.done";   // m_customData: "1" once a bounty's last kill is counted

        /// <summary>The bounty is finished: its icon gets the wax seal.</summary>
        public static void MarkDone(ItemDrop.ItemData item, Contract c) => item.m_customData[KeyDone] = "1";

        private static bool IsDone(ItemDrop.ItemData item) => item.m_customData.TryGetValue(KeyDone, out string d) && d == "1";

        public static bool Is(ItemDrop.ItemData item) => item != null && item.m_shared.m_name == ItemName;

        public static Contract ContractOf(ItemDrop.ItemData item) =>
            Is(item) && item.m_customData.TryGetValue(KeyContract, out string text) ? Contract.Read(text) : null;

        /// <summary>How many notices this player carries.</summary>
        public static int Carried(Player player) => player.GetInventory().GetAllItems().Count(Is);

        /// <summary>Puts a notice for this contract in the player's bag, or at their feet if the bag has no room (never lost).</summary>
        public static void Give(Player player, Contract contract)
        {
            if (Prefab == null || player == null) return;
            ItemDrop.ItemData item = Prefab.GetComponent<ItemDrop>().m_itemData.Clone();
            item.m_dropPrefab = Prefab;
            item.m_stack = 1;
            item.m_customData[KeyContract] = contract.Write();   // (set before it goes in: nothing can stack onto it then)
            if (!player.GetInventory().AddItem(item))
            {
                Transform t = player.transform;
                ItemDrop.DropItem(item, 1, t.position + t.forward * 0.6f + Vector3.up, t.rotation);
                player.Message(MessageHud.MessageType.Center, T("wanted_dropped"));
            }
        }

        // ---- the prefab -----------------------------------------------------------------------------------------------

        private static GameObject Build(ZNetScene scene)
        {
            GameObject source = scene.GetPrefab("LeatherScraps");
            if (source == null) { Plugin.Log.LogWarning("The game has no leather scraps to copy: no notices this session"); return null; }

            _holder = new GameObject("Wanted_items");
            _holder.SetActive(false);
            Object.DontDestroyOnLoad(_holder);

            GameObject go = Object.Instantiate(source, _holder.transform);
            go.name = PrefabName;
            ItemDrop drop = go.GetComponent<ItemDrop>();
            ItemDrop.ItemData.SharedData s = drop.m_itemData.m_shared;
            s.m_name = ItemName;
            s.m_description = "$wanted_notice_desc";
            s.m_itemType = ItemDrop.ItemData.ItemType.Misc;
            s.m_maxStackSize = 1;      // it carries its own contract: two must never merge
            s.m_autoStack = false;
            s.m_value = 0;             // a free paper with a price would be a coin machine
            s.m_weight = 0.1f;
            s.m_teleportable = true;

            try { Dress(go, scene); }
            catch (Exception e) { Plugin.Log.LogWarning("Notice look: " + e.Message); }

            // 0-3: a notice of each kind; 4-7: the same, finished (a wax seal on it). Picked by GetIcon below from the item's own contract,
            // not by the game's variants (it resets a variant its item doesn't declare, and declared ones become a choice when crafting).
            var icons = new Sprite[Kinds * 2];
            for (int k = 0; k < icons.Length; k++) icons[k] = Paper.IconFor((Kind)(k % Kinds), scene, done: k >= Kinds);
            if (icons.All(i => i != null)) s.m_icons = icons;
            drop.m_itemData.m_dropPrefab = go;
            return go;
        }

        /// <summary>The leather scraps' own look is switched off; a sheet of paper lies in its place.</summary>
        private static void Dress(GameObject go, ZNetScene scene)
        {
            foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true)) r.enabled = false;
            Material paper = Paper.For(Kind.Kill, scene);
            if (paper == null) return;
            var sheet = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.DestroyImmediate(sheet.GetComponent<Collider>());
            sheet.name = "wanted_sheet";
            sheet.transform.SetParent(go.transform, false);
            sheet.transform.localScale = new Vector3(Paper.Width, 0.004f, Paper.Height);
            sheet.GetComponent<MeshRenderer>().sharedMaterial = paper;
        }

        // ---- what the bag shows ---------------------------------------------------------------------------------------

        [HarmonyPatch(typeof(ItemDrop.ItemData), nameof(ItemDrop.ItemData.GetTooltip),
            new[] { typeof(ItemDrop.ItemData), typeof(int), typeof(bool), typeof(float), typeof(int), typeof(bool) })]
        private static class ItemData_GetTooltip
        {
            private static void Postfix(ItemDrop.ItemData item, ref string __result)
            {
                try
                {
                    Contract c = ContractOf(item);
                    if (c == null) return;
                    __result = Contracts.Describe(c) + "\n" + Status(item, c) + "\n\n" + __result;
                }
                catch (Exception e) { Plugin.Log.LogWarning("Notice tooltip: " + e.Message); }
            }
        }

        [HarmonyPatch(typeof(ItemDrop.ItemData), nameof(ItemDrop.ItemData.GetIcon))]
        private static class ItemData_GetIcon
        {
            private static void Postfix(ItemDrop.ItemData __instance, ref Sprite __result)
            {
                if (!Is(__instance)) return;
                Sprite[] icons = __instance.m_shared.m_icons;
                Contract c = ContractOf(__instance);
                if (c == null || icons == null || icons.Length < Kinds * 2) return;
                bool done = IsDone(__instance);
                Sprite sketch = Plugin.Illustrated.Value ? Sketch.IconFor(c, ZNetScene.instance, Plugin.NoticeHeading.Value, done) : null;
                __result = sketch ?? icons[(int)c.Kind + (done ? Kinds : 0)] ?? __result;
            }
        }

        /// <summary>How far along it is: kills counted, or how many of the things it asks for are in the bag.</summary>
        private static string Status(ItemDrop.ItemData item, Contract c)
        {
            if (c.Kind == Kind.Kill)
            {
                int n = Kills.Progress(item);
                return n >= c.Count ? "<color=#99e68c>" + T("wanted_status_done") + "</color>" : T("wanted_status_kills", n, c.Count);
            }
            // counted where the paper is: the player's bag, or the chest open beside it (the box it's about to be handed in at)
            Player p = Player.m_localPlayer;
            Inventory bag = p != null ? p.GetInventory() : null;
            Inventory chest = InventoryGui.instance != null ? OpenChest(InventoryGui.instance)?.GetInventory() : null;
            string text;
            if (bag != null && bag.ContainsItem(item)) text = T("wanted_status_have", HandIn.CountOf(bag, c.Target), c.Count);
            else if (chest != null && chest.ContainsItem(item)) text = T("wanted_status_box", HandIn.CountOf(chest, c.Target), c.Count);
            else return T("wanted_status_needs", c.Count);
            int have = HandIn.CountOf(bag != null && bag.ContainsItem(item) ? bag : chest, c.Target);
            return have >= c.Count ? "<color=#99e68c>" + text + "</color>" : text;
        }

        private static readonly AccessTools.FieldRef<InventoryGui, Container> OpenChest =
            AccessTools.FieldRefAccess<InventoryGui, Container>("m_currentContainer");

        // ---- when the game registers its own ---------------------------------------------------------------------------
        // ZNetScene.Awake can run before ObjectDB.Awake, and the main menu has an ObjectDB copy of its own: register from all three.

        [HarmonyPatch(typeof(ZNetScene), "Awake")]
        private static class ZNetScene_Awake
        {
            private static void Postfix(ZNetScene __instance) => Guarded(() => Register(__instance, ObjectDB.instance));
        }

        [HarmonyPatch(typeof(ObjectDB), "Awake")]
        private static class ObjectDB_Awake
        {
            private static void Postfix(ObjectDB __instance) => Guarded(() => Register(ZNetScene.instance, __instance));
        }

        [HarmonyPatch(typeof(ObjectDB), nameof(ObjectDB.CopyOtherDB))]
        private static class ObjectDB_CopyOtherDB
        {
            private static void Postfix(ObjectDB __instance) => Guarded(() => Register(ZNetScene.instance, __instance));
        }

        private static void Guarded(Action register)
        {
            try { register(); }
            catch (Exception e) { Plugin.Log.LogError("Registering the notice failed: " + e); }
        }
    }
}
