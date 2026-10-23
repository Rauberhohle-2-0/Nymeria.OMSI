using System.Numerics;
using Nymeria.Omsi.Maps;
using Nymeria.Omsi.Rendering;
using Nymeria.Omsi.Scenery;
using Nymeria.Omsi.Terrain;
using Nymeria.Omsi.World;

namespace Nymeria.OMSI.Tests;

/// <summary>
/// Headless tests for scenery loading, caching, and counted skips.
/// </summary>
/// <remarks>
/// The whole installation layout here is synthetic: a map folder, a Sceneryobjects
/// folder, a tiny text <c>.x</c> mesh, and a generated bitmap. It proves the loader's
/// resolution, caching, and counting rules. It is not OMSI content and says nothing
/// about whether a real Grundorf object renders.
/// </remarks>
public sealed class OmsiSceneryLibraryTests
{
    private const string Mesh = """
        xof 0303txt 0032

        Frame Root {
          FrameTransformMatrix {
            1.000000,0.000000,0.000000,0.000000,
            0.000000,1.000000,0.000000,0.000000,
            0.000000,0.000000,1.000000,0.000000,
            0.000000,0.000000,0.000000,1.000000;;
          }

          Mesh Box {
            3;
            -1.000000;0.000000;-1.000000;,
            1.000000;0.000000;-1.000000;,
            0.000000;2.000000;0.000000;;
            1;
            3;0,1,2;;

            MeshNormals {
              3;
              0.000000;1.000000;0.000000;,
              0.000000;1.000000;0.000000;,
              0.000000;1.000000;0.000000;;
              1;
              3;0,1,2;;
            }

            MeshTextureCoords {
              3;
              0.000000;0.000000;,
              1.000000;0.000000;,
              1.000000;1.000000;;
            }

            MeshMaterialList {
              1;
              1;
              0;;
              Material wall.bmp {
                1.000000;1.000000;1.000000;1.000000;;
                1.000000;
                0.000000;0.000000;0.000000;;
                0.000000;0.000000;0.000000;;
                TextureFilename {
                  "wall.bmp";
                }
              }
            }
          }
        }
        """;

    [Fact]
    public void LoadsOneModelAndTwoInstancesFromOneDefinition()
    {
        using Installation installation = Installation.Create(objectPlacements: 2);

        OmsiWorldModel world = OmsiWorldLoader.Load(installation.MapDirectory);
        OmsiTerrainSet terrain = OmsiTerrainSet.Load(world);
        OmsiSceneryLibrary scene = OmsiSceneryLibrary.Load(
            world,
            TerrainHeightSampler.Create(terrain),
            installation.RootPath);

        Assert.Equal(2, scene.Counts.Placements.Considered);
        Assert.Equal(2, scene.Counts.Placements.Instanced);
        Assert.Equal(1, scene.Counts.Definitions.Loaded);
        Assert.Equal(2, scene.Instances.Count);
        Assert.Same(scene.Instances[0].Model, scene.Instances[1].Model);
        Assert.Equal(1, scene.Counts.Meshes.Decoded);
        Assert.Equal(0, scene.Counts.Placements.Failures[SceneryDiagnosticClass.MissingAsset]);
    }

    [Fact]
    public void ParsesABrokenDefinitionOnceAndCountsEverySkippedPlacement()
    {
        using Installation installation = Installation.Create(
            objectPlacements: 2,
            scoContent: "[script]\r\nscript.osc\r\n[mesh]\r\nmissing.x\r\n",
            writeMesh: false,
            writeTexture: false);

        OmsiWorldModel world = OmsiWorldLoader.Load(installation.MapDirectory);
        OmsiTerrainSet terrain = OmsiTerrainSet.Load(world);
        OmsiSceneryLibrary scene = OmsiSceneryLibrary.Load(
            world,
            TerrainHeightSampler.Create(terrain),
            installation.RootPath);

        Assert.Empty(scene.Instances);
        Assert.Equal(2, scene.Counts.Placements.Considered);
        Assert.Equal(0, scene.Counts.Definitions.Loaded);

        // The definition is parsed once, so the unsupported keyword occurs once, while
        // both placements are still counted as failures and the one broken definition is
        // counted once.
        Assert.Equal(1, scene.Counts.UnsupportedKeywords);
        Assert.Equal(1, scene.Counts.Definitions.WithoutGeometry);
        Assert.Equal(2, scene.Counts.Placements.Failures[SceneryDiagnosticClass.MissingAsset]);
        Assert.Equal(2, scene.Counts.Placements.Failures.Total);

        Assert.Single(
            scene.Diagnostics,
            diagnostic => diagnostic.Diagnostic.Message.Contains(
                "could not be resolved",
                StringComparison.Ordinal));
    }

    [Fact]
    public void ReportsAMissingTextureReachedByADrawnSubset()
    {
        using Installation installation = Installation.Create(
            objectPlacements: 1,
            writeTexture: false);

        OmsiWorldModel world = OmsiWorldLoader.Load(installation.MapDirectory);
        OmsiTerrainSet terrain = OmsiTerrainSet.Load(world);
        OmsiSceneryLibrary scene = OmsiSceneryLibrary.Load(
            world,
            TerrainHeightSampler.Create(terrain),
            installation.RootPath);

        Assert.Single(scene.Instances);
        Assert.Equal(1, scene.Counts.Subsets.WithoutTexture);
        Assert.Contains(
            scene.Diagnostics,
            diagnostic => diagnostic.Kind == SceneryDiagnosticClass.MissingAsset &&
                diagnostic.Diagnostic.Message.Contains(
                    "The texture 'wall.bmp' declared by [matl] could not be resolved",
                    StringComparison.Ordinal));
    }

    [Fact]
    public void PlacesTerrainRelativeObjectsOnTheDecodedGround()
    {
        using Installation installation = Installation.Create(objectPlacements: 1);

        OmsiWorldModel world = OmsiWorldLoader.Load(installation.MapDirectory);
        OmsiTerrainSet terrain = OmsiTerrainSet.Load(world);
        OmsiSceneryLibrary scene = OmsiSceneryLibrary.Load(
            world,
            TerrainHeightSampler.Create(terrain),
            installation.RootPath);

        SceneryInstance instance = Assert.Single(scene.Instances);

        // The tile is flat at 3 m and the placement z is 1 m: the object's origin is
        // at 4 m, not at 1 m and not snapped to the ground.
        Assert.Equal(4.0f, instance.Transform.Translation.Y, precision: 3);
        Assert.Equal(0, scene.Counts.Placements.WithoutTerrain);
    }

    [Fact]
    public void SkipsTerrainRelativeObjectsWhenNoTerrainCoversThem()
    {
        using Installation installation = Installation.Create(objectPlacements: 1);

        OmsiWorldModel world = OmsiWorldLoader.Load(installation.MapDirectory);
        OmsiSceneryLibrary scene = OmsiSceneryLibrary.Load(
            world,
            terrain: null,
            installation.RootPath);

        Assert.Empty(scene.Instances);
        Assert.Equal(1, scene.Counts.Placements.WithoutTerrain);
        Assert.Contains(
            scene.Diagnostics,
            diagnostic => diagnostic.Diagnostic.Message.Contains(
                "terrain-relative",
                StringComparison.Ordinal));
    }

    [Fact]
    public void CountsUnresolvedReferencesWithTheirTileAndLine()
    {
        using Installation installation = Installation.Create(
            objectPlacements: 1,
            objectReference: @"Sceneryobjects\Absent\missing.sco");

        OmsiWorldModel world = OmsiWorldLoader.Load(installation.MapDirectory);
        OmsiTerrainSet terrain = OmsiTerrainSet.Load(world);
        OmsiSceneryLibrary scene = OmsiSceneryLibrary.Load(
            world,
            TerrainHeightSampler.Create(terrain),
            installation.RootPath);

        Assert.Empty(scene.Instances);
        Assert.Equal(1, scene.Counts.Placements.Failures[SceneryDiagnosticClass.MissingAsset]);
        Assert.Contains(
            scene.Diagnostics,
            diagnostic => diagnostic.Diagnostic.Message.Contains(
                "could not be resolved",
                StringComparison.Ordinal) &&
                diagnostic.Diagnostic.LineNumber is not null);
    }

    [Fact]
    public void RefusesToResolveAnythingWithoutAContentRoot()
    {
        using Installation installation = Installation.Create(objectPlacements: 1);

        OmsiWorldModel world = OmsiWorldLoader.Load(installation.MapDirectory);

        OmsiSceneryLibrary scene = OmsiSceneryLibrary.Load(
            world,
            terrain: null,
            contentRoot: null);

        Assert.Empty(scene.Instances);
        Assert.Equal(0, scene.Counts.Placements.Considered);
        Assert.Contains(
            scene.Diagnostics,
            diagnostic => diagnostic.Diagnostic.Message.Contains(
                "installation root",
                StringComparison.Ordinal));
    }

    [Fact]
    public void LoadedModelsUseTheEngineTextureForTheSubset()
    {
        using Installation installation = Installation.Create(objectPlacements: 1);

        OmsiWorldModel world = OmsiWorldLoader.Load(installation.MapDirectory);
        OmsiTerrainSet terrain = OmsiTerrainSet.Load(world);
        OmsiSceneryLibrary scene = OmsiSceneryLibrary.Load(
            world,
            TerrainHeightSampler.Create(terrain),
            installation.RootPath);

        SceneryModel model = Assert.Single(scene.Models);

        Assert.NotEmpty(model.Subsets);
        Assert.All(model.Subsets, subset => Assert.True(subset.IndexCount > 0));
        Assert.Contains(
            model.Subsets,
            subset => subset.Material.BaseColorTexture is not null);

        Assert.True(scene.Counts.Textures.Decoded > 0);
    }

    [Fact]
    public void SummarizesALoadedSceneWithoutClaimingCompleteness()
    {
        using Installation installation = Installation.Create(objectPlacements: 2);

        OmsiWorldModel world = OmsiWorldLoader.Load(installation.MapDirectory);
        OmsiTerrainSet terrain = OmsiTerrainSet.Load(world);
        OmsiSceneryLibrary scene = OmsiSceneryLibrary.Load(
            world,
            TerrainHeightSampler.Create(terrain),
            installation.RootPath);

        using var writer = new StringWriter();

        ScenerySummaryWriter.Write(
            writer,
            world,
            terrain,
            installation.RootPath,
            scene.Counts,
            scene.Diagnostics,
            scene.UnsupportedKeywords);

        string text = writer.ToString();

        Assert.StartsWith("scene: incomplete — 2 of 2 placement(s) instanced", text);
        Assert.Contains(
            "definitions: 1 loaded, 1 with drawable geometry, 0 without",
            text,
            StringComparison.Ordinal);
        Assert.Contains("meshes: 1 file(s) decoded, 0 skipped (none)", text, StringComparison.Ordinal);
        Assert.Contains("textures: 1 decoded, 0 skipped (none)", text, StringComparison.Ordinal);
        Assert.Contains("placement skips: none", text, StringComparison.Ordinal);
        Assert.Contains("unsupported definition keywords: none", text, StringComparison.Ordinal);
        Assert.Contains(
            "asset roots: " + installation.RootPath,
            text,
            StringComparison.Ordinal);
        Assert.DoesNotContain("scene: complete", text, StringComparison.Ordinal);
    }

    [Fact]
    public void InstanceTransformsUseTheTileOrigin()
    {
        using Installation installation = Installation.Create(objectPlacements: 1);

        OmsiWorldModel world = OmsiWorldLoader.Load(installation.MapDirectory);
        OmsiTerrainSet terrain = OmsiTerrainSet.Load(world);
        OmsiSceneryLibrary scene = OmsiSceneryLibrary.Load(
            world,
            TerrainHeightSampler.Create(terrain),
            installation.RootPath);

        SceneryInstance instance = Assert.Single(scene.Instances);
        Vector3 origin = instance.Transform.Translation;

        // Tile (0, 0), placement x = 12.5, y = 3.25, z = 1 above 3 m of ground.
        // OMSI +y is world -z.
        Assert.Equal(12.5f, origin.X, precision: 3);
        Assert.Equal(4.0f, origin.Y, precision: 3);
        Assert.Equal(-3.25f, origin.Z, precision: 3);
    }

    /// <summary>
    /// A synthetic installation: a map inside <c>maps</c> and one scenery object beside
    /// it, which is the layout the host's content-root derivation expects.
    /// </summary>
    private sealed class Installation : IDisposable
    {
        private Installation(
            TemporaryDirectory root,
            string mapDirectory,
            string scoPath)
        {
            Root = root;
            MapDirectory = mapDirectory;
            ScoPath = scoPath;
        }

        internal TemporaryDirectory Root { get; }

        internal string RootPath => Root.Path;

        internal string MapDirectory { get; }

        internal string ScoPath { get; }

        internal static Installation Create(
            int objectPlacements,
            string objectReference = @"Sceneryobjects\Test\object.sco",
            string? scoContent = null,
            bool writeMesh = true,
            bool writeTexture = true)
        {
            var root = new TemporaryDirectory();

            string mapDirectory = Path.Combine(root.Path, "maps", "TestMap");
            Directory.CreateDirectory(mapDirectory);

            File.WriteAllText(
                Path.Combine(mapDirectory, "global.cfg"),
                "[name]\r\nTest Map\r\n",
                System.Text.Encoding.Unicode);

            var tile = new System.Text.StringBuilder("[version]\r\n12\r\n");

            for (int index = 0; index < objectPlacements; index++)
            {
                tile.Append("[object]\r\n");
                tile.Append("0\r\n");
                tile.Append(objectReference).Append("\r\n");
                tile.Append("42\r\n");
                tile.Append("12.5\r\n");
                tile.Append("3.25\r\n");
                tile.Append("1\r\n");
                tile.Append("0\r\n");
                tile.Append("-1\r\n");
                tile.Append("-1\r\n");
                tile.Append("0\r\n");
            }

            File.WriteAllText(
                Path.Combine(mapDirectory, "tile_0_0.map"),
                tile.ToString(),
                System.Text.Encoding.Unicode);

            File.WriteAllBytes(
                Path.Combine(mapDirectory, "tile_0_0.map.terrain"),
                TerrainDecoderTests.Payload(height: 3.0f));

            string objectDirectory = Path.Combine(
                root.Path,
                "Sceneryobjects",
                "Test");

            string modelDirectory = Path.Combine(objectDirectory, "model");
            string textureDirectory = Path.Combine(objectDirectory, "texture");
            Directory.CreateDirectory(modelDirectory);
            Directory.CreateDirectory(textureDirectory);

            string scoPath = Path.Combine(objectDirectory, "object.sco");
            File.WriteAllText(
                scoPath,
                scoContent ?? "[mesh]\r\nbox.x\r\n[matl]\r\nwall.bmp\r\n");

            if (writeMesh)
            {
                File.WriteAllText(Path.Combine(modelDirectory, "box.x"), Mesh);
            }

            if (writeTexture)
            {
                File.WriteAllBytes(
                    Path.Combine(textureDirectory, "wall.bmp"),
                    Bitmap());
            }

            return new Installation(root, mapDirectory, scoPath);
        }

        /// <summary>
        /// Builds a 2 x 2, 24-bit uncompressed bitmap so the engine's own decoder has a
        /// real file to read.
        /// </summary>
        private static byte[] Bitmap()
        {
            const int width = 2;
            const int height = 2;
            int rowBytes = ((width * 3) + 3) / 4 * 4;
            int pixelBytes = rowBytes * height;
            var bytes = new byte[54 + pixelBytes];

            bytes[0] = (byte)'B';
            bytes[1] = (byte)'M';
            BitConverter.GetBytes(bytes.Length).CopyTo(bytes, 2);
            BitConverter.GetBytes(54).CopyTo(bytes, 10);
            BitConverter.GetBytes(40).CopyTo(bytes, 14);
            BitConverter.GetBytes(width).CopyTo(bytes, 18);
            BitConverter.GetBytes(height).CopyTo(bytes, 22);
            BitConverter.GetBytes((short)1).CopyTo(bytes, 26);
            BitConverter.GetBytes((short)24).CopyTo(bytes, 28);
            BitConverter.GetBytes(pixelBytes).CopyTo(bytes, 34);

            for (int index = 54; index < bytes.Length; index++)
            {
                bytes[index] = 0xC0;
            }

            return bytes;
        }

        public void Dispose()
        {
            Root.Dispose();
        }
    }
}
