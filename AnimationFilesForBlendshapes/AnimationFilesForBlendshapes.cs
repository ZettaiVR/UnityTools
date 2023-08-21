using UnityEngine;
using System.Collections.Generic;
using System;
using Unity.Collections;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Zettai
{
    public class AnimationFilesForBlendshapes : MonoBehaviour
    {
        public SkinnedMeshRenderer skinnedMeshRenderer;
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
                for (int i = 0; i < count; i++)
                {
                    var shapeName = mesh.GetBlendShapeName(i);
                    SaveAnimationFile(name, objectName, script.FolderName, $"blendShape.{shapeName}", typeof(SkinnedMeshRenderer), values);
                }
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }
        }
        public static void SaveAnimationFile(string name, string objectName, string assetFolder, string path, Type type, List<float> values)
        {
            var clip = new AnimationClip { frameRate = 60 };
            AnimationUtility.SetAnimationClipSettings(clip, new AnimationClipSettings { loopTime = false });
            clip.SetCurve(objectName, type, path, CreateCurve(values, 60));
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
