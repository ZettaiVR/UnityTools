using UnityEngine;
using System;
using Zettai;
#if UNITY_EDITOR
using UnityEditor;

#if VRC_SDK_VRCSDK2 || VRC_SDK_VRCSDK3
public class RemoveUnneededBones_VRC : VRC.SDKBase.Editor.BuildPipeline.IVRCSDKPreprocessAvatarCallback
{
    public int callbackOrder => 0;

    public bool OnPreprocessAvatar(GameObject avatarGameObject)
    {
        if (!RemoveUnneededBones_Editor.enableOnBuild)
            return true;
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

#if CVR_CCK_EXISTS
[InitializeOnLoad]
public class RemoveUnneededBones_CVR
{
    static RemoveUnneededBones_CVR()
    {
        ABI.CCK.Scripts.Editor.CCK_BuildUtility.PreAvatarBundleEvent.AddListener(OnPreprocess);
        ABI.CCK.Scripts.Editor.CCK_BuildUtility.PrePropBundleEvent.AddListener(OnPreprocess);
    }

    public static void OnPreprocess(GameObject avatarGameObject)
    {
        if (!RemoveUnneededBones_Editor.enableOnBuild)
            return;
        try
        {
            RemoveUnneededBones.Remove(avatarGameObject.transform);
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
        }
    }
}
#endif


[CustomEditor(typeof(RemoveUnneededBones))]
[CanEditMultipleObjects]
[DisallowMultipleComponent]
public class RemoveUnneededBones_Editor : Editor
{
    private const string Path = "Tools/Zettai/";
    private const string RemoveUnneededBonesText = "Remove unneeded bones on build";
    private const string RemoveUnneededBonesTextPath = Path + RemoveUnneededBonesText;
    public static bool enableOnBuild = true;
    [MenuItem(RemoveUnneededBonesTextPath, false, 1)]
    private static void EnableOnBuild()
    {
        enableOnBuild = !enableOnBuild;
    }
    [MenuItem(RemoveUnneededBonesTextPath, true, 1)]
    private static bool EnableOnBuildValidate()
    {
        Menu.SetChecked(RemoveUnneededBonesTextPath, enableOnBuild);
        return true;
    }

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
            EditorApplication.playModeStateChanged += Clean;
        }
    }
}
#endif