using System;
using System.Text;
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
        private void AppendLine(StringBuilder sb, string text, object value) 
        {
            sb.Append(text);
            sb.Append(value);
            sb.AppendLine();
        }
        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();
            AppendLine(sb, "textureMemMB: ", textureMemMB);
            AppendLine(sb, "calc_memMB: ", calc_memMB);
            AppendLine(sb, "AudioClipSizeMB: ", AudioClipSizeMB);
            AppendLine(sb, "AudioClipCount: ", AudioClipCount);
            AppendLine(sb, "AudioClipLength: ", AudioClipLength);
            AppendLine(sb, "AudioSources: ", AudioSources);
            AppendLine(sb, "TrianglesOrQuads: ", TrianglesOrQuads);
            AppendLine(sb, "meshRenderers: ", meshRenderers);
            AppendLine(sb, "skinnedMeshRenderers: ", skinnedMeshRenderers);
            AppendLine(sb, "lineTrailRenderers: ", lineTrailRenderers);
            AppendLine(sb, "lineTrailRendererTriCount: ", lineTrailRendererTriCount);
            AppendLine(sb, "clothNumber: ", clothNumber);
            AppendLine(sb, "clothVertCount: ", clothVertCount);
            AppendLine(sb, "clothDiff: ", clothDiff);
            AppendLine(sb, "dbCount: ", dbCount);
            AppendLine(sb, "dbCollisionCount: ", dbCollisionCount);
            AppendLine(sb, "materialCount: ", materialCount);
            AppendLine(sb, "passCount: ", passCount);
            AppendLine(sb, "TransformCount: ", TransformCount);
            AppendLine(sb, "RigidBodyCount: ", RigidBodyCount);
            AppendLine(sb, "ColliderCount: ", ColliderCount);
            AppendLine(sb, "JointCount: ", JointCount);
            AppendLine(sb, "ConstraintCount: ", ConstraintCount);
            AppendLine(sb, "Animators: ", Animators);
            AppendLine(sb, "OtherFinalIKComponents: ", OtherFinalIKComponents);
            AppendLine(sb, "VRIKComponents: ", VRIKComponents);
            AppendLine(sb, "TwistRelaxers: ", TwistRelaxers);
            AppendLine(sb, "MaxHiearchyDepth: ", MaxHiearchyDepth);
            AppendLine(sb, "Lights: ", Lights);
            AppendLine(sb, "skinnedBonesVRC: ", skinnedBonesVRC);
            AppendLine(sb, "skinnedBones: ", skinnedBones);
            AppendLine(sb, "particleSystems: ", particleSystems);
            AppendLine(sb, "maxParticles: ", maxParticles);
            AppendLine(sb, "LongestPath: ", LongestPath);
            if (!string.IsNullOrEmpty(AvatarInfoString))
            {
                sb.Append("AvatarInfoString: "); sb.Append(AvatarInfoString); sb.AppendLine();
            }
            int keywordcount = additionalShaderKeywords.Length;
            if (keywordcount > 0)
            {
                sb.AppendLine("additionalShaderKeywords: ");
                for (int i = 0; i < keywordcount; i++)
                {
                    sb.Append(additionalShaderKeywords[i]);
                    if (i != keywordcount)
                    {
                        sb.AppendLine(", ");
                    }
                }
                sb.AppendLine();
            }
            else 
            {
                sb.AppendLine("additionalShaderKeywords: none.");
            }
            return sb.ToString();
        }
    }
    
#if UNITY_EDITOR
    [CustomEditor(typeof(AvatarInfo))]
    [CanEditMultipleObjects]
    [DisallowMultipleComponent]
    public class AvatarInfo_Editor : Editor
    {
        private readonly System.Diagnostics.Stopwatch stopwatch = new System.Diagnostics.Stopwatch();
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
                Debug.Log(avatarInfo);
            }
        }
    }
#endif
}