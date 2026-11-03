// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using OpenVirtue.Engine.Interpreter;

namespace OpenVirtue.Engine.Tests.Interpreter;

/// <summary>
/// The runtime's <c>RANDOM</c> generator: unseeded by default, or the seeded one a host passes for a
/// reproducible run (the oracle-diff replay, where round2 anim-random differed on every run).
/// </summary>
public class WdlRuntimeRandomTests
{
    private const string MinimalMap =
        "VERTEX 0 0 0;#0\nVERTEX 1 0 0;#1\nREGION r 0 10;#0\nPLAYER_START 0 0 0 0;#1";

    [Fact]
    public void Random_SameSeed_DrawsTheSameValues()
    {
        WdlRuntime first = Create(new Random(7));
        WdlRuntime second = Create(new Random(7));

        double[] a = [.. Enumerable.Range(0, 5).Select(_ => first.Random(10))];
        double[] b = [.. Enumerable.Range(0, 5).Select(_ => second.Random(10))];

        Assert.True(first.RandomSeeded);
        Assert.Equal(a, b);
    }

    [Fact]
    public void Randomize_SeededRuntime_StaysReproducible()
    {
        WdlRuntime first = Create(new Random(3));
        WdlRuntime second = Create(new Random(3));

        first.Randomize();
        second.Randomize();

        Assert.Equal(first.Random(1), second.Random(1));
    }

    [Fact]
    public void Constructor_NoGenerator_IsUnseeded()
    {
        Assert.False(Create(null).RandomSeeded);
    }

    [Fact]
    public void AnimatedTexture_RandomDelay_DrawsFromTheSeededGenerator()
    {
        // A looping texture's RANDOM factor is drawn as the runtime is built, so the seed must be in
        // place before then.
        const string Declarations =
            "BMAP p1, <p1.pcx>; BMAP p2, <p2.pcx>; " +
            "TEXTURE anim { CYCLES 2; BMAPS p1, p2; DELAY 2, 2; RANDOM 1; } " +
            "REGION r { FLOOR_TEX anim; }";
        WdlRuntime runtime = Create(new Random(11), Declarations);
        var fresh = new Random(11);
        fresh.NextDouble(); // the texture loop's one draw

        Assert.Equal(fresh.Next(), runtime.RandomGenerator.Next());
    }

    private static WdlRuntime Create(Random? random, string declarations = "")
    {
        var resources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["m.wmp"] = MinimalMap };
        Level level = LevelLoader.LoadCore(
            "x",
            "MAPFILE <m.wmp>; " + declarations,
            n => resources.GetValueOrDefault(Path.GetFileName(n)));
        return new WdlRuntime(level, random: random);
    }
}
