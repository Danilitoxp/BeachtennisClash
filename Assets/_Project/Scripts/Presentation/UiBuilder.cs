using UnityEngine;
using UnityEngine.UI;

namespace BeachTennis.Presentation
{
    /// <summary>Small helpers for the placeholder uGUI screens (replaced by UI Toolkit menus in Phase 6).</summary>
    public static class UiBuilder
    {
        public const float RefHeight = 1080f;

        public static RectTransform Canvas(string name, int sortingOrder)
        {
            var go = new GameObject(name);
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, RefHeight);
            scaler.matchWidthOrHeight = 1f;
            return go.GetComponent<RectTransform>();
        }

        /// <summary>Auto-sized dark panel anchored at <paramref name="anchor"/> (0..1), with a padded text inside.</summary>
        public static Text Panel(RectTransform parent, Vector2 anchor, Vector2 offset, int fontSize, TextAnchor align, out RectTransform panel, float alpha = 0.65f)
        {
            var go = new GameObject("Panel", typeof(RectTransform), typeof(Image), typeof(ContentSizeFitter), typeof(VerticalLayoutGroup));
            panel = go.GetComponent<RectTransform>();
            panel.SetParent(parent, false);
            panel.anchorMin = panel.anchorMax = panel.pivot = anchor;
            panel.anchoredPosition = offset;
            var img = go.GetComponent<Image>();
            img.color = new Color(0f, 0f, 0f, alpha);
            img.raycastTarget = false;
            var fitter = go.GetComponent<ContentSizeFitter>();
            fitter.horizontalFit = fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var layout = go.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(18, 18, 12, 12);
            layout.childAlignment = align;
            Text t = Text(panel, fontSize, align);
            UiFonts.PrewarmBuffers(t);
            return t;
        }

        public static Text Text(RectTransform parent, int fontSize, TextAnchor align)
        {
            var go = new GameObject("Text", typeof(RectTransform), typeof(Text));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            var t = go.GetComponent<Text>();
            t.fontSize = fontSize;
            t.font = UiFonts.Get(fontSize);
            t.alignment = align;
            t.color = Color.white;
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            return t;
        }

        /// <summary>A fixed-size button-looking panel with a centered label.</summary>
        public static RectTransform Button(RectTransform parent, string label, Vector2 anchoredPos, Vector2 size, int fontSize)
        {
            var go = new GameObject(label, typeof(RectTransform), typeof(Image));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = size;
            var img = go.GetComponent<Image>();
            img.color = new Color(1f, 1f, 1f, 0.16f);
            img.raycastTarget = false;
            Text t = Text(rt, fontSize, TextAnchor.MiddleCenter);
            t.text = label;
            return rt;
        }
    }
}
