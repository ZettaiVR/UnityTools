using UnityEditor;
using System.Collections.Generic;
using ABI.CCK.Components;
using System;
using System.Linq;
using UnityEngine;
using ABI.CCK.Scripts.Editor;

namespace CCK_Extensions
{
    public class UploadPercent : ABI.CCK.Scripts.Runtime.CCK_RuntimeUploaderMaster
    {
        void Update()
        {
            if (isUploading)
            {
                //or just cast it as int
#if UNITY_EDITOR
                updater.uploadProgressText.text = updater.uploadProgressText.text.Split('.')[0] + "%";
#endif
            }
        }
    }

    [CustomEditor(typeof(CVRAvatar))]
    public class AvatarExtensions : CCK_CVRAvatarEditor
    {
        private Extention extention = null;
        public void OnEnable()
        {
            extention = new Extention(((CVRAvatar)target)) { useEyeBoneWeights = true };           
        }

        public override void OnInspectorGUI()
        {
            extention.OnInspectorGUI();
            EditorGUILayout.Space();
            base.OnInspectorGUI();
        }
        public class Extention
        {
            private static readonly string[] _visemeNames = new[] { "sil", "PP", "FF", "TH", "DD", "kk", "CH", "SS", "nn", "RR", "aa", "E", "ih", "oh", "ou" };
            private static readonly string[] _blinkNames = new[] { "blink_left", "blink_right", "lowerlid_left", "lowerlid_right" };
            private readonly CVRAvatar m_avatar;
            public bool useEyeBoneWeights;
            private List<string> m_blendShapeNames = null;
            public Extention(CVRAvatar avatar)
            {
                m_avatar = avatar;
            }
            public void OnInspectorGUI()
            {
                GUILayout.Space(10);
                useEyeBoneWeights = EditorGUILayout.Toggle("Eyebone weights for viewpoint", useEyeBoneWeights);
                EditorGUILayout.HelpBox("When true the viewpoint will be calculated based on the vertices of the eye, when false the position of the eye bones will be used.", MessageType.Info);
                GUILayout.Space(10);
                if (GUILayout.Button("Auto detect settings"))
                {
                    FindMesh();
                    FindVisemes();
                    Animator animator;
                    if (m_avatar.bodyMesh == null) { FindMesh(); }
                    if ((animator = m_avatar.gameObject.GetComponent<Animator>()) != null && m_avatar.bodyMesh != null)
                    {
                        Transform leftEye;
                        Transform rightEye;
                        var bones = m_avatar.bodyMesh.bones.ToList();
                        var eyes = new List<int>();
                        if ((leftEye = animator.GetBoneTransform(HumanBodyBones.LeftEye)) != null)
                        {
                            eyes.Add(bones.IndexOf(leftEye));
                        }
                        if ((rightEye = animator.GetBoneTransform(HumanBodyBones.RightEye)) != null)
                        {
                            eyes.Add(bones.IndexOf(rightEye));
                        }
                        if (eyes.Count > 0 && useEyeBoneWeights)
                        {
                            m_avatar.viewPosition = GetAvgPositionFromBones(animator, m_avatar.transform.position, m_avatar.bodyMesh, eyes.ToArray());
                        }
                        else
                        {
                            GetViewpoint();
                        }
                        if (m_avatar.useVisemeLipsync)
                        {
                            m_avatar.voicePosition = GetAvgPositionFromBlendshape(m_avatar.transform.position, m_avatar.bodyMesh, m_avatar.visemeBlendshapes[m_avatar.visemeBlendshapes.Length - 1]);
                        }
                        else
                        {
                            //when we don't have visemes guess voicePosition from viewPosition
                            m_avatar.voicePosition = new Vector3
                            {
                                x = (Mathf.Round(m_avatar.viewPosition.x * 1000f) / 1000f),
                                y = (Mathf.Round(m_avatar.viewPosition.y / 1.05f * 1000f) / 1000f), //this is usually fairly close
                                z = (Mathf.Round(m_avatar.viewPosition.z * 1.7f * 1000f) / 1000f)   //this can be off
                            };
                        }
                    }
                }
            }
            private Vector3 GetViewpointFromEyeBones(Animator animator, Vector3 avatarRoot)
            {
                Transform LeftEye;
                Transform RightEye;
                if ((LeftEye = animator.GetBoneTransform(HumanBodyBones.LeftEye)) != null &&
                    (RightEye = animator.GetBoneTransform(HumanBodyBones.RightEye)) != null)
                {
                    var center = Vector3.Lerp(LeftEye.position, RightEye.position, 0.5f) - avatarRoot;
                    //Vector3 localScale = animator.gameObject.transform.localScale;
                    //center.Scale(new Vector3(1 / localScale.x, 1 / localScale.y, 1 / localScale.z)); 
                    return new Vector3(
                         (Mathf.Round(center.x * 1000f) / 1000f),
                         (Mathf.Round(center.y * 1000f) / 1000f),
                         (Mathf.Round(center.z * 1000f) / 1000f)
                         );
                }
                return Vector3.zero;
            }
            public Vector3 GetAvgPositionFromBlendshape(Vector3 avatarRoot, SkinnedMeshRenderer skinnedMesh, string blendShapeName)
            {
                float smoothing = 0.001f * skinnedMesh.sharedMesh.bounds.max.magnitude; //the amount of minimum vert movement when we consider it moving the mouth. too low and it might get inaccurate results, too high and it fails to find any verts moving at all.
                int index = skinnedMesh.sharedMesh.GetBlendShapeIndex(blendShapeName);
                var ohBlendVerts = new Vector3[skinnedMesh.sharedMesh.vertexCount];
                var ohBlendNormals = new Vector3[skinnedMesh.sharedMesh.vertexCount];
                var ohBlendTangents = new Vector3[skinnedMesh.sharedMesh.vertexCount]; // Copy blend shape data from myMesh to tmpMesh
                var blendVerts = new Vector3[skinnedMesh.sharedMesh.vertexCount];
                var meshVerts = skinnedMesh.sharedMesh.vertices; //caching this makes it literally 100x faster. 
                var rootScale = skinnedMesh.rootBone.transform.lossyScale.magnitude; //scale with armature scale, ie. when Armature is scaled at magnitude 100 instead of 1.
                int j = 0; //moving vert counter
                try
                {
                    skinnedMesh.sharedMesh.GetBlendShapeFrameVertices(index, skinnedMesh.sharedMesh.GetBlendShapeFrameCount(index) - 1, ohBlendVerts, ohBlendNormals, ohBlendTangents);
                    for (int i = 0; i < ohBlendVerts.Length; i++)
                    {
                        if (ohBlendVerts[i].magnitude > smoothing / rootScale)
                        {
                            blendVerts[j] = meshVerts[i];
                            j++;
                        }
                    }
                    var ohverts = new Vector3[j];
                    for (int i = 0; i < j; i++)
                    {
                        ohverts[i] = blendVerts[i];
                    }

                    var posAverage = new Vector3(
                        ohverts.Average(x => x.x),
                        ohverts.Average(x => x.y),
                        ohverts.Average(x => x.z)
                        );
                    Debug.Log($"mesh vert count: {meshVerts.Length}, blendshape vert count: {j} ({Mathf.Round(j * 10000f / meshVerts.Length) / 100f}%), raw position: {posAverage}");
                    var result = skinnedMesh.transform.TransformPoint(posAverage) - avatarRoot;
                    return new Vector3(
                        (Mathf.Round(result.x * 1000f) / 1000f),
                        (Mathf.Round(result.y * 1000f) / 1000f),
                        (Mathf.Round(result.z * 1000f) / 1000f)
                        );
                }
                catch (Exception e)
                {
                    Debug.LogWarning(e.Message);
                }
                return Vector3.zero;
            }
            public Vector3 GetAvgPositionFromBones(Animator animator, Vector3 avatarRoot, SkinnedMeshRenderer skinnedMesh, int[] eyeBones)
            {
                var boneVerts = new Vector3[skinnedMesh.sharedMesh.vertexCount];
                var meshVerts = skinnedMesh.sharedMesh.vertices;
                var rootScale = skinnedMesh.rootBone.transform.lossyScale.magnitude; //scale with armature scale, ie. when Armature is scaled at magnitude 100 instead of 1.

                int j = 0; 
                try
                {
                    List<BoneWeight> boneWeights = new List<BoneWeight>();
                    skinnedMesh.sharedMesh.GetBoneWeights(boneWeights);

                    for (int i = 0; i < boneWeights.Count; i++)
                    {
                        for (int eyeIndex = 0; eyeIndex < eyeBones.Length; eyeIndex++)
                        {
                            if ((boneWeights[i].boneIndex0.Equals(eyeBones[eyeIndex]) && boneWeights[i].weight0 > 0.4f / rootScale) ||
                                (boneWeights[i].boneIndex1.Equals(eyeBones[eyeIndex]) && boneWeights[i].weight1 > 0.4f / rootScale))
                            {
                                boneVerts[j] = meshVerts[i];
                                j++;
                            }
                        }
                    }
                    var eyeVerts = new Vector3[j];
                    for (int i = 0; i < j; i++)
                    {
                        eyeVerts[i] = boneVerts[i];
                    }

                    var posAverage = new Vector3(
                        eyeVerts.Average(x => x.x),
                        eyeVerts.Average(x => x.y),
                        eyeVerts.Average(x => x.z)
                        );
                    Debug.Log($"mesh vert count: {meshVerts.Length}, bone vert count: {j} ({Mathf.Round(j * 10000f / meshVerts.Length) / 100f}%), raw position: {posAverage}");
                    var result = skinnedMesh.transform.TransformPoint(posAverage) - avatarRoot;
                    result = new Vector3(
                        (Mathf.Round(result.x * 1000f) / 1000f),
                        (Mathf.Round(result.y * 1000f) / 1000f),
                        (Mathf.Round(result.z * 1000f) / 1000f)
                        );
                    //Z is more accurate from bone position
                    var view = GetViewpointFromEyeBones(animator, avatarRoot);
                    result.z = Mathf.Lerp(view.z, result.z, 0.5f);
                    return result;
                }
                catch (Exception e)
                {
                    Debug.LogWarning(e.Message);
                }
                return Vector3.zero;
            }
            private void GetViewpoint()
            {
                Transform LeftEye;
                Transform RightEye;
                Animator animator = m_avatar.GetComponent<Animator>();
                if ((LeftEye = animator.GetBoneTransform(HumanBodyBones.LeftEye)) != null &&
                        (RightEye = animator.GetBoneTransform(HumanBodyBones.RightEye)) != null)
                {
                    m_avatar.useEyeMovement = true;
                    var center = Vector3.Lerp(LeftEye.position, RightEye.position, 0.5f) - m_avatar.transform.position;
                    m_avatar.viewPosition = new Vector3
                    {
                        x = (Mathf.Round(center.x * 1000f) / 1000f),
                        y = (Mathf.Round(center.y * 1000f) / 1000f),
                        z = (Mathf.Round(center.z * 1000f) / 1000f)
                    };
                }
                else
                {
                    Transform Neck;
                    // if you don't have a neck bone the head can be used for this
                    // while the neck is optional, the head isn't.
                    if ((Neck = animator.GetBoneTransform(HumanBodyBones.Neck)) == null)
                    {
                        Neck = animator.GetBoneTransform(HumanBodyBones.Neck);
                    }
                    m_avatar.useEyeMovement = false;
                    Vector3 eyeLevel = (Neck.position - m_avatar.transform.position) * 1.1f;
                    eyeLevel.z += eyeLevel.y * 0.03f;
                    m_avatar.viewPosition = new Vector3
                    {
                        x = (Mathf.Round(eyeLevel.x * 1000f) / 1000f),
                        y = (Mathf.Round(eyeLevel.y * 1000f) / 1000f),
                        z = (Mathf.Round(eyeLevel.z * 1000f) / 1000f)
                    };
                }
            }
            void FindVisemes()
            {
                int index = 0;
                if (m_blendShapeNames == null || m_blendShapeNames.Count < 4) GetBlendShapeNames();
                for (int i = 0; i < m_avatar.visemeBlendshapes.Length; i++)
                {
                    index = m_blendShapeNames.FindIndex(x => x.EndsWith("v_" + _visemeNames[i], StringComparison.OrdinalIgnoreCase));
                    if (index >= 0)
                    {
                        m_avatar.visemeBlendshapes[i] = m_blendShapeNames[index];
                        m_avatar.useVisemeLipsync = true;
                    }
                }
                for (int i = 0; i < m_avatar.blinkBlendshape.Length; i++)
                {
                    index = m_blendShapeNames.FindIndex(x => x.EndsWith(_blinkNames[i], StringComparison.OrdinalIgnoreCase));
                    if (index >= 0)
                    {
                        m_avatar.blinkBlendshape[i] = m_blendShapeNames[index];
                        m_avatar.useBlinkBlendshapes = true;
                    }
                }
            }
            private void FindMesh()
            {
                if (m_avatar.bodyMesh is null)
                {
                    var smrs = m_avatar.GetComponentsInChildren<SkinnedMeshRenderer>();
                    foreach (var smr in smrs)
                    {
                        if (smr.sharedMesh.blendShapeCount >= (_visemeNames.Length + 4))
                        {
                            m_avatar.bodyMesh = smr;
                            GetBlendShapeNames();
                            return;
                        }
                    }
                }
            }
            
            void GetBlendShapeNames()
            {
                m_blendShapeNames = new List<string> { "-none-" };
                if (m_avatar.bodyMesh != null && m_avatar.bodyMesh.sharedMesh != null)
                {
                    for (int i = 0; i < m_avatar.bodyMesh.sharedMesh.blendShapeCount; ++i)
                        m_blendShapeNames.Add(m_avatar.bodyMesh.sharedMesh.GetBlendShapeName(i));
                }
                else
                {
                    m_blendShapeNames.Add("-none-");
                }
            }
        }
    }
}