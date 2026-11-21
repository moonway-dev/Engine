namespace Engine.Scene;

public static class CloudPresets
{
    public readonly record struct Settings(float Coverage, float Density, float Bottom, float Top);

    public static readonly string[] Names = ["Sunny", "Partly cloudy", "Overcast", "Stormy"];

    public static Settings Get(string name) => TryGet(name, out Settings settings) ? settings : Get(Names[0]);

    public static bool TryGet(string name, out Settings settings)
    {
        switch (name)
        {
            case "Sunny":
                settings = new Settings(0.30f, 0.55f, 1.8f, 4.6f);
                return true;
            case "Partly cloudy":
                settings = new Settings(0.48f, 0.8f, 1.6f, 5.4f);
                return true;
            case "Overcast":
                settings = new Settings(0.68f, 1.05f, 1.3f, 6.8f);
                return true;
            case "Stormy":
                settings = new Settings(0.88f, 1.4f, 1.0f, 7.6f);
                return true;
            default:
                settings = default;
                return false;
        }
    }
}
