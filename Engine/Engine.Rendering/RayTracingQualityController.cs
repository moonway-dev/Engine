using Engine.RHI;

namespace Engine.Rendering;

public enum RayTracingQualityPreset
{
    Low,
    Medium,
    High,
    Ultra,
}

public enum RayTracingQualityCamera
{
    Scene,
    Game,
}

public enum RayTracingQualityEffect
{
    Shadows,
    Reflections,
    Gi,
}

public readonly record struct RayTracingQualityProfile(float ResolutionScale, uint Samples, uint FarTraceRate)
{
    private static readonly RayTracingQualityProfile[] Profiles =
    [
        new(0.25f, 1, 8),
        new(0.5f, 2, 4),
        new(0.75f, 4, 2),
        new(1f, 8, 1),
    ];

    public static RayTracingQualityProfile FromStage(int stage) => Profiles[Math.Clamp(stage, 0, Profiles.Length - 1)];
}

public sealed class RayTracingQualityController
{
    private const int OverBudgetFramesToReduce = 3;
    private const int UnderBudgetFramesToRaise = 30;
    private readonly AdaptationState[,] _states = new AdaptationState[2, 3];
    private readonly double[,] _latestMilliseconds = new double[2, 3];
    private long _lastFrameNumber = -1;

    private struct AdaptationState
    {
        public int Stage;
        public int OverBudgetFrames;
        public int UnderBudgetFrames;
        public bool Initialized;
    }

    public bool HasGpuTiming { get; private set; }
    public long LastTimingFrame => _lastFrameNumber;

    public void ResetTiming()
    {
        HasGpuTiming = false;
        _lastFrameNumber = -1;
        Array.Clear(_latestMilliseconds);
        Array.Clear(_states);
    }

    public void Update(GpuFrameTiming? timing, bool timestampsSupported)
    {
        if (!timestampsSupported)
        {
            HasGpuTiming = false;
            return;
        }
        if (timing is null || timing.Incomplete || timing.FrameNumber <= _lastFrameNumber)
            return;

        _lastFrameNumber = timing.FrameNumber;
        HasGpuTiming = true;
        Array.Clear(_latestMilliseconds);
        bool[,] found = new bool[2, 3];
        foreach (GpuPassTiming pass in timing.Passes)
        {
            if (!TryClassify(pass.Name, out int camera, out int effect))
                continue;
            _latestMilliseconds[camera, effect] += Math.Max(0, pass.Milliseconds);
            found[camera, effect] = true;
        }

        for (int camera = 0; camera < 2; camera++)
        for (int effect = 0; effect < 3; effect++)
        {
            if (!found[camera, effect])
                continue;
            ref AdaptationState state = ref _states[camera, effect];
            if (!state.Initialized)
            {
                state.Stage = 3;
                state.Initialized = true;
            }
            double budget = Budget((RayTracingQualityCamera)camera, (RayTracingQualityEffect)effect);
            double milliseconds = _latestMilliseconds[camera, effect];
            if (milliseconds > budget)
            {
                state.OverBudgetFrames++;
                state.UnderBudgetFrames = 0;
                if (state.OverBudgetFrames >= OverBudgetFramesToReduce)
                {
                    state.Stage = Math.Max(0, state.Stage - 1);
                    state.OverBudgetFrames = 0;
                }
            }
            else if (milliseconds < budget * 0.55)
            {
                state.UnderBudgetFrames++;
                state.OverBudgetFrames = 0;
                if (state.UnderBudgetFrames >= UnderBudgetFramesToRaise)
                {
                    state.Stage = Math.Min(3, state.Stage + 1);
                    state.UnderBudgetFrames = 0;
                }
            }
            else
            {
                state.OverBudgetFrames = 0;
                state.UnderBudgetFrames = 0;
            }
        }
    }

    public int GetStage(
        RayTracingQualityCamera camera,
        RayTracingQualityEffect effect,
        RayTracingQualityPreset preset,
        bool adaptiveEnabled,
        bool sceneActive,
        bool gameActive)
    {
        int maximum = Math.Clamp((int)preset, 0, 3);
        if (!adaptiveEnabled)
            return maximum;

        if (!HasGpuTiming)
        {
            if (sceneActive && gameActive)
                return camera == RayTracingQualityCamera.Game ? Math.Min(maximum, 2) : 0;
            return Math.Min(maximum, 2);
        }

        AdaptationState state = _states[(int)camera, (int)effect];
        int stage = state.Initialized ? state.Stage : camera == RayTracingQualityCamera.Game ? 2 : 1;
        stage = Math.Min(maximum, stage);
        if (sceneActive && gameActive && camera == RayTracingQualityCamera.Scene)
            stage = Math.Min(stage, 2);
        return stage;
    }

    public double GetLastMilliseconds(RayTracingQualityCamera camera, RayTracingQualityEffect effect) =>
        _latestMilliseconds[(int)camera, (int)effect];

    private static double Budget(RayTracingQualityCamera camera, RayTracingQualityEffect effect) =>
        camera == RayTracingQualityCamera.Game
            ? effect switch
            {
                RayTracingQualityEffect.Shadows => 2.0,
                RayTracingQualityEffect.Reflections => 3.5,
                _ => 2.5,
            }
            : effect switch
            {
                RayTracingQualityEffect.Shadows => 1.0,
                RayTracingQualityEffect.Reflections => 2.0,
                _ => 1.5,
            };

    private static bool TryClassify(string name, out int camera, out int effect)
    {
        int prefixLength;
        if (name.StartsWith("RtGame", StringComparison.Ordinal))
        {
            camera = (int)RayTracingQualityCamera.Game;
            prefixLength = 6;
        }
        else if (name.StartsWith("RtScene", StringComparison.Ordinal))
        {
            camera = (int)RayTracingQualityCamera.Scene;
            prefixLength = 7;
        }
        else
        {
            camera = -1;
            effect = -1;
            return false;
        }

        string pass = name[prefixLength..];
        if (pass.Contains("Gi", StringComparison.Ordinal))
            effect = (int)RayTracingQualityEffect.Gi;
        else if (pass.Contains("Reflection", StringComparison.Ordinal)
            || pass.StartsWith("Filter", StringComparison.Ordinal)
            || pass.StartsWith("Composite", StringComparison.Ordinal))
            effect = (int)RayTracingQualityEffect.Reflections;
        else if (pass.StartsWith("Trace", StringComparison.Ordinal)
            || pass.Contains("Shadow", StringComparison.Ordinal))
            effect = (int)RayTracingQualityEffect.Shadows;
        else
            effect = -1;
        return effect >= 0;
    }
}
