using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TextureArrayPipeline.Runtime;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace TextureArrayPipeline
{
    public static class TextureArrayBuilder
    {
        public static Texture2DArray Build(TextureArrayDefinition definition)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            List<Texture2D> textures = ResolveTextures(definition.TextureGuids, out List<string> missing);

            if (missing.Count > 0)
            {
                Debug.LogError(
                    $"[TextureArrayPipeline] {missing.Count} GUID(s) could not be resolved:\n" +
                    string.Join("\n", missing), definition
                );
                return null;
            }

            if (textures.Count == 0)
            {
                Debug.LogError("[TextureArrayPipeline] No textures assigned.", definition);
                return null;
            }

            TextureArrayBuildSettings s = definition.Settings;
            int targetW = s.ResolvedWidth(definition.BuildMode);
            int targetH = s.ResolvedHeight(definition.BuildMode);

            if (!NormalizeImporters(textures, targetW, targetH, s))
                return null;

            textures = ResolveTextures(definition.TextureGuids, out _);
            GraphicsFormat? forceFormat = s.AdvancedFormat != GraphicsFormat.None ? s.AdvancedFormat : null;

            if (NormalizeCompression(textures, forceFormat, out bool compressionChanged) && compressionChanged)
                textures = ResolveTextures(definition.TextureGuids, out _);

            GraphicsFormat arrayFormat = ResolveFormat(textures, s);
            if (arrayFormat == GraphicsFormat.None)
            {
                Debug.LogError("[TextureArrayPipeline] Could not determine a valid GraphicsFormat.", definition);
                return null;
            }

            if (s.AdvancedFormat == GraphicsFormat.None && !ValidateFormats(textures, arrayFormat, definition))
                return null;

            if (s.GenerateMipmaps && !ValidateMipmaps(textures, definition))
                return null;

            if (SystemInfo.copyTextureSupport == CopyTextureSupport.None)
            {
                Debug.LogError("[TextureArrayPipeline] CopyTexture is not supported on this platform.", definition);
                return null;
            }

            Texture2DArray array = BuildArray(textures, targetW, targetH, arrayFormat, s, definition.name);
            if (array == null) return null;

            EmbedSubAsset(definition, array);
            definition.RecordBuildTime();
            AssetDatabase.SaveAssets();
            return array;
        }

        public static bool IsStale(TextureArrayDefinition definition)
        {
            if (definition == null) return false;

            string soPath = AssetDatabase.GetAssetPath(definition);
            bool hasSubAsset = AssetDatabase.LoadAllAssetsAtPath(soPath)
                .Any(o => o is Texture2DArray && !AssetDatabase.IsMainAsset(o));

            if (!hasSubAsset || definition.LastBuildTimeTicks == 0)
                return true;

            long lastBuild = definition.LastBuildTimeTicks;
            foreach (string guid in definition.TextureGuids)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(assetPath)) return true;

                string fullPath = Path.GetFullPath(assetPath);
                if (!File.Exists(fullPath) || new FileInfo(fullPath).LastWriteTimeUtc.Ticks > lastBuild)
                    return true;
            }

            return false;
        }

        public static Texture2DArray GetSubAsset(TextureArrayDefinition definition)
        {
            if (definition == null) return null;
            return AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GetAssetPath(definition))
                .OfType<Texture2DArray>()
                .FirstOrDefault(o => !AssetDatabase.IsMainAsset(o));
        }

        private static List<Texture2D> ResolveTextures(IReadOnlyList<string> guids, out List<string> missing)
        {
            var textures = new List<Texture2D>(guids.Count);
            missing = new List<string>();

            foreach (string guid in guids)
            {
                if (string.IsNullOrEmpty(guid)) continue;

                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(path))
                {
                    missing.Add($"<unknown> ({guid})");
                    continue;
                }

                Texture2D tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                if (tex == null)
                {
                    missing.Add(path);
                    continue;
                }

                textures.Add(tex);
            }

            return textures;
        }

        private static bool NormalizeImporters(
            List<Texture2D> textures,
            int targetW,
            int targetH,
            TextureArrayBuildSettings settings
        )
        {
            bool anyChanged = false;

            foreach (Texture2D tex in textures)
            {
                string path = AssetDatabase.GetAssetPath(tex);
                if (string.IsNullOrEmpty(path)) continue;
                if (AssetImporter.GetAtPath(path) is not TextureImporter importer) continue;

                bool changed = false;

                bool isNormalMap = settings.IsNormalMap;
                bool autoFixEnabled = settings.AutoFixNormalMapImport;
                bool isAlreadyNormalMap = importer.textureType == TextureImporterType.NormalMap;

                if (isNormalMap && autoFixEnabled && !isAlreadyNormalMap)
                {
                    importer.textureType = TextureImporterType.NormalMap;
                    changed = true;
                }

                int neededSize = Mathf.Max(targetW, targetH);
                if (importer.maxTextureSize != neededSize)
                {
                    importer.maxTextureSize = neededSize;
                    changed = true;
                }

                if (!changed) continue;
                importer.SaveAndReimport();
                anyChanged = true;
            }

            if (anyChanged) AssetDatabase.Refresh();
            return true;
        }

        /// <summary>
        /// Unifies compression across all textures.
        /// <c>forceFormat = null</c> picks the highest-quality format already present (Quick Mode).
        /// <c>forceFormat</c> set enforces that exact format on every texture (Advanced Mode).
        /// </summary>
        private static bool NormalizeCompression(
            List<Texture2D> textures,
            GraphicsFormat? forceFormat,
            out bool anyChanged
        )
        {
            anyChanged = false;

            GraphicsFormat target = forceFormat ?? PickBestFormat(textures);
            TextureImporterFormat importerFmt = ToImporterFormat(target);

            if (importerFmt == TextureImporterFormat.Automatic)
                return true;

            foreach (Texture2D tex in textures)
            {
                if (tex.graphicsFormat == target)
                    continue;

                string path = AssetDatabase.GetAssetPath(tex);
                if (string.IsNullOrEmpty(path) || AssetImporter.GetAtPath(path) is not TextureImporter importer)
                    continue;

                TextureImporterPlatformSettings ps = importer.GetDefaultPlatformTextureSettings();
                ps.format = importerFmt;
                importer.SetPlatformTextureSettings(ps);

                Debug.Log(
                    $"[TextureArrayPipeline] '{tex.name}': {tex.graphicsFormat} → {importerFmt} " +
                    $"({(forceFormat.HasValue ? "enforced" : "auto-normalized")})", tex
                );

                importer.SaveAndReimport();
                anyChanged = true;
            }

            if (anyChanged) AssetDatabase.Refresh();
            return true;
        }

        private static GraphicsFormat PickBestFormat(List<Texture2D> textures)
        {
            GraphicsFormat best = textures[0].graphicsFormat;
            uint bestBlock = GraphicsFormatUtility.GetBlockSize(best);

            foreach (Texture2D tex in textures)
            {
                uint block = GraphicsFormatUtility.GetBlockSize(tex.graphicsFormat);
                if (block > bestBlock)
                {
                    bestBlock = block;
                    best = tex.graphicsFormat;
                }
            }

            return best;
        }

        private static TextureImporterFormat ToImporterFormat(GraphicsFormat gf) => gf switch
        {
            GraphicsFormat.RGBA_DXT1_SRGB or GraphicsFormat.RGBA_DXT1_UNorm => TextureImporterFormat.DXT1,
            GraphicsFormat.RGBA_DXT5_SRGB or GraphicsFormat.RGBA_DXT5_UNorm => TextureImporterFormat.DXT5,
            GraphicsFormat.RGBA_BC7_SRGB or GraphicsFormat.RGBA_BC7_UNorm => TextureImporterFormat.BC7,
            GraphicsFormat.R_BC4_UNorm or GraphicsFormat.R_BC4_SNorm => TextureImporterFormat.BC4,
            GraphicsFormat.RG_BC5_UNorm or GraphicsFormat.RG_BC5_SNorm => TextureImporterFormat.BC5,
            GraphicsFormat.RGB_BC6H_SFloat or GraphicsFormat.RGB_BC6H_UFloat => TextureImporterFormat.BC6H,
            GraphicsFormat.R8G8B8A8_SRGB or GraphicsFormat.R8G8B8A8_UNorm => TextureImporterFormat.RGBA32,
            GraphicsFormat.R8G8B8_SRGB or GraphicsFormat.R8G8B8_UNorm => TextureImporterFormat.RGB24,
            GraphicsFormat.R8_UNorm => TextureImporterFormat.R8,
            GraphicsFormat.R16_UNorm => TextureImporterFormat.R16,
            GraphicsFormat.R8G8_SRGB or GraphicsFormat.R8G8_UNorm => TextureImporterFormat.RG16,
            GraphicsFormat.R16G16B16A16_UNorm => TextureImporterFormat.RGBA64,
            _ => TextureImporterFormat.Automatic,
        };

        private static GraphicsFormat ResolveFormat(List<Texture2D> textures, TextureArrayBuildSettings s)
        {
            if (s.AdvancedFormat != GraphicsFormat.None)
                return s.AdvancedFormat;

            return textures.Count > 0 ? textures[0].graphicsFormat : GraphicsFormat.None;
        }

        private static bool ValidateFormats(List<Texture2D> textures, GraphicsFormat expected, Object context)
        {
            foreach (Texture2D tex in textures)
            {
                if (tex.graphicsFormat == expected)
                    continue;

                Debug.LogError(
                    $"[TextureArrayPipeline] Format mismatch: '{tex.name}' has {tex.graphicsFormat}, " +
                    $"expected {expected}.\n" +
                    "Tip: switch to Advanced Mode and set an explicit format, or make all " +
                    "source textures use the same compression.", context
                );
                return false;
            }

            return true;
        }

        private static bool ValidateMipmaps(List<Texture2D> textures, Object context)
        {
            foreach (Texture2D tex in textures)
            {
                if (tex.mipmapCount > 1)
                    continue;

                Debug.LogError(
                    $"[TextureArrayPipeline] '{tex.name}' has no mip chain. " +
                    "Enable 'Generate Mip Maps' in its import settings, or disable mipmaps.", context
                );
                return false;
            }

            return true;
        }

        private static Texture2DArray BuildArray(
            List<Texture2D> textures,
            int targetW,
            int targetH,
            GraphicsFormat format,
            TextureArrayBuildSettings settings,
            string assetName
        )
        {
            bool needsScaling = textures.Any(t =>
                t.width != targetW || t.height != targetH || t.graphicsFormat != format
            );

            return needsScaling
                ? BuildArrayWithScaling(textures, targetW, targetH, format, settings, assetName)
                : BuildArrayFastPath(textures, targetW, targetH, format, settings, assetName);
        }

        private static Texture2DArray BuildArrayFastPath(
            List<Texture2D> textures,
            int targetW,
            int targetH,
            GraphicsFormat format,
            TextureArrayBuildSettings settings,
            string assetName)
        {
            var flags = settings.GenerateMipmaps ? TextureCreationFlags.MipChain : TextureCreationFlags.None;
            var array = new Texture2DArray(targetW, targetH, textures.Count, format, flags)
            {
                wrapMode = settings.WrapMode,
                filterMode = settings.FilterMode,
                name = assetName,
            };

            try
            {
                for (int i = 0; i < textures.Count; i++)
                {
                    Texture2D src = textures[i];
                    EditorUtility.DisplayProgressBar("Building Texture Array",
                        $"Slice {i + 1}/{textures.Count}: {src.name}", (float)i / textures.Count
                    );

                    int mipCount = settings.GenerateMipmaps
                        ? Mathf.Min(array.mipmapCount, src.mipmapCount)
                        : 1;

                    for (int mip = 0; mip < mipCount; mip++)
                        Graphics.CopyTexture(src, 0, mip, array, i, mip);
                }

                array.Apply(false, false);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            return array;
        }

        private static Texture2DArray BuildArrayWithScaling(
            List<Texture2D> textures,
            int targetW,
            int targetH,
            GraphicsFormat format,
            TextureArrayBuildSettings settings,
            string assetName
        )
        {
            bool isSrgb = GraphicsFormatUtility.IsSRGBFormat(format);
            RenderTextureReadWrite rwMode = isSrgb ? RenderTextureReadWrite.sRGB : RenderTextureReadWrite.Linear;
            BlitFormat blit = BlitFormatMap.GetValueOrDefault(format, BlitFormatFallback);

            var flags = settings.GenerateMipmaps ? TextureCreationFlags.MipChain : TextureCreationFlags.None;
            var array = new Texture2DArray(targetW, targetH, textures.Count, format, flags)
            {
                wrapMode = settings.WrapMode,
                filterMode = settings.FilterMode,
                name = assetName,
            };

            RenderTexture prevActive = RenderTexture.active;
            try
            {
                for (int i = 0; i < textures.Count; i++)
                {
                    Texture2D src = textures[i];
                    EditorUtility.DisplayProgressBar("Building Texture Array (scaling)",
                        $"Slice {i + 1}/{textures.Count}: {src.name}", (float)i / textures.Count
                    );

                    RenderTexture rt = RenderTexture.GetTemporary(targetW, targetH, 0, blit.Rt, rwMode);
                    Graphics.Blit(src, rt);
                    RenderTexture.active = rt;

                    var temp = new Texture2D(
                        targetW,
                        targetH,
                        blit.Tex,
                        mipChain: settings.GenerateMipmaps,
                        linear: !isSrgb
                    );

                    temp.ReadPixels(new Rect(0, 0, targetW, targetH), 0, 0);
                    temp.Apply(updateMipmaps: settings.GenerateMipmaps);

                    RenderTexture.active = null;
                    RenderTexture.ReleaseTemporary(rt);

                    if (blit.Compress)
                        EditorUtility.CompressTexture(temp, blit.CompressFmt, TextureCompressionQuality.Normal);

                    GraphicsFormat actualFmt = temp.graphicsFormat;
                    if (actualFmt != format)
                    {
                        Object.DestroyImmediate(temp);
                        Debug.LogError(
                            $"[TextureArrayPipeline] Cannot produce {format} for '{src.name}' " +
                            $"(ended up as {actualFmt}). " +
                            "This format is not writable via ReadPixels on this platform.", null);
                        return null;
                    }

                    int mipCount = settings.GenerateMipmaps
                        ? Mathf.Min(array.mipmapCount, temp.mipmapCount)
                        : 1;

                    for (int mip = 0; mip < mipCount; mip++)
                        Graphics.CopyTexture(temp, 0, mip, array, i, mip);

                    Object.DestroyImmediate(temp);
                }

                array.Apply(false, false);
            }
            finally
            {
                RenderTexture.active = prevActive;
                EditorUtility.ClearProgressBar();
            }

            return array;
        }

        private static readonly BlitFormat BlitFormatFallback = new(RenderTextureFormat.ARGB32, TextureFormat.RGBA32);

        private static readonly Dictionary<GraphicsFormat, BlitFormat> BlitFormatMap = new()
        {
            [GraphicsFormat.RGBA_DXT1_SRGB] = new(
                RenderTextureFormat.ARGB32,
                TextureFormat.RGBA32,
                TextureFormat.DXT1
            ),
            [GraphicsFormat.RGBA_DXT1_UNorm] = new(
                RenderTextureFormat.ARGB32,
                TextureFormat.RGBA32,
                TextureFormat.DXT1
            ),
            [GraphicsFormat.RGBA_DXT5_SRGB] = new(
                RenderTextureFormat.ARGB32,
                TextureFormat.RGBA32,
                TextureFormat.DXT5
            ),
            [GraphicsFormat.RGBA_DXT5_UNorm] = new(
                RenderTextureFormat.ARGB32,
                TextureFormat.RGBA32,
                TextureFormat.DXT5
            ),
            [GraphicsFormat.RGBA_BC7_SRGB] = new(
                RenderTextureFormat.ARGB32,
                TextureFormat.RGBA32,
                TextureFormat.BC7
            ),
            [GraphicsFormat.RGBA_BC7_UNorm] = new(
                RenderTextureFormat.ARGB32,
                TextureFormat.RGBA32,
                TextureFormat.BC7
            ),
            [GraphicsFormat.R_BC4_UNorm] = new(
                RenderTextureFormat.ARGB32,
                TextureFormat.RGBA32,
                TextureFormat.BC4
            ),
            [GraphicsFormat.R_BC4_SNorm] = new(
                RenderTextureFormat.ARGB32,
                TextureFormat.RGBA32,
                TextureFormat.BC4
            ),
            [GraphicsFormat.RG_BC5_UNorm] = new(
                RenderTextureFormat.ARGB32,
                TextureFormat.RGBA32,
                TextureFormat.BC5
            ),
            [GraphicsFormat.RG_BC5_SNorm] = new(
                RenderTextureFormat.ARGB32,
                TextureFormat.RGBA32,
                TextureFormat.BC5
            ),
            [GraphicsFormat.RGB_BC6H_SFloat] = new(
                RenderTextureFormat.ARGBHalf,
                TextureFormat.RGBAHalf,
                TextureFormat.BC6H
            ),
            [GraphicsFormat.RGB_BC6H_UFloat] = new(
                RenderTextureFormat.ARGBHalf,
                TextureFormat.RGBAHalf,
                TextureFormat.BC6H
            ),

            // 8-bit uncompressed
            [GraphicsFormat.R8G8B8A8_SRGB] = new(RenderTextureFormat.ARGB32, TextureFormat.RGBA32),
            [GraphicsFormat.R8G8B8A8_UNorm] = new(RenderTextureFormat.ARGB32, TextureFormat.RGBA32),
            [GraphicsFormat.R8G8B8_SRGB] = new(RenderTextureFormat.ARGB32, TextureFormat.RGB24),
            [GraphicsFormat.R8G8B8_UNorm] = new(RenderTextureFormat.ARGB32, TextureFormat.RGB24),
            [GraphicsFormat.B8G8R8A8_SRGB] = new(RenderTextureFormat.ARGB32, TextureFormat.BGRA32),
            [GraphicsFormat.B8G8R8A8_UNorm] = new(RenderTextureFormat.ARGB32, TextureFormat.BGRA32),
            [GraphicsFormat.R8_UNorm] = new(RenderTextureFormat.R8, TextureFormat.R8),
            [GraphicsFormat.R8G8_UNorm] = new(RenderTextureFormat.RG16, TextureFormat.RG16),
            [GraphicsFormat.R8G8_SRGB] = new(RenderTextureFormat.RG16, TextureFormat.RG16),

            // 16-bit normalized
            [GraphicsFormat.R16_UNorm] = new(RenderTextureFormat.R16, TextureFormat.R16),
            [GraphicsFormat.R16G16_UNorm] = new(RenderTextureFormat.RGHalf, TextureFormat.RG32),
            [GraphicsFormat.R16G16B16A16_UNorm] = new(RenderTextureFormat.ARGBHalf, TextureFormat.RGBA64),

            // 16-bit float
            [GraphicsFormat.R16_SFloat] = new(RenderTextureFormat.RHalf, TextureFormat.RHalf),
            [GraphicsFormat.R16G16_SFloat] = new(RenderTextureFormat.RGHalf, TextureFormat.RGHalf),
            [GraphicsFormat.R16G16B16A16_SFloat] = new(RenderTextureFormat.ARGBHalf, TextureFormat.RGBAHalf),

            // 32-bit float
            [GraphicsFormat.R32_SFloat] = new(RenderTextureFormat.RFloat, TextureFormat.RFloat),
            [GraphicsFormat.R32G32_SFloat] = new(RenderTextureFormat.RGFloat, TextureFormat.RGFloat),
            [GraphicsFormat.R32G32B32A32_SFloat] = new(RenderTextureFormat.ARGBFloat, TextureFormat.RGBAFloat),
        };

        private readonly struct BlitFormat
        {
            public readonly RenderTextureFormat Rt;
            public readonly TextureFormat Tex;
            public readonly bool Compress;
            public readonly TextureFormat CompressFmt;

            public BlitFormat(RenderTextureFormat rt, TextureFormat tex, TextureFormat compressFmt)
            {
                Rt = rt;
                Tex = tex;
                Compress = true;
                CompressFmt = compressFmt;
            }

            public BlitFormat(RenderTextureFormat rt, TextureFormat tex)
            {
                Rt = rt;
                Tex = tex;
                Compress = false;
                CompressFmt = default;
            }
        }

        private static void EmbedSubAsset(TextureArrayDefinition definition, Texture2DArray newArray)
        {
            string soPath = AssetDatabase.GetAssetPath(definition);

            foreach (Object obj in AssetDatabase.LoadAllAssetsAtPath(soPath))
            {
                if (obj is not Texture2DArray existing || AssetDatabase.IsMainAsset(obj))
                    continue;

                AssetDatabase.RemoveObjectFromAsset(existing);
                Object.DestroyImmediate(existing, true);
            }

            AssetDatabase.AddObjectToAsset(newArray, definition);
            EditorUtility.SetDirty(definition);
            AssetDatabase.ImportAsset(soPath, ImportAssetOptions.ForceUpdate);

            Debug.Log(
                $"[TextureArrayPipeline] Built '{newArray.name}' — " +
                $"{newArray.depth} slice(s), {newArray.width}×{newArray.height}, {newArray.graphicsFormat}.",
                definition
            );
        }
    }
}