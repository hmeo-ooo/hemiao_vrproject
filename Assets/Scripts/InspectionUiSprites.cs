using UnityEngine;

/// <summary>
/// 审视界面工具图标的运行时占位 Sprite 生成与解析。
/// </summary>
public static class InspectionUiSprites
{
    static Sprite _knifePlaceholder;
    static Sprite _hammerPlaceholder;

    public static Sprite ResolveKnifeSprite(Sprite itemSprite, Sprite globalDefault)
    {
        if (itemSprite != null) return itemSprite;
        if (globalDefault != null) return globalDefault;
        return GetKnifePlaceholder();
    }

    public static Sprite GetKnifePlaceholder()
    {
        if (_knifePlaceholder != null) return _knifePlaceholder;

        const int w = 128;
        const int h = 128;
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            name = "InspectionKnifePlaceholder",
        };

        var clear = new Color32(0, 0, 0, 0);
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
                tex.SetPixel(x, y, clear);
        }

        FillRoundedRect(tex, 18, 14, 46, 46, 8, new Color32(90, 58, 38, 255));
        FillTriangle(tex,
            new Vector2Int(42, 106), new Vector2Int(18, 108), new Vector2Int(62, 62),
            new Color32(210, 220, 230, 255));
        StrokeTriangle(tex,
            new Vector2Int(42, 106), new Vector2Int(18, 108), new Vector2Int(62, 62),
            new Color32(60, 70, 85, 255));

        tex.Apply();
        _knifePlaceholder = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.15f, 0.85f), 100f);
        _knifePlaceholder.name = "InspectionKnifePlaceholder";
        return _knifePlaceholder;
    }

    public static Sprite ResolveHammerSprite(Sprite itemSprite, Sprite globalDefault)
    {
        if (itemSprite != null) return itemSprite;
        if (globalDefault != null) return globalDefault;
        return GetHammerPlaceholder();
    }

    /// <summary>旧 API 兼容：不再访问 Unity 内置 UISprite，统一走运行时占位图。</summary>
    public static Sprite GetBuiltinUiSprite() => GetHammerPlaceholder();

    public static Sprite GetHammerPlaceholder()
    {
        if (_hammerPlaceholder != null) return _hammerPlaceholder;

        const int w = 128;
        const int h = 128;
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            name = "InspectionHammerPlaceholder",
        };

        var clear = new Color32(0, 0, 0, 0);
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
                tex.SetPixel(x, y, clear);
        }

        FillRoundedRect(tex, 54, 18, 74, 88, 6, new Color32(95, 62, 38, 255));
        FillRoundedRect(tex, 28, 78, 100, 108, 8, new Color32(170, 175, 185, 255));
        FillRoundedRect(tex, 34, 88, 94, 102, 4, new Color32(120, 125, 135, 255));

        tex.Apply();
        _hammerPlaceholder = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.2f, 0.85f), 100f);
        _hammerPlaceholder.name = "InspectionHammerPlaceholder";
        return _hammerPlaceholder;
    }

    static void FillRoundedRect(Texture2D tex, int x0, int y0, int x1, int y1, int radius, Color32 color)
    {
        for (int y = y0; y <= y1; y++)
        {
            for (int x = x0; x <= x1; x++)
            {
                if (!InsideRoundedRect(x, y, x0, y0, x1, y1, radius)) continue;
                tex.SetPixel(x, y, color);
            }
        }
    }

    static bool InsideRoundedRect(int x, int y, int x0, int y0, int x1, int y1, int radius)
    {
        if (x < x0 || x > x1 || y < y0 || y > y1) return false;

        int r = Mathf.Max(1, radius);
        if (x < x0 + r && y < y0 + r)
            return (x - (x0 + r)) * (x - (x0 + r)) + (y - (y0 + r)) * (y - (y0 + r)) <= r * r;
        if (x > x1 - r && y < y0 + r)
            return (x - (x1 - r)) * (x - (x1 - r)) + (y - (y0 + r)) * (y - (y0 + r)) <= r * r;
        if (x < x0 + r && y > y1 - r)
            return (x - (x0 + r)) * (x - (x0 + r)) + (y - (y1 - r)) * (y - (y1 - r)) <= r * r;
        if (x > x1 - r && y > y1 - r)
            return (x - (x1 - r)) * (x - (x1 - r)) + (y - (y1 - r)) * (y - (y1 - r)) <= r * r;
        return true;
    }

    static void FillTriangle(Texture2D tex, Vector2Int a, Vector2Int b, Vector2Int c, Color32 color)
    {
        int minX = Mathf.Min(a.x, Mathf.Min(b.x, c.x));
        int maxX = Mathf.Max(a.x, Mathf.Max(b.x, c.x));
        int minY = Mathf.Min(a.y, Mathf.Min(b.y, c.y));
        int maxY = Mathf.Max(a.y, Mathf.Max(b.y, c.y));

        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                if (PointInTriangle(new Vector2(x + 0.5f, y + 0.5f), a, b, c))
                    tex.SetPixel(x, y, color);
            }
        }
    }

    static void StrokeTriangle(Texture2D tex, Vector2Int a, Vector2Int b, Vector2Int c, Color32 color)
    {
        DrawLine(tex, a, b, color);
        DrawLine(tex, b, c, color);
        DrawLine(tex, c, a, color);
    }

    static void DrawLine(Texture2D tex, Vector2Int a, Vector2Int b, Color32 color)
    {
        int dx = Mathf.Abs(b.x - a.x);
        int dy = Mathf.Abs(b.y - a.y);
        int sx = a.x < b.x ? 1 : -1;
        int sy = a.y < b.y ? 1 : -1;
        int err = dx - dy;
        int x = a.x;
        int y = a.y;

        while (true)
        {
            if (x >= 0 && x < tex.width && y >= 0 && y < tex.height)
                tex.SetPixel(x, y, color);

            if (x == b.x && y == b.y) break;
            int e2 = err * 2;
            if (e2 > -dy) { err -= dy; x += sx; }
            if (e2 < dx) { err += dx; y += sy; }
        }
    }

    static bool PointInTriangle(Vector2 p, Vector2Int a, Vector2Int b, Vector2Int c)
    {
        float d1 = Sign(p, a, b);
        float d2 = Sign(p, b, c);
        float d3 = Sign(p, c, a);
        bool hasNeg = d1 < 0f || d2 < 0f || d3 < 0f;
        bool hasPos = d1 > 0f || d2 > 0f || d3 > 0f;
        return !(hasNeg && hasPos);
    }

    static float Sign(Vector2 p1, Vector2Int p2, Vector2Int p3) =>
        (p1.x - p3.x) * (p2.y - p3.y) - (p2.x - p3.x) * (p1.y - p3.y);
}
