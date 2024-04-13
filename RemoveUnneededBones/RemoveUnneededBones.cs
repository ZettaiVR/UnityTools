using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Unity.Collections;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Zettai
{
    public class RemoveUnneededBones : MonoBehaviour
    {
        public bool save = true;
        private static readonly List<SkinnedMeshRenderer> renderers = new List<SkinnedMeshRenderer>();
        private static readonly Dictionary<int, int> indices = new Dictionary<int, int>();
        private static readonly List<int> orderIndices = new List<int>();
        private static readonly HashSet<int> usedIndices = new HashSet<int>();
        private static readonly List<Matrix4x4> bindposes = new List<Matrix4x4>();
        private static readonly List<Matrix4x4> newBindposes = new List<Matrix4x4>();
        private static readonly List<Transform> newTransforms = new List<Transform>();
        public static List<string> createdAssetNames = new List<string>();
        private readonly static System.Diagnostics.Stopwatch sw1 = new System.Diagnostics.Stopwatch();
        private readonly static System.Diagnostics.Stopwatch sw2 = new System.Diagnostics.Stopwatch();

        public static void Remove(Transform root, bool save = true)
        {
            if (!root)
                return;
            sw1.Restart();
            sw2.Reset();
            root.GetComponentsInChildren(true, renderers);

#if UNITY_EDITOR
            if (!AssetDatabase.IsValidFolder("Assets/temp") && save)
                AssetDatabase.CreateFolder("Assets", "temp");
#endif
            foreach (var renderer in renderers)
            {
                if (renderer.gameObject.CompareTag("EditorOnly"))
                    continue;
                indices.Clear();
                usedIndices.Clear();
                orderIndices.Clear();
                bindposes.Clear();
                newBindposes.Clear();
                newTransforms.Clear();
                var mesh = Instantiate(renderer.sharedMesh);
                var weights1 = mesh.GetAllBoneWeights();
                var weights = new NativeArray<BoneWeight1>(weights1, Allocator.Temp);
                //var weights = mesh.boneWeights;
                var bonesPerVertex = mesh.GetBonesPerVertex();
                foreach (var item in weights)
                {
                    usedIndices.Add(item.boneIndex);
                }
                /*foreach (var item in weights)
                {
                    usedIndices.Add(item.boneIndex0);
                    usedIndices.Add(item.boneIndex1);
                    usedIndices.Add(item.boneIndex2);
                    usedIndices.Add(item.boneIndex3);
                }*/
                var bones = renderer.bones;
                orderIndices.AddRange(usedIndices);
                var ordered = orderIndices.OrderBy(a => a).ToArray();
                int index = 0;
                mesh.GetBindposes(bindposes);
                foreach (var boneIndex in ordered)
                {
                    indices.Add(boneIndex, index);
                    newBindposes.Add(bindposes[boneIndex]);
                    newTransforms.Add(bones[boneIndex]);
                    index++;
                }
                if (index == bones.Length)
                    continue;

                for (int i = 0; i < weights.Length; i++)
                {
                    var item = weights[i];
                    item.boneIndex = indices[item.boneIndex];
                    weights[i] = item;
                }

                 mesh.SetBoneWeights(bonesPerVertex, weights);
                 weights.Dispose();
                 
                /*for (int i = 0; i < weights.Length; i++)
                {
                    BoneWeight item = weights[i];
                    item.boneIndex0 = indices[item.boneIndex0];
                    item.boneIndex1 = indices[item.boneIndex1];
                    item.boneIndex2 = indices[item.boneIndex2];
                    item.boneIndex3 = indices[item.boneIndex3];
                    weights[i] = item;
                }
                mesh.boneWeights = weights;*/
                mesh.bindposes = newBindposes.ToArray();
                renderer.bones = newTransforms.ToArray();
                sw1.Stop();
                sw2.Start();

#if UNITY_EDITOR
                if (save)
                {
                    var name = $"Assets/temp/RemoveUnneededBonesTempAsset-{root.name}-{renderer.name}-{mesh.name}-{Guid.NewGuid()}.mesh";
                    AssetDatabase.CreateAsset(mesh, name);
                    createdAssetNames.Add(name);
                }
#endif
                sw2.Stop();
                sw1.Start();
                renderer.sharedMesh = mesh;
            }
            sw1.Stop ();
            Debug.Log($"cleanup took {sw1.Elapsed.TotalMilliseconds:N2} ms, saving took {sw2.Elapsed.TotalMilliseconds:N2} ms. ");

#if UNITY_EDITOR
            if (save)
            {
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }
#endif
        }
    }
}