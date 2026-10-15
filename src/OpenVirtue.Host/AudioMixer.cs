// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

namespace OpenVirtue.Host;

/// <summary>
/// Eight mono voices mixed to interleaved stereo float samples. Pan follows the linear law the
/// previous host got from NAudio's <c>WaveChannel32</c>: −1 is fully left, +1 is fully right,
/// and 0 leaves both channels at <c>volume</c>. The device callback and the frame loop share
/// one lock.
/// </summary>
public sealed class AudioMixer
{
    public const int VoiceCount = 8;

    private readonly object _gate = new();
    private readonly Voice[] _voices = new Voice[VoiceCount];
    private float[] _beep = [];
    private int _beepPos;
    private int _outputRate = 44100;

    public AudioMixer()
    {
        for (int i = 0; i < _voices.Length; i++)
        {
            _voices[i] = new Voice();
        }
    }

    /// <summary>Sample rate <see cref="Render"/> consumes. Set before the device starts.</summary>
    public int OutputRate
    {
        get { lock (_gate) { return _outputRate; } }
        set { lock (_gate) { _outputRate = Math.Max(1, value); } }
    }

    public void StartVoice(int channel, ReadOnlyMemory<float> monoSamples, int sampleRate, float volume, float pan, bool loop)
    {
        if (channel < 0 || channel >= VoiceCount || monoSamples.Length == 0 || sampleRate <= 0)
        {
            return;
        }

        lock (_gate)
        {
            Voice voice = _voices[channel];
            voice.Samples = monoSamples.ToArray();
            voice.SampleRate = sampleRate;
            voice.Volume = Math.Clamp(volume, 0, 1);
            voice.Pan = Math.Clamp(pan, -1, 1);
            voice.Loop = loop;
            voice.Position = 0;
            voice.Active = true;
        }
    }

    public void UpdateVoice(int channel, float volume, float pan)
    {
        if (channel < 0 || channel >= VoiceCount)
        {
            return;
        }

        lock (_gate)
        {
            Voice voice = _voices[channel];
            if (!voice.Active)
            {
                return;
            }

            voice.Volume = Math.Clamp(volume, 0, 1);
            voice.Pan = Math.Clamp(pan, -1, 1);
        }
    }

    public void StopChannel(int channel)
    {
        if (channel < 0 || channel >= VoiceCount)
        {
            return;
        }

        lock (_gate)
        {
            _voices[channel].Active = false;
        }
    }

    public void StopAll()
    {
        lock (_gate)
        {
            foreach (Voice voice in _voices)
            {
                voice.Active = false;
            }
        }
    }

    public bool IsChannelPlaying(int channel)
    {
        if (channel < 0 || channel >= VoiceCount)
        {
            return false;
        }

        lock (_gate)
        {
            return _voices[channel].Active;
        }
    }

    /// <summary>Queues a short square beep mixed over the voices. A new beep replaces one still playing.</summary>
    public void PlayBeep()
    {
        const int Rate = 44100;
        const float Seconds = 0.06f;
        int count = (int)(Rate * Seconds);
        var samples = new float[count];
        for (int i = 0; i < count; i++)
        {
            samples[i] = ((i / 50) % 2 == 0 ? 0.25f : -0.25f);
        }

        lock (_gate)
        {
            _beep = samples;
            _beepPos = 0;
        }
    }

    /// <summary>Mixes <paramref name="frameCount"/> stereo frames into <paramref name="interleaved"/> (length ≥ frames × 2).</summary>
    public void Render(Span<float> interleaved, int frameCount)
    {
        if (frameCount <= 0)
        {
            return;
        }

        interleaved[..(frameCount * 2)].Clear();
        lock (_gate)
        {
            foreach (Voice voice in _voices)
            {
                if (!voice.Active || voice.Samples.Length == 0)
                {
                    continue;
                }

                (float leftGain, float rightGain) = PanGains(voice.Volume, voice.Pan);
                double step = voice.SampleRate / (double)_outputRate;
                for (int frame = 0; frame < frameCount; frame++)
                {
                    if (voice.Position >= voice.Samples.Length)
                    {
                        if (!voice.Loop)
                        {
                            voice.Active = false;
                            break;
                        }

                        voice.Position %= voice.Samples.Length;
                    }

                    int index = (int)voice.Position;
                    int next = index + 1;
                    float sample = voice.Samples[index];
                    if (next < voice.Samples.Length)
                    {
                        float t = (float)(voice.Position - index);
                        sample += (voice.Samples[next] - sample) * t;
                    }

                    interleaved[frame * 2] += sample * leftGain;
                    interleaved[(frame * 2) + 1] += sample * rightGain;
                    voice.Position += step;
                }
            }

            for (int frame = 0; frame < frameCount && _beepPos < _beep.Length; frame++)
            {
                float sample = _beep[_beepPos++];
                interleaved[frame * 2] += sample;
                interleaved[(frame * 2) + 1] += sample;
            }
        }
    }

    /// <summary>Linear constant-amplitude pan. Center does not attenuate either side.</summary>
    public static (float Left, float Right) PanGains(float volume, float pan)
    {
        volume = Math.Clamp(volume, 0, 1);
        pan = Math.Clamp(pan, -1, 1);
        float left = pan <= 0 ? 1f : 1f - pan;
        float right = pan >= 0 ? 1f : 1f + pan;
        return (volume * left, volume * right);
    }

    private sealed class Voice
    {
        public float[] Samples = [];
        public int SampleRate = 44100;
        public double Position;
        public float Volume;
        public float Pan;
        public bool Loop;
        public bool Active;
    }
}

/// <summary>Turns a PCM <see cref="OpenVirtue.Formats.Wav.WavSound"/> into mono floats in −1..1.</summary>
public static class WavPcm
{
    public static float[] ToMono(OpenVirtue.Formats.Wav.WavSound sound)
    {
        ArgumentNullException.ThrowIfNull(sound);
        if (sound.AudioFormat != OpenVirtue.Formats.Wav.WavSound.FormatPcm)
        {
            throw new NotSupportedException("Only uncompressed PCM is mixed.");
        }

        int channels = Math.Max(1, sound.Channels);
        int bytesPerSample = sound.BitsPerSample / 8;
        if (bytesPerSample is not (1 or 2))
        {
            throw new NotSupportedException($"Unsupported PCM width {sound.BitsPerSample}.");
        }

        int frameSize = bytesPerSample * channels;
        int frames = frameSize == 0 ? 0 : sound.Data.Length / frameSize;
        var mono = new float[frames];
        ReadOnlySpan<byte> data = sound.Data;
        for (int frame = 0; frame < frames; frame++)
        {
            float sum = 0;
            int offset = frame * frameSize;
            for (int channel = 0; channel < channels; channel++)
            {
                sum += bytesPerSample == 1
                    ? (data[offset + channel] - 128) / 128f
                    : BitConverter.ToInt16(data.Slice(offset + (channel * 2), 2)) / 32768f;
            }

            mono[frame] = sum / channels;
        }

        return mono;
    }
}

/// <summary>An audio sink that accepts every call and plays nothing.</summary>
public sealed class NullAudioSink : IAudioSink
{
    public void StartVoice(int channel, ReadOnlyMemory<float> monoSamples, int sampleRate, float volume, float pan, bool looping)
    {
    }

    public void UpdateVoice(int channel, float volume, float pan)
    {
    }

    public void StopChannel(int channel)
    {
    }

    public void StopAll()
    {
    }

    public bool IsChannelPlaying(int channel) => false;

    public void PlayBeep()
    {
    }
}
