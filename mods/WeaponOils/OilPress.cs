using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;

namespace WeaponOils
{
    /// <summary>
    /// The oil press: a copy of the game's fermenter (its barrel, materials, sounds, switches and behaviour: oil bases go in, mature, and
    /// are tapped out as finished oil, as mead is) with a station name of its own, so the game never takes it for a fermenter, and without
    /// the fermenter's PlayerBase child (which would make every press a base for raids and comfort). Registered by name through
    /// PieceReload (safe to repeat; a hot reload replaces it without the game ever missing the prefab).
    /// </summary>
    internal static class OilPress
    {
        public const string PrefabName = "nick_oilpress";
        public const string PieceName = "$nick_oilpress";

        internal static GameObject Prefab;
        private static PieceTable _table;   // the build menu it was put on, kept so an unload can take it off again at the main menu
        private static GameObject _holder;  // inactive parent of the prefab: lives as long as the game does (an old copy's press stays valid)

        /// <summary>Registers (and builds, the first time) the press in the scene and the hammer's build menu. Safe to repeat.</summary>
        public static void Register(ZNetScene scene)
        {
            if (scene == null) return;
            if (Prefab == null) Prefab = Build(scene);
            if (Prefab == null) return;

            GameObject hammer = scene.GetPrefab("Hammer");
            PieceTable table = hammer != null ? hammer.GetComponent<ItemDrop>()?.m_itemData.m_shared.m_buildPieces : null;
            if (table != null) _table = table;
            PieceReload.ReplacePrefab(scene, Prefab, table != null ? table : _table);
            ApplyCost();
            ApplyDuration();
            AddConversions();
            Plugin.Synced.NeedsServer(Prefab);
        }

        /// <summary>Each oil base becomes its oil in the press (this mod's earlier entries for the same pair are replaced). Safe to repeat.</summary>
        public static void AddConversions()
        {
            if (Prefab == null) return;
            foreach (OilDef def in OilDefs.All)
                if (OilItems.Bases.TryGetValue(def.Id, out GameObject from) && OilItems.Oils.TryGetValue(def.Id, out GameObject to))
                    Content.Conversion(Prefab, from, to, makes: Plugin.OilsPerBatch.Value);
        }

        /// <summary>The cost and station from the settings, onto the piece (a mistake in either keeps the last good one).</summary>
        public static void ApplyCost()
        {
            Piece piece = Prefab != null ? Prefab.GetComponent<Piece>() : null;
            if (piece == null) return;
            Costs.Apply(piece, Plugin.PressCost.Value, Plugin.Log);
            Costs.ApplyStation(piece, Plugin.PressStation.Value, Plugin.Log);
        }

        /// <summary>How long an oil base matures in the press, from the setting (presses already standing keep theirs until their area reloads).</summary>
        public static void ApplyDuration()
        {
            Fermenter fermenter = Prefab != null ? Prefab.GetComponent<Fermenter>() : null;
            if (fermenter == null) return;
            fermenter.m_fermentationDuration = Plugin.PressMinutes.Value * 60f;
        }

        /// <summary>Takes the press off the build menu when this copy unloads. The prefab stays registered: no area loading meanwhile loses a press.</summary>
        public static void Unload() => PieceReload.UnloadPiece(Prefab, _table);

        public static void Forget()
        {
            Prefab = null;   // the old prefab and its holder stay alive for pieces already standing; the next copy replaces it by name
            _table = null;
            _holder = null;
        }

        // ---- the prefab ------------------------------------------------------------------------------------------

        private static GameObject Build(ZNetScene scene)
        {
            GameObject source = scene.GetPrefab("fermenter");
            if (source == null) { Plugin.Log.LogWarning("The game has no fermenter to copy: no oil press this session"); return null; }

            _holder = new GameObject("WeaponOils_prefabs");
            _holder.SetActive(false);   // nothing under it wakes up: they are prefabs, not objects
            Object.DontDestroyOnLoad(_holder);

            GameObject press = Object.Instantiate(source, _holder.transform);
            press.name = PrefabName;

            Piece piece = press.GetComponent<Piece>();
            piece.m_name = PieceName;
            piece.m_description = "$nick_oilpress_desc";

            Fermenter fermenter = press.GetComponent<Fermenter>();
            fermenter.m_name = PieceName;
            fermenter.m_conversion = new List<Fermenter.ItemConversion>();   // OilItems fills it: the fermenter's own mead entries are not ours

            Transform playerBase = press.transform.Find("PlayerBase");
            if (playerBase != null) Object.DestroyImmediate(playerBase.gameObject);

            try { AddLook(press, scene, fermenter); }
            catch (Exception e) { Plugin.Log.LogWarning("Oil press look: " + e.Message); }   // the fermenter's own barrel is better than no press

            try { piece.m_icon = Icon.Make(press, fallback: null); }
            catch (Exception e) { Plugin.Log.LogWarning("Oil press icon: " + e.Message); }
            if (piece.m_icon == null) piece.m_icon = source.GetComponent<Piece>().m_icon;   // the fermenter's own picture will do
            return press;
        }

        // ---- the look ---------------------------------------------------------------------------------------------
        // The whole press is the fermenter shrunk to the size of the game's storage barrel (0.9 m tall): its switches, effects and tap
        // point shrink with it, so it works as it did. Its own barrel is switched off (its colliders stay) and the storage barrel's model
        // stands in its place, with a screw-press frame and a spout of our own in the game's wood and iron. Everything we add goes under one
        // child scaled the other way up, so it is written in plain metres. Looks only: no colliders of ours.

        private const float Shrink = 0.41f;   // the fermenter is 2.2 m tall, the storage barrel 0.9 m

        private static void AddLook(GameObject press, ZNetScene scene, Fermenter fermenter)
        {
            Material wood = MaterialOf(scene, "Wood") ?? MaterialOf(scene, "FineWood");
            Material iron = MaterialOf(scene, "Iron") ?? MaterialOf(scene, "Bronze");

            press.transform.localScale = Vector3.one * Shrink;
            fermenter.m_hoverOffset *= Shrink;
            _round = 0;

            // the fermenter's barrel and lid are hidden, not removed: the barrel's colliders and the lid object the fermenter switches stay
            foreach (string path in new[] { "New/barrel", "_top/default" })
            {
                Transform t = press.transform.Find(path);
                MeshRenderer r = t != null ? t.GetComponent<MeshRenderer>() : null;
                if (r != null) r.enabled = false;
            }

            var visual = new GameObject("nick_visual").transform;
            visual.SetParent(press.transform, false);
            visual.localScale = Vector3.one / Shrink;   // below this, metres are real metres

            AddBarrel(visual, scene);
            Material staves = BarrelWood(scene) ?? wood;   // the frame is cut from the same wood as the barrel
            Transform ram = null;
            if (staves != null || iron != null) { ram = AddFrame(visual, staves, iron); AddSpout(visual, iron); }
            AddPlate(ram != null ? ram : visual, scene);
            press.AddComponent<PressAnimator>();   // lowers the ram while the oil matures

            // The fermenter's bubbles and smoke were made for a barrel 2.4 times the size, and by default a particle system ignores its
            // parents' scale (positions follow, sizes and offsets don't): make them follow the press down.
            foreach (ParticleSystem ps in press.GetComponentsInChildren<ParticleSystem>(true))
            {
                ParticleSystem.MainModule main = ps.main;
                main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            }
        }

        /// <summary>The storage barrel's staves (no lid), copied from its prefab (same mesh and material, so it is the game's own barrel).</summary>
        private static void AddBarrel(Transform visual, ZNetScene scene)
        {
            GameObject chest = scene.GetPrefab("piece_chest_barrel");
            Transform body = chest != null ? chest.transform.Find("New/blackmetalchest") : null;
            if (body == null) { Plugin.Log.LogWarning("The storage barrel has changed shape: the oil press keeps the fermenter's barrel"); return; }

            // The barrel's own lid is left off: the press plate (AddPlate) is the only lid, held above the open barrel.
            // the barrel prefab hangs its pieces under objects scaled 0.9 and turned half way round
            var barrel = new GameObject("barrel").transform;
            barrel.SetParent(visual, false);
            barrel.localScale = Vector3.one * 0.9f;
            barrel.localRotation = Quaternion.Euler(0f, 180f, 0f);
            Object.Instantiate(body.gameObject, barrel, false);
        }

        /// <summary>How far the ram sinks into the barrel (metres) when the oil is nearly done.</summary>
        internal const float RamTravel = 0.2f;

        /// <summary>
        /// The frame: two posts on footings with iron bands, a cross beam, and the ram (screw, handle, and the plate that
        /// AddPlate adds) that PressAnimator lowers. Hand-made on purpose: every post leans, thickens and stands a little differently, the
        /// same way every time (a fixed seed). Returns the ram.
        /// </summary>
        private static Transform AddFrame(Transform visual, Material wood, Material iron)
        {
            var frame = new GameObject("nick_frame").transform;
            frame.SetParent(visual, false);
            var rnd = new System.Random(11);
            float R(float from, float to) => from + (float)rnd.NextDouble() * (to - from);

            // The frame is low: the beam is just clear of the handle when the ram is pressed all the way down (the handle rests at 1.62 and
            // sinks RamTravel), and the screw is long enough to pass through it at both ends of the travel.
            const float postX = 0.46f, beamY = 1.2f;
            var tops = new Vector3[2];
            for (int i = 0; i < 2; i++)
            {
                float side = i == 0 ? -1f : 1f;
                float height = beamY + R(0.1f, 0.16f), radius = R(0.038f, 0.047f);
                var foot = new Vector3(side * postX + R(-0.012f, 0.012f), 0f, R(-0.02f, 0.02f));
                Quaternion lean = Quaternion.Euler(R(-2.5f, 2.5f), R(0f, 90f), R(-2.5f, 2.5f));
                Vector3 up = lean * Vector3.up;
                Between(frame, "post", foot, foot + up * height, radius, wood, wood: true);
                // a footing it stands on, an iron band round the post above it, and bands either side of where the beam rests
                Cylinder(frame, "footing", foot + Vector3.up * 0.025f, Quaternion.Euler(0f, R(0f, 90f), 0f), 0.05f, radius * R(2.2f, 2.6f), wood, wood: true);
                Cylinder(frame, "foot_band", foot + up * 0.1f, lean, 0.05f, radius * 1.4f, iron);
                Cylinder(frame, "joint_band_low", foot + up * (beamY - 0.1f), lean, 0.045f, radius * 1.4f, iron);
                Cylinder(frame, "joint_band_high", foot + up * (beamY + 0.085f), lean, 0.045f, radius * 1.4f, iron);
                tops[i] = foot + up * beamY;
            }
            // the beam runs from post to post, a little past each end, not quite level
            Vector3 left = tops[0] + new Vector3(-R(0.05f, 0.12f), R(-0.015f, 0.015f), 0f);
            Vector3 right = tops[1] + new Vector3(R(0.06f, 0.14f), R(-0.015f, 0.015f), 0f);
            Between(frame, "beam", left, right, 0.055f, wood, wood: true);

            // the ram: screw, handle and (added by AddPlate) the plate, one piece, so that it moves and turns together
            var ram = new GameObject("ram").transform;
            ram.SetParent(frame, false);
            Cylinder(ram, "screw", new Vector3(0f, 1.32f, 0f), Quaternion.identity, 0.72f, 0.03f, iron);   // from the plate up through the beam
            Cylinder(ram, "handle", new Vector3(0f, 1.62f, 0f), Quaternion.Euler(90f, 0f, 0f), 0.5f, 0.02f, iron);
            Cylinder(ram, "handle_cap_a", new Vector3(0f, 1.62f, 0.25f), Quaternion.Euler(90f, 0f, 0f), 0.04f, 0.03f, iron);
            Cylinder(ram, "handle_cap_b", new Vector3(0f, 1.62f, -0.25f), Quaternion.Euler(90f, 0f, 0f), 0.04f, 0.03f, iron);
            return ram;
        }

        /// <summary>A cylinder running from one point to another.</summary>
        private static void Between(Transform parent, string name, Vector3 from, Vector3 to, float radius, Material material, bool wood = false)
        {
            Vector3 along = to - from;
            Cylinder(parent, name, (from + to) * 0.5f, Quaternion.FromToRotation(Vector3.up, along), along.magnitude, radius, material, wood);
        }

        /// <summary>The round part of the press: another of the storage barrel's lids, held above the open barrel by the screw.</summary>
        private static void AddPlate(Transform parent, ZNetScene scene)
        {
            GameObject chest = scene.GetPrefab("piece_chest_barrel");
            Transform lid = chest != null ? chest.transform.Find("New/Closed/blackmetalchesttop_closed") : null;
            var plate = new GameObject("plate").transform;
            plate.SetParent(parent, false);
            plate.localPosition = new Vector3(0f, 0.93f, 0f);
            plate.localScale = Vector3.one * 0.9f;
            plate.localRotation = Quaternion.Euler(0f, 180f, 0f);
            if (lid != null) Object.Instantiate(lid.gameObject, plate, false).transform.localPosition = Vector3.zero;
        }

        /// <summary>
        /// A copy of the barrel's material showing only a patch of its staves: the barrel's texture is an atlas of staves and iron bands, so a
        /// bare cylinder would show a bit of everything. The patch is found, not guessed: a place on the barrel mesh whose pixels are all
        /// warm brown. Null when the texture or mesh can't be read (the caller falls back to the game's plain wood).
        /// </summary>
        private static Material BarrelWood(ZNetScene scene)
        {
            GameObject chest = scene.GetPrefab("piece_chest_barrel");
            Transform body = chest != null ? chest.transform.Find("New/blackmetalchest") : null;
            if (body == null) return null;
            Material source = body.GetComponent<MeshRenderer>()?.sharedMaterial;
            Mesh mesh = body.GetComponent<MeshFilter>()?.sharedMesh;
            Texture texture = source != null ? source.mainTexture : null;
            if (texture == null || mesh == null) return null;

            Texture2D pixels = null;
            RenderTexture target = RenderTexture.GetTemporary(texture.width, texture.height, 0, RenderTextureFormat.ARGB32);
            RenderTexture previous = RenderTexture.active;
            try
            {
                Graphics.Blit(texture, target);   // reads any texture, readable or not
                RenderTexture.active = target;
                pixels = new Texture2D(texture.width, texture.height, TextureFormat.RGBA32, false);
                pixels.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0);
                pixels.Apply();

                Vector2[] uv = mesh.uv;   // throws when the mesh isn't readable
                const float patch = 0.03f, half = patch / 2f;
                foreach (Vector2 c in uv)
                {
                    if (!Warm(pixels, c)) continue;
                    // the whole square, not only its middle, must be wood
                    if (!Warm(pixels, c + new Vector2(half, half)) || !Warm(pixels, c + new Vector2(-half, half))
                        || !Warm(pixels, c + new Vector2(half, -half)) || !Warm(pixels, c + new Vector2(-half, -half))) continue;
                    var wood = new Material(source) { name = "nick_oilpress_wood" };
                    wood.mainTextureScale = new Vector2(patch, patch);
                    wood.mainTextureOffset = new Vector2(c.x - half, c.y - half);
                    return wood;
                }
                Plugin.Log.LogInfo("No plain stave found in the storage barrel's texture: the press frame uses the game's wood");
                return null;
            }
            catch (Exception e)
            {
                Plugin.Log.LogInfo("Could not read the storage barrel's wood (" + e.Message + "): the press frame uses the game's wood");
                return null;
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(target);
                if (pixels != null) Object.Destroy(pixels);
            }
        }

        /// <summary>Warm brown: wood, as against the barrel's grey iron bands.</summary>
        private static bool Warm(Texture2D t, Vector2 uv)
        {
            Color c = t.GetPixelBilinear(uv.x, uv.y);
            return c.r > 0.3f && c.r - c.b > 0.15f && c.r >= c.g && c.g >= c.b;
        }

        /// <summary>A tap at the front, where the fermenter's tap switch and output point are: a collar, a pipe, a nozzle and a valve wheel.</summary>
        private static void AddSpout(Transform visual, Material iron)
        {
            var spout = new GameObject("nick_spout").transform;
            spout.SetParent(visual, false);

            Cylinder(spout, "collar", new Vector3(0f, 0.25f, 0.31f), Quaternion.Euler(90f, 0f, 0f), 0.04f, 0.055f, iron);
            Cylinder(spout, "pipe", new Vector3(0f, 0.25f, 0.41f), Quaternion.Euler(90f, 0f, 0f), 0.22f, 0.028f, iron);
            Cylinder(spout, "nozzle", new Vector3(0f, 0.2f, 0.50f), Quaternion.identity, 0.1f, 0.028f, iron);
            Cylinder(spout, "stem", new Vector3(0f, 0.29f, 0.37f), Quaternion.identity, 0.08f, 0.012f, iron);
            Cylinder(spout, "wheel", new Vector3(0f, 0.335f, 0.37f), Quaternion.identity, 0.015f, 0.065f, iron);
        }

        private static int _round;   // which rough round this is, for its seed: the press is built in the same order every time

        /// <summary>
        /// A round of the given length and radius, its middle at centre, turned by rotation: hand-cut (OilModels.RoughCylinder), a wooden
        /// one with few sides and plenty of wobble, an iron one truer and finer.
        /// </summary>
        private static void Cylinder(Transform parent, string name, Vector3 centre, Quaternion rotation, float length, float radius, Material material, bool wood = false)
        {
            var part = new GameObject(name);
            Mesh mesh = wood ? OilModels.RoughCylinder(100 + _round++, 8, 0.07f, 0.03f, length) : OilModels.RoughCylinder(100 + _round++, 12, 0.025f, 0f, length);
            part.AddComponent<MeshFilter>().sharedMesh = mesh;
            part.AddComponent<MeshRenderer>();
            part.transform.SetParent(parent, false);
            part.transform.localPosition = centre;
            part.transform.localRotation = rotation;
            part.transform.localScale = new Vector3(radius * 2f, length * 0.5f, radius * 2f);
            if (material != null) part.GetComponent<MeshRenderer>().sharedMaterial = material;
        }

        /// <summary>The material of a game item's model (the game's own look; Shader.Find would not have the shaders it strips).</summary>
        private static Material MaterialOf(ZNetScene scene, string itemPrefab)
        {
            GameObject item = scene.GetPrefab(itemPrefab);
            MeshRenderer r = item != null ? item.GetComponentInChildren<MeshRenderer>(true) : null;   // (the root can carry a see-through particle material)
            return r != null ? r.sharedMaterial : null;
        }

        // ---- what the fermenter's code does for us ---------------------------------------------------------------

        private static bool IsPress(Fermenter f) => f != null && f.m_name == PieceName;

        /// <summary>"Fermenting" becomes "Pressing": the game's own word is swapped for ours, in every language.</summary>
        [HarmonyPatch(typeof(Fermenter), nameof(Fermenter.GetHoverText))]
        private static class Fermenter_GetHoverText
        {
            private static void Postfix(Fermenter __instance, ref string __result)
            {
                if (!IsPress(__instance) || string.IsNullOrEmpty(__result)) return;
                string word = Localization.instance.Localize("$piece_fermenter_fermenting");
                if (!string.IsNullOrEmpty(word)) __result = __result.Replace(word, Nick.Shared.Translations.T("nick_oilpress_pressing"));
            }
        }

        // The fermenter's own effects (a splash when something is added, one when it is tapped, one where the oil drops) are made at full
        // size. While one of those three methods runs on a press, whatever EffectList.Create makes is shrunk with the press.
        private static bool _ours;

        [HarmonyPatch(typeof(Fermenter), "RPC_AddItem")]
        private static class Fermenter_RPC_AddItem
        {
            private static void Prefix(Fermenter __instance) => _ours = IsPress(__instance);
            private static void Finalizer() => _ours = false;
        }

        [HarmonyPatch(typeof(Fermenter), "RPC_Tap")]
        private static class Fermenter_RPC_Tap
        {
            private static void Prefix(Fermenter __instance) => _ours = IsPress(__instance);
            private static void Finalizer() => _ours = false;
        }

        [HarmonyPatch(typeof(Fermenter), "DelayedTap")]
        private static class Fermenter_DelayedTap
        {
            private static void Prefix(Fermenter __instance) => _ours = IsPress(__instance);
            private static void Finalizer() => _ours = false;
        }

        [HarmonyPatch(typeof(EffectList), nameof(EffectList.Create),
            typeof(Vector3), typeof(Quaternion), typeof(Transform), typeof(float), typeof(int), typeof(ZDOID))]
        private static class EffectList_Create
        {
            private static void Postfix(GameObject[] __result)
            {
                if (!_ours || __result == null) return;
                foreach (GameObject go in __result)
                {
                    if (go == null) continue;
                    go.transform.localScale *= Shrink;
                    foreach (ParticleSystem ps in go.GetComponentsInChildren<ParticleSystem>(true))
                    {
                        ParticleSystem.MainModule main = ps.main;
                        main.scalingMode = ParticleSystemScalingMode.Hierarchy;
                    }
                }
            }
        }

        // ---- when the game registers its own ---------------------------------------------------------------------
        // The host deletes a placed piece it doesn't know, so the press goes into ZNetScene as early as the scene exists.

        [HarmonyPatch(typeof(ZNetScene), "Awake")]
        private static class ZNetScene_Awake
        {
            private static void Postfix(ZNetScene __instance)
            {
                try { Register(__instance); }
                catch (Exception e) { Plugin.Log.LogError("Registering the oil press failed: " + e); }
            }
        }
    }
}
