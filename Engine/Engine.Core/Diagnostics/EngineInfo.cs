using System.Runtime.InteropServices;

namespace Engine.Core;

public static class EngineInfo
{
    public const string EngineName = "Engine";
    public const string EngineVersion = "0.1.0";

    public static string RuntimeDescription => RuntimeInformation.FrameworkDescription;
    public static string OperatingSystem => RuntimeInformation.OSDescription;
    public static bool Is64BitProcess => Environment.Is64BitProcess;
}
