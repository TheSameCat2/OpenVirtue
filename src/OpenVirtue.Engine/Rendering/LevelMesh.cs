// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

namespace OpenVirtue.Engine.Rendering;

/// <summary>A batch of triangles that share one texture.</summary>
public sealed class MeshBatch
{
    public MeshBatch(
        string? texture,
        IReadOnlyList<RenderVertex> vertices,
        AcknexObject? animationSource = null,
        bool alphaTest = false)
    {
        Texture = texture;
        Vertices = vertices;
        AnimationSource = animationSource;
        AlphaTest = alphaTest;
    }

    /// <summary>The texture name for this batch (resolve via <see cref="Level.Textures"/>), or null for untextured.</summary>
    public string? Texture { get; }

    /// <summary>The wall or region whose live <c>CYCLE</c> selects this batch's bitmap phase.</summary>
    public AcknexObject? AnimationSource { get; }

    /// <summary>Whether palette index 0 is discarded for this material batch.</summary>
    public bool AlphaTest { get; }

    /// <summary>Triangle-list vertices (every three form a triangle).</summary>
    public IReadOnlyList<RenderVertex> Vertices { get; }
}

/// <summary>
/// Renderer-ready geometry for a level: triangle batches grouped by texture. This is
/// the GPU-agnostic input the Direct3D renderer uploads — it carries no graphics-API types.
/// </summary>
public sealed class LevelMesh
{
    public LevelMesh(IReadOnlyList<MeshBatch> batches) => Batches = batches;

    /// <summary>The texture-grouped triangle batches.</summary>
    public IReadOnlyList<MeshBatch> Batches { get; }

    /// <summary>Total vertex count across all batches.</summary>
    public int VertexCount => Batches.Sum(b => b.Vertices.Count);
}
