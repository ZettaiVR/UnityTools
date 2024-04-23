using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System;
using UnityEditor.Build;

public class BakeBlendshapesBuildIntergration
{
    private const string Path = "Tools/Zettai/";
    private const string MMDText = "Keep MMD blendshapes";
    private const string BakeEnabled = "Bake static blendshapes";
    private const string OnBuildEnabled = "Enable blendshape removal on build";
    private const string MMDTextPath = Path + MMDText;
    private const string BakeEnabledPath = Path + BakeEnabled;
    private const string OnBuildEnabledPath = Path + OnBuildEnabled;
    private static bool enableOnBuild = true;
    private static bool mmd = true;
    private static bool bake = true;

    [MenuItem(OnBuildEnabledPath, false, 1)]
    private static void EnableOnBuild()
    {
        enableOnBuild = !enableOnBuild;
    }
    [MenuItem(OnBuildEnabledPath, true, 1)]
    private static bool EnableOnBuildValidate()
    {
        Menu.SetChecked(OnBuildEnabledPath, enableOnBuild);
        return true;
    }
    [MenuItem(BakeEnabledPath, false, 2)]
    private static void EnableBake()
    {
        bake = !bake;
    }
    [MenuItem(BakeEnabledPath, true, 2)]
    private static bool EnableBakeValidate()
    {
        Menu.SetChecked(BakeEnabledPath, bake);
        return true;
    }
    [MenuItem(MMDTextPath, false, 3)]
    private static void EnableMMD()
    {
        mmd = !mmd;
    }
    [MenuItem(MMDTextPath, true, 3)]
    private static bool EnableMMDValidate()
    {
        Menu.SetChecked(MMDTextPath, mmd);
        return true;
    }
    internal static void FindControllersCVRAvatar(GameObject avatarGameObject, List<string> keepBlendshapes, List<RuntimeAnimatorController> controllers)
    {
#if CVR_CCK_EXISTS
        var avatar = avatarGameObject.GetComponent<ABI.CCK.Components.CVRAvatar>();
        if (!avatar)
        {
            return;
        }
        controllers.Add(avatar.overrides);
        if (avatar.bodyMesh)
            return;

        var path = AnimationUtility.CalculateTransformPath(avatar.bodyMesh.transform, avatarGameObject.transform);
        var visemes = avatar.visemeBlendshapes;
        if (avatar.useVisemeLipsync && visemes != null)
        {
            if (avatar.visemeMode == ABI.CCK.Components.CVRAvatar.CVRAvatarVisemeMode.Visemes)
            {
                for (int i = 0; i < visemes.Length; i++)
                {
                    if (!string.IsNullOrEmpty(visemes[i]))
                        keepBlendshapes.Add(path + "/" + visemes[i]);
                }
            }
            else if (avatar.visemeMode == ABI.CCK.Components.CVRAvatar.CVRAvatarVisemeMode.SingleBlendshape)
            {
                if (!string.IsNullOrEmpty(visemes[0]))
                    keepBlendshapes.Add(path + "/" + visemes[0]);
            }
        }
        if (avatar.blinkBlendshape != null)
        {
            for (int i = 0; i < avatar.blinkBlendshape.Length; i++)
            {
                if (!string.IsNullOrEmpty(avatar.blinkBlendshape[i]))
                    keepBlendshapes.Add(path + "/" + avatar.blinkBlendshape[i]);
            }
        }
#endif
    }
    internal static void FindControllersCVRProp(GameObject avatarGameObject, List<string> keepBlendshapes, List<RuntimeAnimatorController> controllers)
    {
#if CVR_CCK_EXISTS
        var prop = avatarGameObject.GetComponent<ABI.CCK.Components.CVRSpawnable>();
        if (!prop)
            return;
        if (!avatarGameObject.TryGetComponent<Animator>(out var animator) || !animator || !animator.runtimeAnimatorController)
            return;
        controllers.Add(animator.runtimeAnimatorController);
#endif
    }
    internal static void FindControllersVRCSDK3(GameObject avatarGameObject, List<string> keepBlendshapes, List<RuntimeAnimatorController> controllers)
    {
#if VRC_SDK_VRCSDK3
        var avatar3 = avatarGameObject.GetComponent<VRC.SDK3.Avatars.Components.VRCAvatarDescriptor>();
        if (!avatar3)
            return;
        
        if (avatar3.lipSync == VRC.SDKBase.VRC_AvatarDescriptor.LipSyncStyle.JawFlapBlendShape)
        {
            if (avatar3.VisemeSkinnedMesh)
            {
                var path = AnimationUtility.CalculateTransformPath(avatar3.VisemeSkinnedMesh.transform, avatarGameObject.transform);
                keepBlendshapes.Add(path + "/" + avatar3.MouthOpenBlendShapeName);
            }
        }
        else if (avatar3.lipSync == VRC.SDKBase.VRC_AvatarDescriptor.LipSyncStyle.VisemeBlendShape)
        {
            if (avatar3.VisemeSkinnedMesh && avatar3.VisemeBlendShapes != null)
            {
                var path = AnimationUtility.CalculateTransformPath(avatar3.VisemeSkinnedMesh.transform, avatarGameObject.transform);
                var visemes = avatar3.VisemeBlendShapes;
                for (int i = 0; i < visemes.Length; i++)
                {
                    if (!string.IsNullOrEmpty(visemes[i]))
                        keepBlendshapes.Add(path + "/" + visemes[i]);
                }
            }
        }
        var eyelids = avatar3.customEyeLookSettings.eyelidsBlendshapes;
        var eyelidMesh = avatar3.customEyeLookSettings.eyelidsSkinnedMesh;
        if (eyelids != null && eyelids.Length > 0 && eyelidMesh && eyelidMesh.sharedMesh)
        {
            var path = AnimationUtility.CalculateTransformPath(eyelidMesh.transform, avatarGameObject.transform);
            for (int i = 0; i < eyelids.Length; i++)
            {
                var name = eyelidMesh.sharedMesh.GetBlendShapeName(eyelids[i]);
                if (!string.IsNullOrEmpty(name))
                    keepBlendshapes.Add(path + "/" + name);
            }
        }
        if (avatar3.customizeAnimationLayers)
        {
            var list = new List<VRC.SDK3.Avatars.Components.VRCAvatarDescriptor.CustomAnimLayer>();
            list.AddRange(avatar3.baseAnimationLayers);
            list.AddRange(avatar3.specialAnimationLayers);
            for (int i = 0; i < list.Count; i++)
            {
                var item = list[i];
                var valid = !item.isDefault;
                if (valid)
                {
                    controllers.Add(item.animatorController);
                }
            }
        }
#endif
    }
    internal static void FindControllersVRCSDK2(GameObject avatarGameObject, List<string> keepBlendshapes, List<RuntimeAnimatorController> controllers)
    {
#if VRC_SDK_VRCSDK2
        var avatar2 = avatarGameObject.GetComponent<VRCSDK2.VRC_AvatarDescriptor>();
        if (!avatar2)
            return;
        
        var eyelidMesh = avatar2.VisemeSkinnedMesh;
        if (eyelidMesh && eyelidMesh.sharedMesh && eyelidMesh.sharedMesh.blendShapeCount >= 4)
        {
            var path = AnimationUtility.CalculateTransformPath(eyelidMesh.transform, avatarGameObject.transform);
            for (int i = 0; i < 4; i++)
            {
                keepBlendshapes.Add(path + "/" + eyelidMesh.sharedMesh.GetBlendShapeName(i));
            }
            var visemes = avatar2.VisemeBlendShapes;
            if (avatar2.lipSync == VRC.SDKBase.VRC_AvatarDescriptor.LipSyncStyle.JawFlapBlendShape)
            {
                keepBlendshapes.Add(path + "/" + avatar2.MouthOpenBlendShapeName);
            }
            else if (avatar2.lipSync == VRC.SDKBase.VRC_AvatarDescriptor.LipSyncStyle.VisemeBlendShape && visemes != null)
            {
                for (int i = 0; i < visemes.Length; i++)
                {
                    keepBlendshapes.Add(path + "/" + visemes[i]);
                }
            }
        }
        controllers.Add(avatar2.CustomStandingAnims);
        controllers.Add(avatar2.CustomSittingAnims);
#endif
    }

#if VRC_SDK_VRCSDK2 || VRC_SDK_VRCSDK3
    public class BakeBlendshapes_VRC : VRC.SDKBase.Editor.BuildPipeline.IVRCSDKPreprocessAvatarCallback
    {
        public int callbackOrder => 0;
        public bool OnPreprocessAvatar(GameObject avatarGameObject)
        {
            try
            {
                var animator = avatarGameObject.GetComponent<Animator>();
                if (!animator)
                    return true;
                var keepBlendshapes = new List<string>();
                var controllers = new List<RuntimeAnimatorController>();
                FindControllersVRCSDK2(avatarGameObject, keepBlendshapes, controllers);
                FindControllersVRCSDK3(avatarGameObject, keepBlendshapes, controllers); 
                keepBlendshapes.RemoveAll(a => string.IsNullOrEmpty(a));
                BakeBlendshapes.Process(animator, keepBlendshapes, controllers, true, true, false, true);
            }
            catch (Exception ex) { Debug.LogException(ex); }
            return true;
        }
    }
    public class BakeBlendshapes_VRC_Clenup : VRC.SDKBase.Editor.BuildPipeline.IVRCSDKPostprocessAvatarCallback 
    {
        int IOrderedCallback.callbackOrder => 0;

        void VRC.SDKBase.Editor.BuildPipeline.IVRCSDKPostprocessAvatarCallback.OnPostprocessAvatar()
        {
            BakeBlendshapes.CleanupTemp();
        }
    }
#endif

#if CVR_CCK_EXISTS
    [InitializeOnLoad]
    public class BakeBlendshapes_CVR
    {
        static BakeBlendshapes_CVR()
        {
            ABI.CCK.Scripts.Editor.CCK_BuildUtility.PreAvatarBundleEvent.AddListener(OnPreprocess);
            ABI.CCK.Scripts.Editor.CCK_BuildUtility.PrePropBundleEvent.AddListener(OnPreprocess);
        }

        public static void OnPreprocess(GameObject avatarGameObject)
        {
            try
            {
                Animator animator = avatarGameObject.GetComponent<Animator>();
                if (!animator)
                    return;

                var keepBlendshapes = new List<string>();
                var controllers = new List<RuntimeAnimatorController>();
                FindControllersCVRAvatar(avatarGameObject, keepBlendshapes, controllers);
                FindControllersCVRProp(avatarGameObject, keepBlendshapes, controllers);
                keepBlendshapes.RemoveAll(a => string.IsNullOrEmpty(a));
                BakeBlendshapes.Process(animator, keepBlendshapes, controllers, true, true, false, true);
            }
            catch (Exception ex) { Debug.LogException(ex); }
        }
    }
    [InitializeOnLoad]
    public static class PlayModeStateChanged
    {
        static PlayModeStateChanged()
        {
            EditorApplication.playModeStateChanged += Clean;
        }

        private static void Clean(PlayModeStateChange change)
        {
            if (!change.HasFlag(PlayModeStateChange.ExitingPlayMode))
            {
                return;
            }
            BakeBlendshapes.CleanupTemp();
        }
    }
#endif
}