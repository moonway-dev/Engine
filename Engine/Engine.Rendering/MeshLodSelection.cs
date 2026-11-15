namespace Engine.Rendering;

public enum MeshLodMode
{
    Auto,
    Off,
    Forced,
}

public static class MeshLodSelection
{
    public static int Select(ReadOnlySpan<float> errors, float pixelsPerUnit, float scale,
        float pixelBudget, int previousLevel = 0, float hysteresis = 0.15f)
    {
        if (errors.IsEmpty || !float.IsFinite(pixelsPerUnit) || pixelsPerUnit <= 0
            || !float.IsFinite(scale) || !float.IsFinite(pixelBudget) || pixelBudget <= 0)
            return 0;
        previousLevel = Math.Clamp(previousLevel, 0, errors.Length - 1);
        hysteresis = float.IsFinite(hysteresis) ? Math.Clamp(hysteresis, 0, 0.5f) : 0;
        double projection = (double)pixelsPerUnit * MathF.Abs(scale);
        int selected = 0;
        for (int level = 1; level < errors.Length; level++)
        {
            if (!float.IsFinite(errors[level]) || errors[level] < 0 || errors[level] < errors[level - 1])
                break;
            float threshold = pixelBudget * (level > previousLevel ? 1 - hysteresis : 1 + hysteresis);
            if (errors[level] * projection > threshold)
                break;
            selected = level;
        }
        return selected;
    }
}
