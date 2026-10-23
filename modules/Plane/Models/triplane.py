"""The Central powers' triplane, as code: a chunky cartoon red WWI triplane, Red Baron style.

Run from the repository root:
    blender -b --factory-startup --python modules/Plane/Models/triplane.py
It writes modules/Plane/Models/triplane.glb next to this script.

Same scale and style as biplane.py: Blender axes +Y the nose, +Z up, +X right (glTF and
Godot: nose -Z, Y up), about 7 m span. Bright red, three stacked wings with black crosses
on white, a cream cowl ring, a little pilot. Everything is joined into one mesh "Triplane"
except the propeller ("Propeller", spinning about its own Z in Godot), an empty "Exhaust"
where the smoke trail starts and empties "LeftWing" / "RightWing" on top of the top wing,
where wing damage shows. Shared parts come from kit.py.
"""

import math
import os
import sys

from mathutils import Matrix, Vector

HERE = os.path.dirname(os.path.abspath(__file__))
sys.dont_write_bytecode = True  # no __pycache__ next to the scripts
sys.path.insert(0, HERE)
import kit  # noqa: E402

OUT = os.path.join(HERE, "triplane.glb")

COLORS = {
    "Red": (0.88, 0.05, 0.04),
    "DarkRed": (0.55, 0.04, 0.03),
    "Cream": (0.93, 0.86, 0.66),
    "Wood": (0.55, 0.31, 0.12),
    "White": (0.96, 0.96, 0.94),
    "Black": (0.05, 0.05, 0.06),
    "Skin": (0.95, 0.68, 0.52),
    "Leather": (0.40, 0.22, 0.10),
    "Brass": (0.85, 0.62, 0.20),
    "Glass": (0.55, 0.80, 0.90),
    "Metal": (0.35, 0.35, 0.38),
}
kit.begin(COLORS)

# ---------------------------------------------------------------- the plane

# Fuselage: short and fat, red, tapering to the tail. (y, half width, half height, center z)
kit.loft("Fuselage", [
    (2.1, 0.74, 0.74, 0.15),
    (1.3, 0.80, 0.82, 0.15),
    (0.3, 0.76, 0.80, 0.15),
    (-0.9, 0.60, 0.64, 0.2),
    (-2.0, 0.38, 0.42, 0.28),
    (-2.9, 0.18, 0.24, 0.34),
    (-3.3, 0.06, 0.10, 0.37),
], "Red")

# Rotary engine: a fat cream cowl ring, open at the front, the dark engine face inside.
kit.loft("Cowl", [
    (2.0, 0.80, 0.80, 0.15),
    (2.45, 0.88, 0.88, 0.15),
    (2.8, 0.82, 0.82, 0.15),
], "Cream", cap=False)
kit.torus("CowlRing", (0, 2.8, 0.15), 0.72, 0.12, "Cream", axis=(0, 1, 0))
kit.disc("EngineFace", (0, 2.7, 0.15), 0.74, "Black", normal=(0, 1, 0), depth=0.06)
for i in range(9):  # the cylinders of the rotary, peeking out of the ring
    a = 2 * math.pi * i / 9
    kit.sphere("Cylinder", (0.5 * math.cos(a), 2.74, 0.15 + 0.5 * math.sin(a)), 0.14, "Metal",
               scale=(1, 0.6, 1))
kit.cylinder("Shaft", (0, 2.6, 0.15), (0, 3.0, 0.15), 0.12, "Metal")

# Three stacked wings, red, rounded tips, shrinking toward the bottom; a little one on the axle.
TOP_Z, MID_Z, LOW_Z = 1.55, 0.55, -0.45
kit.slab("TopWing", kit.stadium(7.0, 1.1), 0.17, "Red", Matrix.Translation((0, 0.65, TOP_Z)))
kit.slab("MidWing", kit.stadium(6.4, 1.05), 0.16, "Red", Matrix.Translation((0, 0.45, MID_Z)))
kit.slab("LowWing", kit.stadium(5.8, 1.0), 0.16, "Red", Matrix.Translation((0, 0.3, LOW_Z)))


def cross(name, center, size, normal, up):
    """A black cross on a white square, flat on a surface facing normal (up: the cross's up)."""
    n, u = Vector(normal).normalized(), Vector(up).normalized()
    r = u.cross(n)
    basis = Matrix((r, u, n)).transposed().to_4x4()
    at = Matrix.Translation(center) @ basis
    half = size / 2
    square = [(-half, -half), (half, -half), (half, half), (-half, half)]
    kit.slab(name + "White", square, 0.02, "White", at)
    bar = size * 0.17
    long_bar = size * 0.42
    for points in ([(-bar, -long_bar), (bar, -long_bar), (bar, long_bar), (-bar, long_bar)],
                   [(-long_bar, -bar), (long_bar, -bar), (long_bar, bar), (-long_bar, bar)]):
        kit.slab(name + "Black", points, 0.02, "Black", at @ Matrix.Translation((0, 0, 0.018)))


for side in (-1, 1):
    cross("TopCross", (side * 2.3, 0.65, TOP_Z + 0.095), 0.95, (0, 0, 1), (0, 1, 0))
    cross("LowCross", (side * 1.9, 0.3, LOW_Z - 0.09), 0.85, (0, 0, -1), (0, 1, 0))
    cross("SideCross", (side * 0.43, -2.1, 0.3), 0.6, (side, 0, 0.0), (0, 0, 1))

# Struts: one wooden I-strut per side through all three wings, cabane struts to the top wing.
for side in (-1, 1):
    kit.cylinder("Strut", (side * 2.25, 0.3, LOW_Z), (side * 2.3, 0.65, TOP_Z), 0.07, "Wood")
    for dy in (0.35, -0.3):
        kit.cylinder("Cabane", (side * 0.4, 0.3 + dy, 0.85), (side * 0.6, 0.65 + dy, TOP_Z), 0.045, "Wood")

# Tail: a red triangular stabilizer, a rounded white rudder with a black cross, no fin.
kit.slab("Stabilizer", [(-1.3, -0.35), (-0.15, 0.3), (0.15, 0.3), (1.3, -0.35), (1.1, -0.6), (-1.1, -0.6)],
         0.1, "Red", Matrix.Translation((0, -2.85, 0.37)))
SIDEWAYS = Matrix(((0, 0, 1, 0), (1, 0, 0, 0), (0, 1, 0, 0), (0, 0, 0, 1)))
rudder = [(-3.2, 0.2), (-3.2, 1.25)]
for i in range(9):  # round back edge, top to bottom
    a = math.pi / 2 - math.pi * i / 8
    rudder.append((-3.55 + 0.45 * math.cos(a) * 0.9, 0.72 + 0.53 * math.sin(a)))
kit.slab("Rudder", rudder, 0.08, "White", SIDEWAYS)
for side in (-1, 1):
    cross("RudderCross", (side * 0.05, -3.55, 0.75), 0.55, (side, 0, 0), (0, 0, 1))
kit.cylinder("RudderPost", (0, -3.2, 0.3), (0, -3.2, 1.25), 0.045, "Wood")

# Twin black guns on the nose.
for side in (-1, 1):
    kit.cylinder("Gun", (side * 0.22, 0.6, 0.98), (side * 0.22, 2.4, 0.98), 0.07, "Black")
    kit.cylinder("GunMuzzle", (side * 0.22, 2.4, 0.98), (side * 0.22, 2.55, 0.98), 0.095, "Black")
    kit.cylinder("GunBody", (side * 0.22, 0.55, 0.97), (side * 0.22, 1.15, 0.97), 0.12, "Black")

# Landing gear: wooden V struts, a little red axle wing, fat black wheels with cream hubs.
AXLE = Vector((0, 1.3, -1.5))
for side in (-1, 1):
    hub = AXLE + Vector((side * 0.95, 0, 0))
    kit.cylinder("Gear", (side * 0.4, 1.75, -0.35), hub, 0.05, "Wood")
    kit.cylinder("Gear", (side * 0.4, 0.75, -0.35), hub, 0.05, "Wood")
    kit.torus("Wheel", hub, 0.28, 0.17, "Black", axis=(1, 0, 0))
    kit.disc("WheelFill", hub, 0.3, "Black", normal=(1, 0, 0), depth=0.18)
    kit.disc("Hub", hub + Vector((side * 0.1, 0, 0)), 0.14, "Cream", normal=(1, 0, 0), depth=0.06)
kit.slab("AxleWing", kit.stadium(1.75, 0.45), 0.12, "Red", Matrix.Translation(AXLE + Vector((0, -0.08, 0))))

# Cockpit rim and the pilot: leather helmet, goggles, white scarf.
PILOT = Vector((0, -1.0, 1.3))
kit.torus("CockpitRim", (0, -1.0, 0.86), 0.42, 0.07, "Leather", scale=(1, 1.2, 1))
kit.sphere("Shoulders", PILOT + Vector((0, 0, -0.38)), 0.34, "Leather", scale=(1.15, 0.9, 0.7))
kit.sphere("Head", PILOT, 0.3, "Skin")
kit.sphere("Helmet", PILOT + Vector((0, -0.03, 0.06)), 0.315, "Leather", scale=(1, 1, 0.95))
kit.sphere("Nose", PILOT + Vector((0, 0.3, -0.06)), 0.08, "Skin")
for side in (-1, 1):
    eye = PILOT + Vector((side * 0.12, 0.27, 0.1))
    kit.torus("Goggle", eye, 0.085, 0.03, "Brass", axis=(0, 1, 0))
    kit.disc("GoggleGlass", eye, 0.09, "Glass", normal=(0, 1, 0), depth=0.03)
    kit.sphere("EarFlap", PILOT + Vector((side * 0.29, -0.02, -0.06)), 0.09, "Leather", scale=(0.6, 1, 1.2))
kit.torus("GoggleStrap", PILOT + Vector((0, 0, 0.1)), 0.31, 0.025, "Leather")
kit.torus("Scarf", PILOT + Vector((0, 0, -0.28)), 0.22, 0.09, "White")
for i in range(4):  # the scarf's tail, flapping back
    t = i / 3
    kit.sphere("ScarfTail", PILOT + Vector((-0.2 - 0.12 * t, -0.25 - 0.32 * t, -0.28 + 0.05 * math.sin(t * 5))),
               0.085 - 0.012 * i, "White", scale=(0.5, 1.3, 0.8))

# ---------------------------------------------------------------- propeller, markers, export

kit.propeller((0, 3.12, 0.15))
# Smoke start: under the cowl ring, where a rotary throws its oily exhaust.
kit.empty("Exhaust", (0.0, 2.3, -0.8))
# Wing damage shows here, on top of the top wing, inside the crosses.
kit.empty("LeftWing", (-1.4, 0.65, TOP_Z + 0.1))
kit.empty("RightWing", (1.4, 0.65, TOP_Z + 0.1))

kit.export("Triplane", OUT)
