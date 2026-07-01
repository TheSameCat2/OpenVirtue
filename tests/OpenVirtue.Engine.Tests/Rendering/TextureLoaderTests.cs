// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using OpenVirtue.Engine.Rendering;
using OpenVirtue.Engine.Tests.TestSupport;
using OpenVirtue.Formats.Wrs;

namespace OpenVirtue.Engine.Tests.Rendering;

public class TextureLoaderTests
{
    /// <summary>
    /// Decodes referenced textures for every local retail archive when user-supplied game
    /// data is present. No-op otherwise.
    /// </summary>
    [Fact]
    public void Load_LocalRetailArchiveTextures_DecodeToRgba()
    {
        IReadOnlyList<string> archives = ResearchData.WrsFiles();
        if (archives.Count == 0)
        {
            return;
        }

        foreach (string path in archives)
        {
            WrsArchive archive = WrsArchive.ReadFile(path);
            Level level = LevelLoader.Load(archive, ResearchData.MainWdlName(path));

            IReadOnlyDictionary<string, TextureImage> images = TextureLoader.Load(archive, level);

            Assert.NotEmpty(images);
            foreach ((_, TextureImage image) in images)
            {
                Assert.True(image.Width > 0 && image.Height > 0, $"{Path.GetFileName(path)}: image should have pixels");
                Assert.Equal(image.Width * image.Height * 4, image.Rgba.Length);
            }
        }
    }

    /// <summary>
    /// Loads the real apathy level's textures (when present): decodes each referenced PCX and
    /// crops to its BMAP rectangle. No-op when game data is absent.
    /// </summary>
    [Fact]
    public void Load_RealApathyTextures_DecodeToRgba()
    {
        string? apathy = ResearchData.WrsFiles()
            .FirstOrDefault(p => Path.GetFileName(p).Equals("apathy.wrs", StringComparison.OrdinalIgnoreCase));
        if (apathy is null)
        {
            return; // no local game data — skip
        }

        WrsArchive archive = WrsArchive.ReadFile(apathy);
        Level level = LevelLoader.Load(archive, "APATHY.WDL");

        IReadOnlyDictionary<string, TextureImage> images = TextureLoader.Load(archive, level);

        Assert.NotEmpty(images);
        foreach (TextureImage image in images.Values)
        {
            Assert.True(image.Width > 0 && image.Height > 0);
            Assert.Equal(image.Width * image.Height * 4, image.Rgba.Length);
        }
    }

}
