// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

namespace OpenVirtue.Host;

/// <summary>
/// Playback of the host's eight voices. The host decodes WAV and computes volume and pan;
/// the sink mixes and talks to the device. One voice occupies one channel.
/// </summary>
public interface IAudioSink
{
    /// <summary>Starts or replaces the voice on <paramref name="channel"/> (0..7).</summary>
    void StartVoice(int channel, ReadOnlyMemory<float> monoSamples, int sampleRate, float volume, float pan, bool looping);

    /// <summary>Updates the live volume and pan of a channel. No effect if it is silent.</summary>
    void UpdateVoice(int channel, float volume, float pan);

    /// <summary>Stops one channel.</summary>
    void StopChannel(int channel);

    /// <summary>Stops every channel.</summary>
    void StopAll();

    /// <summary>True while the channel still has samples to play.</summary>
    bool IsChannelPlaying(int channel);

    /// <summary>Plays a short diagnostic beep on top of the voices.</summary>
    void PlayBeep();
}
