"""Change only the disposable copy's serialized PlayerSettings identity."""
import hashlib,json,pathlib,struct
ROOT=pathlib.Path(__file__).resolve().parent.parent
SOURCE=pathlib.Path(r'D:\SteamLibrary\steamapps\common\Starship EVO\Starship EVO_Data')
TARGET=ROOT/'test-install'/'SEVO'/'Starship EVO_Data'
COMPANY=b'SEVO Tractor SandboxXX'
PRODUCT=b'SEVO TB Test'

def settings_range(data):
    metadata,size,version,offset=struct.unpack_from('>4I',data)
    assert version in (21,22,23) and data[16]==0
    pos=20
    if version>=22:
        metadata,size,offset,_=struct.unpack_from('>Iqqq',data,pos);pos+=28
    assert size==len(data)
    end=data.index(b'\0',pos,min(pos+128,offset));unity=data[pos:end].decode();pos=end+1
    def read(fmt):
        nonlocal pos
        n=struct.calcsize('<'+fmt);assert pos+n<=offset
        value=struct.unpack_from('<'+fmt,data,pos)[0];pos+=n;return value
    read('i');assert not read('?'),'Expected stripped type trees'
    types=[]
    for _ in range(read('i')):
        cid=read('i');read('?');read('h')
        if cid==114:pos+=16
        pos+=16;types.append(cid)
    settings=[]
    for _ in range(read('i')):
        pos=(pos+3)&~3;read('q')
        start=read('q' if version>=22 else 'I')+offset
        length=read('I');tid=read('i')
        assert start+length<=size
        if types[tid]==129:settings.append((start,length))
    assert len(settings)==1
    return settings[0],unity

assert TARGET.resolve().is_relative_to((ROOT/'test-install').resolve())
source=SOURCE/'globalgamemanagers';target=TARGET/'globalgamemanagers'
original=source.read_bytes();assert target.read_bytes()==original
(start,size),unity=settings_range(original);patched=bytearray(original)
for old,new in [(b'Moonfire Entertainment',COMPANY),(b'Starship EVO',PRODUCT)]:
    assert len(old)==len(new)
    pattern=struct.pack('<i',len(old))+old;assert original.count(pattern)==1
    at=original.index(pattern)+4;assert start<=at and at+len(old)<=start+size
    patched[at:at+len(old)]=new
assert settings_range(patched)==settings_range(original)
target.write_bytes(patched);(TARGET/'app.info').write_bytes(COMPANY+b'\n'+PRODUCT)
assert source.read_bytes()==original
sha=lambda p:hashlib.sha256(p.read_bytes()).hexdigest().upper()
manifest=dict(company=COMPANY.decode(),product=PRODUCT.decode(),unity=unity,
    source_settings_sha256=sha(source),test_settings_sha256=sha(target),
    source_assembly_sha256=sha(SOURCE/'Managed'/'Assembly-CSharp.dll'),
    test_assembly_sha256=sha(TARGET/'Managed'/'Assembly-CSharp.dll'),
    plugin_sha256=sha(TARGET.parent/'BepInEx'/'plugins'/'Tractor Beam'/'TractorBeam.dll'))
assert manifest['source_assembly_sha256']==manifest['test_assembly_sha256']
(ROOT/'local-evidence'/'test-identity.json').write_text(json.dumps(manifest,indent=2))
print(json.dumps(manifest,indent=2))
