// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

namespace OpenVirtue.Formats.Save;

/// <summary>
/// The engine's own skills, as every full save the original wrote lists them after the level's
/// (OV-20261029-save-body round 9): the documented predefined skills and four internal ones whose names
/// start with an underscore. A save without all of them is not loaded. The original matches skills by
/// name, so the order here is only the one its saves happen to use.
/// </summary>
/// <remarks>
/// A list of names read off saves the patched DOS runtime wrote for synthetic levels (the skills a
/// level does not declare); see <c>PROVENANCE.md</c>.
/// </remarks>
public static class AcknexPredefinedSkills
{
    /// <summary>The 238 names.</summary>
    public static IReadOnlyList<string> Names { get; } =
    [
        "_force", "_svol_old", "_mvol_old", "_shot_no", "EYE_DIST", "GFX_LAST_Y", "GFX_LAST_X", "GFX_DY",
        "GFX_DX", "GFX_Y", "GFX_X", "STR_LEN", "LOAD_MODE", "SCOPE_4", "SCOPE_3", "SCOPE_2", "SCOPE_1",
        "LEND_1", "LSTART_1", "PEND_1", "PSTART_1", "NTOUCH_INDEX", "MTOUCH_INDEX", "HERE_INDEX",
        "TIME_SLICES", "TIME_CLIPPING", "TIME_VERTICES", "TIME_TOTAL", "TIME_FBUFFER", "TIME_DRAW",
        "TIME_RENDER", "TIME_TARGETS", "TIME_OBJECTS", "TIME_ACTIONS", "MAX_DIST", "HPIXELS", "SPANS",
        "VISIBLES", "SEGMENTS", "BETWEENS", "ACTIONS", "ACTIVE_REGTICKS", "ACTIVE_OBJTICKS", "ACTIVE_TARGETS",
        "ACTIVE_OBJECTS", "ACTIVE_NEXUS", "ERROR", "DEBUG_MODE", "SKIP_ANGLE", "DELTA_ANGLE", "MOVE_ANGLE",
        "BOUNCE_VY", "BOUNCE_VX", "IMPACT_VROT", "IMPACT_VZ", "PALANIM_DELAY", "CHANNEL", "TOUCH_STATE",
        "CLIPPING", "TOUCH_TYPE", "TOUCH_MODE", "BUFFER_MODE", "FLOOR_MODE", "CDAUDIO_VOL", "SOUND_VOL",
        "MOVE_MODE", "NEXUS_MODE", "BLUR_MODE", "RENDER_MODE", "KEY_SHIFT", "HIT_Y", "HIT_X", "INERTIA",
        "FRICTION", "SHOOT_ANGLE", "SHOOT_FAC", "HALF_PI", "TWO_PI", "PI", "ACTOR_CEIL_HGT",
        "ACTOR_FLOOR_HGT", "ACTOR_IMPACT_VZ", "ACTOR_IMPACT_VY", "ACTOR_IMPACT_VX", "SKIP_FRAMES",
        "ACTOR_CLIMB", "SHOOT_SECTOR", "THING_DIST", "ACTOR_DIST", "THING_WIDTH", "ACTOR_WIDTH", "SKY_OFFS_Y",
        "SKY_OFFS_X", "AMBIENT", "RECORD", "LINES", "SIZE_Y", "PLAYER_LAST_Y", "PLAYER_LAST_X", "PLAYER_ROLL",
        "PLAYER_DEPTH", "VESA_MODE", "CD_TRACK", "TOTAL_TIME", "TOTAL_FRAMES", "MOUSE_ANGLE", "SLOPE_SIDE",
        "SLOPE_AHEAD", "SLOPE_Y", "SLOPE_X", "CEIL_HGT", "FLOOR_HGT", "WDIST_MINY", "WDIST_MAXY",
        "WDIST_MINX", "WDIST_MAXX", "STEP_DIST", "REAL_SPEED", "MINV_DIST", "INV_DIST", "HIT_MINDIST",
        "LAST_TOUCHED", "LAST_HIT", "JOYSTICK_Y", "JOYSTICK_X", "MOUSE_TIME", "SHIFT_SENSE", "KEY_SENSE",
        "MOUSE_SENSE", "JOY_SENSE", "FRAME_COLOR", "FRAME_Y", "FRAME_X", "FRAME_HGT", "FRAME_WIDTH",
        "SCREEN_Y", "SCREEN_X", "SCREEN_HGT", "SCREEN_WIDTH", "COLOR_WAYS", "COLOR_BORDER", "COLOR_WALLS",
        "COLOR_THINGS", "COLOR_ACTORS", "COLOR_PLAYER", "PANEL_LAYER", "TEXT_LAYER", "MAP_LAYER",
        "MAP_EDGE_Y2", "MAP_EDGE_Y1", "MAP_EDGE_X2", "MAP_EDGE_X1", "MAP_CENTERY", "MAP_CENTERX", "MAP_OFFSY",
        "MAP_OFFSX", "MAP_SCALE", "MAP_DETAIL", "MAP_ROT", "MAP_MODE", "MAP_MAXY", "MAP_MAXX", "MAP_MINY",
        "MAP_MINX", "PLAYER_Z", "WAIT_ACTIONS", "CHANNEL_7", "CHANNEL_6", "CHANNEL_5", "CHANNEL_4",
        "CHANNEL_3", "CHANNEL_2", "CHANNEL_1", "CHANNEL_0", "REMOTE_1", "REMOTE_0", "NODE", "MOUSE_MOVING",
        "MICKEY_Y", "MICKEY_X", "TOUCH_RANGE", "MOUSE_CALM", "ASPECT", "PLAYER_MSIN", "FLIC_FRAME",
        "FLIC_STATUS", "PLAYER_HGT", "HIT_DIST", "FORCE_UP", "RESULT", "FORCE_AHEAD", "FORCE_TILT",
        "FORCE_ROT", "FORCE_STRAFE", "RANDOM", "TIME_FAC", "TIME_CORR", "ACCELERATION", "PLAYER_SPEED",
        "PLAYER_COS", "PLAYER_SIN", "WAVE", "WALK", "MOUSE_MODE", "MOUSE_Y", "MOUSE_X", "DISPLAY_SIZE",
        "SHOOT_Y", "SHOOT_X", "SHOOT_RANGE", "PLAYER_RANGE", "PLAYER_Y", "PLAYER_X", "PLAYER_ANGLE",
        "IMPACT_VY", "IMPACT_VX", "PLAYER_WIDTH", "MOTION_BLUR", "DARK_DIST", "LIGHT_DIST", "PLAYER_LIGHT",
        "PLAYER_ARC", "PLAYER_TILT", "PLAYER_VROT", "PLAYER_VZ", "PLAYER_VY", "PLAYER_VX", "PSOUND_TONE",
        "PSOUND_VOL", "MUSIC_VOL", "WALK_TIME", "WAVE_PERIOD", "WALK_PERIOD", "PLAYER_CLIMB", "PLAYER_SIZE",
        "SECS", "TICKS", "STEPS",
    ];
}
