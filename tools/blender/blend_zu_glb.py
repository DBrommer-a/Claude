"""Wandelt eine .blend-Datei in GLB um (für Unity/glTFast). Läuft in Blender:

    blender -b --factory-startup --disable-autoexec MODELL.blend --python blend_zu_glb.py -- AUSGABE.glb [--info] [--texturen ORDNER]

--disable-autoexec: Skripte aus der .blend-Datei laufen nicht (fremde Dateien sind Daten, kein Code).
--info:      nur auflisten (Objekte, Maße, Materialien, Texturen), nichts schreiben.
--texturen:  Ordner, in dem fehlende Texturen gesucht werden (z. B. der textures-Ordner aus dem Zip).
Exportiert werden nur sichtbare Mesh-Objekte, Modifikatoren angewendet, Y nach oben (glTF-Standard).
"""
import os
import sys

import bpy

MAX_SUBDIV = 1


def arguments():
    args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    out, info, textures = None, False, None
    i = 0
    while i < len(args):
        if args[i] == "--info":
            info = True
        elif args[i] == "--texturen" and i + 1 < len(args):
            textures = args[i + 1]
            i += 1
        else:
            out = args[i]
        i += 1
    return out, info, textures


def describe():
    print("== Objekte")
    for o in bpy.data.objects:
        mats = [s.material.name for s in getattr(o, "material_slots", []) if s.material]
        print(f"  {o.type:8s} {o.name!r:40s} sichtbar={o.visible_get()} maße={tuple(round(v, 3) for v in o.dimensions)} "
              f"ort={tuple(round(v, 3) for v in o.matrix_world.translation)} eltern={o.parent.name if o.parent else '-'} "
              f"mods={[m.type for m in getattr(o, 'modifiers', [])]} mat={mats}")
    print("== Materialien")
    for m in bpy.data.materials:
        nodes = [n.type for n in m.node_tree.nodes] if m.use_nodes and m.node_tree else []
        print(f"  {m.name!r}: nodes={sorted(set(nodes))}")
    print("== Bilder")
    for img in bpy.data.images:
        path = bpy.path.abspath(img.filepath) if img.filepath else ""
        print(f"  {img.name!r}: gepackt={bool(img.packed_file)} pfad={img.filepath!r} vorhanden={os.path.exists(path) if path else False}")


def find_textures(folder):
    if not folder or not os.path.isdir(folder):
        return
    names = {f.lower(): os.path.join(folder, f) for f in os.listdir(folder)}
    for img in bpy.data.images:
        if img.packed_file or not img.filepath:
            continue
        if os.path.exists(bpy.path.abspath(img.filepath)):
            continue
        hit = names.get(os.path.basename(bpy.path.abspath(img.filepath)).lower())
        if hit:
            img.filepath = hit
            img.reload()
            print(f"  Textur gefunden: {img.name} -> {hit}")


def fix_transparent_materials():
    """Reines „Transparent BSDF“ kennt glTF nicht (würde als graue, undurchsichtige Hülle ankommen).
    Solche Materialien werden zu Principled mit Alpha 0, der Exporter schreibt sie als durchsichtig."""
    for m in bpy.data.materials:
        if not (m.use_nodes and m.node_tree):
            continue
        nodes = m.node_tree.nodes
        output = next((n for n in nodes if n.type == "OUTPUT_MATERIAL" and n.is_active_output), None)
        if output is None or not output.inputs["Surface"].is_linked:
            continue
        source = output.inputs["Surface"].links[0].from_node
        if source.type != "BSDF_TRANSPARENT":
            continue
        principled = nodes.new("ShaderNodeBsdfPrincipled")
        principled.inputs["Alpha"].default_value = 0.0
        m.node_tree.links.new(principled.outputs["BSDF"], output.inputs["Surface"])
        if hasattr(m, "surface_render_method"):
            m.surface_render_method = "BLENDED"
        print(f"  Durchsichtig gemacht: {m.name}")


def export(out):
    fix_transparent_materials()
    bpy.ops.object.select_all(action="DESELECT")
    meshes = [o for o in bpy.data.objects if o.type == "MESH" and o.visible_get()]
    for o in meshes:
        o.select_set(True)
    if not meshes:
        raise SystemExit("Keine sichtbaren Meshes gefunden.")
    bpy.context.view_layer.objects.active = meshes[0]
    # Unterteilung (Subdivision) begrenzen: für eine 30-cm-Drohne im Spiel reicht Stufe 1.
    for o in meshes:
        for m in o.modifiers:
            if m.type == "SUBSURF":
                m.levels = min(m.levels, MAX_SUBDIV)
                m.render_levels = min(m.render_levels, MAX_SUBDIV)
    depsgraph = bpy.context.evaluated_depsgraph_get()
    triangles = 0
    for o in meshes:
        evaluated = o.evaluated_get(depsgraph)
        mesh = evaluated.to_mesh()
        mesh.calc_loop_triangles()
        triangles += len(mesh.loop_triangles)
        evaluated.to_mesh_clear()
    print(f"Dreiecke nach Modifikatoren: {triangles:,}")
    os.makedirs(os.path.dirname(os.path.abspath(out)), exist_ok=True)
    bpy.ops.export_scene.gltf(
        filepath=out,
        export_format="GLB",
        use_selection=True,
        export_apply=True,
        export_yup=True,
        export_image_format="AUTO",
        export_materials="EXPORT",
    )
    print(f"GLB geschrieben: {out} ({os.path.getsize(out) / 1e6:.1f} MB, {len(meshes)} Meshes)")


def main():
    out, info, textures = arguments()
    find_textures(textures)
    describe()
    if info:
        return
    if not out:
        raise SystemExit("Ausgabedatei fehlt (… -- AUSGABE.glb).")
    export(out)


main()
