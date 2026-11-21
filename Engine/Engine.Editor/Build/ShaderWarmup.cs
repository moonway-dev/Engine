using Engine.RHI;
using Engine.Shaders;

namespace Engine.Editor.Build;

public static class ShaderWarmup
{
    private static readonly int[] ReflectionCapacities = [18, 42, 84, 153, 306, 612, 1536];

    public sealed record WarmupResult(int Compiled, int Failed, List<string> Failures);

    public static WarmupResult CompileAll(SlangShaderCompiler compiler, string shaderDirectory, Action<string>? log = null)
    {
        int compiled = 0;
        int failed = 0;
        List<string> failures = [];

        void Compile(string source, (ShaderStage Stage, string EntryPoint)[] entries, IReadOnlyDictionary<string, string?>? defines = null)
        {
            try
            {
                compiler.CompileProgram(Path.Combine(shaderDirectory, source), entries, defines?.ToDictionary(pair => pair.Key, pair => pair.Value), programName: Path.GetFileNameWithoutExtension(source));
                compiled++;
            }
            catch (Exception exception) when (exception is ShaderCompilationException or FileNotFoundException or NotSupportedException or InvalidOperationException)
            {
                failed++;
                failures.Add($"{source}: {exception.Message.Split('\n')[0]}");
            }
        }

        Dictionary<string, string?> DefineCombo(bool temporal, bool rt)
        {
            Dictionary<string, string?> defines = [];
            if (temporal)
                defines["TEMPORAL_MRT"] = "1";
            if (rt)
                defines["SPECULAR_MRT"] = "1";
            return defines;
        }

        (ShaderStage, string)[] vsPs = [(ShaderStage.Vertex, "vsMain"), (ShaderStage.Fragment, "psMain")];
        (ShaderStage, string)[] cs = [(ShaderStage.Compute, "csMain")];
        (ShaderStage, string)[] raySet = [(ShaderStage.RayGeneration, "rayGen"), (ShaderStage.Miss, "missMain"), (ShaderStage.ClosestHit, "closestHitMain")];

        log?.Invoke("shaders: compiling pipeline variants…");
        Compile("Shadow.slang", vsPs);
        Compile("Tonemap.slang", vsPs);
        Compile("DebugLines.slang", vsPs);
        Compile("Bloom.slang", cs);
        Compile("IBLBake.slang", cs);
        Compile("TemporalResolve.slang", cs);
        Compile("TemporalResolve.slang", cs, new Dictionary<string, string?> { ["RT_GI_DENOISE"] = "1" });
        Compile("SSGI.slang", cs);
        Compile("GIComposite.slang", cs);
        foreach (bool temporal in new[] { false, true })
        {
            foreach (bool rt in new[] { false, true })
            {
                Compile("PBR.slang", vsPs, DefineCombo(temporal, rt));
            }
        }

        Compile("RtPrepass.slang", vsPs);
        Compile("RtShadows.slang", raySet);
        Compile("RtFilter.slang", cs);
        Compile("RtComposite.slang", cs);
        foreach (bool temporal in new[] { false, true })
        {
            foreach (bool rt in new[] { false, true })
            {
                Dictionary<string, string?> defines = DefineCombo(temporal, rt);
                defines["RT_SHADOWS"] = "1";
                Compile("PBR.slang", vsPs, defines);
            }
        }
        foreach (int capacity in ReflectionCapacities)
        {
            Dictionary<string, string?> materialDefines = new()
            {
                ["RT_MATERIAL_TEXTURE_COUNT"] = capacity.ToString(System.Globalization.CultureInfo.InvariantCulture),
            };
            Compile("RtReflections.slang", raySet, materialDefines);
            Compile("RtGi.slang", raySet, materialDefines);
        }

        if (failed > 0)
        {
            foreach (string failure in failures.Take(6))
                log?.Invoke($"warning: shader warmup {failure}");
        }
        return new WarmupResult(compiled, failed, failures);
    }
}
