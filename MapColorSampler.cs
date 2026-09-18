using System.Diagnostics;
using MelonLoader;
using UnityEngine;
using Il2CppScheduleOne.Map;
using Il2CppScheduleOne.UI.Phone.Map;

namespace ScheduleOneNavigator
{
    // Reads the game's own phone-map artwork (MapApp.MainMapSprite) as a
    // pixel color lookup, so TerrainGrid can ask "what does the map itself
    // say is here" (e.g. water = blue) instead of relying only on physics
    // trigger geometry. See TerrainGrid's water-check comment for why this
    // exists - the WaterCollider trigger volume is suspected to extend past
    // the visible water surface near at least one island, wrongly marking
    // real land as unwalkable.
    //
    // Coordinate space: GetMapPosition(world) is already confirmed (see
    // FullMapView's EnsureMapSprite/UpdatePlayerDot - the sprite Image and
    // every marker share this same space with no extra flip) to be a
    // uniform-scale projection centered at (0,0), MapDimensions units wide,
    // with +Y matching Unity UI's +Y-up. Texture2D.GetPixels32() is also
    // row-major from the bottom-left (+Y-up), so the two already agree - no
    // vertical flip needed here, unlike some of this project's earlier
    // click-calibration bugs elsewhere.
    static class MapColorSampler
    {
        static Color32[] mapPixels;
        static int texWidth, texHeight;
        static Rect spriteRect;
        static Sprite cachedSprite;
        static bool loggedNotReady;

        public static bool IsReady => mapPixels != null;

        static void EnsureLoaded()
        {
            MapApp mapApp = MapApp.Instance;
            Sprite sprite = mapApp != null ? mapApp.MainMapSprite : null;
            if (sprite == null)
            {
                if (!loggedNotReady)
                {
                    loggedNotReady = true;
                    MelonLogger.Warning("[Minimap] MapColorSampler: MapApp/MainMapSprite not ready yet - falling back to the trigger-based water check until it is.");
                }
                return;
            }

            if (mapPixels != null && sprite == cachedSprite)
                return;

            var sw = Stopwatch.StartNew();
            Texture2D tex = sprite.texture;

            // Texture2D.GetPixels32() would throw if the source asset isn't
            // marked readable at import time - not something we can check
            // from decompiled C# (it's an asset import flag, not a field).
            // Blitting through a RenderTexture and reading back via
            // ReadPixels sidesteps that entirely: it reads the GPU
            // framebuffer, not the source asset's CPU-side copy.
            RenderTexture rt = RenderTexture.GetTemporary(tex.width, tex.height, 0, RenderTextureFormat.ARGB32);
            Graphics.Blit(tex, rt);
            RenderTexture prev = RenderTexture.active;
            RenderTexture.active = rt;
            Texture2D readable = new Texture2D(tex.width, tex.height, TextureFormat.RGBA32, false);
            readable.ReadPixels(new Rect(0, 0, tex.width, tex.height), 0, 0);
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);

            mapPixels = readable.GetPixels32();
            texWidth = tex.width;
            texHeight = tex.height;
            spriteRect = sprite.rect;
            cachedSprite = sprite;
            Object.Destroy(readable); // only the managed Color32[] needs to survive

            MelonLogger.Msg($"[Minimap] MapColorSampler: cached {texWidth}x{texHeight} map texture " +
                $"({mapPixels.Length * 4 / 1024 / 1024}MB), {sw.ElapsedMilliseconds}ms.");
        }

        // World position -> the map-art pixel color at that spot, or false
        // if the map texture isn't loaded yet (caller should fall back to
        // another signal rather than treat "unknown" as "not water").
        public static bool TryGetColorAt(Vector3 worldPos, out Color32 color)
        {
            EnsureLoaded();
            color = default;
            if (!IsReady)
                return false;

            MapPositionUtility posUtil = MapPositionUtility.Instance;
            if (posUtil == null)
                return false;

            Vector2 mapPos = posUtil.GetMapPosition(worldPos);
            float dims = posUtil.MapDimensions;
            if (dims <= 0f)
                return false;
            float half = dims / 2f;
            float u = (mapPos.x + half) / dims;
            float v = (mapPos.y + half) / dims;

            int px = Mathf.Clamp(Mathf.FloorToInt(spriteRect.x + u * spriteRect.width), 0, texWidth - 1);
            int py = Mathf.Clamp(Mathf.FloorToInt(spriteRect.y + v * spriteRect.height), 0, texHeight - 1);
            color = mapPixels[py * texWidth + px];
            return true;
        }
    }
}
