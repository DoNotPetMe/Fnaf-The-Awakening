#!/usr/bin/env python3
"""
Render the generated cast of The Awakening to images, without Unity.

AnimatronicFactory builds its characters from data at load time, which means the
only way to see one is normally to open the editor and press Play. This is a
faithful port of that construction — the same bone hierarchy, the same primitives,
the same per-species dressing, the same weathering maths — feeding a small software
rasteriser, so the cast can be reviewed from a terminal, in CI, or in a pull request.

    python3 Tools/render_cast.py --out renders/

It is a preview, not a screenshot: lighting and post are this script's own, and
Unity's materials will read differently. What it is faithful about is the geometry,
the proportions and the colours.
"""

from __future__ import annotations

import argparse
import math
import os
from dataclasses import dataclass, field

import numpy as np
from PIL import Image, ImageDraw, ImageFont

# ---------------------------------------------------------------------------
# Maths, mirroring UnityEngine where it matters
# ---------------------------------------------------------------------------


def euler(x: float, y: float, z: float) -> np.ndarray:
    """Unity's Quaternion.Euler as a matrix: Z, then X, then Y."""
    rx, ry, rz = math.radians(x), math.radians(y), math.radians(z)

    cx, sx = math.cos(rx), math.sin(rx)
    cy, sy = math.cos(ry), math.sin(ry)
    cz, sz = math.cos(rz), math.sin(rz)

    mx = np.array([[1, 0, 0], [0, cx, -sx], [0, sx, cx]], dtype=np.float64)
    my = np.array([[cy, 0, sy], [0, 1, 0], [-sy, 0, cy]], dtype=np.float64)
    mz = np.array([[cz, -sz, 0], [sz, cz, 0], [0, 0, 1]], dtype=np.float64)

    return my @ mx @ mz


IDENTITY = np.eye(3)


def hash4(x: int, y: int, z: int, seed: int) -> float:
    """Port of ProcNoise.Hash, including the unsigned wraparound."""
    h = (x * 374761393 + y * 668265263 + z * 1442695040 + seed * 2246822519) & 0xFFFFFFFF
    h = ((h ^ (h >> 13)) * 1274126177) & 0xFFFFFFFF
    h ^= h >> 16
    return (h & 0xFFFFFF) / 16777216.0


def rgb_to_hsv(c):
    r, g, b = c
    mx, mn = max(r, g, b), min(r, g, b)
    v = mx
    d = mx - mn
    s = 0.0 if mx <= 0 else d / mx
    if d == 0:
        h = 0.0
    elif mx == r:
        h = ((g - b) / d) % 6
    elif mx == g:
        h = (b - r) / d + 2
    else:
        h = (r - g) / d + 4
    return h / 6.0, s, v


def hsv_to_rgb(h, s, v):
    i = int(h * 6) % 6
    f = h * 6 - int(h * 6)
    p, q, t = v * (1 - s), v * (1 - f * s), v * (1 - (1 - f) * s)
    return [(v, t, p), (q, v, p), (p, v, t), (p, q, v), (t, p, v), (v, p, q)][i]


# ---------------------------------------------------------------------------
# Specs — mirrors SettingsAssetBuilder
# ---------------------------------------------------------------------------


@dataclass
class Spec:
    key: str
    name: str
    role: str
    species: str
    height: float
    bulk: float
    head_scale: float
    shell_primary: tuple
    shell_secondary: tuple
    fabric: tuple
    eye_glow: tuple
    wear: float
    exposed: bool
    seed: int
    dossier: str = ""


CAST = [
    Spec("barty", "Bartholomew Bellows", "PROSPECTOR BEAR  ·  show host", "bear",
         2.05, 1.35, 1.20,
         (0.40, 0.24, 0.13), (0.72, 0.55, 0.32), (0.42, 0.13, 0.15), (1.00, 0.76, 0.32),
         0.60, True, 1,
         "Stopped by a shut blast door. Comes toward noise, and closing the door is noise."),
    Spec("vesper", "Vesper", "CAVE BAT  ·  flight effect, 1984", "bat",
         1.55, 0.72, 1.30,
         (0.22, 0.18, 0.26), (0.38, 0.30, 0.40), (0.30, 0.16, 0.24), (0.85, 0.55, 1.00),
         0.75, True, 2,
         "Uses the crawlways, where there are no cameras. Only light turns her back, and "
         "she moves faster the quieter the facility is."),
    Spec("marlow", "Marlow", "MOLE  ·  “Down Below”, 1981", "mole",
         1.45, 1.25, 1.15,
         (0.26, 0.20, 0.17), (0.46, 0.35, 0.28), (0.30, 0.26, 0.18), (1.00, 0.62, 0.25),
         0.80, True, 3,
         "Tunnels through the sump floor, which has to be dry. He is the price of running "
         "the pump."),
    Spec("echo", "Echo", "SALAMANDER  ·  closing number, 1986", "salamander",
         1.60, 0.95, 1.10,
         (0.14, 0.26, 0.25), (0.28, 0.44, 0.40), (0.55, 0.20, 0.26), (0.45, 1.00, 0.85),
         0.90, False, 4,
         "Swims the channel, which has to be deep. She is the price of not running it, and "
         "a bolted grate is odds rather than a wall."),
    Spec("chorus", "The Chorus", "NO SERVICE RECORD", "composite",
         2.25, 1.10, 1.00,
         (0.13, 0.12, 0.14), (0.30, 0.20, 0.18), (0.24, 0.10, 0.12), (1.00, 0.22, 0.18),
         1.00, True, 5,
         "Stopped by nothing. It leans on a door until the door stops being a door. Only a "
         "completely dark, silent station loses it."),
]

REFERENCE_HEIGHT = 1.95

# Material ids, so the shader can treat plastic, steel and cloth differently.
SHELL, METAL, FABRIC, GLOW = 0, 1, 2, 3


def weathered(base, spec: Spec, part_seed: int):
    """Port of AnimatronicFactory.Weathered."""
    variation = hash4(part_seed, spec.seed, 3, 17)
    grime = spec.wear * (0.55 + (1.0 - 0.55) * variation)

    h, s, v = rgb_to_hsv(base)
    s *= 1.0 - grime * 0.28
    v *= 1.0 - grime * 0.24
    aged = hsv_to_rgb(h, s, v)

    dust = (0.42, 0.40, 0.35)
    k = grime * 0.16
    return tuple(aged[i] * (1 - k) + dust[i] * k for i in range(3))


# ---------------------------------------------------------------------------
# Mesh accumulation — mirrors MeshBuilder
# ---------------------------------------------------------------------------


@dataclass
class Mesh:
    verts: list = field(default_factory=list)
    normals: list = field(default_factory=list)
    colors: list = field(default_factory=list)
    materials: list = field(default_factory=list)
    tris: list = field(default_factory=list)

    def add(self, p, n, c, m):
        self.verts.append(p)
        self.normals.append(n)
        self.colors.append(c)
        self.materials.append(m)
        return len(self.verts) - 1

    def tri(self, a, b, c):
        self.tris.append((a, b, c))

    def quad(self, a, b, c, d):
        self.tri(a, b, c)
        self.tri(a, c, d)


class PartBuilder:
    """Emits primitives into a mesh under a transform, colour and material."""

    def __init__(self, mesh: Mesh, matrix: np.ndarray, origin: np.ndarray):
        self.mesh = mesh
        self.matrix = matrix
        self.origin = origin
        self.color = (1.0, 1.0, 1.0)
        self.material = SHELL

    def _p(self, local):
        return self.origin + self.matrix @ np.asarray(local, dtype=np.float64)

    def _n(self, local):
        n = self.matrix @ np.asarray(local, dtype=np.float64)
        length = np.linalg.norm(n)
        return n / length if length > 1e-9 else np.array([0.0, 1.0, 0.0])

    # -- primitives ---------------------------------------------------------

    def box(self, centre, size, rot=None):
        centre = np.asarray(centre, dtype=np.float64)
        h = np.asarray(size, dtype=np.float64) * 0.5
        r = IDENTITY if rot is None else rot

        corners = {}
        for sx in (-1, 1):
            for sy in (-1, 1):
                for sz in (-1, 1):
                    corners[(sx, sy, sz)] = centre + r @ (h * np.array([sx, sy, sz], dtype=np.float64))

        faces = [
            ([(-1, -1, 1), (1, -1, 1), (1, 1, 1), (-1, 1, 1)], (0, 0, 1)),
            ([(1, -1, -1), (-1, -1, -1), (-1, 1, -1), (1, 1, -1)], (0, 0, -1)),
            ([(1, -1, 1), (1, -1, -1), (1, 1, -1), (1, 1, 1)], (1, 0, 0)),
            ([(-1, -1, -1), (-1, -1, 1), (-1, 1, 1), (-1, 1, -1)], (-1, 0, 0)),
            ([(-1, 1, 1), (1, 1, 1), (1, 1, -1), (-1, 1, -1)], (0, 1, 0)),
            ([(-1, -1, -1), (1, -1, -1), (1, -1, 1), (-1, -1, 1)], (0, -1, 0)),
        ]

        for keys, normal in faces:
            n = self._n(r @ np.asarray(normal, dtype=np.float64))
            idx = [self.mesh.add(self._p(corners[k]), n, self.color, self.material) for k in keys]
            self.mesh.quad(*idx)

    def rounded_box(self, centre, size, rot=None, bevel=0.08):
        size = np.asarray(size, dtype=np.float64)
        inner = size * (1.0 - bevel)
        self.box(centre, [size[0], inner[1], inner[2]], rot)
        self.box(centre, [inner[0], size[1], inner[2]], rot)
        self.box(centre, [inner[0], inner[1], size[2]], rot)

    def sphere(self, centre, radius, segments=12, rings=8, scale=(1, 1, 1)):
        centre = np.asarray(centre, dtype=np.float64)
        s = np.asarray(scale, dtype=np.float64)
        ring_starts = []

        for r in range(rings):
            phi = math.pi * (r + 1) / (rings + 1)
            y = math.cos(phi) * radius
            rr = math.sin(phi) * radius
            first = len(self.mesh.verts)

            for i in range(segments):
                a = (i / segments) * math.tau
                local = np.array([math.cos(a) * rr * s[0], y * s[1], math.sin(a) * rr * s[2]])
                self.mesh.add(self._p(centre + local),
                              self._n(local), self.color, self.material)
            ring_starts.append(first)

        for r in range(rings - 1):
            lo, hi = ring_starts[r + 1], ring_starts[r]
            for i in range(segments):
                nx = (i + 1) % segments
                self.mesh.tri(lo + i, hi + i, hi + nx)
                self.mesh.tri(lo + i, hi + nx, lo + nx)

        top = self.mesh.add(self._p(centre + np.array([0, radius * s[1], 0])),
                            self._n([0, 1, 0]), self.color, self.material)
        bottom = self.mesh.add(self._p(centre - np.array([0, radius * s[1], 0])),
                               self._n([0, -1, 0]), self.color, self.material)

        for i in range(segments):
            nx = (i + 1) % segments
            self.mesh.tri(top, ring_starts[0] + nx, ring_starts[0] + i)
            self.mesh.tri(bottom, ring_starts[-1] + i, ring_starts[-1] + nx)

    def cylinder(self, base, radius, height, segments=12, rot=None, top_scale=1.0, caps=True):
        base = np.asarray(base, dtype=np.float64)
        r = IDENTITY if rot is None else rot

        def ring(centre_local, rad):
            first = len(self.mesh.verts)
            for i in range(segments):
                a = (i / segments) * math.tau
                offset = np.array([math.cos(a) * rad, 0.0, math.sin(a) * rad])
                self.mesh.add(self._p(centre_local + r @ offset),
                              self._n(r @ np.array([math.cos(a), 0.0, math.sin(a)])),
                              self.color, self.material)
            return first

        top_centre = base + r @ np.array([0.0, height, 0.0])
        lower = ring(base, radius)
        upper = ring(top_centre, radius * top_scale)

        for i in range(segments):
            nx = (i + 1) % segments
            self.mesh.tri(lower + i, upper + i, upper + nx)
            self.mesh.tri(lower + i, upper + nx, lower + nx)

        if not caps:
            return

        self.disc(top_centre, r @ np.array([0.0, 1.0, 0.0]), radius * top_scale, segments, r)
        self.disc(base, r @ np.array([0.0, -1.0, 0.0]), radius, segments, r)

    def cone(self, base, radius, height, segments=10, rot=None):
        base = np.asarray(base, dtype=np.float64)
        r = IDENTITY if rot is None else rot

        first = len(self.mesh.verts)
        for i in range(segments):
            a = (i / segments) * math.tau
            offset = np.array([math.cos(a) * radius, 0.0, math.sin(a) * radius])
            self.mesh.add(self._p(base + r @ offset),
                          self._n(r @ np.array([math.cos(a), 0.4, math.sin(a)])),
                          self.color, self.material)

        apex_local = base + r @ np.array([0.0, height, 0.0])
        for i in range(segments):
            nx = (i + 1) % segments
            apex = self.mesh.add(self._p(apex_local), self._n(r @ np.array([0.0, 1.0, 0.0])),
                                 self.color, self.material)
            self.mesh.tri(first + i, apex, first + nx)

        self.disc(base, r @ np.array([0.0, -1.0, 0.0]), radius, segments, r)

    def disc(self, centre, normal, radius, segments, rot):
        centre = np.asarray(centre, dtype=np.float64)
        n = self._n(normal)
        c = self.mesh.add(self._p(centre), n, self.color, self.material)

        first = len(self.mesh.verts)
        for i in range(segments):
            a = (i / segments) * math.tau
            offset = rot @ np.array([math.cos(a) * radius, 0.0, math.sin(a) * radius])
            self.mesh.add(self._p(centre + offset), n, self.color, self.material)

        up = self.matrix @ (rot @ np.array([0.0, 1.0, 0.0]))
        face_up = float(np.dot(n, up / max(np.linalg.norm(up), 1e-9))) >= 0

        for i in range(segments):
            a = first + i
            b = first + (i + 1) % segments
            if face_up:
                self.mesh.tri(c, b, a)
            else:
                self.mesh.tri(c, a, b)


# ---------------------------------------------------------------------------
# Character construction — mirrors AnimatronicFactory
# ---------------------------------------------------------------------------


class Rig:
    """Bone transforms, resolved to world space as they are created."""

    def __init__(self):
        self.bones = {}

    def bone(self, name, parent, local_pos, local_rot=None):
        local_rot = IDENTITY if local_rot is None else local_rot
        local_pos = np.asarray(local_pos, dtype=np.float64)

        if parent is None:
            origin, matrix = np.zeros(3), IDENTITY
        else:
            origin, matrix = self.bones[parent]

        self.bones[name] = (origin + matrix @ local_pos, matrix @ local_rot)
        return name

    def at(self, name):
        return self.bones[name]


def build_character(spec: Spec) -> Mesh:
    mesh = Mesh()
    scale = spec.height / REFERENCE_HEIGHT
    bulk, species = spec.bulk, spec.species

    hip_height = {"salamander": 0.58, "mole": 0.72, "bat": 0.88}.get(species, 0.95)
    spine_lean = {"salamander": 38.0, "bat": 26.0, "mole": 16.0, "composite": 20.0}.get(species, 4.0)
    chest_front = 0.13 * bulk

    rig = Rig()
    rig.bone("Hips", None, [0, hip_height * scale, 0])
    rig.bone("Spine", "Hips", [0, 0.18 * scale, 0], euler(spine_lean * 0.4, 0, 0))
    rig.bone("Chest", "Spine", [0, 0.22 * scale, 0], euler(spine_lean * 0.6, 0, 0))
    rig.bone("Neck", "Chest", [0, 0.24 * scale, 0], euler(-spine_lean * 0.8, 0, 0))
    rig.bone("Head", "Neck", [0, 0.12 * scale, 0])

    head_radius = 0.17 * spec.head_scale * scale
    rig.bone("Jaw", "Head", [0, head_radius * 0.14, head_radius * 0.30])

    shoulder_width = 0.22 * bulk
    rig.bone("Shoulder.L", "Chest", [-shoulder_width * scale, 0.12 * scale, 0])
    rig.bone("Shoulder.R", "Chest", [shoulder_width * scale, 0.12 * scale, 0])
    rig.bone("UpperArm.L", "Shoulder.L", [-0.06 * scale, -0.04 * scale, 0], euler(6, 0, 8))
    rig.bone("UpperArm.R", "Shoulder.R", [0.06 * scale, -0.04 * scale, 0], euler(6, 0, -8))
    rig.bone("Forearm.L", "UpperArm.L", [0, -0.32 * scale, 0], euler(8, 0, 0))
    rig.bone("Forearm.R", "UpperArm.R", [0, -0.32 * scale, 0], euler(8, 0, 0))
    rig.bone("Hand.L", "Forearm.L", [0, -0.30 * scale, 0])
    rig.bone("Hand.R", "Forearm.R", [0, -0.30 * scale, 0])

    hip_width = 0.13 * bulk
    thigh_length = hip_height * 0.48
    shin_length = hip_height * 0.46
    rig.bone("Thigh.L", "Hips", [-hip_width * scale, -0.04 * scale, 0])
    rig.bone("Thigh.R", "Hips", [hip_width * scale, -0.04 * scale, 0])
    rig.bone("Shin.L", "Thigh.L", [0, -thigh_length * scale, 0])
    rig.bone("Shin.R", "Thigh.R", [0, -thigh_length * scale, 0])
    rig.bone("Foot.L", "Shin.L", [0, -shin_length * scale, 0])
    rig.bone("Foot.R", "Shin.R", [0, -shin_length * scale, 0])

    if species in ("salamander", "composite"):
        rig.bone("Tail", "Hips", [0, 0.02 * scale, -0.16 * scale], euler(-14, 0, 0))

    def part(bone, color=None, material=SHELL):
        origin, matrix = rig.at(bone)
        b = PartBuilder(mesh, matrix, origin)
        if color is not None:
            b.color = color
        b.material = material
        return b

    head = 0.17 * spec.head_scale * scale

    # ---- torso ------------------------------------------------------------
    part("Hips", weathered(spec.shell_primary, spec, 1)).rounded_box(
        [0, 0, 0], np.array([0.30 * bulk, 0.22, 0.22 * bulk]) * scale)

    part("Spine", weathered(spec.shell_secondary, spec, 2)).sphere(
        np.array([0, 0.09, 0.03]) * scale, 0.17 * scale, 16, 12,
        (bulk * 0.95, 0.95, bulk * 0.85))

    part("Chest", weathered(spec.shell_primary, spec, 3)).rounded_box(
        np.array([0, 0.10, 0]) * scale,
        np.array([0.40 * bulk, 0.34, 0.26 * bulk]) * scale, bevel=0.22)

    if spec.exposed and spec.wear > 0.4:
        frame = part("Chest", (0.60, 0.60, 0.62), METAL)
        for i in range(3):
            frame.cylinder(np.array([-0.09 + i * 0.09, -0.02, chest_front * 0.92]) * scale,
                           0.018 * scale, 0.30 * scale, 6)
        frame.cylinder(np.array([0, 0.10, chest_front * 0.92]) * scale, 0.03 * scale, 0.20 * scale, 8,
                       euler(0, 0, 90))

    part("Neck", (0.45, 0.46, 0.48), METAL).cylinder(
        np.array([0, -0.06, 0]) * scale, 0.062 * scale, 0.20 * scale, 10)

    part("Neck", weathered(spec.shell_primary, spec, 4)).cylinder(
        np.array([0, -0.07, 0]) * scale, 0.105 * bulk * scale, 0.09 * scale, 12, top_scale=0.82)

    # ---- arms -------------------------------------------------------------
    for side in (0, 1):
        tag = "L" if side == 0 else "R"
        part(f"Shoulder.{tag}", weathered(spec.shell_primary, spec, 10 + side)).sphere(
            [0, 0, 0], 0.10 * bulk * scale, 12, 8)

        stripped = spec.exposed and spec.wear > 0.7 and side == 1
        upper = part(f"UpperArm.{tag}",
                     (0.55, 0.56, 0.58) if stripped else weathered(spec.shell_primary, spec, 12 + side),
                     METAL if stripped else SHELL)
        upper.cylinder(np.array([0, -0.30, 0]) * scale,
                       (0.035 if stripped else 0.072) * bulk * scale, 0.30 * scale, 10,
                       top_scale=1.1)

        part(f"Forearm.{tag}", weathered(spec.shell_secondary, spec, 14 + side)).cylinder(
            np.array([0, -0.28, 0]) * scale, 0.062 * bulk * scale, 0.28 * scale, 10, top_scale=1.15)

        # hand
        digger = species == "mole"
        palm_width = 0.14 if digger else 0.09
        part(f"Hand.{tag}", weathered(spec.shell_secondary, spec, 20 + side)).rounded_box(
            np.array([0, -0.05, 0]) * scale,
            np.array([palm_width, 0.11, 0.05]) * scale, bevel=0.2)

        claw = part(f"Hand.{tag}",
                    (0.62, 0.60, 0.55) if digger else weathered(spec.shell_secondary, spec, 24),
                    METAL if digger else SHELL)
        finger = 0.16 if digger else 0.09
        for f in range(3):
            claw.cone(np.array([(f - 1) * 0.038, -0.10, 0]) * scale,
                      0.020 * scale, finger * scale, 6, euler(180, 0, 0))
        claw.cone(np.array([0.06 if side == 0 else -0.06, -0.07, 0.01]) * scale,
                  0.018 * scale, finger * 0.7 * scale, 6,
                  euler(150, 0, -30 if side == 0 else 30))

    # ---- legs -------------------------------------------------------------
    for side in (0, 1):
        tag = "L" if side == 0 else "R"
        part(f"Thigh.{tag}", weathered(spec.shell_primary, spec, 30 + side)).cylinder(
            [0, -thigh_length * scale, 0], 0.085 * bulk * scale, thigh_length * scale, 10, top_scale=1.25)

        part(f"Shin.{tag}", weathered(spec.shell_primary, spec, 32 + side)).cylinder(
            [0, -shin_length * scale, 0], 0.068 * bulk * scale, shin_length * scale, 10, top_scale=1.2)

        part(f"Shin.{tag}", (0.50, 0.51, 0.53), METAL).cylinder(
            np.array([-0.05, -0.02, 0]) * scale, 0.022 * scale, 0.10 * scale, 6, euler(0, 0, 90))

        part(f"Foot.{tag}", weathered(spec.shell_secondary, spec, 34 + side)).rounded_box(
            np.array([0, 0.04, 0.05]) * scale,
            np.array([0.14 * bulk, 0.08, 0.26]) * scale, bevel=0.25)

    # ---- head -------------------------------------------------------------
    part("Head", weathered(spec.shell_primary, spec, 40)).sphere(
        [0, head * 0.55, 0], head, 18, 14, (1.0, 0.95, 1.05))

    part("Head", weathered(spec.shell_secondary, spec, 41)).sphere(
        [0, head * 0.35, head * 0.78], head * 0.52, 14, 10, (1.15, 0.72, 1.25))

    part("Jaw", weathered(spec.shell_secondary, spec, 42)).rounded_box(
        [0, -head * 0.08, head * 0.44],
        [head * 0.98, head * 0.30, head * 1.05], bevel=0.28)

    teeth = part("Jaw", (0.86, 0.84, 0.78), METAL)
    for i in range(6):
        teeth.box([(i - 2.5) * head * 0.24, head * 0.04, head * 0.92],
                  [head * 0.17, head * 0.15, head * 0.10])

    dress = {
        "bear": dress_bear, "bat": dress_bat, "mole": dress_mole,
        "salamander": dress_salamander, "composite": dress_composite,
    }[species]
    dress(mesh, rig, spec, scale, head, part)

    # ---- eyes -------------------------------------------------------------
    for side in (0, 1):
        eye = part("Head", spec.eye_glow, GLOW)
        eye.sphere([(-1 if side == 0 else 1) * head * 0.33, head * 0.66, head * 0.92],
                   head * 0.155, 14, 12)

    return mesh


def dress_bear(mesh, rig, spec, scale, head, part):
    ears = part("Head", weathered(spec.shell_primary, spec, 50))
    for side in (-1, 1):
        ears.sphere([side * head * 0.72, head * 1.25, -head * 0.1],
                    head * 0.34, 12, 8, (1, 1, 0.45))

    hat = part("Head", (0.28, 0.22, 0.16), FABRIC)
    hat.cylinder([0, head * 1.32, 0], head * 1.35, head * 0.08, 16)
    hat.cylinder([0, head * 1.38, 0], head * 0.78, head * 0.62, 16, top_scale=0.92)

    part("Chest", spec.fabric, FABRIC).rounded_box(
        np.array([0, 0.06, 0.13 * spec.bulk * 0.9]) * scale,
        np.array([0.30 * spec.bulk, 0.28, 0.05]) * scale, bevel=0.15)

    tie = part("Chest", (0.55, 0.09, 0.12), FABRIC)
    tie.box(np.array([-0.05, 0.24, 0.13 * spec.bulk * 1.1]) * scale, np.array([0.08, 0.06, 0.03]) * scale, euler(0, 0, 18))
    tie.box(np.array([0.05, 0.24, 0.13 * spec.bulk * 1.1]) * scale, np.array([0.08, 0.06, 0.03]) * scale, euler(0, 0, -18))


def dress_bat(mesh, rig, spec, scale, head, part):
    ears = part("Head", weathered(spec.shell_secondary, spec, 60))
    for side in (-1, 1):
        ears.cone([side * head * 0.5, head * 1.0, -head * 0.05],
                  head * 0.34, head * 1.5, 10, euler(-12, 0, side * 16))

    for side in (0, 1):
        tag = "L" if side == 0 else "R"
        d = 1.0 if side == 0 else -1.0

        membrane = part(f"Forearm.{tag}",
                        tuple(c * 0.8 for c in spec.fabric), FABRIC)
        membrane.box(np.array([d * 0.13, -0.12, -0.01]) * scale,
                     np.array([0.025, 0.52, 0.46]) * scale, euler(0, 0, d * 22))

        struts = part(f"Forearm.{tag}", (0.40, 0.40, 0.42), METAL)
        for f in range(3):
            struts.cylinder(np.array([d * 0.06, -0.02 - f * 0.02, -0.1]) * scale,
                            0.008 * scale, 0.34 * scale, 5, euler(0, 0, 90 + f * 11))


def dress_mole(mesh, rig, spec, scale, head, part):
    part("Head", (0.62, 0.40, 0.40)).cone(
        [0, head * 0.38, head * 1.05], head * 0.34, head * 0.8, 14, euler(90, 0, 0))

    goggles = part("Head", (0.35, 0.33, 0.30), METAL)
    for side in (-1, 1):
        goggles.cylinder([side * head * 0.36, head * 0.62, head * 0.62],
                         head * 0.26, head * 0.16, 12, euler(90, 0, 0))
    goggles.box([0, head * 0.62, head * 0.2], [head * 1.5, head * 0.12, head * 0.1])

    part("Head", (0.72, 0.52, 0.12)).sphere(
        [0, head * 0.95, 0], head * 1.02, 14, 8, (1, 0.62, 1))


def dress_salamander(mesh, rig, spec, scale, head, part):
    frills = part("Head", (0.68, 0.24, 0.32), FABRIC)
    for side in (-1, 1):
        for f in range(3):
            frills.cone([side * head * 0.62, head * 0.55 + f * head * 0.16, -head * 0.3],
                        head * 0.11, head * 0.5, 7,
                        euler(-30, 0, side * (55 + f * 12)))

    tail = part("Tail", weathered(spec.shell_primary, spec, 70))
    length = 0.26 * scale
    for i in range(3):
        radius = (0.09 + (0.02 - 0.09) * (i / 2.0)) * scale
        tail.cylinder([0, 0, -length * i], radius, length, 10, euler(90, 0, 0), top_scale=0.7)

    crest = part("Chest", (0.55, 0.20, 0.26), FABRIC)
    for i in range(4):
        crest.box(np.array([0, 0.02 + i * 0.07, -0.14]) * scale,
                  np.array([0.02, 0.10, 0.08]) * scale, euler(18, 0, 0))


def dress_composite(mesh, rig, spec, scale, head, part):
    dress_bat(mesh, rig, spec, scale, head, part)

    extra = part("Chest", weathered(spec.shell_secondary, spec, 80))
    extra.rounded_box(np.array([0, 0.06, 0.13 * spec.bulk * 1.15]) * scale,
                      [head * 0.9, head * 0.3, head * 0.5], euler(12, 0, 0), bevel=0.25)

    extra_teeth = part("Chest", (0.80, 0.78, 0.72), METAL)
    for i in range(5):
        extra_teeth.box([(i - 2) * head * 0.2, 0.06 * scale, 0.13 * spec.bulk * 1.45 * scale],
                        [head * 0.14, head * 0.2, head * 0.08])

    part("Head", weathered((0.45, 0.27, 0.15), spec, 81)).sphere(
        [head * 0.74, head * 1.2, -head * 0.1], head * 0.34, 12, 8, (1, 1, 0.45))

    claws = part("Hand.L", (0.58, 0.56, 0.52), METAL)
    for f in range(3):
        claws.cone(np.array([(f - 1) * 0.04, -0.11, 0]) * scale,
                   0.022 * scale, 0.2 * scale, 6, euler(180, 0, 0))


# ---------------------------------------------------------------------------
# Rasteriser
# ---------------------------------------------------------------------------


def look_at(eye, target, up=(0, 1, 0)):
    eye = np.asarray(eye, dtype=np.float64)
    target = np.asarray(target, dtype=np.float64)

    forward = target - eye
    forward /= np.linalg.norm(forward)
    right = np.cross(forward, np.asarray(up, dtype=np.float64))
    right /= np.linalg.norm(right)
    true_up = np.cross(right, forward)

    return eye, np.stack([right, true_up, forward])


def rasterise(mesh: Mesh, width: int, height: int, eye, basis, fov_degrees=28.0):
    """Deferred pass: depth, albedo, world normal, material and emission buffers."""
    verts = np.asarray(mesh.verts, dtype=np.float64)
    normals = np.asarray(mesh.normals, dtype=np.float64)
    colors = np.asarray(mesh.colors, dtype=np.float64)
    materials = np.asarray(mesh.materials, dtype=np.int32)
    tris = np.asarray(mesh.tris, dtype=np.int32)

    # World -> view.
    view = (verts - eye) @ basis.T
    depth = view[:, 2]

    focal = (0.5 * height) / math.tan(math.radians(fov_degrees) * 0.5)
    safe = np.maximum(depth, 1e-4)

    sx = width * 0.5 + focal * view[:, 0] / safe
    sy = height * 0.5 - focal * view[:, 1] / safe
    inv_w = 1.0 / safe

    zbuf = np.zeros((height, width), dtype=np.float64)          # stores 1/depth
    albedo = np.zeros((height, width, 3), dtype=np.float64)
    normal_buf = np.zeros((height, width, 3), dtype=np.float64)
    mat_buf = np.full((height, width), -1, dtype=np.int32)

    for a, b, c in tris:
        if depth[a] <= 0.01 or depth[b] <= 0.01 or depth[c] <= 0.01:
            continue

        x0, y0, x1, y1, x2, y2 = sx[a], sy[a], sx[b], sy[b], sx[c], sy[c]

        min_x = max(int(math.floor(min(x0, x1, x2))), 0)
        max_x = min(int(math.ceil(max(x0, x1, x2))), width - 1)
        min_y = max(int(math.floor(min(y0, y1, y2))), 0)
        max_y = min(int(math.ceil(max(y0, y1, y2))), height - 1)
        if min_x > max_x or min_y > max_y:
            continue

        area = (x1 - x0) * (y2 - y0) - (x2 - x0) * (y1 - y0)
        if abs(area) < 1e-9:
            continue

        px = np.arange(min_x, max_x + 1, dtype=np.float64)[None, :]
        py = np.arange(min_y, max_y + 1, dtype=np.float64)[:, None]

        w0 = ((x1 - px) * (y2 - py) - (x2 - px) * (y1 - py)) / area
        w1 = ((x2 - px) * (y0 - py) - (x0 - px) * (y2 - py)) / area
        w2 = 1.0 - w0 - w1

        inside = (w0 >= -1e-6) & (w1 >= -1e-6) & (w2 >= -1e-6)
        if not inside.any():
            continue

        iw = w0 * inv_w[a] + w1 * inv_w[b] + w2 * inv_w[c]

        tile = zbuf[min_y:max_y + 1, min_x:max_x + 1]
        closer = inside & (iw > tile)
        if not closer.any():
            continue

        # Perspective-correct attribute interpolation.
        pw0 = (w0 * inv_w[a]) / iw
        pw1 = (w1 * inv_w[b]) / iw
        pw2 = (w2 * inv_w[c]) / iw

        tri_albedo = (pw0[..., None] * colors[a] + pw1[..., None] * colors[b] + pw2[..., None] * colors[c])
        tri_normal = (pw0[..., None] * normals[a] + pw1[..., None] * normals[b] + pw2[..., None] * normals[c])

        tile[closer] = iw[closer]
        albedo[min_y:max_y + 1, min_x:max_x + 1][closer] = tri_albedo[closer]
        normal_buf[min_y:max_y + 1, min_x:max_x + 1][closer] = tri_normal[closer]

        # Nearest material rather than a blend — they are not interpolatable.
        dominant = materials[a]
        mat_buf[min_y:max_y + 1, min_x:max_x + 1][closer] = dominant

    return zbuf, albedo, normal_buf, mat_buf


def box_blur(image, radius):
    """Separable box blur via summed-area differences."""
    if radius < 1:
        return image

    kernel = 2 * radius + 1
    padded = np.pad(image, ((radius, radius), (radius, radius), (0, 0)), mode="edge")

    def sweep(data, axis):
        # A leading zero makes the window difference produce exactly len - kernel + 1
        # rows, which is the original extent. Without it the result is one short.
        cumulative = np.cumsum(data, axis=axis)
        zeros = np.zeros_like(np.take(cumulative, [0], axis=axis))
        cumulative = np.concatenate([zeros, cumulative], axis=axis)

        upper = np.take(cumulative, range(kernel, cumulative.shape[axis]), axis=axis)
        lower = np.take(cumulative, range(0, cumulative.shape[axis] - kernel), axis=axis)
        return (upper - lower) / kernel

    return sweep(sweep(padded, 0), 1)


def ambient_occlusion(zbuf, strength=0.85):
    """Cheap screen-space AO from the depth buffer."""
    depth = np.where(zbuf > 0, 1.0 / np.maximum(zbuf, 1e-6), 1e6)
    occlusion = np.zeros_like(depth)
    samples = 0

    for radius in (2, 4, 8, 14):
        for dy, dx in ((radius, 0), (-radius, 0), (0, radius), (0, -radius),
                       (radius, radius), (-radius, -radius), (radius, -radius), (-radius, radius)):
            shifted = np.roll(np.roll(depth, dy, axis=0), dx, axis=1)
            # A neighbour meaningfully in front of this pixel occludes it.
            difference = depth - shifted
            occlusion += np.clip(difference / (0.06 * radius), 0.0, 1.0)
            samples += 1

    occlusion = np.clip(occlusion / samples, 0.0, 1.0)
    return np.clip(1.0 - occlusion * strength, 0.0, 1.0)


def aces(x):
    a, b, c, d, e = 2.51, 0.03, 2.43, 0.59, 0.14
    return np.clip((x * (a * x + b)) / (x * (c * x + d) + e), 0.0, 1.0)


def shade(spec: Spec, zbuf, albedo, normal_buf, mat_buf, eye, basis, width, height):
    hit = mat_buf >= 0

    normal = normal_buf.copy()
    length = np.linalg.norm(normal, axis=2, keepdims=True)
    normal = np.divide(normal, np.maximum(length, 1e-9))

    # View vector, reconstructed from the pixel ray.
    focal = (0.5 * height) / math.tan(math.radians(28.0) * 0.5)
    px = (np.arange(width)[None, :] - width * 0.5) / focal
    py = -(np.arange(height)[:, None] - height * 0.5) / focal
    ray = np.stack([np.broadcast_to(px, (height, width)),
                    np.broadcast_to(py, (height, width)),
                    np.ones((height, width))], axis=2)
    ray = ray @ basis          # view -> world
    ray /= np.linalg.norm(ray, axis=2, keepdims=True)
    view_dir = -ray

    # Two-sided: these are closed solids, but a mirrored build would otherwise
    # produce black patches where a face points away.
    facing = np.sum(normal * view_dir, axis=2, keepdims=True)
    normal = np.where(facing < 0, -normal, normal)

    def light(direction, colour, intensity):
        d = np.asarray(direction, dtype=np.float64)
        d /= np.linalg.norm(d)
        lambert = np.clip(np.sum(normal * d, axis=2), 0.0, 1.0)[..., None]

        half = d + view_dir
        half /= np.maximum(np.linalg.norm(half, axis=2, keepdims=True), 1e-9)
        spec_term = np.clip(np.sum(normal * half, axis=2), 0.0, 1.0)[..., None]

        return lambert * np.asarray(colour) * intensity, spec_term

    # Three-point rig: warm key, cool fill, cold rim to carry the silhouette.
    key, key_spec = light((-0.55, 0.72, 0.85), (1.00, 0.86, 0.66), 0.92)
    fill, fill_spec = light((0.92, 0.08, 0.30), (0.42, 0.56, 0.78), 0.26)
    rim, rim_spec = light((-0.20, 0.30, -0.95), (0.62, 0.74, 1.00), 0.70)

    ao = ambient_occlusion(zbuf)[..., None]

    # Hemisphere ambient: cool from above, warm bounce from below.
    up = np.clip(normal[..., 1:2] * 0.5 + 0.5, 0.0, 1.0)
    ambient = (up * np.array([0.085, 0.100, 0.140]) + (1 - up) * np.array([0.055, 0.044, 0.038])) * ao

    gloss = np.select(
        [mat_buf == METAL, mat_buf == FABRIC, mat_buf == GLOW],
        [64.0, 8.0, 16.0], default=26.0)[..., None]
    gloss_strength = np.select(
        [mat_buf == METAL, mat_buf == FABRIC, mat_buf == GLOW],
        [0.85, 0.04, 0.10], default=0.22)[..., None]

    diffuse = (key + fill + rim * 0.55 + ambient) * albedo * ao
    specular = ((key_spec ** gloss) * 1.5 + (rim_spec ** gloss) * 0.9) * gloss_strength

    colour = diffuse + specular

    # Eyes are lamps, not surfaces.
    glow_mask = (mat_buf == GLOW)[..., None]
    emission = np.where(glow_mask, np.asarray(spec.eye_glow) * 2.6, 0.0)
    colour = np.where(glow_mask, emission, colour)

    colour = np.where(hit[..., None], colour, 0.0)

    # Bloom, from the emissive pixels only.
    glow = np.where(glow_mask, np.asarray(spec.eye_glow), 0.0)
    bloom = box_blur(glow, 12) * 0.9 + box_blur(glow, 34) * 0.55
    colour += bloom

    return colour, hit


def compose(colour, hit, width, height, spec: Spec):
    # Backdrop: a cold vertical gradient with a warm pool behind the subject.
    ys = np.linspace(0.0, 1.0, height)[:, None, None]
    xs = np.linspace(-1.0, 1.0, width)[None, :, None]

    backdrop = (np.array([0.030, 0.034, 0.042]) * (1.0 - ys)
                + np.array([0.012, 0.013, 0.016]) * ys)

    halo = np.exp(-((xs ** 2) * 3.0 + ((ys - 0.42) ** 2) * 6.0))
    backdrop = backdrop + halo * np.array([0.055, 0.045, 0.032])

    # A soft contact shadow, so the figure is standing on something.
    floor = np.exp(-(((ys - 0.93) * 22.0) ** 2) - ((xs * 2.2) ** 2))
    backdrop *= 1.0 - floor * 0.55

    image = np.where(hit[..., None], colour, backdrop)

    image = aces(image * 0.95)

    # Vignette and a little grain, matching the game's own presentation.
    radius = np.sqrt(xs ** 2 + (ys * 2 - 1) ** 2)
    image *= np.clip(1.0 - (radius ** 2) * 0.16, 0.0, 1.0)

    rng = np.random.default_rng(spec.seed)
    image += (rng.random((height, width, 1)) - 0.5) * 0.012

    image = np.clip(image, 0.0, 1.0) ** (1.0 / 2.2)
    return (image * 255.0 + 0.5).astype(np.uint8)


# ---------------------------------------------------------------------------
# Output
# ---------------------------------------------------------------------------


def font(size, bold=False):
    candidates = [
        "/usr/share/fonts/truetype/dejavu/DejaVuSans%s.ttf" % ("-Bold" if bold else ""),
        "/usr/share/fonts/truetype/liberation/LiberationSans%s.ttf" % ("-Bold" if bold else "-Regular"),
    ]
    for path in candidates:
        if os.path.exists(path):
            try:
                return ImageFont.truetype(path, size)
            except OSError:
                pass
    return ImageFont.load_default()


def render_character(spec: Spec, width=760, height=1080, supersample=2):
    mesh = build_character(spec)

    w, h = width * supersample, height * supersample

    # Frame the whole figure with a little headroom, from a three-quarter view.
    centre = np.array([0.0, spec.height * 0.52, 0.0])
    distance = spec.height * 3.05
    azimuth, elevation = math.radians(34.0), math.radians(7.0)

    eye = centre + np.array([
        math.sin(azimuth) * math.cos(elevation) * distance,
        math.sin(elevation) * distance,
        math.cos(azimuth) * math.cos(elevation) * distance,
    ])

    eye_position, basis = look_at(eye, centre)

    zbuf, albedo, normal_buf, mat_buf = rasterise(mesh, w, h, eye_position, basis)
    colour, hit = shade(spec, zbuf, albedo, normal_buf, mat_buf, eye_position, basis, w, h)
    pixels = compose(colour, hit, w, h, spec)

    image = Image.fromarray(pixels, "RGB").resize((width, height), Image.LANCZOS)
    return image, len(mesh.tris)


def label(image: Image.Image, spec: Spec):
    draw = ImageDraw.Draw(image, "RGBA")
    width, height = image.size

    draw.rectangle([0, height - 132, width, height], fill=(6, 7, 9, 215))
    draw.line([(28, height - 132), (width - 28, height - 132)], fill=(60, 52, 40, 255), width=1)

    swatch = tuple(int(c * 255) for c in spec.eye_glow)
    draw.rectangle([28, height - 112, 34, height - 78], fill=swatch)

    draw.text((48, height - 114), spec.name.upper(), font=font(27, bold=True), fill=(236, 186, 96))
    draw.text((48, height - 80), spec.role, font=font(15), fill=(128, 128, 134))

    wrapped, line, limit = [], "", 74
    for word in spec.dossier.split():
        if len(line) + len(word) + 1 > limit:
            wrapped.append(line)
            line = word
        else:
            line = (line + " " + word).strip()
    wrapped.append(line)

    for i, text in enumerate(wrapped[:2]):
        draw.text((48, height - 56 + i * 20), text, font=font(14), fill=(158, 158, 164))

    return image


def contact_sheet(images, specs, cell=(420, 600)):
    pad, header = 20, 92
    width = pad + (cell[0] + pad) * len(images)
    height = header + cell[1] + pad + 54

    sheet = Image.new("RGB", (width, height), (9, 10, 13))
    draw = ImageDraw.Draw(sheet)

    draw.text((pad + 8, 24), "THE AWAKENING  ·  THE CAST",
              font=font(34, bold=True), fill=(236, 186, 96))
    draw.text((pad + 10, 64), "Generated by AnimatronicFactory. No imported models.",
              font=font(15), fill=(120, 120, 126))

    for i, (image, spec) in enumerate(zip(images, specs)):
        x = pad + (cell[0] + pad) * i
        sheet.paste(image.resize(cell, Image.LANCZOS), (x, header))

        draw.rectangle([x, header, x + cell[0], header + cell[1]], outline=(38, 38, 44))
        draw.text((x + 6, header + cell[1] + 10), spec.name.upper(),
                  font=font(19, bold=True), fill=(226, 226, 230))
        draw.text((x + 6, header + cell[1] + 34), spec.role,
                  font=font(13), fill=(120, 120, 126))

    return sheet


def main():
    parser = argparse.ArgumentParser(description="Render the generated cast to PNGs.")
    parser.add_argument("--out", default="renders", help="output directory")
    parser.add_argument("--width", type=int, default=760)
    parser.add_argument("--height", type=int, default=1080)
    parser.add_argument("--supersample", type=int, default=2)
    parser.add_argument("--only", default=None, help="render one character by id")
    args = parser.parse_args()

    os.makedirs(args.out, exist_ok=True)
    specs = [s for s in CAST if args.only is None or s.key == args.only]

    images = []
    for spec in specs:
        image, triangles = render_character(spec, args.width, args.height, args.supersample)
        image = label(image, spec)

        path = os.path.join(args.out, f"{spec.key}.png")
        image.save(path)
        images.append(image)

        print(f"{spec.name:<24} {triangles:>6} triangles  ->  {path}")

    if len(images) > 1:
        sheet = contact_sheet(images, specs)
        path = os.path.join(args.out, "cast.png")
        sheet.save(path)
        print(f"{'contact sheet':<24} {'':>6}             ->  {path}")


if __name__ == "__main__":
    main()
