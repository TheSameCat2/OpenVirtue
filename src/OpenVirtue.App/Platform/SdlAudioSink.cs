// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using OpenVirtue.Host;
using SDL;

namespace OpenVirtue.App.Platform;

/// <summary>
/// Plays <see cref="AudioMixer"/> into an SDL3 float32 device stream. The callback and the
/// frame loop share the mixer's lock. A device that cannot do stereo float32 is refused so
/// the app can fall back to <see cref="NullAudioSink"/>.
/// </summary>
public sealed unsafe class SdlAudioSink : IAudioSink, IDisposable
{
    private readonly AudioMixer _mixer = new();
    private readonly GCHandle _handle;
    private SDL_AudioStream* _stream;
    private bool _disposed;

    private SdlAudioSink()
    {
        _handle = GCHandle.Alloc(this);
    }

    public static bool TryCreate(out SdlAudioSink? sink, out string? error)
    {
        sink = null;
        error = null;
        var created = new SdlAudioSink();
        SDL_AudioSpec spec = default;
        spec.format = SDL_AudioFormat.SDL_AUDIO_F32LE;
        spec.channels = 2;
        spec.freq = 44100;
        SDL_AudioStream* stream = SDL3.SDL_OpenAudioDeviceStream(
            SDL3.SDL_AUDIO_DEVICE_DEFAULT_PLAYBACK,
            &spec,
            &AudioCallback,
            GCHandle.ToIntPtr(created._handle));
        if (stream == null)
        {
            error = SDL3.SDL_GetError();
            created.Dispose();
            return false;
        }

        if (spec.format != SDL_AudioFormat.SDL_AUDIO_F32LE || spec.channels != 2)
        {
            error = "SDL audio device did not accept stereo float32.";
            SDL3.SDL_DestroyAudioStream(stream);
            created.Dispose();
            return false;
        }

        created._mixer.OutputRate = spec.freq > 0 ? spec.freq : 44100;
        created._stream = stream;
        if (!SDL3.SDL_ResumeAudioStreamDevice(stream))
        {
            error = SDL3.SDL_GetError();
            created.Dispose();
            return false;
        }

        sink = created;
        return true;
    }

    public void StartVoice(int channel, ReadOnlyMemory<float> monoSamples, int sampleRate, float volume, float pan, bool looping) =>
        _mixer.StartVoice(channel, monoSamples, sampleRate, volume, pan, looping);

    public void UpdateVoice(int channel, float volume, float pan) => _mixer.UpdateVoice(channel, volume, pan);

    public void StopChannel(int channel) => _mixer.StopChannel(channel);

    public void StopAll() => _mixer.StopAll();

    public bool IsChannelPlaying(int channel) => _mixer.IsChannelPlaying(channel);

    public void PlayBeep() => _mixer.PlayBeep();

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_stream != null)
        {
            SDL_AudioStream* stream = _stream;
            _stream = null;
            SDL3.SDL_DestroyAudioStream(stream);
        }

        if (_handle.IsAllocated)
        {
            _handle.Free();
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void AudioCallback(nint userdata, SDL_AudioStream* stream, int additionalAmount, int totalAmount)
    {
        if (stream == null)
        {
            return;
        }

        var self = GCHandle.FromIntPtr(userdata).Target as SdlAudioSink;
        if (self is null || self._disposed)
        {
            return;
        }

        int bytes = additionalAmount > 0 ? additionalAmount : totalAmount;
        int frames = bytes / 8;
        if (frames <= 0)
        {
            return;
        }

        float[] interleaved = new float[frames * 2];
        self._mixer.Render(interleaved, frames);
        fixed (float* samples = interleaved)
        {
            SDL3.SDL_PutAudioStreamData(stream, (nint)samples, frames * sizeof(float) * 2);
        }
    }
}
