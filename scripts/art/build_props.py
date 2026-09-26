"""Create Kinesthetic's editable Blender props and small Unity GLB assets.

Blender Z is up and -Y is the front of the chair. Materials are intentionally
simple and rounded so the equipment belongs beside the exported Mii.
"""
import bpy
import math
from pathlib import Path
from mathutils import Vector

ROOT = Path(__file__).resolve().parents[2]
ART = ROOT / 'art/props'
UNITY = ROOT / 'unity/KinestheticUnity/Assets/Kinesthetic/Art/BlenderProps'
ART.mkdir(parents=True, exist_ok=True)
UNITY.mkdir(parents=True, exist_ok=True)

bpy.ops.object.select_all(action='SELECT')
bpy.ops.object.delete(use_global=False)
for collection in list(bpy.data.collections):
    if collection.name != 'Collection' and not collection.objects:
        bpy.data.collections.remove(collection)

def srgb(hex_value):
    values = [int(hex_value[i:i + 2], 16) / 255 for i in (1, 3, 5)]
    return tuple(v / 12.92 if v <= .04045 else ((v + .055) / 1.055) ** 2.4 for v in values) + (1,)

def material(name, color, roughness=.8, metallic=0):
    mat = bpy.data.materials.new(name)
    mat.diffuse_color = srgb(color)
    mat.use_nodes = True
    bsdf = mat.node_tree.nodes.get('Principled BSDF')
    bsdf.inputs['Base Color'].default_value = srgb(color)
    bsdf.inputs['Roughness'].default_value = roughness
    bsdf.inputs['Metallic'].default_value = metallic
    return mat

BLACK = material('Matte black upholstery', '#1A1D21')
FRAME = material('Satin charcoal frame', '#30363B', .53, .35)
STEEL = material('Brushed steel', '#9BA4A8', .4, .65)
RUBBER = material('Near-black rubber', '#111316', .92)
WEIGHT = material('Graphite ten-pound weight', '#25282B', .82)
WHITE = material('Soft white lettering', '#E9E9E3', .85)
GRIP = material('Black textured grip', '#151719', .9)

def collection(name):
    result = bpy.data.collections.new(name)
    bpy.context.scene.collection.children.link(result)
    return result

def organize(obj, group, root):
    for previous in list(obj.users_collection):
        previous.objects.unlink(obj)
    group.objects.link(obj)
    obj.parent = root
    return obj

def root_object(name, group):
    obj = bpy.data.objects.new(name, None)
    group.objects.link(obj)
    return obj

def bevel(obj, width, segments=2):
    mod = obj.modifiers.new('Soft Mii-style edges', 'BEVEL')
    mod.width = width
    mod.segments = segments
    mod.affect = 'EDGES'
    normal = obj.modifiers.new('Weighted surface normals', 'WEIGHTED_NORMAL')
    normal.keep_sharp = True
    for polygon in obj.data.polygons:
        polygon.use_smooth = True

def cube(name, group, root, pos, dimensions, mat, radius=.015):
    bpy.ops.mesh.primitive_cube_add(size=1, location=pos)
    obj = bpy.context.object
    obj.name = name
    obj.dimensions = dimensions
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    if radius:
        bevel(obj, radius)
    obj.data.materials.append(mat)
    return organize(obj, group, root)

def tube(name, group, root, a, b, radius, mat, vertices=12):
    start, end = Vector(a), Vector(b)
    midpoint = (start + end) / 2
    bpy.ops.mesh.primitive_cylinder_add(vertices=vertices, radius=radius, depth=(end-start).length, location=midpoint)
    obj = bpy.context.object
    obj.name = name
    obj.rotation_euler = (end-start).to_track_quat('Z', 'Y').to_euler()
    bevel(obj, min(radius * .35, .008), 2)
    obj.data.materials.append(mat)
    return organize(obj, group, root)

def ring(name, group, root, center, major, minor, mat):
    bpy.ops.mesh.primitive_torus_add(major_segments=48, minor_segments=8,
        location=center, rotation=(0, math.pi/2, 0), major_radius=major, minor_radius=minor)
    obj = bpy.context.object
    obj.name = name
    for face in obj.data.polygons:
        face.use_smooth = True
    obj.data.materials.append(mat)
    return organize(obj, group, root)

def wheel(name, group, root, x, y, z, radius, drive):
    ring(name+' · tire', group, root, (x,y,z), radius, radius * (.085 if drive else .14), RUBBER)
    ring(name+' · steel rim', group, root, (x,y,z), radius * .82, radius * .05, STEEL)
    if drive:
        for i in range(8):
            angle = i * math.tau / 8
            tube(name+' · spoke', group, root, (x,y,z),
                (x, y + math.cos(angle)*radius*.79, z + math.sin(angle)*radius*.79),
                .006, FRAME, 8)
        side = 1 if x > 0 else -1
        ring(name+' · push rim', group, root, (x + side*.045,y,z), radius*.94, .009, STEEL)
    tube(name+' · hub', group, root, (x-.035,y,z), (x+.035,y,z), radius*.16, FRAME)

chair_group = collection('Wheelchair — black and steel')
chair = root_object('Kinesthetic Mii wheelchair', chair_group)
cube('Black seat cushion', chair_group, chair, (0,0,.235), (.53,.51,.075), BLACK, .035)
cube('Black back cushion', chair_group, chair, (0,.27,.44), (.53,.065,.38), BLACK, .029)
for side in (-1,1):
    x = side*.33
    wheel('Left drive wheel' if side < 0 else 'Right drive wheel', chair_group, chair, side*.40,.075,.255,.24,True)
    wheel('Left front caster' if side < 0 else 'Right front caster', chair_group, chair, side*.25,-.34,.09,.075,False)
    tube('Seat side rail', chair_group, chair, (side*.275,.25,.23), (side*.275,-.28,.23), .014, FRAME)
    tube('Lower diagonal frame', chair_group, chair, (side*.27,.18,.23), (side*.24,-.29,.11), .013, STEEL)
    tube('Rear wheel mount', chair_group, chair, (side*.27,.20,.20), (side*.4,.075,.255), .012, FRAME)
    tube('Back upright', chair_group, chair, (side*.25,.27,.26), (side*.25,.29,.65), .015, FRAME)
    tube('Rear push handle', chair_group, chair, (side*.25,.29,.65), (side*.25,.41,.65), .018, GRIP)
    tube('Front armrest support', chair_group, chair, (x,-.19,.25), (x,-.19,.40), .013, STEEL)
    tube('Rear armrest support', chair_group, chair, (x,.19,.25), (x,.19,.40), .013, STEEL)
    cube('Black arm pad', chair_group, chair, (x,0,.405), (.07,.42,.046), BLACK, .022)
    tube('Footrest brace', chair_group, chair, (side*.25,-.28,.17), (side*.22,-.40,.09), .013, FRAME)
    cube('Black footplate', chair_group, chair, (side*.13,-.39,.09), (.2,.20,.025), BLACK, .012)
tube('Front crossbrace', chair_group, chair, (-.26,-.23,.15), (.26,-.23,.15), .01, STEEL)
tube('Rear axle', chair_group, chair, (-.40,.075,.255), (.40,.075,.255), .012, FRAME)

dumbbell_group = collection('Dumbbell — 10 LB')
dumbbell = root_object('Kinesthetic 10 LB dumbbell', dumbbell_group)
tube('Knurled steel grip', dumbbell_group, dumbbell, (-.27,0,0), (.27,0,0), .026, STEEL)
for side in (-1,1):
    bpy.ops.mesh.primitive_cylinder_add(vertices=6, radius=.145, depth=.155,
        location=(side*.34,0,0), rotation=(0,math.pi/2,0))
    head = bpy.context.object
    head.name = 'Black hexagonal 10 LB weight'
    head.data.materials.append(WEIGHT)
    bevel(head,.009,2)
    organize(head,dumbbell_group,dumbbell)
    tube('Steel retaining collar', dumbbell_group, dumbbell,
        (side*.235,0,0), (side*.258,0,0), .045, STEEL)
    bpy.ops.object.text_add(location=(side*.424,0,0), rotation=(math.pi/2, 0, side*math.pi/2))
    text = bpy.context.object
    text.name = 'Embossed 10 LB label'
    text.data.body = '10\nLB'
    text.data.size = .065
    text.data.align_x = 'CENTER'
    text.data.align_y = 'CENTER'
    text.data.extrude = .001
    text.data.bevel_depth = .0006
    text.data.materials.append(WHITE)
    bpy.ops.object.convert(target='MESH')
    organize(bpy.context.object,dumbbell_group,dumbbell)

club_group = collection('Golf club — black and steel')
club = root_object('Kinesthetic golf club · grip origin', club_group)
tube('Black rubber grip', club_group, club, (0,0,-.23), (0,0,0), .025, GRIP)
tube('Satin steel shaft', club_group, club, (0,0,-.20), (.025,0,-.90), .011, STEEL)
tube('Angled hosel', club_group, club, (.025,0,-.90), (.055,0,-.975), .014, STEEL)
bpy.ops.mesh.primitive_uv_sphere_add(segments=16, ring_count=8, location=(.17,0,-.99))
driver_head=bpy.context.object;driver_head.name='Rounded cartoon driver head'
driver_head.scale=(.155,.073,.053)
bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
driver_head.data.materials.append(FRAME)
for polygon in driver_head.data.polygons:polygon.use_smooth=True
organize(driver_head,club_group,club)
cube('Brushed strike face', club_group, club, (.17,-.067,-.99), (.205,.008,.054), STEEL, .004)
for i in range(3):
    tube('Clubface groove', club_group, club, (.09,-.062,-1.011+i*.018), (.25,-.062,-1.011+i*.018), .0016, WEIGHT,8)

assets = [('MiiWheelchair',chair_group), ('Dumbbell10lb',dumbbell_group), ('GolfClub',club_group)]
for name, group in assets:
    bpy.ops.object.select_all(action='DESELECT')
    for obj in group.objects:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = next(o for o in group.objects if o.type == 'EMPTY')
    bpy.ops.export_scene.gltf(filepath=str(UNITY / (name + '.glb')),
        export_format='GLB', use_selection=True, export_animations=False, export_apply=False)
    print('PROP_EXPORTED',name,len(group.objects),flush=True)

bpy.ops.wm.save_as_mainfile(filepath=str(ART/'Kinesthetic-Props.blend'))

# Gallery render uses only root translations; source geometry in the saved blend stays at origin.
chair.location.x = -1.20
dumbbell.location.x = -.28
dumbbell.location.y = -.07
dumbbell.location.z = .155
other_dumbbell = bpy.data.objects.new('Second 10 LB dumbbell · preview only',None)
bpy.context.scene.collection.objects.link(other_dumbbell)
other_dumbbell.location=(.36,.16,.155)
other_dumbbell.rotation_euler.z=.13
for source in dumbbell_group.objects:
    if source == dumbbell:continue
    copy=source.copy();copy.data=source.data
    bpy.context.scene.collection.objects.link(copy)
    copy.parent=other_dumbbell
club.location.x = 1.20
club.location.z = 1.13
club.rotation_euler.y = -.12
club.rotation_euler.z = -.12
bpy.ops.mesh.primitive_plane_add(size=200, location=(0,0,-.03))
plane = bpy.context.object;plane.name='Preview floor';plane.data.materials.append(material('Preview background','#252A2D'))

world = bpy.data.worlds.new('Preview world');world.use_nodes=True
world.node_tree.nodes['Background'].inputs['Color'].default_value=(.25,.29,.32,1)
world.node_tree.nodes['Background'].inputs['Strength'].default_value=.55
bpy.context.scene.world=world
camdata=bpy.data.cameras.new('Gallery camera');cam=bpy.data.objects.new('Gallery camera',camdata)
bpy.context.scene.collection.objects.link(cam);bpy.context.scene.camera=cam
cam.location=(3.1,-4.6,2.25);cam.rotation_euler=(Vector((0,0,.32))-cam.location).to_track_quat('-Z','Y').to_euler()
camdata.type='ORTHO';camdata.ortho_scale=3.6
for name,loc,power,size in [('Key',(-2,-3,5),550,4),('Fill',(4,-1,3),250,3),('Rim',(0,3,3),400,3)]:
    data=bpy.data.lights.new(name,'AREA');data.energy=power;data.shape='DISK';data.size=size
    obj=bpy.data.objects.new(name,data);bpy.context.scene.collection.objects.link(obj)
    obj.location=loc;obj.rotation_euler=(Vector((0,0,.3))-obj.location).to_track_quat('-Z','Y').to_euler()
scene=bpy.context.scene;scene.render.engine='CYCLES';scene.cycles.samples=24;scene.cycles.device='CPU'
scene.render.resolution_x=1400;scene.render.resolution_y=850;scene.render.resolution_percentage=100
scene.render.image_settings.file_format='PNG';scene.view_settings.view_transform='Standard'
scene.render.filepath=str(ART/'preview.png')
bpy.ops.render.render(write_still=True)
print('PROPS_COMPLETE',flush=True)
