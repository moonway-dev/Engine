using Engine.RHI;

namespace Engine.Player;

public sealed class PlayerOptions
{
    public int Width { get; private set; } = 1600;
    public int Height { get; private set; } = 900;
    public int Frames { get; private set; }
    public GraphicsBackend RequestedBackend { get; private set; } = GraphicsBackend.Vulkan;
    public bool Validation { get; private set; }
    public bool VSync { get; private set; } = true;
    public string? ProjectPath { get; private set; }
    public string? ScenePath { get; private set; }

    internal void WithScenePath(string path) => ScenePath = path;

    public static PlayerOptions Parse(string[] args)
    {
        PlayerOptions options = new();
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i].ToLowerInvariant())
            {
                case "--width":
                    options.Width = ParseInt(args, ref i, options.Width);
                    break;
                case "--height":
                    options.Height = ParseInt(args, ref i, options.Height);
                    break;
                case "--frames":
                    options.Frames = ParseInt(args, ref i, options.Frames);
                    break;
                case "--scene":
                    if (i + 1 < args.Length)
                        options.ScenePath = args[++i];
                    break;
                case "--project":
                    if (i + 1 < args.Length)
                        options.ProjectPath = args[++i];
                    break;
                case "--backend":
                    if (i + 1 < args.Length && args[++i].Equals("null", StringComparison.OrdinalIgnoreCase))
                        options.RequestedBackend = GraphicsBackend.Null;
                    break;
                case "--validation":
                    options.Validation = true;
                    break;
                case "--no-vsync":
                    options.VSync = false;
                    break;
            }
        }
        return options;
    }

    private static int ParseInt(string[] args, ref int index, int fallback)
    {
        if (index + 1 >= args.Length || !int.TryParse(args[index + 1], out int value))
            return fallback;
        index++;
        return value;
    }
}
