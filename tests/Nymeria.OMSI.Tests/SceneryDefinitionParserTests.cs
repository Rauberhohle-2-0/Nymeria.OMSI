using Nymeria.Omsi.Maps;
using Nymeria.Omsi.Scenery;

namespace Nymeria.OMSI.Tests;

/// <summary>
/// Headless tests for the supported <c>.sco</c> and model-configuration subset.
/// </summary>
/// <remarks>
/// Every fixture here is a short synthetic file written by the test. It exercises the
/// parser's rules, not real OMSI content: whether Grundorf's own objects use an external
/// <c>model.cfg</c> or inline model sections remains to be confirmed by the owner's
/// Windows run.
/// </remarks>
public sealed class SceneryDefinitionParserTests
{
    private const string ModelX = """
        xof 0303txt 0032
        """;

    [Fact]
    public void ReadsAnExternalModelConfiguration()
    {
        using Fixture fixture = Fixture.Create(
            sco: "[model]\r\nmodel.cfg\r\n[absheight]\r\n",
            modelConfig: "[mesh]\r\nbox.x\r\n[matl]\r\nwall.bmp\r\n");

        SceneryParseResult result = Parse(fixture);

        Assert.NotNull(result.Definition);

        SceneryDefinition definition = result.Definition!;

        Assert.True(definition.UsesExternalModelConfig);
        Assert.True(definition.UsesAbsoluteHeight);
        Assert.EndsWith("model.cfg", definition.ModelConfigPath, StringComparison.Ordinal);
        Assert.Single(definition.Meshes);
        Assert.True(definition.Meshes[0].IsResolved);
        Assert.EndsWith("box.x", definition.Meshes[0].Path, StringComparison.Ordinal);
        Assert.Single(definition.Textures);
        Assert.EndsWith("wall.bmp", definition.Textures[0].Path, StringComparison.Ordinal);
    }

    [Fact]
    public void ReadsInlineModelSections()
    {
        using Fixture fixture = Fixture.Create(
            sco: "[model]\r\n[mesh]\r\nbox.x\r\n[matl]\r\nwall.bmp\r\n",
            modelConfig: null);

        SceneryParseResult result = Parse(fixture);

        Assert.NotNull(result.Definition);
        Assert.False(result.Definition!.UsesExternalModelConfig);
        Assert.Null(result.Definition.ModelConfigPath);
        Assert.Single(result.Definition.Meshes);
        Assert.True(result.Definition.Meshes[0].IsResolved);
    }

    [Fact]
    public void ReadsInlineMeshesFromTheDocumentedModelAndTextureFolders()
    {
        using Fixture fixture = Fixture.Create(
            sco: "[mesh]\r\nbox.x\r\n[matl]\r\nwall.bmp\r\n",
            modelConfig: null);

        fixture.Delete("box.x");
        fixture.Delete("wall.bmp");
        fixture.WriteAt(Path.Combine("model", "box.x"), ModelX);
        fixture.WriteAt(Path.Combine("texture", "wall.bmp"), "bmp");

        SceneryParseResult result = Parse(fixture);

        Assert.NotNull(result.Definition);
        Assert.False(result.Definition!.UsesExternalModelConfig);
        Assert.EndsWith(
            Path.Combine("model", "box.x"),
            result.Definition.Meshes[0].Path,
            StringComparison.Ordinal);
        Assert.EndsWith(
            Path.Combine("texture", "wall.bmp"),
            result.Definition.Textures[0].Path,
            StringComparison.Ordinal);
    }

    [Fact]
    public void AcceptsAnInlineMeshWithoutAModelBlock()
    {
        using Fixture fixture = Fixture.Create(
            sco: "[mesh]\r\nbox.x\r\n",
            modelConfig: null);

        SceneryParseResult result = Parse(fixture);

        Assert.NotNull(result.Definition);
        Assert.DoesNotContain(
            result.Diagnostics,
            diagnostic => diagnostic.Diagnostic.Message.Contains(
                "no [model]",
                StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ReadsAMeshNamedDirectlyByTheModelBlock()
    {
        using Fixture fixture = Fixture.Create(
            sco: "[model]\r\nbox.x\r\n",
            modelConfig: null);

        SceneryParseResult result = Parse(fixture);

        Assert.NotNull(result.Definition);
        Assert.False(result.Definition!.UsesExternalModelConfig);
        Assert.Single(result.Definition.Meshes);
        Assert.True(result.Definition.Meshes[0].IsResolved);
    }

    [Fact]
    public void ReadsAParentReferenceThatStaysInsideTheContentRoot()
    {
        using Fixture fixture = Fixture.Create(
            sco: "[mesh]\r\n..\\Shared\\model\\box.x\r\n",
            modelConfig: null);

        string sharedDirectory = Path.Combine(
            fixture.Root.Path,
            "Sceneryobjects",
            "Shared",
            "model");

        Directory.CreateDirectory(sharedDirectory);
        File.WriteAllText(Path.Combine(sharedDirectory, "box.x"), ModelX);

        SceneryParseResult result = Parse(fixture);

        Assert.NotNull(result.Definition);
        Assert.True(result.Definition!.Meshes[0].IsResolved);
        Assert.EndsWith(
            Path.Combine("Shared", "model", "box.x"),
            result.Definition.Meshes[0].Path,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ResolvesAnExternalModelConfigurationInItsOwnFolder()
    {
        using Fixture fixture = Fixture.Create(
            sco: "[model]\r\nmodel\\model.cfg\r\n",
            modelConfig: null);

        fixture.Delete("box.x");
        fixture.Delete("wall.bmp");
        fixture.WriteAt(
            Path.Combine("model", "model.cfg"),
            "[mesh]\r\nbox.x\r\n[matl]\r\nwall.bmp\r\n");
        fixture.WriteAt(Path.Combine("model", "box.x"), ModelX);
        fixture.WriteAt(Path.Combine("texture", "wall.bmp"), "bmp");

        SceneryParseResult result = Parse(fixture);

        Assert.NotNull(result.Definition);
        Assert.True(result.Definition!.UsesExternalModelConfig);
        Assert.EndsWith(
            Path.Combine("Test", "model"),
            result.Definition.AssetDirectory,
            StringComparison.Ordinal);
        Assert.EndsWith(
            Path.Combine("Test", "model", "box.x"),
            result.Definition.Meshes[0].Path,
            StringComparison.Ordinal);
        Assert.EndsWith(
            Path.Combine("Test", "texture", "wall.bmp"),
            result.Definition.Textures[0].Path,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ResolvesATextureFromTheGlobalTextureFolder()
    {
        using Fixture fixture = Fixture.Create(
            sco: "[mesh]\r\nbox.x\r\n[matl]\r\nglobal.bmp\r\n",
            modelConfig: null);

        string globalDirectory = Path.Combine(fixture.Root.Path, "Texture");
        Directory.CreateDirectory(globalDirectory);
        File.WriteAllText(Path.Combine(globalDirectory, "global.bmp"), "bmp");

        SceneryParseResult result = Parse(fixture);

        Assert.NotNull(result.Definition);
        Assert.EndsWith(
            Path.Combine("Texture", "global.bmp"),
            result.Definition!.Textures[0].Path,
            StringComparison.Ordinal);
    }

    [Fact]
    public void PrefersADdsTextureVariant()
    {
        using Fixture fixture = Fixture.Create(
            sco: "[mesh]\r\nbox.x\r\n[matl]\r\nwall.bmp\r\n",
            modelConfig: null);

        fixture.Write("wall.dds", "dds");

        SceneryParseResult result = Parse(fixture);

        Assert.NotNull(result.Definition);
        Assert.EndsWith(
            "wall.dds",
            result.Definition!.Textures[0].Path,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReportsAMissingTextureWithoutThrowing()
    {
        using Fixture fixture = Fixture.Create(
            sco: "[mesh]\r\nbox.x\r\n[matl]\r\nabsent.dds\r\n",
            modelConfig: null);

        SceneryParseResult result = Parse(fixture);

        Assert.NotNull(result.Definition);
        Assert.Null(result.Definition!.Textures[0].Path);
        Assert.Contains("not found", result.Definition.Textures[0].Error, StringComparison.Ordinal);
    }

    [Fact]
    public void IgnoresSwitchedOffBlocksAndCommentLines()
    {
        using Fixture fixture = Fixture.Create(
            sco: "[model]\r\nmodel.cfg\r\n",
            modelConfig: """
                -<DISABLED>-
                [mesh]
                missing.x
                -<ENABLED>-
                ' [mesh]
                ' ignored.x
                [mesh]
                box.x
                """);

        SceneryParseResult result = Parse(fixture);

        Assert.NotNull(result.Definition);
        Assert.Single(result.Definition!.Meshes);
        Assert.EndsWith("box.x", result.Definition.Meshes[0].Path, StringComparison.Ordinal);
    }

    [Fact]
    public void IndentedKeywordsAreNotBlocks()
    {
        using Fixture fixture = Fixture.Create(
            sco: "[model]\r\nmodel.cfg\r\n",
            modelConfig: """
                [mesh]
                box.x
                  [mesh]
                  help-text.x
                """);

        SceneryParseResult result = Parse(fixture);

        Assert.NotNull(result.Definition);
        Assert.Single(result.Definition!.Meshes);
    }

    [Fact]
    public void CountsUnsupportedKeywordsWithoutInventingMeaning()
    {
        using Fixture fixture = Fixture.Create(
            sco: "[model]\r\nmodel.cfg\r\n[script]\r\nscript.osc\r\n[animations]\r\n",
            modelConfig: "[mesh]\r\nbox.x\r\n");

        SceneryParseResult result = Parse(fixture);

        Assert.NotNull(result.Definition);
        Assert.Equal(2, result.Definition!.UnsupportedKeywordCount);
        Assert.Contains(
            result.Definition.UnsupportedKeywords,
            keyword => keyword.Keyword == "script" && keyword.Occurrences == 1);
        Assert.Contains(
            result.Definition.UnsupportedKeywords,
            keyword => keyword.Keyword == "animations" && keyword.Occurrences == 1);
    }

    [Fact]
    public void ReportsAnUnsupportedMeshExtension()
    {
        using Fixture fixture = Fixture.Create(
            sco: "[model]\r\nmodel.cfg\r\n",
            modelConfig: "[mesh]\r\nbox.obj\r\n");

        fixture.Write("box.obj", "v 0 0 0\r\n");

        SceneryParseResult result = Parse(fixture);

        Assert.NotNull(result.Definition);
        Assert.False(result.Definition!.Meshes[0].IsResolved);
        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.Diagnostic.Message.Contains(
                "does not decode",
                StringComparison.Ordinal));
    }

    [Fact]
    public void RejectsATraversingMeshReference()
    {
        using Fixture fixture = Fixture.Create(
            sco: "[model]\r\nmodel.cfg\r\n",
            modelConfig: "[mesh]\r\n..\\..\\..\\Windows\\notepad.exe\r\n");

        SceneryParseResult result = Parse(fixture);

        Assert.NotNull(result.Definition);
        Assert.False(result.Definition!.Meshes[0].IsResolved);
        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.Diagnostic.Message.Contains("..", StringComparison.Ordinal));
    }

    [Fact]
    public void RejectsAMeshReferenceThatClimbsAboveTheContentRoot()
    {
        using Fixture fixture = Fixture.Create(
            sco: "[mesh]\r\n..\\..\\..\\outside.x\r\n",
            modelConfig: null);

        SceneryParseResult result = Parse(fixture);

        Assert.NotNull(result.Definition);
        Assert.False(result.Definition!.Meshes[0].IsResolved);
        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.Diagnostic.Message.Contains(
                "climbs above",
                StringComparison.Ordinal));
    }

    [Fact]
    public void RejectsADefinitionWithoutGeometry()
    {
        using Fixture fixture = Fixture.Create(
            sco: "[model]\r\nmodel.cfg\r\n",
            modelConfig: "[script]\r\nscript.osc\r\n");

        SceneryParseResult result = Parse(fixture);

        Assert.Null(result.Definition);
        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.Diagnostic.Severity == MapDiagnosticSeverity.Error);
    }

    [Fact]
    public void ReportsAMissingDefinitionFile()
    {
        using var root = new TemporaryDirectory();

        SceneryParseResult result = SceneryDefinitionParser.Parse(
            Path.Combine(root.Path, "absent.sco"),
            "Sceneryobjects/Absent/absent.sco",
            root.Path);

        Assert.Null(result.Definition);
        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.Diagnostic.Severity == MapDiagnosticSeverity.Error);
    }

    [Fact]
    public void ReportsAMissingModelConfiguration()
    {
        using Fixture fixture = Fixture.Create(
            sco: "[model]\r\nmissing.cfg\r\n",
            modelConfig: null);

        SceneryParseResult result = Parse(fixture);

        Assert.Null(result.Definition);
        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.Diagnostic.Message.Contains(
                "no geometry",
                StringComparison.Ordinal) ||
                diagnostic.Diagnostic.Message.Contains("could not be resolved", StringComparison.Ordinal));
    }

    private static SceneryParseResult Parse(Fixture fixture)
    {
        return SceneryDefinitionParser.Parse(
            fixture.ScoPath,
            "Sceneryobjects/Test/object.sco",
            fixture.Root.Path);
    }

    private sealed class Fixture : IDisposable
    {
        private Fixture(TemporaryDirectory root, string scoPath)
        {
            Root = root;
            ScoPath = scoPath;
        }

        internal TemporaryDirectory Root { get; }

        internal string ScoPath { get; }

        internal static Fixture Create(string sco, string? modelConfig)
        {
            var root = new TemporaryDirectory();
            string directory = Path.Combine(root.Path, "Sceneryobjects", "Test");
            Directory.CreateDirectory(directory);

            string scoPath = Path.Combine(directory, "object.sco");
            File.WriteAllText(scoPath, sco);

            if (modelConfig is not null)
            {
                File.WriteAllText(Path.Combine(directory, "model.cfg"), modelConfig);
            }

            File.WriteAllText(Path.Combine(directory, "box.x"), ModelX);
            File.WriteAllText(Path.Combine(directory, "wall.bmp"), "bmp");

            return new Fixture(root, scoPath);
        }

        internal void Write(string fileName, string content)
        {
            WriteAt(fileName, content);
        }

        internal void WriteAt(string relativePath, string content)
        {
            string directory = Path.GetDirectoryName(ScoPath)!;
            string path = Path.Combine(directory, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
        }

        internal void Delete(string fileName)
        {
            string directory = Path.GetDirectoryName(ScoPath)!;
            File.Delete(Path.Combine(directory, fileName));
        }

        public void Dispose()
        {
            Root.Dispose();
        }
    }
}
