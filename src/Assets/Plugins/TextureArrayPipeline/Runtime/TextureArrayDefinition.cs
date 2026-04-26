using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace TextureArrayPipeline.Runtime
{
    /// <summary>
    /// Stores the source texture GUIDs and build settings for a <see cref="Texture2DArray"/> asset.
    /// </summary>
    /// <remarks>
    /// Created with <see cref="HideFlags.DontSaveInBuild"/> so editor-only data never enters the
    /// player build. The generated <see cref="Texture2DArray"/> is embedded as a sub-asset and
    /// does not carry that flag — it is included in builds when referenced by a material.
    /// </remarks>
    [CreateAssetMenu(fileName = "TextureArrayDefinition", menuName = "TextureArrayDefinition")]
    public class TextureArrayDefinition : ScriptableObject
    {
        [SerializeField]
        private List<string> _textureGuids = new();

        [SerializeField]
        private TextureArrayBuildMode _buildMode = TextureArrayBuildMode.Quick;

        [SerializeField]
        private TextureArrayBuildSettings _settings = new();

        [SerializeField]
        private long _lastBuildTimeTicks;

        public IReadOnlyList<string> TextureGuids => _textureGuids;
        public TextureArrayBuildMode BuildMode => _buildMode;
        public TextureArrayBuildSettings Settings => _settings;
        public long LastBuildTimeTicks => _lastBuildTimeTicks;

#if UNITY_EDITOR
        public void RecordBuildTime()
        {
            _lastBuildTimeTicks = DateTime.UtcNow.Ticks;
            UnityEditor.EditorUtility.SetDirty(this);
        }
#endif
    }

    public enum TextureArrayBuildMode
    {
        /// <summary>Choose a target resolution; format and mipmaps are detected automatically.</summary>
        Quick,

        /// <summary>Full manual control over all array parameters.</summary>
        Advanced,
    }

    [Serializable]
    public class TextureArrayBuildSettings
    {
        public QuickResolutionPreset QuickTargetResolution = QuickResolutionPreset.Standard512;
        public int AdvancedWidth = 512;
        public int AdvancedHeight = 512;

        /// <summary>
        /// When set, all source textures are reimported to this format before building.
        /// <see cref="GraphicsFormat.None"/> auto-detects from the first source texture.
        /// </summary>
        public GraphicsFormat AdvancedFormat = GraphicsFormat.None;

        public bool GenerateMipmaps = true;
        public FilterMode FilterMode = FilterMode.Trilinear;
        public TextureWrapMode WrapMode = TextureWrapMode.Repeat;
        public bool IsNormalMap;
        public bool AutoFixNormalMapImport = true;

        public int ResolvedWidth(TextureArrayBuildMode mode)
        {
            return mode == TextureArrayBuildMode.Quick ? (int)QuickTargetResolution : AdvancedWidth;
        }

        public int ResolvedHeight(TextureArrayBuildMode mode)
        {
            return mode == TextureArrayBuildMode.Quick ? (int)QuickTargetResolution : AdvancedHeight;
        }
    }

    public enum QuickResolutionPreset
    {
        Low256 = 256,
        Standard512 = 512,
        High1024 = 1024,
        Ultra2048 = 2048,
    }
}