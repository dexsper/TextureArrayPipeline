using System;
using TextureArrayPipeline.Runtime;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEditor.Rendering.Universal.ShaderGUI;
using UnityEngine;

namespace TextureArrayPipeline.ShaderEditor.Lit
{
    public class LitEditor : BaseShaderGUI
    {
        private static readonly string[] WorkflowModeNames = Enum.GetNames(typeof(LitGUI.WorkflowMode));

        private LitGUI.LitProperties _litProperties;
        private LitDetailGUI.LitProperties _litDetailProperties;
        private MaterialProperty _baseMapArrayProp;

        public override void FillAdditionalFoldouts(MaterialHeaderScopeList materialScopesList)
        {
            materialScopesList.RegisterHeaderScope(
                LitDetailGUI.Styles.DetailInputs,
                Expandable.Details,
                _ => LitDetailGUI.DoDetailArea(_litDetailProperties, materialEditor)
            );
        }

        public override void FindProperties(MaterialProperty[] properties)
        {
            base.FindProperties(properties);
            _litProperties = new LitGUI.LitProperties(properties);
            _litDetailProperties = new LitDetailGUI.LitProperties(properties);
            _baseMapArrayProp = FindProperty("_BaseMapArray", properties, false);
        }

        public override void ValidateMaterial(Material material)
        {
            SetMaterialKeywords(material, LitGUI.SetMaterialKeywords, LitDetailGUI.SetMaterialKeywords);
        }

        public override void DrawSurfaceOptions(Material material)
        {
            EditorGUIUtility.labelWidth = 0f;

            if (_litProperties.workflowMode != null)
                DoPopup(LitGUI.Styles.workflowModeText, _litProperties.workflowMode, WorkflowModeNames);

            base.DrawSurfaceOptions(material);
        }

        public override void DrawSurfaceInputs(Material material)
        {
            if (_baseMapArrayProp != null && baseColorProp != null)
            {
                materialEditor.TexturePropertySingleLine(
                    ArrayShaderStyles.BaseMapArrayText,
                    _baseMapArrayProp,
                    baseColorProp
                );
            }

            LitGUI.Inputs(_litProperties, materialEditor, material);
            DrawEmissionProperties(material, true);
            DrawTileOffset(materialEditor, _baseMapArrayProp);
        }

        public override void DrawAdvancedOptions(Material material)
        {
            if (_litProperties is { reflections: not null, highlights: not null })
            {
                materialEditor.ShaderProperty(_litProperties.highlights, LitGUI.Styles.highlightsText);
                materialEditor.ShaderProperty(_litProperties.reflections, LitGUI.Styles.reflectionsText);
            }

            base.DrawAdvancedOptions(material);
        }

        public override void AssignNewShaderToMaterial(Material material, Shader oldShader, Shader newShader)
        {
            if (material == null)
                throw new ArgumentNullException(nameof(material));

            if (material.HasProperty(ShaderProps.Emission))
                material.SetColor(ShaderProps.EmissionColor, material.GetColor(ShaderProps.Emission));

            base.AssignNewShaderToMaterial(material, oldShader, newShader);

            if (oldShader == null || !oldShader.name.Contains("Legacy Shaders/"))
            {
                SetupMaterialBlendMode(material);
                return;
            }

            SurfaceType surfaceType = SurfaceType.Opaque;
            BlendMode blendMode = BlendMode.Alpha;

            if (oldShader.name.Contains("/Transparent/Cutout/"))
            {
                surfaceType = SurfaceType.Opaque;
                material.SetFloat(ShaderProps.AlphaClip, 1);
            }
            else if (oldShader.name.Contains("/Transparent/"))
            {
                surfaceType = SurfaceType.Transparent;
                blendMode = BlendMode.Alpha;
            }

            material.SetFloat(ShaderProps.Blend, (float)blendMode);
            material.SetFloat(ShaderProps.Surface, (float)surfaceType);

            if (surfaceType == SurfaceType.Opaque)
                material.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
            else
                material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");

            bool isSpecular = oldShader.name.Equals("Standard (Specular setup)");
            material.SetFloat(
                ShaderProps.WorkflowMode,
                (float)(isSpecular ? LitGUI.WorkflowMode.Specular : LitGUI.WorkflowMode.Metallic)
            );

            var sourceTexture = material.GetTexture(
                isSpecular ? ShaderProps.SpecGlossMap : ShaderProps.MetallicGlossMap
            );

            if (sourceTexture != null)
                material.SetTexture(ShaderProps.MetallicSpecGlossMap, sourceTexture);
        }
    }
}