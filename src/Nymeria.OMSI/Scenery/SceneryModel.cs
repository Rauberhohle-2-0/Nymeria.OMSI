using System.Numerics;
using Nymeria.Core.Materials;
using Nymeria.Core.Models;

namespace Nymeria.Omsi.Scenery;

/// <summary>
/// One decoded mesh file, shared by every subset and placement that names it.
/// </summary>
/// <remarks>
/// The mesh is engine data (<see cref="MeshData"/>) plus the path it came from. GPU
/// buffers are not part of it: the renderer owns those and creates them once per
/// distinct mesh, so repeated placements share one upload.
/// </remarks>
internal sealed class SceneryMesh
{
    internal SceneryMesh(MeshData data, string sourcePath)
    {
        Data = data;
        SourcePath = sourcePath;
    }

    /// <summary>Gets the decoded geometry.</summary>
    internal MeshData Data { get; }

    /// <summary>Gets the resolved mesh file this geometry came from.</summary>
    internal string SourcePath { get; }
}

/// <summary>
/// One drawable subset of a scenery model: an index range of one mesh plus the
/// material that colors it.
/// </summary>
internal sealed class ScenerySubset
{
    internal ScenerySubset(
        SceneryMesh mesh,
        int indexOffset,
        int indexCount,
        Material material,
        string? materialName)
    {
        Mesh = mesh;
        IndexOffset = indexOffset;
        IndexCount = indexCount;
        Material = material;
        MaterialName = materialName;
    }

    /// <summary>Gets the mesh whose index buffer the subset draws from.</summary>
    internal SceneryMesh Mesh { get; }

    /// <summary>Gets the first index of the subset in the mesh's index buffer.</summary>
    internal int IndexOffset { get; }

    /// <summary>Gets the number of indices in the subset.</summary>
    internal int IndexCount { get; }

    /// <summary>Gets the engine material, including its optional base-color texture.</summary>
    internal Material Material { get; }

    /// <summary>
    /// Gets the opaque material name from the mesh, or <see langword="null"/> when the
    /// subset named none.
    /// </summary>
    internal string? MaterialName { get; }
}

/// <summary>
/// A loaded scenery definition: every subset of every mesh it names, ready to draw.
/// </summary>
/// <remarks>
/// A model is keyed by the resolved <c>.sco</c> path, so twenty identical copies of a
/// building decode and upload their geometry once. The model holds CPU data only; the
/// renderer binds its own GPU resources to the same object.
/// </remarks>
internal sealed class SceneryModel
{
    internal SceneryModel(
        string sourceReference,
        string definitionPath,
        IReadOnlyList<ScenerySubset> subsets,
        BoundingBox bounds,
        bool usesAbsoluteHeight)
    {
        SourceReference = sourceReference;
        DefinitionPath = definitionPath;
        Subsets = [.. subsets];
        Bounds = bounds;
        UsesAbsoluteHeight = usesAbsoluteHeight;
    }

    /// <summary>Gets the reference as written in the tile file.</summary>
    internal string SourceReference { get; }

    /// <summary>Gets the resolved <c>.sco</c> path used as the cache key.</summary>
    internal string DefinitionPath { get; }

    /// <summary>Gets the drawable subsets, in mesh and subset order.</summary>
    internal IReadOnlyList<ScenerySubset> Subsets { get; }

    /// <summary>Gets the model-space bounds of all geometry.</summary>
    internal BoundingBox Bounds { get; }

    /// <summary>
    /// Gets a value indicating whether the definition declares <c>[absheight]</c>, so
    /// placement heights are absolute rather than terrain-relative.
    /// </summary>
    internal bool UsesAbsoluteHeight { get; }

    /// <summary>Gets the total index count across every subset.</summary>
    internal int IndexCount
    {
        get
        {
            int total = 0;

            foreach (ScenerySubset subset in Subsets)
            {
                total += subset.IndexCount;
            }

            return total;
        }
    }
}

/// <summary>
/// One placement of a loaded model in the scene.
/// </summary>
/// <param name="Model">The shared model to draw.</param>
/// <param name="Transform">The placement's model-to-world matrix.</param>
/// <param name="TileColumn">The source tile column.</param>
/// <param name="TileRow">The source tile row.</param>
/// <param name="LineNumber">The placement's line in the tile file, for diagnostics.</param>
internal readonly record struct SceneryInstance(
    SceneryModel Model,
    Matrix4x4 Transform,
    int TileColumn,
    int TileRow,
    int LineNumber);
