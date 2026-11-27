using System.Text.Json;
using Engine.Editor.Mcp;
using Engine.Rendering;
using Engine.Scene;
using Light = Engine.Scene.Light;
using Xunit;

namespace Engine.Tests.Mcp;

public sealed class ComponentValueConverterTests
{
    [Fact]
    public void DescribeIncludesComponentProperties()
    {
        Light light = new() { Intensity = 2.5f, Kind = LightKind.Point };
        Engine.Mcp.McpComponentInfo info = ComponentValueConverter.Describe(light);
        Assert.Equal("Light", info.Type);
        Assert.Equal(2.5f, info.Properties["intensity"]);
        Assert.Equal("Point", info.Properties["kind"]);
    }

    [Fact]
    public void WriteAppliesScalarEnumAndVectorValues()
    {
        Light light = new();
        ComponentValueConverter.Write(light, "intensity", Json("""3.5"""));
        ComponentValueConverter.Write(light, "kind", Json(""" "Directional" """));
        ComponentValueConverter.Write(light, "color", Json("""{"x":0.2,"y":0.4,"z":0.8}"""));
        Assert.Equal(3.5f, light.Intensity);
        Assert.Equal(LightKind.Directional, light.Kind);
        Assert.Equal(0.4f, light.Color.Y);
    }

    [Fact]
    public void ReadReturnsConvertedValue()
    {
        MeshRenderer renderer = new() { Metallic = 0.25f };
        Assert.Equal(0.25f, ComponentValueConverter.Read(renderer, "metallic"));
        Assert.Null(ComponentValueConverter.Read(renderer, "albedoTexture"));
    }

    [Fact]
    public void UnknownPropertyOrTypeIsRejected()
    {
        Light light = new();
        Func<object?> unknown = () =>
        {
            ComponentValueConverter.Write(light, "doesNotExist", Json("1"));
            return null;
        };
        Func<object?> badValue = () =>
        {
            ComponentValueConverter.Write(light, "intensity", Json("{}"));
            return null;
        };
        Assert.Throws<ArgumentException>(unknown);
        Assert.Throws<ArgumentException>(badValue);
    }

    private static JsonElement Json(string text) => JsonSerializer.Deserialize<JsonElement>(text);
}
