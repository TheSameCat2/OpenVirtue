// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using static TraceProbeWdl;

/// <summary>
/// The cases of the <c>instructions</c> probe: the region and object instructions the retail
/// scripts use that no earlier probe measured — <c>SHIFT</c>, <c>ROTATE</c>, <c>LIFT</c>,
/// <c>DROP</c>, <c>SET_ALL</c>, <c>PUSH</c> and <c>EXPLODE</c> (manual pp. 75, 90–93). Each carrier
/// prints the skills in question before and after the instruction and for a few cycles on. The
/// region cases use the pad island (x −12…−4, y 12…20) with its wall named <c>in_pw</c>; the pad's
/// <c>GENIUS</c> is an invisible actor at its centre (−8, 16). Positions print as the skills read
/// them (three decimals).
/// </summary>
internal static class InstructionProbeCases
{
    /// <summary>The PRINTFILE base name of the trace file (written as OVINS0.TXT).</summary>
    public const string TraceFile = "ovins";

    private const string Skills =
        "SKILL in_n { VAL 0; }\nSKILL in_v { VAL 0.3; }\nSKILL in_c { VAL 0; }\nSYNONYM in_r { TYPE REGION; }\n" +
        "TEXTURE in_tex { SCALE_X 16; SCALE_Y 16; BMAPS red_map; }\n";

    /// <summary>The pad's own wall: invisible and passable, so the player can walk and be beamed onto the pad.</summary>
    private const string PadWallDeclaration = "WALL in_pw { TEXTURE red_tex; FLAGS INVISIBLE, PASSABLE; }\n";

    private static string L(string token, params string[] extra) => V(token, ["TICKS", .. extra]);

    /// <summary>Prints <paramref name="token"/> with <paramref name="skills"/> once a cycle for <paramref name="cycles"/> cycles.</summary>
    private static string Cycles(string token, int cycles, params string[] skills) =>
        $"    RULE in_c = 0;\n    WHILE (in_c < {cycles})\n    {{\n" + Indent(L(token, ["in_c", .. skills])) + "        RULE in_c = in_c + 1;\n        WAIT 1;\n    }\n";

    private static string Beam(double x, double y, string token) =>
        FormattableString.Invariant($"    SET PLAYER_X, {x};\n    SET PLAYER_Y, {y};\n") + L(token, "PLAYER_X", "PLAYER_Y", "PLAYER_Z") + "    WAIT 2;\n";

    // ---- SHIFT ----------------------------------------------------------------------------------

    private static readonly string[] ShiftSkills = ["in_t.X", "in_t.Y", "in_a.X", "in_a.Y", "PLAYER_X", "PLAYER_Y", "in_pw.X1", "in_pw.Y1", "in_pw.X2", "in_pw.Y2"];

    /// <summary>The pad's own EACH_TICK: shifts, rotates or lifts the pad a little every cycle while <c>in_n</c> is set (the manual's wheel example).</summary>
    private static string PadTick(string instruction) =>
        Action("in_pt", "    IF (in_n > 0) {\n        " + instruction + "\n        RULE in_n = in_n + 1;\n    }\n");

    private static readonly string ShiftDeclarations =
        Skills + PadWallDeclaration + PadTick("SHIFT pad, 0.1, 0;") +
        "THING in_t { TEXTURE in_tex; }\nACTOR in_a { TEXTURE in_tex; SPEED 0; }\n";

    /// <summary>Four cycles, each <paramref name="instruction"/> then a print, then <c>in_n</c> set for three cycles of the pad's own EACH_TICK.</summary>
    private static string Repeated(string instruction, string[] skills) =>
        "    RULE in_c = 0;\n    WHILE (in_c < 4)\n    {\n        " + instruction + "\n        RULE in_c = in_c + 1;\n" + Indent(L("L", ["in_c", .. skills])) + "        WAIT 1;\n    }\n" +
        "    RULE in_n = 1;\n" + Cycles("E", 3, ["in_n", .. skills]) + "    RULE in_n = 0;\n    WAIT 1;\n";

    private static readonly string ShiftRun =
        Beam(-10, 14, "B") + L("S0", ShiftSkills) +
        "    SHIFT pad, 1.5, -0.25;\n" + L("S1", ShiftSkills) + Cycles("C1", 2, ShiftSkills) +
        Repeated("SHIFT pad, in_v, in_v;", ShiftSkills) +
        "    SHIFT pad, 20, 0;\n" + L("S5", ShiftSkills) + Cycles("C5", 2, ShiftSkills) + P("RET");

    // ---- ROTATE ---------------------------------------------------------------------------------

    private static readonly string[] RotateSkills = ["in_t.X", "in_t.Y", "in_t.ANGLE", "in_a.X", "in_a.Y", "in_a.ANGLE", "PLAYER_X", "PLAYER_Y", "PLAYER_ANGLE", "in_pw.X1", "in_pw.Y1", "in_pw.X2", "in_pw.Y2"];

    private static readonly string RotateDeclarations =
        Skills + PadWallDeclaration + PadTick("ROTATE pad, 0.05;") +
        "THING in_t { TEXTURE in_tex; }\nACTOR in_a { TEXTURE in_tex; SPEED 0; }\nACTOR in_g { TEXTURE in_tex; FLAGS INVISIBLE; }\n";

    private static readonly string RotateRun =
        Beam(-10, 16, "B") + L("S0", RotateSkills) +
        "    ROTATE pad, 0.5;\n" + L("S1", RotateSkills) + Cycles("C1", 2, RotateSkills) +
        Repeated("ROTATE pad, in_v;", RotateSkills) +
        "    ROTATE pad, -1;\n" + L("S5", RotateSkills) + Cycles("C5", 2, RotateSkills) + P("RET");

    // ---- LIFT -----------------------------------------------------------------------------------

    private static readonly string[] LiftSkills = ["pad.FLOOR_HGT", "pad.CEIL_HGT", "in_t.Z", "in_a.Z", "PLAYER_Z", "FLOOR_HGT", "CEIL_HGT", "in_pw.Z1", "in_pw.Z2"];

    private static readonly string LiftDeclarations =
        Skills + PadWallDeclaration + PadTick("LIFT pad, 0.5;") +
        "THING in_t { TEXTURE in_tex; }\nACTOR in_a { TEXTURE in_tex; SPEED 0; }\n";

    private static readonly string LiftRun =
        Beam(-8, 14, "B") + Cycles("C0", 2, LiftSkills) +
        "    LIFT pad, 2;\n" + L("S1", LiftSkills) + Cycles("C1", 4, LiftSkills) +
        Repeated("LIFT pad, in_v;", LiftSkills) +
        "    LIFT pad, -4;\n" + L("S5", LiftSkills) + Cycles("C5", 6, LiftSkills) + P("RET");

    // ---- DROP -----------------------------------------------------------------------------------

    private static readonly string[] DropSkills = ["in_d0.X", "in_d0.Y", "in_d0.Z", "in_d6.X", "in_d6.Y", "in_d6.Z", "in_u.X", "in_u.Y", "in_u.Z", "in_c", "PLAYER_X", "PLAYER_Y", "PLAYER_ANGLE", "MOUSE_ANGLE"];

    private const string UnplacedVisible = "    IF (in_u.INVISIBLE == 1) { RULE in_c = 1; } ELSE { RULE in_c = 0; }\n";

    private static readonly string DropDeclarations =
        Skills + PadWallDeclaration +
        "THING in_d0 { TEXTURE in_tex; DIST 0; }\nTHING in_d6 { TEXTURE in_tex; DIST 6; }\nTHING in_u { TEXTURE in_tex; DIST 4; }\n";

    private static readonly string DropRun =
        UnplacedVisible + L("S0", DropSkills) +
        "    SET PLAYER_ANGLE, 0.7;\n    DROP in_d0;\n    DROP in_d6;\n    DROP in_u;\n" + UnplacedVisible + L("S1", DropSkills) + "    WAIT 2;\n" + UnplacedVisible + L("C1", DropSkills) +
        "    SET PLAYER_ANGLE, 2.5;\n    SET MOUSE_ANGLE, 1;\n    DROP in_d0;\n    DROP in_d6;\n    DROP in_u;\n" + UnplacedVisible + L("S2", DropSkills) + "    WAIT 2;\n" + UnplacedVisible + L("C2", DropSkills) +
        Beam(-8, 16, "B") + "    SET PLAYER_ANGLE, 4;\n    DROP in_d6;\n" + UnplacedVisible + L("S3", DropSkills) +
        "    SET in_r, in_d6.REGION;\n    RULE in_n = in_r.FLOOR_HGT;\n" + L("R", "in_n") + P("RET");

    // ---- SET_ALL --------------------------------------------------------------------------------

    private static readonly string SetAllDeclarations =
        Skills +
        "THING in_m { TEXTURE in_tex; SKILL1 0; EACH_TICK in_mt; }\nTHING in_o { TEXTURE in_tex; SKILL1 9; EACH_TICK in_mt; }\n" +
        Action("in_mt", "    IF (in_n > 0) {\n        IF (MY.INVISIBLE == 1) { RULE in_c = 1; } ELSE { RULE in_c = 0; }\n" + Indent(V("M", "TICKS", "MY.X", "in_c", "MY.SKILL1", "MY.SKILL2")) + "    }\n");

    private static readonly string SetAllRun =
        "    RULE in_n = 1;\n    WAIT 2;\n" + P("A") +
        "    SET_ALL in_m.INVISIBLE, 1;\n    SET_ALL in_m.SKILL2, 7;\n    WAIT 2;\n" + P("B") +
        "    SET in_m.INVISIBLE, 0;\n    SET in_m.SKILL1, 5;\n    WAIT 2;\n" + P("C") +
        "    SET_ALL in_m.X, 30;\n    SET_ALL in_m.SKILL1, 4;\n    WAIT 2;\n" + P("D") +
        "    SET_ALL in_m.TEXTURE, red_tex;\n    SET_ALL in_o.SKILL2, 1;\n    WAIT 2;\n" + P("E") +
        "    RULE in_n = 0;\n" + P("RET");

    // ---- PUSH -----------------------------------------------------------------------------------

    private static readonly string HitHook =
        Action("in_hit", V("H", "TICKS", "MY.SKILL1", "HIT_DIST", "RESULT", "HIT_X", "HIT_Y"));

    private static string Thing(string name, int id, string flags = "") =>
        $"THING {name} {{ TEXTURE in_tex; IF_HIT in_hit; SKILL1 {id};{flags} }}\n";

    private static readonly string PushDeclarations =
        Skills + HitHook + PadWallDeclaration +
        "WALL in_hw { TEXTURE red_tex; IF_HIT in_hit; SKILL1 8; }\n" +
        Thing("in_t1", 1) + Thing("in_t2", 2) + Thing("in_t3", 3) + Thing("in_t4", 4) + Thing("in_t5", 5, " FLAGS INVISIBLE;") + Thing("in_t6", 6);

    private static string Push(string amount, string token) =>
        $"    PUSH {amount};\n" + L(token, "HIT_DIST", "RESULT", "HIT_MINDIST");

    private static readonly string PushRun =
        L("S0", "PLAYER_X", "PLAYER_Y", "PLAYER_ANGLE") +
        Push("5", "P5") + Push("7", "P7") + Push("9", "P9") + Push("12", "P12") + Push("18", "P18") + Push("30", "P30") + Push("in_v", "PV") +
        "    SET PLAYER_ANGLE, 1.5708;\n" + Push("9", "Q9") + Push("30", "Q30") +
        "    SET PLAYER_ANGLE, 3.1416;\n" + Push("9", "R9") +
        "    SET PLAYER_ANGLE, 0;\n    WAIT 1;\n" + Push("18", "P18B") + P("RET");

    // ---- EXPLODE --------------------------------------------------------------------------------

    private static readonly string ExplodeDeclarations =
        Skills + HitHook + PadWallDeclaration +
        Thing("in_f3", 3, " FLAGS FRAGILE;") + Thing("in_f6", 6, " FLAGS FRAGILE;") + Thing("in_f12", 12, " FLAGS FRAGILE;") + Thing("in_s4", 4) +
        "ACTOR in_e { TEXTURE in_tex; SPEED 0; IF_HIT in_hit; SKILL1 20; FLAGS FRAGILE; }\n";

    private static string Explode(int range, string token) =>
        $"    SET SHOOT_RANGE, {range};\n    EXPLODE in_e;\n" + L(token, "HIT_DIST", "RESULT", "HIT_MINDIST");

    private static readonly string ExplodeRun =
        L("S0", "SHOOT_RANGE", "PLAYER_X", "PLAYER_Y") +
        Explode(10, "E10") + Explode(4, "E4") + Explode(20, "E20") +
        Beam(-8, 16, "B") + Explode(30, "E30") + P("RET");

    // ---- round 2 variants -----------------------------------------------------------------------

    private static readonly string ShiftShakeRun =
        Beam(-10, 14, "B") + L("S0", ShiftSkills) +
        "    SHIFT pad, 1.5, -0.25;\n    SHAKE in_pw;\n    SHAKE in_t;\n    SHAKE in_a;\n" + L("S1", ShiftSkills) + Cycles("C1", 2, ShiftSkills) +
        Repeated("SHIFT pad, in_v, in_v;\n        SHAKE in_pw;\n        SHAKE in_t;\n        SHAKE in_a;", ShiftSkills) +
        "    SHIFT pad, 20, 0;\n    SHAKE in_pw;\n" + L("S5", ShiftSkills) + Cycles("C5", 2, ShiftSkills) + P("RET");

    private static readonly string ShiftOutRun =
        L("S0", ShiftSkills) +
        "    SHIFT pad, 1.5, -0.25;\n" + L("S1", ShiftSkills) + Cycles("C1", 2, ShiftSkills) +
        Repeated("SHIFT pad, in_v, in_v;", ShiftSkills) +
        "    SHIFT pad, 20, 0;\n" + L("S5", ShiftSkills) + Cycles("C5", 2, ShiftSkills) + P("RET");

    private static readonly string SetAllNearRun =
        "    RULE in_n = 1;\n    WAIT 2;\n" + P("A") +
        "    SET_ALL in_m.INVISIBLE, 1;\n    SET_ALL in_m.SKILL2, 7;\n    WAIT 2;\n" + P("B") +
        "    SET_ALL in_m.INVISIBLE, 0;\n    SET in_m.SKILL1, 5;\n    WAIT 2;\n" + P("C") +
        "    SET_ALL in_m.X, 12;\n    SET_ALL in_m.SKILL1, 4;\n    WAIT 2;\n" + P("D") +
        "    SET in_m.X, 14;\n    SET in_m.INVISIBLE, 1;\n    WAIT 2;\n" + P("E") +
        "    SET_ALL in_m.INVISIBLE, 0;\n    WAIT 2;\n" + P("F") +
        "    RULE in_n = 0;\n" + P("RET");

    private static readonly string PushTwoRun =
        L("S0", "PLAYER_X", "PLAYER_Y", "PLAYER_ANGLE") +
        Push("9", "P9") + Push("12", "P12") + Push("18", "P18") + Push("30", "P30") +
        "    SET PLAYER_ANGLE, 1.5708;\n" + Push("30", "Q30") + "    SET PLAYER_ANGLE, 0;\n    WAIT 1;\n" + Push("18", "P18B") + P("RET");

    private static readonly string PushNoHitDeclarations =
        Skills + HitHook + PadWallDeclaration +
        "THING in_n1 { TEXTURE in_tex; SKILL1 1; }\n" + Thing("in_t2", 2) + Thing("in_t7", 7);

    // ---- round 3 variants -----------------------------------------------------------------------

    // Round 5: Apathy's secret door (lonely.wdl) shifts its region 24 times, 0.25 a cycle, from the
    // region's EACH_TICK without a SHAKE, and the door opens in the original. The pad of rounds 1-3 had
    // an INVISIBLE wall and objects; these carriers take each away.
    private const string VisiblePadWallDeclaration = "WALL in_pw { TEXTURE red_tex; FLAGS PASSABLE; }\n";

    private static readonly string ShiftVisibleDeclarations =
        Skills + VisiblePadWallDeclaration + PadTick("SHIFT pad, 0.1, 0;") +
        "THING in_t { TEXTURE in_tex; }\nACTOR in_a { TEXTURE in_tex; SPEED 0; }\n";

    private static readonly string[] EmptyShiftSkills = ["PLAYER_X", "PLAYER_Y", "in_pw.X1", "in_pw.Y1", "in_pw.X2", "in_pw.Y2"];

    private static string ShiftFacingRun(double angle, string[] skills) =>
        FormattableString.Invariant($"    SET PLAYER_ANGLE, {angle};\n    WAIT 2;\n") + L("S0", skills) +
        "    SHIFT pad, 1.5, -0.25;\n" + L("S1", skills) + Cycles("C1", 2, skills) +
        Repeated("SHIFT pad, in_v, in_v;", skills) +
        "    SHIFT pad, 20, 0;\n" + L("S5", skills) + Cycles("C5", 2, skills) + P("RET");

    private static string ShiftWallDeclarations(string flags) =>
        Skills + $"WALL in_pw {{ TEXTURE red_tex;{flags} }}\n" + PadTick("SHIFT pad, 0.1, 0;");

    private static string ShiftFromRun(double x, double y, double angle, string[] skills) =>
        Beam(x, y, "B") + ShiftFacingRun(angle, skills);

    private static readonly string ShiftTickOnlyRun =
        "    WAIT 2;\n" + L("S0", EmptyShiftSkills) + "    RULE in_n = 1;\n" + Cycles("E", 6, ["in_n", .. EmptyShiftSkills]) + "    RULE in_n = 0;\n    WAIT 1;\n" + L("S5", EmptyShiftSkills) + P("RET");

    private static readonly string ShiftSmallRun =
        "    WAIT 2;\n" + L("S0", EmptyShiftSkills) +
        "    RULE in_c = 0;\n    WHILE (in_c < 6)\n    {\n        SHIFT pad, 0, 0.25;\n        RULE in_c = in_c + 1;\n" + Indent(L("L", ["in_c", .. EmptyShiftSkills])) + "        WAIT 1;\n    }\n" + P("RET");

    private static string ShiftSmallThenRun(string after, string[]? skills = null)
    {
        skills ??= EmptyShiftSkills;
        return "    WAIT 2;\n" + L("S0", skills) +
            "    RULE in_c = 0;\n    WHILE (in_c < 6)\n    {\n        SHIFT pad, 0, 0.25;\n" + after + "        RULE in_c = in_c + 1;\n" + Indent(L("L", ["in_c", .. skills])) + "        WAIT 1;\n    }\n" + P("RET");
    }

    private static readonly string[] MoverShiftSkills = ["in_v", "in_mv.X", "in_mv.Y", "in_pw.X1", "in_pw.Y1", "in_pw.X2", "in_pw.Y2"];

    private static readonly string ShiftOtherDeclarations =
        ShiftWallDeclarations("") + "THING in_o { TEXTURE in_tex; }\nACTOR in_oa { TEXTURE in_tex; SPEED 0; }\n";

    private static readonly string ShiftMoverDeclarations =
        ShiftWallDeclarations("") + "ACTOR in_mv { TEXTURE in_tex; SPEED 0.2; ANGLE 1.5708; }\n";

    private static string ShiftFacingSmallRun(double angle) =>
        FormattableString.Invariant($"    SET PLAYER_ANGLE, {angle};\n") + ShiftSmallThenRun("");

    // Round 10: the range of SHOOT_ANGLE after an actor's SHOOT. Apathy's monsters turn 0.4 a tick
    // toward it until within 0.6, their own ANGLE kept in 0..6.28.
    private static readonly string ShootAngleDeclarations =
        Skills + "ACTOR in_s1 { TEXTURE in_tex; }\nACTOR in_s2 { TEXTURE in_tex; }\nACTOR in_s3 { TEXTURE in_tex; }\nACTOR in_s4 { TEXTURE in_tex; }\nACTOR in_s5 { TEXTURE in_tex; }\n";

    private static string ShootFrom(string actor, string token) =>
        $"    SHOOT {actor};\n" + L(token, "SHOOT_ANGLE", "HIT_DIST", "RESULT");

    private static readonly string ShootAngleRun =
        "    SET SHOOT_RANGE, 200;\n    SET SHOOT_FAC, 1;\n    WAIT 2;\n" +
        ShootFrom("in_s1", "A1") + ShootFrom("in_s2", "A2") + ShootFrom("in_s3", "A3") + ShootFrom("in_s4", "A4") + ShootFrom("in_s5", "A5") +
        "    SET PLAYER_ANGLE, 3.1416;\n    WAIT 2;\n" +
        ShootFrom("in_s1", "B1") + ShootFrom("in_s2", "B2") + ShootFrom("in_s3", "B3") + ShootFrom("in_s4", "B4") + ShootFrom("in_s5", "B5") + P("RET");

    private static string ShiftShakeOnlyRun(string shaken) =>
        Beam(-10, 14, "B") + L("S0", ShiftSkills) +
        $"    SHIFT pad, 1.5, -0.25;\n    SHAKE {shaken};\n" + L("S1", ShiftSkills) + Cycles("C1", 2, ShiftSkills) +
        Repeated($"SHIFT pad, in_v, in_v;\n        SHAKE {shaken};", ShiftSkills) +
        "    SHIFT pad, 20, 0;\n" + L("S5", ShiftSkills) + Cycles("C5", 2, ShiftSkills) + P("RET");

    private static readonly string ShakeThingDeclarations =
        Skills + PadWallDeclaration +
        "THING in_t { TEXTURE in_tex; EACH_TICK in_tt; }\n" +
        Action("in_tt", "    IF (in_n > 0) {\n        SET in_r, MY.REGION;\n        RULE in_c = in_r.SKILL1;\n" + Indent(V("T", "TICKS", "MY.X", "MY.Y", "MY.Z", "in_c", "MY.DISTANCE")) + "    }\n");

    private static readonly string ShakeThingRun =
        "    RULE in_n = 1;\n    WAIT 2;\n" + P("A") +
        "    SET in_t.X, 4;\n    SET in_t.Y, 2;\n    WAIT 3;\n" + P("B") +
        "    SHAKE in_t;\n    WAIT 3;\n" + P("C") +
        "    SET in_t.X, -8;\n    SET in_t.Y, 16;\n    SHAKE in_t;\n    WAIT 3;\n" + P("D") +
        "    SET in_t.Z, 3;\n    WAIT 2;\n" + P("E") + "    SHAKE in_t;\n    WAIT 3;\n" + P("F") +
        "    RULE in_n = 0;\n" + P("RET");

    private static readonly string PushLowRun =
        "    SET PLAYER_SIZE, 3;\n    SET PLAYER_Z, 3;\n    WAIT 3;\n" + L("S0", "PLAYER_X", "PLAYER_Y", "PLAYER_ANGLE", "PLAYER_Z") +
        Push("9", "P9") + Push("12", "P12") + Push("18", "P18") + Push("30", "P30") +
        "    SET PLAYER_ANGLE, 1.5708;\n    WAIT 2;\n" + Push("30", "Q30") + "    SET PLAYER_ANGLE, 0;\n    WAIT 2;\n" + Push("18", "P18B") + P("RET");

    private static readonly string SetAllShakeRun =
        "    RULE in_n = 1;\n    WAIT 2;\n" + P("A") +
        "    SET_ALL in_m.X, 12;\n    WAIT 2;\n" + P("B") +
        "    SHAKE in_m;\n    WAIT 2;\n" + P("C") +
        "    SET in_m.X, 14;\n    WAIT 2;\n" + P("D") + "    SHAKE in_m;\n    WAIT 2;\n" + P("E") +
        "    RULE in_n = 0;\n" + P("RET");

    // ---- round 4: walls under PUSH and SHOOT --------------------------------------------------

    private static readonly string WallHitDeclarations =
        Skills + HitHook + PadWallDeclaration +
        "WALL in_hw { TEXTURE red_tex; IF_HIT in_hit; SKILL1 8; }\n" + Thing("in_t7", 7) + Thing("in_t3", 3);

    private static readonly TraceProbeMap WallHitMap = new(
        PadBody: "SKILL1 2;", PadWall: "in_pw", Walls: [new TraceProbeWall("in_hw", 22, -4, 22, 4)],
        Placements: ["THING in_t7 26 0 0 0", "THING in_t3 0 8 0 0"]);

    private static readonly string PushWallRun =
        L("S0", "PLAYER_X", "PLAYER_Y", "PLAYER_ANGLE") +
        Push("5", "P5") + Push("25", "P25") + Push("30", "P30") +
        "    SET PLAYER_ANGLE, 1.5708;\n    WAIT 2;\n" + Push("30", "Q30") +
        "    SET PLAYER_ANGLE, 3.1416;\n    WAIT 2;\n" + Push("30", "R30") +
        "    SET PLAYER_ANGLE, 0;\n    SET PLAYER_X, 10;\n    WAIT 2;\n" + Push("15", "P15") + Push("11", "P11") + P("RET");

    private static string Shoot(string token) =>
        "    SHOOT;\n" + L(token, "HIT_DIST", "RESULT", "HIT_X", "HIT_Y", "PLAYER_X", "PLAYER_Y", "PLAYER_ANGLE");

    private static readonly string ShootWallRun =
        L("S0", "SHOOT_RANGE", "PLAYER_X", "PLAYER_Y", "PLAYER_ANGLE") +
        Shoot("A0") +
        "    SET PLAYER_X, 10;\n    WAIT 2;\n" + Shoot("A10") +
        "    SET PLAYER_ANGLE, 0.2;\n    WAIT 2;\n" + Shoot("A02") +
        "    SET PLAYER_ANGLE, -0.3;\n    WAIT 2;\n" + Shoot("A03") +
        "    SET PLAYER_Y, 2;\n    SET PLAYER_ANGLE, 0;\n    WAIT 2;\n" + Shoot("AY2") +
        "    SET PLAYER_ANGLE, 1.5708;\n    WAIT 2;\n" + Shoot("AN") +
        "    SET SHOOT_RANGE, 10;\n    SET PLAYER_ANGLE, 0;\n    WAIT 2;\n" + Shoot("AR10") + P("RET");

    public static readonly IReadOnlyList<TraceProbeCase> All =
    [
        new TraceProbeCase(
            "push-wall",
            "Round 4. A solid wall with IF_HIT (SKILL1 8) at x 22 across the view, a thing behind it at 26 and one to the left at 8: does PUSH fire the wall (the retail opens doors with PUSH 18), at which HIT_DIST, is the thing behind it (not drawn) ever hit, and the one to the left when facing it?",
            WallHitDeclarations,
            PushWallRun,
            Predict(
                ("a visible wall is an object for PUSH: the nearest within n fires", "P25: H 8 at about 22; P30: H 8; Q30: H 3 at 8; R30: none; P15: H 8 at 12; P11: none"),
                ("walls never fire under PUSH", "P25, P30: none"),
                ("the thing behind the wall counts though not drawn", "P30: H 7 or H 8")),
            Map: WallHitMap),
        new TraceProbeCase(
            "shoot-wall",
            "Round 4. SHOOT at the same solid wall from the origin and from 12 away, straight on and 0.2 and −0.3 rad off, from y 2, facing away, and with SHOOT_RANGE 10 from 12 away: HIT_DIST (the depth along the sight line or the ray), HIT_X/HIT_Y on a wall (no wall hit was probed in OV-20261009-shoot-mapping), and the miss.",
            WallHitDeclarations,
            ShootWallRun,
            Predict(
                ("HIT_DIST is the depth along the sight line, HIT_X/HIT_Y the texel", "A0 22, A10 12, A02 12 (ray 12.24), A03 12 (ray 12.57); AN 0; AR10 0"),
                ("HIT_DIST is the ray's length", "A02 12.245, A03 12.574"),
                ("HIT_X/HIT_Y are the hit's map position", "A0 HIT_X 22 HIT_Y 0")),
            Map: WallHitMap),

        new TraceProbeCase(
            "shift-visible",
            "Round 5. The shift-out case with the pad's wall visible (red_tex, PASSABLE) and the player at the origin facing the pad (2.03 rad): do later shifts apply once the wall is drawn?",
            ShiftVisibleDeclarations,
            ShiftFacingRun(2.03, ShiftSkills),
            Predict(("a drawn region takes every shift", "L lines +0.3 a cycle, E +0.1, S5 +20"), ("still only the first", "L, E, S5 as S1")),
            Map: new TraceProbeMap(PadBody: "SKILL1 2;\n    EACH_TICK in_pt;", PadWall: "in_pw", Placements: ["THING in_t -8 16 0 2", "ACTOR in_a -6 18 0 2"])),
        new TraceProbeCase(
            "shift-visible-away",
            "Round 5. shift-visible with the player facing away from the pad (5.17 rad): is it the drawing or the wall's flags?",
            ShiftVisibleDeclarations,
            ShiftFacingRun(5.17, ShiftSkills),
            Predict(("only a drawn region takes later shifts", "L, E, S5 as S1"), ("a visible-flagged wall is enough", "L lines +0.3 a cycle")),
            Map: new TraceProbeMap(PadBody: "SKILL1 2;\n    EACH_TICK in_pt;", PadWall: "in_pw", Placements: ["THING in_t -8 16 0 2", "ACTOR in_a -6 18 0 2"])),
        new TraceProbeCase(
            "shift-empty",
            "Round 5. The shift-out case (invisible wall, player at the origin facing 0) with no thing or actor on the pad: is it the region's objects that hold later shifts back?",
            ShiftDeclarations,
            ShiftFacingRun(0, EmptyShiftSkills),
            Predict(("an empty region takes every shift", "L lines +0.3 a cycle, E +0.1, S5 +20"), ("still only the first", "L, E, S5 as S1")),
            Map: new TraceProbeMap(PadBody: "SKILL1 2;\n    EACH_TICK in_pt;", PadWall: "in_pw")),
        new TraceProbeCase(
            "shift-visible-empty",
            "Round 5. Apathy's door as near as the pad comes: a visible wall, no objects, the player facing the pad.",
            ShiftVisibleDeclarations,
            ShiftFacingRun(2.03, EmptyShiftSkills),
            Predict(("every shift applies", "L lines +0.3 a cycle, E +0.1, S5 +20"), ("still only the first", "L, E, S5 as S1")),
            Map: new TraceProbeMap(PadBody: "SKILL1 2;\n    EACH_TICK in_pt;", PadWall: "in_pw")),

        new TraceProbeCase(
            "shift-near-solid",
            "Round 6. Apathy's secret door shifts every cycle with the player close by facing away. The empty pad with a solid visible wall, the player 1.5 below it facing away.",
            ShiftWallDeclarations(""),
            ShiftFromRun(-8, 10.5, 4.71, EmptyShiftSkills),
            Predict(("every shift applies", "L lines +0.3 a cycle, E +0.1, S5 +20"), ("still only the first", "L, E, S5 as S1")),
            Map: new TraceProbeMap(PadBody: "SKILL1 2;\n    EACH_TICK in_pt;", PadWall: "in_pw")),
        new TraceProbeCase(
            "shift-near-passable",
            "Round 6. As shift-near-solid with the wall PASSABLE.",
            ShiftWallDeclarations(" FLAGS PASSABLE;"),
            ShiftFromRun(-8, 10.5, 4.71, EmptyShiftSkills),
            Predict(("every shift applies", "L lines +0.3 a cycle, E +0.1, S5 +20"), ("still only the first", "L, E, S5 as S1")),
            Map: new TraceProbeMap(PadBody: "SKILL1 2;\n    EACH_TICK in_pt;", PadWall: "in_pw")),
        new TraceProbeCase(
            "shift-near-invisible",
            "Round 6. As shift-near-solid with the wall INVISIBLE (solid).",
            ShiftWallDeclarations(" FLAGS INVISIBLE;"),
            ShiftFromRun(-8, 10.5, 4.71, EmptyShiftSkills),
            Predict(("every shift applies", "L lines +0.3 a cycle, E +0.1, S5 +20"), ("still only the first", "L, E, S5 as S1")),
            Map: new TraceProbeMap(PadBody: "SKILL1 2;\n    EACH_TICK in_pt;", PadWall: "in_pw")),
        new TraceProbeCase(
            "shift-far-solid",
            "Round 6. As shift-near-solid with the player at the origin facing away (5.17).",
            ShiftWallDeclarations(""),
            ShiftFromRun(0, 0, 5.17, EmptyShiftSkills),
            Predict(("every shift applies", "L lines +0.3 a cycle, E +0.1, S5 +20"), ("still only the first", "L, E, S5 as S1")),
            Map: new TraceProbeMap(PadBody: "SKILL1 2;\n    EACH_TICK in_pt;", PadWall: "in_pw")),
        new TraceProbeCase(
            "shift-far-solid-facing",
            "Round 6. As shift-far-solid facing the pad (2.03).",
            ShiftWallDeclarations(""),
            ShiftFromRun(0, 0, 2.03, EmptyShiftSkills),
            Predict(("every shift applies", "L lines +0.3 a cycle, E +0.1, S5 +20"), ("still only the first", "L, E, S5 as S1")),
            Map: new TraceProbeMap(PadBody: "SKILL1 2;\n    EACH_TICK in_pt;", PadWall: "in_pw")),

        new TraceProbeCase(
            "shift-tick-only",
            "Round 7. The empty pad (solid visible wall) shifted only by its own EACH_TICK (0.1, 0) for six cycles, with no SHIFT from an action before.",
            ShiftWallDeclarations(""),
            ShiftTickOnlyRun,
            Predict(("every shift applies", "y +0.25 or x +0.1 a cycle"), ("only the first", "constant after the first")),
            Map: new TraceProbeMap(PadBody: "SKILL1 2;\n    EACH_TICK in_pt;", PadWall: "in_pw")),
        new TraceProbeCase(
            "shift-small",
            "Round 7. The empty pad shifted by SHIFT pad, 0, 0.25 from the action six cycles running, as Apathy's door, without the first big shift.",
            ShiftWallDeclarations(""),
            ShiftSmallRun,
            Predict(("every shift applies", "y +0.25 or x +0.1 a cycle"), ("only the first", "constant after the first")),
            Map: new TraceProbeMap(PadBody: "SKILL1 2;\n    EACH_TICK in_pt;", PadWall: "in_pw")),

        new TraceProbeCase(
            "shift-shake-other",
            "Round 7. shift-small with SHAKE of a thing in the room (not the pad's) after every SHIFT: is the release global?",
            ShiftOtherDeclarations,
            ShiftSmallThenRun("        SHAKE in_o;\n"),
            Predict(("every shift applies", "y +0.25 a cycle"), ("only the first", "constant after the first")),
            Map: new TraceProbeMap(PadBody: "SKILL1 2;\n    EACH_TICK in_pt;", PadWall: "in_pw", Placements: ["THING in_o 20 0 0 0"])),
        new TraceProbeCase(
            "shift-actor-ahead",
            "Round 7. shift-small with an actor (SPEED 0) of the room straight ahead at (10, 0) in view: does a drawn actor elsewhere release it?",
            ShiftOtherDeclarations,
            ShiftSmallThenRun(""),
            Predict(("every shift applies", "y +0.25 a cycle"), ("only the first", "constant after the first")),
            Map: new TraceProbeMap(PadBody: "SKILL1 2;\n    EACH_TICK in_pt;", PadWall: "in_pw", Placements: ["ACTOR in_oa 10 0 0 0"])),
        new TraceProbeCase(
            "shift-thing-ahead",
            "Round 7. shift-small with a thing of the room straight ahead at (10, 0) in view.",
            ShiftOtherDeclarations,
            ShiftSmallThenRun(""),
            Predict(("every shift applies", "y +0.25 a cycle"), ("only the first", "constant after the first")),
            Map: new TraceProbeMap(PadBody: "SKILL1 2;\n    EACH_TICK in_pt;", PadWall: "in_pw", Placements: ["THING in_o 10 0 0 0"])),

        new TraceProbeCase(
            "shift-mover-ahead",
            "Round 8. shift-small with an actor of the room walking on TARGET MOVE (SPEED 0.2) straight ahead at (10, -8): does an engine-moved actor release the region?",
            ShiftMoverDeclarations,
            "    SET in_mv.TARGET, MOVE;\n" + ShiftSmallThenRun("        IF (in_mv.VISIBLE == 1) { RULE in_v = 1; } ELSE { RULE in_v = 0; }\n", MoverShiftSkills),
            Predict(("every shift applies", "y +0.25 a cycle"), ("only the first", "constant after the first")),
            Map: new TraceProbeMap(PadBody: "SKILL1 2;\n    EACH_TICK in_pt;", PadWall: "in_pw", Placements: ["ACTOR in_mv 10 -8 0 0"])),
        new TraceProbeCase(
            "shift-mover-behind",
            "Round 8. As shift-mover-ahead with the walker behind the player at (-10, -16), out of view.",
            ShiftMoverDeclarations,
            "    SET in_mv.TARGET, MOVE;\n" + ShiftSmallThenRun("        IF (in_mv.VISIBLE == 1) { RULE in_v = 1; } ELSE { RULE in_v = 0; }\n", MoverShiftSkills),
            Predict(("every shift applies", "y +0.25 a cycle"), ("only the first", "constant after the first")),
            Map: new TraceProbeMap(PadBody: "SKILL1 2;\n    EACH_TICK in_pt;", PadWall: "in_pw", Placements: ["ACTOR in_mv -10 -16 0 0"])),

        new TraceProbeCase(
            "shift-block-facing",
            "Round 9. Apathy's door is a block (CEIL_HGT = FLOOR_HGT): the pad as a block, its solid wall drawn whole, the player at the origin facing it, SHIFT pad, 0, 0.25 six cycles running.",
            ShiftWallDeclarations(""),
            ShiftFacingSmallRun(2.03),
            Predict(("every shift applies", "y +0.25 a cycle"), ("only the first", "constant after the first")),
            Map: new TraceProbeMap(PadBody: "SKILL1 2;\n    EACH_TICK in_pt;", PadWall: "in_pw", PadCeiling: 0)),
        new TraceProbeCase(
            "shift-block-away",
            "Round 9. shift-block-facing with the player facing away (5.17).",
            ShiftWallDeclarations(""),
            ShiftFacingSmallRun(5.17),
            Predict(("every shift applies", "y +0.25 a cycle"), ("only the first", "constant after the first")),
            Map: new TraceProbeMap(PadBody: "SKILL1 2;\n    EACH_TICK in_pt;", PadWall: "in_pw", PadCeiling: 0)),

        new TraceProbeCase(
            "shoot-angle",
            "Round 10. Actors at (20, 0), (0, 20), (0, -20), (-10, 0) and (14, 14) SHOOT from the player at the origin, facing 0 then pi: what range does SHOOT_ANGLE (the angle from the actor to the player) read in?",
            ShootAngleDeclarations,
            ShootAngleRun,
            Predict(("0..2pi", "A1 3.141, A2 4.712, A3 1.570, A4 0, A5 3.927"), ("-pi..pi", "A2 -1.570, A5 -2.356")),
            Map: new TraceProbeMap(Placements: ["ACTOR in_s1 20 0 0 0", "ACTOR in_s2 0 20 0 0", "ACTOR in_s3 0 -20 0 0", "ACTOR in_s4 -10 0 0 0", "ACTOR in_s5 14 14 0 0"])),

        new TraceProbeCase(
            "shift-shake-wall",
            "Round 3. shift-shake shook the pad's wall, thing and actor after every SHIFT and every one applied. Here only the wall is shaken (SHAKE in_pw): enough?",
            ShiftDeclarations,
            ShiftShakeOnlyRun("in_pw"),
            Predict(("the wall's SHAKE releases the region", "L lines +0.3 a cycle"), ("not enough", "L lines as S1")),
            Map: new TraceProbeMap(PadBody: "SKILL1 2;\n    EACH_TICK in_pt;", PadWall: "in_pw", Placements: ["THING in_t -8 16 0 2", "ACTOR in_a -6 18 0 2"])),
        new TraceProbeCase(
            "shift-shake-thing",
            "Round 3. Only the thing on the pad is shaken (SHAKE in_t) after every SHIFT: enough?",
            ShiftDeclarations,
            ShiftShakeOnlyRun("in_t"),
            Predict(("a thing's SHAKE releases the region", "L lines +0.3 a cycle"), ("not enough", "L lines as S1")),
            Map: new TraceProbeMap(PadBody: "SKILL1 2;\n    EACH_TICK in_pt;", PadWall: "in_pw", Placements: ["THING in_t -8 16 0 2", "ACTOR in_a -6 18 0 2"])),
        new TraceProbeCase(
            "shake-thing",
            "Round 3. A thing of the pad's region (SKILL1 2) with an EACH_TICK printing its X, Y, Z, its region's SKILL1 and DISTANCE: SET X/Y to (4, 2) in the room (SKILL1 0 there): does its REGION follow without SHAKE (B), after SHAKE (C), back onto the pad with SHAKE (D), and does a SET Z hold without (E) and with SHAKE (F)?",
            ShakeThingDeclarations,
            ShakeThingRun,
            Predict(
                ("REGION follows only after SHAKE", "T lines after B: in_c 2 at (4, 2); after C: in_c 0"),
                ("REGION follows at once", "after B: in_c 0"),
                ("Z is put on the floor by SHAKE", "after E: Z 3; after F: Z 0")),
            Map: new TraceProbeMap(PadBody: "SKILL1 2;", PadWall: "in_pw", Placements: ["THING in_t -8 16 0 2"])),
        new TraceProbeCase(
            "push-low",
            "Round 3. The push case with the eye at 3 (PLAYER_SIZE/PLAYER_Z 3): rounds 1 and 2 hit only the thing at 16 from the origin whatever the push range or the facing, never the one at 10 — read as the nearest object VISIBLE in the last frame, the 2-high thing at 10 being cut by the bottom of the screen with the eye at 8. With the eye at 3: is the thing at 10 hit by PUSH 12 and 18?",
            PushDeclarations,
            PushLowRun,
            Predict(
                ("the nearest visible object", "P12, P18: H 1 at 10; Q30 (facing +Y, a frame later): H 3 at 8 or none"),
                ("still the thing at 16", "P18: H 2")),
            Map: new TraceProbeMap(PadBody: "SKILL1 2;", PadWall: "in_pw", Walls: [new TraceProbeWall("in_hw", 22, -4, 22, 4)],
                Placements: ["THING in_t1 10 0 0 0", "THING in_t2 16 0 0 0", "THING in_t3 0 8 0 0", "THING in_t4 -7 0 0 0", "THING in_t5 6 0 0 0", "THING in_t6 30 0 0 0"])),
        new TraceProbeCase(
            "set-all-shake",
            "Round 3. SET_ALL in_m.X, 12 stopped the three things' EACH_TICKs for good in round 2. Does SHAKE in_m bring one (the first placed) back, and does SET in_m.X, 14 (one object) stop that one too until a SHAKE?",
            SetAllDeclarations,
            SetAllShakeRun,
            Predict(
                ("SHAKE re-registers the first placed", "after C: one M line at X 12"),
                ("SET X stops the object until SHAKE", "after D: no in_m line; after E: one at 14"),
                ("SET X moves it and it keeps running", "after D: one M line at 14")),
            Map: new TraceProbeMap(RoomBody: "CLIP_DIST 100;", Placements: ["THING in_m 10 -10 0 0", "THING in_m 10 0 0 0", "THING in_m 10 10 0 0", "THING in_o 20 0 0 0"])),

        new TraceProbeCase(
            "shift-shake",
            "Round 2. The shift case with SHAKE in_pw, in_t, in_a after every SHIFT: round 1 saw only the first SHIFT, ROTATE or LIFT of a run move anything (walls and the pad's objects), every later one nothing. Does a SHAKE let the next one through?",
            ShiftDeclarations,
            ShiftShakeRun,
            Predict(
                ("SHAKE lets later shifts apply", "L lines +0.3 a cycle, E lines +0.1, S5 +20"),
                ("still only the first", "L, E, S5 as S1")),
            Map: new TraceProbeMap(PadBody: "SKILL1 2;\n    EACH_TICK in_pt;", PadWall: "in_pw", Placements: ["THING in_t -8 16 0 2", "ACTOR in_a -6 18 0 2"])),
        new TraceProbeCase(
            "shift-save",
            "Round 2. The shift case with FLAGS SAVE on the pad (the manual's wheel example sets it): do later shifts apply?",
            ShiftDeclarations,
            ShiftRun,
            Predict(
                ("SAVE lets later shifts apply", "L lines +0.3 a cycle, E +0.1, S5 +20"),
                ("still only the first", "L, E, S5 as S1")),
            Map: new TraceProbeMap(PadBody: "SKILL1 2;\n    EACH_TICK in_pt;\n    FLAGS SAVE;", PadWall: "in_pw", Placements: ["THING in_t -8 16 0 2", "ACTOR in_a -6 18 0 2"])),
        new TraceProbeCase(
            "shift-out",
            "Round 2. The shift case with the player left at the origin, outside the pad: do later shifts apply?",
            ShiftDeclarations,
            ShiftOutRun,
            Predict(
                ("with the player outside every shift applies", "L lines +0.3 a cycle, E +0.1, S5 +20"),
                ("still only the first", "L, E, S5 as S1")),
            Map: new TraceProbeMap(PadBody: "SKILL1 2;\n    EACH_TICK in_pt;", PadWall: "in_pw", Placements: ["THING in_t -8 16 0 2", "ACTOR in_a -6 18 0 2"])),
        new TraceProbeCase(
            "set-all-near",
            "Round 2. set-all with CLIP_DIST 100 (round 1 lost the thing moved to x 30 from the EACH_TICK lines) and SET_ALL in_m.X, 12: are the positions changed (manual p.75 says not), does SET change the first placement only, and does SET_ALL INVISIBLE 0 bring all three back (INVISIBLE things run no EACH_TICK)?",
            SetAllDeclarations,
            SetAllNearRun,
            Predict(
                ("positions are not changed by SET_ALL", "after D: three M lines at X 10 with SKILL1 4"),
                ("SET_ALL moves them", "after D: three M lines at X 12"),
                ("SET moves and hides the first only", "after E: two M lines at 12 (or 10)")),
            Map: new TraceProbeMap(RoomBody: "CLIP_DIST 100;", Placements: ["THING in_m 10 -10 0 0", "THING in_m 10 0 0 0", "THING in_m 10 10 0 0", "THING in_o 20 0 0 0"])),
        new TraceProbeCase(
            "push-two",
            "Round 2. Round 1's PUSH hit only the thing at 16 (SKILL1 2, placed second), never the one at 10 (SKILL1 1, placed first) nor any other. Here the thing at 16 is placed first and the one at 10 second: is it the placement, the distance or the SKILL1 value?",
            PushDeclarations,
            PushTwoRun,
            Predict(
                ("the second placed object", "P12/P18: H 1 at 10"),
                ("the thing at 16 whatever its place", "P18: H 2 at 16, P12 none"),
                ("the nearest visible", "P12: H 1 at 10")),
            Map: new TraceProbeMap(PadBody: "SKILL1 2;", PadWall: "in_pw", Placements: ["THING in_t2 16 0 0 0", "THING in_t1 10 0 0 0"])),
        new TraceProbeCase(
            "push-nohit",
            "Round 2. A thing without IF_HIT at 10 (in_n1), one with at 16 (in_t2) and one with at 24 (in_t7): does PUSH 12 hit nothing (the nearest has no hook) or the first with a hook; PUSH 18, 30?",
            PushNoHitDeclarations,
            PushTwoRun,
            Predict(
                ("the nearest object within n, hook or not", "P12: none; P18: H 2 at 16; P30: H 2"),
                ("the nearest object with a hook", "P12: none; P18: H 2; P30: H 2 (not 7)"),
                ("the last placed within n", "P30: H 7 at 24")),
            Map: new TraceProbeMap(PadBody: "SKILL1 2;", PadWall: "in_pw", Placements: ["THING in_n1 10 0 0 0", "THING in_t2 16 0 0 0", "THING in_t7 24 0 0 0"])),

        new TraceProbeCase(
            "shift",
            "SHIFT pad, dx, dy with a thing and a standing actor of the pad's region and the player on it: which of them move, do the pad's walls (in_pw.X1/Y1/X2/Y2), do four 0.3 skill shifts a cycle apart (L) and three 0.1 shifts from the pad's own EACH_TICK (E) each apply (round 1 saw only the first of five instructions move the walls), and a shift by 20?",
            ShiftDeclarations,
            ShiftRun,
            Predict(
                ("things, actors and vertices move, the player stays (manual p.93)", "S1: in_t/in_a +1.5/−0.25, in_pw.X1.. +1.5/−0.25, PLAYER unchanged"),
                ("the player on the pad moves too", "PLAYER_X/Y +1.5/−0.25 at S1"),
                ("three 0.3 shifts give 0.899 (307/1024 each)", "S4 − S1 = 0.899 or 0.900")),
            Map: new TraceProbeMap(PadBody: "SKILL1 2;\n    EACH_TICK in_pt;", PadWall: "in_pw", Placements: ["THING in_t -8 16 0 2", "ACTOR in_a -6 18 0 2"])),
        new TraceProbeCase(
            "rotate",
            "ROTATE pad, angle about the GENIUS actor at the pad's centre (−8, 16): do the thing at (−6, 16), the actor at (−8, 18) (both of the pad's region), the player at (−10, 16) and the walls turn, by what rounding, do the objects' ANGLEs turn with them, and do four 0.3 rotations a cycle apart (L) and three 0.05 from the pad's EACH_TICK (E) each apply?",
            RotateDeclarations,
            RotateRun,
            Predict(
                ("walls and objects turn about the GENIUS, the player does not", "S1: in_t at (−8 + 2cos0.5, 16 + 2sin0.5) = (−6.245, 16.959); in_pw corners turned; PLAYER unchanged"),
                ("ANGLE turns with the region", "in_t.ANGLE, in_a.ANGLE +0.5 at S1"),
                ("ANGLE stays", "ANGLE 0 throughout"),
                ("−1 then 0.3 come back to 0.3 total", "S4 at the angle 0.3")),
            Map: new TraceProbeMap(PadBody: "SKILL1 2;\n    GENIUS in_g;\n    EACH_TICK in_pt;", PadWall: "in_pw", Placements: ["THING in_t -6 16 0 2", "ACTOR in_a -8 18 0 2", "ACTOR in_g -8 16 0 2"])),
        new TraceProbeCase(
            "lift",
            "LIFT pad, h with a thing, an actor and the player on the pad: does the region's FLOOR_HGT rise, the walls' Z1/Z2, the objects' Z, and does the player follow (PLAYER_Z, FLOOR_HGT) in the next cycles? Then four 0.3 skill lifts a cycle apart, three 0.5 lifts from the pad's own EACH_TICK, and −4.",
            LiftDeclarations,
            LiftRun,
            Predict(
                ("vertices and objects rise, the player follows the floor in the next cycle", "S1: pad.FLOOR_HGT 2 (or the walls' Z1/Z2 2), in_t.Z/in_a.Z +2; C1: PLAYER_Z up by 2 within a cycle or two"),
                ("the player is lifted at once", "PLAYER_Z +2 at S1"),
                ("the player does not follow", "PLAYER_Z unchanged through C1")),
            Map: new TraceProbeMap(PadBody: "SKILL1 2;\n    EACH_TICK in_pt;", PadWall: "in_pw", Placements: ["THING in_t -8 16 0 2", "ACTOR in_a -6 18 0 2"])),
        new TraceProbeCase(
            "drop",
            "DROP of a placed thing with DIST 0 and 6 and of an unplaced one with DIST 4, the player at the origin facing 0.7 rad, then 2.5 rad with MOUSE_ANGLE 1 set, then from the pad: where do they land (X, Y, Z, REGION), does the unplaced one become visible, and does MOUSE_ANGLE steer the drop without the mouse pointer?",
            DropDeclarations,
            DropRun,
            Predict(
                ("DIST along PLAYER_ANGLE from the player, Z on the floor", "S1: in_d6 at (6cos0.7, 6sin0.7) = (4.589, 3.865), in_d0 at (0, 0), in_u at (3.059, 2.577)"),
                ("MOUSE_ANGLE steers when set", "S2: in_d6 at (6cos1, 6sin1) = (3.242, 5.049)"),
                ("PLAYER_ANGLE steers", "S2: in_d6 at (6cos2.5, 6sin2.5) = (−4.807, 3.591)"),
                ("Z keeps the thing's height", "Z unchanged from S0")),
            Map: new TraceProbeMap(PadBody: "SKILL1 2;\n    FLOOR_HGT 1;", PadWall: "in_pw", PadFloor: 1, Placements: ["THING in_d0 10 -20 0 0", "THING in_d6 12 -20 0 0"])),
        new TraceProbeCase(
            "set-all",
            "SET_ALL on a thing placed three times (in_m at (10, −10), (10, 0), (10, 10)) and another (in_o): do all three placements take INVISIBLE 1 and SKILL2 7 where SET changes one, is SET_ALL ...X ignored (manual p.75), and does SET_ALL in_m.TEXTURE change them all? M lines: X, INVISIBLE, SKILL1, SKILL2 from each thing's EACH_TICK.",
            SetAllDeclarations,
            SetAllRun,
            Predict(
                ("SET_ALL reaches every placement, SET the first", "after B: three M lines with INVISIBLE 1 SKILL2 7; after C: one M line with INVISIBLE 0 SKILL1 5"),
                ("SET_ALL ...X is ignored", "after D: X unchanged, SKILL1 4 on all three"),
                ("SET changes all placements too", "after C: three M lines with INVISIBLE 0")),
            Map: new TraceProbeMap(Placements: ["THING in_m 10 -10 0 0", "THING in_m 10 0 0 0", "THING in_m 10 10 0 0", "THING in_o 20 0 0 0"])),
        new TraceProbeCase(
            "push",
            "PUSH n from the origin with things (IF_HIT printing SKILL1, HIT_DIST, RESULT) ahead at 6 (INVISIBLE), 10, 16 and 30, left at 8, behind at 7, and a solid wall with IF_HIT at x = 22: which fires for 5, 7, 9, 12, 18, 30 and a 0.3 skill facing +X (round 1 hit the thing at 16 with PUSH 18 and not the one at 10), facing +Y (9, 30), facing −X (9), and what do HIT_DIST, RESULT and HIT_MINDIST read?",
            PushDeclarations,
            PushRun,
            Predict(
                ("the nearest visible object in the view within n fires, RESULT 1 − distance/n", "P9: none (the 6 is INVISIBLE); P12: H 1 at 10; P18: H 1; Q9: H 3 at 8; R9: H 4 at 7"),
                ("the last placed object within n fires whatever the view", "P18: H 2 at 16; Q30, R9: H 2 or H 6"),
                ("walls with IF_HIT fire", "P30: H 8 at 22 or H 6 at 30")),
            Map: new TraceProbeMap(PadBody: "SKILL1 2;", PadWall: "in_pw", Walls: [new TraceProbeWall("in_hw", 22, -4, 22, 4)],
                Placements: ["THING in_t1 10 0 0 0", "THING in_t2 16 0 0 0", "THING in_t3 0 8 0 0", "THING in_t4 -7 0 0 0", "THING in_t5 6 0 0 0", "THING in_t6 30 0 0 0"])),
        new TraceProbeCase(
            "explode",
            "EXPLODE in_e (an actor at (20, 0)) with FRAGILE things 3, 6 and 12 away and a plain one 4 away, SHOOT_RANGE 10, 4 and 20, then from the pad with 30 (the player 36 away): which IF_HITs fire, with what RESULT by distance, and what do HIT_DIST, RESULT and HIT_MINDIST read after?",
            ExplodeDeclarations,
            ExplodeRun,
            Predict(
                ("FRAGILE objects within SHOOT_RANGE fire, RESULT 1 − distance/range (manual p.91: 0 to 1)", "E10: H 3 and H 6 with RESULT 0.7 and 0.4, not H 4 or H 12; E4: H 3 only; E20: H 3, 6, 12"),
                ("the exploding actor hits itself too", "H 20 in every line"),
                ("the player is hit when within range", "HIT_DIST set at E30 (the player 36 away: no)")),
            Map: new TraceProbeMap(PadBody: "SKILL1 2;", PadWall: "in_pw", Placements: ["ACTOR in_e 20 0 0 0", "THING in_f3 23 0 0 0", "THING in_f6 20 6 0 0", "THING in_f12 8 0 0 0", "THING in_s4 20 -4 0 0"])),
    ];

    /// <summary>The probe as the generator consumes it.</summary>
    public static readonly TraceProbeSet Set = new(
        "instructions",
        TraceFile,
        "ovinss",
        "Every line is a token and `TICKS`, then the case's skills in the order its question names them\n" +
        "(`C*` lines carry a cycle counter first). `B` is a beam of the player (PLAYER_X/Y/Z after it). Compare\n" +
        "the trace with `case.json` → `predictions`, check `OVCON.TXT` for compile errors, and run\n" +
        "`ovtool oracle diff` on the probe to see where OpenVirtue differs.",
        All);
}
