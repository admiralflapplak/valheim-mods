using System.Collections.Generic;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace WeaponOils
{
    /// <summary>
    /// A small drop in the oil's colour beside the level number of an oiled weapon in any inventory grid (the bag, a chest), and in the
    /// corner of its hotbar slot, so an oiled weapon shows at a glance. It is a child of the slot's level text, set just left of the number each time the grid is drawn, and
    /// hidden when the slot holds no oiled weapon (an oil that has worn off is cleared by OilState.Active).
    /// </summary>
    internal static class OilMark
    {
        private const string MarkName = "nick_oil_mark";
        private static readonly Dictionary<string, Sprite> Sprites = new Dictionary<string, Sprite>();   // by oil id
        private static readonly List<GameObject> Made = new List<GameObject>();

        private static readonly AccessTools.FieldRef<InventoryGrid, List<InventoryElement>> Elements =
            AccessTools.FieldRefAccess<InventoryGrid, List<InventoryElement>>("m_elements");
        private static readonly AccessTools.FieldRef<InventoryGrid, Inventory> GridInventory =
            AccessTools.FieldRefAccess<InventoryGrid, Inventory>("m_inventory");

        [HarmonyPatch(typeof(InventoryGrid), "UpdateGui")]
        private static class InventoryGrid_UpdateGui
        {
            private static void Postfix(InventoryGrid __instance)
            {
                Inventory inventory = GridInventory(__instance);
                List<InventoryElement> elements = Elements(__instance);
                if (inventory == null || elements == null) return;
                foreach (InventoryElement e in elements)
                {
                    if (e == null || e.m_quality == null) continue;
                    ItemDrop.ItemData item = e.m_used ? inventory.GetItemAt(e.Position.x, e.Position.y) : null;
                    OilDef oil = item != null && OilState.HasOil(item) ? OilState.Active(item, out _) : null;
                    Show(e.m_quality, oil);
                }
            }
        }

        // ---- the hotbar: its slots have no level number, so the drop sits in the slot's top right corner ----------------

        private static readonly AccessTools.FieldRef<HotkeyBar, System.Collections.IList> BarElements =
            AccessTools.FieldRefAccess<HotkeyBar, System.Collections.IList>("m_elements");
        private static readonly AccessTools.FieldRef<HotkeyBar, List<ItemDrop.ItemData>> BarItems =
            AccessTools.FieldRefAccess<HotkeyBar, List<ItemDrop.ItemData>>("m_items");
        private static System.Reflection.FieldInfo _elementGo;   // HotkeyBar.ElementData.m_go (a private class)

        [HarmonyPatch(typeof(HotkeyBar), "UpdateIcons")]
        private static class HotkeyBar_UpdateIcons
        {
            private static void Postfix(HotkeyBar __instance)
            {
                System.Collections.IList elements = BarElements(__instance);
                List<ItemDrop.ItemData> items = BarItems(__instance);
                if (elements == null || items == null) return;
                for (int i = 0; i < elements.Count; i++)
                {
                    object element = elements[i];
                    if (element == null) continue;
                    if (_elementGo == null) _elementGo = AccessTools.Field(element.GetType(), "m_go");
                    var slot = _elementGo?.GetValue(element) as GameObject;
                    if (slot == null) continue;
                    ItemDrop.ItemData item = items.Find(it => it.m_gridPos.x == i);   // (the game puts the item of bag column i in slot i)
                    OilDef oil = item != null && OilState.HasOil(item) ? OilState.Active(item, out _) : null;
                    ShowInCorner(slot.transform, oil);
                }
            }
        }

        private static void ShowInCorner(Transform slot, OilDef oil)
        {
            Transform t = slot.Find(MarkName);
            if (oil == null)
            {
                if (t != null && t.gameObject.activeSelf) t.gameObject.SetActive(false);
                return;
            }
            Image mark = t != null ? t.GetComponent<Image>() : Make(slot);
            if (t == null)
            {
                var rt = (RectTransform)mark.transform;
                rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(1f, 1f);
                rt.anchoredPosition = new Vector2(-4f, -4f);
                rt.sizeDelta = new Vector2(12f, 16f);
            }
            mark.gameObject.SetActive(true);
            mark.sprite = SpriteOf(oil);
        }

        private static void Show(TMP_Text level, OilDef oil)
        {
            Transform t = level.transform.Find(MarkName);
            if (oil == null)
            {
                if (t != null && t.gameObject.activeSelf) t.gameObject.SetActive(false);
                return;
            }
            Image mark = t != null ? t.GetComponent<Image>() : Make(level.transform);
            mark.gameObject.SetActive(true);
            mark.sprite = SpriteOf(oil);

            // just left of the number as drawn (the text's box is wider than its digits); without a number, at the box's right edge
            var rt = (RectTransform)mark.transform;
            float size = Mathf.Max(10f, level.fontSize * 0.95f);
            rt.sizeDelta = new Vector2(size * 0.75f, size);
            Bounds drawn = level.textBounds;
            Rect box = ((RectTransform)level.transform).rect;
            bool hasNumber = level.enabled && drawn.size.x > 0.01f;
            float left = hasNumber ? drawn.min.x : box.xMax;
            float middle = hasNumber ? drawn.center.y : box.center.y;
            rt.localPosition = new Vector3(left - size * 0.45f, middle, 0f);
        }

        private static Image Make(Transform level)
        {
            var go = new GameObject(MarkName, typeof(RectTransform), typeof(Image));
            go.layer = level.gameObject.layer;
            var rt = (RectTransform)go.transform;
            rt.SetParent(level, false);
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            var image = go.GetComponent<Image>();
            image.raycastTarget = false;   // the slot under it still takes the clicks
            image.preserveAspect = true;
            Made.Add(go);
            return image;
        }

        /// <summary>A drop of the oil's colour with a dark outline and a glint, drawn once per oil (smoothed, so it stays clean when small).</summary>
        private static Sprite SpriteOf(OilDef oil)
        {
            if (Sprites.TryGetValue(oil.Id, out Sprite cached) && cached != null) return cached;
            Color colour = OilItems.ColourOf(oil);
            Color outline = new Color(0.08f, 0.06f, 0.04f, 1f);
            const int W = 24, H = 32, Sub = 4;
            var px = new Color[W * H];
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    float fill = 0f, edge = 0f, glint = 0f;
                    for (int sy = 0; sy < Sub; sy++)
                        for (int sx = 0; sx < Sub; sx++)
                        {
                            float u = x + (sx + 0.5f) / Sub, v = y + (sy + 0.5f) / Sub;
                            float d = Drop(u, v);
                            if (d <= 0f) fill++;
                            else if (d <= 1.8f) edge++;
                            if (d <= 0f && (u - 9f) * (u - 9f) + (v - 12f) * (v - 12f) < 5f) glint++;
                        }
                    const float n = Sub * Sub;
                    float shade = Mathf.Lerp(0.75f, 1.1f, y / (float)H);   // darker at the bottom, as a lit drop
                    Color c = Color.Lerp(colour * shade, Color.white, glint / n * 0.8f);
                    float cover = (fill + edge) / n;
                    Color col = cover > 0f ? Color.Lerp(outline, c, fill / (fill + edge)) : Color.clear;
                    col.a = cover;
                    px[y * W + x] = col;
                }
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false) { name = "nick_oil_mark_" + oil.Id, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            tex.SetPixels(px);
            tex.Apply(false, true);
            Sprite sprite = Sprite.Create(tex, new Rect(0, 0, W, H), new Vector2(0.5f, 0.5f), 100f);
            sprite.name = tex.name;
            Sprites[oil.Id] = sprite;
            return sprite;
        }

        /// <summary>How far a point is outside the drop's shape (negative inside): a round bottom drawn up to a point at the top.</summary>
        private static float Drop(float x, float y)
        {
            const float cx = 12f, cy = 11f, r = 8.5f, tip = 30f;
            float dx = Mathf.Abs(x - cx);
            if (y <= cy) return Mathf.Sqrt(dx * dx + (y - cy) * (y - cy)) - r;
            float t = (y - cy) / (tip - cy);
            if (t >= 1f) return y - tip + dx;
            float half = r * Mathf.Pow(1f - t, 1.3f);
            return dx - half;
        }

        internal static void Forget()
        {
            foreach (GameObject go in Made) if (go != null) Object.Destroy(go);
            Made.Clear();
            Sprites.Clear();   // (the textures go with the game's next unload of unused assets)
        }
    }
}
