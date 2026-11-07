"""Optimize approved visual geometry and export direct-Unity arrays plus standard OBJ previews."""
from collections import Counter, defaultdict
from itertools import combinations
from pathlib import Path
import hashlib
import json
import math
import os
import sys

import bpy
from mathutils import Vector

ROOT=Path(__file__).resolve().parent.parent
INPUT=ROOT/'local-evidence'/'stock-weapon-meshes'/'tractor-upgrades-unified-original-housings.blend'
OUT=Path(os.environ.get('TRACTOR_MESH_OUTPUT', ROOT/'assets'/'upgrade-meshes'))
OUT.mkdir(parents=True,exist_ok=True)
MODULES={'Pull':'pull','Push':'push','Slowdown':'slowdown','Shield coupling':'shield-coupling','Capacity':'capacity'}
ORDER=('armor','dark','copper','glow')
COLORS={
 'armor':(.64,.60,.55),'dark':(.20,.225,.25),'copper':(.57,.224,.106),
 'pull':(.08,.80,.94),'push':(1.0,.57,.095),'slowdown':(1.0,.73,.18),
 'shield-coupling':(.56,.17,.83),'capacity':(.35,.82,.13)
}
EVIDENCE=INPUT.parent/'mesh-optimization'
EVIDENCE.mkdir(exist_ok=True)
bpy.ops.wm.open_mainfile(filepath=str(INPUT))

def material(name):
    if name.startswith(('Painted armor','Housing')): return 'armor'
    if name.startswith('Push front') or name.startswith('Push side'): return 'copper'
    if name.startswith(('Mount and inset','Native mounting foot','Slot liners')): return 'dark'
    if name.startswith(('Pull contact','Push emitters','Slowdown indicators','Shield field chamber','Capacity banks')): return 'glow'
    raise ValueError('Unmapped material: '+name)

def key(p): return tuple(round(x,6) for x in p)
def cross(tri): return (tri[1]-tri[0]).cross(tri[2]-tri[0])
def packed(p, digits=7): return [round(float(x),digits) for x in p]

def unity(p): return Vector((-p.x,-.5-p.y,.5-p.z))
def blender(p): return Vector((-p[0],-.5-p[1],.5-p[2]))

def generated_feet():
    # Original topology for two tapered cleats. Dimensions measured from the socket-fit study.
    for sign in (-1,1):
        p=[
            blender((x,y,sign*z))
            for y,xwide,znear,zfar in [
                (-.17521492,.125,.06667944,.15556832),
                (-.00020209,.1875,.04862388,.17362388)
            ]
            for x,z in [(-xwide,znear),(xwide,znear),(xwide,zfar),(-xwide,zfar)]
        ]
        quads=[(0,1,2,3),(4,7,6,5),(0,4,5,1),(1,5,6,2),(2,6,7,3),(3,7,4,0)]
        center=sum(p,Vector())/8
        t=[]
        for a,b,c,d in quads:
            for tri in [(p[a],p[b],p[c]),(p[a],p[c],p[d])]:
                if cross(tri).dot(tri[0]-center)<0: tri=tri[::-1]
                t.append((tri,'dark'))
        yield {'name':'reconstructed_mount_'+str(sign),'tris':t,'vertices':p}

def read_part(o):
    mesh=o.data
    mesh.calc_loop_triangles()
    # Location offsets only lay out five neighboring assemblies in the approval scene.
    assert max(abs(x) for x in o.rotation_euler)<1e-8 and all(abs(x-1)<1e-8 for x in o.scale)
    verts=[v.co.copy() for v in mesh.vertices]
    tris=[(tuple(verts[i] for i in t.vertices),material(mesh.materials[t.material_index].name))
          for t in mesh.loop_triangles]
    face_normals={tuple(sorted(key(verts[i]) for i in t.vertices)):mesh.polygons[t.polygon_index].normal.copy()
                  for t in mesh.loop_triangles}
    return {'name':o.name,'vertices':verts,'tris':tris,'face_normals':face_normals}

def is_closed(tris):
    e=Counter(tuple(sorted((key(a),key(b)))) for tri,_ in tris for a,b in zip(tri,tri[1:]+tri[:1]))
    return bool(e) and all(n==2 for n in e.values())

def orient(part):
    t=part['tris']
    if is_closed(t):
        volume=sum(a.dot(b.cross(c)) for (a,b,c),_ in t)
        if volume<0: part['tris']=[(tri[::-1],m) for tri,m in t]
    else:
        hint=next((n for prefix,n in [
            ('Inlaid side status indicator',Vector((-1,0,0))),
            ('Orange discharge slat',Vector((0,-1,0))),
            ('Upper yellow status channel',Vector((0,0,1))),
            ('Dark crown plate',Vector((0,0,1)))
        ] if part['name'].startswith(prefix)),None)
        assert hint is not None,part['name']
        part['tris']=[(tri[::-1] if cross(tri).dot(hint)<0 else tri,m) for tri,m in t]

def convex_planes(part):
    # Admit a component as an occluder only if closed and each triangle plane
    # supports its entire vertex set. Strict containment then proves a hidden face.
    t=part['tris']
    if not is_closed(t): return None
    points=part['vertices']
    planes={}
    for tri,_ in t:
        n=cross(tri)
        if n.length_squared<1e-14: return None
        n.normalize()
        h=n.dot(tri[0])
        if any(n.dot(v)>h+3e-6 for v in points): return None
        planes[(*key(n),round(h,6))]=(n,h)
    return tuple(planes.values())

def contained(tri,planes):
    return all(n.dot(v)<h-2e-5 for n,h in planes for v in tri)

def flatten(parts):
    all_faces={}
    removed=Counter()
    for p in parts:
        orient(p)
    solids=[convex_planes(p) for p in parts]
    for j,p in enumerate(parts):
        for t,m in p['tris']:
            if cross(t).length_squared<1e-14:
                removed['degenerate']+=1
                continue
            if any(i!=j and planes is not None and contained(t,planes) for i,planes in enumerate(solids)):
                removed['fully_hidden']+=1
                continue
            k=tuple(sorted(key(v) for v in t))
            if k in all_faces:
                ot,om=all_faces[k]
                assert om==m and cross(ot).dot(cross(t))>0,('conflicting exact faces',p['name'])
                removed['duplicate']+=1
                continue
            all_faces[k]=(t,m)
    return list(all_faces.values()),dict(removed),sum(x is not None for x in solids)

def bounds(points):
    return {'min':packed(min(v[i] for v in points) for i in range(3)),
            'max':packed(max(v[i] for v in points) for i in range(3))}

def arrays(faces,face_normals=None):
    # UV0 uses one deterministic planar projection per face. This is a new layout,
    # not a copied atlas. UV seams and sharp shading remain separate vertices.
    w=[unity(v) for tri,_ in faces for v in tri]
    b=bounds(w)
    vertices,normals,uv=[],[],[]
    sub=defaultdict(list)
    cache={}
    for raw,mat in faces:
        t=tuple(unity(v) for v in raw[::-1])  # inverse slot transform reflects X; fix winding
        flat=cross(t).normalized()
        # One source polygon normal across its tessellation. Keep genuine hard edges split.
        source_n=face_normals.get(tuple(sorted(key(v) for v in raw))) if face_normals else None
        n=-source_n.normalized() if source_n is not None else flat
        if n.dot(flat)<0: n=-n
        assert n.dot(flat)>.8
        axis=max(range(3),key=lambda i:abs(n[i]))
        i,j=((1,2),(2,0),(0,1))[axis]
        for p in t:
            u=(p[i]-b['min'][i])/(b['max'][i]-b['min'][i])
            v=(p[j]-b['min'][j])/(b['max'][j]-b['min'][j])
            if n[axis]<0: u=1-u
            q=packed(p); normal=packed(n); tex=packed((min(1,max(0,u)),min(1,max(0,v))))
            k=(*q,*packed(n,5),*tex)
            index=cache.get(k)
            if index is None:
                index=cache[k]=len(vertices)
                vertices.append(q)
                normals.append(normal)
                uv.append(tex)
            sub[mat].append(index)
    return dict(schemaVersion=1,vertices=vertices,normals=normals,uv=uv,
        submeshes=[dict(material=m,indices=sub[m]) for m in ORDER if sub[m]],bounds=b)

def validate(a):
    v,n,u=a['vertices'],a['normals'],a['uv']
    assert 0<len(v)==len(n)==len(u)<65536
    assert all(all(math.isfinite(x) for x in p) for p in v+n+u)
    assert all(abs(Vector(normal).length-1)<1e-5 for normal in n)
    assert all(0<=x<=1 for p in u for x in p)
    assert bounds(v)==a['bounds']
    indices=set()
    for stream in a['submeshes']:
        t=stream['indices']
        assert t and len(t)%3==0 and all(0<=i<len(v) for i in t)
        indices.update(t)
        for i,j,k in zip(t[::3],t[1::3],t[2::3]):
            assert len({i,j,k})==3
            c=cross((Vector(v[i]),Vector(v[j]),Vector(v[k])))
            assert c.length_squared>1e-14 and all(c.dot(Vector(n[q]))>0 for q in (i,j,k))
            au,av=u[i];bu,bv=u[j];cu,cv=u[k]
            assert abs((bu-au)*(cv-av)-(bv-av)*(cu-au))>1e-8
    assert len(indices)==len(v)
    assert len(set(tuple(p+n0+t) for p,n0,t in zip(v,n,u)))==len(v)

def obj_mtl(name,a):
    # OBJ uses RH winding and negates X, as in the stock UnityPy previews.
    header=['# Right-handed OBJ view: X mirrored from direct Unity payload.','mtllib '+name+'.mtl','o '+name]
    for x,y,z in a['vertices']: header.append(f'v {-x:g} {y:g} {z:g}')
    for u,v in a['uv']: header.append(f'vt {u:g} {v:g}')
    for x,y,z in a['normals']: header.append(f'vn {-x:g} {y:g} {z:g}')
    materials=[]
    for s in a['submeshes']:
        m=s['material']
        color=COLORS[name if m=='glow' else m]
        materials.extend((f'newmtl {m}','Kd '+' '.join(map(str,color)),
                          'Ke '+' '.join(map(str,color if m=='glow' else (0,0,0)))))
        header.append('usemtl '+m)
        t=s['indices']
        for i,j,k in zip(t[::3],t[1::3],t[2::3]):
            header.append('f '+' '.join(f'{q+1}/{q+1}/{q+1}' for q in (k,j,i)))
    (OUT/(name+'.obj')).write_text('\n'.join(header)+'\n',encoding='ascii')
    (OUT/(name+'.mtl')).write_text('\n'.join(materials)+'\n',encoding='ascii')

manifest={'schemaVersion':1,'sourceBlendSha256':hashlib.sha256(INPUT.read_bytes()).hexdigest(),
          'axes':'Native Unity addon local, +Y outward, slot origin at (0,0,0).',
          'feet':'Two procedural tapered cleats fitted to measured stock bounds; no copied game triangles.',
          'materialOrder':list(ORDER),'materialColors':COLORS,'modules':{}}
payloads={}
for label,name in MODULES.items():
    source=[read_part(o) for o in bpy.data.collections[label].objects if o.type=='MESH' and not o.name.startswith('Beam Weapon')]
    stock=next(p for p in source if p['name'].startswith('Add-on foot'))
    feet=list(generated_feet())
    oldmount=sorted(set(key(unity(v)) for v in stock['vertices']))
    newmount=sorted(set(key(unity(v)) for p in feet for v in p['vertices']))
    assert oldmount==newmount, (oldmount,newmount)
    parts=[p for p in source if p is not stock]+feet
    kept,removed,solids=flatten(parts)
    original=[(t,m) for p in source for t,m in p['tris']]
    norm={key:n for p in parts for key,n in p.get('face_normals',{}).items()}
    baseline=arrays(original,norm)
    a=arrays(kept,norm)
    previous_triangle_normal_count=len(arrays(kept)['vertices'])
    validate(a)
    assert a['bounds']==bounds([unity(v) for p in source for v in p['vertices']])
    path=OUT/(name+'.json')
    path.write_text(json.dumps(a,separators=(',',':'))+'\n',encoding='ascii')
    obj_mtl(name,a)
    manifest['modules'][name]=dict(sourceObjects=len(source),
        sourceVertexRecords=sum(len(p['vertices']) for p in source),
        sourceTriangles=sum(len(p['tris']) for p in source),
        sourceComparableGpuVertices=len(baseline['vertices']), priorPerTriangleNormalVertices=previous_triangle_normal_count,
        sourceMaterials=len(set(m.name for o in bpy.data.collections[label].objects
            if o.type=='MESH' and not o.name.startswith('Beam Weapon') for m in o.data.materials)),
        verifiedConvexOccluders=solids,trianglesRemoved=removed,exportObjects=1,exportVertices=len(a['vertices']),
        exportTriangles=len(kept),exportMaterials=len(a['submeshes']),bounds=a['bounds'],
        payloadSha256=hashlib.sha256(path.read_bytes()).hexdigest())
    print(name,manifest['modules'][name],flush=True)
    payloads[name]=a
(OUT/'manifest.json').write_text(json.dumps(manifest,indent=2)+'\n',encoding='utf-8')
