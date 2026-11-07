# Tractor Beam upgrade mesh optimization

The five approved housings were consolidated into one visual mesh per add-on. The exported mounts use two procedural tapered cleats reconstructed from the measured stock footprint. Their coordinates match the stock foot to six decimal places in local units. The payloads include no stock emitter geometry, copied stock triangles, or extracted textures. The approved mounted scene and all previous drafts remain available in local evidence.

The optimizer removes a triangle only if all three vertices are strictly inside a different closed convex part. It also checks degeneracy and duplicate faces, preserves the approved flat polygon shading, and welds identical position, normal, and UV tuples. It retains genuine hard edges and UV seams.

| Add-on | Source → export triangles | Comparable GPU vertices | Source → export material groups |
| --- | ---: | ---: | ---: |
| Pull | 694 → 628 | 1,296 → 1,194 | 5 → 3 |
| Push | 272 → 248 | 496 → 464 | 6 → 4 |
| Slowdown | 486 → 424 | 892 → 794 | 4 → 3 |
| Shield Coupling | 406 → 340 | 760 → 654 | 4 → 3 |
| Capacity | 870 → 576 | 1,624 → 1,122 | 5 → 3 |
| **Total** | **2,728 → 2,216** | **5,068 → 4,228** | **24 → 16** |

GPU vertices in both columns use the same split-normal and planar UV layout. Raw Blender vertex records do not include the required corner splits and are therefore not comparable.

## Runtime payload

The files in `assets/upgrade-meshes` are direct Unity mesh arrays. Each JSON supplies `schemaVersion`, equal-length `vertices`, `normals`, and `uv` arrays, `submeshes` with zero-based triangle index lists and material names, and `bounds`. The mount origin is local (0, 0, 0), local +Y points out of the native slot, and the JSON indices already use native winding. The OBJ copies negate X and reverse winding for right-handed model editors. Import the JSON directly in Unity to avoid a second coordinate conversion.

UV0 is normalized planar projection. The four semantic channels are `armor`, `dark`, `copper`, and `glow`. Only `glow` is emissive by intent. Preview RGB values are recorded in the manifest. Similar dark and copper tones were consolidated into their channels, so material colors in the preview are approximations. The runtime adapter still needs to bind game materials and tune the intensity of the glow.

## Verification

The native fit was measured on Steam build 26451593 / 26w45a, Unity 6000.6.6f1. Offline validation checked finite arrays, unit normals, index ranges, nondegenerate winding and UVs, bounds, uniqueness of exported tuples, and coplanar triangle overlap. The final payloads had no detected positive-area coplanar face pairs at the audit tolerance. Ten orthographic views per add-on yielded 470,450 ray samples with no silhouette differences and a maximum first-visible surface depth difference of 0.00000215 local units. The optimized Blender file reopened with all five meshes, UV0, and custom normals.

The ray test is sampled and does not establish collision or watertightness. Existing partially intersecting decorative shells were preserved where complete occlusion could not be proved. Unity runtime rendering, collision, and glow are integration checks. The game, Keel, and live saves were not modified by this mesh pass.
