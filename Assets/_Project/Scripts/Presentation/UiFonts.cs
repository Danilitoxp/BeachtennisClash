using UnityEngine;

namespace BeachTennis.Presentation
{
    /// <summary>
    /// Shared runtime font for placeholder UI. Dynamic fonts rebuild their glyph texture every time a
    /// new character appears, which allocates (~60 KB) and stalls a frame; measured on device during
    /// the first seconds of play. <see cref="Get"/> pre-renders the whole character set once at the
    /// sizes in use, so text updates during gameplay never trigger a rebuild.
    /// </summary>
    public static class UiFonts
    {
        private const string Charset =
            " !\"#$%&'()*+,-./0123456789:;<=>?@ABCDEFGHIJKLMNOPQRSTUVWXYZ[\\]^_`abcdefghijklmnopqrstuvwxyz{|}~" +
            "ÁÂÃÀÇÉÊÍÓÔÕÚÜáâãàçéêíóôõúü°·—–→←↑↓×±";

        private static Font _font;

        public static Font Get(int fontSize)
        {
            if (_font == null) _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _font.RequestCharactersInTexture(Charset, fontSize, FontStyle.Normal);
            return _font;
        }

        /// <summary>
        /// uGUI grows a Text's vertex buffers (≈300 B per character) the first time the text gets longer
        /// than ever before: a ~60 KB allocation and a stalled frame in the middle of play. Rendering a
        /// long string once at creation sizes the buffers up front. Call after the Text is in an active canvas.
        /// </summary>
        public static void PrewarmBuffers(UnityEngine.UI.Text text, int characters = 600)
        {
            string original = text.text;
            var sb = new System.Text.StringBuilder(characters + characters / 40);
            for (int i = 0; i < characters; i++)
            {
                sb.Append('W');
                if (i % 40 == 39) sb.Append('\n');
            }
            text.text = sb.ToString();
            Canvas.ForceUpdateCanvases();
            text.text = original;
        }
    }
}
