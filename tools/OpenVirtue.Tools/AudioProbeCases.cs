// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using static TraceProbeWdl;

/// <summary>
/// The cases of the <c>audio</c> probe: how the original hands out its eight sound channels, as the
/// script sees them through <c>CHANNEL</c> and <c>CHANNEL_0…7</c> (manual p.109). The manual says
/// <c>CHANNEL</c> is the channel the last sound went to, −1 if it was not played "e.g. if all channels
/// are busy with <c>SLOOP</c> sounds" (so a normal sound may take a busy channel), and that
/// <c>CHANNEL_n</c> reads 0 (free), 1 (a sound) or 2 (a <c>SLOOP</c> sound). It does not say which
/// free channel a sound takes, which busy one it takes when none is free, when a channel reads free
/// again, or what <c>STOP_SOUND</c>, volume 0, an out-of-range emitter or a flic do to the channels.
/// <para>
/// The sounds are synthetic 8-bit mono tones (<see cref="SyntheticWav"/>): <see cref="OneSecond"/>
/// (1 s at 11025 Hz), <see cref="FourSeconds"/> (4 s), <see cref="Quarter"/> (0.25 s) and
/// <see cref="OneSecond22"/> (1 s at 22050 Hz). A global <c>EACH_TICK.1</c> sampler counts its runs
/// in <c>au_n</c> and prints a <c>C</c> line with <c>TICKS</c>, <c>au_n</c>, <c>FLIC_FRAME</c>,
/// <c>CHANNEL</c> and the eight channel states whenever the states change.
/// </para>
/// </summary>
internal static class AudioProbeCases
{
    /// <summary>The PRINTFILE base name of the trace file (written as OVAUD0.TXT).</summary>
    public const string TraceFile = "ovaud";

    public const string OneSecond = "PRBS1.WAV";

    public const string FourSeconds = "PRBS4.WAV";

    public const string Quarter = "PRBSQ.WAV";

    public const string OneSecond22 = "PRBS2.WAV";

    /// <summary>The sound files the generator writes: name, sample rate, length in samples, tone in Hz.</summary>
    public static readonly IReadOnlyList<(string File, int Rate, int Samples, int Tone)> Sounds =
    [
        (OneSecond, 11025, 11025, 440),
        (FourSeconds, 11025, 44100, 330),
        (Quarter, 11025, 2756, 660),
        (OneSecond22, 22050, 22050, 550),
    ];

    private static readonly string[] States =
        ["CHANNEL", "CHANNEL_0", "CHANNEL_1", "CHANNEL_2", "CHANNEL_3", "CHANNEL_4", "CHANNEL_5", "CHANNEL_6", "CHANNEL_7"];

    private const string Declarations =
        "SOUND au_s1,<" + OneSecond + ">;\nSOUND au_s4,<" + FourSeconds + ">;\nSOUND au_sq,<" + Quarter + ">;\nSOUND au_s2,<" + OneSecond22 + ">;\n" +
        "SKILL au_n { VAL 0; }\nSKILL au_st { VAL 0; }\nSKILL au_last { VAL -1; }\n";

    /// <summary>A trace line: the token, <c>TICKS</c>, the sampler's run count, <c>FLIC_FRAME</c>, <c>CHANNEL</c> and the eight states.</summary>
    private static string L(string token) => V(token, ["TICKS", "au_n", "FLIC_FRAME", .. States]);

    /// <summary>The sampler: the channel states as one base-3 number, printed when it changes.</summary>
    private static readonly string Sampler = Action(
        "au_tick",
        "    RULE au_n = au_n + 1;\n" +
        "    RULE au_st = CHANNEL_0 + 3*CHANNEL_1 + 9*CHANNEL_2 + 27*CHANNEL_3 + 81*CHANNEL_4 + 243*CHANNEL_5 + 729*CHANNEL_6 + 2187*CHANNEL_7;\n" +
        "    IF (au_st != au_last) {\n        RULE au_last = au_st;\n" + Indent(L("C")) + "    }\n");

    private const string Arm = "    SET EACH_TICK.1, au_tick;\n";

    private const string Clear = "    SET EACH_TICK.1, NULL;\n";

    private static string Play(string sound, string volume = "1", string? balance = null) =>
        $"    PLAY_SOUND {sound}, {volume}{(balance is null ? "" : ", " + balance)};\n";

    private static TraceProbeCase Case(
        string label,
        string question,
        string run,
        (string Reading, string Trace)[] predictions,
        string declarations = "",
        TraceProbeMap? map = null) => new(
        label,
        question,
        Declarations + declarations + Sampler,
        Arm + L("GO") + run + L("RET") + Clear,
        Predict(predictions),
        Map: map);

    private static string Wait(int ticks) => $"    WAITT {ticks};\n";

    /// <summary>Eight sounds, one per cycle, each followed by a trace line.</summary>
    private static string Staggered(string sound, int count, string prefix)
    {
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < count; i++)
        {
            sb.Append(Play(sound)).Append(L(prefix + i)).Append("    WAIT 1;\n");
        }

        return sb.ToString();
    }

    public static readonly IReadOnlyList<TraceProbeCase> All =
    [
        Case(
            "one",
            "One 1-second sound: which channel, does CHANNEL_n read 1 in the same cycle, and how many ticks until it reads 0?",
            Play("au_s1") + L("P") + Wait(48),
            [("lowest channel, busy at once", "P … 0 1 0 0 0 0 0 0 0"), ("free after the sound's length", "a C line with CHANNEL_0 0 about 16 ticks after P")]),
        Case(
            "quarter",
            "A 0.25-second sound: free after about 4 ticks, or after a fixed minimum?",
            Play("au_sq") + L("P") + Wait(32),
            [("free after 4 ticks", "the C line with CHANNEL_0 0 about 4 ticks after P")]),
        Case(
            "rate-22",
            "A 1-second sound at 22050 Hz: free after 16 ticks (rate honoured) or 32 (played at 11 kHz)?",
            Play("au_s2") + L("P") + Wait(48),
            [("the rate is honoured", "CHANNEL_0 0 about 16 ticks after P")]),
        Case(
            "nine",
            "Nine 4-second sounds in one cycle: channels 0–7 in order, and the ninth: CHANNEL −1, or which busy channel does it take?",
            string.Concat(Enumerable.Range(0, 9).Select(i => Play("au_s4") + L("N" + i))) + Wait(16),
            [("lowest free first", "N0 … N7 print CHANNEL 0 … 7"), ("no channel for the ninth", "N8 prints CHANNEL −1"), ("the ninth takes a busy channel", "N8 prints CHANNEL 0…7")]),
        Case(
            "steal",
            "Eight 4-second sounds one cycle apart, then two more: which busy channels do the ninth and tenth take (the oldest, the newest, channel 0)?",
            Staggered("au_s4", 8, "S") + Play("au_s4") + L("S8") + "    WAIT 1;\n" + Play("au_s4") + L("S9") + Wait(16),
            [("the oldest is taken", "S8 CHANNEL 0, S9 CHANNEL 1"), ("the newest is taken", "S8 CHANNEL 7"), ("none", "S8 CHANNEL −1")]),
        Case(
            "reuse",
            "A 1-second sound on channel 0, a 4-second one on channel 1; after the first ends, does the next sound take channel 0 again or channel 2?",
            Play("au_s1") + L("A") + "    WAIT 1;\n" + Play("au_s4") + L("B") + Wait(32) + Play("au_sq") + L("C2") + Wait(16),
            [("lowest free channel", "C2 CHANNEL 0"), ("round robin", "C2 CHANNEL 2")]),
        Case(
            "same",
            "The same sound twice in one cycle: two channels, or the second restarts the first?",
            Play("au_s4") + L("A") + Play("au_s4") + L("B") + Wait(16),
            [("two channels", "B CHANNEL 1"), ("restart", "B CHANNEL 0 and only channel 0 busy")]),
        Case(
            "stop",
            "Three sounds, then STOP_SOUND: are the channels free in the same cycle, or only in the next?",
            Play("au_s4") + Play("au_s4") + Play("au_s4") + L("A") + "    STOP_SOUND;\n" + L("STOP") + "    WAIT 1;\n" + L("NEXT") + Wait(8),
            [("free at once", "STOP … 0 0 0"), ("free in the next cycle", "STOP shows 1 1 1, NEXT 0 0 0")]),
        Case(
            "volume-0",
            "PLAY_SOUND with volume 0: does it take a channel?",
            Play("au_s1", "0") + L("P") + Wait(24),
            [("it takes a channel", "P CHANNEL 0, CHANNEL_0 1"), ("it is skipped", "P CHANNEL −1")]),
        Case(
            "sound-vol-0",
            "SOUND_VOL 0, then PLAY_SOUND at volume 1: does it take a channel?",
            "    SET SOUND_VOL, 0;\n" + Play("au_s1") + L("P") + Wait(24) + "    SET SOUND_VOL, 1;\n",
            [("it takes a channel", "P CHANNEL 0"), ("it is skipped", "P CHANNEL −1")]),
        Case(
            "soundfile",
            "PLAY_SOUNDFILE <PRBS1.WAV>, 1: does it take a channel and set CHANNEL, and for how long?",
            $"    PLAY_SOUNDFILE <{OneSecond}>, 1;\n" + L("P") + Wait(48),
            [("like PLAY_SOUND", "P CHANNEL 0, free about 16 ticks later")]),
        Case(
            "far-emitter",
            "PLAY_SOUND at a thing 25 steps away whose texture has SDIST 5: is the sound played (a channel) or skipped (CHANNEL −1)?",
            Play("au_s1", "1", "au_t") + L("P") + Wait(24),
            [("played silently", "P CHANNEL 0"), ("skipped", "P CHANNEL −1")],
            FarThing,
            FarMap),
        Case(
            "flic",
            "A 4-second sound, then a 120-frame flic (70 ms); 10 frames in, a 1-second sound: does the flic stop the first, and do they end after real time (57 and 14 frames) or after their ticks in flic frames (59 and 15)?",
            Play("au_s4") + L("A") + $"    PLAY_FLICFILE <{FlicDetailProbeCases.SecondFlicFile}>;\n" + L("F") + "    WAIT 10;\n" + Play("au_s1") + L("B") + Wait(160),
            [("sounds go on in the flic, real time", "CHANNEL_0 0 near FLIC_FRAME 57"), ("the flic stops sounds", "CHANNEL_0 0 at F")]),
        Case(
            "sloop",
            "A ceiling texture with SOUND au_s4 and SLOOP: does a channel read 2 from the start, and do eight PLAY_SOUNDs take it?",
            L("A") + Staggered("au_sq", 8, "S") + Wait(8) + "    STOP_SOUND;\n" + L("STOP") + Wait(16),
            [("the loop holds a channel", "A shows a 2"), ("normal sounds leave the loop alone", "S7 CHANNEL −1 or a 1-channel"), ("STOP_SOUND stops the loop too", "STOP all 0, then a C line with 2 again")],
            "",
            SloopMap),
        Case(
            "skip-channel",
            "Round 2. Two sounds (CHANNEL 1), then one at volume 0, one at SOUND_VOL 0 and one at the far thing: does CHANNEL stay 1 or turn −1? Then one at volume 0.001.",
            Play("au_s4") + Play("au_s4") + L("A") + Play("au_s1", "0") + L("V0") + "    SET SOUND_VOL, 0;\n" + Play("au_s1") + L("SV0") +
            "    SET SOUND_VOL, 1;\n" + Play("au_s1", "1", "au_t") + L("FAR") + Play("au_s1", "0.001") + L("LOW") + Wait(8),
            [("unchanged", "V0, SV0, FAR CHANNEL 1"), ("−1", "V0 CHANNEL −1")],
            FarThing,
            FarMap),
        Case(
            "near-emitter",
            "Round 2. The control for far-emitter: the thing's texture has SDIST 100, so the player is in range.",
            Play("au_s1", "1", "au_t") + L("P") + Wait(24),
            [("played", "P CHANNEL 0, CHANNEL_0 1")],
            FarThing.Replace("SDIST 5", "SDIST 100", StringComparison.Ordinal),
            FarMap),
        Case(
            "stop-lag",
            "Round 2. One 4-second sound, STOP_SOUND, then a line every cycle: in which cycle does CHANNEL_0 read 0? And a sound right after STOP_SOUND: which channel?",
            Play("au_s4") + Play("au_s4") + L("A") + "    STOP_SOUND;\n" + Play("au_sq") + L("STOP") + string.Concat(Enumerable.Range(1, 5).Select(i => "    WAIT 1;\n" + L("W" + i))) + Wait(8),
            [("free two cycles later", "W1 busy, W2 free"), ("a new sound after STOP takes a free channel", "STOP CHANNEL 2")]),
        Case(
            "stop-sloop",
            "Round 2. In the SLOOP room, STOP_SOUND, then a line every cycle: does channel 0 drop to 0 and come back?",
            L("A") + "    STOP_SOUND;\n" + L("STOP") + string.Concat(Enumerable.Range(1, 6).Select(i => "    WAIT 1;\n" + L("W" + i))) + Wait(8),
            [("the loop is stopped and restarted", "a 0 then 2 in channel 0"), ("the loop is untouched", "2 throughout")],
            "",
            SloopMap),
    ];

    private const string FarThing = "BMAP au_tmap,<PRBMAG.PCX>;\nTEXTURE au_ttex { SCALE_X 4; SCALE_Y 4; BMAPS au_tmap; SDIST 5; }\nTHING au_t { TEXTURE au_ttex; }\n";

    // Properties, not fields: All is initialised first.
    private static TraceProbeMap FarMap => new(Placements: ["THING au_t 24 -8 0 0"]);

    private static TraceProbeMap SloopMap => new(
        RoomCeiling: "au_ltex",
        Textures: "SOUND au_lsnd,<" + FourSeconds + ">;\nTEXTURE au_ltex { SCALE_X 4; SCALE_Y 4; BMAPS cel_map; SOUND au_lsnd; SVOL 0.5; FLAGS SLOOP; }\n");

    /// <summary>The probe as the generator consumes it.</summary>
    public static readonly TraceProbeSet Set = new(
        "audio",
        TraceFile,
        "ovaus",
        "Every line holds `TICKS`, the sampler's cycle count, `FLIC_FRAME`, `CHANNEL` and `CHANNEL_0…7`.\n" +
        "`C` lines come from the per-cycle sampler whenever a channel state changes; the other tokens follow\n" +
        "the case's own `PLAY_SOUND`s. The sounds are synthetic tones of 1 s, 4 s and 0.25 s at 11025 Hz and\n" +
        "1 s at 22050 Hz.",
        All);
}
