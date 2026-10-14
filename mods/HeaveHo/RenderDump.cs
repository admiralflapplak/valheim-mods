using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

namespace HeaveHo
{
    /// <summary>
    /// Debug: writes everything relevant to "why is the oar see-through" to BepInEx/HeaveHo_render_dump.txt —
    /// the camera's rendering path and command buffers, and for the nearest ship's hull, water mask and oars:
    /// shader passes + LightMode tags, render state properties (ZWrite/ZTest/Stencil/Blend…), keywords, queues.
    /// </summary>
    internal static class RenderDump
    {
        private static readonly string[] PassTags = { "LightMode", "RenderType", "Queue" };

        internal static void Write(string pluginDir)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Heave Ho render dump — {DateTime.Now}");
            sb.AppendLine($"Unity {Application.unityVersion}, graphics {SystemInfo.graphicsDeviceType}");
            sb.AppendLine();

            DumpCameras(sb);

            RowingShip ship = FindNearestShip();
            if (ship == null)
            {
                sb.AppendLine("No ship found near the player.");
            }
            else
            {
                sb.AppendLine($"===== SHIP '{Utils.GetPrefabName(ship.gameObject)}' layer={LayerMask.LayerToName(ship.gameObject.layer)} =====");
                foreach (Renderer r in ship.GetComponentsInChildren<Renderer>(true))
                {
                    string n = r.name.ToLowerInvariant();
                    bool interesting = n.Contains("hull") || n.Contains("watermask") || n.StartsWith("heaveho_");
                    if (!interesting) continue;
                    DumpRenderer(sb, r, ship.transform);
                }
            }

            string path = Path.Combine(Paths(pluginDir), "HeaveHo_render_dump.txt");
            File.WriteAllText(path, sb.ToString());
            Plugin.Log.LogInfo("Render dump written to " + path);
            Player.m_localPlayer?.Message(MessageHud.MessageType.TopLeft, "Heave Ho: render dump saved");
        }

        // BepInEx root (two levels up from plugins/HeaveHo), falling back to the plugin folder.
        private static string Paths(string pluginDir)
        {
            DirectoryInfo d = Directory.GetParent(pluginDir)?.Parent;
            return d != null && d.Exists ? d.FullName : pluginDir;
        }

        private static RowingShip FindNearestShip()
        {
            if (RowingShip.LocalRowingShip != null) return RowingShip.LocalRowingShip;
            Player p = Player.m_localPlayer;
            if (p == null) return null;
            return UnityEngine.Object.FindObjectsByType<RowingShip>(FindObjectsSortMode.None)
                .OrderBy(s => Vector3.Distance(s.transform.position, p.transform.position))
                .FirstOrDefault();
        }

        private static void DumpCameras(StringBuilder sb)
        {
            foreach (Camera cam in Camera.allCameras)
            {
                sb.AppendLine($"===== CAMERA '{cam.name}' enabled={cam.enabled} depth={cam.depth} =====");
                sb.AppendLine($"  renderingPath={cam.renderingPath} actual={cam.actualRenderingPath} " +
                              $"depthTextureMode={cam.depthTextureMode} clearFlags={cam.clearFlags} " +
                              $"near={cam.nearClipPlane} far={cam.farClipPlane} hdr={cam.allowHDR} msaa={cam.allowMSAA}");
                sb.AppendLine($"  cullingMask: {MaskNames(cam.cullingMask)}");

                foreach (CameraEvent ev in Enum.GetValues(typeof(CameraEvent)))
                {
                    CommandBuffer[] bufs = cam.GetCommandBuffers(ev);
                    foreach (CommandBuffer b in bufs)
                        sb.AppendLine($"  cmdbuf @{ev}: '{b.name}' ({b.sizeInBytes} bytes)");
                }

                foreach (MonoBehaviour mb in cam.GetComponents<MonoBehaviour>())
                    if (mb != null) sb.AppendLine($"  component: {mb.GetType().FullName} enabled={mb.enabled}");
                sb.AppendLine();
            }
        }

        private static void DumpRenderer(StringBuilder sb, Renderer r, Transform ship)
        {
            Vector3 c = ship.InverseTransformPoint(r.bounds.center);
            sb.AppendLine($"--- RENDERER '{HierPath(r.transform, ship)}' ({r.GetType().Name}) enabled={r.enabled} " +
                          $"active={r.gameObject.activeInHierarchy} layer={LayerMask.LayerToName(r.gameObject.layer)}");
            sb.AppendLine($"    bounds center(ship space)=({c.x:F2},{c.y:F2},{c.z:F2}) size={r.bounds.size} " +
                          $"shadows={r.shadowCastingMode} sortingOrder={r.sortingOrder}");

            foreach (Material m in r.sharedMaterials)
            {
                if (m == null) { sb.AppendLine("    material: null"); continue; }
                DumpMaterial(sb, m);
            }
            sb.AppendLine();
        }

        private static void DumpMaterial(StringBuilder sb, Material m)
        {
            Shader sh = m.shader;
            sb.AppendLine($"    MATERIAL '{m.name}' shader='{sh?.name}' queue={m.renderQueue} " +
                          $"(shader default {sh?.renderQueue}) passes={m.passCount}");
            sb.AppendLine($"      keywords: {string.Join(" ", m.shaderKeywords)}");

            for (int i = 0; i < m.passCount; i++)
            {
                string tags = "";
                if (sh != null)
                    foreach (string t in PassTags)
                    {
                        ShaderTagId v = sh.FindPassTagValue(i, new ShaderTagId(t));
                        if (!string.IsNullOrEmpty(v.name)) tags += $" {t}={v.name}";
                    }
                sb.AppendLine($"      pass {i}: '{m.GetPassName(i)}' enabled={m.GetShaderPassEnabled(m.GetPassName(i))}{tags}");
            }

            sb.AppendLine($"      tags: RenderType={m.GetTag("RenderType", true)} Queue={m.GetTag("Queue", true)}");

            if (sh == null) return;
            int count = sh.GetPropertyCount();
            for (int i = 0; i < count; i++)
            {
                string name = sh.GetPropertyName(i);
                ShaderPropertyType type = sh.GetPropertyType(i);
                string value;
                switch (type)
                {
                    case ShaderPropertyType.Float:
                    case ShaderPropertyType.Range:
                        value = m.GetFloat(name).ToString("0.###"); break;
                    case ShaderPropertyType.Int:
                        value = m.GetInteger(name).ToString(); break;
                    case ShaderPropertyType.Color:
                        value = m.GetColor(name).ToString(); break;
                    case ShaderPropertyType.Vector:
                        value = m.GetVector(name).ToString(); break;
                    case ShaderPropertyType.Texture:
                        Texture t = m.GetTexture(name);
                        value = t != null ? $"'{t.name}' {t.width}x{t.height}" : "none"; break;
                    default:
                        value = "?"; break;
                }
                sb.AppendLine($"      {name} ({type}) = {value}");
            }
        }

        private static string MaskNames(int mask)
        {
            var names = Enumerable.Range(0, 32)
                .Where(i => (mask & (1 << i)) != 0)
                .Select(i => { string n = LayerMask.LayerToName(i); return string.IsNullOrEmpty(n) ? i.ToString() : n; });
            return string.Join(", ", names);
        }

        private static string HierPath(Transform t, Transform root)
        {
            string p = t.name;
            while (t.parent != null && t != root) { t = t.parent; p = t.name + "/" + p; }
            return p;
        }
    }
}
