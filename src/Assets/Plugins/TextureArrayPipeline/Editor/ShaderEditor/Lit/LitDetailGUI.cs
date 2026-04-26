using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace TextureArrayPipeline.ShaderEditor.Lit
{
    /// <summary>Detail-map shader GUI utilities for the LitArray shader.</summary>
    public static class LitDetailGUI
    {
        private static readonly int DetailAlbedoMapProperty = Shader.PropertyToID("_DetailAlbedoMap");
        private static readonly int DetailNormalMapProperty = Shader.PropertyToID("_DetailNormalMap");
        private static readonly int DetailAlbedoMapScaleProperty = Shader.PropertyToID("_DetailAlbedoMapScale");

        public static class Styles
        {
            public static readonly GUIContent DetailInputs = EditorGUIUtility.TrTextContent(
                "Detail Inputs",
                "These settings define the surface details by tiling and overlaying additional maps on the surface."
            );

            public static readonly GUIContent DetailMaskText = EditorGUIUtility.TrTextContent(
                "Mask",
                "Select a mask for the Detail map. The mask uses the alpha channel of the selected texture. The Tiling and Offset settings have no effect on the mask."
            );

            public static readonly GUIContent DetailAlbedoMapText = EditorGUIUtility.TrTextContent(
                "Base Map",
                "Select the surface detail texture.The alpha of your texture determines surface hue and intensity."
            );

            public static readonly GUIContent DetailNormalMapText = EditorGUIUtility.TrTextContent(
                "Normal Map",
                "Designates a Normal Map to create the illusion of bumps and dents in the details of this Material's surface."
            );

            public static readonly GUIContent DetailAlbedoMapScaleInfo = EditorGUIUtility.TrTextContent(
                "Setting the scaling factor to a value other than 1 results in a less performant shader variant."
            );

            public static readonly GUIContent DetailAlbedoMapFormatError = EditorGUIUtility.TrTextContent(
                "This texture is not in linear space."
            );
        }

        public struct LitProperties
        {
            public readonly MaterialProperty DetailMask;
            public readonly MaterialProperty DetailAlbedoMapScale;
            public readonly MaterialProperty DetailAlbedoMap;
            public readonly MaterialProperty DetailNormalMapScale;
            public readonly MaterialProperty DetailNormalMap;

            public LitProperties(MaterialProperty[] properties)
            {
                DetailMask = BaseShaderGUI.FindProperty("_DetailMask", properties, false);
                DetailAlbedoMapScale = BaseShaderGUI.FindProperty("_DetailAlbedoMapScale", properties, false);
                DetailAlbedoMap = BaseShaderGUI.FindProperty("_DetailAlbedoMap", properties, false);
                DetailNormalMapScale = BaseShaderGUI.FindProperty("_DetailNormalMapScale", properties, false);
                DetailNormalMap = BaseShaderGUI.FindProperty("_DetailNormalMap", properties, false);
            }
        }

        public static void DoDetailArea(LitProperties properties, MaterialEditor materialEditor)
        {
            materialEditor.TexturePropertySingleLine(Styles.DetailMaskText, properties.DetailMask);
            materialEditor.TexturePropertySingleLine(
                Styles.DetailAlbedoMapText,
                properties.DetailAlbedoMap,
                properties.DetailAlbedoMap.textureValue != null ? properties.DetailAlbedoMapScale : null
            );

            if (!Mathf.Approximately(properties.DetailAlbedoMapScale.floatValue, 1.0f))
            {
                EditorGUILayout.HelpBox(Styles.DetailAlbedoMapScaleInfo.text, MessageType.Info, true);
            }

            var detailAlbedoTexture = properties.DetailAlbedoMap.textureValue as Texture2D;
            if (detailAlbedoTexture != null && GraphicsFormatUtility.IsSRGBFormat(detailAlbedoTexture.graphicsFormat))
            {
                EditorGUILayout.HelpBox(Styles.DetailAlbedoMapFormatError.text, MessageType.Warning, true);
            }

            materialEditor.TexturePropertySingleLine(
                Styles.DetailNormalMapText,
                properties.DetailNormalMap,
                properties.DetailNormalMap.textureValue != null ? properties.DetailNormalMapScale : null
            );

            materialEditor.TextureScaleOffsetProperty(properties.DetailAlbedoMap);
        }

        public static void SetMaterialKeywords(Material material)
        {
            bool hasDetailAlbedoMap = material.HasProperty(DetailAlbedoMapProperty);
            bool hasDetailNormalMap = material.HasProperty(DetailNormalMapProperty);
            bool hasDetailAlbedoMapScale = material.HasProperty(DetailAlbedoMapScaleProperty);

            if (!hasDetailAlbedoMap || !hasDetailNormalMap || !hasDetailAlbedoMapScale)
                return;

            bool isScaled = !Mathf.Approximately(material.GetFloat(DetailAlbedoMapScaleProperty), 1.0f);
            bool hasDetailMap = material.GetTexture(DetailAlbedoMapProperty) ||
                                material.GetTexture(DetailNormalMapProperty);

            CoreUtils.SetKeyword(material, "_DETAIL_MULX2", !isScaled && hasDetailMap);
            CoreUtils.SetKeyword(material, "_DETAIL_SCALED", isScaled && hasDetailMap);
        }
    }
}