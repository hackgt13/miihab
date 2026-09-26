"""Export the purchased course without modifying its .blend source.

Run using Blender --background --disable-autoexec <Golf.blend> --python this.py.
Evaluates procedural meshes and bakes terrain color into portable textures.
"""
import bpy
import json
from pathlib import Path
from mathutils import Vector

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'unity/KinestheticUnity/Assets/Kinesthetic/Art/PurchasedGolf'
OUT.mkdir(parents=True, exist_ok=True)
source_scene = bpy.context.scene
if bpy.context.object and bpy.context.object.mode != 'OBJECT':
    bpy.ops.object.mode_set(mode='OBJECT')
source_scene.render.engine = 'CYCLES'
source_scene.cycles.device = 'CPU'
source_scene.cycles.samples = 4
source_scene.render.bake.use_pass_direct = False
source_scene.render.bake.use_pass_indirect = False
source_scene.render.bake.use_pass_color = True
source_scene.render.bake.margin = 8
textures = ROOT / 'art/course-reference/Golf/Textures'
for img in bpy.data.images:
    if img.packed_file or not img.filepath:
        continue
    if not Path(bpy.path.abspath(img.filepath)).exists():
        candidate = textures / Path(img.filepath).name
        if candidate.exists():
            img.filepath = str(candidate)
            img.reload()
        elif img.name == 'Rough Map':
            # Missing optional blend mask: choose first authored green shader.
            mask = bpy.data.images.new('Unity fallback green mask', width=4, height=4)
            mask.generated_color = (0, 0, 0, 1)
            for mat in bpy.data.materials:
                if mat.use_nodes:
                    for n in mat.node_tree.nodes:
                        if n.type == 'TEX_IMAGE' and n.image == img:
                            n.image = mask

deps = bpy.context.evaluated_depsgraph_get()
keep = []
report = []
terrain_names = ['Course Green', 'Course Green.004', 'Course Rough', 'Course Rough.001']
prop_names = ['Flag', 'Flag Pole', 'Tee Marker 1', 'Tee Marker 2']
for original in list(source_scene.objects):
    is_tree = (original.name.startswith(('Tree', 'Evergreen')) and
               'Generator' not in original.name and 'Billboard' not in original.name)
    is_cloud = original.name.startswith('Cloud')
    if original.hide_render or original.type != 'MESH' or not (
        original.name in terrain_names + prop_names or is_tree or is_cloud):
        continue
    evaluated = original.evaluated_get(deps)
    mesh = bpy.data.meshes.new_from_object(evaluated, preserve_all_data_layers=True, depsgraph=deps)
    if not mesh.vertices:
        bpy.data.meshes.remove(mesh)
        continue
    obj = bpy.data.objects.new('Export_' + original.name, mesh)
    source_scene.collection.objects.link(obj)
    obj.matrix_world = original.matrix_world.copy()
    obj.hide_render = False
    keep.append(obj)
    report.append({'name': original.name, 'vertices': len(mesh.vertices), 'polygons': len(mesh.polygons)})

# Hide sources so baking/render scene contains only evaluated export geometry.
for obj in list(source_scene.objects):
    if obj not in keep and obj.type == 'MESH':
        obj.hide_render = True

for obj in keep:
    if obj.name.removeprefix('Export_') not in terrain_names:
        continue
    bpy.ops.object.select_all(action='DESELECT')
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    old_uv = obj.data.uv_layers.active.name if obj.data.uv_layers else ''
    # Preserve original sampling before introducing a nonoverlapping bake UV.
    for slot in obj.material_slots:
        if not slot.material:
            continue
        mat = slot.material.copy()
        slot.material = mat
        if not mat.use_nodes:
            continue
        for n in list(mat.node_tree.nodes):
            if n.type == 'UVMAP' and not n.uv_map:
                n.uv_map = old_uv
            if n.type == 'TEX_IMAGE' and not n.inputs['Vector'].is_linked:
                uv = mat.node_tree.nodes.new('ShaderNodeUVMap')
                uv.uv_map = old_uv
                mat.node_tree.links.new(uv.outputs['UV'], n.inputs['Vector'])
    bake_uv = obj.data.uv_layers.new(name='UnityBake')
    xs = [v.co.x for v in obj.data.vertices]
    ys = [v.co.y for v in obj.data.vertices]
    minx, miny = min(xs), min(ys)
    dx, dy = max(xs)-minx, max(ys)-miny
    for loop in obj.data.loops:
        v = obj.data.vertices[loop.vertex_index].co
        bake_uv.data[loop.index].uv = ((v.x-minx)/max(dx, .001), (v.y-miny)/max(dy, .001))
    obj.data.uv_layers.active = bake_uv
    bake_uv.active_render = True
    size = 2048 if obj.name == 'Export_Course Green' else 1024
    img = bpy.data.images.new(obj.name + '_BaseColor', width=size, height=size, alpha=False)
    for slot in obj.material_slots:
        mat = slot.material
        if mat and mat.use_nodes:
            target = mat.node_tree.nodes.new('ShaderNodeTexImage')
            target.image = img
            mat.node_tree.nodes.active = target
    print('BAKING', obj.name, flush=True)
    bpy.ops.object.bake(type='DIFFUSE')
    img.filepath_raw = str(OUT / (img.name + '.png'))
    img.file_format = 'PNG'
    img.save()
    mat = bpy.data.materials.new(obj.name + '_Baked')
    mat.use_nodes = True
    nodes = mat.node_tree.nodes
    tex = nodes.new('ShaderNodeTexImage'); tex.image = img
    uv = nodes.new('ShaderNodeUVMap'); uv.uv_map = 'UnityBake'
    mat.node_tree.links.new(uv.outputs['UV'], tex.inputs['Vector'])
    bsdf = nodes.get('Principled BSDF'); bsdf.inputs['Roughness'].default_value = 1
    mat.node_tree.links.new(tex.outputs['Color'], bsdf.inputs['Base Color'])
    obj.data.materials.clear(); obj.data.materials.append(mat)
    for poly in obj.data.polygons:
        poly.material_index = 0
    # The baked UV is the only exported map.
    for layer in list(obj.data.uv_layers):
        if layer != bake_uv:
            obj.data.uv_layers.remove(layer)

# Portable alpha-cutout foliage and simple prop materials.
converted = {}
for obj in keep:
    if obj.name.removeprefix('Export_') in terrain_names:
        continue
    for slot in obj.material_slots:
        src = slot.material
        if not src:
            continue
        if src.name in converted:
            slot.material = converted[src.name]; continue
        mat = bpy.data.materials.new(src.name + '_Unity')
        mat.use_nodes = True
        bsdf = mat.node_tree.nodes.get('Principled BSDF')
        bsdf.inputs['Roughness'].default_value = .9
        src_bsdf = next((n for n in src.node_tree.nodes if n.type == 'BSDF_PRINCIPLED'), None) if src.use_nodes else None
        image_node = next((n for n in src.node_tree.nodes if n.type == 'TEX_IMAGE' and n.image and n.image.size[0] > 0), None) if src.use_nodes else None
        if image_node:
            tex = mat.node_tree.nodes.new('ShaderNodeTexImage'); tex.image = image_node.image
            mat.node_tree.links.new(tex.outputs['Color'], bsdf.inputs['Base Color'])
            if any(t in src.name for t in ['Tree', 'Cloud', 'Evergreen']):
                mat.node_tree.links.new(tex.outputs['Alpha'], bsdf.inputs['Alpha'])
                mat.surface_render_method = 'DITHERED'
        elif src_bsdf:
            bsdf.inputs['Base Color'].default_value = src_bsdf.inputs['Base Color'].default_value
        if src.name.startswith('Bark'):
            bsdf.inputs['Base Color'].default_value = (.16, .11, .055, 1)
        mat.use_backface_culling = False
        converted[src.name] = mat
        slot.material = mat

for name, source in [('TeeAnchor', 'Golf Ball'), ('CupAnchor', 'Flag Pole'), ('SourceCameraAnchor','Camera')]:
    marker = bpy.data.objects.new(name, None)
    source_scene.collection.objects.link(marker)
    marker.matrix_world = bpy.data.objects[source].matrix_world.copy()
    keep.append(marker)

bpy.ops.object.select_all(action='DESELECT')
for obj in keep:
    obj.select_set(True)
bpy.ops.export_scene.gltf(filepath=str(OUT/'PurchasedCourse.glb'), export_format='GLB',
    use_selection=True, export_apply=True, export_animations=False, export_cameras=False,
    export_extras=True)
(OUT/'export-report.json').write_text(json.dumps({'source':'User-purchased Golf.zip',
    'objects':report,'missing_mask_fallback':'Rough Map -> black mask; first green shader',
    'source_unmodified':True}, indent=2))
print('COURSE EXPORT COMPLETE', flush=True)
