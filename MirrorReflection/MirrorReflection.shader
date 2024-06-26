﻿Shader"FX/MirrorReflection2"
{
	Properties
	{
        [MainColor][HDR] _Color("Color", Color) = (1,1,1,1)
		[MainTexture] _MainTex("Base (RGB)", 2D) = "white" {}
		[HideInInspector][PerRendererData][ToggleUI] _portalMode("Enable portal mode", Float) = 0
		[HideInInspector][PerRendererData][NoScaleOffset] _ReflectionTexLeft("_ReflectionTexLeft", 2D) = "white" {}
		[HideInInspector][PerRendererData][NoScaleOffset] _ReflectionTexRight("_ReflectionTexRight", 2D) = "white" {}
	}
	SubShader
	{
		Tags { "RenderType" = "Opaque" }
		LOD 100
		Pass 
		{
			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#include "UnityCG.cginc"
			#include "UnityInstancing.cginc"
			struct v2f
			{
				float2 uv : TEXCOORD0;
				float4 refl : TEXCOORD1;
				float4 pos : SV_POSITION;
				UNITY_VERTEX_OUTPUT_STEREO
			};

			struct appdata
			{
				float4 vertex : POSITION;
				float2 uv : TEXCOORD0;

				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			float4 _MainTex_ST;
			v2f vert(appdata v)
			{
				v2f o; 
				
				UNITY_SETUP_INSTANCE_ID(v);
				UNITY_INITIALIZE_OUTPUT(v2f, o);
				UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

				o.pos = UnityObjectToClipPos(v.vertex);
				o.uv = TRANSFORM_TEX(v.uv, _MainTex);
				o.refl = ComputeNonStereoScreenPos(o.pos);
				return o;
			}
			sampler2D _MainTex;
			sampler2D _ReflectionTexLeft;
			sampler2D _ReflectionTexRight;
			fixed4 _Color;
			float _portalMode;

			fixed4 frag(v2f i) : SV_Target
			{
				UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);

				fixed4 tex = tex2D(_MainTex, i.uv);
				float4 projCoord = UNITY_PROJ_COORD(i.refl);
				if (_portalMode == 0)
				{
				
				    float2 proj2 = float2(1 - projCoord.x / projCoord.w, projCoord.y / projCoord.w);
				    if (unity_StereoEyeIndex == 0) 
				        tex *= tex2D(_ReflectionTexLeft, proj2);
				    else
				        tex *= tex2D(_ReflectionTexRight, proj2);
				}
				else
				{
				    if (unity_StereoEyeIndex == 0) 
				    tex *= tex2Dproj(_ReflectionTexLeft, UNITY_PROJ_COORD(i.refl));
				    else
				        tex *= tex2Dproj(_ReflectionTexRight, UNITY_PROJ_COORD(i.refl));

				}
                return tex * _Color;
			}
			ENDCG
		}
	}
}