Shader"FX/MirrorDepth"
{
    Properties
    {
        [HideInInspector]_MainTex("Base (unused)", 2D) = "white" {}
    }
    SubShader
    {
        
        Tags { "RenderType" = "Opaque" }
        Cull off
        ZTest Always
        Blend Off
        ZClip False
        Conservative True

        CGINCLUDE
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
        float _Depth;
        float frag(v2f i) : SV_Depth
        {
            return 0;
        }
        ENDCG
        
        Pass
        {
            Cull off
            ZTest Always
            Blend Off
            ZClip False
            Conservative True

            CGPROGRAM
            
            #pragma vertex vert
            #pragma fragment frag
            ENDCG
        }
    }
}