using Engine.Editor;
using Engine.Core;

namespace Engine.Editor;

class Program
{
    static void Main(string[] args)
    {
        foreach (var arg in args)
        {
            if (arg == "--devmode" || arg == "-devmode")
            {
                Engine.Core.System.DevMode = true;
                break;
            }
        }

        using var editor = new EditorApplication();
        editor.Run();
    }
}
