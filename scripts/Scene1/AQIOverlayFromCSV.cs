using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class AQIOverlayFromJSON : MonoBehaviour
{
    [Serializable] public class Root { public string[] dates; public City[] cities; }
    [Serializable] public class City
    {
        public string id, name, country, region;
        public float lat, lon;
        public Sample[] values;
    }
    [Serializable] public class Sample { public float aqi, pm25, pm10, no2, so2, co, o3; }

    [Header("Inputs")]
    public TextAsset jsonFile;
    public Renderer pollutionRenderer;

    [Header("Optional UI")]
    public Slider dateSlider;     // set Whole Numbers = true
    public TMP_Text dateLabel;

    [Header("Overlay Texture")]
    public int texWidth = 2048;
    public int texHeight = 1024;
    public int brushRadiusPx = 10;
    public float alpha = 0.85f;

    [Header("Neon Look")]
    [Range(0f, 2f)] public float stampStrength = 0.95f;
    [Range(0f, 1f)] public float minNeonValue = 0.90f;
    [Range(0f, 1f)] public float maxNeonValue = 1.00f;

    Root data;
    Texture2D overlayTex;
    Color32[] pixels;

    int[] cityAtPixel;                 // NEW: maps pixel -> city index
    int currentDateIndex = 0;          // NEW

    public Root Data => data;          // NEW: expose data to other scripts
    public int CurrentDateIndex => currentDateIndex; // NEW

    void Start()
    {
        if (!jsonFile || !pollutionRenderer)
        {
            Debug.LogError("Assign jsonFile and pollutionRenderer.");
            return;
        }

        data = JsonUtility.FromJson<Root>(jsonFile.text);

        overlayTex = new Texture2D(texWidth, texHeight, TextureFormat.RGBA32, false, true);
        overlayTex.wrapMode = TextureWrapMode.Repeat;
        overlayTex.filterMode = FilterMode.Bilinear;

        pixels = new Color32[texWidth * texHeight];
        cityAtPixel = new int[texWidth * texHeight];                 // NEW
        Array.Fill(cityAtPixel, -1);                                 // NEW

        pollutionRenderer.material.mainTexture = overlayTex;

        if (dateSlider)
        {
            dateSlider.wholeNumbers = true;
            dateSlider.minValue = 0;
            dateSlider.maxValue = data.dates.Length - 1;
            dateSlider.value = 0;
            dateSlider.onValueChanged.AddListener(v => SetDateIndex((int)v));
        }

        SetDateIndex(0);
    }

    public void SetDateIndex(int idx)
    {
        idx = Mathf.Clamp(idx, 0, data.dates.Length - 1);
        currentDateIndex = idx;                                      // NEW

        Array.Clear(pixels, 0, pixels.Length);
        Array.Fill(cityAtPixel, -1);                                 // NEW

        for (int cityIndex = 0; cityIndex < data.cities.Length; cityIndex++)
        {
            var c = data.cities[cityIndex];

            float aqi = c.values[idx].aqi;
            Color col = AqiToNeonColor(aqi);
            col.a = alpha;

            PaintPoint(c.lat, c.lon, col, cityIndex);               // NEW: pass cityIndex
        }

        overlayTex.SetPixels32(pixels);
        overlayTex.Apply(false, false);

        if (dateLabel) dateLabel.text = data.dates[idx];
    }

    // NEW: include cityIndex
    void PaintPoint(float lat, float lon, Color col, int cityIndex)
    {
        float u = (lon + 180f) / 360f;
        float v = (lat + 90f) / 180f;

        int x = Mathf.RoundToInt(u * (texWidth - 1));
        int y = Mathf.RoundToInt(v * (texHeight - 1));

        StampCircleWrap(x, y, brushRadiusPx, col, cityIndex);       // NEW
    }

    // NEW: include cityIndex
    void StampCircleWrap(int cx, int cy, int r, Color c, int cityIndex)
    {
        int r2 = r * r;

        for (int dy = -r; dy <= r; dy++)
        for (int dx = -r; dx <= r; dx++)
        {
            if (dx * dx + dy * dy > r2) continue;

            int x = (cx + dx) % texWidth;
            if (x < 0) x += texWidth;

            int y = cy + dy;
            if (y < 0 || y >= texHeight) continue;

            int idx = y * texWidth + x;

            // additive-ish blend
            Color existing = pixels[idx];
            Color added = existing + c * stampStrength;

            added.r = Mathf.Clamp01(added.r);
            added.g = Mathf.Clamp01(added.g);
            added.b = Mathf.Clamp01(added.b);
            added.a = Mathf.Clamp01(Mathf.Max(existing.a, c.a));

            pixels[idx] = added;

            // NEW: mark this pixel as belonging to this city
            cityAtPixel[idx] = cityIndex;
        }
    }

    // NEW: click lookup method
    public bool TryGetCityAtUV(Vector2 uv, out City city, out float aqi)
    {
        city = null;
        aqi = 0f;

        int x = Mathf.Clamp((int)(uv.x * texWidth), 0, texWidth - 1);
        int y = Mathf.Clamp((int)(uv.y * texHeight), 0, texHeight - 1);

        int idx = y * texWidth + x;
        int cityIndex = cityAtPixel[idx];

        // If user clicked slightly off the blob, search nearby pixels
        if (cityIndex < 0)
        {
            const int searchR = 12;
            for (int r = 1; r <= searchR; r++)
            {
                for (int dy = -r; dy <= r; dy++)
                for (int dx = -r; dx <= r; dx++)
                {
                    int xx = x + dx;
                    int yy = y + dy;
                    if (yy < 0 || yy >= texHeight) continue;

                    // wrap horizontally
                    if (xx < 0) xx += texWidth;
                    if (xx >= texWidth) xx -= texWidth;

                    int i2 = yy * texWidth + xx;
                    int ci = cityAtPixel[i2];
                    if (ci >= 0)
                    {
                        cityIndex = ci;
                        goto Found;
                    }
                }
            }
            return false;
        }

    Found:
        city = data.cities[cityIndex];
        aqi = city.values[currentDateIndex].aqi;
        return true;
    }

    // Neon mapping (same as we used)
    Color AqiToNeonColor(float aqi)
    {
        aqi = Mathf.Max(0f, aqi);

        const float H_GREEN  = 120f / 360f;
        const float H_YELLOW = 58f  / 360f;
        const float H_RED    = 0f;

        float vDeep = Mathf.Clamp01(minNeonValue);
        float vLight = Mathf.Clamp01(maxNeonValue);

        if (aqi <= 50f)
        {
            float t = Mathf.Clamp01(aqi / 50f);
            float v = Mathf.Lerp(vDeep, vLight, t);
            return Color.HSVToRGB(H_GREEN, 1f, v);
        }

        if (aqi <= 100f)
        {
            float t = Mathf.Clamp01((aqi - 50f) / 50f);
            float v = Mathf.Lerp(vDeep, vLight, t);
            return Color.HSVToRGB(H_YELLOW, 1f, v);
        }

        float tt = Mathf.Clamp01((aqi - 100f) / 400f);
        float vRed = Mathf.Lerp(vLight, vDeep, tt);
        return Color.HSVToRGB(H_RED, 1f, vRed);
    }
}
