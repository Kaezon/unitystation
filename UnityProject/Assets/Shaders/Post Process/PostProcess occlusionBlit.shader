﻿Shader "PostProcess/PPRT Transform Blit"
{
	Properties
	{
		_MainTex ("Texture", 2D) = "white" {}
		_SourceTex ("Texture", 2D) = "white" {}
	}
	SubShader
	{
		// No culling or depth
		Cull Off ZWrite Off ZTest Always

		Pass
		{
			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			
			#include "UnityCG.cginc"

			struct appdata
			{
				float4 vertex : POSITION;
				float2 uv : TEXCOORD0;
			};

			struct v2f
			{
				//float2 uv : TEXCOORD0;
				float2 occlusionUv : TEXCOORD1;
				float4 vertex : SV_POSITION;
			};
			
			sampler2D _MainTex;
			sampler2D _SourceTex;
			
			float4 _Transform;
			
			v2f vert (appdata v)
			{
				v2f o;

				o.vertex = UnityObjectToClipPos(v.vertex);
				o.occlusionUv = ((v.uv + _Transform.xy) * _Transform.zw) - (_Transform.zw - float2(1,1)) * 0.5f;
				return o;
			} 

			fixed4 frag (v2f i) : SV_Target
			{
				fixed4 color = tex2D(_SourceTex, i.occlusionUv);

				return color;
			}
			
			ENDCG
		}
	}
}