using UnityEditor;
using UnityEngine;

namespace Zettai
{
    public class AvatarInfo : MonoBehaviour
    {
        public string AvatarInfoString;
        public uint AudioClipCount;
        public double AudioClipSizeMB;
        public uint AudioClipLength;
        public double textureMemMB;
        public double calc_memMB;
        public long textureMem;
        public long calc_mem;
        public uint triangles;
        public uint meshRenderers;
        public uint skinnedMeshRenderers;
        public uint lineTrailRenderers;
        public uint clothNumber;
        public uint clothVertCount;
        public uint clothDiff;
        public uint dbCount;
        public uint dbCollCount;
        public uint materialCount;
    }

#if UNITY_EDITOR
    [CustomEditor(typeof(AvatarInfo))]
    public class AvatarInfo_Editor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            AvatarInfo avatarInfo = (AvatarInfo)target;
            if (GUILayout.Button("Measure Avatar"))
            {
                avatarInfo.AvatarInfoString = "";
                QualitySettings.anisotropicFiltering = AnisotropicFiltering.ForceEnable;
                AvatarInfoCalc.CheckTextures(avatarInfo.gameObject, ref avatarInfo);
                AvatarInfoCalc.CheckAudio(avatarInfo.gameObject,ref avatarInfo);
                AvatarInfoCalc.CheckDB(avatarInfo.gameObject, ref avatarInfo);
                AvatarInfoCalc.CheckRenderers(avatarInfo.gameObject, ref avatarInfo);
                AvatarInfoCalc.CheckCloth(avatarInfo.gameObject, ref avatarInfo);
            }
        }
    }
#endif
}