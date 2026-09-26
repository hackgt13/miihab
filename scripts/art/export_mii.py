"""Export the supplied Mii Maker selection into a small, skinned Unity GLB.

Run in Blender with --disable-autoexec. Source file is never overwritten.
Geometry, armature, and facial sprites come from the user's .blend; this only
resolves Blender-only selectors/material graphs and preserves useful weights.
"""
import bpy, json, sys, math, argparse
from pathlib import Path
from mathutils import Matrix, Vector
import numpy as np

ROOT = Path(__file__).resolve().parents[2]
parser = argparse.ArgumentParser()
parser.add_argument('--profile', default=str(ROOT / 'art/mii/profile.json'))
parser.add_argument('--art-dir', default=str(ROOT / 'art/mii'))
parser.add_argument('--output-subdir', default='')
parser.add_argument('--name', default='KinestheticMii')
args = parser.parse_args(sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else [])
PROFILE = Path(args.profile)
profile = json.loads(PROFILE.read_text()) if PROFILE.exists() else {}
OUT = ROOT / 'unity/KinestheticUnity/Assets/Kinesthetic/Art/Mii' / args.output_subdir
OUT.mkdir(parents=True, exist_ok=True)
ART = Path(args.art_dir); ART.mkdir(parents=True, exist_ok=True)
source_rig = bpy.data.objects['Mii Armature']
colors = bpy.data.node_groups['Color Control'].nodes['Group Output']
def linear(v): return v/12.92 if v <= .04045 else ((v+.055)/1.055)**2.4
for key, value in profile.get('colors', {}).items():
    rgb = [int(value.lstrip('#')[i:i+2],16)/255 for i in (0,2,4)]
    colors.inputs[key].default_value = (*[linear(v) for v in rgb],1)
for key,value in profile.get('values',{}).items():colors.inputs[key].default_value=value
# Override only the selection index, retaining the maker's mirroring/shrinkwrap logic.
for part, source_name in profile.get('parts', {}).items():
    obj=bpy.data.objects[part]; modifier=obj.modifiers['GeometryNodes']
    group=modifier.node_group.copy(); modifier.node_group=group
    # Collection Info uses Blender's natural ordering, not Python's lexical sort.
    # A one-object collection selects the named part unambiguously on all versions.
    selected=bpy.data.collections.new('KIN selection '+part)
    selected.objects.link(bpy.data.objects[source_name])
    modifier['Socket_2']=selected
    modifier['Socket_6']=False; modifier['Socket_7']=False
    index=0
    nearest=group.nodes.get('Sample Nearest')
    if nearest:
        for link in list(group.links):
            if link.from_node == nearest:
                target=link.to_socket;group.links.remove(link);target.default_value=index
    print('SELECT',part,source_name,index)
for bone, changes in profile.get('facePose', {}).items():
    pb=source_rig.pose.bones[bone]
    if 'scale' in changes:pb.scale=changes['scale']
    if 'location' in changes:pb.location=changes['location']
    if 'rotation' in changes:pb.rotation_mode='XYZ';pb.rotation_euler=[math.radians(v) for v in changes['rotation']]
bpy.context.view_layer.update()
# Keep a native editable maker with all part controls before preparing the game export.
for name in profile.get('omit', []):
    obj=bpy.data.objects.get(name)
    if obj:
        obj.hide_render=True
        obj.hide_set(True)
bpy.ops.wm.save_as_mainfile(filepath=str(ART/(args.name+'-editable.blend')), copy=True)

collection=bpy.data.collections.new('Kinesthetic Export');bpy.context.scene.collection.children.link(collection)
rig=bpy.data.objects.new('KinestheticMiiRig',source_rig.data.copy());collection.objects.link(rig)
# Keep the original anatomical skeleton; selectors and wiggle/IK controllers stay in the maker only.
keep={'root','COG','ctrl.torso','hip','spine.001','spine.002','neck','head'}
for side in ['L','R']:
    keep.update(f'{name}.{side}' for name in ['collar','bicep','forearm','hand','thigh','calf','foot'])
bpy.context.view_layer.objects.active=rig;rig.select_set(True)
bpy.ops.object.mode_set(mode='EDIT')
for bone in list(rig.data.edit_bones):
    if bone.name not in keep:rig.data.edit_bones.remove(bone)
bpy.ops.object.mode_set(mode='OBJECT')
for bone in rig.data.bones:bone.use_deform=True
rig.select_set(False)

exported=[]
def snapshot(obj, name, rigid_head=False):
    dg=bpy.context.evaluated_depsgraph_get();ev=obj.evaluated_get(dg)
    mesh=bpy.data.meshes.new_from_object(ev,preserve_all_data_layers=True,depsgraph=dg)
    if not mesh.vertices:return None
    mesh.transform(obj.matrix_world)
    result=bpy.data.objects.new(name,mesh);collection.objects.link(result)
    if rigid_head:
        # These are rigid face/hair pieces on the maker's head, not inferred facial tracking.
        group=result.vertex_groups.new(name='head');group.add(list(range(len(mesh.vertices))),1,'REPLACE')
    else:
        for group in obj.vertex_groups:result.vertex_groups.new(name=group.name)
        for group in list(result.vertex_groups):
            if group.name not in keep:result.vertex_groups.remove(group)
    result.parent=rig;result.matrix_world=Matrix.Identity(4)
    arm=result.modifiers.new('Original Mii skin weights','ARMATURE');arm.object=rig
    for polygon in mesh.polygons:polygon.use_smooth=True
    exported.append(result);return result

# Source weight mesh is the maker's original body, including articulated arms/hands.
body=bpy.data.objects['torso m weights']
for mod in body.modifiers:
    if mod.type=='ARMATURE':mod.show_viewport=False;mod.show_render=False
sub=body.modifiers.new('Export surface resolution','SUBSURF');sub.levels=2;sub.render_levels=2
bpy.context.view_layer.update()
snapshot(body,'MiiBody')
for name in ['head','hair','eye.L','eye.R','eyebrow.L','eyebrow.R','nose','mouth','glasses','mustache','beard','beauty mark','makeup','wrinkles']:
    if name in profile.get('omit',[]):continue
    snapshot(bpy.data.objects[name], 'Mii_'+name, True)

# Bake custom color groups/sprite atlases into standard base-color + alpha textures.
# This keeps the supplied face artwork instead of substituting a new face texture.
scene=bpy.context.scene;scene.render.engine='CYCLES';scene.cycles.samples=1;scene.cycles.device='CPU'
scene.render.bake.use_clear=True;scene.render.bake.margin=3
material_cache={}
def constant_material(source):
    if source.name in material_cache:return material_cache[source.name]
    source_nodes=source.node_tree.nodes
    bsdf=next((n for n in source_nodes if n.type=='BSDF_PRINCIPLED'),None)
    if not bsdf:return None
    rgba=list(bsdf.inputs['Base Color'].default_value)
    if bsdf.inputs['Base Color'].is_linked:
        link=bsdf.inputs['Base Color'].links[0]
        if link.from_node.type=='GROUP' and link.from_node.node_tree.name=='Color Control':rgba=list(colors.inputs[link.from_socket.name].default_value)
        else:return None
    if any(n.type=='TEX_IMAGE' for n in source_nodes):return None
    mat=bpy.data.materials.new('KIN '+source.name);mat.use_nodes=True
    target=mat.node_tree.nodes.get('Principled BSDF');target.inputs['Base Color'].default_value=rgba;target.inputs['Roughness'].default_value=.83;target.inputs['Specular IOR Level'].default_value=.2
    mat.diffuse_color=rgba;material_cache[source.name]=mat;return mat

for obj in exported:
    simple=[constant_material(m) if m else None for m in obj.data.materials]
    if simple and all(simple):
        indices=[p.material_index for p in obj.data.polygons]
        obj.data.materials.clear()
        for mat in simple:obj.data.materials.append(mat)
        for polygon,index in zip(obj.data.polygons,indices):polygon.material_index=index
        continue
    if not obj.data.uv_layers:continue
    # Work on copies so one part's bake cannot mutate another part's source material.
    for slot in obj.material_slots:
        if slot.material:slot.material=slot.material.copy()
    original=obj.data.uv_layers.active;original.name='SourceUV'
    uv=obj.data.uv_layers.new(name='GameUV')
    xs=[loop.uv.x for loop in original.data];ys=[loop.uv.y for loop in original.data]
    minx,maxx,miny,maxy=min(xs),max(xs),min(ys),max(ys)
    for before,after in zip(original.data,uv.data):after.uv=((before.uv.x-minx)/max(maxx-minx,1e-6),(before.uv.y-miny)/max(maxy-miny,1e-6))
    uv.active_render=True;obj.data.uv_layers.active=uv
    resolution=512
    color_image=bpy.data.images.new(obj.name+' color',width=resolution,height=resolution,alpha=True)
    alpha_image=bpy.data.images.new(obj.name+' alpha',width=resolution,height=resolution,alpha=True)
    sources=[]
    for mat in obj.data.materials:
        nodes=mat.node_tree.nodes;links=mat.node_tree.links
        source_uv=nodes.new('ShaderNodeUVMap');source_uv.uv_map='SourceUV'
        for node in list(nodes):
            if node.type=='TEX_IMAGE':links.new(source_uv.outputs['UV'],node.inputs['Vector'])
        bsdf=next((n for n in nodes if n.type=='BSDF_PRINCIPLED'),None)
        if bsdf is None:
            # The maker's glasses keep their Principled shader inside a group.
            group_node=next((n for n in nodes if n.type=='GROUP' and n.node_tree.name.startswith('glasses color')),None)
            if group_node:
                group_node.node_tree=group_node.node_tree.copy();group=group_node.node_tree
                nested=next(n for n in group.nodes if n.type=='BSDF_PRINCIPLED')
                output=next(n for n in group.nodes if n.type=='GROUP_OUTPUT')
                bsdf=nodes.new('ShaderNodeBsdfPrincipled')
                for field in ['Base Color','Alpha']:
                    socket_type='NodeSocketColor' if field=='Base Color' else 'NodeSocketFloat'
                    group.interface.new_socket(name='Bake '+field,in_out='OUTPUT',socket_type=socket_type)
                    source=nested.inputs[field]
                    if source.is_linked:group.links.new(source.links[0].from_socket,output.inputs['Bake '+field])
                    else:output.inputs['Bake '+field].default_value=source.default_value
                    links.new(group_node.outputs['Bake '+field],bsdf.inputs[field])
        if not bsdf:raise RuntimeError('Unsupported face shader: '+mat.name)
        emit=nodes.new('ShaderNodeEmission');emit.inputs['Strength'].default_value=1
        for output in list(nodes):
            if output.type=='OUTPUT_MATERIAL':nodes.remove(output)
        out=nodes.new('ShaderNodeOutputMaterial');out.is_active_output=True;out.target='ALL';links.new(emit.outputs[0],out.inputs['Surface'])
        bake=nodes.new('ShaderNodeTexImage');nodes.active=bake
        sources.append((mat,bsdf,emit,bake))
    for pass_name,image in [('Base Color',color_image),('Alpha',alpha_image)]:
        for mat,bsdf,emit,bake in sources:
            links=mat.node_tree.links
            for link in list(emit.inputs['Color'].links):links.remove(link)
            src=bsdf.inputs[pass_name]
            if src.is_linked:links.new(src.links[0].from_socket,emit.inputs['Color'])
            else:
                value=src.default_value;emit.inputs['Color'].default_value=(value,value,value,1) if isinstance(value,float) else value
            bake.image=image;mat.node_tree.nodes.active=bake
        bpy.ops.object.select_all(action='DESELECT');obj.select_set(True);bpy.context.view_layer.objects.active=obj
        bpy.ops.object.bake(type='EMIT')
    pixels=np.asarray(color_image.pixels[:],dtype=np.float32).reshape(-1,4)
    alpha=np.asarray(alpha_image.pixels[:],dtype=np.float32).reshape(-1,4)
    pixels[:,3]=np.clip(alpha[:,0],0,1);color_image.pixels.foreach_set(pixels.ravel());color_image.update()
    color_image.filepath_raw=str(OUT/(obj.name+'.png'));color_image.file_format='PNG';color_image.save();color_image.pack()
    mat=bpy.data.materials.new('KIN '+obj.name);mat.use_nodes=True;mat.surface_render_method='DITHERED';mat.use_transparency_overlap=False
    n=mat.node_tree.nodes;t=n.new('ShaderNodeTexImage');t.image=color_image
    bsdf=n.get('Principled BSDF');mat.node_tree.links.new(t.outputs['Color'],bsdf.inputs['Base Color']);mat.node_tree.links.new(t.outputs['Alpha'],bsdf.inputs['Alpha']);bsdf.inputs['Roughness'].default_value=.9;bsdf.inputs['Specular IOR Level'].default_value=.15
    obj.data.materials.clear();obj.data.materials.append(mat)
    for polygon in obj.data.polygons:polygon.material_index=0
    # Export only normalized UV0, otherwise glTF samples the original atlas UVs.
    obj.data.uv_layers.remove(original);obj.data.uv_layers.active=uv
    print('BAKED',obj.name,len(obj.data.vertices),flush=True)

bpy.ops.object.select_all(action='DESELECT');rig.select_set(True)
for obj in exported:obj.select_set(True)
bpy.context.view_layer.objects.active=rig
bpy.ops.export_scene.gltf(filepath=str(OUT/(args.name+'.glb')),export_format='GLB',use_selection=True,export_animations=False,export_skins=True,export_apply=False,export_yup=True)
manifest={'source':bpy.data.filepath,'profile':profile,'objects':[{ 'name':o.name,'vertices':len(o.data.vertices),'materials':[m.name for m in o.data.materials]} for o in exported], 'bones':[b.name for b in rig.data.bones]}
(ART/'export-manifest.json').write_text(json.dumps(manifest,indent=2))
# Save the clean export separately; preserve the native maker as the editable source above.
for o in list(bpy.data.objects):
    if o not in exported and o!=rig:bpy.data.objects.remove(o,do_unlink=True)
bpy.ops.wm.save_as_mainfile(filepath=str(ART/(args.name+'-export.blend')))
print('MII_EXPORT_COMPLETE',len(exported),len(rig.data.bones),flush=True)
