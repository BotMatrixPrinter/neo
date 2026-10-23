"""The player's biplane, as code: a chunky cartoon WWI fighter.

Run from the repository root:
    blender -b --factory-startup --python modules/Plane/Models/biplane.py
It writes modules/Plane/Models/biplane.glb next to this script.

Blender axes here: +Y is the nose, +Z up, +X right. glTF (and Godot) turn that into
nose -Z, Y up. About 7 m span. Everything is joined into one mesh "Biplane" except the
propeller ("Propeller", spinning about its own Z in Godot) and an empty "Exhaust" that
marks where the smoke trail starts.
"""

import math
import os

import bmesh
import bpy
from mathutils import Matrix, Vector

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "biplane.glb")

# ---------------------------------------------------------------- scene and materials

bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene

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
MATERIALS = {}
for name, rgb in COLORS.items():
    mat = bpy.data.materials.new(name)
    bsdf = mat.node_tree.nodes.get("Principled BSDF")
    bsdf.inputs["Base Color"].default_value = (*rgb, 1.0)
    bsdf.inputs["Roughness"].default_value = 0.9
    MATERIALS[name] = mat

parts = []


def make_object(name, bm, material, keep=False):
    mesh = bpy.data.meshes.new(name)
    bm.normal_update()
    bm.to_mesh(mesh)
    bm.free()
    for poly in mesh.polygons:
        poly.use_smooth = True
    mesh.materials.append(MATERIALS[material])
    obj = bpy.data.objects.new(name, mesh)
    scene.collection.objects.link(obj)
    if not keep:
        parts.append(obj)
    return obj


def transform(bm, matrix):
    bmesh.ops.transform(bm, matrix=matrix, verts=bm.verts)


def loft(name, rings, material, segments=24, cap=True):
    """A body along Y from rings of (y, half width x, half height z, center z)."""
    bm = bmesh.new()
    loops = []
    for y, rx, rz, cz in rings:
        loop = []
        for s in range(segments):
            a = 2 * math.pi * s / segments
            loop.append(bm.verts.new((rx * math.cos(a), y, cz + rz * math.sin(a))))
        loops.append(loop)
    for a, b in zip(loops, loops[1:]):
        for s in range(segments):
            t = (s + 1) % segments
            bm.faces.new((a[s], a[t], b[t], b[s]))
    if cap:
        bm.faces.new(list(reversed(loops[0])))
        bm.faces.new(loops[-1])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return make_object(name, bm, material)


def sphere(name, center, radius, material, scale=(1, 1, 1)):
    bm = bmesh.new()
    bmesh.ops.create_uvsphere(bm, u_segments=20, v_segments=12, radius=radius)
    transform(bm, Matrix.Translation(center) @ Matrix.Diagonal((*scale, 1)))
    return make_object(name, bm, material)


def cylinder(name, a, b, radius, material, segments=12, radius2=None):
    """A cylinder (or cone) from point a to point b."""
    a, b = Vector(a), Vector(b)
    axis = b - a
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=True, segments=segments, radius1=radius,
                          radius2=radius if radius2 is None else radius2, depth=axis.length)
    rot = Vector((0, 0, 1)).rotation_difference(axis.normalized()).to_matrix().to_4x4()
    transform(bm, Matrix.Translation((a + b) / 2) @ rot)
    return make_object(name, bm, material)


def torus(name, center, major, minor, material, axis=(0, 0, 1), scale=(1, 1, 1)):
    bm = bmesh.new()
    seg_major, seg_minor = 24, 10
    rings = []
    for i in range(seg_major):
        u = 2 * math.pi * i / seg_major
        ring = []
        for j in range(seg_minor):
            v = 2 * math.pi * j / seg_minor
            r = major + minor * math.cos(v)
            ring.append(bm.verts.new((r * math.cos(u), r * math.sin(u), minor * math.sin(v))))
        rings.append(ring)
    for i in range(seg_major):
        a, b = rings[i], rings[(i + 1) % seg_major]
        for j in range(seg_minor):
            k = (j + 1) % seg_minor
            bm.faces.new((a[j], b[j], b[k], a[k]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    rot = Vector((0, 0, 1)).rotation_difference(Vector(axis).normalized()).to_matrix().to_4x4()
    transform(bm, Matrix.Translation(center) @ rot @ Matrix.Diagonal((*scale, 1)))
    return make_object(name, bm, material)


def slab(name, outline, thickness, material, matrix):
    """An outline in the XY plane, extruded up by thickness and rounded a little."""
    bm = bmesh.new()
    verts = [bm.verts.new((x, y, -thickness / 2)) for x, y in outline]
    face = bm.faces.new(verts)
    ext = bmesh.ops.extrude_face_region(bm, geom=[face])
    top = [v for v in ext["geom"] if isinstance(v, bmesh.types.BMVert)]
    bmesh.ops.translate(bm, vec=(0, 0, thickness), verts=top)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bmesh.ops.bevel(bm, geom=[e for e in bm.edges if not e.is_wire], offset=thickness * 0.35,
                    segments=2, affect="EDGES", profile=0.5)
    bmesh.ops.triangulate(bm, faces=bm.faces)
    transform(bm, matrix)
    return make_object(name, bm, material)


def stadium(span, chord, tip_steps=10):
    """Plan of a wing: a rectangle span x chord with fully rounded tips (X across, Y along)."""
    r = chord / 2
    half = span / 2 - r
    points = []
    for i in range(tip_steps + 1):  # right tip, front to back
        a = math.pi / 2 - math.pi * i / tip_steps
        points.append((half + r * math.cos(a), r * math.sin(a)))
    for i in range(tip_steps + 1):  # left tip, back to front
        a = -math.pi / 2 - math.pi * i / tip_steps
        points.append((-half + r * math.cos(a), r * math.sin(a)))
    return points


def disc(name, center, radius, material, normal=(0, 0, 1), depth=0.02):
    n = Vector(normal).normalized()
    c = Vector(center)
    return cylinder(name, c - n * depth / 2, c + n * depth / 2, radius, material, segments=24)


# ---------------------------------------------------------------- the plane

# Fuselage: fat at the cockpit, tapering to the tail. (y, half width, half height, center z)
loft("Fuselage", [
    (2.25, 0.72, 0.72, 0.15),
    (1.4, 0.78, 0.80, 0.15),
    (0.4, 0.74, 0.78, 0.15),
    (-0.8, 0.60, 0.64, 0.18),
    (-2.0, 0.40, 0.44, 0.25),
    (-3.0, 0.20, 0.26, 0.32),
    (-3.45, 0.06, 0.10, 0.36),
], "Green")

# Engine cowl and spinner, red.
loft("Cowl", [
    (2.15, 0.80, 0.80, 0.15),
    (2.6, 0.84, 0.84, 0.15),
    (2.95, 0.74, 0.74, 0.15),
    (3.05, 0.55, 0.55, 0.15),
], "Red")
sphere("Spinner", (0, 3.12, 0.15), 0.32, "Red", scale=(1, 1.35, 1))

# Exhaust stubs on both sides of the cowl.
for side in (-1, 1):
    cylinder("ExhaustPipe", (side * 0.78, 2.35, -0.05), (side * 0.98, 2.0, -0.10), 0.09, "Metal")

# Wings: cream, rounded tips, upper one staggered forward.
UPPER_Z, LOWER_Z = 1.45, -0.55
slab("UpperWing", stadium(7.0, 1.35), 0.18, "Cream", Matrix.Translation((0, 0.55, UPPER_Z)))
slab("LowerWing", stadium(6.2, 1.2), 0.16, "Cream", Matrix.Translation((0, 0.05, LOWER_Z)))

# Roundels: blue, white, red, on top of the upper wing and under the lower wing.
for side in (-1, 1):
    for z, y, sign in ((UPPER_Z + 0.09, 0.55, 1), (LOWER_Z - 0.08, 0.05, -1)):
        x = side * 2.45
        disc("RoundelBlue", (x, y, z + sign * 0.012), 0.52, "Blue", depth=0.02)
        disc("RoundelWhite", (x, y, z + sign * 0.028), 0.34, "White", depth=0.02)
        disc("RoundelRed", (x, y, z + sign * 0.044), 0.17, "Red", depth=0.02)

# Struts: interplane struts near the tips, cabane struts over the fuselage.
for side in (-1, 1):
    for dy in (0.4, -0.35):
        cylinder("Strut", (side * 2.3, 0.05 + dy, LOWER_Z), (side * 2.4, 0.55 + dy, UPPER_Z), 0.05, "Wood")
        cylinder("Cabane", (side * 0.42, 0.25 + dy, 0.85), (side * 0.6, 0.55 + dy, UPPER_Z), 0.045, "Wood")

# Tail: cream stabilizer and fin, a rudder striped blue, white, red (front to back).
slab("Stabilizer", stadium(2.5, 0.8), 0.1, "Cream", Matrix.Translation((0, -3.05, 0.36)))
# Outlines drawn in (y, z) and extruded across X.
SIDEWAYS = Matrix(((0, 0, 1, 0), (1, 0, 0, 0), (0, 1, 0, 0), (0, 0, 0, 1)))
fin = [(0.25, 0.0), (-0.75, 0.0), (-0.75, 0.88), (-0.45, 0.9), (-0.05, 0.4)]
slab("Fin", fin, 0.09, "Cream", Matrix.Translation((0, -2.75, 0.4)) @ SIDEWAYS)
for material, y0, y1 in (("Blue", -3.5, -3.7), ("White", -3.7, -3.9), ("Red", -3.9, -4.05)):
    slab("Rudder" + material, [(y0, 0.3), (y1, 0.3), (y1, 1.22), (y0, 1.3)], 0.08, material, SIDEWAYS)
# Rudder post joining the fin to the rudder.
cylinder("RudderPost", (0, -3.5, 0.36), (0, -3.5, 1.3), 0.045, "Wood")

# Twin black guns on the cowl.
for side in (-1, 1):
    cylinder("Gun", (side * 0.22, 0.7, 0.98), (side * 0.22, 2.55, 0.98), 0.07, "Black")
    cylinder("GunMuzzle", (side * 0.22, 2.55, 0.98), (side * 0.22, 2.7, 0.98), 0.095, "Black")
    cylinder("GunBody", (side * 0.22, 0.65, 0.97), (side * 0.22, 1.25, 0.97), 0.12, "Black")

# Landing gear: wooden V struts, a black axle, fat black wheels with red hubs.
AXLE = Vector((0, 1.35, -1.55))
for side in (-1, 1):
    hub = AXLE + Vector((side * 0.95, 0, 0))
    cylinder("Gear", (side * 0.4, 1.8, -0.35), hub, 0.05, "Wood")
    cylinder("Gear", (side * 0.4, 0.8, -0.35), hub, 0.05, "Wood")
    torus("Wheel", hub, 0.28, 0.17, "Black", axis=(1, 0, 0))
    disc("WheelFill", hub, 0.3, "Black", normal=(1, 0, 0), depth=0.18)
    disc("Hub", hub + Vector((side * 0.1, 0, 0)), 0.14, "Red", normal=(1, 0, 0), depth=0.06)
cylinder("Axle", AXLE + Vector((-0.95, 0, 0)), AXLE + Vector((0.95, 0, 0)), 0.045, "Black")

# Cockpit rim and the pilot: leather helmet, goggles, white scarf.
PILOT = Vector((0, -0.95, 1.3))
torus("CockpitRim", (0, -0.95, 0.84), 0.42, 0.07, "Leather", scale=(1, 1.2, 1))
sphere("Shoulders", PILOT + Vector((0, 0, -0.38)), 0.34, "Leather", scale=(1.15, 0.9, 0.7))
sphere("Head", PILOT, 0.3, "Skin")
sphere("Helmet", PILOT + Vector((0, -0.03, 0.06)), 0.315, "Leather", scale=(1, 1, 0.95))
sphere("Nose", PILOT + Vector((0, 0.3, -0.06)), 0.08, "Skin")
for side in (-1, 1):
    eye = PILOT + Vector((side * 0.12, 0.27, 0.1))
    torus("Goggle", eye, 0.085, 0.03, "Brass", axis=(0, 1, 0))
    disc("GoggleGlass", eye, 0.09, "Glass", normal=(0, 1, 0), depth=0.03)
    sphere("EarFlap", PILOT + Vector((side * 0.29, -0.02, -0.06)), 0.09, "Leather", scale=(0.6, 1, 1.2))
torus("GoggleStrap", PILOT + Vector((0, 0, 0.1)), 0.31, 0.025, "Leather", scale=(1, 1, 1))
torus("Scarf", PILOT + Vector((0, 0, -0.28)), 0.22, 0.09, "White")
for i in range(4):  # the scarf's tail, flapping back
    t = i / 3
    sphere("ScarfTail", PILOT + Vector((0.2 + 0.12 * t, -0.25 - 0.32 * t, -0.28 + 0.05 * math.sin(t * 5))),
           0.085 - 0.012 * i, "White", scale=(0.5, 1.3, 0.8))

# ---------------------------------------------------------------- propeller

bm = bmesh.new()
for sign in (-1, 1):
    blade = bmesh.new()
    bmesh.ops.create_cube(blade, size=1.0)
    for v in blade.verts:  # taper toward the tip, round it a bit
        t = (v.co.z + 0.5)
        v.co.x *= 0.22 * (1.0 - 0.35 * t)
        v.co.y *= 0.07
        v.co.z = 0.15 + 1.25 * t
    bmesh.ops.bevel(blade, geom=list(blade.edges), offset=0.025, segments=2, affect="EDGES")
    for v in blade.verts:  # a little twist
        a = math.radians(18) * (1 - v.co.z / 1.4)
        x, y = v.co.x, v.co.y
        v.co.x, v.co.y = x * math.cos(a) - y * math.sin(a), x * math.sin(a) + y * math.cos(a)
    if sign < 0:
        transform(blade, Matrix.Rotation(math.pi, 4, "Y"))
    data = bpy.data.meshes.new("blade")
    blade.to_mesh(data)
    blade.free()
    bm.from_mesh(data)
bmesh.ops.create_cone(bm, cap_ends=True, segments=12, radius1=0.16, radius2=0.16, depth=0.14,
                      matrix=Matrix.Rotation(math.radians(90), 4, "X"))
propeller = make_object("Propeller", bm, "Wood", keep=True)
propeller.location = (0, 3.22, 0.15)

# Smoke start: behind the exhaust stubs, on the plane's right.
exhaust = bpy.data.objects.new("Exhaust", None)
exhaust.location = (0.95, 1.9, -0.1)
scene.collection.objects.link(exhaust)

# ---------------------------------------------------------------- join and export

bpy.ops.object.select_all(action="DESELECT")
for obj in parts:
    obj.select_set(True)
bpy.context.view_layer.objects.active = parts[0]
bpy.ops.object.join()
body = bpy.context.view_layer.objects.active
body.name = "Biplane"
body.data.name = "Biplane"

bpy.ops.object.select_all(action="SELECT")
bpy.ops.export_scene.gltf(filepath=OUT, export_format="GLB", use_selection=False,
                          export_apply=True, export_yup=True)
print("NEO_EXPORTED", OUT, len(body.data.polygons), "faces")
