Shader "Hidden/NDMF/BakeMetallicGlossReflectionMap"
{
    Properties
    {
        _Smoothness ("Texture", 2D) = "white" {}
        _Metallic ("Texture", 2D) = "white" {}
        _ReflectionColor ("sColor", Color) = (1,1,1,1)
        _ReflectionColorTex ("Texture", 2D) = "white" {}
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        ColorMask RGBA
        Blend One Zero, One Zero // preserve alpha
        LOD 100

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            // make fog work
            #pragma multi_compile_fog

            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
            };

            sampler2D _Smoothness;
            float4 _Smoothness_ST;

            sampler2D _Metallic;
            float4 _Metallic_ST;

            sampler2D _ReflectionColorTex;
            float4 _ReflectionColorTex_ST;

            fixed4 _ReflectionColor;
            
            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                fixed4 smoothnessCol = tex2D(_Smoothness, TRANSFORM_TEX(i.uv, _Smoothness));
                fixed4 metallicCol = tex2D(_Metallic, TRANSFORM_TEX(i.uv, _Metallic));
                fixed4 reflectionCol = tex2D(_ReflectionColorTex, TRANSFORM_TEX(i.uv, _ReflectionColorTex))
                    * _ReflectionColor;

                // Liltoon uses only the red channel for metallic and smoothness; it uses all channels for reflection color,
                // but since XSToon only supports one channel, we merge luminance and alpha here.
                fixed smoothness = smoothnessCol.r;
                fixed metallic = metallicCol.r;
                fixed reflection = Luminance(reflectionCol.xyz) * reflectionCol.a;

                // Resonite uses metallic in R, smooth/gloss in A
                return fixed4(metallic, 1,1, smoothness * reflection);
            }
            ENDCG
        }
    }
}
