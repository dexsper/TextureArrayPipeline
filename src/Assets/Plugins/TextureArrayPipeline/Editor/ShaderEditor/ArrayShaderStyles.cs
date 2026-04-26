using UnityEditor;
using UnityEngine;

namespace TextureArrayPipeline.ShaderEditor
{
    public static class ArrayShaderStyles
    {
        public static readonly GUIContent BaseMapArrayText = EditorGUIUtility.TrTextContent(
            "Albedo Array",
            "Texture2DArray used as the base albedo source."
        );
    }
}