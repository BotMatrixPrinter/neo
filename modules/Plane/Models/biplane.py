"""The Allies' biplane, as code: a chunky cartoon WWI fighter (the player flies one).

Run from the repository root:
    blender -b --factory-startup --python modules/Plane/Models/biplane.py
It writes modules/Plane/Models/biplane.glb next to this script.

Blender axes here: +Y is the nose, +Z up, +X right. glTF (and Godot) turn that into
nose -Z, Y up. About 7 m span. Everything is joined into one mesh "Biplane" except the
propeller ("Propeller", spinning about its own Z in Godot), an empty "Exhaust" that
marks where the smoke trail starts and empties "LeftWing" / "RightWing" on top of the
upper wing, where wing damage shows. Shared parts come from kit.py.
"""

import math
import os
import sys

from mathutils import Matrix, Vector

HERE = os.path.dirname(os.path.abspath(__file__))
sys.dont_write_bytecode = True  # no __pycache__ next to the scripts
sys.path.insert(0, HERE)
import kit  # noqa: E402

OUT = os.path.join(HERE, "biplane.glb")

COLORS = {
    "Green": (0.16, 0.42, 0.14),
    "Red": (0.78, 0.10, 0.07),
    "Cream": (0.93, 0.86, 0.66),
    "Wood": (0.55, 0.31, 0.12),
    "Blue": (0.08, 0.22, 0.62),
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

# Fuselage: fat at the cockpit, tapering to the tail. (y, half width, half height, center z)
kit.loft("Fuselage", [
    (2.25, 0.72, 0.72, 0.15),
    (1.4, 0.78, 0.80, 0.15),
    (0.4, 0.74, 0.78, 0.15),
    (-0.8, 0.60, 0.64, 0.18),
    (-2.0, 0.40, 0.44, 0.25),
    (-3.0, 0.20, 0.26, 0.32),
    (-3.45, 0.06, 0.10, 0.36),
], "Green")

# Engine cowl and spinner, red.
kit.loft("Cowl", [
    (2.15, 0.80, 0.80, 0.15),
    (2.6, 0.84, 0.84, 0.15),
    (2.95, 0.74, 0.74, 0.15),
    (3.05, 0.55, 0.55, 0.15),
], "Red")
kit.sphere("Spinner", (0, 3.12, 0.15), 0.32, "Red", scale=(1, 1.35, 1))

# Exhaust stubs on both sides of the cowl.
for side in (-1, 1):
    kit.cylinder("ExhaustPipe", (side * 0.78, 2.35, -0.05), (side * 0.98, 2.0, -0.10), 0.09, "Metal")

# Wings: cream, rounded tips, upper one staggered forward.
UPPER_Z, LOWER_Z = 1.45, -0.55
kit.slab("UpperWing", kit.stadium(7.0, 1.35), 0.18, "Cream", Matrix.Translation((0, 0.55, UPPER_Z)))
kit.slab("LowerWing", kit.stadium(6.2, 1.2), 0.16, "Cream", Matrix.Translation((0, 0.05, LOWER_Z)))

# Roundels: blue, white, red, on top of the upper wing and under the lower wing.
for side in (-1, 1):
    for z, y, sign in ((UPPER_Z + 0.09, 0.55, 1), (LOWER_Z - 0.08, 0.05, -1)):
        x = side * 2.45
        kit.disc("RoundelBlue", (x, y, z + sign * 0.012), 0.52, "Blue", depth=0.02)
        kit.disc("RoundelWhite", (x, y, z + sign * 0.028), 0.34, "White", depth=0.02)
        kit.disc("RoundelRed", (x, y, z + sign * 0.044), 0.17, "Red", depth=0.02)

# Struts: interplane struts near the tips, cabane struts over the fuselage.
for side in (-1, 1):
    for dy in (0.4, -0.35):
        kit.cylinder("Strut", (side * 2.3, 0.05 + dy, LOWER_Z), (side * 2.4, 0.55 + dy, UPPER_Z), 0.05, "Wood")
        kit.cylinder("Cabane", (side * 0.42, 0.25 + dy, 0.85), (side * 0.6, 0.55 + dy, UPPER_Z), 0.045, "Wood")

# Tail: cream stabilizer and fin, a rudder striped blue, white, red (front to back).
kit.slab("Stabilizer", kit.stadium(2.5, 0.8), 0.1, "Cream", Matrix.Translation((0, -3.05, 0.36)))
# Outlines drawn in (y, z) and extruded across X.
SIDEWAYS = Matrix(((0, 0, 1, 0), (1, 0, 0, 0), (0, 1, 0, 0), (0, 0, 0, 1)))
fin = [(0.25, 0.0), (-0.75, 0.0), (-0.75, 0.88), (-0.45, 0.9), (-0.05, 0.4)]
kit.slab("Fin", fin, 0.09, "Cream", Matrix.Translation((0, -2.75, 0.4)) @ SIDEWAYS)
for material, y0, y1 in (("Blue", -3.5, -3.7), ("White", -3.7, -3.9), ("Red", -3.9, -4.05)):
    kit.slab("Rudder" + material, [(y0, 0.3), (y1, 0.3), (y1, 1.22), (y0, 1.3)], 0.08, material, SIDEWAYS)
# Rudder post joining the fin to the rudder.
kit.cylinder("RudderPost", (0, -3.5, 0.36), (0, -3.5, 1.3), 0.045, "Wood")

# Twin black guns on the cowl.
for side in (-1, 1):
    kit.cylinder("Gun", (side * 0.22, 0.7, 0.98), (side * 0.22, 2.55, 0.98), 0.07, "Black")
    kit.cylinder("GunMuzzle", (side * 0.22, 2.55, 0.98), (side * 0.22, 2.7, 0.98), 0.095, "Black")
    kit.cylinder("GunBody", (side * 0.22, 0.65, 0.97), (side * 0.22, 1.25, 0.97), 0.12, "Black")

# Landing gear: wooden V struts, a black axle, fat black wheels with red hubs.
AXLE = Vector((0, 1.35, -1.55))
for side in (-1, 1):
    hub = AXLE + Vector((side * 0.95, 0, 0))
    kit.cylinder("Gear", (side * 0.4, 1.8, -0.35), hub, 0.05, "Wood")
    kit.cylinder("Gear", (side * 0.4, 0.8, -0.35), hub, 0.05, "Wood")
    kit.torus("Wheel", hub, 0.28, 0.17, "Black", axis=(1, 0, 0))
    kit.disc("WheelFill", hub, 0.3, "Black", normal=(1, 0, 0), depth=0.18)
    kit.disc("Hub", hub + Vector((side * 0.1, 0, 0)), 0.14, "Red", normal=(1, 0, 0), depth=0.06)
kit.cylinder("Axle", AXLE + Vector((-0.95, 0, 0)), AXLE + Vector((0.95, 0, 0)), 0.045, "Black")

# Cockpit rim and the pilot: leather helmet, goggles, white scarf.
PILOT = Vector((0, -0.95, 1.3))
kit.torus("CockpitRim", (0, -0.95, 0.84), 0.42, 0.07, "Leather", scale=(1, 1.2, 1))
kit.sphere("Shoulders", PILOT + Vector((0, 0, -0.38)), 0.34, "Leather", scale=(1.15, 0.9, 0.7))
kit.sphere("Head", PILOT, 0.3, "Skin")
kit.sphere("Helmet", PILOT + Vector((0, -0.03, 0.06)), 0.315, "Leather", scale=(1, 1, 0.95))
kit.sphere("Nose", PILOT + Vector((0, 0.3, -0.06)), 0.08, "Skin")
for side in (-1, 1):
    eye = PILOT + Vector((side * 0.12, 0.27, 0.1))
    kit.torus("Goggle", eye, 0.085, 0.03, "Brass", axis=(0, 1, 0))
    kit.disc("GoggleGlass", eye, 0.09, "Glass", normal=(0, 1, 0), depth=0.03)
    kit.sphere("EarFlap", PILOT + Vector((side * 0.29, -0.02, -0.06)), 0.09, "Leather", scale=(0.6, 1, 1.2))
kit.torus("GoggleStrap", PILOT + Vector((0, 0, 0.1)), 0.31, 0.025, "Leather", scale=(1, 1, 1))
kit.torus("Scarf", PILOT + Vector((0, 0, -0.28)), 0.22, 0.09, "White")
for i in range(4):  # the scarf's tail, flapping back
    t = i / 3
    kit.sphere("ScarfTail", PILOT + Vector((0.2 + 0.12 * t, -0.25 - 0.32 * t, -0.28 + 0.05 * math.sin(t * 5))),
           0.085 - 0.012 * i, "White", scale=(0.5, 1.3, 0.8))

# ---------------------------------------------------------------- propeller, markers, export

kit.propeller((0, 3.22, 0.15))
# Smoke start: behind the exhaust stubs, on the plane's right.
kit.empty("Exhaust", (0.95, 1.9, -0.1))
# Wing damage shows here, on top of the upper wing, between the roundel and the fuselage.
kit.empty("LeftWing", (-1.6, 0.55, UPPER_Z + 0.1))
kit.empty("RightWing", (1.6, 0.55, UPPER_Z + 0.1))

kit.export("Biplane", OUT)
