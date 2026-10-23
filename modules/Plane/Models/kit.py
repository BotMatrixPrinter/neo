"""Shared parts for the plane model scripts (biplane.py, triplane.py): materials, simple
mesh builders, the propeller, empties and the glTF export. Imported by those scripts, which
Blender runs with --python; not run on its own.

Blender axes: +Y is the nose, +Z up, +X right (glTF and Godot: nose -Z, Y up).
"""

import math

import bmesh
import bpy
from mathutils import Matrix, Vector

scene = None
MATERIALS = {}
parts = []  # objects joined into the body at export


def begin(colors):
    """An empty scene with one material per color name (rough, flat colors)."""
    global scene
    bpy.ops.wm.read_factory_settings(use_empty=True)
    scene = bpy.context.scene
    MATERIALS.clear()
    parts.clear()
    for name, rgb in colors.items():
        mat = bpy.data.materials.new(name)
        bsdf = mat.node_tree.nodes.get("Principled BSDF")
        bsdf.inputs["Base Color"].default_value = (*rgb, 1.0)
        bsdf.inputs["Roughness"].default_value = 0.9
        MATERIALS[name] = mat


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


def propeller(location, material="Wood"):
    """The two-blade propeller "Propeller", unrotated at location, blades across its X/Z
    (glTF X/Y), thin along the shaft; not joined into the body."""
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
    prop = make_object("Propeller", bm, material, keep=True)
    prop.location = location
    return prop


def empty(name, location):
    """A marker the view uses (smoke start, where wing damage shows)."""
    obj = bpy.data.objects.new(name, None)
    obj.location = location
    scene.collection.objects.link(obj)
    return obj


def export(body_name, out):
    """Joins every part into one mesh body_name and writes everything to the .glb out."""
    bpy.ops.object.select_all(action="DESELECT")
    for obj in parts:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = parts[0]
    bpy.ops.object.join()
    body = bpy.context.view_layer.objects.active
    body.name = body_name
    body.data.name = body_name

    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.export_scene.gltf(filepath=out, export_format="GLB", use_selection=False,
                              export_apply=True, export_yup=True)
    print("NEO_EXPORTED", out, len(body.data.polygons), "faces")
