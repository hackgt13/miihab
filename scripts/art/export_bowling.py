"""Blender --background --disable-autoexec Bowling.blend --python scripts/art/export_bowling.py.
Exports the supplied alley and reusable ball/pin meshes without altering the source.
"""
import bpy
from pathlib import Path
from mathutils import Matrix, Vector

OUT = Path(__file__).resolve().parents[2] / 'unity/KinestheticUnity/Assets/Kinesthetic/Bowling/Art'
OUT.mkdir(parents=True, exist_ok=True)
bpy.context.scene.frame_set(1)
deps = bpy.context.evaluated_depsgraph_get()
sources = list(bpy.context.scene.objects)
scale = .265
# Unity's glTF importer reverses X. Rotate the exported root 180 degrees in the
# scene builder: source +Y becomes Unity +Z, source +X becomes Unity +X.
origin = Vector((-24.1758, -3.29977, -.030293))
world = Matrix.Scale(scale, 4) @ Matrix.Translation(-origin)
materials = {}

def portable(src):
    if src.name in materials:
        return materials[src.name]
    mat = bpy.data.materials.new(src.name + '_Bowling')
    mat.use_nodes = True
    mat.use_backface_culling = False
    bsdf = mat.node_tree.nodes.get('Principled BSDF')
    bsdf.inputs['Roughness'].default_value = .55
    nodes = src.node_tree.nodes if src.use_nodes else []
    shader = next((n for n in nodes if n.type == 'BSDF_PRINCIPLED'), None)
    image = next((n for n in nodes if n.type == 'TEX_IMAGE' and n.image and n.image.size[0]), None)
    if image:
        tex = mat.node_tree.nodes.new('ShaderNodeTexImage')
        tex.image = image.image
        mat.node_tree.links.new(tex.outputs['Color'], bsdf.inputs['Base Color'])
        if 'Marker' in src.name:
            mat.node_tree.links.new(tex.outputs['Alpha'], bsdf.inputs['Alpha'])
            mat.surface_render_method = 'DITHERED'
    else:
        bsdf.inputs['Base Color'].default_value = shader.inputs['Base Color'].default_value if shader else src.diffuse_color
    materials[src.name] = mat
    return mat

def clone(original, transform):
    mesh = bpy.data.meshes.new_from_object(original.evaluated_get(deps), preserve_all_data_layers=True, depsgraph=deps)
    mesh.transform(transform @ original.matrix_world)
    obj = bpy.data.objects.new(original.name + '_Export', mesh)
    bpy.context.scene.collection.objects.link(obj)
    for slot in obj.material_slots:
        if slot.material:
            slot.material = portable(slot.material)
    return obj

def export(name, objects):
    bpy.ops.object.select_all(action='DESELECT')
    for obj in objects:
        obj.select_set(True)
    bpy.ops.export_scene.gltf(filepath=str(OUT / (name + '.glb')), export_format='GLB',
        use_selection=True, export_apply=True, export_animations=False, export_cameras=False,
        export_lights=False, export_extras=False)
    for obj in objects:
        bpy.data.objects.remove(obj, do_unlink=True)

environment = []
for obj in sources:
    if obj.type != 'MESH' or obj.hide_render:
        continue
    if obj.name in ('Player', 'Bowling Ball') or obj.name.startswith('Pin ') and obj.name != 'Pin Background' or 'HUD' in obj.name:
        continue
    environment.append(clone(obj, world))
export('BowlingAlley', environment)

for source, name, bottom in [('Pin 1', 'BowlingPin', True), ('Bowling Ball', 'BowlingBall', False)]:
    obj = bpy.data.objects[source]
    corners = [obj.matrix_world @ Vector(c) for c in obj.bound_box]
    center = sum(corners, Vector()) / 8
    if bottom:
        center.z = min(c.z for c in corners)
    export(name, [clone(obj, Matrix.Scale(scale, 4) @ Matrix.Translation(-center))])
print('Bowling export complete:', OUT)
