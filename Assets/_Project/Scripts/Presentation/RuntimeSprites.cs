using UnityEngine;

namespace BeachTennis.Presentation
{
    /// <summary>
    /// Procedural placeholder sprites (soft disc for shadows, ring for impact marks), generated once
    /// and cached. Replaced by authored textures in the visual production phase.
    /// </summary>
    public static class RuntimeSprites
    {
        private static Sprite _softDisc;
        private static Sprite _ring;
        private static Material _spriteMaterial;

        public static Sprite SoftDisc => _softDisc != null ? _softDisc : (_softDisc = Create(64, SoftDiscAlpha));
        public static Sprite Ring => _ring != null ? _ring : (_ring = Create(64, RingAlpha));

        /// <summary>Unlit, alpha-blended material that works for SpriteRenderer and LineRenderer under URP.</summary>
        public static Material SpriteMaterial =>
            _spriteMaterial != null ? _spriteMaterial : (_spriteMaterial = new Material(Shader.Find("Sprites/Default")));

        private static float SoftDiscAlpha(float r)
        {
            float t = Mathf.Clamp01(1f - r);
            return t * t * (3f - 2f * t);
        }

        private static float RingAlpha(float r)
        {
            float d = Mathf.Abs(r - 0.8f) / 0.15f;
            return Mathf.Clamp01(1f - d);
        }

        private static Sprite Create(int size, System.Func<float, float> alpha)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                name = "RuntimeSprite"
            };
            var pixels = new Color32[size * size];
            float half = (size - 1) * 0.5f;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = (x - half) / half, dy = (y - half) / half;
                float r = Mathf.Sqrt(dx * dx + dy * dy);
                byte a = (byte)(alpha(r) * 255f);
                pixels[y * size + x] = new Color32(255, 255, 255, a);
            }
            tex.SetPixels32(pixels);
            tex.Apply(false, true);
            // pixelsPerUnit = size → the sprite is 1 m wide; scale the transform to the wanted diameter.
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
        }
    }
}
