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
    public float MaxDistance = 0.5f;        //set it based on avatar scale/height
    public List<Renderer> RenderersToHide = new List<Renderer>();  //if a renderer is in both it will be hidden
    public List<Renderer> RenderersToShow = new List<Renderer>();
    public readonly Dictionary<Transform, RendererOptions.RendererOptionsEnum> skinnedMeshRendererTransformOptions = new Dictionary<Transform, RendererOptions.RendererOptionsEnum>();

    private const string OverrideHideTag = "[HideInView]";
    private const string OverrideShowTag = "[ShowInView]";
    private const string MainCamera = "MainCamera";
    private const UnityEngine.Rendering.ShadowCastingMode shadowsOnly = UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly;
    private static readonly Vector3 ZeroScale = new Vector3(0.0001f, 0.0001f, 0.0001f);

    private int ShadowCloneLayer = 0;
    private int frameIndex = 0;
    private bool isActive;
    private bool InitDone = false;
    private Vector3 headPosition;
    private Transform head;
    private Transform headZero;
    internal bool optionsChanged = false; 
    private MaterialPropertyBlock properties;

    private readonly List<Material> originalSmrSharedMaterials = new List<Material>();
    private readonly List<MeshData> meshDatas = new List<MeshData>();
    private readonly List<Renderer> _renderersToHide = new List<Renderer>();
    private readonly List<Renderer> _renderersToShow = new List<Renderer>();
    private readonly List<Renderer> removedFromHideList = new List<Renderer>();
    private readonly List<Renderer> removedFromShowList = new List<Renderer>();
    private readonly List<Renderer> otherRenderers = new List<Renderer>();
    private readonly List<SkinnedMeshRenderer> skinnedMeshRenderers = new List<SkinnedMeshRenderer>();
    private readonly List<Transform> transformsUnderGameObject = new List<Transform>();
    private readonly List<RendererClone> RendererShadowClones = new List<RendererClone>();
    private readonly HashSet<Transform> transformsUnderHeadSet = new HashSet<Transform>();

    private static readonly List<RendererOptions> allRendererOptions = new List<RendererOptions>();
    private static readonly List<Transform> tempTransforms = new List<Transform>();
    private static readonly List<Animator> tempAnimators = new List<Animator>();
    private static readonly HashSet<Renderer> tempRendererSet = new HashSet<Renderer>();
    private static readonly HashSet<Transform> tempTransformSet = new HashSet<Transform>();
    /// <summary>
    /// Bones to hide as key, their parent as value
    /// </summary>
    private readonly Dictionary<Transform, Transform> transformsToHide = new Dictionary<Transform, Transform>();
    private readonly Dictionary<Transform, Transform> headTransformsToHide = new Dictionary<Transform, Transform>();
    private readonly Dictionary<Transform, RendererOptions> rendererOptions = new Dictionary<Transform, RendererOptions>();
    private readonly Dictionary<Transform, Transform> boneToZeroBone = new Dictionary<Transform, Transform>();

    [Flags] public enum MeshOptions : byte
    {
        None = 0,
        Enabled = 1,
        Hide = 2,
        Shrink = 4,
        OverrideHide = 8,
        OverrideShow = 16,
    }

    class MeshData 
    {
        public MeshOptions meshOptions;
        public Transform[] originalBones;
        public Transform[] hiddenBones;
        public Renderer renderer;
        public SkinnedMeshRenderer skinnedMeshRenderer;
        public RendererOptions rendererOptions;
        public bool enabledState;
        public bool isSkinned;
        internal bool recalculate;
    }

    class RendererClone 
    {
        public Renderer original;
        public Renderer clone;
        public int BlendShapeCount;
        public bool enabled;
        public bool isSkinned;
        public Mesh sharedMesh;
        public Material[] originalMaterials;
        public Material[] cloneMaterials;
        public SkinnedMeshRenderer originalSmr;
        public SkinnedMeshRenderer cloneSmr;
        public float[] previousBlendshapeValues;
    }

    void Start() 
    {
        Initialize();
    }
    private void UpdatePerFrame()
    {
        if (!InitDone)
            Initialize();

        var frameCount = Time.frameCount;
        if (frameCount == frameIndex)
            return;

        frameIndex = frameCount;
        headPosition = head.position;

        SyncShadowClones();

        if (optionsChanged)
        {
            SetupTransformOverrides();
            Setup();
            optionsChanged = false;
            return;
        }
        if (!IsEqual(RenderersToHide, _renderersToHide) || !IsEqual(RenderersToShow, _renderersToShow))
            UpdateShowHide();
    }
    private bool IsEqual(List<Renderer> a, List<Renderer> b) 
    {
        if (a == null || b == null)
            return false;
        if (a.Count != b.Count)
            return false;
        for (int i = 0; i < a.Count; i++)
        {
            if (a[i] != b[i])
                return false;
        }
        return true;
    }
    internal void AddRendererOptions(RendererOptions options) 
    {
        if (!rendererOptions.ContainsKey(options.transform))
        {
            rendererOptions[options.transform] = options;
        }
    }
    internal void RemoveRendererOptions(Transform optionsTransform)
    {
        rendererOptions.Remove(optionsTransform);
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
        tempRendererSet.Clear();
        tempRendererSet.UnionWith(renderersIn);
        renderersIn.Clear();
        renderersIn.AddRange(tempRendererSet);
        tempRendererSet.Clear();
        for (int i = 0; i < renderersIn.Count; i++)
        {
            for (int j = 0; j < meshDatas.Count; j++)
            {
                var meshData = meshDatas[i];
                if (meshData.renderer == renderersIn[i]) 
                {
                    meshData.meshOptions = meshData.meshOptions.SetFlag(MeshOptions.OverrideHide, overrideHide);
                    meshData.meshOptions = meshData.meshOptions.SetFlag(MeshOptions.OverrideShow, overrideShow);
                }
            }
        }
    }
    public void Initialize()
    {
        properties = new MaterialPropertyBlock();
        ShadowCloneLayer = LayerMask.NameToLayer("PlayerClone");
        if (ShadowCloneLayer < 0) ShadowCloneLayer = 0;
        ClearAll();
        var animator = GetHumanAnimator(transform);
        if (animator && animator.isHuman)
        {
            isActive = true;
            head = animator.GetBoneTransform(HumanBodyBones.Head);
            headZero = GetZeroTransform(head, "HeadZeroBone");
            head.GetComponentsInChildren(true, transformsUnderGameObject);
            transformsUnderHeadSet.Clear();
            transformsUnderHeadSet.UnionWith(transformsUnderGameObject);
            foreach (var bone in transformsUnderGameObject)
            {
                headTransformsToHide[bone] = headZero;
            }
            transformsUnderGameObject.Clear();
        }
        else
        {
            //only works on humanoids (needs a head to hide)
            isActive = false;
            return;
        }
        gameObject.GetComponentsInChildren(true, allRendererOptions);
        foreach (var option in allRendererOptions)
            rendererOptions.Add(option.transform, option);
        allRendererOptions.Clear();
        gameObject.GetComponentsInChildren(true, transformsUnderGameObject); // all transforms
        SetupTransformOverrides();
        GetRenderes();
        Setup();
        InitDone = true;
    }
    private void OnEnable()
    {
        foreach (var data in meshDatas) 
        {
            if (data.recalculate && data.isSkinned && data.skinnedMeshRenderer) 
            {
                data.skinnedMeshRenderer.forceMatrixRecalculationPerRender = true;
            }
        }
        Camera.onPreRender += OnPreRenderShrink;
        Camera.onPostRender += OnPostRenderShrink;
    }
    private void OnDisable()
    {
        foreach (var data in meshDatas)
        {
            if (data.recalculate && data.isSkinned && data.skinnedMeshRenderer)
            {
                data.skinnedMeshRenderer.forceMatrixRecalculationPerRender = false;
            }
        }
        Camera.onPreRender -= OnPreRenderShrink;
        Camera.onPostRender -= OnPostRenderShrink;
    }

    private void Setup()
    {
        SetupSkinnedRenderers();
        SetupOtherRenderers();
        UpdateShowHide();
    }

    private void ClearAll()
    {
        allRendererOptions.Clear();
        rendererOptions.Clear();
        otherRenderers.Clear();
        skinnedMeshRenderers.Clear();
        _renderersToHide.Clear();
        _renderersToShow.Clear();
        removedFromHideList.Clear();
        removedFromShowList.Clear();
        transformsToHide.Clear();
        meshDatas.Clear();
        foreach (var item in RendererShadowClones)
            if (item.clone) 
                GameObject.Destroy(item.clone.gameObject);
         
        RendererShadowClones.Clear();
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
        var rendererCount = otherRenderers.Count;
        for (var i = 0; i < rendererCount; i++)
        {
            var otherRenderer = otherRenderers[i];
            var gameObject = otherRenderer.gameObject;
            var overrideOptions = GetRendererOptions(gameObject.transform);
            var show = gameObject.CompareTag(OverrideShowTag) || overrideOptions == RendererOptions.RendererOptionsEnum.ForceShow;
            var meshData = new MeshData
            {
                renderer = otherRenderer,
                isSkinned = false
            };
            meshDatas.Add(meshData);
            if (show)
                continue;
            var hide = gameObject.CompareTag(OverrideHideTag ) || overrideOptions == RendererOptions.RendererOptionsEnum.ForceHide;
            var renderShadowsOnly = otherRenderer.shadowCastingMode == shadowsOnly;
            var underHead = transformsUnderHeadSet.Contains(otherRenderer.gameObject.transform);
            if (hide || underHead && !renderShadowsOnly)
            {
                meshData.meshOptions |= MeshOptions.Hide;
                CreateShadowCopy(otherRenderer);
            }
        }
    }
    private void CreateShadowCopy(Renderer renderer) 
    {
        Renderer cloneRenderer = GameObject.Instantiate(renderer, renderer.transform);
        var cloneGameObject = cloneRenderer.gameObject;
        cloneGameObject.name = renderer.name + "-ShadowClone";
        cloneGameObject.transform.parent = renderer.transform;
        cloneGameObject.transform.localPosition = Vector3.zero;
        cloneGameObject.transform.localRotation = Quaternion.identity;
        cloneGameObject.layer = ShadowCloneLayer;
        cloneRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        var blendShapeCount = 0;
        var originalSmr = renderer as SkinnedMeshRenderer;
        var cloneSmr = cloneRenderer as SkinnedMeshRenderer;
        bool skinned = originalSmr;
        if (skinned)
        {
            blendShapeCount = originalSmr.sharedMesh ? originalSmr.sharedMesh.blendShapeCount : 0;
            skinned = blendShapeCount > 0;
        }
        var clone = new RendererClone
        {
            original = renderer,
            clone = cloneRenderer,
            BlendShapeCount = blendShapeCount,
            enabled = true,
            isSkinned = skinned,
            originalSmr = originalSmr,
            cloneSmr = cloneSmr,
            sharedMesh = originalSmr.sharedMesh,
            originalMaterials = originalSmr.sharedMaterials,
            cloneMaterials = cloneSmr.sharedMaterials,
            previousBlendshapeValues = new float[blendShapeCount],
        };
        RendererShadowClones.Add(clone);
        var previousValues = clone.previousBlendshapeValues;
        for (int i = 0; i < blendShapeCount; i++)
        {
            previousValues[i] = cloneSmr.GetBlendShapeWeight(i);
        }
    }

    private void SetupSkinnedRenderers()
    {
        for (var i = 0; i < skinnedMeshRenderers.Count; i++)
         {
            var skinnedMeshRenderer = skinnedMeshRenderers[i];
            var gameObject = skinnedMeshRenderer.gameObject;
            var overrideOptions = GetRendererOptions(gameObject.transform);
            var show = gameObject.CompareTag(OverrideShowTag) || overrideOptions == RendererOptions.RendererOptionsEnum.ForceShow; // name.Contains(OverrideShow)
            var hide = gameObject.CompareTag(OverrideHideTag) || overrideOptions == RendererOptions.RendererOptionsEnum.ForceHide;
            var meshData = new MeshData
            {
                renderer = skinnedMeshRenderer,
                skinnedMeshRenderer = skinnedMeshRenderer,
                isSkinned = true
            };
            meshDatas.Add(meshData);
            
            var bones = meshData.hiddenBones = meshData.originalBones = skinnedMeshRenderer.bones;

            if (show || skinnedMeshRenderer.shadowCastingMode == shadowsOnly)
            {
                //don't hide/shrink stuff that's shadows only or is tagged to be shown
                if (hide)
                {
                    meshData.meshOptions |= MeshOptions.Hide;
                    CreateShadowCopy(skinnedMeshRenderer);
                }
                continue;
            }
            var shrinkThisMesh = false;
            var hideThisMesh = false;
            var noHeadBones = meshData.hiddenBones.ToArray();
            meshData.hiddenBones = noHeadBones;
            var length = bones.Length;
            Array.Copy(bones, noHeadBones, length);
            if (bones != null && length > 0)
            {
                tempTransformSet.Clear();
                tempTransforms.Clear();
                tempTransforms.AddRange(bones);
                var mesh = skinnedMeshRenderer.sharedMesh;
                if (mesh.isReadable) 
                {
                    var weights = mesh.GetAllBoneWeights();
                    var weightsLength = weights.Length;
                    for (int j = 0; j < weightsLength; j++)
                    {
                        tempTransformSet.Add(bones[weights[j].boneIndex]);
                    }
                    for (int j = 0; j < tempTransforms.Count; j++)
                    {
                        var t = tempTransforms[j];
                        if (!tempTransformSet.Contains(t))
                        {
                            tempTransforms[j] = null;
                        }
                    }

                }
                //it has bones, check all bones if we want to hide them
                for (var j = 0; j < length; j++)
                {
                    var bone = tempTransforms[j];
                    if (!bone)
                        continue;
                    gameObject = bone.gameObject;
                    var otherHide = transformsToHide.ContainsKey(bone);
                    var headHide = transformsUnderHeadSet.Contains(bone);
                    var _overrideOptions = GetRendererOptions(bone);
                    var overrideShow = gameObject.CompareTag(OverrideShowTag) || _overrideOptions == RendererOptions.RendererOptionsEnum.ForceShow; //boneName.Contains(OverrideShow);
                    var overrideHide = gameObject.CompareTag(OverrideHideTag) || _overrideOptions == RendererOptions.RendererOptionsEnum.ForceHide;
                    if (((!headHide && !otherHide) || overrideShow) && !overrideHide)
                        continue;

                    if (!shrinkThisMesh)
                    {
                        shrinkThisMesh = true;
                    }
                    noHeadBones[j] = transformsToHide[bone];
                }

                meshData.recalculate = shrinkThisMesh;
                skinnedMeshRenderer.forceMatrixRecalculationPerRender = shrinkThisMesh;
            }
            else
            {
                //has no bones... we'll have to disable it if it's under the head bone

                shrinkThisMesh = hideThisMesh = transformsUnderHeadSet.Contains(skinnedMeshRenderer.gameObject.transform) &&
                    skinnedMeshRenderer.shadowCastingMode != shadowsOnly;
            }
            hideThisMesh |= hide;
            meshData.meshOptions = meshData.meshOptions.SetFlag(MeshOptions.Shrink, shrinkThisMesh);
            meshData.meshOptions = meshData.meshOptions.SetFlag(MeshOptions.Hide, hideThisMesh);
            if (shrinkThisMesh || hideThisMesh)
                CreateShadowCopy(skinnedMeshRenderer);
        }
    }

    private RendererOptions.RendererOptionsEnum GetRendererOptions(Transform search)
    {
        if (rendererOptions.TryGetValue(search, out var options)) 
        {
            if (options)
                return options.firstPersonVisibility;
            else
                rendererOptions.Remove(search);
        }
        if (skinnedMeshRendererTransformOptions.TryGetValue(search, out var value)) 
        {
            return value;
        }
        return RendererOptions.RendererOptionsEnum.Default;
    }

    private void ClearTransformOverrides()
    {
        transformsToHide.Clear();
       
        foreach (var item in headTransformsToHide.Keys)
        {
            transformsToHide[item] = headTransformsToHide[item];
        }
    }


    private void SetupTransformOverrides()
    {
        ClearTransformOverrides();
        foreach (var bone in transformsUnderGameObject)
        {
            var overrideOptions = GetRendererOptions(bone.gameObject.transform);
            var forceHide = bone.gameObject.CompareTag(OverrideHideTag) || overrideOptions == RendererOptions.RendererOptionsEnum.ForceHide;
            if (!bone || transformsToHide.ContainsKey(bone) || !forceHide)
                continue;
            bone.GetComponentsInChildren(true, tempTransforms);
            var zeroTransform = GetZeroTransform(bone, "ZeroBone");
            foreach (var childBone in tempTransforms)
            {
                transformsToHide[childBone] = zeroTransform;
                if (childBone.TryGetComponent<Renderer>(out var renderer))
                    RenderersToHide.Add(renderer);
            }
        }
        tempTransforms.Clear();
    }

    private Transform GetZeroTransform(Transform bone, string name)
    {
        if (!boneToZeroBone.TryGetValue(bone, out var zeroTransform))
        {
            var zeroBone = new GameObject
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            zeroTransform = zeroBone.transform;
            zeroTransform.name = name;
            zeroTransform.SetParent(bone);
            zeroTransform.localScale = ZeroScale;
            zeroTransform.localPosition = Vector3.zero;
            boneToZeroBone[bone] = zeroTransform;
        }

        return zeroTransform;
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
        foreach (var item in boneToZeroBone.Values)
        {
            GameObject.DestroyImmediate(item.gameObject);
        }
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
    private void SyncShadowClones()
    {
        foreach (var item in RendererShadowClones)
        {
            if (!item.enabled)
                continue;

            if (!item.original)
            {
                item.enabled = false;
                GameObject.Destroy(item.cloneSmr);
                continue;
            }
            bool isEnabled = item.clone.enabled = item.original.enabled;
            if (!isEnabled)
                continue;

            var originalSmr = item.originalSmr;
            var cloneSmr = item.cloneSmr;
            // materials                        
            originalSmr.GetSharedMaterials(originalSmrSharedMaterials);
            for (int i = 0; i < originalSmrSharedMaterials.Count; i++)
            {
                if (originalSmrSharedMaterials[i] != item.cloneMaterials[i]) 
                {
                    cloneSmr.sharedMaterials[i] = item.cloneMaterials[i] = originalSmrSharedMaterials[i];
                }
            }
            // material properties
            originalSmr.GetPropertyBlock(properties);
            cloneSmr.SetPropertyBlock(properties);

            // copy blendshapes
            if (item.isSkinned)
            {
                var previousValues = item.previousBlendshapeValues;
                for (int i = 0; i < item.BlendShapeCount; i++)
                {
                    var value = originalSmr.GetBlendShapeWeight(i);
                    if (previousValues[i] != value) 
                    {
                        cloneSmr.SetBlendShapeWeight(i, value);
                        previousValues[i] = value;
                    }
                }
            }
        }
    }
    public void OnPreRenderShrink(Camera cam)
    {
        UpdatePerFrame();
        isActive = Vector3.Distance(cam.transform.position, headPosition) < MaxDistance;
        if (!isActive || !cam.CompareTag(MainCamera))
            return;
        ShrinkMeshes();     
    }
    public void OnPostRenderShrink(Camera cam)
    {
        if (!isActive || !cam.CompareTag(MainCamera))
            return;
        UnShrinkMeshes();
    }

    private void ShrinkMeshes()
    {
        var meshDataCount = meshDatas.Count;
        for (var i = 0; i < meshDataCount; i++) 
        {
            var meshData = meshDatas[i];
            var options = meshData.meshOptions;
            var overrideHide = options.HasFlag(MeshOptions.OverrideHide);
            var overrideShow = options.HasFlag(MeshOptions.OverrideShow);
            var shrink = options.HasFlag(MeshOptions.Shrink);
            if (meshData.isSkinned)
            {
                var skinnedMeshRenderer = meshData.renderer as SkinnedMeshRenderer;
                if (!skinnedMeshRenderer)
                    continue;
                if (shrink && !overrideHide && !overrideShow)
                {
                    skinnedMeshRenderer.bones = meshData.hiddenBones;
                    continue;
                }
                meshData.enabledState = skinnedMeshRenderer.enabled;
                if (overrideHide && !overrideShow)
                {
                    skinnedMeshRenderer.enabled = false;
                }
            }
            else
            {
                var renderer = meshData.renderer;
                var hide = options.HasFlag(MeshOptions.Hide);
                if (renderer && (hide || overrideHide) && !overrideShow)
                {
                    var enabled = meshData.enabledState = renderer.enabled;
                    if (enabled)
                        renderer.enabled = false;
                }
            }
        }
    }

    private void UnShrinkMeshes()
    {
        var meshDataCount = meshDatas.Count;
        for (var i = 0; i < meshDataCount; i++) 
        {
            var meshData = meshDatas[i];
            var options = meshData.meshOptions;
            var overrideHide = options.HasFlag(MeshOptions.OverrideHide);
            var overrideShow = options.HasFlag(MeshOptions.OverrideShow);
            if (meshData.isSkinned)
            {
                var skinnedMeshRenderer = meshData.renderer as SkinnedMeshRenderer;
                if (!skinnedMeshRenderer)
                    continue;
                var shrink = options.HasFlag(MeshOptions.Shrink);
                if (shrink && !overrideShow && !overrideHide)
                {
                    skinnedMeshRenderer.bones = meshData.originalBones;
                }
                if (overrideHide || overrideShow)
                {
                    skinnedMeshRenderer.enabled = meshData.enabledState;
                }
            }
            else
            {
                var renderer = meshData.renderer;
                var hide = options.HasFlag(MeshOptions.Hide);
                if (renderer && (hide || overrideHide) && !overrideShow)
                {
                    renderer.enabled = meshData.enabledState;
                }
            }
        }
    }
   
#if UNITY_EDITOR
    [InitializeOnLoad]
    public static class TagInit
    {
        static TagInit()
        {
            AddTag(OverrideHideTag);
            AddTag(OverrideShowTag);
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