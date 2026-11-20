using System.Numerics;

namespace Engine.Editor.Models;

public readonly record struct ProjectBrowserLayout(int Columns, int Rows, float CellWidth, float RowHeight, float PreviewSize, bool IsList)
{
    public static ProjectBrowserLayout Create(float width, float zoom, float textHeight, int count)
    {
        width = MathF.Max(1f, width);
        bool list = zoom <= 40f;
        float size = MathF.Min(MathF.Max(40f, zoom), width);
        int columns = list ? 1 : Math.Max(1, (int)((width + 8f) / (size + 8f)));
        float cellWidth = list ? width : MathF.Min(size, (width - (columns - 1) * 8f) / columns);
        float rowHeight = list ? textHeight + 8f : cellWidth + textHeight + 12f;
        return new ProjectBrowserLayout(columns, (count + columns - 1) / columns, cellWidth, rowHeight, list ? textHeight + 2f : MathF.Max(1f, cellWidth - 8f), list);
    }

    public Vector2 Position(int index) => new(index % Columns * (CellWidth + 8f), index / Columns * RowHeight);

    public static Vector2 FitImage(int width, int height, Vector2 available)
    {
        if (width <= 0 || height <= 0)
            return Vector2.Zero;
        float scale = MathF.Min(available.X / width, available.Y / height);
        return new Vector2(width * scale, height * scale);
    }

    public static string Ellipsize(string text, float width, Func<string, float> measure)
    {
        if (measure(text) <= width)
            return text;
        const string suffix = "…";
        if (measure(suffix) > width)
            return string.Empty;
        int low = 0;
        int high = text.Length;
        while (low < high)
        {
            int middle = (low + high + 1) / 2;
            if (measure(text[..middle] + suffix) <= width)
                low = middle;
            else
                high = middle - 1;
        }
        if (low > 0 && char.IsHighSurrogate(text[low - 1]))
            low--;
        return text[..low] + suffix;
    }
}
