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
        public float clothDiff;
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
        public long maxParticles;
        public int otherRenderers;
        public int additionalShaderKeywordCount;
        public string[] additionalShaderKeywords;
        public string LongestPath;
        public string _MillisecsTaken;
        public bool ShouldLog;
    }
    
#if UNITY_EDITOR
    [CustomEditor(typeof(AvatarInfo))]
    [CanEditMultipleObjects]
    [DisallowMultipleComponent]
    public class AvatarInfo_Editor : Editor
    {
        private System.Diagnostics.Stopwatch stopwatch = new System.Diagnostics.Stopwatch();
        private bool run = false;
        static readonly long nanosecPerTick = (1000L * 1000L * 1000L) / System.Diagnostics.Stopwatch.Frequency;
        public override void OnInspectorGUI()
        {
            AvatarInfo avatarInfo = (AvatarInfo)target;

            run = GUILayout.Button("Measure Avatar");
            DrawDefaultInspector();
            run = run || GUILayout.Button("Measure Avatar");
            if (run)
            {
                stopwatch.Reset();
                long prev;
                string test = "CountObjects: ";
                stopwatch.Start();
                avatarInfo.AvatarInfoString = "";
                AvatarInfoCalc.Instance.ShouldLog = avatarInfo.ShouldLog;
                AvatarInfoCalc.Instance.CountObjects(avatarInfo.gameObject, ref avatarInfo);
                stopwatch.Stop();
                test += stopwatch.ElapsedTicks + " CheckTextures: ";
                prev = stopwatch.ElapsedTicks;
                stopwatch.Start();
                AvatarInfoCalc.Instance.CheckTextures(avatarInfo.gameObject, ref avatarInfo);
                stopwatch.Stop();
                test += stopwatch.ElapsedTicks - prev + " CheckAudio: ";
                prev = stopwatch.ElapsedTicks;
                stopwatch.Start();
                AvatarInfoCalc.Instance.CheckAudio(avatarInfo.gameObject,ref avatarInfo);
                stopwatch.Stop();
                test += stopwatch.ElapsedTicks - prev + " CheckDB: ";
                prev = stopwatch.ElapsedTicks;
                stopwatch.Start();
                AvatarInfoCalc.Instance.CheckDB(avatarInfo.gameObject, ref avatarInfo);
                stopwatch.Stop();
                test += stopwatch.ElapsedTicks - prev + " CheckRenderers: ";
                prev = stopwatch.ElapsedTicks;
                stopwatch.Start();
                AvatarInfoCalc.Instance.CheckRenderers(avatarInfo.gameObject, ref avatarInfo);
                stopwatch.Stop();
                test += stopwatch.ElapsedTicks - prev + " CheckCloth: ";
                prev = stopwatch.ElapsedTicks;
                stopwatch.Start();
                AvatarInfoCalc.Instance.CheckCloth(avatarInfo.gameObject, ref avatarInfo);
                stopwatch.Stop();
                test += stopwatch.ElapsedTicks - prev + " Total: " + stopwatch.ElapsedTicks;
                avatarInfo._MillisecsTaken = (stopwatch.ElapsedTicks * nanosecPerTick / 1000000f).ToString();
                Debug.Log(test);
            }
        }
    }
#endif
}