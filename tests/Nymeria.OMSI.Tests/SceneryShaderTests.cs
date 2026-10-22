using System.Reflection;
using Nymeria.Omsi.Rendering;

namespace Nymeria.OMSI.Tests;

/// <summary>
/// Headless tests that the scenery shader family is embedded under the names the
/// engine resolves, and that it declares the bindings the renderer sets.
/// </summary>
/// <remarks>
/// The Metal source is the artifact a macOS build actually uses; the HLSL source is
/// compiled to DXIL only on Windows, so its compilation is not verified here.
/// </remarks>
public sealed class SceneryShaderTests
{
    [Fact]
    public void TheMetalSceneryShaderIsEmbeddedUnderTheEngineName()
    {
        Assembly assembly = typeof(SceneryRenderer).Assembly;
        string expected = SceneryRenderer.ShaderResourcePrefix + ".metal";

        Assert.Equal("Nymeria.OMSI.Shaders.Scenery.metal", expected);
        Assert.Contains(expected, assembly.GetManifestResourceNames());
    }

    [Fact]
    public void TheMetalSceneryShaderDeclaresTheBindingsTheRendererSets()
    {
        string source = ReadShader();

        Assert.Contains("vertex_main", source, StringComparison.Ordinal);
        Assert.Contains("fragment_main", source, StringComparison.Ordinal);

        // Two vertex uniform slots: the frame matrix and the model matrix.
        Assert.Contains("[[buffer(0)]]", source, StringComparison.Ordinal);
        Assert.Contains("[[buffer(1)]]", source, StringComparison.Ordinal);

        // One fragment texture and one fragment sampler.
        Assert.Contains("[[texture(0)]]", source, StringComparison.Ordinal);
        Assert.Contains("[[sampler(0)]]", source, StringComparison.Ordinal);

        // Position, normal, and texture coordinate attributes.
        Assert.Contains("[[attribute(2)]]", source, StringComparison.Ordinal);
    }

    [Fact]
    public void TheMetalSceneryShaderAlphaTestsAndKeepsTheTerrainLightDirection()
    {
        string source = ReadShader();
        string hlsl = ReadSource("src/Nymeria.OMSI/Shaders/Scenery.hlsl");

        Assert.Contains("discard_fragment", source, StringComparison.Ordinal);
        Assert.Contains("discard;", hlsl, StringComparison.Ordinal);

        // Both files must agree on the fixed sun so the two views look consistent.
        foreach (string shader in new[] { source, hlsl })
        {
            Assert.Contains("normalize(float3(-0.45, 0.80, 0.35))", shader, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void TheShaderPrefixMatchesTheLogicalFamilyName()
    {
        Assert.Equal("Nymeria.OMSI.Shaders.Scenery", SceneryRenderer.ShaderResourcePrefix);
        Assert.NotEqual(ScenePresenter.ShaderResourcePrefix, SceneryRenderer.ShaderResourcePrefix);
    }

    private static string ReadShader()
    {
        Assembly assembly = typeof(SceneryRenderer).Assembly;
        string name = SceneryRenderer.ShaderResourcePrefix + ".metal";

        using Stream stream = assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"{name} is not embedded.");
        using var reader = new StreamReader(stream);

        return reader.ReadToEnd();
    }

    private static string ReadSource(string relativePath)
    {
        string? directory = AppContext.BaseDirectory;

        while (directory is not null)
        {
            string candidate = Path.Combine(directory, relativePath);

            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }

            directory = Path.GetDirectoryName(directory);
        }

        throw new FileNotFoundException(
            $"The repository source '{relativePath}' was not found above the test output.");
    }
}
