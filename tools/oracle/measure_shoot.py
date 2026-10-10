#!/usr/bin/env python3
# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 The OpenVirtue Authors
"""Reduce OV-20261009-shoot-mapping probe outputs to one CSV row per variant.

Each variant folder (written by ``ovtool oracle prepare shoot-mapping``) holds a
``geometry.json`` whose ``shoot`` section carries the fixture and the predicted hit under the
shared-projection model next to the competing readings, and a ``work/`` directory the patched
DOS runtime wrote into:

- ``OVHIT0.TXT``  — the target's IF_HIT action: its letter (L/C/R) and ``HIN_*`` values
- ``OVSHO0.TXT``  — ``PRE`` written before SHOOT, then ``HOUT_*`` values written right after it
- ``OVPRB0.TXT``  — the pre-shot skill dump (``SHX=``, ``SHY=``, ``SHRNG=``, ``SHFAC=``, ``TCK=`` …)
- ``OVSHT0.PCX``  — a screenshot after the shot (sprite / wall texture orientation)

The script reports raw values and, per question, which prediction the observation is nearest
to; it never adjusts the data. Everything stays local (``_research/oracle-runs/``).

Usage:
    measure_shoot.py <variant-dir>... [--no-header]
    measure_shoot.py --selftest
"""
from __future__ import annotations

import argparse
import csv
import json
import re
import sys
import tempfile
from pathlib import Path

import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parent))
from measure_projection import read_pcx  # noqa: E402  (own PCX decoder, no Pillow needed)

VALUE_RE = re.compile(r"^\s*([A-Za-z_]+)=\s*(-?\d+(?:\.\d+)?)\s*$", re.M)
LETTER_RE = re.compile(r"^\s*([LCR])\s*$", re.M)

COLUMNS = [
    "variant", "shoot_x", "shoot_y", "player_tilt", "status", "flags",
    "state_SHX", "state_SHY", "state_SHRNG", "state_SHFAC", "state_TCK", "state_ANG", "state_PZ",
    "pre_marker", "hit_letters",
    "hin_D", "hin_X", "hin_Y", "hin_R", "hin_T",
    "hout_D", "hout_X", "hout_Y", "hout_R", "hout_T",
    "pred_target", "pred_texel_x", "pred_texel_y", "pred_dist_forward", "pred_dist_ray",
    "target_match", "lateral_model", "lateral_delta_px", "vertical_model", "vertical_delta_px",
    "dist_model", "dist_delta", "result_expected", "result_delta",
    "sprite_count", "sprite_columns", "sprite_left_halves", "face_left_color", "image",
]

TARGET_LETTERS = {"tgt_left": "L", "tgt_center": "C", "tgt_right": "R"}


def fmt(value):
    if value is None:
        return ""
    if isinstance(value, float):
        return f"{value:.3f}"
    return str(value)


def read_latin1(path: Path) -> str:
    return path.read_text(encoding="latin-1", errors="replace")


def find_file(work: Path, name: str) -> Path | None:
    exact = work / name
    if exact.exists():
        return exact
    if not work.exists():
        return None
    candidates = sorted(work.glob(name[:5] + "*" + exact.suffix.upper())) + sorted(work.glob(name[:5] + "*" + exact.suffix.lower()))
    return candidates[0] if candidates else None


def parse_values(text: str) -> dict[str, float]:
    return {m.group(1).upper(): float(m.group(2)) for m in VALUE_RE.finditer(text)}


def nearest(observed: float | None, candidates: dict[str, float | None]):
    """Name and signed delta (observed − predicted) of the nearest non-null candidate."""
    if observed is None:
        return None, None
    best = None
    for name, value in candidates.items():
        if value is None:
            continue
        delta = observed - value
        if best is None or abs(delta) < abs(best[1]):
            best = (name, delta)
    return best if best else (None, None)


def texel(obj, axis):
    if not obj:
        return None
    t = obj.get("texel")
    return None if not t else t.get(axis)


# --------------------------------------------------------------------------- screenshot

def sprite_orientation(rgb: np.ndarray, colors: dict[str, list[int]], tolerance: float = 40.0):
    """Which bitmap half appears on the screen left of every target sprite and of the far-wall face.

    Samples rows around the eye line (the targets span the whole room height, the face the rows
    the center target leaves free), labels pixels by nearest of the four marker colors and
    groups adjacent target_left/target_right runs into sprite blobs.
    """
    names = [n for n in ("target_left", "target_right", "yellow", "mark") if n in colors]
    table = np.array([colors[n] for n in names], dtype=np.float32)
    h, w = rgb.shape[:2]
    rows = range(max(1, h // 2 - 40), min(h, h // 2 + 41), 8)
    blobs_by_row = []
    face_left_votes: dict[str, int] = {}
    for y in rows:
        line = rgb[y].astype(np.float32)
        d2 = ((line[:, None, :] - table[None, :, :]) ** 2).sum(axis=2)
        best = d2.argmin(axis=1)
        dist = np.sqrt(d2[np.arange(w), best])
        labels = np.where(dist <= tolerance, best, -1)
        runs = []
        x = 0
        while x < w:
            j = x + 1
            while j < w and labels[j] == labels[x]:
                j += 1
            if labels[x] >= 0:
                runs.append((names[labels[x]], x, j))
            x = j
        blobs = []
        for name, start, end in runs:
            if name not in ("target_left", "target_right"):
                continue
            if blobs and start - blobs[-1][2] <= 2 and name != blobs[-1][3]:
                blobs[-1] = (blobs[-1][0], blobs[-1][1], end, name)
            else:
                blobs.append((name, start, end, name))  # (left color, start, end, last color)
        blobs_by_row.append(blobs)
        # Ignore seam-width runs (a 1-px mark-colored sliver sits at the face's left edge in the
        # real captures); the face halves are ~70 px wide at D = 48.
        face = [(name, start) for name, start, end in runs if name in ("yellow", "mark") and end - start >= 4]
        if face:
            leftmost = min(face, key=lambda f: f[1])[0]
            face_left_votes[leftmost] = face_left_votes.get(leftmost, 0) + 1
    if not blobs_by_row:
        return 0, "", "", ""
    # Majority sprite count, then the median blob centers / left colors for rows with that count.
    counts = [len(b) for b in blobs_by_row]
    count = max(set(counts), key=counts.count)
    chosen = [b for b in blobs_by_row if len(b) == count]
    centers = []
    lefts = []
    for i in range(count):
        centers.append(float(np.median([(b[i][1] + b[i][2]) / 2 for b in chosen])))
        colors_i = [b[i][0] for b in chosen]
        lefts.append(max(set(colors_i), key=colors_i.count))
    face_left = max(face_left_votes, key=face_left_votes.get) if face_left_votes else ""
    return count, " ".join(f"{c:.1f}" for c in centers), "/".join(lefts), face_left


# --------------------------------------------------------------------------- per variant

def measure_variant(variant_dir: Path, image_override: Path | None = None) -> dict:
    geo = json.loads((variant_dir / "geometry.json").read_text(encoding="utf-8"))
    row = {c: None for c in COLUMNS}
    row["variant"] = geo.get("variant")
    row["player_tilt"] = geo.get("player_tilt")
    shoot = geo.get("shoot")
    if not shoot:
        row["status"] = "not-a-shoot-variant"
        return row
    row["shoot_x"] = shoot.get("shoot_x")
    row["shoot_y"] = shoot.get("shoot_y")
    work = variant_dir / "work"
    flags = []

    state_path = find_file(work, geo.get("state_file", "OVPRB0.TXT"))
    state = parse_values(read_latin1(state_path)) if state_path else {}
    for key in ("SHX", "SHY", "SHRNG", "SHFAC", "TCK", "ANG", "PZ"):
        row[f"state_{key}"] = state.get(key)

    sho_path = find_file(work, shoot.get("after_shot_file", "OVSHO0.TXT"))
    hit_path = find_file(work, shoot.get("hit_file", "OVHIT0.TXT"))
    hout = parse_values(read_latin1(sho_path)) if sho_path else {}
    hit_text = read_latin1(hit_path) if hit_path else ""
    hin = parse_values(hit_text)
    row["pre_marker"] = ("PRE" in read_latin1(sho_path)) if sho_path else None
    letters = LETTER_RE.findall(hit_text)
    row["hit_letters"] = "".join(letters)
    for key in "DXYRT":
        row[f"hin_{key}"] = hin.get(f"HIN_{key}")
        row[f"hout_{key}"] = hout.get(f"HOUT_{key}")

    if sho_path is None and hit_path is None:
        row["status"] = "missing"
    elif hit_path is None:
        row["status"] = "no-hit-marker"
    elif len(letters) != 1:
        row["status"] = "ok"
        flags.append(f"letters={len(letters)}")
    else:
        row["status"] = "ok"

    pred = shoot.get("prediction", {})
    alt = pred.get("alternatives", {})
    row["pred_target"] = TARGET_LETTERS.get(pred.get("target"), pred.get("target"))
    row["pred_texel_x"] = texel(pred, "x")
    row["pred_texel_y"] = texel(pred, "y")
    row["pred_dist_forward"] = pred.get("hit_dist_forward")
    row["pred_dist_ray"] = pred.get("hit_dist_ray")

    letter = letters[0] if len(letters) == 1 else None
    if letter is not None:
        inverted = TARGET_LETTERS.get((alt.get("shoot_x_sign_inverted") or {}).get("target"))
        row["target_match"] = "shared" if letter == row["pred_target"] else ("x-sign-inverted" if letter == inverted else "neither")

    # Prefer the values read after the instruction (documented as valid there, p.90); fall back to the hook's.
    obs_x = hout.get("HOUT_X", hin.get("HIN_X"))
    obs_y = hout.get("HOUT_Y", hin.get("HIN_Y"))
    obs_d = hout.get("HOUT_D", hin.get("HIN_D"))
    obs_r = hout.get("HOUT_R", hin.get("HIN_R"))
    if hout and hin:
        for key in "DXYR":
            a, b = hout.get(f"HOUT_{key}"), hin.get(f"HIN_{key}")
            if a is not None and b is not None and abs(a - b) > 0.0005:
                flags.append(f"hin/hout {key} differ")

    lateral_candidates = {
        "shared": texel(pred, "x"),
        "linear": texel(alt.get("linear_angle"), "x"),
        "mirrored": texel(alt.get("texture_mirrored"), "x"),
    }
    # A dead-center hit puts the mirrored and unmirrored texels within a pixel of each other;
    # only report "mirrored" when the two readings are actually separable.
    if (lateral_candidates["shared"] is not None and lateral_candidates["mirrored"] is not None
            and abs(lateral_candidates["shared"] - lateral_candidates["mirrored"]) < 4):
        del lateral_candidates["mirrored"]
        flags.append("mirror-unresolvable-here")
    row["lateral_model"], row["lateral_delta_px"] = nearest(obs_x, lateral_candidates)
    row["vertical_model"], row["vertical_delta_px"] = nearest(obs_y, {
        "shared": texel(pred, "y"),
        "y-sign-inverted": texel(alt.get("shoot_y_sign_inverted"), "y"),
        "no-shear": texel(alt.get("no_shear"), "y"),
    })
    row["dist_model"], row["dist_delta"] = nearest(obs_d, {
        "forward": pred.get("hit_dist_forward"),
        "ray": pred.get("hit_dist_ray"),
    })
    if obs_d is not None and state.get("SHRNG") and state.get("SHFAC") is not None:
        expected = state["SHFAC"] * (1 - obs_d / state["SHRNG"])
        row["result_expected"] = expected
        row["result_delta"] = None if obs_r is None else obs_r - expected

    image_path = image_override or find_file(work, geo.get("screenshot", "OVSHT0.PCX"))
    if image_path is not None and image_path.exists():
        row["image"] = image_path.name
        try:
            rgb = read_pcx(image_path.read_bytes())
            count, columns, lefts, face_left = sprite_orientation(rgb, geo.get("colors", {}))
            row["sprite_count"] = count
            row["sprite_columns"] = columns
            row["sprite_left_halves"] = lefts
            row["face_left_color"] = face_left
        except (ValueError, OSError) as exc:
            flags.append(f"image:{exc}")
    else:
        flags.append("no-screenshot")

    row["flags"] = ";".join(flags)
    return row


# --------------------------------------------------------------------------- selftest

def selftest() -> int:
    """Synthesizes a variant under the shared model and one under the linear model and checks the verdicts."""
    geometry = {
        "variant": "selftest", "player_tilt": 0, "state_file": "OVPRB0.TXT", "screenshot": "OVSHT0.PCX",
        "colors": {"target_left": [40, 220, 220], "target_right": [240, 130, 40], "yellow": [240, 220, 40], "mark": [170, 190, 255]},
        "shoot": {
            "shoot_x": 0.55, "shoot_y": 0, "hit_file": "OVHIT0.TXT", "after_shot_file": "OVSHO0.TXT",
            "prediction": {
                "target": "tgt_right", "texel": {"x": 51.5, "y": 128}, "hit_dist_forward": 44, "hit_dist_ray": 45.94,
                "alternatives": {
                    "linear_angle": {"target": "tgt_right", "texel": {"x": 38.6, "y": 128}},
                    "shoot_x_sign_inverted": {"target": "tgt_left", "texel": {"x": 12.5, "y": 128}},
                    "shoot_y_sign_inverted": {"z": 8, "texel": {"x": 51.5, "y": 128}},
                    "no_shear": {"z": 8, "texel": {"x": 51.5, "y": 128}},
                    "texture_mirrored": {"texel": {"x": 12.5, "y": 128}},
                },
            },
        },
    }
    failures = 0
    cases = [
        ("shared", "R", 51, 128, 45.94, "shared", "ray", "shared"),
        ("linear", "R", 39, 128, 44.0, "linear", "forward", "shared"),
        ("inverted", "L", 12, 128, 44.0, "mirrored", "forward", "x-sign-inverted"),
    ]
    with tempfile.TemporaryDirectory() as tmp:
        for name, letter, hx, hy, dist, want_lateral, want_dist, want_target in cases:
            vdir = Path(tmp) / name
            (vdir / "work").mkdir(parents=True)
            (vdir / "geometry.json").write_text(json.dumps(geometry), encoding="utf-8")
            (vdir / "work" / "OVPRB0.TXT").write_text("SHX=0.550\nSHY=0\nSHRNG=500.000\nSHFAC=1.000\nTCK=16.000\n", encoding="latin-1")
            (vdir / "work" / "OVSHO0.TXT").write_text(
                f"PRE\nHOUT_D={dist:.3f}\nHOUT_X={hx}\nHOUT_Y={hy}\nHOUT_R={1 - dist / 500:.3f}\nHOUT_T=16.000\n", encoding="latin-1")
            (vdir / "work" / "OVHIT0.TXT").write_text(
                f"{letter}\nHIN_D={dist:.3f}\nHIN_X={hx}\nHIN_Y={hy}\nHIN_R={1 - dist / 500:.3f}\nHIN_T=16.000\n", encoding="latin-1")
            row = measure_variant(vdir)
            got = (row["status"], row["hit_letters"], row["lateral_model"], row["dist_model"], row["target_match"], row["vertical_model"])
            want = ("ok", letter, want_lateral, want_dist, want_target, "shared")
            ok = got == want and row["pre_marker"] is True and abs(row["result_delta"]) < 0.002 and "no-screenshot" in row["flags"]
            print(f"{'ok ' if ok else 'FAIL'} {name}: {got}")
            failures += 0 if ok else 1
        # Orientation reader: a synthetic frame with three cyan|orange sprites and a yellow|mark face.
        frame = np.zeros((480, 640, 3), dtype=np.uint8)
        frame[:, :] = (120, 120, 120)
        for cx in (160, 320, 480):
            frame[:, cx - 26:cx] = (40, 220, 220)
            frame[:, cx:cx + 26] = (240, 130, 40)
        frame[:, 214:294] = (240, 220, 40)   # face left half, left of the center sprite
        frame[:, 346:426] = (170, 190, 255)  # face right half, right of it
        count, columns, lefts, face_left = sprite_orientation(frame, geometry["colors"])
        ok = count == 3 and lefts == "target_left/target_left/target_left" and face_left == "yellow"
        print(f"{'ok ' if ok else 'FAIL'} orientation: count={count} columns={columns} lefts={lefts} face_left={face_left}")
        failures += 0 if ok else 1
    print("selftest", "passed" if failures == 0 else f"FAILED ({failures})")
    return 0 if failures == 0 else 1


def main(argv=None) -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("variants", nargs="*", help="variant directories (each holding geometry.json and work/)")
    ap.add_argument("--image", help="read this screenshot instead of the variant's (single variant)")
    ap.add_argument("--selftest", action="store_true", help="synthesize outputs and verify the verdicts")
    ap.add_argument("--no-header", action="store_true")
    args = ap.parse_args(argv)
    if args.selftest:
        return selftest()
    if not args.variants:
        ap.error("give at least one variant directory or --selftest")
    writer = csv.writer(sys.stdout, lineterminator="\n")
    if not args.no_header:
        writer.writerow(COLUMNS)
    for variant in args.variants:
        vdir = Path(variant)
        if not (vdir / "geometry.json").exists():
            continue
        row = measure_variant(vdir, Path(args.image) if args.image else None)
        writer.writerow([fmt(row[c]) for c in COLUMNS])
    return 0


if __name__ == "__main__":
    sys.exit(main())
