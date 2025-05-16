Shader "Hidden/NDMF/BakeEmission"
{
    Properties
    {
        _EmissionMap ("Texture", 2D) = "white" {}
        _EmissionBlendMask ("Texture", 2D) = "white" {}
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        ColorMask RGB
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
                float2 uv_mask : TEXCOORD1;
                float4 vertex : SV_POSITION;
            };

            sampler2D _EmissionMap;
            float4 _EmissionMap_ST;

            sampler2D _EmissionBlendMask;
            float4 _EmissionBlendMask_ST;

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _EmissionMap);
                o.uv_mask = TRANSFORM_TEX(v.uv, _EmissionBlendMask);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // sample the texture
                fixed4 maskCol = tex2D(_EmissionMap, i.uv);
                fixed4 blendMask = tex2D(_EmissionBlendMask, i.uv_mask);
                maskCol.rgb *= blendMask.rgb;
                maskCol.rgb *= blendMask.aaa;
                return maskCol;
            }
            ENDCG
        }
    }
}
