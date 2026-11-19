using System.Collections.Generic;
using FishingVR.Fish;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using FishingVR.UI;

namespace FishingVR.Bestiary
{
    /// <summary>
    /// The bestiary list (built at start in a world-space UI canvas on this object): every species of the
    /// catalogue, two lines each: the name ("???" until caught), then rarity (in colour) · count · record.
    /// <see cref="width"/> x <see cref="height"/> metres on the local XY plane, readable from -Z.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public class BestiaryPanel : MonoBehaviour
    {
        const float PixelsPerMetre = 1000f; // canvas units: 1 unit = 1 mm

        [Tooltip("Species listed (sorted by rarity). Empty = the species of the scene's FishBiteAI.")]
        [SerializeField] List<FishSpecies> catalog = new List<FishSpecies>();
        [SerializeField] float width = 0.3f;
        [SerializeField] float height = 0.26f;
        [SerializeField] TMP_FontAsset font;
        [SerializeField] Color background = new Color(0.07f, 0.1f, 0.14f, 0.95f);
        [SerializeField] Color textColor = new Color(0.95f, 0.97f, 1f);
        [SerializeField] Color unknownColor = new Color(0.6f, 0.65f, 0.7f);

        TextMeshProUGUI title;
        CanvasSnapshot snapshot;

        /// <summary>The image the panel is shown through (both eyes in VR).</summary>
        public CanvasSnapshot Snapshot => snapshot;
        readonly List<(FishSpecies species, TextMeshProUGUI name, TextMeshProUGUI info)> rows = new();

        public List<FishSpecies> Catalog => catalog;

        void Start()
        {
            if (catalog.Count == 0)
            {
                var ai = FindAnyObjectByType<FishBiteAI>();
                if (ai != null) catalog.AddRange(ai.Species);
            }
            catalog.RemoveAll(s => s == null);
            catalog.Sort((a, b) => a.rarity != b.rarity ? a.rarity.CompareTo(b.rarity) : string.CompareOrdinal(a.displayName, b.displayName));
            Build();
            Refresh();
            FishBestiary.Changed += Refresh;
        }

        void OnDestroy() => FishBestiary.Changed -= Refresh;

        void Build()
        {
            var canvas = GetComponent<Canvas>();
            if (canvas == null) canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            var rt = (RectTransform)transform;
            var w = width * PixelsPerMetre;
            var h = height * PixelsPerMetre;
            rt.sizeDelta = new Vector2(w, h);
            rt.localScale = Vector3.one / PixelsPerMetre;

            var bg = gameObject.GetComponent<Image>();
            if (bg == null) bg = gameObject.AddComponent<Image>();
            bg.color = background;
            bg.raycastTarget = false;

            const float pad = 10f;
            const float titleHeight = 34f;
            title = MakeText("Title", new Rect(pad, 4f, w - 2f * pad, titleHeight), 26f, 16f, TextAlignmentOptions.Center);
            title.fontStyle = FontStyles.Bold;

            var count = Mathf.Max(1, catalog.Count);
            var rowHeight = (h - titleHeight - 2f * pad) / count;
            var nameHeight = rowHeight * 0.54f;
            for (var i = 0; i < catalog.Count; i++)
            {
                var y = titleHeight + pad + rowHeight * i;
                var name = MakeText($"Name{i}", new Rect(pad, y, w - 2f * pad, nameHeight), nameHeight, 12f, TextAlignmentOptions.BottomLeft);
                name.fontStyle = FontStyles.Bold;
                var info = MakeText($"Info{i}", new Rect(pad + 6f, y + nameHeight, w - 2f * pad - 6f, rowHeight - nameHeight), rowHeight - nameHeight, 10f, TextAlignmentOptions.TopLeft);
                rows.Add((catalog[i], name, info));
            }
        }

        /// <summary>Text in the rectangle (x, y from the top-left corner, in canvas units).</summary>
        TextMeshProUGUI MakeText(string name, Rect r, float size, float minSize, TextAlignmentOptions align)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = gameObject.layer;
            var rt = (RectTransform)go.transform;
            rt.SetParent(transform, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(r.x, -r.y);
            rt.sizeDelta = new Vector2(r.width, r.height);
            var tmp = go.AddComponent<TextMeshProUGUI>();
            if (font != null) tmp.font = font;
            tmp.alignment = align;
            tmp.color = textColor;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            tmp.raycastTarget = false;
            // Long names shrink to fit instead of being cut.
            tmp.enableAutoSizing = true;
            tmp.fontSizeMax = size;
            tmp.fontSizeMin = minSize;
            tmp.richText = true;
            return tmp;
        }

        public void Refresh()
        {
            if (title == null) return;
            var found = 0;
            foreach (var row in rows)
            {
                var entry = FishBestiary.Get(row.species);
                var known = entry != null && entry.caught > 0;
                if (known) found++;
                // The rarity is always shown (what is left to find), in its colour.
                var rarity = $"<color=#{ColorUtility.ToHtmlStringRGB(row.species.rarity.DisplayColor())}>{row.species.rarity.DisplayName()}</color>";
                if (known)
                {
                    row.name.text = row.species.displayName;
                    row.name.color = textColor;
                    row.info.text = $"{rarity}  ·  x{entry.caught}  ·  record {entry.heaviestKg:0.00} kg";
                    row.info.color = textColor;
                }
                else
                {
                    row.name.text = "???";
                    row.name.color = unknownColor;
                    row.info.text = $"{rarity}  ·  pas encore pêché";
                    row.info.color = unknownColor;
                }
            }
            title.text = $"BESTIAIRE   {found}/{rows.Count}";
            if (snapshot == null) snapshot = GetComponent<CanvasSnapshot>();
            if (snapshot == null) snapshot = gameObject.AddComponent<CanvasSnapshot>();
            snapshot.Capture();
        }
    }
}
