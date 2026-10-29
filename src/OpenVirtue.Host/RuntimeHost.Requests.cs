// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using System.Numerics;
using System.Text.Json;
using OpenVirtue.Engine;
using OpenVirtue.Engine.Interpreter;
using OpenVirtue.Engine.Map;
using OpenVirtue.Engine.Rendering;
using OpenVirtue.Formats.Flic;
using OpenVirtue.Formats.Pcx;
using OpenVirtue.Formats.Save;
using OpenVirtue.Formats.Wav;
using OpenVirtue.Formats.Wrs;

namespace OpenVirtue.Host;

public sealed partial class RuntimeHost
{
    private static readonly JsonSerializerOptions SaveJsonOptions = new() { WriteIndented = true };

    private void HandleRuntimeControlRequests()
    {
        if (_runtime.ExitRequested)
        {
            _exitRequested = true;
            _window.Close();
            return;
        }

        try
        {
            if (_runtime.RequestedLevelName is { } levelName)
            {
                IReadOnlyDictionary<string, double> globals = _runtime.CaptureLevelTransitionGlobals();
                ReloadWorld(levelName, mapOverrideName: null, _runtime.RequestedLevelResource, globals);
            }
            else if (_runtime.RequestedMapName is { } mapName)
            {
                ReloadWorld(_mainWdlName, mapName, resourceName: null);
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            _exitRequested = true;
            _window.Close();
        }
    }

    private void HandleRuntimeGeometryInvalidations()
    {
        int pending = _runtime.ShakeRequests.Count;
        if (pending == _handledGeometryInvalidations)
        {
            return;
        }

        _handledGeometryInvalidations = pending;
        if (_geometryReady)
        {
            RebuildGeometryResources();
        }
    }

    private void HandleRuntimeAudioRequests()
    {
        PruneFinishedSounds();
        while (_handledTextureSoundRequests < _runtime.TextureSoundRequests.Count)
        {
            PlayTextureSoundCue(_runtime.TextureSoundRequests[_handledTextureSoundRequests++]);
        }

        while (_handledAudioRequests < _runtime.AudioRequests.Count)
        {
            WdlAudioRequest request = _runtime.AudioRequests[_handledAudioRequests++];
            switch (request.Operation)
            {
                case WdlSoundOperation.Play:
                    TryPlaySound(request.Sound);
                    break;
                case WdlSoundOperation.StopChannel:
                    StopActiveSound(request.Channel);
                    break;
                case WdlSoundOperation.StopAll:
                    StopActiveSounds();
                    break;
            }
        }
    }

    private void PlayTextureSoundCue(WdlTextureSoundRequest cue)
    {
        if (string.IsNullOrWhiteSpace(cue.Texture.SoundName) ||
            !TrySoundEmitter(cue.Target, out Vector3 position, out _))
        {
            return;
        }

        (float volume, float pan) = SpatialSoundMix(
            cue.Texture.SoundVolume,
            position,
            cue.Texture,
            applyVerticalDistance: cue.Target is MapEntity);
        if (volume > 0)
        {
            _runtime.PlayTextureSound(cue.Texture.SoundName, volume, loop: false, balance: pan);
        }
    }

    private void TryPlaySound(WdlSoundRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.FileName) ||
            !TryGetArchiveBytes(request.FileName, out byte[] bytes))
        {
            _runtime.CompleteSound(request.Channel);
            return;
        }

        try
        {
            WavSound decoded = WavSound.Read(bytes);
            (float volume, float pan) = SoundMix(request);
            if (volume <= 0)
            {
                _audio.StopChannel(request.Channel);
                _hostedChannels.Remove(request.Channel);
                _runtime.CompleteSound(request.Channel);
                return;
            }

            float[] mono = WavPcm.ToMono(decoded);
            _audio.StartVoice(request.Channel, mono, decoded.SampleRate, volume, pan, request.Loop);
            if (request.Channel is >= 0 and < AudioMixer.VoiceCount)
            {
                _hostedChannels.Add(request.Channel);
            }
        }
        catch (Exception ex) when (ex is InvalidDataException or NotSupportedException or OverflowException or ArgumentException)
        {
            _runtime.CompleteSound(request.Channel);
        }
    }

    private (float Volume, float Pan) SoundMix(WdlSoundRequest request)
    {
        double volume = Math.Clamp(request.Volume, 0, 1) * Math.Clamp(_runtime.GetSkill("SOUND_VOL"), 0, 1);
        double pan = Math.Clamp(request.Balance ?? 0, -1, 1);
        if (request.EmitterName is null ||
            _runtime.GetObject(request.EmitterName) is not { } emitter ||
            !TrySoundEmitter(emitter, out Vector3 position, out LevelTexture? texture))
        {
            return ((float)volume, (float)pan);
        }

        return SpatialSoundMix(volume, position, texture, pan, emitter is MapEntity);
    }

    private (float Volume, float Pan) SpatialSoundMix(
        double volume,
        Vector3 position,
        LevelTexture? texture,
        double pan = 0,
        bool applyVerticalDistance = true)
    {
        Vector3 offset = RenderSpace.FromMap(position) - RenderSpace.FromMap(_player.Position);
        float horizontalDistance = MathF.Sqrt((offset.X * offset.X) + (offset.Z * offset.Z));
        double audibleDistance = texture?.SoundDistance ?? 100;
        if (audibleDistance <= 0 || horizontalDistance >= audibleDistance)
        {
            return (0, 0);
        }

        volume *= 1 - (horizontalDistance / audibleDistance);
        if (applyVerticalDistance &&
            texture is { SoundVerticalDistance: > 0 } spatialTexture &&
            Math.Abs(offset.Y) >= spatialTexture.SoundVerticalDistance)
        {
            return (0, 0);
        }

        if (horizontalDistance > 0.0001f)
        {
            Vector3 horizontalDirection = Vector3.Normalize(new Vector3(offset.X, 0, offset.Z));
            pan = Math.Clamp(Vector3.Dot(horizontalDirection, _camera.Right), -1, 1);
        }

        return ((float)Math.Clamp(volume, 0, 1), (float)pan);
    }

    private bool TrySoundEmitter(AcknexObject emitter, out Vector3 position, out LevelTexture? texture)
    {
        string? textureName = null;
        switch (emitter)
        {
            case MapEntity entity:
                double floor = entity.Region >= 0 && entity.Region < _level.Regions.Count
                    ? _level.Regions[entity.Region].FloorHeight
                    : 0;
                double height = entity["GROUND"] > 0 ? entity.Z : floor + entity.Z;
                position = new Vector3((float)entity.X, (float)height, (float)entity.Y);
                textureName = entity.Texture;
                break;
            case Wall wall when wall.Vertex1 >= 0 && wall.Vertex1 < _level.Vertices.Count &&
                                wall.Vertex2 >= 0 && wall.Vertex2 < _level.Vertices.Count:
                Vertex first = _level.Vertices[wall.Vertex1];
                Vertex second = _level.Vertices[wall.Vertex2];
                position = new Vector3(
                    (first.X + second.X) * 0.5f,
                    0,
                    (first.Y + second.Y) * 0.5f);
                textureName = wall.Texture;
                break;
            default:
                position = default;
                texture = null;
                return false;
        }

        texture = textureName is not null && _level.Textures.TryGetValue(textureName, out LevelTexture found)
            ? found
            : null;
        return true;
    }

    private static string? EmitterTextureName(AcknexObject emitter) => emitter switch
    {
        MapEntity entity => entity.Texture,
        Wall wall => wall.Texture,
        _ => null,
    };

    private bool HasArchiveEntry(string fileName)
    {
        string normalized = Path.GetFileName(fileName);
        return _archive is not null &&
               normalized.Length > 0 &&
               _archive.Entries.Any(entry => entry.Name.Equals(normalized, StringComparison.OrdinalIgnoreCase));
    }

    private bool TryGetArchiveBytes(string fileName, out byte[] bytes)
    {
        bytes = [];
        if (_archive is null)
        {
            return false;
        }

        string normalized = Path.GetFileName(fileName);
        foreach (WrsEntry entry in _archive.Entries)
        {
            if (entry.Name.Equals(normalized, StringComparison.OrdinalIgnoreCase))
            {
                bytes = entry.GetData();
                return true;
            }
        }

        return false;
    }

    private void PruneFinishedSounds()
    {
        if (_hostedChannels.Count == 0)
        {
            return;
        }

        int[] channels = _hostedChannels.ToArray();
        foreach (int channel in channels)
        {
            if (_audio.IsChannelPlaying(channel))
            {
                continue;
            }

            _hostedChannels.Remove(channel);
            _runtime.CompleteSound(channel);
        }
    }

    private void StopActiveSound(int channel)
    {
        _audio.StopChannel(channel);
        _hostedChannels.Remove(channel);
    }

    private void StopActiveSounds()
    {
        foreach (int channel in _hostedChannels)
        {
            _runtime.CompleteSound(channel);
        }

        _hostedChannels.Clear();
        _audio.StopAll();
    }

    private void UpdateTextureSounds()
    {
        if (_player.Region < 0 || _player.Region >= _level.Regions.Count)
        {
            return;
        }

        Region region = _level.Regions[_player.Region];
        var position = new Vector2(_player.Position.X, _player.Position.Z);
        if (_textureSoundRegion != _player.Region)
        {
            if (_ceilingSoundChannel >= 0)
            {
                _runtime.StopSoundChannel(_ceilingSoundChannel);
            }

            _textureSoundRegion = _player.Region;
            _ceilingSoundChannel = -1;
            _lastTextureSoundPosition = position;
            _walkSoundDistance = 0;
            _lastWaveSoundCycle = WaveSoundCycle();
            StartCeilingTextureSound(region);
        }

        UpdateCeilingSoundVolume(region);
        UpdateSpatialTextureSounds();
        float moved = Vector2.Distance(position, _lastTextureSoundPosition);
        _lastTextureSoundPosition = position;
        double wavePeriod = Math.Max(0.001, _runtime.GetSkill("WAVE_PERIOD"));
        _runtime.SetSkill("WAVE", Math.Sin(Math.Tau * _runtime.GetSkill("TICKS") / wavePeriod));
        if (!_player.Grounded || moved <= 0.0001f ||
            !TryTexture(region.FloorTexture, out LevelTexture floorTexture) ||
            string.IsNullOrWhiteSpace(floorTexture.SoundName))
        {
            _runtime.SetSkill("WALK", 0);
            _lastWaveSoundCycle = WaveSoundCycle();
            return;
        }

        _walkSoundDistance += moved;
        double walkPeriod = Math.Max(0.001, _runtime.GetSkill("WALK_PERIOD"));
        _runtime.SetSkill("WALK", Math.Sin(Math.Tau * _walkSoundDistance / walkPeriod));
        bool trigger;
        if (floorTexture.IsSoundLoop)
        {
            long waveCycle = WaveSoundCycle();
            trigger = waveCycle != _lastWaveSoundCycle;
            _lastWaveSoundCycle = waveCycle;
        }
        else
        {
            trigger = _walkSoundDistance >= walkPeriod;
            if (trigger)
            {
                _walkSoundDistance %= walkPeriod;
            }
        }

        if (trigger)
        {
            double volume = floorTexture.SoundVolume * Math.Clamp(_runtime.GetSkill("PSOUND_VOL"), 0, 2);
            _runtime.PlayTextureSound(floorTexture.SoundName, volume, loop: false);
        }
    }

    private long WaveSoundCycle()
    {
        double period = Math.Max(0.001, _runtime.GetSkill("WAVE_PERIOD"));
        return (long)Math.Floor(_runtime.GetSkill("TICKS") / period);
    }

    private void StartCeilingTextureSound(Region region)
    {
        if (!TryTexture(region.CeilTexture, out LevelTexture texture) ||
            string.IsNullOrWhiteSpace(texture.SoundName))
        {
            return;
        }

        int channel = _runtime.PlayTextureSound(texture.SoundName, texture.SoundVolume, texture.IsSoundLoop);
        if (texture.IsSoundLoop)
        {
            _ceilingSoundChannel = channel;
        }
    }

    private void UpdateCeilingSoundVolume(Region region)
    {
        if (_ceilingSoundChannel < 0 || !TryTexture(region.CeilTexture, out LevelTexture texture))
        {
            return;
        }

        if (_runtime.GetSkill($"CHANNEL_{_ceilingSoundChannel}") == 0)
        {
            _ceilingSoundChannel = -1;
            return;
        }

        _audio.UpdateVoice(
            _ceilingSoundChannel,
            (float)(texture.SoundVolume * Math.Clamp(_runtime.GetSkill("SOUND_VOL"), 0, 1)),
            pan: 0);
    }

    private bool TryTexture(string? name, out LevelTexture texture)
    {
        if (!string.IsNullOrWhiteSpace(name) && _level.Textures.TryGetValue(name, out texture))
        {
            return true;
        }

        texture = default;
        return false;
    }

    private void UpdateSpatialTextureSounds()
    {
        var candidates = new List<SpatialLoopCandidate>();
        foreach (Wall wall in _level.Walls)
        {
            AddSpatialLoopCandidate(wall, candidates);
        }

        foreach (Thing thing in _level.Things)
        {
            AddSpatialLoopCandidate(thing, candidates);
        }

        foreach (Actor actor in _level.Actors)
        {
            AddSpatialLoopCandidate(actor, candidates);
        }

        // Nothing loops nearby and nothing plays: the usual case, with nothing to stop or start.
        if (candidates.Count == 0 && _spatialLoopSounds.Count == 0)
        {
            return;
        }

        Dictionary<AcknexObject, SpatialLoopCandidate> audible = candidates.ToDictionary(static candidate => candidate.Emitter);
        foreach ((AcknexObject emitter, SpatialLoopSound active) in _spatialLoopSounds.ToArray())
        {
            if (!audible.TryGetValue(emitter, out SpatialLoopCandidate candidate) ||
                !candidate.SoundName.Equals(active.SoundName, StringComparison.OrdinalIgnoreCase) ||
                _runtime.GetSkill($"CHANNEL_{active.Channel}") == 0)
            {
                _runtime.StopSoundChannel(active.Channel);
                _spatialLoopSounds.Remove(emitter);
                continue;
            }

            _audio.UpdateVoice(active.Channel, candidate.Volume, candidate.Pan);
        }

        foreach (SpatialLoopCandidate candidate in candidates.OrderByDescending(static item => item.Volume))
        {
            if (_spatialLoopSounds.ContainsKey(candidate.Emitter) || !HasFreeSoundChannel())
            {
                continue;
            }

            int channel = _runtime.PlayTextureSound(candidate.SoundName, candidate.BaseVolume, loop: true, candidate.Pan);
            if (channel >= 0)
            {
                _spatialLoopSounds[candidate.Emitter] = new SpatialLoopSound(channel, candidate.SoundName);
            }
        }
    }

    private void AddSpatialLoopCandidate(AcknexObject emitter, List<SpatialLoopCandidate> candidates)
    {
        // Most emitters have no looping sound texture, so the texture is checked before the
        // emitter is placed (which reads its skills).
        if (_level.FindTexture(EmitterTextureName(emitter)) is not { } entry ||
            !entry.Texture.IsSoundLoop ||
            string.IsNullOrWhiteSpace(entry.Texture.SoundName) ||
            !TrySoundEmitter(emitter, out Vector3 position, out _))
        {
            return;
        }

        LevelTexture spatialTexture = entry.Texture;

        (float requestVolume, float pan) = SpatialSoundMix(
            spatialTexture.SoundVolume,
            position,
            spatialTexture,
            applyVerticalDistance: emitter is MapEntity);
        if (requestVolume <= 0)
        {
            return;
        }

        float volume = requestVolume * (float)Math.Clamp(_runtime.GetSkill("SOUND_VOL"), 0, 1);
        candidates.Add(new SpatialLoopCandidate(emitter, spatialTexture.SoundName, requestVolume, volume, pan));
    }

    private bool HasFreeSoundChannel()
    {
        for (int channel = 0; channel < 8; channel++)
        {
            if (_runtime.GetSkill($"CHANNEL_{channel}") == 0)
            {
                return true;
            }
        }

        return false;
    }

    private void HandleRuntimePaletteRequests()
    {
        while (_handledPaletteFadeRequests < _runtime.PaletteFadeRequests.Count)
        {
            ApplyPaletteFade(_runtime.PaletteFadeRequests[_handledPaletteFadeRequests++]);
        }
    }

    private void ApplyPaletteFade(WdlPaletteFadeRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.FileName) || !TryGetArchiveBytes(request.FileName, out byte[] bytes))
        {
            return;
        }

        try
        {
            PcxImage paletteImage = PcxImage.Read(bytes);
            Vector3 target = PaletteColorCast(paletteImage.Palette);
            float factor = (float)Math.Clamp(request.Factor, 0, 1);
            _paletteTint = Vector3.Lerp(Vector3.One, target, factor);
        }
        catch (InvalidDataException)
        {
            // A bad palette asset must not stop the frame.
        }
    }

    private static Vector3 PaletteColorCast(Rgb24[] palette)
    {
        if (palette.Length == 0)
        {
            return Vector3.One;
        }

        long r = 0;
        long g = 0;
        long b = 0;
        foreach (Rgb24 color in palette)
        {
            r += color.R;
            g += color.G;
            b += color.B;
        }

        var average = new Vector3(r, g, b) / (255.0f * palette.Length);
        float strongestChannel = Math.Max(average.X, Math.Max(average.Y, average.Z));
        return strongestChannel > 0.01f ? average / strongestChannel : Vector3.Zero;
    }

    private void HandleRuntimePresentationRequests()
    {
        while (_handledBeepRequests < _runtime.BeepCount)
        {
            _handledBeepRequests++;
            _audio.PlayBeep();
        }

        // PLAY_FLICFILE does not hold its action and STOP_FLIC ends the flic before the next
        // frame (OV-20261015-flic-play-blocking). Stops and plays are applied in script order.
        while (_handledFlicRequests < _runtime.FlicRequests.Count)
        {
            WdlFlicRequest request = _runtime.FlicRequests[_handledFlicRequests++];
            HandleStopFlicRequests(request.StopsBefore);
            HandleFlicRequest(request);
        }

        HandleStopFlicRequests(_runtime.StopFlicCount);

        while (_handledScreenshotRequests < _runtime.ScreenshotRequests.Count)
        {
            _pendingScreenshots.Add(_runtime.ScreenshotRequests[_handledScreenshotRequests++]);
        }
    }

    private void HandleStopFlicRequests(int stopCount)
    {
        while (_handledStopFlicRequests < stopCount)
        {
            _handledStopFlicRequests++;
            if (_activeFlic is not null)
            {
                _activeFlic = null;
                _runtime.SetSkill(WdlRuntime.FlicFrameSkill, 0);
            }

            _lastPresentationStatus = "flic stopped";
        }
    }

    private void HandleFlicRequest(WdlFlicRequest request)
    {
        string fileName = Path.GetFileName(request.FileName);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            _lastPresentationStatus = "flic request ignored";
            EndUnplayableFlic();
            return;
        }

        if (!TryGetArchiveBytes(fileName, out byte[] bytes))
        {
            _lastPresentationStatus = $"flic {fileName} missing";
            EndUnplayableFlic();
            return;
        }

        try
        {
            FlicAnimation animation = FlicAnimation.Read(bytes);
            if (animation.Frames.Count == 0)
            {
                throw new InvalidDataException("FLIC contains no display frames.");
            }

            // A new flic replaces one that is still playing.
            _activeFlic = new FlicPlayback(request, fileName, animation);
            _runtime.SetSkill(WdlRuntime.FlicFrameSkill, 1);
            _lastPresentationStatus = $"flic {fileName} 1/{animation.Frames.Count}";
        }
        catch (Exception ex) when (ex is InvalidDataException or NotSupportedException or OverflowException)
        {
            _lastPresentationStatus = $"flic {fileName} invalid";
            EndUnplayableFlic();
        }
    }

    /// <summary>
    /// A flic that cannot be played leaves nothing on screen: any flic still playing goes on, and
    /// <c>FLIC_FRAME</c> reads its frame, or 0 when none plays. The original skips a missing file
    /// alone and faults when one is requested while a flic plays (OV-20261015-flic-scheduler-detail);
    /// OpenVirtue keeps the playing flic.
    /// </summary>
    private void EndUnplayableFlic() =>
        _runtime.SetSkill(WdlRuntime.FlicFrameSkill, _activeFlic is { } playing ? playing.FrameIndex + 1 : 0);

    /// <summary>The most flic cycles one host frame catches up on; the rest of a stall is dropped.</summary>
    private const int MaxFlicCyclesPerFrame = 4;

    /// <summary>
    /// Runs the scheduler cycles of the flic frames that are due. The original runs one cycle per
    /// flic frame: cycle k shows frame k with <c>FLIC_FRAME</c> k, and its <c>TIME_CORR</c> is the
    /// frame delay in 64 ms units (OV-20261015-flic-scheduler-detail). The first cycle comes in the
    /// next host frame after <c>PLAY_FLICFILE</c>, and cycle k is due k − 1 frame delays after the
    /// request, so a host frame may run none. The cycle due after the last frame is a world frame.
    /// </summary>
    /// <returns><c>true</c> if the flic ran out in this host frame, so the world frame runs now.</returns>
    private bool RunFlicCycles(WdlRuntime tickRuntime, FlicPlayback playback)
    {
        double frameSeconds = Math.Max(0.001, playback.Animation.FrameDelay.TotalSeconds);
        double cycleSeconds = Timing.FlicCycleSeconds(playback.Animation.FrameDelay.TotalMilliseconds);
        playback.ElapsedSeconds += Math.Max(0, _lastFrameSeconds);
        for (int ran = 0; ran < MaxFlicCyclesPerFrame; ran++)
        {
            if (playback.CyclesRun > 0)
            {
                if (playback.ElapsedSeconds < frameSeconds)
                {
                    return false;
                }

                playback.ElapsedSeconds -= frameSeconds;
            }

            if (playback.CyclesRun >= playback.Animation.Frames.Count)
            {
                _activeFlic = null;
                _runtime.SetSkill(WdlRuntime.FlicFrameSkill, 0);
                _lastPresentationStatus = $"flic {playback.FileName} complete";
                return true;
            }

            playback.FrameIndex = playback.CyclesRun++;
            tickRuntime.SetSkill(WdlRuntime.FlicFrameSkill, playback.FrameIndex + 1);
            _lastPresentationStatus = $"flic {playback.FileName} {playback.FrameIndex + 1}/{playback.Animation.Frames.Count}";
            _lastTimeCorrection = tickRuntime.TickFlic(cycleSeconds);
            HandleRuntimeHostRequests();
            if (!ReferenceEquals(_runtime, tickRuntime) || !ReferenceEquals(_activeFlic, playback))
            {
                // STOP_FLIC, a new flic or a level change: the world or the new flic starts next frame.
                return false;
            }
        }

        playback.ElapsedSeconds = Math.Min(playback.ElapsedSeconds, frameSeconds);
        return false;
    }

    private void HandleRuntimeInputRequests()
    {
        if (_handlingInputRequest || _runtime.PendingInputRequests.Count == 0)
        {
            return;
        }

        _handlingInputRequest = true;
        _window.SetCursorVisible(true);
        try
        {
            while (_runtime.PendingInputRequests.Count > 0 && _window.IsOpen && !_exitRequested)
            {
                WdlInputRequest request = _runtime.PendingInputRequests[0];
                string? entered = _window.Prompt(request.StringName, request.InitialValue, request.Capacity);
                int resultCode = entered is null ? -1 : 0;
                _runtime.CompleteInput(request.Id, entered ?? request.InitialValue, resultCode);
                HandleRuntimeHostRequests();
                if (_runtime.ExitRequested || !_window.IsOpen)
                {
                    break;
                }
            }
        }
        finally
        {
            _handlingInputRequest = false;
            if (!_runtime.ExitRequested && _window.IsOpen)
            {
                _frameClock.Restart();
            }
        }
    }

    private void HandleRuntimePersistenceRequests()
    {
        while (_handledPersistenceRequests < _runtime.PersistenceRequests.Count)
        {
            WdlPersistenceRequest request = _runtime.PersistenceRequests[_handledPersistenceRequests++];
            if (request.InfoOnly)
            {
                // The runtime reads and writes the original's file itself, in the instruction
                // (OV-20261029-save-format).
                _lastPersistenceStatus = $"{PersistenceVerb(request)} info {PersistenceSlotName(request)}";
                continue;
            }

            try
            {
                if (request.Operation == WdlPersistenceOperation.Save)
                {
                    SaveRuntimeSnapshot(request);
                }
                else
                {
                    LoadRuntimeSnapshot(request);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                _lastPersistenceStatus = $"{PersistenceVerb(request)} {PersistenceSlotName(request)} failed: {ex.Message}";
            }
        }
    }

    private void FlushPendingScreenshots()
    {
        if (_pendingScreenshots.Count == 0)
        {
            return;
        }

        foreach (WdlScreenshotRequest request in _pendingScreenshots)
        {
            try
            {
                SaveScreenshot(request);
            }
            catch (IOException)
            {
                // A failed capture must not stop the frame.
            }
        }

        _pendingScreenshots.Clear();
    }

    private void SaveScreenshot(WdlScreenshotRequest request)
    {
        RgbaImage image = _graphics.ReadRgba();
        byte[] png = PngWriter.Encode(image.Width, image.Height, image.Pixels);
        string path = ScreenshotPath(request);
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllBytes(path, png);
    }

    private string ScreenshotPath(WdlScreenshotRequest request)
    {
        string directory = Path.Combine(_contentRoot, "screenshots");
        return Path.Combine(directory, $"{SanitizeFileName(request.BaseName)}{request.Number:000}.png");
    }

    private void SaveRuntimeSnapshot(WdlPersistenceRequest request)
    {
        string path = RuntimeSavePath(request);
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using FileStream stream = File.Create(path);
        JsonSerializer.Serialize(stream, _runtime.CaptureSnapshot(), SaveJsonOptions);
        _lastPersistenceStatus = $"saved {PersistenceSlotName(request)}";
    }

    private void LoadRuntimeSnapshot(WdlPersistenceRequest request)
    {
        string path = RuntimeSavePath(request);
        if (!File.Exists(path))
        {
            if (_runtime.SaveFilePath(request) is { } original && File.Exists(original))
            {
                ImportOriginalSave(request, original);
                return;
            }

            _lastPersistenceStatus = $"missing {PersistenceSlotName(request)}";
            return;
        }

        using FileStream stream = File.OpenRead(path);
        WdlRuntimeSnapshot? snapshot = JsonSerializer.Deserialize<WdlRuntimeSnapshot>(stream, SaveJsonOptions);
        if (snapshot is null)
        {
            _lastPersistenceStatus = $"load {PersistenceSlotName(request)} empty";
            return;
        }

        _runtime.RestoreSnapshot(snapshot);
        AdoptRuntimeHeading();
        _handledGeometryInvalidations = _runtime.ShakeRequests.Count;
        _hasPlayerRuntimeSnapshot = false;
        StopActiveSounds();
        RebuildWorldResources();
        ApplyRuntimePlayerState();
        _lastPersistenceStatus = $"loaded {PersistenceSlotName(request)}";
    }

    /// <summary>
    /// Loads what OpenVirtue can read of a full save the original wrote:
    /// <list type="bullet">
    /// <item>its <c>GLOBAL</c> and <c>PLAYER</c> skills, the predefined ones and so the player's
    /// position included (OV-20261029-save-format);</item>
    /// <item>the things', actors' and walls' positions, angles, speeds, offsets and skills
    /// (OV-20261029-save-body).</item>
    /// </list>
    /// Regions keep their level values, as the original's saves do. Waiting actions and the global
    /// slots are not read yet.
    /// </summary>
    private void ImportOriginalSave(WdlPersistenceRequest request, string path)
    {
        byte[] body = AcknexSaveFile.Unpack(File.ReadAllBytes(path));
        IReadOnlyList<SaveSkill> skills = AcknexSaveFile.ReadSkills(body);
        int restored = _runtime.RestoreSavedSkills(skills, name => !_level.LocalSkills.Contains(name));
        int objects = ImportOriginalObjects(body);
        if (objects > 0)
        {
            RebuildWorldResources();
        }

        _hasPlayerRuntimeSnapshot = false;
        ApplyRuntimePlayerState();
        AdoptRuntimeHeading();
        _lastPersistenceStatus = $"loaded {restored} skills and {objects} objects of {PersistenceSlotName(request)} (original save)";
    }

    /// <summary>
    /// Copies the object records of an original full save onto the level's things, actors and walls.
    /// The save lists actors, then things, then walls, each in reverse map order.
    /// </summary>
    private int ImportOriginalObjects(byte[] body)
    {
        if (AcknexSaveFile.ReadObjects(body, _level.Things.Count + _level.Actors.Count, _level.Walls.Count) is not { } saved)
        {
            return 0;
        }

        AcknexObject[] entities = [.. _level.Actors.Reverse(), .. _level.Things.Reverse()];
        AcknexObject[] walls = [.. _level.Walls.Reverse()];
        for (int i = 0; i < entities.Length; i++)
        {
            Apply(entities[i], saved.Entities[i]);
        }

        for (int i = 0; i < walls.Length; i++)
        {
            Apply(walls[i], saved.Walls[i]);
        }

        return entities.Length + walls.Length;

        static void Apply(AcknexObject target, SaveObjectState state)
        {
            foreach ((string name, int raw) in state.RawSkills)
            {
                target[name] = raw / 1024.0;
            }
        }
    }

    private string RuntimeSavePath(WdlPersistenceRequest request)
    {
        string root = Path.Combine(_contentRoot, "openvirtue-saves", SanitizeFileName(ArchiveStem()));
        string level = SanitizeFileName(Path.GetFileNameWithoutExtension(_mainWdlName));
        return Path.Combine(root, $"{level}-{SanitizeFileName(request.BaseName)}{request.Number:000}.ovsave.json");
    }

    private string RuntimePrintDirectory() =>
        Path.Combine(_contentRoot, "openvirtue-prints", SanitizeFileName(ArchiveStem()));

    private static string PersistenceVerb(WdlPersistenceRequest request) =>
        request.Operation == WdlPersistenceOperation.Save ? "save" : "load";

    private static string PersistenceSlotName(WdlPersistenceRequest request) =>
        $"{request.BaseName}{request.Number:000}";

    private static string SanitizeFileName(string value)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        return new string(value.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
    }

    private sealed class FlicPlayback(WdlFlicRequest request, string fileName, FlicAnimation animation)
    {
        public WdlFlicRequest Request { get; } = request;

        public string FileName { get; } = fileName;

        public FlicAnimation Animation { get; } = animation;

        /// <summary>The 0-based frame on screen.</summary>
        public int FrameIndex { get; set; }

        /// <summary>The flic's scheduler cycles so far; cycle k shows frame k.</summary>
        public int CyclesRun { get; set; }

        /// <summary>Host time since the last cycle.</summary>
        public double ElapsedSeconds { get; set; }
    }
}
