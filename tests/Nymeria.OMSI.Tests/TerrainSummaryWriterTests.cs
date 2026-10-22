using System.Reflection;
using Nymeria.Omsi.Rendering;
using Nymeria.Omsi.Terrain;
using Nymeria.Omsi.World;

namespace Nymeria.OMSI.Tests;

/// <summary>
/// Headless tests for the terrain mode's console contract and its embedded shader
/// resource names. No window, device, or SDL call is involved.
/// </summary>
public sealed class TerrainSummaryWriterTests
{
    [Fact]
    public void SummarySaysTerrainOnlyAndReportsGeometry()
    {
        (OmsiWorldModel world, OmsiTerrainSet terrain, OmsiTerrainModel model) =
            BuildModel();

        var writer = new StringWriter();
        TerrainSummaryWriter.Write(writer, world, terrain, model);
        string summary = writer.ToString();

        Assert.Contains("3D terrain only", summary, StringComparison.Ordinal);
        Assert.Contains("not rendered", summary, StringComparison.Ordinal);
        Assert.Contains("terrain tiles: 1 decoded", summary, StringComparison.Ordinal);
        Assert.Contains("61 x 61 samples", summary, StringComparison.Ordinal);
        Assert.Contains("5 m apart", summary, StringComparison.Ordinal);
        Assert.Contains("7200 triangles", summary, StringComparison.Ordinal);
        Assert.Contains("controls:", summary, StringComparison.Ordinal);
        Assert.Contains("right mouse button", summary, StringComparison.Ordinal);
    }

    [Fact]
    public void SummaryWithNoGeometrySaysSo()
    {
        using var directory = new TemporaryDirectory();
        directory.WriteUtf16Le("global.cfg", "[name]\r\nTest Map\r\n");
        directory.WriteUtf16Le("tile_0_0.map", "[version]\r\n12\r\n");

        OmsiWorldModel world = OmsiWorldLoader.Load(directory.Path);
        OmsiTerrainSet terrain = OmsiTerrainSet.Load(world);
        OmsiTerrainModel model = OmsiTerrainModel.Create(terrain);

        var writer = new StringWriter();
        TerrainSummaryWriter.Write(writer, world, terrain, model);
        string summary = writer.ToString();

        Assert.Contains("geometry: none", summary, StringComparison.Ordinal);
        Assert.Contains(
            "terrain diagnostics:",
            summary,
            StringComparison.Ordinal);
    }

    [Fact]
    public void TerrainShaderFamilyIsEmbeddedUnderTheNameTheEngineResolves()
    {
        Assembly assembly = typeof(ScenePresenter).Assembly;
        string expected = ScenePresenter.ShaderResourcePrefix + ".metal";

        Assert.Equal("Nymeria.OMSI.Shaders.Terrain.metal", expected);
        Assert.Contains(expected, assembly.GetManifestResourceNames());

        using Stream stream = assembly.GetManifestResourceStream(expected)
            ?? throw new InvalidOperationException($"{expected} is not embedded.");
        using var reader = new StreamReader(stream);
        string source = reader.ReadToEnd();

        // The engine's MSL naming convention: one resource holding both stages, with
        // the entry points and the vertex uniform buffer slot the presenter binds.
        Assert.Contains("vertex_main", source, StringComparison.Ordinal);
        Assert.Contains("fragment_main", source, StringComparison.Ordinal);
        Assert.Contains("[[buffer(0)]]", source, StringComparison.Ordinal);
        Assert.Contains("[[attribute(2)]]", source, StringComparison.Ordinal);
    }

    private static (OmsiWorldModel World, OmsiTerrainSet Terrain, OmsiTerrainModel Model)
        BuildModel()
    {
        using var directory = new TemporaryDirectory();
        directory.WriteUtf16Le("global.cfg", "[name]\r\nTest Map\r\n");
        directory.WriteUtf16Le("tile_0_0.map", "[version]\r\n12\r\n");
        directory.WriteRaw(
            "tile_0_0.map.terrain",
            TerrainDecoderTests.Payload(height: 2.0f));

        OmsiWorldModel world = OmsiWorldLoader.Load(directory.Path);
        OmsiTerrainSet terrain = OmsiTerrainSet.Load(world);
        OmsiTerrainModel model = OmsiTerrainModel.Create(terrain);

        return (world, terrain, model);
    }
}
