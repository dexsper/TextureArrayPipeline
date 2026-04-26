using System;
using TextureArrayPipeline.Runtime;
using UnityEditor;
using UnityEditor.Rendering.Universal.ShaderGUI;
using UnityEngine;

namespace TextureArrayPipeline.ShaderEditor.SimpleLit
{
    public class SimpleLitEditor : BaseShaderGUI
    {
        private SimpleLitGUI.SimpleLitProperties _shadingModelProperties;
        private MaterialProperty _baseMapArrayProp;

        public override void FindProperties(MaterialProperty[] properties)
        {
            base.FindProperties(properties);
            _shadingModelProperties = new SimpleLitGUI.SimpleLitProperties(properties);
            _baseMapArrayProp = FindProperty("_BaseMapArray", properties, false);
        }

        public override void ValidateMaterial(Material material)
        {
            SetMaterialKeywords(material, SimpleLitGUI.SetMaterialKeywords);
        }

        public override void DrawSurfaceOptions(Material material)
        {
            if (material == null)
                throw new ArgumentNullException(nameof(material));

            EditorGUIUtility.labelWidth = 0f;
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

            SimpleLitGUI.Inputs(_shadingModelProperties, materialEditor, material);
            DrawEmissionProperties(material, true);
            DrawTileOffset(materialEditor, _baseMapArrayProp);
        }

        public override void DrawAdvancedOptions(Material material)
        {
            SimpleLitGUI.Advanced(_shadingModelProperties);
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
                blendMode = BlendMode.Alpha;
                surfaceType = SurfaceType.Transparent;
            }

            material.SetFloat(ShaderProps.Blend, (float)blendMode);
            material.SetFloat(ShaderProps.Surface, (float)surfaceType);
        }
    }
}