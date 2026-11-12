#!/usr/bin/env python3
# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 The OpenVirtue Authors
"""Hold keys and move the mouse in an oracle carrier's DOSBox-X window through XTest.

DOSBox-X AUTOTYPE presses keys but cannot hold one, and synthetic XSendEvent input is not
taken as held keys or relative mouse motion. This helper is meant for an emulator running on
its own X server (Xvfb), so XTest input reaches nothing else: it waits for the window whose
name contains `--title`, puts the pointer in its middle (giving it focus without a window
manager), clicks once so DOSBox-X locks the mouse (`autolock=true`), then plays a schedule of
held keys and relative mouse motion and logs every step with its time.
Standard library only (ctypes, libX11 and libXtst); run with `python3 -I`.
"""

import argparse
import ctypes
import ctypes.util
import sys
import time

KEYSYMS = {
    "up": 0xFF52, "down": 0xFF54, "left": 0xFF51, "right": 0xFF53,
    "pageup": 0xFF55, "pagedown": 0xFF56, "home": 0xFF50, "end": 0xFF57,
    "shift": 0xFFE1, "alt": 0xFFE9, "ctrl": 0xFFE3,
    "comma": 0x2C, "period": 0x2E,
}

# (label, keys held, mouse dx and dy per step, steps, seconds per step[, seconds between key presses]); a
# gap of no input follows each.
SCHEDULES = {}
SCHEDULES["basic"] = [
    ("up", ["up"], 0, 0, 1, 1.5),
    ("up-shift", ["shift", "up"], 0, 0, 1, 1.5),
    ("right", ["right"], 0, 0, 1, 1.5),
    ("right-shift", ["shift", "right"], 0, 0, 1, 1.5),
    ("comma", ["comma"], 0, 0, 1, 1.5),
    ("alt-right", ["alt", "right"], 0, 0, 1, 1.5),
    ("pageup", ["pageup"], 0, 0, 1, 1.5),
    ("home", ["home"], 0, 0, 1, 1.5),
    ("end", ["end"], 0, 0, 1, 1.5),
    ("mouse-r1", [], 1, 0, 60, 1 / 60),
    ("mouse-r4", [], 4, 0, 60, 1 / 60),
    ("mouse-r16", [], 16, 0, 60, 1 / 60),
    ("mouse-r64", [], 64, 0, 30, 1 / 60),
    ("mouse-l8", [], -8, 0, 60, 1 / 60),
    ("mouse-up8", [], 0, -8, 60, 1 / 60),
    ("mouse-down8", [], 0, 8, 60, 1 / 60),
    ("alt-mouse-r8", ["alt"], 8, 0, 60, 1 / 60),
    ("up-mouse-r8", ["up"], 8, 0, 60, 1 / 60),
    ("mouse-burst40", [], 40, 0, 1, 0.5),
]
SCHEDULES["order"] = [
    ("up", ["up"], 0, 0, 1, 1.5),
    ("shift-then-up", ["shift", "up"], 0, 0, 1, 1.5, 0.5),
    ("up-then-shift", ["up", "shift"], 0, 0, 1, 1.5, 0.5),
    ("right-then-shift", ["right", "shift"], 0, 0, 1, 1.5, 0.5),
    ("up-then-down", ["up", "down"], 0, 0, 1, 1.5, 0.5),
    ("right-mouse-r8", ["right"], 8, 0, 90, 1 / 60),
    ("right-mouse-l8", ["right"], -8, 0, 90, 1 / 60),
    ("up-mouse-up8", ["up"], 0, -8, 90, 1 / 60),
    ("mouse-r8-then-alt", [], 8, 0, 60, 1 / 60),
    ("alt-hold", ["alt"], 0, 0, 1, 1.0),
    ("comma-then-period", ["comma", "period"], 0, 0, 1, 1.5, 0.5),
]


def load():
    x11 = ctypes.CDLL(ctypes.util.find_library("X11") or sys.exit("libX11 not found"))
    xtst = ctypes.CDLL(ctypes.util.find_library("Xtst") or sys.exit("libXtst not found"))
    x11.XOpenDisplay.restype = ctypes.c_void_p
    x11.XOpenDisplay.argtypes = [ctypes.c_char_p]
    x11.XDefaultRootWindow.restype = ctypes.c_ulong
    x11.XDefaultRootWindow.argtypes = [ctypes.c_void_p]
    x11.XQueryTree.argtypes = [ctypes.c_void_p, ctypes.c_ulong, ctypes.POINTER(ctypes.c_ulong),
                               ctypes.POINTER(ctypes.c_ulong), ctypes.POINTER(ctypes.POINTER(ctypes.c_ulong)),
                               ctypes.POINTER(ctypes.c_uint)]
    x11.XFetchName.argtypes = [ctypes.c_void_p, ctypes.c_ulong, ctypes.POINTER(ctypes.c_char_p)]
    x11.XFree.argtypes = [ctypes.c_void_p]
    x11.XKeysymToKeycode.restype = ctypes.c_ubyte
    x11.XKeysymToKeycode.argtypes = [ctypes.c_void_p, ctypes.c_ulong]
    x11.XFlush.argtypes = [ctypes.c_void_p]
    x11.XSync.argtypes = [ctypes.c_void_p, ctypes.c_int]

    class Attrs(ctypes.Structure):
        _fields_ = [("x", ctypes.c_int), ("y", ctypes.c_int), ("width", ctypes.c_int), ("height", ctypes.c_int),
                    ("pad", ctypes.c_byte * 256)]

    x11.XGetWindowAttributes.argtypes = [ctypes.c_void_p, ctypes.c_ulong, ctypes.POINTER(Attrs)]
    x11.XTranslateCoordinates.argtypes = [ctypes.c_void_p, ctypes.c_ulong, ctypes.c_ulong, ctypes.c_int, ctypes.c_int,
                                          ctypes.POINTER(ctypes.c_int), ctypes.POINTER(ctypes.c_int),
                                          ctypes.POINTER(ctypes.c_ulong)]
    xtst.XTestFakeKeyEvent.argtypes = [ctypes.c_void_p, ctypes.c_uint, ctypes.c_int, ctypes.c_ulong]
    xtst.XTestFakeButtonEvent.argtypes = [ctypes.c_void_p, ctypes.c_uint, ctypes.c_int, ctypes.c_ulong]
    xtst.XTestFakeMotionEvent.argtypes = [ctypes.c_void_p, ctypes.c_int, ctypes.c_int, ctypes.c_int, ctypes.c_ulong]
    xtst.XTestFakeRelativeMotionEvent.argtypes = [ctypes.c_void_p, ctypes.c_int, ctypes.c_int, ctypes.c_ulong]
    return x11, xtst, Attrs


def find_window(x11, display, window, title):
    name = ctypes.c_char_p()
    if x11.XFetchName(display, window, ctypes.byref(name)) and name.value:
        found = title.encode() in name.value
        x11.XFree(name)
        if found:
            return window
    root, parent = ctypes.c_ulong(), ctypes.c_ulong()
    children, count = ctypes.POINTER(ctypes.c_ulong)(), ctypes.c_uint()
    if not x11.XQueryTree(display, window, ctypes.byref(root), ctypes.byref(parent), ctypes.byref(children), ctypes.byref(count)):
        return None
    try:
        for i in range(count.value):
            hit = find_window(x11, display, children[i], title)
            if hit:
                return hit
    finally:
        if children:
            x11.XFree(children)
    return None


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--title", required=True)
    ap.add_argument("--start-after", type=float, default=20.0, help="seconds after the window appears")
    ap.add_argument("--gap", type=float, default=1.0)
    ap.add_argument("--log", required=True)
    ap.add_argument("--schedule", default="basic", choices=sorted(SCHEDULES))
    args = ap.parse_args()
    x11, xtst, Attrs = load()
    display = x11.XOpenDisplay(None)
    if not display:
        sys.exit("cannot open the display")
    root = x11.XDefaultRootWindow(display)
    log = open(args.log, "w", buffering=1)
    t0 = time.monotonic()

    def note(text):
        log.write(f"{time.monotonic() - t0:9.3f} {text}\n")

    window = None
    while window is None:
        window = find_window(x11, display, root, args.title)
        if window is None:
            if time.monotonic() - t0 > 120:
                sys.exit("window not found")
            time.sleep(0.25)
    attrs = Attrs()
    x11.XGetWindowAttributes(display, window, ctypes.byref(attrs))
    rx, ry, child = ctypes.c_int(), ctypes.c_int(), ctypes.c_ulong()
    x11.XTranslateCoordinates(display, window, root, attrs.width // 2, attrs.height // 2,
                              ctypes.byref(rx), ctypes.byref(ry), ctypes.byref(child))
    note(f"window {window:#x} {attrs.width}x{attrs.height} centre {rx.value},{ry.value}")
    time.sleep(args.start_after)
    xtst.XTestFakeMotionEvent(display, -1, rx.value, ry.value, 0)
    x11.XSync(display, 0)
    time.sleep(0.3)
    xtst.XTestFakeButtonEvent(display, 1, 1, 0)
    xtst.XTestFakeButtonEvent(display, 1, 0, 0)
    x11.XSync(display, 0)
    note("click (lock)")
    time.sleep(2.0)
    for label, keys, dx, dy, steps, period, *rest in SCHEDULES[args.schedule]:
        stagger = rest[0] if rest else 0.0
        codes = [x11.XKeysymToKeycode(display, KEYSYMS[k]) for k in keys]
        note(f"begin {label}")
        for n, code in enumerate(codes):
            if n and stagger:
                time.sleep(stagger)
                note(f"  press {keys[n]}")
            xtst.XTestFakeKeyEvent(display, code, 1, 0)
            x11.XSync(display, 0)
        start = time.monotonic()
        for i in range(steps):
            if dx or dy:
                xtst.XTestFakeRelativeMotionEvent(display, dx, dy, 0)
                x11.XSync(display, 0)
            target = start + (i + 1) * period
            time.sleep(max(0.0, target - time.monotonic()))
        for code in reversed(codes):
            xtst.XTestFakeKeyEvent(display, code, 0, 0)
        x11.XSync(display, 0)
        note(f"end {label}")
        time.sleep(args.gap)
    note("done")


if __name__ == "__main__":
    main()
