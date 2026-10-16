#!/usr/bin/env python3
# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 The OpenVirtue Authors
"""Send one left mouse click to an oracle carrier's DOSBox-X window.

DOSBox-X AUTOTYPE only presses mapper keys, so the trace probes that need a mouse button
(`TraceProbeCase.ClickAfterSeconds`) start this helper from `run.sh`. It waits, finds the X11
window whose name contains the carrier's title (DOSBox-X's SDL1 build runs under X11 or
XWayland), and sends a ButtonPress/ButtonRelease pair for button 1 to that window and all its
children with XSendEvent. It does not move the host pointer or touch any other window.
Standard library only (ctypes and libX11); run with `python3 -I`.
"""

import argparse
import ctypes
import ctypes.util
import sys
import time

BUTTON_PRESS = 4
BUTTON_RELEASE = 5
BUTTON_PRESS_MASK = 1 << 2
BUTTON_RELEASE_MASK = 1 << 3
BUTTON1_MASK = 1 << 8


class XButtonEvent(ctypes.Structure):
    _fields_ = [
        ("type", ctypes.c_int),
        ("serial", ctypes.c_ulong),
        ("send_event", ctypes.c_int),
        ("display", ctypes.c_void_p),
        ("window", ctypes.c_ulong),
        ("root", ctypes.c_ulong),
        ("subwindow", ctypes.c_ulong),
        ("time", ctypes.c_ulong),
        ("x", ctypes.c_int),
        ("y", ctypes.c_int),
        ("x_root", ctypes.c_int),
        ("y_root", ctypes.c_int),
        ("state", ctypes.c_uint),
        ("button", ctypes.c_uint),
        ("same_screen", ctypes.c_int),
    ]


class XEvent(ctypes.Union):
    _fields_ = [("type", ctypes.c_int), ("xbutton", XButtonEvent), ("pad", ctypes.c_long * 24)]


def load_x11():
    name = ctypes.util.find_library("X11")
    if name is None:
        sys.exit("libX11 not found")
    x11 = ctypes.CDLL(name)
    x11.XOpenDisplay.restype = ctypes.c_void_p
    x11.XOpenDisplay.argtypes = [ctypes.c_char_p]
    x11.XDefaultRootWindow.restype = ctypes.c_ulong
    x11.XDefaultRootWindow.argtypes = [ctypes.c_void_p]
    x11.XQueryTree.argtypes = [
        ctypes.c_void_p, ctypes.c_ulong, ctypes.POINTER(ctypes.c_ulong), ctypes.POINTER(ctypes.c_ulong),
        ctypes.POINTER(ctypes.POINTER(ctypes.c_ulong)), ctypes.POINTER(ctypes.c_uint)]
    x11.XFetchName.argtypes = [ctypes.c_void_p, ctypes.c_ulong, ctypes.POINTER(ctypes.c_char_p)]
    x11.XFree.argtypes = [ctypes.c_void_p]
    x11.XSendEvent.argtypes = [ctypes.c_void_p, ctypes.c_ulong, ctypes.c_int, ctypes.c_long, ctypes.POINTER(XEvent)]
    x11.XFlush.argtypes = [ctypes.c_void_p]
    x11.XCloseDisplay.argtypes = [ctypes.c_void_p]
    return x11


def children(x11, display, window):
    root = ctypes.c_ulong()
    parent = ctypes.c_ulong()
    kids = ctypes.POINTER(ctypes.c_ulong)()
    count = ctypes.c_uint()
    if not x11.XQueryTree(display, window, ctypes.byref(root), ctypes.byref(parent), ctypes.byref(kids), ctypes.byref(count)):
        return []
    result = [kids[i] for i in range(count.value)]
    if kids:
        x11.XFree(kids)
    return result


def window_name(x11, display, window):
    name = ctypes.c_char_p()
    if x11.XFetchName(display, window, ctypes.byref(name)) and name.value is not None:
        value = name.value.decode("latin-1")
        x11.XFree(name)
        return value
    return None


def find_windows(x11, display, root, title):
    """Every window whose name contains `title`, plus all of its descendants."""
    matched = []
    stack = [root]
    while stack:
        window = stack.pop()
        name = window_name(x11, display, window)
        if name is not None and title in name:
            subtree = [window]
            while subtree:
                w = subtree.pop()
                matched.append(w)
                subtree.extend(children(x11, display, w))
            continue
        stack.extend(children(x11, display, window))
    return matched


def send(x11, display, root, window, kind):
    event = XEvent()
    b = event.xbutton
    b.type = kind
    b.send_event = 1
    b.display = display
    b.window = window
    b.root = root
    b.subwindow = 0
    b.time = 0
    b.x = b.y = 100
    b.x_root = b.y_root = 100
    b.state = 0 if kind == BUTTON_PRESS else BUTTON1_MASK
    b.button = 1
    b.same_screen = 1
    mask = BUTTON_PRESS_MASK if kind == BUTTON_PRESS else BUTTON_RELEASE_MASK
    return x11.XSendEvent(display, window, 1, mask, ctypes.byref(event))


def main():
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--title", required=True, help="substring of the DOSBox-X window name")
    parser.add_argument("--after", type=float, required=True, help="seconds to wait before the click")
    parser.add_argument("--hold", type=float, default=0.25, help="seconds between press and release")
    args = parser.parse_args()

    start = time.monotonic()
    x11 = load_x11()
    display = x11.XOpenDisplay(None)
    if not display:
        sys.exit("cannot open the X display (is DISPLAY set?)")
    root = x11.XDefaultRootWindow(display)

    time.sleep(max(0.0, args.after - (time.monotonic() - start)))
    windows = find_windows(x11, display, root, args.title)
    print(f"t={time.monotonic() - start:.2f}s windows={[hex(w) for w in windows]}", flush=True)
    if not windows:
        x11.XCloseDisplay(display)
        sys.exit(f"no window named like '{args.title}'")

    for w in windows:
        send(x11, display, root, w, BUTTON_PRESS)
    x11.XFlush(display)
    print(f"t={time.monotonic() - start:.2f}s press", flush=True)
    time.sleep(args.hold)
    for w in windows:
        send(x11, display, root, w, BUTTON_RELEASE)
    x11.XFlush(display)
    print(f"t={time.monotonic() - start:.2f}s release", flush=True)
    x11.XCloseDisplay(display)


if __name__ == "__main__":
    main()
