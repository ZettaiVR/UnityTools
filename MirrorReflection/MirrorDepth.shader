Shader "FX/MirrorDepth"
{
    Properties
    {
        [HideInInspector][MainColor] _Color("Color (unused)", Color) = (1,1,1,1)
        [HideInInspector]_MainTex("Base (unused)", 2D) = "white" {}
    }	
    CGINCLUDE	
		#pragma vertex vert
		#pragma fragment frag
		struct appdata
		{
			float4 vertex: POSITION;
		};
		struct v2f
		{
			float4 pos: SV_POSITION;
		};
		v2f vert(appdata v)
		{
			v2f o;
			o.pos = UnityObjectToClipPos(v.vertex * 1.01);
			return o;
		}
		float frag(v2f i) : SV_Depth
		{
			#ifdef UNITY_REVERSED_Z
			return 0;
			#else
			return 1;
			#endif
		}
    ENDCG
    
    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        Pass
        {
            Cull off
            ZTest Always
            Blend Off
            ZClip False
            Conservative True
            CGPROGRAM ENDCG
        }
    } 
	SubShader
    {
        Tags { "RenderType" = "Opaque" }
        Pass
        {
            Cull off
            ZTest Always
            Blend Off
            ZClip False
            CGPROGRAM ENDCG
        }
    }
}