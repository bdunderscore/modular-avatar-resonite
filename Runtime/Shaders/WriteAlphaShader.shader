Shader "Hidden/NDMF/WriteAlpha"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _AlphaMaskScale ("Alpha Mask Scale", Float) = 1.0
        _AlphaMaskValue ("Alpha Mask Value", Float) = 0.0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        ColorMask A
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

            sampler2D _MainTex;
            float4 _MainTex_ST;
            float _AlphaMaskScale, _AlphaMaskValue;

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // sample the texture
                float alpha =  tex2D(_MainTex, i.uv).r;
                alpha = saturate(alpha * _AlphaMaskScale + _AlphaMaskValue);
                fixed4 col = fixed4(0,0,0,1) * alpha;
                return col;
            }
            ENDCG
        }
    }
}
