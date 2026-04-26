using UnityEngine;

namespace TextureArrayPipeline.Runtime
{
    public static class ShaderProps
    {
        public const string LitArrayShaderName = "Universal Render Pipeline/LitArray";
        public const string SimpleLitArrayShaderName = "Universal Render Pipeline/SimpleLitArray";

        public static readonly int MapArray = Shader.PropertyToID("_BaseMapArray");
        public static readonly int Surface = Shader.PropertyToID("_Surface");
        public static readonly int Blend = Shader.PropertyToID("_Blend");
        public static readonly int AlphaClip = Shader.PropertyToID("_AlphaClip");
        public static readonly int Emission = Shader.PropertyToID("_Emission");
        public static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");
        public static readonly int WorkflowMode = Shader.PropertyToID("_WorkflowMode");
        public static readonly int MetallicGlossMap = Shader.PropertyToID("_MetallicGlossMap");
        public static readonly int MetallicSpecGlossMap = Shader.PropertyToID("_MetallicSpecGlossMap");
        public static readonly int SpecGlossMap = Shader.PropertyToID("_SpecGlossMap");
    }
}