using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif
public static class MirrorAvatarExtentions 
{
    public static MirrorAvatar.MeshOptions SetFlag(this MirrorAvatar.MeshOptions flags, MirrorAvatar.MeshOptions flag, bool value)
    {
        if (value)
            flags |= flag;
        else
            flags &= ~flag;

        return flags;
    }
}
public class MirrorAvatar : MonoBehaviour
{
    private const string OverrideHide = "[HideInView]";
    private const string OverrideShow = "[ShowInView]";
    private const string MainCamera = "MainCamera";
    private static readonly Vector3 ZeroScale = new Vector3(0.0001f, 0.0001f, 0.0001f);
    private const UnityEngine.Rendering.ShadowCastingMode shadowsOnly = UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly;
    private MeshOptions[] skinnedMeshOptions;
    private MeshOptions[] otherMeshOptions;
    private bool[] enabledStateMesh;
    private bool[] enabledStateSkinnedMesh;
    private bool isActive;
    public bool InitDone = false;
    private Transform head;
    public float MaxDistance = 0.5f;        //set it based on avatar scale/height
    public List<Renderer> RenderersToHide = new List<Renderer>();  //if a renderer is in both it will be hidden
    public List<Renderer> RenderersToShow = new List<Renderer>();
    private readonly List<Renderer> _renderersToHide = new List<Renderer>();
    private readonly List<Renderer> _renderersToShow = new List<Renderer>();
    private readonly List<Renderer> removedFromHideList = new List<Renderer>();
    private readonly List<Renderer> removedFromShowList = new List<Renderer>();
    private readonly List<Renderer> otherRenderers = new List<Renderer>();
    private readonly List<SkinnedMeshRenderer> skinnedMeshRenderers = new List<SkinnedMeshRenderer>();
    private readonly List<Transform> zeroTransforms = new List<Transform>();
    private readonly List<Transform[]> originalBones = new List<Transform[]>();
    private readonly List<Transform[]> hiddenBones = new List<Transform[]>();
    private static readonly List<Transform> transformsUnderGameObject = new List<Transform>();
    private static readonly List<Transform> tempTransforms = new List<Transform>();
    private static readonly List<Animator> tempAnimators = new List<Animator>();
    private static readonly HashSet<Transform> transformsUnderHeadSet = new HashSet<Transform>();
    private static readonly HashSet<Renderer> rendererSet = new HashSet<Renderer>();
    /// <summary>
    /// Bones to hide as key, their parent as value
    /// </summary>
    private static readonly Dictionary<Transform, Transform> transformsToHide = new Dictionary<Transform, Transform>();

   
    void Start() 
    {
        Initialize();
    }
    private void LateUpdate()
    {
        if (!InitDone)
            Initialize();
        if (RenderersToHide.SequenceEqual(_renderersToHide) && RenderersToShow.SequenceEqual(_renderersToShow))
            return;
        UpdateShowHide();
    }
    private void UpdateShowHide() 
    {
        Add(removedFromHideList, _renderersToHide, RenderersToHide);
        Add(removedFromShowList, _renderersToShow, RenderersToShow);
        ModifyOverrides(removedFromHideList, overrideHide: false, overrideShow: false);
        ModifyOverrides(removedFromShowList, overrideHide: false, overrideShow: false);
        ModifyOverrides(RenderersToShow, overrideHide: false, overrideShow: true);
        ModifyOverrides(RenderersToHide, overrideHide: true, overrideShow: false);
        _renderersToHide.Clear();
        _renderersToShow.Clear();
        _renderersToHide.AddRange(RenderersToHide);
        _renderersToShow.AddRange(RenderersToShow);
    }
    private static void Add(List<Renderer> ToFill, List<Renderer> AddFrom, List<Renderer> Containing)
    {
        ToFill.Clear();
        foreach (var value in AddFrom)
        {
            if (Containing.Contains(value))
                ToFill.Add(value);
        }
    }
    
    private void ModifyOverrides(List<Renderer> renderersIn, bool overrideHide, bool overrideShow)
    {
        if (!renderersIn.Any())
            return;
        rendererSet.Clear();
        rendererSet.UnionWith(renderersIn);
        renderersIn.Clear();
        renderersIn.AddRange(rendererSet);
        rendererSet.Clear();
        for (int i = 0; i < renderersIn.Count; i++)
        {
            int index;
            if (renderersIn[i] is SkinnedMeshRenderer smr)
            {
                index = skinnedMeshRenderers.IndexOf(smr);
                if (index >= 0)
                {
                    skinnedMeshOptions[i] = skinnedMeshOptions[i].SetFlag(MeshOptions.OverrideHide, overrideHide);
                    skinnedMeshOptions[i] = skinnedMeshOptions[i].SetFlag(MeshOptions.OverrideShow, overrideShow);
                }
            }
            index = otherRenderers.IndexOf(renderersIn[i]);
            if (index >= 0)
            {
                otherMeshOptions[i] = otherMeshOptions[i].SetFlag(MeshOptions.OverrideHide, overrideHide);
                otherMeshOptions[i] = otherMeshOptions[i].SetFlag(MeshOptions.OverrideShow, overrideShow);
            }
        }
    }
    public void Initialize()
    {
        ClearAll();
        var animator = GetHumanAnimator(transform);
        if (animator && animator.isHuman)
        {
            isActive = true;
            head = animator.GetBoneTransform(HumanBodyBones.Head);
            var zeroBone = new GameObject
            {
                name = "HeadZeroBone",
                hideFlags = HideFlags.HideAndDontSave,
            };
            var zeroTransform = zeroBone.transform;
            zeroTransform.SetParent(head);
            zeroTransform.localScale = ZeroScale;
            zeroTransform.localPosition = Vector3.zero;
            zeroTransforms.Add(zeroTransform);
            head.GetComponentsInChildren(true, transformsUnderGameObject);
            transformsUnderHeadSet.Clear();
            transformsUnderHeadSet.UnionWith(transformsUnderGameObject);
            foreach (var bone in transformsUnderGameObject)
            {
                transformsToHide[bone] = zeroTransform;
            }
            transformsUnderGameObject.Clear();

        }
        else
        {
            //only works on humanoids (needs a head to hide)
            isActive = false;
            return;
        }
        SetupTransformOverrides();
        GetRenderes();
        SetupSkinnedRenderers();
        SetupOtherRenderers();
        UpdateShowHide();
        Camera.onPreRender += OnPreRenderShrink;
        Camera.onPostRender += OnPostRenderShrink;
        InitDone = true;
    }

    private void ClearAll()
    {
        skinnedMeshOptions = null;
        otherMeshOptions = null;
        enabledStateMesh = null;
        enabledStateSkinnedMesh = null;
        originalBones.Clear();
        hiddenBones.Clear();
        otherRenderers.Clear();
        skinnedMeshRenderers.Clear();
        _renderersToHide.Clear();
        _renderersToShow.Clear();
        transformsToHide.Clear();
        removedFromHideList.Clear();
        removedFromShowList.Clear();
        foreach (var item in zeroTransforms)
        {
            GameObject.DestroyImmediate(item.gameObject);
        }
        zeroTransforms.Clear();
    }

    private static Animator GetHumanAnimator(Transform root) 
    {
        if (root.TryGetComponent(out Animator animator) && animator.isHuman)
            return animator;
        foreach (Transform child in root)
        {
            if (child.TryGetComponent(out animator) && animator.isHuman)
                return animator;
        }
        root.GetComponentsInChildren(true, tempAnimators);
        if (tempAnimators.Count == 0)
            return null;
        animator = tempAnimators[0];
        if (tempAnimators.Count == 1 && animator && animator.isHuman)
            return animator;

        var min = int.MaxValue;
        var minIndex = -1;
        for (int i = 0; i < tempAnimators.Count; i++)
        {
            var tempAnimator = tempAnimators[i];
            if (!tempAnimator || !tempAnimator.isHuman)
                continue;
            var depth = GetDepth(root, tempAnimator.transform);
            if (min > depth)
            {
                min = depth;
                minIndex = i;
            }
        }
        if (minIndex > 0)
            return tempAnimators[minIndex];
        return null;
    }
    private static int GetDepth(Transform root, Transform target) 
    {
        if (target == null)
            return -1;
        if (target == root)
            return 0;
        if (target.parent == root)
            return 1;
        // easy cases done 

        var depth = 1;
        var parent = target.parent;
        while (parent && parent != root) 
        {
            depth++;
            parent = parent.parent;
        }
        return depth;
    }

    private void SetupOtherRenderers()
    {
        otherMeshOptions = new MeshOptions[otherRenderers.Count];
        enabledStateMesh = new bool[otherRenderers.Count];
        var rendererCount = otherRenderers.Count;
        for (var i = 0; i < rendererCount; i++)
        {
            var otherRenderer = otherRenderers[i];
            var gameObject = otherRenderer.gameObject;
            //var name = otherRenderer.name;
            var show = gameObject.CompareTag(OverrideShow); //name.Contains(OverrideShow);
            if (show)
                continue;
            var hide = gameObject.CompareTag(OverrideHide);//name.Contains(OverrideHide);
            var renderShadowsOnly = otherRenderer.shadowCastingMode == shadowsOnly;
            var underHead = transformsUnderHeadSet.Contains(otherRenderer.gameObject.transform);
            if (hide || underHead && !renderShadowsOnly)
                otherMeshOptions[i] |= MeshOptions.Hide;
        }
    }

    private void SetupSkinnedRenderers()
    {
        skinnedMeshOptions = new MeshOptions[skinnedMeshRenderers.Count];
        enabledStateSkinnedMesh = new bool[skinnedMeshRenderers.Count];
        var empty = Array.Empty<Transform>();
        for (var i = 0; i < skinnedMeshRenderers.Count; i++)
        {
            var skinnedMeshRenderer = skinnedMeshRenderers[i];
            var gameObject = skinnedMeshRenderer.gameObject;
            //var name = skinnedMeshRenderer.name;
            var show = gameObject.CompareTag(OverrideShow); // name.Contains(OverrideShow)
            if (show || skinnedMeshRenderer.shadowCastingMode == shadowsOnly)
            {
                //don't hide/shrink stuff that's shadows only or is tagged to be shown
                var hide = gameObject.CompareTag(OverrideHide);//name.Contains(OverrideHide);
                if (hide)
                    skinnedMeshOptions[i] |= MeshOptions.Hide;
                originalBones.Add(empty);
                hiddenBones.Add(empty);
                continue;
            }
            var shrinkThisMesh = false;
            var hideThisMesh = false;
            var bones = skinnedMeshRenderer.bones;
            var noHeadBones = bones;
            originalBones.Add(bones);
            if (bones != null && bones.Length > 0)
            {
                //it has bones
                for (var j = 0; j < bones.Length; j++)
                {
                    var bone = bones[j];
                    gameObject = bone.gameObject;
                    //var boneName = bone.name;
                    var otherHide = transformsToHide.ContainsKey(bone);
                    var headHide = transformsUnderHeadSet.Contains(bone);
                    var overrideShow = gameObject.CompareTag(OverrideShow); //boneName.Contains(OverrideShow);
                    if ((!headHide && !otherHide) || overrideShow)
                        continue;

                    if (!shrinkThisMesh)
                    {
                        shrinkThisMesh = true;
                        noHeadBones = bones.ToArray(); // another array with the same content
                    }
                    noHeadBones[j] = transformsToHide[bone];
                }
                skinnedMeshRenderer.forceMatrixRecalculationPerRender = shrinkThisMesh;
            }
            else
            {
                //has no bones... we'll have to disable it if it's under the head bone

                shrinkThisMesh = hideThisMesh = transformsUnderHeadSet.Contains(skinnedMeshRenderer.gameObject.transform) &&
                    skinnedMeshRenderer.shadowCastingMode != shadowsOnly;
            }
            hideThisMesh |= gameObject.CompareTag(OverrideHide); //name.Contains(OverrideHide);
            hiddenBones.Add(noHeadBones);
            if (shrinkThisMesh)
                skinnedMeshOptions[i] |= MeshOptions.Shrink;
            if (hideThisMesh)
                skinnedMeshOptions[i] |= MeshOptions.Hide;
        }
    }

    private void SetupTransformOverrides()
    {
        gameObject.GetComponentsInChildren(true, transformsUnderGameObject); // all transforms
        foreach (var bone in transformsUnderGameObject)
        {
            if (!bone || transformsToHide.ContainsKey(bone) || !bone.gameObject.CompareTag(OverrideHide))  //|| !bone.name.Contains(OverrideHide) )
                continue;
            bone.GetComponentsInChildren(true, tempTransforms);
            var zeroBone = new GameObject
            {
                hideFlags = HideFlags.HideAndDontSave,
                name = "ZeroBone",
            };
            var zeroTransform = zeroBone.transform;
            zeroTransform.SetParent(bone);
            zeroTransform.localScale = ZeroScale;
            zeroTransform.localPosition = Vector3.zero;
            zeroTransforms.Add(zeroTransform);
            transformsToHide[bone] = zeroTransform;
            foreach (var childBone in tempTransforms)
            {
                transformsToHide[childBone] = zeroTransform;
                if (childBone.TryGetComponent<Renderer>(out var renderer))
                    RenderersToHide.Add(renderer);
            }
        }
        transformsUnderGameObject.Clear();
        tempTransforms.Clear();
    }
    private void GetRenderes()
    {
        gameObject.GetComponentsInChildren(true, skinnedMeshRenderers);
        if (gameObject.TryGetComponent<SkinnedMeshRenderer>(out var smr))
            skinnedMeshRenderers.Add(smr);
        gameObject.GetComponentsInChildren(true, otherRenderers);
        if (gameObject.TryGetComponent<Renderer>(out var renderer))
            otherRenderers.Add(renderer);
        RemoveSMR(otherRenderers, skinnedMeshRenderers);
    }

    private void OnDestroy()
    {
        Camera.onPreRender -= OnPreRenderShrink;
        Camera.onPostRender -= OnPostRenderShrink;
    }
    private void RemoveSMR(List<Renderer> MRList, List<SkinnedMeshRenderer> SMRList) 
    {
        if (MRList == null || SMRList == null || MRList.Count == 0 || SMRList.Count == 0) 
            return;
        for (int i = 0; i < SMRList.Count; i++)
        {
            var item = SMRList[i];
            MRList.Remove(item);
        }
    }
    public void OnPreRenderShrink(Camera cam)
    {
        isActive = Vector3.Distance(cam.transform.position, head.position) < MaxDistance;
        if (!isActive || !cam.CompareTag(MainCamera))
            return;

        var count = skinnedMeshOptions.Length;
        for (var i = 0; i < count; i++)
        {
            var options = skinnedMeshOptions[i];
            var skinnedMeshRenderer = skinnedMeshRenderers[i];
            var overrideHide = options.HasFlag(MeshOptions.OverrideHide);
            var overrideShow = options.HasFlag(MeshOptions.OverrideShow);
            var shrink = options.HasFlag(MeshOptions.Shrink);
            if (shrink && !overrideHide && !overrideShow)
            {
                skinnedMeshRenderer.bones = hiddenBones[i];
                continue;
            }
            if (overrideHide && !overrideShow)
            {
                enabledStateSkinnedMesh[i] = skinnedMeshRenderer.enabled;
                skinnedMeshRenderer.enabled = false;
            }
        }
        count = otherMeshOptions.Length;
        for (var i = 0; i < count; i++)
        {
            var options = otherMeshOptions[i];
            var overrideHide = options.HasFlag(MeshOptions.OverrideHide);
            var overrideShow = options.HasFlag(MeshOptions.OverrideShow);
            var hide = options.HasFlag(MeshOptions.Hide);
            if ((hide || overrideHide) && !overrideShow)
            {
                var renderer = otherRenderers[i];
                var enabled = enabledStateMesh[i] = renderer.enabled;
                if (enabled)
                    renderer.enabled = false;
            }
        }
    }
    public void OnPostRenderShrink(Camera cam)
    {
        if (!isActive || !cam.CompareTag(MainCamera))
            return;
        var count = skinnedMeshOptions.Length;

        for (var i = 0; i < count; i++)
        {
            var options = skinnedMeshOptions[i];
            var skinnedMeshRenderer = skinnedMeshRenderers[i];
            var overrideHide = options.HasFlag(MeshOptions.OverrideHide);
            var overrideShow = options.HasFlag(MeshOptions.OverrideShow);
            var shrink = options.HasFlag(MeshOptions.Shrink);
            if (shrink && !overrideShow && !overrideHide)
            {
                skinnedMeshRenderer.bones = originalBones[i];
            }
            if (overrideHide || overrideShow)
            {
                skinnedMeshRenderer.enabled = enabledStateSkinnedMesh[i];
            }
        }
        count = otherMeshOptions.Length;
        for (var i = 0; i < count; i++)
        {
            var options = otherMeshOptions[i];
            var renderer = otherRenderers[i];
            var hide = options.HasFlag(MeshOptions.Hide);
            var overrideHide = options.HasFlag(MeshOptions.OverrideHide);
            var overrideShow = options.HasFlag(MeshOptions.OverrideShow);

            if ((hide || overrideHide) && !overrideShow)
            {
                renderer.enabled = enabledStateMesh[i];
            }
        }
    }
    [Flags] public enum MeshOptions : byte
    {
        None = 0,
        Enabled = 1,
        Hide = 2,
        Shrink = 4,
        OverrideHide = 8,
        OverrideShow = 16,
    }
#if UNITY_EDITOR
    [InitializeOnLoad]
    public static class TagInit
    {
        static TagInit()
        {
            AddTag(OverrideHide);
            AddTag(OverrideShow);
        }
        private static void AddTag(string name)
        {
            var tagManager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")?[0]);
            var tagsProp = tagManager?.FindProperty("tags");
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
                return true;
            for (int i = 0; i < property.arraySize; i++)
                if (property.GetArrayElementAtIndex(i)?.stringValue?.Equals(value) == true)
                    return true;
            return false;
        }
    }
#endif   
}