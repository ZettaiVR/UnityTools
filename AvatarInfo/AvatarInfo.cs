using System;
using UnityEditor;
using UnityEngine;

namespace Zettai
{
    public class AvatarInfo : MonoBehaviour
    {
        public double textureMemMB;
        public double calc_memMB;
        public long textureMem;
        public long calc_mem;
        public string AvatarInfoString;
        public double AudioClipSizeMB;
        public int AudioClipCount;
        public int AudioClipLength;
        public int AudioSources;
        public int TrianglesOrQuads;
        public int meshRenderers;
        public int skinnedMeshRenderers;
        public int lineTrailRenderers;
        public int lineTrailRendererTriCount;
        public int clothNumber;
        public int clothVertCount;
        public int clothDiff;
        public int dbCount;
        public int dbCollisionCount;
        public int materialCount;
        public int passCount;
        public int TransformCount;
        public int RigidBodyCount;
        public int ColliderCount;
        public int JointCount;
        public int ConstraintCount;
        public int Animators;
        public int OtherFinalIKComponents;
        public int VRIKComponents;
        public int TwistRelaxers;
        public int MaxHiearchyDepth;
        public int Lights;
        public int skinnedBonesVRC;
        public int skinnedBones;
        public int particleSystems;
        public int maxParticles;
        public int otherRenderers;
        public int additionalShaderKeywordCount;
        public string LongestPath;
        public string _MillisecsTaken;
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
                DateTime start = DateTime.Now;
                avatarInfo.AvatarInfoString = "";
                QualitySettings.anisotropicFiltering = AnisotropicFiltering.ForceEnable;
                AvatarInfoCalc.Instance.CountObjects(avatarInfo.gameObject, ref avatarInfo);
                AvatarInfoCalc.Instance.CheckTextures(avatarInfo.gameObject, ref avatarInfo);
                AvatarInfoCalc.Instance.CheckAudio(avatarInfo.gameObject,ref avatarInfo);
                AvatarInfoCalc.Instance.CheckDB(avatarInfo.gameObject, ref avatarInfo);
                AvatarInfoCalc.Instance.CheckRenderers(avatarInfo.gameObject, ref avatarInfo);
                AvatarInfoCalc.Instance.CheckCloth(avatarInfo.gameObject, ref avatarInfo);
                avatarInfo._MillisecsTaken = (DateTime.Now - start).TotalMilliseconds.ToString();
            }
        }
    }
#endif
}