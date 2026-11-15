using Engine.Core;
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

    private bool TryLocateCompiler(out SlangShaderCompiler compiler)
    {
        try
        {
            compiler = new SlangShaderCompiler(cacheDirectory: Path.Combine(_shaderDirectory, "Cache"));
            return true;
        }
        catch (FileNotFoundException)
        {
            compiler = null!;
            return false;
        }
    }

    [Fact]
    public void Compile_ProducesSpirVWithReflection()
    {
        if (!TryLocateCompiler(out SlangShaderCompiler compiler))
            return;
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
        if (!TryLocateCompiler(out SlangShaderCompiler compiler))
            return;
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
        if (!TryLocateCompiler(out SlangShaderCompiler compiler))
            return;
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
        if (!TryLocateCompiler(out SlangShaderCompiler compiler))
            return;
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
    public void Compile_InvalidShader_ThrowsWithCompilerOutput()
    {
        if (!TryLocateCompiler(out SlangShaderCompiler compiler))
            return;
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
        if (!TryLocateCompiler(out SlangShaderCompiler compiler))
            return;
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
