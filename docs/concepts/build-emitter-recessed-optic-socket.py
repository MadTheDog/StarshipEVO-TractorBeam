"""Editable visual concept using the measured stock native mount."""
import json
import sys
from pathlib import Path
import bpy
import bmesh
from mathutils import Vector
from mathutils.bvhtree import BVHTree

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'docs' / 'concepts'
STOCK = json.loads((ROOT / 'local-evidence' / 'stock-weapon-meshes' / 'manifest.json').read_text())
BOUNDS = next(m['obj_bounds'] for m in STOCK['meshes'] if m['mesh_name'] == 'pasted__polySurface8')
WIDTH = BOUNDS[0][1]
RECESS = -BOUNDS[2][1]
HEIGHT = BOUNDS[1][1]
PITCH = 1.0

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.context.preferences.filepaths.save_version = 0
scene = bpy.context.scene
scene.render.engine = 'BLENDER_WORKBENCH'
scene.render.resolution_x, scene.render.resolution_y = 1400, 1000
scene.render.resolution_percentage = 100
scene.render.image_settings.file_format = 'PNG'
scene.world = bpy.data.worlds.new('Slate studio')
scene.world.color = (.075, .095, .13)
shade = scene.display.shading
shade.light = 'STUDIO'
shade.color_type = 'MATERIAL'
shade.show_cavity = True
shade.cavity_type = 'BOTH'
shade.show_shadows = True
shade.background_type = 'WORLD'

def mat(name, rgb):
    m = bpy.data.materials.new(name)
    m.diffuse_color = (*rgb, 1)
    return m

materials = [
    mat('Armor – paintable design intent', (.65,.64,.61)),
    mat('Graphite housing', (.13,.155,.18)),
    mat('Optical well', (.065,.092,.11)),
    mat('Faint cyan status', (.065,.54,.61)),
    mat('Smoky lens', (.10,.22,.25)),
    mat('Socket backing', (.085,.105,.12)),
]
pull_colors = {
    'armor': materials[0], 'dark': materials[1], 'glow': mat('Pull cyan', (.08,.8,.94)),
}
def mesh_obj(name, coll, points, faces, slots, material_list=materials):
    mesh = bpy.data.meshes.new(name)
    mesh.from_pydata(points, [], faces)
    mesh.update()
    obj = bpy.data.objects.new(name, mesh)
    coll.objects.link(obj)
    for m in material_list:
        mesh.materials.append(m)
    for face, slot in zip(mesh.polygons, slots):
        face.material_index = slot
    bm = bmesh.new()
    bm.from_mesh(mesh)
    bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
    bm.to_mesh(mesh)
    bm.free()
    return obj

# Flat rectangular attachment strip, set in from the projecting armor cheeks.
def section(x, back, face, recess, notch):
    return [(-notch,recess),(notch,recess),(notch+.06,face),
            (x-.08,face),(x,face+.09),(x,back-.10),
            (x-.10,back),(-x+.10,back),(-x,back-.10),
            (-x,face+.09),(-x+.08,face),(-notch-.06,face)]

def rect(x, y0, y1, bevel):
    return [(-x+bevel,y0),(x-bevel,y0),(x,y0+bevel),(x,y1-bevel),
            (x-bevel,y1),(-x+bevel,y1),(-x,y1-bevel),(-x,y0+bevel)]

def rings(name, coll, origin, data, wall, end, socket=False):
    n = len(data[0][1])
    points = [(x+origin[0],y,z+origin[1]) for z,ring in data for x,y in ring]
    faces = [tuple(reversed(range(n)))]
    slots = [1]
    for j in range(len(data)-1):
        for i in range(n):
            nxt = (i+1)%n
            if socket and j == 2 and i == 0:
                # Build the socket shoulder as connected faces, with the foot clearance kept at the stock plane.
                a,b,c,d=j*n+i,j*n+nxt,(j+1)*n+nxt,(j+1)*n+i
                loops=[]
                for half, low, high, depth in [
                    (.185,.305,.686,RECESS),
                    (.172,.319,.681,RECESS-.022),
                    (.141,.333,.673,RECESS-.022),
                    (.135,.335,.669,RECESS),
                    # The central fitting sits in the gap between the two mounting cleats.
                    (.092,.447,.553,RECESS),
                    (.074,.459,.541,RECESS-.034)
                ]:
                    k=len(points)
                    points.extend((x+origin[0],depth,z+origin[1]) for x,z in
                                  [(-half,low),(half,low),(half,high),(-half,high)])
                    loops.append((k,k+1,k+2,k+3))
                outer=(a,b,c,d)
                for ring_index,inner in enumerate(loops):
                    for edge in range(4):
                        nxt_edge=(edge+1)%4
                        faces.append((outer[edge],outer[nxt_edge],inner[nxt_edge],inner[edge]))
                        slots.append(1 if ring_index==0 else 5 if ring_index==4 else 0)
                    outer=inner
                faces.append(outer)
                slots.append(0)
                continue
            faces.append((j*n+i,j*n+nxt,(j+1)*n+nxt,(j+1)*n+i))
            slots.append(wall(j,i))
    faces.append(tuple(range((len(data)-1)*n,len(data)*n)))
    slots.append(end)
    return mesh_obj(name,coll,points,faces,slots)

def optic(coll, x, bay):
    top = HEIGHT
    rings('Recessed rectangular lens cap', coll, (x,bay), [
        (.77, rect(.387,-.34,.401,.08)),
        (.825,rect(.448,-.393,.465,.11)),
        (.948,rect(.448,-.393,.465,.11)),
        (top,rect(.393,-.337,.410,.10)),
        (top,rect(.267,-.213,.297,.071)),
        (.942,rect(.245,-.191,.275,.067)),
        (.926,rect(.217,-.162,.246,.060)),
        (.922,rect(.201,-.145,.229,.054)),
    ],lambda j,i:[1,0,0,0,2,2,3][j],4)

def body(coll,x,bay,terminal):
    full=section(WIDTH,WIDTH,-.470,RECESS,.27)
    shoulder=section(.454,.455,-.431,RECESS,.27)
    foot=section(.411,.411,-.392,RECESS,.27)
    top=.79 if terminal else .9999
    rings('Native-pitch socket bay '+str(bay+1),coll,(x,bay),[
        (0,foot),(.066,shoulder),(.145,full),(.69,full),
        (.755,shoulder),(top,shoulder if terminal else foot),
    ],lambda j,i: 1 if i==0 or j in (0,4) else 0,1,socket=True)

def pull(coll,x,bay):
    data=json.loads((ROOT/'assets'/'upgrade-meshes'/'pull.json').read_text())
    points=[(x-a,-.5-b,bay+.5-c) for a,b,c in data['vertices']]
    faces=[]; slots=[]; groups=[]
    for group in data['submeshes']:
        groups.append(pull_colors[group['material']])
        idx=group['indices']
        faces.extend((idx[i],idx[i+2],idx[i+1]) for i in range(0,len(idx),3))
        slots.extend([len(groups)-1]*(len(idx)//3))
    obj=mesh_obj('Approved Pull module – native socket transform',coll,points,faces,slots,groups)
    obj['slot_center_blender']=(x,-.5,bay+.5)
    obj['stock_plane_blender']=RECESS
    return obj

for x,count,name in [(-1.22,1,'Compact'),(1.03,3,'Stretched')]:
    coll=bpy.data.collections.new(name)
    scene.collection.children.link(coll)
    for bay in range(count): body(coll,x,bay,bay==count-1)
    optic(coll,x,count-1)
    if count>1:
        obj=pull(coll,x,1)
        assert abs(max(v.co.y for v in obj.data.vertices)-(-.3247851))<2e-6
        assert max(v.co.y for v in obj.data.vertices) > RECESS
        assert abs(max(v.co.x for v in bpy.data.objects['Native-pitch socket bay 1'].data.vertices)-(-1.22+WIDTH))<1e-6

def label(name,text,xyz,size,rotation):
    data=bpy.data.curves.new(name,type='FONT')
    data.body=text
    data.align_x='CENTER'
    data.size=size
    obj=bpy.data.objects.new(name,data)
    scene.collection.objects.link(obj)
    obj.location=xyz
    obj.rotation_euler=rotation
    data.materials.append(mat(name+' neutral',(.83,.87,.90)))

focus=Vector((-.03,-.20,1.31))
camera_data=bpy.data.cameras.new('Orthographic')
camera_data.type='ORTHO'
camera_data.ortho_scale=5.35
camera=bpy.data.objects.new('Orthographic',camera_data)
scene.collection.objects.link(camera)
camera.location=focus+Vector((2.4,-9,3.9))
camera.rotation_euler=(focus-camera.location).to_track_quat('-Z','Y').to_euler()
scene.camera=camera
label('Compact label','COMPACT',(-1.22,-.69,-.26),.12,camera.rotation_euler)
label('Stretched label','STRETCHED',(1.03,-.69,-.26),.12,camera.rotation_euler)

scene['reference']='Extracted Beam Weapon, Steam build 26451593 / 26w45a.'
scene['verification']='Measured footprint and repeat pitch; real optimized Pull mesh at native mount. Concept; no in-game collision validation.'
# Export native coordinates with one index stream per semantic material.
def export_part(objects, origin_x, path, polygon_normals=False, origin_y=0):
    vertices, normals, uv, groups, seen = [], [], [], {}, {}
    for obj in objects:
        mesh=obj.data
        mesh.calc_loop_triangles()
        for tri in mesh.loop_triangles:
            n=mesh.polygons[tri.polygon_index].normal if polygon_normals else tri.normal
            normal=tuple(round(v,6 if polygon_normals else 7) for v in (-n.x,n.z,-n.y))
            axis=max(range(3),key=lambda i: abs(normal[i]))
            plane=[i for i in range(3) if i!=axis]
            group=groups.setdefault(['armor','dark','well','glow','lens','socket'][tri.material_index],[])
            for vertex in (tri.vertices[0],tri.vertices[2],tri.vertices[1]):
                co=mesh.vertices[vertex].co
                position=tuple(round(v,7) for v in (origin_x-co.x,co.z-origin_y,-co.y))
                texture=tuple(round(max(0,min(1,position[i]+(.5 if i!=1 else origin_y))),7) for i in plane)
                key=(position,normal,texture)
                idx=seen.get(key)
                if idx is None:
                    idx=len(vertices)
                    seen[key]=idx
                    vertices.append(position); normals.append(normal); uv.append(texture)
                group.append(idx)
    payload={'schemaVersion':1,'vertices':vertices,'normals':normals,'uv':uv,
             'submeshes':[{'material':name,'indices':indices} for name,indices in groups.items()],
             'bounds':{'min':[min(v[i] for v in vertices) for i in range(3)],
                       'max':[max(v[i] for v in vertices) for i in range(3)]}}
    count={'vertices':len(vertices),'triangles':sum(map(len,groups.values()))//3}
    if path is not None:
        path.write_text(json.dumps(payload,separators=(',',':'))+'\n',encoding='utf-8')
    return count

def optimize(obj):
    bm=bmesh.new()
    bm.from_mesh(obj.data)
    bm.normal_update()
    bmesh.ops.dissolve_limit(bm,angle_limit=.00001,use_dissolve_boundaries=False,
        verts=list(bm.verts),edges=list(bm.edges),delimit={'MATERIAL','NORMAL'})
    bm.to_mesh(obj.data)
    bm.free()
    obj.data.update()

out_assets=ROOT/'assets'/'emitter-meshes'
out_assets.mkdir(parents=True,exist_ok=True)
compact=[obj for obj in bpy.data.collections['Compact'].objects if obj.type=='MESH']
repeat=[obj for obj in bpy.data.collections['Stretched'].objects if obj.name.startswith('Native-pitch socket bay 1')]
assert len(compact)==2 and len(repeat)==1
parts=[('recessed-optic-host',compact,-1.22),('recessed-optic-repeat',repeat,1.03)]
before={name:export_part(objects,x,None) for name,objects,x in parts}
for obj in scene.objects:
    if obj.type=='MESH' and not obj.name.startswith('Approved Pull'):
        optimize(obj)
if '--export-modular-section' in sys.argv:
    assert all(max(abs(v.co.x+1.22), abs(v.co.z-.5), abs(v.co.y)) <= .50001
               for obj in compact for v in obj.data.vertices)
    print(export_part(compact,-1.22,out_assets/'recessed-optic-section.json',
                      polygon_normals=True,origin_y=.5))
    sys.exit(0)
after={name:export_part(objects,x,out_assets/(name+'.json'),polygon_normals=True) for name,objects,x in parts}

# Check the new raised center fitting against all five real add-on meshes.
bay=bpy.data.objects['Native-pitch socket bay 2']
bay_vertices=[v.co[:] for v in bay.data.vertices]
latch=[]
for face in bay.data.polygons:
    points=[bay_vertices[i] for i in face.vertices]
    if (all(abs(x-1.03)<.093 and 1.446<z<1.554 for x,y,z in points)
            and any(y<RECESS-.001 for x,y,z in points)):
        latch.append(tuple(face.vertices))
assert latch, 'Missing raised center fitting faces'
latch_tree=BVHTree.FromPolygons(bay_vertices,latch)
mount_checks={}
for asset in ('pull','push','slowdown','shield-coupling','capacity'):
    mesh=json.loads((ROOT/'assets'/'upgrade-meshes'/(asset+'.json')).read_text())
    points=[(1.03-a,-.5-b,1.5-c) for a,b,c in mesh['vertices']]
    faces=[tuple(g['indices'][i:i+3]) for g in mesh['submeshes'] for i in range(0,len(g['indices']),3)]
    mount_checks[asset]=len(latch_tree.overlap(BVHTree.FromPolygons(points,faces,all_triangles=True)))
    assert mount_checks[asset]==0, f'{asset} overlaps the center fitting'
report={'target':'26451593 / 26w45a','socketUnity':[0,.5,.5],'repeatPitch':1,
        'fit':'Center seat is at the stock plane; raised lip outside mounting feet and a center fitting in the gap between the cleats.',
        'centerFittingIntersectionPairs':mount_checks,'before':before,'after':after}
(out_assets/'recessed-optic-mesh-report.json').write_text(json.dumps(report,indent=2)+'\n',encoding='utf-8')
print(report)

scene.render.filepath=str(OUT/'emitter-recessed-optic-socket.png')
for screen in bpy.data.screens:
    for area in screen.areas:
        if area.type=='VIEW_3D':
            region=area.spaces.active.region_3d
            region.view_location=focus
            region.view_distance=5.1
            region.view_rotation=camera.rotation_euler.to_quaternion()
            region.view_perspective='ORTHO'
            area.spaces.active.shading.color_type='MATERIAL'
path=OUT/'emitter-recessed-optic-socket.blend'
bpy.ops.wm.save_as_mainfile(filepath=str(path),compress=True)
bpy.ops.render.render(write_still=True)
bpy.ops.wm.open_mainfile(filepath=str(path))
assert len([o for o in bpy.context.scene.objects if o.type=='MESH'])==7
print('Verified saved scene: 7 meshes; native stock envelope, one-unit repeat pitch, real Pull module.')
