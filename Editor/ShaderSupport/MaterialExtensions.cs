#nullable enable

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using UnityEditor;
using UnityEngine;
using Material = UnityEngine.Material;
using Texture = UnityEngine.Texture;

namespace nadena.dev.ndmf.platform.resonite
{
    internal static class MaterialExtensions
    {
        private static Dictionary<Shader, HashSet<string>> validPropertiesCache = new();
        
        private static HashSet<string> GetValidProperties(Shader? shader)
        {
            if (shader == null) return new();
            
            if (validPropertiesCache.TryGetValue(shader, out var properties))
            {
                return properties;
            }

            properties = new HashSet<string>();
            int propertyCount = ShaderUtil.GetPropertyCount(shader);
            for (int i = 0; i < propertyCount; i++)
            {
                var prop = ShaderUtil.GetPropertyName(shader, i);
                properties.Add(prop);
            }
            validPropertiesCache[shader] = properties;
            return properties;
        }
        
        public static Texture? GetTextureSafe(this Material mat, string name)
        {
            if (!GetValidProperties(mat.shader).Contains(name)) return null;

            return mat.GetTexture(name);
        }
        
        public static Vector2 GetTextureScaleSafe(this Material mat, string name)
        {
            if (!GetValidProperties(mat.shader).Contains(name)) return Vector2.one;

            return mat.GetTextureScale(name);
        }
        
        public static Vector2 GetTextureOffsetSafe(this Material mat, string name)
        {
            if (!GetValidProperties(mat.shader).Contains(name)) return Vector2.zero;

            return mat.GetTextureOffset(name);
        }
        
        public static Color GetColorSafe(this Material mat, string name, Color defaultColor)
        {
            if (!GetValidProperties(mat.shader).Contains(name)) return defaultColor;

            return mat.GetColor(name);
        }
        
        [return: NotNullIfNotNull("defaultValue")]
        public static float? GetFloatSafe(this Material mat, string name, float? defaultValue = null)
        {
            if (!GetValidProperties(mat.shader).Contains(name)) return defaultValue;

            return mat.GetFloat(name);
        }
    }
}