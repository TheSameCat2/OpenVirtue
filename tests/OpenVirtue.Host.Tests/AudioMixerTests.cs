// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using OpenVirtue.Host;

namespace OpenVirtue.Host.Tests;

public sealed class AudioMixerTests
{
    [Fact]
    public void PanGains_FullLeft_SilencesTheRightChannel()
    {
        (float left, float right) = AudioMixer.PanGains(1f, -1f);
        Assert.Equal(1f, left);
        Assert.Equal(0f, right);
    }

    [Fact]
    public void PanGains_FullRight_SilencesTheLeftChannel()
    {
        (float left, float right) = AudioMixer.PanGains(1f, 1f);
        Assert.Equal(0f, left);
        Assert.Equal(1f, right);
    }

    [Fact]
    public void PanGains_Center_KeepsBothChannelsAtVolume()
    {
        (float left, float right) = AudioMixer.PanGains(0.5f, 0f);
        Assert.Equal(0.5f, left);
        Assert.Equal(0.5f, right);
    }

    [Theory]
    [InlineData(11025, 11111)]
    [InlineData(22050, 22222)]
    [InlineData(8000, 8000)]
    public void SoundBlasterRate_CutsTheSamplePeriodToWholeMicroseconds(int sampleRate, int played)
    {
        // OV-20261029-audio round 3: a one-second 11025 Hz sound freed its channel at TICKS 29.406.
        Assert.Equal(played, RuntimeHost.SoundBlasterRate(sampleRate));
    }
}
