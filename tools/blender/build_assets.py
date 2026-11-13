"""Procedural asset builder for Ember & Blossom.

Run headless (from the project root):
    blender -b --factory-startup --python tools/blender/build_assets.py -- --out assets/models [--preview DIR] [--only rocks,tree]

Each builder models one asset in a fresh scene, exports it as .glb (Y-up) and optionally
renders a quick Workbench preview PNG. Material names matter: Godot swaps them for the
stylized shaders by name (Rock, Bark, Blossom, Wood, ...).

Axis convention: Blender +Z up, Blender -Y = Godot +Z ("back"/"out"), Blender +Y = Godot -Z ("forward").
"""
import math
import os
import random
import sys

import bmesh
import bpy
from mathutils import Matrix, Vector

# ----------------------------------------------------------------------------- args

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []


def arg(name, default=None):
    if name in argv:
        i = argv.index(name)
        return argv[i + 1] if i + 1 < len(argv) else default
    return default


OUT = os.path.abspath(arg("--out", "assets/models"))
PREVIEW = arg("--preview")
ONLY = set((arg("--only") or "").split(",")) - {""}
os.makedirs(OUT, exist_ok=True)

# ----------------------------------------------------------------------------- palette

PALETTE = {
    "Rock": (0.11, 0.10, 0.14),
    "RockTop": (0.22, 0.21, 0.28),
    "Bark": (0.13, 0.08, 0.09),
    "Blossom": (1.0, 0.55, 0.75),
    "Wood": (0.42, 0.27, 0.17),
    "WoodDark": (0.24, 0.15, 0.10),
    "Roof": (0.20, 0.13, 0.12),
    "Stone": (0.32, 0.32, 0.38),
    "WindowGlow": (1.0, 0.65, 0.35),
    "PineNeedles": (0.13, 0.30, 0.28),
    "Skin": (0.93, 0.72, 0.60),
    "Shirt": (0.86, 0.82, 0.74),
    "Vest": (0.26, 0.15, 0.10),
    "Pants": (0.19, 0.19, 0.25),
    "Boots": (0.30, 0.18, 0.11),
    "Hair": (0.14, 0.08, 0.06),
    "Scarf": (0.62, 0.18, 0.15),
    "Leather": (0.40, 0.25, 0.14),
    "Metal": (0.75, 0.62, 0.35),
    "Eyes": (0.05, 0.04, 0.06),
}
EMISSIVE = {"Blossom": 0.6, "WindowGlow": 3.0}

_materials = {}


def mat(name):
    if name in _materials:
        return _materials[name]
    m = bpy.data.materials.new(name)
    try:
        m.use_nodes = True
    except Exception:
        pass
    col = PALETTE.get(name, (0.8, 0.8, 0.8))
    bsdf = m.node_tree.nodes.get("Principled BSDF")
    if bsdf:
        bsdf.inputs["Base Color"].default_value = (*col, 1.0)
        bsdf.inputs["Roughness"].default_value = 0.95
        if name in EMISSIVE:
            bsdf.inputs["Emission Color"].default_value = (*col, 1.0)
            bsdf.inputs["Emission Strength"].default_value = EMISSIVE[name]
    m.diffuse_color = (*col, 1.0)  # Workbench preview colour
    _materials[name] = m
    return m


# ----------------------------------------------------------------------------- scene helpers

def reset_scene():
    if bpy.app.background:
        bpy.ops.wm.read_factory_settings(use_empty=True)
    else:
        # Live Blender (MCP): a factory reset would unload the MCP add-on and drop the
        # connection, so just clear the scene's objects and data instead.
        for ob in list(bpy.data.objects):
            bpy.data.objects.remove(ob, do_unlink=True)
        for coll in (bpy.data.meshes, bpy.data.curves, bpy.data.materials, bpy.data.cameras,
                     bpy.data.lights, bpy.data.armatures):
            for block in list(coll):
                if block.users == 0:
                    coll.remove(block)
    _materials.clear()


def obj_from_bmesh(name, bm, material=None, smooth=False, parent=None, location=(0, 0, 0)):
    me = bpy.data.meshes.new(name)
    # Material slots must exist *before* writing, or per-face indices get clamped to 0.
    for n in ([material] if isinstance(material, str) else (material or [])):
        me.materials.append(mat(n))
    bm.to_mesh(me)
    bm.free()
    for p in me.polygons:
        p.use_smooth = smooth
    ob = bpy.data.objects.new(name, me)
    ob.location = location
    bpy.context.scene.collection.objects.link(ob)
    if parent:
        ob.parent = parent
    return ob


def add_box(bm, size, center=(0, 0, 0), rot=(0, 0, 0), bevel=0.0, segments=1, mat_index=0):
    """Add a (bevelled) box into an existing bmesh."""
    tmp = bmesh.new()
    bmesh.ops.create_cube(tmp, size=1.0)
    bmesh.ops.scale(tmp, vec=Vector(size), verts=tmp.verts)
    if bevel > 0:
        bmesh.ops.bevel(tmp, geom=list(tmp.edges), offset=bevel, segments=segments,
                        affect="EDGES", profile=0.5)
    m = Matrix.Translation(Vector(center)) @ euler_matrix(rot)
    bmesh.ops.transform(tmp, matrix=m, verts=tmp.verts)
    merge_into(bm, tmp, mat_index)


def add_cone(bm, r1, r2, depth, segs, center, rot=(0, 0, 0), mat_index=0, jitter=0.0, rng=None):
    tmp = bmesh.new()
    bmesh.ops.create_cone(tmp, cap_ends=True, cap_tris=False, segments=segs,
                          radius1=r1, radius2=r2, depth=depth)
    if jitter and rng:
        for v in tmp.verts:
            if v.co.z < 0:  # only the base ring
                k = 1 + rng.uniform(-jitter, jitter)
                v.co.x *= k
                v.co.y *= k
                v.co.z += rng.uniform(-jitter, jitter) * depth * 0.6
    m = Matrix.Translation(Vector(center)) @ euler_matrix(rot)
    bmesh.ops.transform(tmp, matrix=m, verts=tmp.verts)
    merge_into(bm, tmp, mat_index)


def merge_into(bm, tmp, mat_index):
    """Append tmp's geometry into bm with the given material slot index."""
    vmap = {}
    for v in tmp.verts:
        vmap[v] = bm.verts.new(v.co)
    for f in tmp.faces:
        try:
            nf = bm.faces.new([vmap[v] for v in f.verts])
            nf.material_index = mat_index
        except ValueError:
            pass  # duplicate face
    tmp.free()


def euler_matrix(rot):
    from mathutils import Euler
    return Euler(rot, "XYZ").to_matrix().to_4x4()


def multi_material(ob, names):
    """Assign slots to an object that has none yet (e.g. converted curves)."""
    if len(ob.data.materials) == 0:
        for n in names:
            ob.data.materials.append(mat(n))


def hull_rock(rng, n_points, scale, flatten_bottom=None, wobble=0.18):
    """Random points on a squashed ellipsoid -> convex hull = chunky chiselled facets."""
    bm = bmesh.new()
    for _ in range(n_points):
        v = Vector((rng.gauss(0, 1), rng.gauss(0, 1), rng.gauss(0, 1))).normalized()
        v *= 0.5 * (1 + rng.uniform(-wobble, wobble))
        v = Vector((v.x * scale[0], v.y * scale[1], v.z * scale[2]))
        if flatten_bottom is not None and v.z < flatten_bottom:
            v.z = flatten_bottom
        bm.verts.new(v)
    bmesh.ops.convex_hull(bm, input=bm.verts)
    # Drop interior leftovers that convex_hull can leave unconnected.
    loose = [v for v in bm.verts if not v.link_faces]
    bmesh.ops.delete(bm, geom=loose, context="VERTS")
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return bm


def export(name):
    path = os.path.join(OUT, f"{name}.glb")
    bpy.ops.export_scene.gltf(filepath=path, export_format="GLB", export_yup=True,
                              export_apply=True, use_selection=False)
    print(f"[assets] exported {path}")
    if PREVIEW:
        render_preview(name)


def render_preview(name):
    scene = bpy.context.scene
    objs = [o for o in scene.objects if o.type == "MESH"]
    if not objs:
        return
    lo = Vector((1e9, 1e9, 1e9))
    hi = Vector((-1e9, -1e9, -1e9))
    for o in objs:
        for c in o.bound_box:
            w = o.matrix_world @ Vector(c)
            lo = Vector(map(min, lo, w))
            hi = Vector(map(max, hi, w))
    center = (lo + hi) / 2
    radius = max((hi - lo).length / 2, 0.1)

    cam_data = bpy.data.cameras.new("PreviewCam")
    cam_data.lens = 50
    cam = bpy.data.objects.new("PreviewCam", cam_data)
    scene.collection.objects.link(cam)
    direction = Vector((0.9, 1.4, 0.55)).normalized()  # from front-right, slightly above
    cam.location = center + direction * radius * 3.0
    cam.rotation_euler = (center - cam.location).to_track_quat("-Z", "Y").to_euler()
    scene.camera = cam

    scene.render.engine = "BLENDER_WORKBENCH"
    scene.display.shading.light = "STUDIO"
    scene.display.shading.color_type = "MATERIAL"
    scene.display.shading.show_cavity = True
    scene.display.shading.show_shadows = True
    scene.render.resolution_x = 640
    scene.render.resolution_y = 640
    scene.render.film_transparent = False
    world = bpy.data.worlds.new("PreviewWorld")
    world.color = (0.12, 0.1, 0.2)
    scene.world = world
    os.makedirs(PREVIEW, exist_ok=True)
    scene.render.filepath = os.path.join(os.path.abspath(PREVIEW), f"{name}.png")
    bpy.ops.render.render(write_still=True)
    bpy.data.objects.remove(cam)


# ============================================================================= ROCKS

def build_rocks():
    """6 boulder variants + 3 tall cliff columns, each ~1 m across (scaled in-game)."""
    variants = [
        ("rock_boulder_01", 14, (1.0, 0.85, 0.7), -0.22),
        ("rock_boulder_02", 18, (1.0, 1.0, 0.8), -0.25),
        ("rock_boulder_03", 11, (1.0, 0.7, 0.9), -0.2),
        ("rock_slab_01", 16, (1.0, 0.9, 0.45), -0.12),
        ("rock_slab_02", 20, (1.0, 0.75, 0.5), -0.15),
        ("rock_chunk_01", 24, (0.9, 0.9, 1.0), None),
        ("rock_column_01", 30, (0.45, 0.42, 1.0), None),
        ("rock_column_02", 26, (0.5, 0.38, 1.0), None),
        ("rock_column_03", 34, (0.42, 0.5, 1.0), None),
    ]
    for i, (name, n, scale, flat) in enumerate(variants):
        reset_scene()
        rng = random.Random(1000 + i * 17)
        bm = hull_rock(rng, n, scale, flatten_bottom=flat)
        # Normalise to a 1 m bounding box so the game can scale by "size".
        xs = [v.co for v in bm.verts]
        lo = Vector((min(c.x for c in xs), min(c.y for c in xs), min(c.z for c in xs)))
        hi = Vector((max(c.x for c in xs), max(c.y for c in xs), max(c.z for c in xs)))
        mid = (lo + hi) / 2
        size = hi - lo
        for v in bm.verts:
            c = v.co - mid
            v.co = Vector((c.x / size.x, c.y / size.y, c.z / size.z))
        ob = obj_from_bmesh(name, bm, "Rock", smooth=False)
        multi_material(ob, ["Rock"])
        export(name)


def shard_bmesh(rng, height, base_r, lean, twist_sides=5):
    """One obsidian blade: an irregular, tapered prism with a chiselled, off-centre tip."""
    bm = bmesh.new()
    sides = twist_sides + rng.randint(0, 2)
    rings = [(0.0, 1.0), (0.35, rng.uniform(0.7, 0.85)), (0.7, rng.uniform(0.38, 0.55))]
    radii = [base_r * rng.uniform(0.7, 1.25) for _ in range(sides)]
    angles = sorted(rng.uniform(0, math.tau) for _ in range(sides))
    for h, taper in rings:
        for a, r in zip(angles, radii):
            jitter = rng.uniform(0.9, 1.1)
            bm.verts.new(Vector((math.cos(a) * r * taper * jitter + lean.x * h * height,
                                 math.sin(a) * r * taper * jitter + lean.y * h * height,
                                 h * height)))
    # Tip: one or two sharp points, offset so the blade looks split / chipped.
    tip = Vector((lean.x * height + rng.uniform(-0.2, 0.2) * base_r,
                  lean.y * height + rng.uniform(-0.2, 0.2) * base_r, height))
    bm.verts.new(tip)
    if rng.random() < 0.5:
        bm.verts.new(tip + Vector((rng.uniform(-0.5, 0.5) * base_r, rng.uniform(-0.5, 0.5) * base_r,
                                   -height * rng.uniform(0.12, 0.22))))
    bmesh.ops.convex_hull(bm, input=bm.verts)
    loose = [v for v in bm.verts if not v.link_faces]
    bmesh.ops.delete(bm, geom=loose, context="VERTS")
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return bm


def build_shards():
    """Jagged volcanic crags: single obsidian blades and clustered crowns of blades, for
    cliff silhouettes and the lava spires. Normalised to a 1 m footprint, ~1 m tall (scaled in-game)."""
    variants = [
        # name, blades, spread
        ("rock_shard_01", 1, 0.0),
        ("rock_shard_02", 1, 0.0),
        ("rock_crag_01", 5, 0.45),
        ("rock_crag_02", 7, 0.55),
        ("rock_crag_03", 9, 0.7),
    ]
    for i, (name, blades, spread) in enumerate(variants):
        reset_scene()
        rng = random.Random(5000 + i * 31)
        bm = bmesh.new()
        for b in range(blades):
            main = b == 0
            ang = rng.uniform(0, math.tau)
            dist = 0 if main else rng.uniform(0.35, 1.0) * spread
            pos = Vector((math.cos(ang) * dist, math.sin(ang) * dist, 0))
            height = 1.0 if main else rng.uniform(0.35, 0.8)
            # Side blades lean outward, like the crown of a broken basalt column.
            outward = Vector((math.cos(ang), math.sin(ang), 0)) * (0.0 if main else rng.uniform(0.1, 0.35))
            lean = outward + Vector((rng.uniform(-0.08, 0.08), rng.uniform(-0.08, 0.08), 0))
            tmp = shard_bmesh(rng, height, rng.uniform(0.14, 0.22) if main else rng.uniform(0.08, 0.16), lean)
            bmesh.ops.transform(tmp, matrix=Matrix.Translation(pos + Vector((0, 0, -0.05))), verts=tmp.verts)
            merge_into(bm, tmp, 0)
        # Normalise: footprint 1 m wide, height 1 m, base at z=0, centred in xy.
        cos = [v.co for v in bm.verts]
        lo = Vector((min(c.x for c in cos), min(c.y for c in cos), min(c.z for c in cos)))
        hi = Vector((max(c.x for c in cos), max(c.y for c in cos), max(c.z for c in cos)))
        mid = (lo + hi) / 2
        width = max(hi.x - lo.x, hi.y - lo.y)
        for v in bm.verts:
            c = v.co
            v.co = Vector(((c.x - mid.x) / width, (c.y - mid.y) / width, (c.z - lo.z) / (hi.z - lo.z)))
        ob = obj_from_bmesh(name, bm, "Rock", smooth=False)
        multi_material(ob, ["Rock"])
        export(name)


# ============================================================================= CHERRY TREE

def build_cherry_tree():
    """Gnarled, wind-swept cherry tree. Trunk leans out toward Godot +Z (Blender -Y),
    i.e. over the cliff edge. Two meshes: Trunk (Bark) and Blossoms (Blossom)."""
    reset_scene()
    rng = random.Random(42)

    curve = bpy.data.curves.new("TrunkCurve", "CURVE")
    curve.dimensions = "3D"
    curve.bevel_depth = 1.0
    curve.bevel_resolution = 1
    curve.resolution_u = 3
    curve.use_fill_caps = True
    tips = []

    def add_spline(points):
        sp = curve.splines.new("NURBS")
        sp.points.add(len(points) - 1)
        for p, (co, r) in zip(sp.points, points):
            p.co = (co.x, co.y, co.z, 1.0)
            p.radius = r
        sp.order_u = 3
        sp.use_endpoint_u = True

    out_dir = Vector((0.0, -1.0, 0.0))  # over the cliff

    # Trunk: S-curve that twists and leans out.
    trunk = [
        (Vector((0, 0, -0.4)), 0.55),
        (Vector((0.05, 0.1, 0.6)), 0.45),
        (Vector((-0.35, -0.1, 1.5)), 0.38),
        (Vector((-0.2, -0.7, 2.4)), 0.33),
        (Vector((0.35, -1.3, 3.0)), 0.29),
        (Vector((0.5, -1.9, 3.6)), 0.25),
    ]
    add_spline(trunk)
    # Exposed roots gripping the rock.
    for k in range(5):
        ang = k / 5 * math.tau + rng.uniform(-0.3, 0.3)
        d = Vector((math.cos(ang), math.sin(ang), 0))
        add_spline([(Vector((0, 0, 0.3)), 0.25), (d * 0.7 + Vector((0, 0, -0.05)), 0.16),
                    (d * 1.4 + Vector((0, 0, -0.35)), 0.06)])

    def branch(start, direction, length, radius, depth):
        pts = [(start.copy(), radius)]
        p = start.copy()
        d = direction.normalized()
        steps = 3
        for s in range(steps):
            jitter = Vector((rng.uniform(-0.35, 0.35), rng.uniform(-0.35, 0.35), rng.uniform(-0.2, 0.25)))
            d = (d + jitter + out_dir * 0.12 + Vector((0, 0, 0.05))).normalized()
            p = p + d * (length / steps)
            pts.append((p.copy(), radius * (1 - 0.3 * (s + 1) / steps)))
        add_spline(pts)
        if depth == 0:
            tips.append(p)
            return
        if depth <= 1:
            tips.append(p)
        for _ in range(rng.choice((2, 2, 3))):
            nd = (d + Vector((rng.uniform(-0.9, 0.9), rng.uniform(-0.9, 0.9), rng.uniform(-0.25, 0.55)))).normalized()
            branch(p, nd, length * rng.uniform(0.62, 0.78), radius * 0.62, depth - 1)

    crown = trunk[-1][0]
    limb_dirs = [Vector((1, -0.6, 0.5)), Vector((-1, -0.4, 0.45)), Vector((0.2, -1, 0.35)),
                 Vector((-0.3, 0.6, 0.7)), Vector((0.8, 0.5, 0.6))]
    for ld in limb_dirs:
        branch(crown, ld, 2.0, 0.2, 3)
    # One long dramatic limb reaching far over the drop.
    branch(trunk[3][0], Vector((-0.4, -1, 0.15)), 2.6, 0.2, 2)

    trunk_ob = bpy.data.objects.new("Trunk", curve)
    bpy.context.scene.collection.objects.link(trunk_ob)
    bpy.context.view_layer.objects.active = trunk_ob
    trunk_ob.select_set(True)
    bpy.ops.object.convert(target="MESH")
    trunk_ob = bpy.context.view_layer.objects.active
    trunk_ob.name = "Trunk"
    multi_material(trunk_ob, ["Bark"])
    for p in trunk_ob.data.polygons:
        p.use_smooth = True

    # Blossom clouds: many small, soft puffs per tip (a big flat-bottomed canopy built from
    # little florets reads as blossom; a few big icospheres read as pink boulders), plus
    # sprigs that droop below the branch line.
    bm = bmesh.new()

    def puff(center, r, squash=0.8, subdiv=2):
        tmp = bmesh.new()
        bmesh.ops.create_icosphere(tmp, subdivisions=subdiv, radius=r)
        for v in tmp.verts:
            v.co *= 1 + rng.uniform(-0.08, 0.08)
            v.co.z *= squash
        bmesh.ops.transform(tmp, matrix=Matrix.Translation(center), verts=tmp.verts)
        merge_into(bm, tmp, 0)

    for tip in tips:
        # Core: a few medium puffs that give the clump its mass.
        for _ in range(rng.randint(2, 3)):
            off = Vector((rng.gauss(0, 0.25), rng.gauss(0, 0.25), rng.gauss(0.05, 0.12)))
            puff(tip + off, rng.uniform(0.22, 0.32), 0.72)
        # Florets: lots of small puffs scattered over a flattened dome around the core.
        for _ in range(rng.randint(9, 14)):
            d = Vector((rng.gauss(0, 1), rng.gauss(0, 1), abs(rng.gauss(0, 0.6)) - 0.15)).normalized()
            off = Vector((d.x * 0.62, d.y * 0.62, d.z * 0.38)) * rng.uniform(0.55, 1.0)
            puff(tip + off, rng.uniform(0.08, 0.16), 0.85, subdiv=1)
        # Drooping sprigs.
        for _ in range(rng.choice((0, 1, 1, 2))):
            base = tip + Vector((rng.uniform(-0.4, 0.4), rng.uniform(-0.4, 0.4), -0.25))
            for k in range(rng.randint(2, 4)):
                puff(base + Vector((rng.uniform(-0.05, 0.05), rng.uniform(-0.05, 0.05), -0.16 * k)),
                     0.1 - 0.015 * k, 1.0, subdiv=1)
    blossoms = obj_from_bmesh("Blossoms", bm, "Blossom", smooth=True)
    multi_material(blossoms, ["Blossom"])
    print(f"[assets] cherry tree: {len(tips)} blossom tips")
    export("cherry_tree")


# ============================================================================= PINE

def build_pine():
    for i in range(3):
        reset_scene()
        rng = random.Random(77 + i)
        bm = bmesh.new()
        height = rng.uniform(5.5, 7.0)
        add_cone(bm, 0.28, 0.12, height * 0.55, 6, (0, 0, height * 0.275), mat_index=0)
        tiers = 4
        for t in range(tiers):
            f = t / tiers
            r = 1.9 * (1 - f * 0.72)
            depth = 2.2 * (1 - f * 0.35)
            z = 1.6 + f * (height - 2.4) + depth / 2
            add_cone(bm, r, 0.0, depth, 7, (0, 0, z), rot=(0, 0, rng.uniform(0, math.tau)),
                     mat_index=1, jitter=0.18, rng=rng)
        ob = obj_from_bmesh(f"pine_{i + 1:02d}", bm, ["Bark", "PineNeedles"], smooth=False)
        multi_material(ob, ["Bark", "PineNeedles"])
        export(f"pine_{i + 1:02d}")


# ============================================================================= SHACK

def build_shack():
    """4.4 m square plank shack, door on the Godot +Z side (Blender -Y), gable roof."""
    reset_scene()
    rng = random.Random(5)
    bm = bmesh.new()
    W, H = 4.4, 2.8
    half = W / 2 - 0.1
    WOOD, DARK, ROOF, STONE, GLOW = 0, 1, 2, 3, 4

    # Stone footing + plank floor.
    for x in [i * 0.55 - W / 2 + 0.25 for i in range(9)]:
        for y in (-W / 2, W / 2):
            add_box(bm, (0.5, 0.32, 0.28), (x, y, 0.1), rot=(0, 0, rng.uniform(-0.2, 0.2)), bevel=0.05, mat_index=STONE)
            add_box(bm, (0.32, 0.5, 0.28), (y, x, 0.1), rot=(0, 0, rng.uniform(-0.2, 0.2)), bevel=0.05, mat_index=STONE)
    for i in range(16):
        x = -W / 2 + 0.15 + i * 0.275
        add_box(bm, (0.26, W - 0.2, 0.08), (x, 0, 0.22), mat_index=DARK)

    def plank_wall(axis, fixed, gaps):
        """Vertical planks along an axis; gaps = list of (lo, hi, zlo, zhi) openings."""
        n = 17
        for i in range(n):
            t = -half + 0.13 + i * (2 * half - 0.26) / (n - 1)
            top = H + rng.uniform(-0.06, 0.04)
            segments = [(0.25, top)]
            for lo, hi, zlo, zhi in gaps:
                if lo <= t <= hi:
                    segments = [(z0, z1) for z0, z1 in [(0.25, zlo), (zhi, top)] if z1 - z0 > 0.05]
            for z0, z1 in segments:
                c = (t, fixed, (z0 + z1) / 2) if axis == "x" else (fixed, t, (z0 + z1) / 2)
                s = (0.24, 0.09, z1 - z0) if axis == "x" else (0.09, 0.24, z1 - z0)
                add_box(bm, s, c, rot=(0, 0, rng.uniform(-0.02, 0.02)), bevel=0.015,
                        mat_index=WOOD if rng.random() > 0.25 else DARK)

    plank_wall("x", W / 2 - 0.1, [])                              # back (Godot -Z)
    plank_wall("x", -W / 2 + 0.1, [(-0.65, 0.65, 0.0, 2.25)])     # front with door
    plank_wall("y", -W / 2 + 0.1, [(-0.45, 0.45, 1.25, 1.9)])     # west with window
    plank_wall("y", W / 2 - 0.1, [])                              # east

    # Corner posts, door frame, window frame and glow.
    for sx in (-1, 1):
        for sy in (-1, 1):
            add_box(bm, (0.2, 0.2, H + 0.1), (sx * (W / 2 - 0.1), sy * (W / 2 - 0.1), (H + 0.1) / 2 + 0.2), bevel=0.03, mat_index=DARK)
    add_box(bm, (1.5, 0.14, 0.16), (0, -W / 2 + 0.08, 2.32), mat_index=DARK)
    add_box(bm, (0.08, 0.95, 0.08), (-W / 2 + 0.06, 0, 1.22), mat_index=DARK)
    add_box(bm, (0.08, 0.95, 0.08), (-W / 2 + 0.06, 0, 1.93), mat_index=DARK)
    add_box(bm, (0.03, 0.85, 0.62), (-W / 2 + 0.16, 0, 1.57), mat_index=GLOW)

    # Gable roof: ridge runs front-to-back (Blender Y). Two thick slabs + shingle rows.
    rise, span = 1.7, W / 2 + 0.45
    pitch = math.atan2(rise, span)
    slab_w = math.hypot(rise, span)
    for side in (-1, 1):
        cx = side * span / 2
        cz = H + 0.25 + rise / 2
        add_box(bm, (slab_w, W + 0.8, 0.12), (cx, 0, cz), rot=(0, side * pitch, 0), mat_index=ROOF)
        for row in range(5):
            f = (row + 0.5) / 5
            sx = side * span * (1 - f)
            sz = H + 0.25 + rise * f + 0.09
            add_box(bm, (slab_w / 5 + 0.08, W + 0.85, 0.07), (sx, 0, sz), rot=(0, side * pitch, 0),
                    bevel=0.01, mat_index=ROOF if row % 2 else DARK)
    add_box(bm, (0.22, W + 0.95, 0.22), (0, 0, H + 0.3 + rise), bevel=0.03, mat_index=DARK)
    # Gable triangles (planked).
    for y in (-W / 2 + 0.1, W / 2 - 0.1):
        for i in range(9):
            x = -half + 0.25 + i * (2 * half - 0.5) / 8
            hgt = rise * (1 - abs(x) / span)
            add_box(bm, (0.24, 0.09, hgt), (x, y, H + 0.22 + hgt / 2), mat_index=WOOD)

    # Chimney on the back-east corner.
    for k in range(9):
        add_box(bm, (0.62, 0.62, 0.33), (1.3 + rng.uniform(-0.03, 0.03), 1.4, 0.4 + k * 0.42),
                rot=(0, 0, rng.uniform(-0.08, 0.08)), bevel=0.05, mat_index=STONE)

    ob = obj_from_bmesh("Shack", bm, ["Wood", "WoodDark", "Roof", "Stone", "WindowGlow"], smooth=False)
    multi_material(ob, ["Wood", "WoodDark", "Roof", "Stone", "WindowGlow"])
    export("shack")


# ============================================================================= HERO

def build_hero():
    """Stylized adventurer, 1.8 m, facing Godot -Z (Blender +Y). Separate parts with
    origins at their pivots so Godot can swing limbs: Hips, Torso, Head, ArmL/R, LegL/R."""
    reset_scene()
    SKIN, SHIRT, VEST, PANTS, BOOTS, HAIR, SCARF, LEATHER, METAL, EYES = range(10)
    names = ["Skin", "Shirt", "Vest", "Pants", "Boots", "Hair", "Scarf", "Leather", "Metal", "Eyes"]
    rng = random.Random(9)

    pivots = {}

    def part(name, pivot, build, parent=None):
        bm = bmesh.new()
        build(bm)
        ob = obj_from_bmesh(name, bm, names, smooth=False, location=pivot)
        multi_material(ob, names)
        pivots[name] = Vector(pivot)
        if parent:
            ob.parent = parent
            ob.location = Vector(pivot) - pivots[parent.name]
        return ob

    HIP = 0.93

    def hips(bm):
        add_box(bm, (0.38, 0.24, 0.2), (0, 0, -0.04), bevel=0.04, segments=2, mat_index=PANTS)
        add_box(bm, (0.42, 0.27, 0.08), (0, 0, 0.06), bevel=0.02, mat_index=LEATHER)
        add_box(bm, (0.09, 0.03, 0.07), (0, 0.14, 0.06), mat_index=METAL)
        add_box(bm, (0.12, 0.1, 0.16), (0.2, -0.02, -0.04), bevel=0.02, mat_index=LEATHER)  # pouch

    def torso(bm):
        add_box(bm, (0.4, 0.23, 0.52), (0, 0, 0.3), bevel=0.06, segments=2, mat_index=SHIRT)
        for sx in (-1, 1):  # open vest panels
            add_box(bm, (0.15, 0.25, 0.42), (sx * 0.135, 0.005, 0.29), bevel=0.03, mat_index=VEST)
        add_box(bm, (0.42, 0.06, 0.44), (0, -0.1, 0.3), bevel=0.03, mat_index=VEST)
        add_box(bm, (0.32, 0.28, 0.09), (0, 0, 0.56), bevel=0.04, segments=2, mat_index=SCARF)
        add_box(bm, (0.12, 0.06, 0.2), (0.07, -0.13, 0.5), rot=(0.3, 0, 0.2), bevel=0.02, mat_index=SCARF)
        add_box(bm, (0.04, 0.27, 0.48), (-0.06, 0, 0.3), rot=(0, 0.75, 0), mat_index=LEATHER)  # strap

    def head(bm):
        add_box(bm, (0.25, 0.25, 0.27), (0, 0.01, 0.17), bevel=0.08, segments=3, mat_index=SKIN)
        add_box(bm, (0.1, 0.1, 0.1), (0, 0, 0.0), mat_index=SKIN)  # neck
        for sx in (-1, 1):
            add_box(bm, (0.035, 0.02, 0.05), (sx * 0.055, 0.135, 0.18), mat_index=EYES)
            add_box(bm, (0.04, 0.05, 0.06), (sx * 0.13, 0.0, 0.17), bevel=0.01, mat_index=SKIN)  # ears
        add_box(bm, (0.04, 0.04, 0.05), (0, 0.14, 0.13), bevel=0.01, mat_index=SKIN)
        # Messy hair: cap + back + tufts.
        add_box(bm, (0.28, 0.28, 0.12), (0, -0.005, 0.3), bevel=0.05, segments=2, mat_index=HAIR)
        add_box(bm, (0.28, 0.12, 0.22), (0, -0.09, 0.2), bevel=0.04, mat_index=HAIR)
        for _ in range(9):
            a = rng.uniform(0, math.tau)
            r = rng.uniform(0.04, 0.12)
            pos = (math.cos(a) * r, math.sin(a) * r - 0.02, 0.34)
            tilt = (rng.uniform(-0.7, 0.7), rng.uniform(-0.7, 0.7), 0)
            add_cone(bm, 0.06, 0.0, 0.16, 4, pos, rot=tilt, mat_index=HAIR)
        add_cone(bm, 0.07, 0.0, 0.15, 4, (0.05, 0.11, 0.3), rot=(1.6, 0, 0.3), mat_index=HAIR)  # fringe
        add_cone(bm, 0.06, 0.0, 0.14, 4, (-0.06, 0.11, 0.3), rot=(1.5, 0, -0.4), mat_index=HAIR)

    def arm(side):
        def build(bm):
            add_box(bm, (0.13, 0.14, 0.3), (0, 0, -0.14), bevel=0.04, segments=2, mat_index=SHIRT)
            add_box(bm, (0.11, 0.12, 0.2), (0, 0, -0.36), bevel=0.03, mat_index=SKIN)
            add_box(bm, (0.125, 0.135, 0.12), (0, 0, -0.42), bevel=0.02, mat_index=LEATHER)  # bracer
            add_box(bm, (0.1, 0.11, 0.11), (0, 0.01, -0.55), bevel=0.03, mat_index=SKIN)
        return build

    def leg(bm):
        add_box(bm, (0.16, 0.18, 0.6), (0, 0, -0.3), bevel=0.04, segments=2, mat_index=PANTS)
        add_box(bm, (0.18, 0.2, 0.26), (0, 0.0, -0.66), bevel=0.03, mat_index=BOOTS)
        add_box(bm, (0.17, 0.27, 0.1), (0, 0.04, -0.88), bevel=0.03, mat_index=BOOTS)
        add_box(bm, (0.2, 0.22, 0.05), (0, 0.0, -0.53), mat_index=LEATHER)  # boot cuff

    hips_ob = part("Hips", (0, 0, HIP), hips)
    torso_ob = part("Torso", (0, 0, HIP + 0.05), torso, parent=hips_ob)
    part("Head", (0, 0, HIP + 0.63), head, parent=torso_ob)
    part("ArmL", (0.27, 0, HIP + 0.5), arm(1), parent=torso_ob)
    part("ArmR", (-0.27, 0, HIP + 0.5), arm(-1), parent=torso_ob)
    part("LegL", (0.1, 0, HIP - 0.03), leg, parent=hips_ob)
    part("LegR", (-0.1, 0, HIP - 0.03), leg, parent=hips_ob)
    export("hero")


# ============================================================================= main

BUILDERS = {
    "rocks": build_rocks,
    "shards": build_shards,
    "tree": build_cherry_tree,
    "pine": build_pine,
    "shack": build_shack,
    "hero": build_hero,
}

for key, fn in BUILDERS.items():
    if ONLY and key not in ONLY:
        continue
    print(f"[assets] building {key}")
    fn()
print("[assets] done")
