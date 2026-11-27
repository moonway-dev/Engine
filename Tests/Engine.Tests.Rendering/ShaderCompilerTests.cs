using System.Diagnostics;
using System.Text;
using Engine.RHI;
using Engine.Shaders;
using Xunit;

namespace Engine.Tests.Rendering;

public class ShaderCompilerTests : IDisposable
{
    private readonly string _shaderDirectory;

    public ShaderCompilerTests()
    {
        _shaderDirectory = Path.Combine(Path.GetTempPath(), "EngineShaderTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_shaderDirectory);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_shaderDirectory, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private const string TestShader = """
        struct MaterialData
        {
            float3 albedoColor;
            float metallic;
        };
        [[vk::binding(0, 0)]] ConstantBuffer<MaterialData> material;
        struct ModelConstants
        {
            float4x4 model;
        };
        [[vk::push_constant]] ModelConstants modelConstants;

        struct VertexInput
        {
            float3 position : POSITION;
        };
        struct VertexOutput
        {
            float4 position : SV_Position;
        };

        [shader("vertex")]
        VertexOutput vsMain(VertexInput input)
        {
            VertexOutput output;
            output.position = mul(camera-less-projection, float4(input.position, 1.0));
            return output;
        }
        """;

    private const string ValidTestShader = """
        struct MaterialData
        {
            float3 albedoColor;
            float metallic;
        };
        [[vk::binding(0, 0)]] ConstantBuffer<MaterialData> material;
        struct ModelConstants
        {
            float4x4 model;
        };
        [[vk::push_constant]] ModelConstants modelConstants;

        struct VertexInput
        {
            float3 position : POSITION;
        };
        struct VertexOutput
        {
            float4 position : SV_Position;
        };

        [shader("vertex")]
        VertexOutput vsMain(VertexInput input)
        {
            VertexOutput output;
            float4 world = mul(modelConstants.model, float4(input.position, 1.0));
            output.position = world;
            return output;
        }

        [shader("fragment")]
        float4 psMain(VertexOutput input) : SV_Target
        {
            return float4(material.albedoColor, material.metallic);
        }
        """;

    private SlangShaderCompiler CreateCompiler() => new(cacheDirectory: Path.Combine(_shaderDirectory, "Cache"));

    [Fact]
    public void Compile_ProducesSpirVWithReflection()
    {
        SlangShaderCompiler compiler = CreateCompiler();
        string sourcePath = Path.Combine(_shaderDirectory, "Test.slang");
        File.WriteAllText(sourcePath, ValidTestShader);

        ShaderProgramDescription program = compiler.CompileProgram(
            sourcePath,
            [(ShaderStage.Vertex, "vsMain"), (ShaderStage.Fragment, "psMain")],
            programName: "Test");

        Assert.Equal(2, program.Stages.Count);
        Assert.All(program.Stages, stage =>
        {
            Assert.True(stage.SpirV.Length > 100);
            Assert.Equal(0x07230203u, BitConverter.ToUInt32(stage.SpirV, 0));
            Assert.Equal("main", stage.EntryPoint);
        });
        Assert.NotEmpty(program.Reflection.DescriptorSets);
        DescriptorSetLayout set0 = Assert.Single(program.Reflection.DescriptorSets, s => s.SetIndex == 0);
        Assert.Contains(set0.Bindings, b => b.Type == DescriptorType.UniformBuffer);
        Assert.NotEmpty(program.Reflection.PushConstants);
    }

    [Fact]
    public void Compile_SecondRun_UsesCache()
    {
        SlangShaderCompiler compiler = CreateCompiler();
        string sourcePath = Path.Combine(_shaderDirectory, "Cached.slang");
        File.WriteAllText(sourcePath, ValidTestShader);
        ShaderCompilationRequest request = new()
        {
            SourcePath = sourcePath,
            EntryPoint = "vsMain",
            Stage = ShaderStage.Vertex,
        };
        ShaderCompilationResult first = compiler.Compile(request);
        ShaderCompilationResult second = compiler.Compile(request);
        Assert.False(first.FromCache);
        Assert.True(second.FromCache);
        Assert.Equal(first.StageCode.SpirV, second.StageCode.SpirV);
    }

    [Fact]
    public void Compile_ImportedModuleChange_InvalidatesCache()
    {
        SlangShaderCompiler compiler = CreateCompiler();
        string commonPath = Path.Combine(_shaderDirectory, "Common.slang");
        string sourcePath = Path.Combine(_shaderDirectory, "Imported.slang");
        File.WriteAllText(commonPath, "float4 importedColor() { return float4(1.0, 0.0, 0.0, 1.0); }");
        File.WriteAllText(sourcePath, """
            import Common;
            [shader("fragment")]
            float4 psMain() : SV_Target
            {
                return importedColor();
            }
            """);

        ShaderCompilationRequest request = new()
        {
            SourcePath = sourcePath,
            EntryPoint = "psMain",
            Stage = ShaderStage.Fragment,
        };
        ShaderCompilationResult first = compiler.Compile(request);
        ShaderCompilationResult cached = compiler.Compile(request);
        File.WriteAllText(commonPath, "float4 importedColor() { return float4(0.0, 1.0, 0.0, 1.0); }");
        ShaderCompilationResult changed = compiler.Compile(request);

        Assert.False(first.FromCache);
        Assert.True(cached.FromCache);
        Assert.False(changed.FromCache);
        Assert.NotEqual(first.StageCode.SpirV, changed.StageCode.SpirV);
    }

    [Fact]
    public void Compile_ComputeStorageImageAndPushConstantsReflectCorrectly()
    {
        SlangShaderCompiler compiler = CreateCompiler();
        string sourcePath = Path.Combine(_shaderDirectory, "Compute.slang");
        File.WriteAllText(sourcePath, """
            [[vk::binding(0, 0)]] Texture2D<float4> sourceTexture;
            [[vk::binding(1, 0)]] RWTexture2D<float4> destinationTexture;
            struct Constants { uint4 extent; float4 parameters; };
            [[vk::push_constant]] Constants constants;
            [shader("compute")]
            [numthreads(8, 8, 1)]
            void csMain(uint3 id : SV_DispatchThreadID)
            {
                if (all(id.xy < constants.extent.xy))
                    destinationTexture[id.xy] = sourceTexture[id.xy] * constants.parameters.x;
            }
            """);

        ShaderProgramDescription program = compiler.CompileProgram(sourcePath, [(ShaderStage.Compute, "csMain")]);
        DescriptorSetLayout set = Assert.Single(program.Reflection.DescriptorSets);
        Assert.Equal(DescriptorType.SampledImage, Assert.Single(set.Bindings, binding => binding.Binding == 0).Type);
        Assert.Equal(DescriptorType.StorageImage, Assert.Single(set.Bindings, binding => binding.Binding == 1).Type);
        Assert.Equal(32u, Assert.Single(program.Reflection.PushConstants).Size);
    }

    [Fact]
    public void Compile_IBLBakeShader_ReflectsStorageBindingsAndConstants()
    {
        SlangShaderCompiler compiler = CreateCompiler();
        string repositoryRoot = FindRepositoryRoot();
        string shaderPath = Path.Combine(repositoryRoot, "Samples", "Basic3D", "Shaders", "IBLBake.slang");

        ShaderProgramDescription program = compiler.CompileProgram(shaderPath, [(ShaderStage.Compute, "csMain")]);

        DescriptorSetLayout set = Assert.Single(program.Reflection.DescriptorSets);
        Assert.Equal(4, set.Bindings.Count);
        Assert.Equal(DescriptorType.SampledImage, Assert.Single(set.Bindings, binding => binding.Binding == 0).Type);
        Assert.Equal(DescriptorType.Sampler, Assert.Single(set.Bindings, binding => binding.Binding == 1).Type);
        Assert.Equal(DescriptorType.StorageImage, Assert.Single(set.Bindings, binding => binding.Binding == 2).Type);
        Assert.Equal(DescriptorType.StorageImage, Assert.Single(set.Bindings, binding => binding.Binding == 3).Type);
        Assert.Equal(16u, Assert.Single(program.Reflection.PushConstants).Size);
    }

    [Fact]
    public void Compile_TemporalResolve_ReflectsBindingsAndConstants()
    {
        SlangShaderCompiler compiler = CreateCompiler();
        string shaderPath = Path.Combine(FindRepositoryRoot(), "Samples", "Basic3D", "Shaders", "TemporalResolve.slang");

        ShaderProgramDescription program = compiler.CompileProgram(shaderPath, [(ShaderStage.Compute, "csMain")]);

        DescriptorSetLayout set = Assert.Single(program.Reflection.DescriptorSets);
        Assert.Equal(6, set.Bindings.Count);
        Assert.Equal(DescriptorType.SampledImage, Assert.Single(set.Bindings, binding => binding.Binding == 0).Type);
        Assert.Equal(DescriptorType.SampledImage, Assert.Single(set.Bindings, binding => binding.Binding == 1).Type);
        Assert.Equal(DescriptorType.SampledImage, Assert.Single(set.Bindings, binding => binding.Binding == 2).Type);
        Assert.Equal(DescriptorType.SampledImage, Assert.Single(set.Bindings, binding => binding.Binding == 3).Type);
        Assert.Equal(DescriptorType.StorageImage, Assert.Single(set.Bindings, binding => binding.Binding == 4).Type);
        Assert.Equal(DescriptorType.Sampler, Assert.Single(set.Bindings, binding => binding.Binding == 5).Type);
        Assert.Equal(28u, Assert.Single(program.Reflection.PushConstants).Size);
    }

    [Fact]
    public void Compile_PbrTemporalVariant_EmitsMotionTarget()
    {
        SlangShaderCompiler compiler = CreateCompiler();
        string shaderPath = Path.Combine(FindRepositoryRoot(), "Samples", "Basic3D", "Shaders", "PBR.slang");

        ShaderProgramDescription program = compiler.CompileProgram(
            shaderPath,
            [(ShaderStage.Vertex, "vsMain"), (ShaderStage.Fragment, "psMain")],
            new Dictionary<string, string?> { ["TEMPORAL_MRT"] = "1" });

        Assert.Equal(2, program.Stages.Count);
        Assert.NotEmpty(program.Reflection.PushConstants);
        Assert.Equal(128u, Assert.Single(program.Reflection.PushConstants).Size);
    }

    [Fact]
    public void Compile_PbrRtVariant_ReflectsRtBindings()
    {
        SlangShaderCompiler compiler = CreateCompiler();
        string shaderPath = Path.Combine(FindRepositoryRoot(), "Samples", "Basic3D", "Shaders", "PBR.slang");

        ShaderProgramDescription program = compiler.CompileProgram(
            shaderPath,
            [(ShaderStage.Vertex, "vsMain"), (ShaderStage.Fragment, "psMain")],
            new Dictionary<string, string?> { ["TEMPORAL_MRT"] = "1", ["RT_SHADOWS"] = "1", ["SPECULAR_MRT"] = "1" });

        Engine.RHI.DescriptorSetLayout set0 = program.Reflection.DescriptorSets.Single(layout => layout.SetIndex == 0);
        Assert.Equal(DescriptorType.SampledImage, Assert.Single(set0.Bindings, binding => binding.Binding == 9).Type);
        Assert.Equal(DescriptorType.UniformBuffer, Assert.Single(set0.Bindings, binding => binding.Binding == 10).Type);
        Assert.Equal(128u, Assert.Single(program.Reflection.PushConstants).Size);
    }

    [Fact]
    public void Compile_RtShadowProgram_ReflectsStages()
    {
        SlangShaderCompiler compiler = CreateCompiler();
        string shaderPath = Path.Combine(FindRepositoryRoot(), "Samples", "Basic3D", "Shaders", "RtShadows.slang");

        ShaderProgramDescription program = compiler.CompileProgram(
            shaderPath,
            [(ShaderStage.RayGeneration, "rayGen"), (ShaderStage.Miss, "missMain"), (ShaderStage.ClosestHit, "closestHitMain")]);

        Assert.Equal(3, program.Stages.Count);
    }

    [Fact]
    public void Compile_RtReflectionProgram_ReflectsStages()
    {
        SlangShaderCompiler compiler = CreateCompiler();
        string shaderPath = Path.Combine(FindRepositoryRoot(), "Samples", "Basic3D", "Shaders", "RtReflections.slang");

        const uint textureArraySize = 120;
        ShaderProgramDescription program = compiler.CompileProgram(
            shaderPath,
            [(ShaderStage.RayGeneration, "rayGen"), (ShaderStage.Miss, "missMain"), (ShaderStage.ClosestHit, "closestHitMain")],
            new Dictionary<string, string?> { ["RT_MATERIAL_TEXTURE_COUNT"] = textureArraySize.ToString(System.Globalization.CultureInfo.InvariantCulture) });

        Assert.Equal(3, program.Stages.Count);
        Engine.RHI.DescriptorSetLayout set1 = program.Reflection.DescriptorSets.Single(layout => layout.SetIndex == 1);
        Assert.Equal(DescriptorType.StorageBuffer, Assert.Single(set1.Bindings, binding => binding.Binding == 0).Type);
        Assert.Equal(DescriptorType.SampledImage, Assert.Single(set1.Bindings, binding => binding.Binding == 3).Type);
        Assert.Equal(textureArraySize, Assert.Single(set1.Bindings, binding => binding.Binding == 3).ArrayCount);
        DescriptorSetLayout set0 = program.Reflection.DescriptorSets.Single(layout => layout.SetIndex == 0);
        Assert.Equal(DescriptorType.SampledImage, Assert.Single(set0.Bindings, binding => binding.Binding == 10).Type);
    }

    [Fact]
    public void Compile_RtGiProgram_ReflectsStages()
    {
        SlangShaderCompiler compiler = CreateCompiler();
        string shaderPath = Path.Combine(FindRepositoryRoot(), "Samples", "Basic3D", "Shaders", "RtGi.slang");

        const uint textureArraySize = 180;
        ShaderProgramDescription program = compiler.CompileProgram(
            shaderPath,
            [(ShaderStage.RayGeneration, "rayGen"), (ShaderStage.Miss, "missMain"), (ShaderStage.ClosestHit, "closestHitMain")],
            new Dictionary<string, string?> { ["RT_MATERIAL_TEXTURE_COUNT"] = textureArraySize.ToString(System.Globalization.CultureInfo.InvariantCulture) });

        Assert.Equal(3, program.Stages.Count);
        DescriptorSetLayout set1 = program.Reflection.DescriptorSets.Single(layout => layout.SetIndex == 1);
        Assert.Equal(textureArraySize, Assert.Single(set1.Bindings, binding => binding.Binding == 3).ArrayCount);
    }

    [Theory]
    [InlineData(1024u, 2048u, 1020)]
    [InlineData(4096u, 160u, 150)]
    [InlineData(64u, 32u, 24)]
    public void CalculateRtMaterialTextureCapacity_UsesDeviceLimitsAndSixTextureSets(uint perStage, uint descriptorSet, int expected)
    {
        Assert.Equal(expected, Engine.Rendering.RayTracingTextureCapacity.Calculate(perStage, descriptorSet));
    }

    [Theory]
    [InlineData(8u, 4096u)]
    [InlineData(4096u, 5u)]
    public void CalculateRtMaterialTextureCapacity_RejectsLimitsBelowMinimum(uint perStage, uint descriptorSet)
    {
        Assert.Throws<NotSupportedException>(() => Engine.Rendering.RayTracingTextureCapacity.Calculate(perStage, descriptorSet));
    }

    [Fact]
    public void Compile_RtFilterAndComposite_ReflectPushConstants()
    {
        SlangShaderCompiler compiler = CreateCompiler();
        string repositoryRoot = FindRepositoryRoot();
        ShaderProgramDescription filter = compiler.CompileProgram(
            Path.Combine(repositoryRoot, "Samples", "Basic3D", "Shaders", "RtFilter.slang"),
            [(ShaderStage.Compute, "csMain")]);
        ShaderProgramDescription composite = compiler.CompileProgram(
            Path.Combine(repositoryRoot, "Samples", "Basic3D", "Shaders", "RtComposite.slang"),
            [(ShaderStage.Compute, "csMain")]);

        Assert.Equal(56u, Assert.Single(filter.Reflection.PushConstants).Size);
        Assert.Equal(24u, Assert.Single(composite.Reflection.PushConstants).Size);
    }

    [Fact]
    public void Compile_Ssgi_ReflectsBindings()
    {
        SlangShaderCompiler compiler = CreateCompiler();
        string shaderPath = Path.Combine(FindRepositoryRoot(), "Samples", "Basic3D", "Shaders", "SSGI.slang");

        ShaderProgramDescription program = compiler.CompileProgram(shaderPath, [(ShaderStage.Compute, "csMain")]);

        DescriptorSetLayout set = Assert.Single(program.Reflection.DescriptorSets);
        Assert.Equal(4, set.Bindings.Count);
        Assert.Equal(DescriptorType.UniformBuffer, Assert.Single(set.Bindings, binding => binding.Binding == 0).Type);
        Assert.Equal(DescriptorType.SampledImage, Assert.Single(set.Bindings, binding => binding.Binding == 1).Type);
        Assert.Equal(DescriptorType.SampledImage, Assert.Single(set.Bindings, binding => binding.Binding == 2).Type);
        Assert.Equal(DescriptorType.StorageImage, Assert.Single(set.Bindings, binding => binding.Binding == 3).Type);
        Assert.Empty(program.Reflection.PushConstants);
    }

    [Fact]
    public void Compile_GiComposite_ReflectsBindingsAndConstants()
    {
        SlangShaderCompiler compiler = CreateCompiler();
        string shaderPath = Path.Combine(FindRepositoryRoot(), "Samples", "Basic3D", "Shaders", "GIComposite.slang");

        ShaderProgramDescription program = compiler.CompileProgram(shaderPath, [(ShaderStage.Compute, "csMain")]);

        DescriptorSetLayout set = Assert.Single(program.Reflection.DescriptorSets);
        Assert.Equal(4, set.Bindings.Count);
        Assert.Equal(DescriptorType.SampledImage, Assert.Single(set.Bindings, binding => binding.Binding == 0).Type);
        Assert.Equal(DescriptorType.SampledImage, Assert.Single(set.Bindings, binding => binding.Binding == 1).Type);
        Assert.Equal(DescriptorType.StorageImage, Assert.Single(set.Bindings, binding => binding.Binding == 2).Type);
        Assert.Equal(DescriptorType.SampledImage, Assert.Single(set.Bindings, binding => binding.Binding == 3).Type);
        Assert.Equal(16u, Assert.Single(program.Reflection.PushConstants).Size);
    }

    public static TheoryData<ShaderStage, ShaderStages, uint> RayStages => new()
    {
        { ShaderStage.RayGeneration, ShaderStages.RayGeneration, 5313 },
        { ShaderStage.Intersection, ShaderStages.Intersection, 5314 },
        { ShaderStage.AnyHit, ShaderStages.AnyHit, 5315 },
        { ShaderStage.ClosestHit, ShaderStages.ClosestHit, 5316 },
        { ShaderStage.Miss, ShaderStages.Miss, 5317 },
        { ShaderStage.Callable, ShaderStages.Callable, 5318 },
    };

    [Theory]
    [MemberData(nameof(RayStages))]
    public void Parse_SyntheticRayStage_ReflectsAccelerationStructureAndFlags(ShaderStage stage, ShaderStages flags, uint model)
    {
        byte[] module = Module(
            Entry(model, "actualRayEntry", 20),
            Instruction(5341, 1),
            Instruction(32, 2, 0, 1),
            Instruction(59, 2, 20, 0),
            Instruction(71, 20, 34, 4),
            Instruction(71, 20, 33, 7));
        Assert.Equal("actualRayEntry", SpirVReflectionParser.GetEntryPoint(module, stage, "sourceEntry"));
        DescriptorSetLayout set = Assert.Single(SpirVReflectionParser.Parse(module, stage).DescriptorSets);
        Assert.Equal(4u, set.SetIndex);
        DescriptorBinding binding = Assert.Single(set.Bindings);
        Assert.Equal(7u, binding.Binding);
        Assert.Equal(DescriptorType.AccelerationStructure, binding.Type);
        Assert.Equal(flags, binding.Stages);
    }

    [Theory]
    [InlineData(DescriptorType.AccelerationStructure)]
    [InlineData(DescriptorType.Sampler)]
    [InlineData(DescriptorType.SampledImage)]
    [InlineData(DescriptorType.StorageImage)]
    [InlineData(DescriptorType.CombinedImageSampler)]
    [InlineData(DescriptorType.UniformBuffer)]
    [InlineData(DescriptorType.StorageBuffer)]
    public void Parse_SyntheticNestedDescriptorArrays_ClassifiesElementAndMultipliesCount(DescriptorType type)
    {
        uint storage = type == DescriptorType.UniformBuffer ? 2u : type == DescriptorType.StorageBuffer ? 12u : 0u;
        uint[] element = type switch
        {
            DescriptorType.AccelerationStructure => Instruction(5341, 1),
            DescriptorType.Sampler => Instruction(26, 1),
            DescriptorType.SampledImage => Instruction(25, 1, 3, 1, 0, 0, 0, 1, 0),
            DescriptorType.StorageImage => Instruction(25, 1, 3, 1, 0, 0, 0, 2, 0),
            DescriptorType.CombinedImageSampler => Instruction(27, 1, 30),
            _ => Instruction(30, 1, 3),
        };
        byte[] module = Module(
            Entry(5313, "arrays", 20), Instruction(22, 3, 32), element,
            Instruction(21, 4, 32, 0), Instruction(43, 4, 5, 2), Instruction(43, 4, 6, 3),
            Instruction(28, 7, 1, 5), Instruction(28, 8, 7, 6),
            Instruction(32, 9, storage, 8), Instruction(59, 9, 20, storage),
            Instruction(71, 20, 34, 2), Instruction(71, 20, 33, 6));
        DescriptorBinding binding = Assert.Single(Assert.Single(SpirVReflectionParser.Parse(module, ShaderStage.RayGeneration).DescriptorSets).Bindings);
        Assert.Equal(type, binding.Type);
        Assert.Equal(6u, binding.ArrayCount);
    }

    [Fact]
    public void Parse_RuntimeDescriptorArray_RejectsUnsupportedVariableCount()
    {
        byte[] module = Module(Entry(5313, "runtime", 20), Instruction(5341, 1), Instruction(29, 2, 1),
            Instruction(32, 3, 0, 2), Instruction(59, 3, 20, 0), Instruction(71, 20, 33, 0));
        Assert.Throws<NotSupportedException>(() => SpirVReflectionParser.Parse(module, ShaderStage.RayGeneration));
    }

    [Fact]
    public void Parse_EntryPointSelection_UsesEmittedNameAndInterface()
    {
        byte[] module = Module(Entry(5317, "firstMiss", 20), Entry(5317, "secondMiss", 21),
            Instruction(5341, 1), Instruction(32, 2, 0, 1),
            Instruction(59, 2, 20, 0), Instruction(59, 2, 21, 0),
            Instruction(71, 20, 34, 0), Instruction(71, 20, 33, 0),
            Instruction(71, 21, 34, 4), Instruction(71, 21, 33, 3));
        Assert.Throws<ArgumentException>(() => SpirVReflectionParser.GetEntryPoint(module, ShaderStage.Miss));
        Assert.Throws<ArgumentException>(() => SpirVReflectionParser.GetEntryPoint(module, ShaderStage.Vertex));
        Assert.Equal("secondMiss", SpirVReflectionParser.GetEntryPoint(module, ShaderStage.Miss, "secondMiss"));
        DescriptorSetLayout set = Assert.Single(SpirVReflectionParser.Parse(module, ShaderStage.Miss, "secondMiss").DescriptorSets);
        Assert.Equal(4u, set.SetIndex);
        Assert.Equal(3u, Assert.Single(set.Bindings).Binding);
    }

    [Theory]
    [InlineData(32u, 0u, 4u)]
    [InlineData(32u, 1u, 4u)]
    [InlineData(64u, 0u, 8u)]
    [InlineData(16u, 1u, 2u)]
    public void Parse_IntegerPushConstant_UsesWidthNotSignedness(uint width, uint signed, uint expectedSize)
    {
        byte[] module = Module(Entry(5318, "integer", 20), Instruction(21, 1, width, signed),
            Instruction(30, 2, 1), Instruction(72, 2, 0, 35, 8),
            Instruction(32, 3, 9, 2), Instruction(59, 3, 20, 9));
        PushConstantRange range = Assert.Single(SpirVReflectionParser.Parse(module, ShaderStage.Callable).PushConstants);
        Assert.Equal(8u, range.Offset);
        Assert.Equal(expectedSize, range.Size);
        Assert.Equal(ShaderStages.Callable, range.Stages);
    }

    [Fact]
    public void Parse_Matrices_UsesEachMembersStrideAndMajorOrder()
    {
        byte[] module = Module(Entry(5, "matrix", 20), Instruction(22, 1, 32), Instruction(23, 2, 1, 3),
            Instruction(24, 3, 2, 2), Instruction(30, 4, 3, 3),
            Instruction(72, 4, 0, 35, 0), Instruction(72, 4, 0, 7, 16), Instruction(72, 4, 0, 5),
            Instruction(72, 4, 1, 35, 32), Instruction(72, 4, 1, 7, 8), Instruction(72, 4, 1, 4),
            Instruction(32, 5, 9, 4), Instruction(59, 5, 20, 9));
        Assert.Equal(56u, Assert.Single(SpirVReflectionParser.Parse(module, ShaderStage.Compute).PushConstants).Size);
    }

    [Fact]
    public void Parse_MalformedInstructions_RejectsRatherThanTruncating()
    {
        Assert.Throws<ArgumentException>(() => SpirVReflectionParser.Parse(Module([(2u << 16) | 5341u]), ShaderStage.RayGeneration));
        Assert.Throws<ArgumentException>(() => SpirVReflectionParser.Parse(Module([0u]), ShaderStage.RayGeneration));
        Assert.Throws<ArgumentException>(() => SpirVReflectionParser.Parse(Module(Instruction(21, 1)), ShaderStage.Compute));
        Assert.Throws<ArgumentException>(() => SpirVReflectionParser.Parse(new byte[21], ShaderStage.Compute));
    }

    [Theory]
    [InlineData(DescriptorType.Sampler, 2u)]
    [InlineData(DescriptorType.AccelerationStructure, 3u)]
    public void MergeReflections_IncompatibleBinding_Throws(DescriptorType secondType, uint secondCount)
    {
        PipelineReflection first = DescriptorReflection(DescriptorType.AccelerationStructure, 2, ShaderStages.RayGeneration);
        PipelineReflection second = DescriptorReflection(secondType, secondCount, ShaderStages.ClosestHit);
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => SlangShaderCompiler.MergeReflections([first, second]));
        Assert.Contains("set 4, binding 7", error.Message);
    }

    [Fact]
    public void Parse_IncompatibleBindingsInModule_Throws()
    {
        byte[] module = Module(Entry(5313, "aliases", 20, 21), Instruction(5341, 1), Instruction(26, 2),
            Instruction(32, 3, 0, 1), Instruction(32, 4, 0, 2), Instruction(59, 3, 20, 0), Instruction(59, 4, 21, 0),
            Instruction(71, 20, 33, 7), Instruction(71, 21, 33, 7), Instruction(71, 20, 34, 4), Instruction(71, 21, 34, 4));
        Assert.Throws<InvalidOperationException>(() => SpirVReflectionParser.Parse(module, ShaderStage.RayGeneration));
    }

    [Fact]
    public void Compile_RayTracingFixture_AllStagesAndDuplicateEntries_Validate()
    {
        SlangShaderCompiler compiler = CreateCompiler();
        (ShaderStage Stage, string EntryPoint)[] entries =
        [
            (ShaderStage.RayGeneration, "rayGeneration"),
            (ShaderStage.Miss, "radianceMiss"),
            (ShaderStage.Miss, "shadowMiss"),
            (ShaderStage.ClosestHit, "triangleClosestHit"),
            (ShaderStage.AnyHit, "triangleAnyHit"),
            (ShaderStage.Intersection, "sphereIntersection"),
            (ShaderStage.ClosestHit, "sphereClosestHit"),
            (ShaderStage.AnyHit, "sphereAnyHit"),
            (ShaderStage.Callable, "shadeMaterial"),
        ];
        ShaderProgramDescription program = compiler.CompileProgram(Fixture("RayTracingFoundation.slang"), entries,
            options: new ShaderCompilationOptions { PreserveEntryPointName = true });
        Assert.Equal(entries.Length, program.Stages.Count);
        for (int i = 0; i < entries.Length; i++)
        {
            Assert.Equal(entries[i].Stage, program.Stages[i].Stage);
            Assert.Equal(entries[i].EntryPoint, program.Stages[i].EntryPoint);
            Assert.True(BitConverter.ToUInt32(program.Stages[i].SpirV, 4) >= 0x00010400);
            ValidateSpirV(program.Stages[i]);
        }
        Assert.Equal(new uint[] { 0, 2, 4 }, program.Reflection.DescriptorSets.Select(s => s.SetIndex));
        DescriptorBinding scene = Assert.Single(program.Reflection.DescriptorSets.Single(s => s.SetIndex == 0).Bindings);
        Assert.Equal(DescriptorType.AccelerationStructure, scene.Type);
        Assert.Equal(ShaderStages.RayGeneration, scene.Stages);
        DescriptorSetLayout materials = program.Reflection.DescriptorSets.Single(s => s.SetIndex == 4);
        Assert.Equal(DescriptorType.StorageBuffer, materials.Bindings.Single(b => b.Binding == 1).Type);
        Assert.Equal(ShaderStages.ClosestHit | ShaderStages.Callable, materials.Bindings.Single(b => b.Binding == 1).Stages);
        Assert.Equal(2u, materials.Bindings.Single(b => b.Binding == 6).ArrayCount);
        Assert.Equal(DescriptorType.SampledImage, materials.Bindings.Single(b => b.Binding == 6).Type);
        Assert.Equal(2u, materials.Bindings.Single(b => b.Binding == 7).ArrayCount);
        Assert.Equal(DescriptorType.Sampler, materials.Bindings.Single(b => b.Binding == 7).Type);
        PushConstantRange push = Assert.Single(program.Reflection.PushConstants);
        Assert.Equal(16u, push.Size);
        Assert.Equal(ShaderStages.RayGeneration | ShaderStages.Miss, push.Stages);
    }

    [Fact]
    public void Compile_DescriptorArrays_ReflectsActualElementTypes()
    {
        ShaderCompilationResult result = CreateCompiler().Compile(new ShaderCompilationRequest
        {
            SourcePath = Fixture("DescriptorArrays.slang"), EntryPoint = "arrayRayGeneration", Stage = ShaderStage.RayGeneration,
        });
        ValidateSpirV(result.StageCode);
        DescriptorSetLayout scene = result.Reflection.DescriptorSets.Single(s => s.SetIndex == 0);
        Assert.Equal(DescriptorType.AccelerationStructure, Assert.Single(scene.Bindings).Type);
        Assert.Equal(2u, Assert.Single(scene.Bindings).ArrayCount);
        DescriptorBinding output = Assert.Single(result.Reflection.DescriptorSets.Single(s => s.SetIndex == 2).Bindings);
        Assert.Equal(DescriptorType.StorageImage, output.Type);
        Assert.Equal(2u, output.ArrayCount);
        DescriptorSetLayout set = result.Reflection.DescriptorSets.Single(s => s.SetIndex == 4);
        Assert.Equal(new[] { DescriptorType.SampledImage, DescriptorType.Sampler, DescriptorType.UniformBuffer, DescriptorType.StorageBuffer }, set.Bindings.Select(b => b.Type));
        Assert.Equal(new uint[] { 3, 2, 2, 2 }, set.Bindings.Select(b => b.ArrayCount));
    }

    [Fact]
    public void Compile_OptionsAndEmittedEntryPoint_InvalidateCache()
    {
        SlangShaderCompiler compiler = CreateCompiler();
        ShaderCompilationRequest request = new()
        {
            SourcePath = Fixture("RayTracingFoundation.slang"), EntryPoint = "radianceMiss", Stage = ShaderStage.Miss,
        };
        ShaderCompilationResult original = compiler.Compile(request);
        Assert.Equal("main", original.StageCode.EntryPoint);
        Assert.True(compiler.Compile(request).FromCache);
        ShaderCompilationRequest named = request with { Options = new() { PreserveEntryPointName = true } };
        ShaderCompilationResult preserved = compiler.Compile(named);
        Assert.False(preserved.FromCache);
        Assert.Equal("radianceMiss", preserved.StageCode.EntryPoint);
        Assert.True(compiler.Compile(named).FromCache);
        Assert.False(compiler.Compile(request with { Options = new() { Profile = "spirv_1_5" } }).FromCache);
        Assert.False(compiler.Compile(request with { Options = new() { OptimizationLevel = 0 } }).FromCache);
        Assert.False(compiler.Compile(request with { Options = new() { RowMajorMatrices = false } }).FromCache);
        ShaderCompilationRequest capabilities = request with { Options = new() { Capabilities = ["GL_EXT_ray_query"] } };
        Assert.False(compiler.Compile(capabilities).FromCache);
        Assert.True(compiler.Compile(capabilities).FromCache);
        Assert.Throws<ArgumentException>(() => compiler.Compile(request with { Options = new() { Profile = "spirv_1_3" } }));
        Assert.Throws<NotSupportedException>(() => compiler.Compile(request with { Options = new() { Target = "dxil" } }));
    }

    [Fact]
    public void Compile_IncludedFileChange_InvalidatesCache()
    {
        SlangShaderCompiler compiler = CreateCompiler();
        string includeDirectory = Path.Combine(_shaderDirectory, "include path with spaces");
        Directory.CreateDirectory(includeDirectory);
        string include = Path.Combine(includeDirectory, "Color.slang");
        File.WriteAllText(include, "float4 getColor() { return float4(1, 0, 0, 1); }");
        string source = Path.Combine(_shaderDirectory, "Included.slang");
        File.WriteAllText(source, "#include \"Color.slang\"\n[shader(\"fragment\")] float4 psMain() : SV_Target { return getColor(); }");
        ShaderCompilationRequest request = new()
        {
            SourcePath = source, EntryPoint = "psMain", Stage = ShaderStage.Fragment,
            Options = new() { IncludeDirectories = [includeDirectory] },
        };
        ShaderCompilationResult first = compiler.Compile(request);
        Assert.True(compiler.Compile(request).FromCache);
        File.WriteAllText(include, "float4 getColor() { return float4(0, 1, 0, 1); }");
        ShaderCompilationResult changed = compiler.Compile(request);
        Assert.False(changed.FromCache);
        Assert.NotEqual(first.StageCode.SpirV, changed.StageCode.SpirV);
    }

    [Fact]
    public void Compile_CompilerIdentityChange_InvalidatesCache()
    {
        string installedCompiler = SlangShaderCompiler.LocateSlangCompiler();
        string toolDirectory = Path.Combine(_shaderDirectory, "tool copy");
        Directory.CreateDirectory(toolDirectory);
        foreach (string file in Directory.GetFiles(Path.GetDirectoryName(installedCompiler)!))
            File.Copy(file, Path.Combine(toolDirectory, Path.GetFileName(file)));
        string copiedCompiler = Path.Combine(toolDirectory, Path.GetFileName(installedCompiler));
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(copiedCompiler, File.GetUnixFileMode(installedCompiler));
        SlangShaderCompiler compiler = new(cacheDirectory: Path.Combine(_shaderDirectory, "Cache"), slangcPath: copiedCompiler);
        ShaderCompilationRequest request = new()
        {
            SourcePath = Fixture("RayTracingFoundation.slang"), EntryPoint = "shadowMiss", Stage = ShaderStage.Miss,
        };
        ShaderCompilationResult first = compiler.Compile(request);
        Assert.True(compiler.Compile(request).FromCache);
        // An additional packaged Slang backend changes the compiler package identity, without
        // modifying the installed compiler or relying on a second installed version.
        string backend = Path.Combine(toolDirectory, OperatingSystem.IsWindows() ? "slang-test-backend.dll" : "slang-test-backend.so");
        File.WriteAllBytes(backend, [1, 2, 3, 4]);
        Assert.False(compiler.Compile(request).FromCache);
        Assert.True(compiler.Compile(request).FromCache);
        File.WriteAllBytes(backend, [1, 2, 3, 4, 5]);
        ShaderCompilationResult changed = compiler.Compile(request);
        Assert.False(changed.FromCache);
        Assert.Equal(first.StageCode.SpirV, changed.StageCode.SpirV);
    }

    [Fact]
    public void Compile_ConcurrentCachePublicationAndCorruptCache_Recovers()
    {
        SlangShaderCompiler compiler = CreateCompiler();
        ShaderCompilationRequest request = new()
        {
            SourcePath = Fixture("RayTracingFoundation.slang"), EntryPoint = "shadowMiss", Stage = ShaderStage.Miss,
        };
        ShaderCompilationResult[] results = new ShaderCompilationResult[6];
        Parallel.For(0, results.Length, i => results[i] = compiler.Compile(request));
        Assert.All(results, r => Assert.Equal(results[0].StageCode.SpirV, r.StageCode.SpirV));
        Assert.True(compiler.Compile(request).FromCache);
        string cache = Assert.Single(Directory.GetFiles(Path.Combine(_shaderDirectory, "Cache"), "*.spv"));
        Assert.Empty(Directory.GetFiles(Path.Combine(_shaderDirectory, "Cache"), "*.tmp"));
        File.WriteAllBytes(cache, [0, 1, 2, 3]);
        ShaderCompilationResult repaired = compiler.Compile(request);
        Assert.False(repaired.FromCache);
        Assert.Equal(results[0].StageCode.SpirV, repaired.StageCode.SpirV);
        Assert.True(compiler.Compile(request).FromCache);
    }

    private static PipelineReflection DescriptorReflection(DescriptorType type, uint count, ShaderStages stages) => new()
    {
        DescriptorSets = [new DescriptorSetLayout
        {
            SetIndex = 4,
            Bindings = [new DescriptorBinding { Binding = 7, Type = type, ArrayCount = count, Stages = stages }],
        }],
    };

    private static uint[] Instruction(uint opcode, params uint[] operands) => [((uint)operands.Length + 1) << 16 | opcode, .. operands];

    private static uint[] Entry(uint model, string name, params uint[] globals)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(name + '\0');
        Array.Resize(ref bytes, (bytes.Length + 3) / 4 * 4);
        uint[] literal = Enumerable.Range(0, bytes.Length / 4).Select(i => BitConverter.ToUInt32(bytes, i * 4)).ToArray();
        return Instruction(15, [model, 100, .. literal, .. globals]);
    }

    private static byte[] Module(params uint[][] instructions) => new uint[] { 0x07230203, 0x00010400, 0, 200, 0 }
        .Concat(instructions.SelectMany(words => words)).SelectMany(BitConverter.GetBytes).ToArray();

    private static string Fixture(string name) => Path.Combine(FindRepositoryRoot(), "Tests", "Engine.Tests.Rendering", "Shaders", name);

    private void ValidateSpirV(ShaderStageCode code)
    {
        string executable = OperatingSystem.IsWindows() ? "spirv-val.exe" : "spirv-val";
        string? validator = Environment.GetEnvironmentVariable("ENGINE_SPIRV_VAL");
        string? sdk = Environment.GetEnvironmentVariable("VULKAN_SDK");
        if (string.IsNullOrEmpty(validator) && !string.IsNullOrEmpty(sdk))
            validator = Path.Combine(sdk, "Bin", executable);
        if (string.IsNullOrEmpty(validator) || !File.Exists(validator))
        {
            validator = OperatingSystem.IsWindows() && File.Exists("C:/VulkanSDK/1.4.363.1/Bin/spirv-val.exe")
                ? "C:/VulkanSDK/1.4.363.1/Bin/spirv-val.exe"
                : (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)
                    .Select(path => Path.Combine(path.Trim('"'), executable)).FirstOrDefault(File.Exists);
        }
        Assert.True(validator is not null && File.Exists(validator), "spirv-val is required for RT integration tests. Set ENGINE_SPIRV_VAL or install Vulkan SDK.");
        string output = Path.Combine(_shaderDirectory, Guid.NewGuid().ToString("N") + ".spv");
        File.WriteAllBytes(output, code.SpirV);
        ProcessStartInfo info = new()
        {
            FileName = validator!, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true,
        };
        info.ArgumentList.Add("--target-env");
        info.ArgumentList.Add("vulkan1.2");
        info.ArgumentList.Add(output);
        using Process process = Process.Start(info)!;
        Task<string> stdout = process.StandardOutput.ReadToEndAsync();
        Task<string> stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(30000))
        {
            process.Kill(entireProcessTree: true);
            Assert.Fail("spirv-val timed out.");
        }
        Assert.True(process.ExitCode == 0, $"{code.Stage} {code.EntryPoint}: {stdout.Result}{stderr.Result}");
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Engine", "Engine.Shaders", "Engine.Shaders.csproj")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not locate the repository root.");
    }

    [Fact]
    public void Compile_InvalidShader_ThrowsWithCompilerOutput()
    {
        SlangShaderCompiler compiler = CreateCompiler();
        string sourcePath = Path.Combine(_shaderDirectory, "Broken.slang");
        File.WriteAllText(sourcePath, TestShader);
        ShaderCompilationRequest request = new()
        {
            SourcePath = sourcePath,
            EntryPoint = "vsMain",
            Stage = ShaderStage.Vertex,
        };
        ShaderCompilationException exception = Assert.Throws<ShaderCompilationException>(() => compiler.Compile(request));
        Assert.Contains("camera-less-projection", exception.CompilerOutput);
    }

    [Fact]
    public void Compile_MissingSource_ThrowsFileNotFound()
    {
        SlangShaderCompiler compiler = CreateCompiler();
        Assert.Throws<FileNotFoundException>(() => compiler.Compile(new ShaderCompilationRequest
        {
            SourcePath = Path.Combine(_shaderDirectory, "DoesNotExist.slang"),
            EntryPoint = "vsMain",
            Stage = ShaderStage.Vertex,
        }));
    }

    [Fact]
    public void MergeReflections_CombinesStages()
    {
        PipelineReflection vertex = new()
        {
            PushConstants = [new PushConstantRange { Offset = 0, Size = 64, Stages = ShaderStages.Vertex }],
            DescriptorSets =
            [
                new DescriptorSetLayout
                {
                    SetIndex = 0,
                    Bindings = [new DescriptorBinding { Binding = 0, Type = DescriptorType.UniformBuffer, Stages = ShaderStages.Vertex }],
                },
            ],
        };
        PipelineReflection fragment = new()
        {
            DescriptorSets =
            [
                new DescriptorSetLayout
                {
                    SetIndex = 0,
                    Bindings =
                    [
                        new DescriptorBinding { Binding = 0, Type = DescriptorType.UniformBuffer, Stages = ShaderStages.Fragment },
                        new DescriptorBinding { Binding = 1, Type = DescriptorType.SampledImage, Stages = ShaderStages.Fragment },
                    ],
                },
            ],
        };
        PipelineReflection merged = SlangShaderCompiler.MergeReflections([vertex, fragment]);
        DescriptorSetLayout set0 = Assert.Single(merged.DescriptorSets, s => s.SetIndex == 0);
        Assert.Equal(2, set0.Bindings.Count);
        DescriptorBinding binding0 = Assert.Single(set0.Bindings, b => b.Binding == 0);
        Assert.Equal(ShaderStages.Vertex | ShaderStages.Fragment, binding0.Stages);
        PushConstantRange push = Assert.Single(merged.PushConstants);
        Assert.Equal(64u, push.Size);
        Assert.Equal(ShaderStages.Vertex, push.Stages);
    }
}
