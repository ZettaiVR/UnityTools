using UnityEngine;
using System;
using Zettai;
#if UNITY_EDITOR
using UnityEditor;
#if VRC_SDK_VRCSDK2 || VRC_SDK_VRCSDK3
using VRC.SDKBase.Editor.BuildPipeline;
#endif
//#if CVR_CCK_EXISTS
//using ABI.CCK.Scripts.Editor;
//#endif

#if VRC_SDK_VRCSDK2 || VRC_SDK_VRCSDK3
public class RemoveUnneededBones_VRC : IVRCSDKPreprocessAvatarCallback
{
    public int callbackOrder => 0;

    public bool OnPreprocessAvatar(GameObject avatarGameObject)
    {
        try
        {
            RemoveUnneededBones.Remove(avatarGameObject.transform);
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
        }
        return true;
    }
}
#endif
/*
 * Doesn't work due to CVR CCK not making a copy of the assets being uploaded, and saving them to scene after calling the Pre*BundleEvent 
#if CVR_CCK_EXISTS
[InitializeOnLoad]
public class RemoveUnneededBones_CVR
{
    static RemoveUnneededBones_CVR()
    {
        CCK_BuildUtility.PreAvatarBundleEvent.AddListener(OnPreprocess);
        CCK_BuildUtility.PrePropBundleEvent.AddListener(OnPreprocess);
    }

    public static void OnPreprocess(GameObject avatarGameObject)
    {
        try
        {
            //avatarGameObject = GameObject.Instantiate(avatarGameObject);
            RemoveUnneededBones.Remove(avatarGameObject.transform);
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
        }
    }
}
#endif
*/


[CustomEditor(typeof(RemoveUnneededBones))]
[CanEditMultipleObjects]
[DisallowMultipleComponent]
public class RemoveUnneededBones_Editor : Editor
{
    private const string EditorOnly = "EditorOnly";
    public override void OnInspectorGUI()
    {
        RemoveUnneededBones script = (RemoveUnneededBones)target;
        DrawDefaultInspector();
        if (GUILayout.Button("Do it now"))
        {
            var previousTag = script.gameObject.tag;
            var scriptName = script.GetType().Name;
            if (previousTag == EditorOnly)
            {
                Debug.LogWarning($"[{scriptName}] Won't execute when the root is tagged with {EditorOnly} tag!", script.gameObject);
                return;
            }
            script.gameObject.tag = scriptName;
            try
            {
                RemoveUnneededBones.Remove(script.transform, script.save);
            }
            catch  (Exception ex) 
            {
                Debug.LogException(ex, script.gameObject);
            }
            script.gameObject.tag = previousTag;
        }
    }
    public static void Clean(PlayModeStateChange state)
    {
        if (!state.HasFlag(PlayModeStateChange.ExitingEditMode))
            return;
        Debug.Log($"cleaned {RemoveUnneededBones.createdAssetNames.Count}");
        foreach (var item in RemoveUnneededBones.createdAssetNames)
        {
            AssetDatabase.DeleteAsset(item);
        }
        var all = AssetDatabase.FindAssets("RemoveUnneededBonesTempAsset-*", new string[] { "Assets/temp" });
        foreach (var item in all)
        {
            AssetDatabase.DeleteAsset(AssetDatabase.GUIDToAssetPath(item));
        }
    }
    [InitializeOnLoad]
    public static class PlayModeStateChangedExample
    {
        // register an event handler when the class is initialized
        static PlayModeStateChangedExample()
        {
            AddTag(nameof(RemoveUnneededBones));
            EditorApplication.playModeStateChanged += Clean;
        }

        private static void AddTag(string name)
        {
            var tagManager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            var tagsProp = tagManager.FindProperty("tags");
            if (PropertyExists(tagsProp, name))
                return;
            int index = tagsProp.arraySize;
            tagsProp.InsertArrayElementAtIndex(index);
            var sp = tagsProp.GetArrayElementAtIndex(index);
            sp.stringValue = name;
            tagManager.ApplyModifiedProperties();
        }

        private static bool PropertyExists(SerializedProperty property, string value)
        {
            if (property == null)
                return true; // so we don't try to add to a null property
            for (int i = 0; i < property.arraySize; i++)
                if (property.GetArrayElementAtIndex(i)?.stringValue?.Equals(value) == true)
                    return true;
            return false;
        }
    }
}
#endif