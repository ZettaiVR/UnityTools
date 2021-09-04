#if VRC_SDK_VRCSDK2
using UnityEditor;
using System.Collections.Generic;
using System;
using System.Linq;
using UnityEngine;
using VRC.Core;
using Zettai;

[CustomEditor(typeof(VRCSDK2.VRC_AvatarDescriptor), editorForChildClasses: true)]
public class AvatarExtensions : AvatarDescriptorEditor
{
    private Extension extension = null;
    AvatarInfo avatarInfo = null;
    string stats;
    bool updated = false;
    public void OnEnable()
    {
        extension = new Extension((VRCSDK2.VRC_AvatarDescriptor)target);
        avatarInfo = new AvatarInfo();
    }
    public override void OnInspectorGUI()
    {
        extension.OnInspectorGUI();
        EditorGUILayout.Space();
        base.OnInspectorGUI();
        if (GUILayout.Button("Update stats")) 
        {
            updated = false;
        }
        if (!updated) 
        {
            stats = AvatarInfoCalc.ShortStats(((VRCSDK2.VRC_AvatarDescriptor)target).gameObject);
            updated = true;
        }
        EditorGUILayout.TextArea(stats);
    }
    public class Extension
    {
        private static readonly string[] _visemeNames = new[] { "sil", "PP", "FF", "TH", "DD", "kk", "CH", "SS", "nn", "RR", "aa", "E", "ih", "oh", "ou" };
        private readonly VRCSDK2.VRC_AvatarDescriptor m_avatar;
        public bool useEyeBoneWeights;
        private List<string> m_blendShapeNames = null;
        public Extension(VRCSDK2.VRC_AvatarDescriptor avatar)
        {
            m_avatar = avatar;
        }
        public void OnInspectorGUI()
        {
            GUILayout.Space(10);
            useEyeBoneWeights = EditorGUILayout.Toggle("Eyebone weights for viewpoint", useEyeBoneWeights);
            EditorGUILayout.HelpBox("When true the viewpoint will be calculated based on the vertices of the eye, when false the position of the eye bones will be used.", MessageType.Info);
            GUILayout.Space(10);
            if (GUILayout.Button("Auto detect viewpoint"))
            {
                EditorUtility.SetDirty(m_avatar);
                FindMesh();
                FindVisemes();
                Animator animator;
                if (m_avatar.VisemeSkinnedMesh == null) { FindMesh(); }
                if ((animator = m_avatar.gameObject.GetComponent<Animator>()) != null && m_avatar.VisemeSkinnedMesh != null)
                {
                    Transform leftEye;
                    Transform rightEye;
                    var bones = m_avatar.VisemeSkinnedMesh.bones.ToList();
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
                        m_avatar.ViewPosition = GetAvgPositionFromBones(animator, m_avatar.transform.position, m_avatar.VisemeSkinnedMesh, eyes.ToArray());
                    }
                    else
                    {
                        GetViewpoint();
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
                Debug.Log("mesh vert count: " + meshVerts.Length + ", blendshape vert count: " + j + " (" + Mathf.Round(j * 10000f / meshVerts.Length) / 100f + "%), raw position: " + posAverage);
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
                        if ((boneWeights[i].boneIndex0.Equals(eyeBones[eyeIndex]) && boneWeights[i].weight0 > (0.1f / rootScale)) ||
                            (boneWeights[i].boneIndex1.Equals(eyeBones[eyeIndex]) && boneWeights[i].weight1 > (0.1f / rootScale)))
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
                eyeVerts = (from f in eyeVerts
                            orderby f.y
                            select f).ToArray();
                int k = j / 10;
                var eyeVertsFront = new Vector3[k];     // [..............**]   
                Array.Copy(eyeVerts, 0, eyeVertsFront, 0, k);
                var posAverage = eyeVerts.Length > 0 ? new Vector3(
                    eyeVertsFront.Average(x => x.x),
                    eyeVertsFront.Average(x => x.y),
                    eyeVertsFront.Average(x => x.z)
                    ) : new Vector3();
                Debug.Log("mesh vert count: " + meshVerts.Length + ", bone vert count: " + j + " (" + Mathf.Round(j * 10000f / meshVerts.Length) / 100f + "%), raw position: " + posAverage);
                var result = skinnedMesh.transform.TransformPoint(posAverage) - avatarRoot;
                result = new Vector3(
                    (Mathf.Round(result.x * 100f) / 100f),
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
                var center = Vector3.Lerp(LeftEye.position, RightEye.position, 0.5f) - m_avatar.transform.position;
                m_avatar.ViewPosition = new Vector3
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
                Vector3 eyeLevel = (Neck.position - m_avatar.transform.position) * 1.1f;
                eyeLevel.z += eyeLevel.y * 0.03f;
                m_avatar.ViewPosition = new Vector3
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

            for (int i = 0; i < m_avatar.VisemeBlendShapes.Length; i++)
            {
                index = m_blendShapeNames.FindIndex(x => x.EndsWith("v_" + _visemeNames[i], StringComparison.OrdinalIgnoreCase));
                if (index >= 0)
                {
                    m_avatar.VisemeBlendShapes[i] = m_blendShapeNames[index];
                    m_avatar.lipSync = VRCSDK2.VRC_AvatarDescriptor.LipSyncStyle.VisemeBlendShape;
                }
            }
        }
        private void FindMesh()
        {
            if (m_avatar.VisemeSkinnedMesh == null)
            {
                var smrs = m_avatar.GetComponentsInChildren<SkinnedMeshRenderer>();
                foreach (var smr in smrs)
                {
                    if (smr.sharedMesh.blendShapeCount >= (_visemeNames.Length + 4))
                    {
                        m_avatar.VisemeSkinnedMesh = smr;
                        GetBlendShapeNames();
                        return;
                    }
                }
            }
        }
        void GetBlendShapeNames()
        {
            m_blendShapeNames = new List<string> { "-none-" };
            if (m_avatar.VisemeSkinnedMesh != null && m_avatar.VisemeSkinnedMesh.sharedMesh != null)
            {
                for (int i = 0; i < m_avatar.VisemeSkinnedMesh.sharedMesh.blendShapeCount; ++i)
                    m_blendShapeNames.Add(m_avatar.VisemeSkinnedMesh.sharedMesh.GetBlendShapeName(i));
            }
            else
            {
                m_blendShapeNames.Add("-none-");
            }
        }
    }
}

#endif