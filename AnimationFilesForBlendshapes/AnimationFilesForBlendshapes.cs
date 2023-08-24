using UnityEngine;
using System.Collections.Generic;
using System;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Zettai
{
    public class AnimationFilesForBlendshapes : MonoBehaviour
    {
        public SkinnedMeshRenderer skinnedMeshRenderer;
        public bool IndividualFiles = true;
        public string FolderName;
        public string FileNameStart;
        public List<float> ValuesInAnimation = new List<float>() { 0, 1 };
    }
#if UNITY_EDITOR

    [CustomEditor(typeof(AnimationFilesForBlendshapes))]
    public class AnimationFilesForBlendshapesEditor : Editor
    {
        public AnimationFilesForBlendshapes script;
        public override void OnInspectorGUI()
        {
            base.DrawDefaultInspector();
            if (script == null)
            {
                script = (AnimationFilesForBlendshapes)target;
            }
            GUILayout.Space(20);
            
            if (GUILayout.Button("Save to animation file"))
            {
                if (script && !script.skinnedMeshRenderer)
                {
                    script.skinnedMeshRenderer = script.GetComponent<SkinnedMeshRenderer>();                    
                }

                if (!script || !script.skinnedMeshRenderer)
                {
                    return;
                }
                if (script.FolderName.Contains("/") || script.FolderName.Contains("\\"))
                {
                    Debug.LogError($"[AnimationFilesForBlendshapes] Path can't contain '\\' or '/'!");
                    return;
                }

                var mesh = script.skinnedMeshRenderer.sharedMesh;
                var count = mesh.blendShapeCount;
                string name = $"{script.FileNameStart}-{script.gameObject.name}-{mesh.name}";
                List<float> values = script.ValuesInAnimation;
                if (values == null) 
                    values = new List<float>();
                if (values.Count < 1) 
                    values.Add(0);
                string objectName = script.skinnedMeshRenderer.gameObject.name;
                if (script.IndividualFiles)
                {
                    for (int i = 0; i < count; i++)
                    {
                        var shapeName = $"blendShape.{mesh.GetBlendShapeName(i)}";
                        var clip = CreateClip(objectName, shapeName, typeof(SkinnedMeshRenderer), values);
                        SaveAnimationFile(clip, name, script.FolderName, shapeName);
                    }
                }
                else 
                {
                    var names = new List<string>();
                    for (int i = 0; i < count; i++)
                    {
                        names.Add($"blendShape.{mesh.GetBlendShapeName(i)}");
                    }
                    var clip = CreateClip(objectName, names, typeof(SkinnedMeshRenderer), values);
                    SaveAnimationFile(clip, name, script.FolderName, "allNames");

                }
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }
        }
        private static AnimationClip CreateClip(string objectName, string path, Type type, List<float> values) 
        {
            var clip = new AnimationClip { frameRate = 60 };
            clip.SetCurve(objectName, type, path, CreateCurve(values, 60));
            return clip;
        }
        private static AnimationClip CreateClip(string objectName, List<string> pathList, Type type, List<float> values)
        {
            var clip = new AnimationClip { frameRate = 60 };
            foreach (string path in pathList)
            {
                clip.SetCurve(objectName, type, path, CreateCurve(values, 60));
            }
            return clip;
        }
        public static void SaveAnimationFile(AnimationClip clip,string name, string assetFolder, string path)
        {
            AnimationUtility.SetAnimationClipSettings(clip, new AnimationClipSettings { loopTime = false });
            var assetPath = $"Assets/{assetFolder}/{name}-{path}.anim";
            if (!AssetDatabase.IsValidFolder($"Assets/{assetFolder}")) 
            {
                AssetDatabase.CreateFolder("Assets", assetFolder);
            }
            var uniqueAssetPath = AssetDatabase.GenerateUniqueAssetPath(assetPath);
            AssetDatabase.CreateAsset(clip, uniqueAssetPath);
        }
        private static AnimationCurve CreateCurve(List<float> values, float frameRate)
        {
            var curve = new AnimationCurve();
            curve.AddKey(0f, values[0]);
            for (int i = 1; i < values.Count; i++)
            {
                curve.AddKey(i / frameRate, values[i]);
            }
            return curve;
        }
    }


#endif
}
