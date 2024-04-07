Shader"FX/MirrorReflectionCutout"
{
	Properties
	{
        [MainColor][HDR] _Color("Color", Color) = (1,1,1,1)
		[MainTexture] _MainTex("Base (RGB)", 2D) = "white" {}
		[HideInInspector][PerRendererData][NoScaleOffset] _ReflectionTexLeft("_ReflectionTexLeft", 2D) = "white" {}
		[HideInInspector][PerRendererData][NoScaleOffset] _ReflectionTexRight("_ReflectionTexRight", 2D) = "white" {}
		[ToggleUI(HideBackground)] _HideBackground("Hide Background", Float) = 0
        _Transparency("Transparency", Range(0.0, 1.0)) = 1
        //Stencils
        [Space(20)] _Stencil ("Stencil ID", Float) = 0
        [Enum(UnityEngine.Rendering.CompareFunction)] _StencilCompareAction ("Stencil Compare Function", int) = 0
        [Enum(UnityEngine.Rendering.StencilOp)] _StencilOp ("Stencil Pass Operation", int) = 0
        [Enum(UnityEngine.Rendering.StencilOp)] _StencilFail ("Stencil Fail Operation", int) = 0
        [Enum(UnityEngine.Rendering.StencilOp)] _StencilZFail ("Stencil ZFail Operation", int) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        [Enum(UnityEngine.Rendering.BlendMode)] _BlendSrc ("Blend source", int) = 5
        [Enum(UnityEngine.Rendering.BlendMode)] _BlendDst ("Blend destination", int) = 10
	}
	SubShader
	{
		Tags{ "RenderType"="Transparent" "Queue"="Transparent" "IgnoreProjector"="True"}
		ZWrite On
        Blend [_BlendSrc] [_BlendDst]
        LOD 100
        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilCompareAction]
            Pass [_StencilOp]
            Fail [_StencilFail]
            ZFail [_StencilZFail]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }
		Pass {
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
            float _HideBackground;
            float _Transparency;
            fixed4 _Color;
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

			fixed4 frag(v2f i) : SV_Target
			{
				UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
				fixed4 tex = tex2D(_MainTex, i.uv);
				float4 projCoord = UNITY_PROJ_COORD(i.refl);
				float2 proj2 = float2(1 - projCoord.x / projCoord.w, projCoord.y / projCoord.w);
				float4 refl;
				if (unity_StereoEyeIndex == 0) 
				   refl = tex2D(_ReflectionTexLeft, proj2);
				else 
				   refl = tex2D(_ReflectionTexRight, proj2);
	
				// Hiding background
                if (!_HideBackground) 
				{
                   refl.a = 1;
                }
                refl.a *= _Transparency;
                if (refl.a < 0.001)
					discard;
	
				return tex * refl * _Color;
			}
			ENDCG
		}
	}
}