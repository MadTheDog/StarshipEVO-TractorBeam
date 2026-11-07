#if KEEL_WEAPON_ATTACHMENTS
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using Sevo.Framework.Runtime;
using UnityEngine;
using VoxelEngine;

namespace TractorBeam
{
    internal static class TractorUpgradeVisual
    {
        private static readonly DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(MeshPayload));
        private static readonly Lazy<Dictionary<string, float[]>> colors = new Lazy<Dictionary<string, float[]>>(ReadColors);

        internal static WeaponAttachmentVisual Load(string asset, Brick template, List<Mesh> ownership)
        {
            using var stream = typeof(TractorUpgradeVisual).Assembly.GetManifestResourceStream("TractorBeam.Meshes." + asset + ".json")
                ?? throw new FileNotFoundException("Embedded Tractor upgrade mesh is missing.", asset);
            if (!(serializer.ReadObject(stream) is MeshPayload data) || data.Version != 1 ||
                data.Vertices == null || data.Vertices.Length < 3 || data.Vertices.Length > 4096 ||
                data.Normals?.Length != data.Vertices.Length || data.Uv?.Length != data.Vertices.Length ||
                data.Groups == null || data.Groups.Length == 0)
                throw new InvalidDataException("Invalid Tractor upgrade mesh channels: " + asset);

            SelectPalette(template, out int housing, out int glow);
            var vertices = Array.ConvertAll(data.Vertices, Vector3Of);
            var normals = Array.ConvertAll(data.Normals, Vector3Of);
            var uv = Array.ConvertAll(data.Uv, values => values?.Length == 2
                ? new Vector2(values[0], values[1]) : throw new InvalidDataException("Invalid UV tuple."));
            var indices = new List<int>();
            var slots = new List<int>();
            var palette = new List<VoxelVisualMaterial>();
            var paletteSlots = new Dictionary<string, int>(StringComparer.Ordinal);
            bool emitter = asset.StartsWith("recessed-optic-", StringComparison.Ordinal);
            foreach (var group in data.Groups)
            {
                if (group.Indices == null || group.Indices.Length % 3 != 0 ||
                    group.Indices.Length + indices.Count > 2048 * 3)
                    throw new InvalidDataException("Invalid Tractor upgrade triangle count.");
                if (!paletteSlots.TryGetValue(group.Material, out int slot))
                {
                    bool emissive = group.Material == "glow";
                    bool paintable = group.Material == "armor";
                    var color = paintable ? new Color32(128, 128, 128, 255)
                        : emitter ? EmitterColor(group.Material) : PaletteColor(emissive ? asset : group.Material);
                    slot = palette.Count;
                    paletteSlots.Add(group.Material, slot);
                    palette.Add(new VoxelVisualMaterial(emissive ? glow : housing,
                        color, paintable: paintable, emission: emissive ? (byte)180 : (byte)0));
                }
                foreach (int index in group.Indices)
                {
                    if ((uint)index >= vertices.Length) throw new InvalidDataException("Invalid Tractor upgrade triangle index.");
                    indices.Add(index);
                }
                for (int i = 0; i < group.Indices.Length; i += 3) slots.Add(slot);
            }

            var mesh = new Mesh { name = "Tractor " + asset + " module" };
            try
            {
                mesh.vertices = vertices;
                mesh.normals = normals;
                mesh.uv = uv;
                mesh.SetTriangles(indices, 0);
                mesh.RecalculateTangents();
                var tangents = mesh.tangents;
                // Degenerate UV islands still need a stable tangent for the native bake.
                for (int i = 0; i < tangents.Length; i++)
                {
                    var t = tangents[i];
                    var direction = new Vector3(t.x, t.y, t.z);
                    if (direction.sqrMagnitude >= 0.98f && direction.sqrMagnitude <= 1.02f &&
                        (t.w == -1f || t.w == 1f)) continue;
                    Vector3 normal = normals[i];
                    Vector3 axis = Mathf.Abs(normal.y) < 0.9f ? Vector3.up : Vector3.right;
                    direction = Vector3.Cross(axis, normal).normalized;
                    tangents[i] = new Vector4(direction.x, direction.y, direction.z, 1);
                }
                mesh.tangents = tangents;
                Color32 paint = PaletteColor("armor");
                var visual = new WeaponAttachmentVisual(mesh, slots, palette).WithPaint(paint, paint);
                ownership.Add(mesh);
                return visual;
            }
            catch
            {
                UnityEngine.Object.Destroy(mesh);
                throw;
            }
        }

        private static Color32 EmitterColor(string material) => material switch
        {
            "dark" => new Color32(33, 40, 46, 255),
            "well" => new Color32(17, 23, 28, 255),
            "glow" => new Color32(17, 138, 156, 255),
            "lens" => new Color32(26, 56, 64, 255),
            "socket" => new Color32(22, 27, 31, 255),
            _ => throw new InvalidDataException("Unknown emitter material: " + material)
        };

        private static Dictionary<string, float[]> ReadColors()
        {
            using var stream = typeof(TractorUpgradeVisual).Assembly.GetManifestResourceStream("TractorBeam.Meshes.manifest.json")
                ?? throw new FileNotFoundException("Tractor upgrade palette is missing.");
            var reader = new DataContractJsonSerializer(typeof(PaletteManifest),
                new DataContractJsonSerializerSettings { UseSimpleDictionaryFormat = true });
            return (reader.ReadObject(stream) as PaletteManifest)?.Colors
                ?? throw new InvalidDataException("Tractor upgrade palette is invalid.");
        }

        private static Color32 PaletteColor(string key)
        {
            if (!colors.Value.TryGetValue(key, out var rgb) || rgb.Length != 3 ||
                !Array.TrueForAll(rgb, value => value >= 0f && value <= 1f))
                throw new InvalidDataException("Invalid Tractor upgrade palette color: " + key);
            return new Color32((byte)Mathf.RoundToInt(rgb[0] * 255), (byte)Mathf.RoundToInt(rgb[1] * 255),
                (byte)Mathf.RoundToInt(rgb[2] * 255), 255);
        }

        [DataContract]
        private sealed class PaletteManifest
        {
            [DataMember(Name = "materialColors", IsRequired = true)] public Dictionary<string, float[]> Colors { get; set; }
        }

        private static void SelectPalette(Brick template, out int housing, out int glow)
        {
            housing = glow = -1;
            var materials = template.VoxelMaterials;
            var baked = VoxelMaterialResources.materialParamDatas;
            for (int i = 0; i < materials.Length; i++)
            {
                var material = materials[i];
                if ((uint)material.Id >= baked.Length || baked[material.Id].Id != material.Id ||
                    material.dynDataVert != DynData_Vert.none || material.dynDataPixel != DynData_Pixel.none)
                    continue;
                if (material.AlphaMode == AlphaMode.noAlpha && housing < 0) housing = i;
                if (material.AlphaMode is AlphaMode.glowPaint or AlphaMode.glowDiffuse && glow < 0) glow = i;
            }
            if (housing < 0) housing = glow;
            if (housing < 0) throw new InvalidOperationException("Stock addon has no supported baked material slot.");
            if (glow < 0) glow = housing;
        }

        private static Vector3 Vector3Of(float[] values) => values?.Length == 3
            ? new Vector3(values[0], values[1], values[2]) : throw new InvalidDataException("Invalid vertex tuple.");

        [DataContract]
        private sealed class MeshPayload
        {
            [DataMember(Name = "schemaVersion", IsRequired = true)] public int Version { get; set; }
            [DataMember(Name = "vertices", IsRequired = true)] public float[][] Vertices { get; set; }
            [DataMember(Name = "normals", IsRequired = true)] public float[][] Normals { get; set; }
            [DataMember(Name = "uv", IsRequired = true)] public float[][] Uv { get; set; }
            [DataMember(Name = "submeshes", IsRequired = true)] public MaterialGroup[] Groups { get; set; }
        }

        [DataContract]
        private sealed class MaterialGroup
        {
            [DataMember(Name = "material", IsRequired = true)] public string Material { get; set; }
            [DataMember(Name = "indices", IsRequired = true)] public int[] Indices { get; set; }
        }
    }
}
#endif
