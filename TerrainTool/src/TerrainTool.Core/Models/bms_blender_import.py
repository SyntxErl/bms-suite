# BMS Suite - Model Editor "Export to Blender..."
# Builds a .blend from a Falcon BMS model converted by the Model Editor (model.json + mesh.bin + textures),
# laid out for the official BMS Blender plugin (bms-blender-plugin): LOD collections in the plugin's LOD list,
# DOFs / switches / slots as the plugin's empties, materials with the texture nodes the plugin's exporter reads.
#
# The Model Editor runs it for you:
#   blender --background --python bms_blender_import.py -- model.json Model.blend
# By hand: open Blender, Scripting tab, open this file, set JSON_FILE below, Run Script (the scene is replaced).
#
# Coordinates as in the plugin: BMS (x, y, z) <-> Blender (x, z, y); triangles reversed; UV v -> 1 - v; 1 unit = 1 foot.

import json
import math
import os
import struct
import sys

import bpy
from mathutils import Euler, Matrix, Vector

JSON_FILE = ""      # for a run by hand: full path of model.json
BLEND_FILE = ""     # for a run by hand: where to save (empty = do not save)

S = Matrix(((1, 0, 0, 0), (0, 0, 1, 0), (0, 1, 0, 0), (0, 0, 0, 1)))


def log(*a):
    print("[BMS import]", *a, flush=True)


def args():
    if "--" in sys.argv:
        a = sys.argv[sys.argv.index("--") + 1:]
        return a[0], (a[1] if len(a) > 1 else "")
    return JSON_FILE, BLEND_FILE


def bl_matrix(f):
    m = Matrix(((f[0], f[1], f[2], f[3]), (f[4], f[5], f[6], f[7]), (f[8], f[9], f[10], f[11]), (0, 0, 0, 1)))
    return S @ m @ S


def enable_plugin():
    if hasattr(bpy.types.Object, "bml_type"):
        return True
    try:
        import addon_utils
        addon_utils.enable("bms_blender_plugin", default_set=True, persistent=True)
    except Exception as e:  # noqa
        log("BMS plugin not enabled:", e)
    return hasattr(bpy.types.Object, "bml_type")


def clear_scene():
    for o in list(bpy.data.objects):
        bpy.data.objects.remove(o, do_unlink=True)
    for c in list(bpy.data.collections):
        bpy.data.collections.remove(c)
    for m in list(bpy.data.meshes):
        bpy.data.meshes.remove(m)
    for m in list(bpy.data.materials):
        bpy.data.materials.remove(m)
    for i in list(bpy.data.images):
        bpy.data.images.remove(i)


def socket(node, *names):
    for n in names:
        if n in node.inputs:
            return node.inputs[n]
    return None


def make_material(m, base):
    mat = bpy.data.materials.new(m["name"])
    mat.use_nodes = True
    nt = mat.node_tree
    bsdf = next((n for n in nt.nodes if n.type == "BSDF_PRINCIPLED"), None)
    if bsdf is None:
        bsdf = nt.nodes.new("ShaderNodeBsdfPrincipled")
    mat["bms_template"] = m.get("template") or ""

    def image(key, node_name, color, x, y):
        rel = m.get(key)
        if not rel:
            return None
        path = os.path.join(base, rel)
        if not os.path.exists(path):
            return None
        img = bpy.data.images.load(path, check_existing=True)
        if not color:
            try:
                img.colorspace_settings.name = "Non-Color"
            except Exception:  # noqa
                pass
        n = nt.nodes.new("ShaderNodeTexImage")
        n.name = node_name
        n.label = node_name
        n.image = img
        n.location = (x, y)
        return n

    alb = image("albedo", "Albedo Texture", True, -700, 300)
    if alb:
        nt.links.new(alb.outputs["Color"], socket(bsdf, "Base Color"))
        if m.get("alpha"):
            nt.links.new(alb.outputs["Alpha"], socket(bsdf, "Alpha"))
            mat.blend_method = "CLIP" if hasattr(mat, "blend_method") else mat.blend_method
    armw = image("armw", "ARMW Texture", False, -700, 0)
    if armw:
        sep = nt.nodes.new("ShaderNodeSeparateColor" if hasattr(bpy.types, "ShaderNodeSeparateColor") else "ShaderNodeSeparateRGB")
        sep.location = (-400, 0)
        nt.links.new(armw.outputs["Color"], sep.inputs[0])
        nt.links.new(sep.outputs[1], socket(bsdf, "Roughness"))
        nt.links.new(sep.outputs[2], socket(bsdf, "Metallic"))
    nrm = image("normal", "Normal Texture", False, -700, -300)
    if nrm:
        nm = nt.nodes.new("ShaderNodeNormalMap")
        nm.location = (-400, -300)
        nt.links.new(nrm.outputs["Color"], nm.inputs["Color"])
        nt.links.new(nm.outputs["Normal"], socket(bsdf, "Normal"))
    em = image("emissiveMap", "Emissive Texture", True, -700, -600)
    ec = socket(bsdf, "Emission Color", "Emission")
    es = socket(bsdf, "Emission Strength")
    if em and ec:
        nt.links.new(em.outputs["Color"], ec)
        if es:
            es.default_value = float(m.get("emission") or 1)
    if m.get("color") and ec:
        c = m["color"]
        ec.default_value = (c[0], c[1], c[2], 1)
        socket(bsdf, "Base Color").default_value = (c[0], c[1], c[2], 1)
        if es:
            es.default_value = 2
    if m.get("glass"):
        a = socket(bsdf, "Alpha")
        if a and not a.is_linked:
            a.default_value = 0.35
        try:
            mat.blend_method = "BLEND"
        except Exception:  # noqa
            pass
    if m.get("twoSided"):
        mat.use_backface_culling = False
    if alb:
        nt.nodes.active = alb   # the texture shown in Solid / Texture view
    return mat


def build(data, base, blend):
    plugin = enable_plugin()
    log("BMS Blender plugin:", "enabled" if plugin else "NOT installed - BMS data kept as custom properties")
    clear_scene()
    raw = open(os.path.join(base, data["bin"]), "rb").read()
    scene = bpy.context.scene
    scene["bms_source"] = data.get("source", "")
    scene["bms_texture_sets"] = data.get("textureSets", 1)

    mats = {m["name"]: make_material(m, base) for m in data["materials"]}

    if plugin:
        try:
            scene.lod_list.clear()
        except Exception:  # noqa
            pass
    counts = {"mesh": 0, "dof": 0, "switch": 0, "slot": 0}
    warnings = []
    hidden = []
    checks = []

    def empty(name, coll, display="PLAIN_AXES", size=0.5):
        o = bpy.data.objects.new(name, None)
        o.empty_display_type = display
        o.empty_display_size = size
        coll.objects.link(o)
        return o

    def attach(o, parent, pworld, basis):
        o.parent = parent
        o.matrix_parent_inverse = pworld.inverted_safe() if parent else Matrix.Identity(4)
        o.matrix_basis = basis

    def set_prop(o, name, value):
        # a value outside the plugin's ranges (it clamps) is kept as it is in the custom property too
        try:
            setattr(o, name, value)
        except Exception as e:  # noqa
            warnings.append(f"{o.name}: {name} = {value} not set ({e})")
        try:
            if abs(float(getattr(o, name)) - float(value)) > 1e-4:
                o["bms_" + name] = value
                warnings.append(f"{o.name}: {name} = {value} is outside the plugin's range (kept in custom property bms_{name})")
        except Exception:  # noqa
            pass

    def node(n, parent, pworld, coll, off):
        t = n["t"]
        world = pworld
        obj = None
        if t == "dof":
            counts["dof"] += 1
            kind = n["kind"]
            if kind != "TRANSLATE" and parent is not None and getattr(parent, "dof_type", "") == "TRANSLATE":
                counts["under_tdof"] = counts.get("under_tdof", 0) + 1
            obj = empty(f"DOF {n['num']}", coll, "SINGLE_ARROW" if kind == "TRANSLATE" else "CUBE" if kind == "SCALE" else "ARROWS", 0.4)
            world = bl_matrix(n["frame"])
            attach(obj, parent, pworld, world)
            obj["bms_dof"] = {"number": n["num"], "kind": kind, "min": n["min"], "max": n["max"], "multiplier": n["mul"], "flags": n["flags"],
                              "scale": n["scale"], "translation": n["tvec"]}
            if plugin:
                obj.bml_type = "dof"
                obj.dof_type = kind
                obj.bml_dof_number = n["num"]
                fl = n["flags"]
                obj.dof_check_limits = bool(fl & 1)
                obj.dof_reverse = bool(fl & 2)   # before min/max: the plugin swaps them when this changes
                obj.dof_normalise = bool(fl & 4)
                obj.dof_multiply_min_max = True
                set_prop(obj, "dof_multiplier", n["mul"])
                if kind == "ROTATE":
                    set_prop(obj, "dof_min", math.degrees(n["min"]))
                    set_prop(obj, "dof_max", math.degrees(n["max"]))
                else:
                    set_prop(obj, "dof_min_input", n["min"])
                    set_prop(obj, "dof_max_input", n["max"])
                if kind == "TRANSLATE":
                    tv = Vector((n["tvec"][0], n["tvec"][2], n["tvec"][1]))
                    tv = pworld.to_3x3() @ tv if parent else tv
                    set_prop(obj, "dof_x", tv.x); set_prop(obj, "dof_y", tv.y); set_prop(obj, "dof_z", tv.z)
                elif kind == "SCALE":
                    sc = n["scale"]
                    set_prop(obj, "dof_x", sc[0]); set_prop(obj, "dof_y", sc[2]); set_prop(obj, "dof_z", sc[1])
                # the plugin previews the DOF with delta transforms (a limit can turn it): the BMS file is the DOF at
                # rest, so the deltas go to zero and the frame is set again
                obj.delta_location = (0, 0, 0)
                obj.delta_rotation_euler = (0, 0, 0)
                obj.delta_scale = (1, 1, 1)
                obj.matrix_parent_inverse = pworld.inverted_safe() if parent else Matrix.Identity(4)
                obj.matrix_basis = world
            checks.append((obj, world))
        elif t == "switch":
            counts["switch"] += 1
            obj = empty(f"Switch {n['num']} / {n['branch']}", coll, "CIRCLE", 0.3)
            # a switch has the frame of what contains it
            attach(obj, parent, pworld, pworld)
            obj["bms_switch"] = {"number": n["num"], "branch": n["branch"], "on": n["on"]}
            if plugin:
                obj.bml_type = "switch"
                obj.bml_switch_number = n["num"]
                obj.bml_switch_branch = n["branch"]
                obj.switch_default_on = bool(n["on"])
        elif t == "slot":
            counts["slot"] += 1
            obj = empty(f"Slot {n['num']}", coll, "ARROWS", 0.6)
            f = n["frame"]
            loc = Vector((f[3], f[11], f[7]))
            r = n["r"]
            rm = Matrix(((r[0], r[1], r[2]), (r[3], r[4], r[5]), (r[6], r[7], r[8])))
            e = rm.to_euler("XYZ")
            basis = Matrix.Translation(loc) @ Euler((e.x, e.z, e.y), "XYZ").to_matrix().to_4x4()
            attach(obj, parent, pworld, basis)
            world = pworld
            obj["bms_slot"] = n["num"]
            if plugin:
                obj.bml_type = "slot"
                set_prop(obj, "bml_slot_number", n["num"])
                obj.matrix_parent_inverse = pworld.inverted_safe() if parent else Matrix.Identity(4)
                obj.matrix_basis = basis
        elif t == "mesh":
            counts["mesh"] += 1
            vc, ic, at = n["vc"], n["ic"], n["off"]
            P = struct.unpack_from(f"<{vc * 3}f", raw, at); at += vc * 12
            N = struct.unpack_from(f"<{vc * 3}f", raw, at); at += vc * 12
            U = struct.unpack_from(f"<{vc * 2}f", raw, at); at += vc * 8
            I = struct.unpack_from(f"<{ic}i", raw, at)
            # BML repeats a vertex for every triangle corner: corners at the same place become one vertex (the mesh
            # stays connected for editing); normals and UVs stay per corner
            index_of = {}
            verts = []
            remap = [0] * vc
            for i in range(vc):
                key = (P[i * 3], P[i * 3 + 2], P[i * 3 + 1])
                j = index_of.get(key)
                if j is None:
                    j = index_of[key] = len(verts)
                    verts.append(key)
                remap[i] = j
            corners = []
            for i in range(0, ic - 2, 3):
                a, b, c = I[i], I[i + 2], I[i + 1]
                if a >= vc or b >= vc or c >= vc or len({remap[a], remap[b], remap[c]}) < 3:
                    continue
                corners.append((a, b, c))
            me = bpy.data.meshes.new(n["mat"])
            me.from_pydata(verts, [], [(remap[a], remap[b], remap[c]) for a, b, c in corners])
            me.update()
            loop_src = [k for tri in corners for k in tri]
            if len(me.loops) == len(loop_src):
                uv = me.uv_layers.new(name="UVMap")
                uv.data.foreach_set("uv", [x for k in loop_src for x in (U[k * 2], 1 - U[k * 2 + 1])])
                try:
                    if hasattr(me, "use_auto_smooth"):
                        me.use_auto_smooth = True
                    me.normals_split_custom_set([(N[k * 3], N[k * 3 + 2], N[k * 3 + 1]) for k in loop_src])
                except Exception as e:  # noqa
                    warnings.append(f"normals of {n['mat']}: {e}")
            else:
                warnings.append(f"{n['mat']}: {len(me.loops)} corners instead of {len(loop_src)} (UVs and normals left out)")
            if n["mat"] in mats:
                me.materials.append(mats[n["mat"]])
            obj = bpy.data.objects.new(n["mat"], me)
            coll.objects.link(obj)
            attach(obj, parent, pworld, Matrix.Identity(4))
            if n.get("light"):
                obj["bms_light"] = True
            if off:
                hidden.append(obj)
        if t == "switch":
            off = off or not (n["on"] or n.get("defaultOn"))
        for c in n.get("children", []):
            node(c, obj if obj is not None else parent, world, coll, off)
        if obj is not None and t != "mesh" and off:
            hidden.append(obj)

    first = True
    for lod in data["lods"]:
        coll = bpy.data.collections.new(f"LOD {lod['index']} - {lod['file']}")
        scene.collection.children.link(coll)
        coll["bms_lod_file"] = lod["file"]
        coll["bms_lod_distance"] = lod["distance"]
        # tail number branches (switch 61): BMS shows the first one when they all start off
        root = lod["root"]
        mark_defaults(root)
        root["children"] = [x for c in root.get("children", []) for x in hoist_switches(c)]
        node(root, None, Matrix.Identity(4), coll, False)
        if plugin:
            it = scene.lod_list.add()
            it.collection = coll
            it.file_suffix = lod["suffix"]
            it.viewing_distance = int(round(lod["distance"]))
        if not first:
            lc = bpy.context.view_layer.layer_collection.children.get(coll.name)
            if lc:
                lc.hide_viewport = True
        first = False
        log(f"LOD {lod['index']} ({lod['file']}): done")

    for o in hidden:
        try:
            o.hide_set(True)
        except Exception:  # noqa
            pass

    bpy.context.view_layer.update()
    bad = 0
    for o, w in checks:
        d = max(abs(a - b) for ra, rb in zip(o.matrix_world, w) for a, b in zip(ra, rb))
        if d > 1e-3:
            bad += 1
            if bad <= 10:
                warnings.append(f"{o.name}: placed {d:.4f} away from its BMS frame")
    if counts.get("under_tdof"):
        warnings.append(f"{counts['under_tdof']} rotate/scale DOF(s) are inside a translate DOF: check them after exporting with the plugin "
                        "(its exporter writes their position in model axes, BMS reads it in the axes of the DOF above)")
    for w in warnings[:200]:
        log("warning:", w)
    log(f"{len(data['lods'])} LOD(s), {counts['mesh']} meshes, {counts['dof']} DOFs, {counts['switch']} switches, {counts['slot']} slots, {len(mats)} materials")
    if blend:
        bpy.ops.wm.save_as_mainfile(filepath=blend, relative_remap=True)
        log("saved", blend)


def hoist_switches(n):
    """
    The plugin's exporter writes the meshes of a switch in model coordinates, so a switch inside a DOF would come back
    moved twice in BMS. The same model is written with the switch outside: DOF { A, switch { B } } becomes
    DOF { A } + switch { DOF { B } } (same DOF number and values: BMS draws it the same way).
    """
    kids = []
    for c in n.get("children", []):
        kids.extend(hoist_switches(c))
    n["children"] = kids
    if n["t"] != "dof":
        return [n]
    switches = [c for c in kids if c["t"] == "switch"]
    if not switches:
        return [n]
    out = []
    keep = [c for c in kids if c["t"] != "switch"]
    if keep:
        d = dict(n)
        d["children"] = keep
        out.append(d)
    for sw in switches:
        d = dict(n)
        d["children"] = sw.get("children", [])
        s2 = dict(sw)
        s2["children"] = [d]
        out.append(s2)
    return out


def mark_defaults(root):
    """Switch 61 (tail number): when no branch starts on, the first one is shown (as BMS does)."""
    branches = []

    def walk(n):
        if n["t"] == "switch" and n["num"] == 61:
            branches.append(n)
        for c in n.get("children", []):
            walk(c)
    walk(root)
    if branches and not any(b["on"] for b in branches):
        low = min(b["branch"] for b in branches)
        for b in branches:
            if b["branch"] == low:
                b["defaultOn"] = True


def main():
    jf, blend = args()
    if not jf:
        raise SystemExit("model.json not given")
    with open(jf, encoding="utf-8") as f:
        data = json.load(f)
    build(data, os.path.dirname(os.path.abspath(jf)), os.path.abspath(blend) if blend else "")


main()
