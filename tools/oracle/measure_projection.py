#!/usr/bin/env python3
# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 The OpenVirtue Authors
"""Measure the OV-20261009 projection probe screenshots.

Reads each variant directory written by ``ovtool oracle prepare projection-fov |
pixel-aspect | tilt-model`` (its ``geometry.json`` and ``work/<screenshot>0.PCX``) and
prints one CSV row per image with raw pixel measurements:

* horizontal span of the far wall's stripe edges and the yellow target face,
  with the focal length implied by the known geometry (f = S * D / W);
* vertical span of the target face (floor and ceiling junction rows) and h/w;
* the eye-line / horizon row, from the vanishing point of the side walls'
  floor and ceiling junction lines;
* whether the far wall's vertical stripe edges are parallel (fitted angle per edge).

Every row carries a ``status``; anything other than ``ok`` means some estimator was
withheld (clipped face, unreadable image, no far wall...) and the ``flags`` column says
which. Everything is deterministic. ``--selftest`` renders the same rooms with a pinhole
model (shear and pitch variants, plus seam/blob perturbations) and checks the estimators
recover the model's constants or flag the row. Requires numpy; Pillow is only used for
non-PCX images (e.g. a DOSBox-X capture-key PNG).
"""

from __future__ import annotations

import argparse
import json
import math
import re
import struct
import sys
from dataclasses import dataclass, field
from pathlib import Path

import numpy as np

COLOR_ORDER = ["red", "green", "yellow", "blue", "magenta", "floor", "ceiling", "black"]
STRIPE = {"red", "green", "yellow"}
CSV_COLUMNS = [
    "variant", "video", "player_arc", "player_tilt", "aspect", "status", "flags", "width", "height",
    "unclassified_frac", "far_row", "edges_n", "edges_mismatch", "f_h_fit", "f_h_fit_signed", "f_h_fit_rms", "cx_fit",
    "f_h_spacing", "target_w", "target_h", "target_h_spread", "h_over_w", "f_h_target", "f_v_target",
    "far_floor_row", "far_top_row", "far_mid_row", "horizon_row", "horizon_left", "horizon_right",
    "horizon_cross", "edge_angle_left_deg", "edge_angle_right_deg", "edge_angle_diff_deg",
    "edge_angle_max_abs_deg", "edge_rows_min", "state_PLAYER_Z", "state_PLAYER_SIZE", "state_PLAYER_ARC",
    "state_PLAYER_TILT", "state_ASPECT", "state_SCREEN_WIDTH", "state_SCREEN_HGT", "state_FLOOR_HGT",
    "state_CEIL_HGT", "image",
]


@dataclass
class Geometry:
    variant: str
    video: str
    width: int
    height: int
    player_arc: float
    player_tilt: float
    aspect: float
    screenshot: str
    state_file: str
    half_width: int
    depth: int
    back_depth: int
    floor: float
    ceiling: float
    stripe_width: int
    target_width: int
    far_wall_top: float
    alcove_depth: int
    eye_height: float
    segments: list[tuple[int, int, str]]
    colors: dict[str, tuple[int, int, int]]
    state_labels: dict[str, str] = field(default_factory=dict)

    @staticmethod
    def load(path: Path) -> "Geometry":
        g = json.loads(path.read_text(encoding="utf-8-sig"))
        room = g["room"]
        return Geometry(
            variant=g["variant"], video=g["video"], width=g["screen"]["width"], height=g["screen"]["height"],
            player_arc=float(g["player_arc"]), player_tilt=float(g["player_tilt"]), aspect=float(g["aspect"]),
            screenshot=g["screenshot"], state_file=g["state_file"],
            half_width=int(room["half_width"]), depth=int(room["depth"]), back_depth=int(room["back_depth"]),
            floor=float(room["floor"]), ceiling=float(room["ceiling"]), stripe_width=int(room["stripe_width"]),
            target_width=int(room["target_width"]), far_wall_top=float(room.get("far_wall_top", room["ceiling"])),
            alcove_depth=int(room.get("alcove_depth", 0)),
            eye_height=float(room.get("eye_height", room.get("default_eye_height", 3))),
            segments=[(int(s["y0"]), int(s["y1"]), s["color"]) for s in g["far_wall_segments"]],
            colors={k: tuple(int(c) for c in v) for k, v in g["colors"].items()},
            state_labels={str(k): str(v) for k, v in g.get("state_labels", {}).items()},
        )


@dataclass
class Result:
    values: dict = field(default_factory=dict)
    flags: list = field(default_factory=list)

    def set(self, **kwargs):
        self.values.update(kwargs)

    def flag(self, name: str):
        if name not in self.flags:
            self.flags.append(name)

    def finish(self):
        self.values["flags"] = "+".join(self.flags)
        if self.flags and self.values.get("status", "ok") == "ok":
            self.values["status"] = "flagged"

    def row(self) -> str:
        out = []
        for c in CSV_COLUMNS:
            v = self.values.get(c, "")
            if isinstance(v, float):
                out.append("" if math.isnan(v) else f"{v:.3f}")
            else:
                out.append(str(v))
        return ",".join(out)


# --------------------------------------------------------------------------- image loading

def read_pcx(data: bytes) -> np.ndarray:
    """Minimal 8-bit single-plane ZSoft PCX decoder (RLE + 0x0C/768-byte palette trailer).

    Independent of Pillow's version-byte check; 6-bit DAC palettes (all entries <= 63) are
    scaled by 4. Returns an HxWx3 uint8 RGB array.
    """
    if len(data) < 128 or data[0] != 0x0A:
        raise ValueError("not a PCX file")
    encoding, bpp = data[2], data[3]
    xmin, ymin, xmax, ymax = struct.unpack_from("<HHHH", data, 4)
    planes = data[65]
    bytes_per_line = struct.unpack_from("<H", data, 66)[0]
    if bpp != 8 or planes != 1:
        raise ValueError(f"unsupported PCX layout (bpp={bpp}, planes={planes})")
    width, height = xmax - xmin + 1, ymax - ymin + 1
    has_trailer = len(data) >= 128 + 769 and data[-769] == 0x0C
    raster_end = len(data) - 769 if has_trailer else len(data)
    out = np.zeros(height * bytes_per_line, dtype=np.uint8)
    i, o = 128, 0
    n = len(out)
    if encoding == 1:
        while o < n and i < raster_end:
            b = data[i]
            i += 1
            if (b & 0xC0) == 0xC0:
                run = b & 0x3F
                if i >= raster_end:
                    break
                v = data[i]
                i += 1
                run = min(run, n - o)
                out[o:o + run] = v
                o += run
            else:
                out[o] = b
                o += 1
    else:
        take = min(n, raster_end - 128)
        out[:take] = np.frombuffer(data, dtype=np.uint8, count=take, offset=128)
        o = take
    if o < n:
        raise ValueError(f"truncated PCX raster ({o}/{n} bytes)")
    if not has_trailer:
        raise ValueError("PCX has no 256-color palette trailer")
    palette = np.frombuffer(data, dtype=np.uint8, count=768, offset=len(data) - 768).reshape(256, 3).astype(np.int32)
    if palette.max() <= 63:
        palette = palette * 4
    indices = out.reshape(height, bytes_per_line)[:, :width]
    return palette[indices].astype(np.uint8)


def load_image(path: Path):
    """Returns (rgb array, note) or raises ValueError/OSError."""
    data = path.read_bytes()
    if data[:1] == b"\x0a":
        return read_pcx(data), "pcx"
    try:
        from PIL import Image
    except ImportError as exc:  # pragma: no cover
        raise ValueError("Pillow is required for non-PCX images") from exc
    with Image.open(path) as img:
        return np.asarray(img.convert("RGB")), (img.format or "image").lower()


# --------------------------------------------------------------------------- classification

def classify(rgb: np.ndarray, colors: dict[str, tuple[int, int, int]], tolerance: float = 48.0):
    """Nearest-palette-color label per pixel; -1 when farther than ``tolerance`` from every known color."""
    names = [n for n in COLOR_ORDER if n in colors]
    table = np.array([colors[n] for n in names], dtype=np.float32)
    flat = rgb.reshape(-1, 3).astype(np.float32)
    d2 = ((flat[:, None, :] - table[None, :, :]) ** 2).sum(axis=2)
    best = d2.argmin(axis=1)
    dist = np.sqrt(d2[np.arange(len(flat)), best])
    labels = np.where(dist <= tolerance, best, -1).reshape(rgb.shape[:2])
    return labels, names


def runs(line: np.ndarray):
    """Run-length encode a 1-D label line into (label, start, end_exclusive)."""
    out = []
    n = len(line)
    i = 0
    while i < n:
        j = i + 1
        while j < n and line[j] == line[i]:
            j += 1
        out.append((int(line[i]), i, j))
        i = j
    return out


def fit_line(xs, ys):
    """Least squares y = a + b x; returns (a, b, rms)."""
    xs = np.asarray(xs, dtype=np.float64)
    ys = np.asarray(ys, dtype=np.float64)
    if len(xs) < 2:
        return math.nan, math.nan, math.nan
    A = np.vstack([np.ones_like(xs), xs]).T
    coef, *_ = np.linalg.lstsq(A, ys, rcond=None)
    resid = ys - A @ coef
    return float(coef[0]), float(coef[1]), float(math.sqrt(float((resid ** 2).mean())))


def label_spans(line_runs, label, gap_px=2):
    """Spans [start, end) of ``label`` along a run list, merging spans separated by <= gap_px foreign pixels (seams)."""
    spans = [(s, e) for lab, s, e in line_runs if lab == label]
    merged = []
    for s_, e_ in spans:
        if merged and s_ - merged[-1][1] <= gap_px:
            merged[-1] = (merged[-1][0], e_)
        else:
            merged.append((s_, e_))
    return merged


def stripe_edges(line_runs, stripe_ids, names, bridge_px=2):
    """Boundaries between adjacent stripe-colored runs along one row: [(x_edge, left_color, right_color)].

    A foreign run of at most ``bridge_px`` pixels between two different stripe colors (a renderer
    seam) does not delete the boundary; the edge is placed at its midpoint.
    """
    edges = []
    srs = [(lab, s, e) for lab, s, e in line_runs if lab in stripe_ids]
    for (la, sa, ea), (lb, sb, eb) in zip(srs, srs[1:]):
        if la == lb:
            continue
        if ea == sb:
            edges.append((float(sb), names[la], names[lb]))
        elif sb - ea <= bridge_px:
            edges.append(((ea + sb) / 2.0, names[la], names[lb]))
    return edges


# --------------------------------------------------------------------------- measurement

def measure(labels: np.ndarray, names: list[str], geo: Geometry, result: Result):
    H, W = labels.shape
    idx = {n: i for i, n in enumerate(names)}
    stripe_ids = {idx[n] for n in STRIPE if n in idx}
    blue = idx.get("blue", -2)
    floor_id = idx.get("floor", -2)
    ceil_id = idx.get("ceiling", -2)
    yellow = idx.get("yellow", -2)

    is_stripe = np.isin(labels, list(stripe_ids))
    per_row = is_stripe.sum(axis=1)
    edges = []
    far_left = far_right = math.nan
    if per_row.max() == 0:
        result.set(status="no-far-wall")
    else:
        good_rows = np.where(per_row >= 0.9 * per_row.max())[0]
        far_row = int(good_rows[len(good_rows) // 2])  # an actual member, not np.median of an even set
        result.set(far_row=far_row)

        # --- horizontal stripe edges along the far-wall row
        line_runs = runs(labels[far_row])
        edges = stripe_edges(line_runs, stripe_ids, names)
        for (lab, s, e) in line_runs:  # outer far-wall edges against the blue side walls
            if lab in stripe_ids:
                if s > 0 and labels[far_row, s - 1] == blue:
                    edges.append((float(s), "blue", names[lab]))
                if e < W and labels[far_row, e] == blue:
                    edges.append((float(e), names[lab], "blue"))
        edges.sort()
        result.set(edges_n=len(edges))

        spacings = [xb - xa for (xa, _, ca), (xb, cb, _) in zip(edges, edges[1:])
                    if ca == cb and ca in ("red", "green")]
        if spacings:
            result.set(f_h_spacing=float(np.median(spacings)) * geo.depth / geo.stripe_width)

        world_edges, mismatches = assign_world_y(edges, geo)
        result.set(edges_mismatch=mismatches)
        if mismatches:
            result.flag("edge-mismatch")
        if len(world_edges) >= 2:
            a, b, rms = fit_line([y for y, _ in world_edges], [x for _, x in world_edges])
            # f_h_fit_signed keeps the slope's sign: negative means map +y is on the screen LEFT
            # (x = cx + b*y with b < 0), the handedness ADR-0009 rests on. Meaningful only for a
            # far wall whose stripe pattern is asymmetric (the tilt room); the face room is
            # mirror-symmetric, so assign_world_y picks a direction arbitrarily there.
            result.set(f_h_fit=abs(b) * geo.depth, f_h_fit_signed=b * geo.depth, f_h_fit_rms=rms, cx_fit=a)
        elif edges:
            result.flag("no-world-anchor")

        sample_rows = [int(good_rows[int(frac * (len(good_rows) - 1))]) for frac in (0.2, 0.35, 0.5, 0.65, 0.8)]
        measure_target(labels, sample_rows, far_row, yellow, geo, result, H, W)

        xc = W // 2
        cruns = [(cs, ce) for lab, cs, ce in runs(labels[:, xc]) if lab in stripe_ids]
        if cruns:
            cs = min(r[0] for r in cruns)
            ce = max(r[1] for r in cruns)
            if "far_top_row" not in result.values and cs > 0:
                result.set(far_top_row=float(cs))
            if "far_floor_row" not in result.values and ce < H:
                result.set(far_floor_row=float(ce))
            if cs > 0 and ce < H:
                result.set(far_mid_row=(cs + ce) / 2.0)

        far_left = min((x for x, _, _ in edges), default=math.nan)
        far_right = max((x for x, _, _ in edges), default=math.nan)

        angles = trace_vertical_edges(labels, stripe_ids, names, far_row, H)
        if angles:
            angles.sort(key=lambda t: t[0])
            result.set(edge_angle_left_deg=angles[0][1], edge_angle_right_deg=angles[-1][1],
                       edge_angle_diff_deg=angles[-1][1] - angles[0][1],
                       edge_angle_max_abs_deg=max(abs(a) for _, a, _ in angles),
                       edge_rows_min=min(n for _, _, n in angles))

    # --- side-wall junction lines -> horizon row (also when the far wall is out of frame)
    result.set(**side_wall_horizon(labels, blue, floor_id, ceil_id, far_left, far_right, W, H))


def measure_target(labels, sample_rows, far_row, yellow, geo, result, H, W):
    """Yellow face: width from the longest yellow span on five rows across the far wall (median,
    spread-flagged), height from the span containing far_row on five columns across the face."""
    if yellow < 0:
        return
    spans = []
    for r in sample_rows:
        yruns = label_spans(runs(labels[r]), yellow)
        if yruns:
            spans.append(max(yruns, key=lambda q: q[1] - q[0]))
    if not spans:
        return
    if any(s_ <= 0 or e_ >= W for s_, e_ in spans):
        result.flag("target-clipped-x")
        return
    widths = sorted(e_ - s_ for s_, e_ in spans)
    tw = float(widths[len(widths) // 2])
    s, e = next((s_, e_) for s_, e_ in spans if e_ - s_ == tw)
    if widths[-1] - widths[0] > 2:
        result.flag("target-w-spread")
    result.set(target_w=tw, f_h_target=tw * geo.depth / geo.target_width)
    # vertical extent: the yellow run containing far_row, sampled at five columns across the face
    tops, bottoms = [], []
    clipped = False
    for frac in (0.2, 0.35, 0.5, 0.65, 0.8):
        x = int(s + frac * (e - s))
        for cs, ce in label_spans(runs(labels[:, x]), yellow):
            if cs <= far_row < ce:
                if cs == 0 or ce == H:
                    clipped = True
                else:
                    tops.append(cs)
                    bottoms.append(ce)
                break
    if clipped or not tops:
        result.flag("target-clipped-y")
        return
    top, bottom = float(np.median(tops)), float(np.median(bottoms))
    spread = float(max(bottoms) - min(bottoms) + max(tops) - min(tops))
    th = bottom - top
    result.set(target_h=th, target_h_spread=spread, h_over_w=th / tw,
               f_v_target=th * geo.depth / (geo.far_wall_top - geo.floor),
               far_top_row=top, far_floor_row=bottom)
    if spread > 2:
        result.flag("target-h-spread")


def assign_world_y(edges, geo: Geometry):
    """Map screen edges to world y using the yellow target (or a blue wall end) as anchor.

    Returns ([(world_y, x_edge)], mismatches). Edges whose color pair does not match the
    expected boundary are dropped and counted. The screen direction is left free: the fit's
    slope sign reports it, its magnitude is f/D.
    """
    seg = geo.segments
    boundaries = [seg[0][0]] + [s[1] for s in seg]  # boundary k lies between seg[k-1] and seg[k]

    def boundary_colors(k):
        lo = seg[k - 1][2] if k - 1 >= 0 else "blue"
        hi = seg[k][2] if k < len(seg) else "blue"
        return lo, hi

    def mapped_for(anchor_index, anchor_k, direction):
        """Edge at list index anchor_index is boundary anchor_k; walk outward in both directions."""
        mapped, mismatches = [], 0
        for i, (x, l, r) in enumerate(edges):
            k = anchor_k + (i - anchor_index) * direction
            if k < 0 or k > len(seg):
                mismatches += 1
                continue
            lo, hi = boundary_colors(k)
            expect = (lo, hi) if direction > 0 else (hi, lo)
            if (l, r) != expect:
                mismatches += 1
                continue
            mapped.append((float(boundaries[k]), x))
        return mapped, mismatches

    candidates = []
    target_k = next((k for k, (_, _, c) in enumerate(seg) if c == "yellow"), None)
    if target_k is not None:
        for i, (x, l, r) in enumerate(edges):
            if r == "yellow":   # this edge is the target's low-y side (+dir) or high-y side (-dir)
                candidates.append((i, target_k, +1))
                candidates.append((i, target_k + 1, -1))
    for i, (x, l, r) in enumerate(edges):
        if l == "blue":          # far wall starts here: boundary 0 (+dir) or boundary N (-dir)
            candidates.append((i, 0, +1))
            candidates.append((i, len(seg), -1))
        if r == "blue":          # far wall ends here
            candidates.append((i, len(seg), +1))
            candidates.append((i, 0, -1))
    best = None
    for anchor_index, anchor_k, direction in candidates:
        mapped, mismatches = mapped_for(anchor_index, anchor_k, direction)
        key = (mismatches, -len(mapped))
        if best is None or key < best[0]:
            best = (key, mapped, mismatches)
    if best is None:
        return [], 0
    return best[1], best[2]


def side_wall_horizon(labels, blue, floor_id, ceil_id, far_left, far_right, W, H):
    out = {}

    def junctions(xrange):
        floor_pts, ceil_pts = [], []
        for x in xrange:
            bruns = [(s, e) for lab, s, e in runs(labels[:, x]) if lab == blue]
            if not bruns:
                continue
            s, e = max(bruns, key=lambda r: r[1] - r[0])
            if s > 0 and labels[s - 1, x] == ceil_id:
                ceil_pts.append((x + 0.5, float(s)))
            if e < H and labels[e, x] == floor_id:
                floor_pts.append((x + 0.5, float(e)))
        return floor_pts, ceil_pts

    left_end = int(far_left) if not math.isnan(far_left) else W // 2
    right_start = int(math.ceil(far_right)) if not math.isnan(far_right) else W // 2
    sides = {"left": junctions(range(0, left_end)), "right": junctions(range(right_start, W))}
    lines = {}
    for side, (fpts, cpts) in sides.items():
        for kind, pts in (("floor", fpts), ("ceil", cpts)):
            if len(pts) >= 8:
                a, b, _ = fit_line([p[0] for p in pts], [p[1] for p in pts])
                lines[(side, kind)] = (a, b)

    def intersect(l1, l2):
        (a1, b1), (a2, b2) = l1, l2
        if abs(b1 - b2) < 1e-9:
            return None
        x = (a2 - a1) / (b1 - b2)
        return x, a1 + b1 * x

    rows = []
    for side in ("left", "right"):
        if (side, "floor") in lines and (side, "ceil") in lines:
            p = intersect(lines[(side, "floor")], lines[(side, "ceil")])
            if p:
                out[f"horizon_{side}"] = p[1]
                rows.append(p[1])
    for kind in ("floor", "ceil"):
        if ("left", kind) in lines and ("right", kind) in lines:
            p = intersect(lines[("left", kind)], lines[("right", kind)])
            if p:
                if kind == "floor":
                    out["horizon_cross"] = p[1]
                rows.append(p[1])
    if rows:
        out["horizon_row"] = float(np.mean(rows))
    return out


def trace_vertical_edges(labels, stripe_ids, names, far_row, H, min_rows=20, max_gap=2):
    """Follow each stripe boundary up and down from far_row; return [(x_at_far_row, angle_deg, n_rows)]."""
    def row_edges(r):
        return [x for x, _, _ in stripe_edges(runs(labels[r]), stripe_ids, names)]

    start = row_edges(far_row)
    chains = {x: [(far_row + 0.5, x)] for x in start}
    for direction in (-1, +1):
        current = {x: (x, 0) for x in start}   # origin -> (last x, consecutive misses)
        r = far_row + direction
        while 0 <= r < H and current:
            es = row_edges(r)
            nxt = {}
            for origin, (last, misses) in current.items():
                cand = [e for e in es if abs(e - last) <= 3.0]
                if cand:
                    best = min(cand, key=lambda e: abs(e - last))
                    chains[origin].append((r + 0.5, best))
                    nxt[origin] = (best, 0)
                elif misses < max_gap:
                    nxt[origin] = (last, misses + 1)
            current = nxt
            r += direction
    out = []
    for origin, pts in chains.items():
        if len(pts) >= min_rows:
            a, b, _ = fit_line([p[0] for p in pts], [p[1] for p in pts])
            out.append((origin, math.degrees(math.atan(b)), len(pts)))
    return out


STATE_RE = re.compile(r"([A-Z_][A-Z0-9_]*)=\s*([-+]?(?:[0-9]+\.?[0-9]*|\.[0-9]+))")


def read_state(work: Path, name: str) -> dict[str, float]:
    path = work / name
    if not path.exists():
        candidates = sorted(work.glob(name[:5] + "*.TXT")) if work.exists() else []
        if not candidates:
            return {}
        path = candidates[0]
    text = path.read_text(encoding="latin-1", errors="replace")
    return {m.group(1): float(m.group(2)) for m in STATE_RE.finditer(text)}


def measure_variant(variant_dir: Path, image_override: Path | None = None) -> Result:
    geo = Geometry.load(variant_dir / "geometry.json")
    result = Result()
    result.set(variant=geo.variant, video=geo.video, player_arc=geo.player_arc,
               player_tilt=geo.player_tilt, aspect=geo.aspect, status="ok")
    work = variant_dir / "work"
    for k, v in read_state(work, geo.state_file).items():
        result.set(**{f"state_{geo.state_labels.get(k, k)}": v})
    image_path = image_override or (work / geo.screenshot)
    if not image_path.exists():
        candidates = sorted(work.glob(geo.screenshot[:5] + "*.PCX")) if work.exists() else []
        if not candidates:
            result.set(status="missing")
            result.finish()
            return result
        image_path = candidates[0]
    result.set(image=image_path.name)
    try:
        rgb, _ = load_image(image_path)
    except (ValueError, OSError) as exc:
        result.set(status=f"unreadable: {exc}".replace(",", ";"))
        result.finish()
        return result
    labels, names = classify(rgb, geo.colors)
    result.set(width=rgb.shape[1], height=rgb.shape[0], unclassified_frac=float((labels < 0).mean()))
    if (rgb.shape[1], rgb.shape[0]) != (geo.width, geo.height):
        result.flag("size-mismatch")
    if result.values["unclassified_frac"] > 0.02:
        result.flag("unclassified")
    measure(labels, names, geo, result)
    result.finish()
    return result


# --------------------------------------------------------------------------- self test

def render_pinhole(geo: Geometry, f_h: float, f_v: float, cx: float, cy: float, *, shear_px: float = 0.0,
                   pitch_rad: float = 0.0) -> np.ndarray:
    """Ray-cast the probe room with a pinhole camera (y-up world, +x forward, +world-y = screen-left).

    shear_px: vertical screen shift of the whole image (y-shear model); pitch_rad: true camera pitch.
    """
    W, H = geo.width, geo.height
    z_eye = geo.eye_height
    px = np.arange(W) + 0.5
    py = np.arange(H) + 0.5
    PX, PY = np.meshgrid(px, py)
    dx = np.ones_like(PX)
    dl = (cx - PX) / f_h
    du = (cy + shear_px - PY) / f_v
    if pitch_rad:
        c, s = math.cos(pitch_rad), math.sin(pitch_rad)
        dx, du = dx * c - du * s, dx * s + du * c
    colors = geo.colors
    out = np.zeros((H, W, 3), dtype=np.uint8)
    best_t = np.full((H, W), np.inf)

    def hit(mask, t, color):
        nonlocal best_t
        sel = mask & (t > 1e-6) & (t < best_t)
        best_t = np.where(sel, t, best_t)
        out[sel] = colors[color]

    t = geo.depth / dx
    y = t * dl
    z = z_eye + t * du
    inside = (np.abs(y) <= geo.half_width) & (z >= geo.floor) & (z <= geo.far_wall_top)
    for (y0, y1, c) in geo.segments:
        hit(inside & (y >= y0) & (y < y1), t, c)
    if geo.alcove_depth:
        xa = geo.depth + geo.alcove_depth
        t = xa / dx
        y = t * dl
        z = z_eye + t * du
        hit((np.abs(y) <= geo.half_width) & (z >= geo.far_wall_top) & (z <= geo.ceiling), t, "magenta")
        for sign in (+1, -1):
            with np.errstate(divide="ignore", invalid="ignore"):
                t = sign * geo.half_width / dl
            x = t * dx
            z = z_eye + t * du
            hit((x >= geo.depth) & (x <= xa) & (z >= geo.far_wall_top) & (z <= geo.ceiling), t, "blue")
        with np.errstate(divide="ignore", invalid="ignore"):
            t = (geo.ceiling - z_eye) / du
        x = t * dx
        y = t * dl
        hit((x >= geo.depth) & (x <= xa) & (np.abs(y) <= geo.half_width), t, "ceiling")
    t = -geo.back_depth / dx
    y = t * dl
    z = z_eye + t * du
    hit((np.abs(y) <= geo.half_width) & (z >= geo.floor) & (z <= geo.ceiling), t, "magenta")
    for sign in (+1, -1):
        with np.errstate(divide="ignore", invalid="ignore"):
            t = sign * geo.half_width / dl
        x = t * dx
        z = z_eye + t * du
        hit((x >= -geo.back_depth) & (x <= geo.depth) & (z >= geo.floor) & (z <= geo.ceiling), t, "blue")
    for zplane, c in ((geo.floor, "floor"), (geo.ceiling, "ceiling")):
        with np.errstate(divide="ignore", invalid="ignore"):
            t = (zplane - z_eye) / du
        x = t * dx
        y = t * dl
        hit((x >= -geo.back_depth) & (x <= geo.depth) & (np.abs(y) <= geo.half_width), t, c)
    return out


def selftest_geometry(video, w, h, room):
    half, depth, back, ceil, stripe, target, top, alcove, eye = room
    boundaries = []
    ht = target // 2
    for y in range(-half, half + 1, stripe):
        if target and -ht < y < ht:
            continue
        boundaries.append(y)
    seg = []
    for i in range(len(boundaries) - 1):
        y0, y1 = boundaries[i], boundaries[i + 1]
        is_t = target and y0 == -ht and y1 == ht
        seg.append((y0, y1, "yellow" if is_t else ("red" if i % 2 == 0 else "green")))
    return Geometry(
        variant=f"selftest-{video}", video=video, width=w, height=h, player_arc=1.0, player_tilt=0.0, aspect=1.0,
        screenshot="X.PCX", state_file="X.TXT", half_width=half, depth=depth, back_depth=back, floor=0.0,
        ceiling=float(ceil), stripe_width=stripe, target_width=target, far_wall_top=float(top), alcove_depth=alcove,
        eye_height=float(eye), segments=seg,
        colors={"red": (220, 32, 32), "green": (32, 200, 32), "blue": (40, 80, 230), "yellow": (240, 220, 40),
                "magenta": (220, 40, 220), "floor": (120, 120, 120), "ceiling": (60, 60, 60), "black": (0, 0, 0)})


def selftest() -> int:
    failures = 0

    def check(name, got, want, tol):
        nonlocal failures
        ok = got is not None and not (isinstance(got, float) and math.isnan(got)) and abs(got - want) <= tol
        print(f"  {'ok ' if ok else 'FAIL'} {name}: got {got} want {want} ±{tol}")
        if not ok:
            failures += 1

    def expect_present(v, keys):
        nonlocal failures
        for k in keys:
            if k not in v:
                print(f"  FAIL expected column {k} to be present")
                failures += 1

    def expect_absent(v, keys):
        nonlocal failures
        for k in keys:
            if k in v:
                print(f"  FAIL expected column {k} to be absent (got {v[k]})")
                failures += 1

    def expect_flag(res, flag):
        nonlocal failures
        if flag not in res.flags:
            print(f"  FAIL expected flag {flag} (flags={res.flags})")
            failures += 1

    # rooms must match ProjectionProbeGenerator.Room: half, depth, back, ceil, stripe, target, top, alcove, eye
    face = (24, 48, 16, 16, 4, 16, 16, 0, 8)
    tall = (12, 48, 16, 64, 4, 0, 40, 16, 8)

    def F():
        return selftest_geometry("S640x480", 640, 480, face)

    def T():
        return selftest_geometry("S640x480", 640, 480, tall)

    cases = [
        ("face f=554", F(), dict(f_h=554.0, f_v=554.0, cx=320.0, cy=240.0), None),
        ("face f=185 (arc 2, form A)", F(), dict(f_h=185.0, f_v=185.0, cx=320.0, cy=240.0), None),
        ("face f=1194 (arc 0.5, form A)", F(), dict(f_h=1194.0, f_v=1194.0, cx=320.0, cy=240.0), None),
        ("face ASPECT 2 (f_v=2 f_h)", F(), dict(f_h=554.0, f_v=1108.0, cx=320.0, cy=240.0), None),
        ("face ASPECT 0.5", F(), dict(f_h=554.0, f_v=277.0, cx=320.0, cy=240.0), None),
        ("face 320x200 pre-stretched", selftest_geometry("320x200", 320, 200, face), dict(f_h=277.0, f_v=277.0 / 1.2, cx=160.0, cy=100.0), None),
        ("face f=554 seam row", F(), dict(f_h=554.0, f_v=554.0, cx=320.0, cy=240.0), "seam-row"),
        ("face f=554 seam column", F(), dict(f_h=554.0, f_v=554.0, cx=320.0, cy=240.0), "seam-col"),
        ("face f=554 center blob", F(), dict(f_h=554.0, f_v=554.0, cx=320.0, cy=240.0), "blob"),
        ("face f=554 face clipped (cy=60)", F(), dict(f_h=554.0, f_v=554.0, cx=320.0, cy=60.0), None),
        ("tall shear 0", T(), dict(f_h=554.0, f_v=554.0, cx=320.0, cy=240.0, shear_px=0.0), None),
        ("tall shear +277", T(), dict(f_h=554.0, f_v=554.0, cx=320.0, cy=240.0, shear_px=277.0), None),
        ("tall shear +554", T(), dict(f_h=554.0, f_v=554.0, cx=320.0, cy=240.0, shear_px=554.0), None),
        ("tall shear -277", T(), dict(f_h=554.0, f_v=554.0, cx=320.0, cy=240.0, shear_px=-277.0), None),
        ("tall shear -554", T(), dict(f_h=554.0, f_v=554.0, cx=320.0, cy=240.0, shear_px=-554.0), None),
        ("tall pitch 26.6deg", T(), dict(f_h=554.0, f_v=554.0, cx=320.0, cy=240.0, pitch_rad=math.atan(0.5)), None),
        ("tall pitch 45deg", T(), dict(f_h=554.0, f_v=554.0, cx=320.0, cy=240.0, pitch_rad=math.atan(1.0)), None),
    ]
    for name, geo, params, perturb in cases:
        print(f"[selftest] {name}")
        rgb = render_pinhole(geo, **params)
        labels0, _ = classify(rgb, geo.colors)
        if perturb == "seam-row":
            per_row = np.isin(labels0, [0, 1, 2]).sum(axis=1)
            good = np.where(per_row >= 0.9 * per_row.max())[0]
            rgb[int(np.median(good))] = 0
        elif perturb == "seam-col":
            r0 = int(np.where(np.isin(labels0, [0, 1, 2]).sum(axis=1) > 0)[0].mean())
            xs = [x for x, _, _ in stripe_edges(runs(labels0[r0]), {0, 1, 2}, ["red", "green", "yellow"])]
            rgb[:, int(xs[1])] = 0
        elif perturb == "blob":
            rgb[234:246, 316:324] = 0
        labels, names = classify(rgb, geo.colors)
        res = Result()
        res.set(status="ok")
        measure(labels, names, geo, res)
        res.finish()
        v = res.values
        f_h, f_v = params["f_h"], params["f_v"]
        tol_f = 0.02 * f_h
        tol_spacing = max(tol_f, geo.depth / geo.stripe_width)
        pitched = "pitch_rad" in params
        z_eye = geo.eye_height
        cy = params["cy"]
        shear = params.get("shear_px", 0.0)
        face_top_row = cy + shear - f_v * (geo.far_wall_top - z_eye) / geo.depth
        face_floor_row = cy + shear + f_v * z_eye / geo.depth
        face_in_frame = 0 < face_top_row and face_floor_row < geo.height
        # side walls are in frame when the far corner is inside the width, and a junction line
        # is usable when its far end (at x = depth) is inside the height
        corner_dx = f_h * geo.half_width / geo.depth
        ceil_far_row = cy + shear - f_v * (geo.ceiling - z_eye) / geo.depth
        side_visible = corner_dx < params["cx"] - 8
        horizon_expected = (not pitched) and side_visible and (0 < face_floor_row < geo.height or 0 < ceil_far_row < geo.height)

        if not pitched:
            far_visible = face_floor_row > 0 and face_top_row < geo.height
            if far_visible:
                expect_present(v, ["f_h_fit", "f_h_spacing", "edges_n", "edge_angle_max_abs_deg"])
                check("f_h_fit", v.get("f_h_fit"), f_h, tol_f)
                check("f_h_spacing", v.get("f_h_spacing"), f_h, tol_spacing)
                check("edges_mismatch", v.get("edges_mismatch"), 0, 0)
                check("edge_angle_max_abs_deg (parallel)", v.get("edge_angle_max_abs_deg"), 0.0, 0.3)
            else:
                check("status no-far-wall", 1.0 if v.get("status") == "no-far-wall" else 0.0, 1.0, 0.0)
        if geo.target_width:
            if perturb != "blob":
                check("f_h_target", v.get("f_h_target"), f_h, tol_f)
            if face_in_frame:
                expect_present(v, ["target_h", "h_over_w", "f_v_target", "far_top_row", "far_floor_row"])
                check("f_v_target", v.get("f_v_target"), f_v, 0.02 * f_v)
                check("h_over_w", v.get("h_over_w"), f_v / f_h, 0.03)
                check("far_floor_row", v.get("far_floor_row"), face_floor_row, 1.5)
            else:
                expect_absent(v, ["target_h", "h_over_w", "f_v_target"])
                expect_flag(res, "target-clipped-y")
        if pitched:
            p = params["pitch_rad"]
            expect_present(v, ["edge_angle_diff_deg", "far_top_row"])
            check("edge_angle_diff_deg (converging)", abs(v.get("edge_angle_diff_deg", 0.0)), 8.5 if p < 0.6 else 16.0, 5.0)
            want = cy - f_v * math.tan(math.atan((geo.far_wall_top - z_eye) / geo.depth) - p)
            check("far_top_row (pitch)", v.get("far_top_row"), want, 2.0)
        elif geo.alcove_depth:
            want = cy + shear - f_v * (geo.far_wall_top - z_eye) / geo.depth
            if 0 < want < geo.height:
                expect_present(v, ["far_top_row"])
                check("far_top_row (shear)", v.get("far_top_row"), want, 2.0)
            else:
                expect_absent(v, ["far_top_row"])
            floor_want = cy + shear + f_v * z_eye / geo.depth
            if 0 < floor_want < geo.height and shear > -277:
                expect_present(v, ["far_floor_row"])
                check("far_floor_row (shear)", v.get("far_floor_row"), floor_want, 2.0)
        if horizon_expected:
            expect_present(v, ["horizon_row"])
            check("horizon_row", v.get("horizon_row"), cy + shear, 3.0)
        elif "horizon_row" in v and not pitched:
            check("horizon_row (unexpected but present)", v.get("horizon_row"), cy + shear, 3.0)
        if perturb == "blob":
            expect_flag(res, "target-w-spread")
        print("  " + res.row())
    print(f"[selftest] {'PASS' if failures == 0 else f'{failures} FAILURE(S)'}")
    return 1 if failures else 0


# --------------------------------------------------------------------------- main

def main(argv=None) -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("variants", nargs="*", help="variant directories (each holding geometry.json and work/)")
    ap.add_argument("--image", help="measure this image instead of the variant's screenshot (single variant)")
    ap.add_argument("--selftest", action="store_true", help="render synthetic rooms and verify the estimators")
    ap.add_argument("--no-header", action="store_true")
    args = ap.parse_args(argv)
    if args.selftest:
        return selftest()
    if not args.variants:
        ap.error("give at least one variant directory or --selftest")
    if not args.no_header:
        print(",".join(CSV_COLUMNS))
    for v in args.variants:
        vd = Path(v)
        if vd.name.startswith("_"):
            continue
        if not (vd / "geometry.json").exists():
            print(f"{vd.name},,,,,no-geometry")
            continue
        res = measure_variant(vd, Path(args.image) if args.image else None)
        print(res.row())
    return 0


if __name__ == "__main__":
    sys.exit(main())
