using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Profiling;
using UnityEngine;

public class MirrorAvatar : MonoBehaviour
{
    [Tooltip("Max distance the main camera can be from the head bone to shrink.")][Range(0f, 10f)]
    public float MaxDistance = 0.5f;        //set it based on avatar scale/height
    public List<Renderer> RenderersToHide = new List<Renderer>();  //if a renderer is in both it will be hidden
    public List<Renderer> RenderersToShow = new List<Renderer>();
    public readonly Dictionary<Transform, RendererOptions.RendererOptionsEnum> skinnedMeshRendererTransformOptions = new Dictionary<Transform, RendererOptions.RendererOptionsEnum>();
    [Tooltip("When false we scale the head to 0.0001f, when true we scale to 0f. Must be set before initialization.")]
    public bool trueZeroScale = false;
    [Tooltip("Should sync blendshape values to shadow clones?")]
    public bool syncBlendshapes = true;
    [Tooltip("Should unshrink head after rendering with the main camera? Turning it off can save some performance when only the main camera is rendering, but might cause some issues.")]
    public bool unshrinkAfterRender = false;
    [Tooltip("Should use a copy of the meshes instead of swapping the bones arrays? This can save performance by not requiring recalculating meshes, but best used when you can have them on their own rendering layers.")]
    public bool useRendererCopies = true;
    [Tooltip("Create the copy of the meshes on startup. Has a significant performance impact.")]
    public bool createRendererCopies = true;

    private bool wasUseRendererCopies = false;

    private const UnityEngine.Rendering.ShadowCastingMode shadowsOnly = UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly;
    private const string MainCamera = "MainCamera";
    private const string PlayerCloneLayerName = "PlayerLocal"; //"PlayerClone";
    private const string HeadZeroBoneName = "HeadZeroBone";
    private const string ShadowCloneSuffix = "-ShadowClone";
    private const string RendererCloneName = "RendererClones";
    private const string FirstPersonCloneSuffix = "-FirstPersonClone";
    private const string ZeroBoneName = "ZeroBone";
    private static readonly Vector3 CloseZeroScale = new Vector3(0.0001f, 0.0001f, 0.0001f);
    private static readonly Vector3 TrueZeroScale = new Vector3(0f, 0f, 0f);
    private static readonly Vector3 NaN3 = new Vector3(float.NaN, float.NaN, float.NaN);

    private int PlayerCloneLayer = 0;
    private int frameIndex = 0;
    private int cameraRenders = 0;
    private bool isActive;
    private bool isShrunk;
    private bool InitDone = false;
    private bool forceMatrixRecalculationPerRender;
    private bool singleCameraInLastFrame = false;
    private bool syncDone = false;
    private Vector3 headPosition;
    private Transform head;
    private Transform headZero;
    internal bool optionsChanged = false; 
    private MaterialPropertyBlock properties;
    private string rendererCloneName = RendererCloneName;

    private readonly List<Material> originalSmrSharedMaterials = new List<Material>();
    private readonly Dictionary<Renderer, MeshData> meshDatas = new Dictionary<Renderer, MeshData>();
    private readonly List<Renderer> _renderersToHide = new List<Renderer>();
    private readonly List<Renderer> _renderersToShow = new List<Renderer>();
    private readonly List<Renderer> removedFromHideList = new List<Renderer>();
    private readonly List<Renderer> removedFromShowList = new List<Renderer>();
    private readonly List<Renderer> otherRenderers = new List<Renderer>();
    private readonly List<SkinnedMeshRenderer> skinnedMeshRenderers = new List<SkinnedMeshRenderer>();
    private readonly List<Transform> transformsUnderGameObject = new List<Transform>();
    private readonly HashSet<Transform> transformsUnderHeadSet = new HashSet<Transform>();

    private static readonly List<RendererOptions> allRendererOptions = new List<RendererOptions>();
    private static readonly List<Component> components = new List<Component>();
    private static readonly List<Transform> tempTransforms = new List<Transform>();
    private static readonly List<Animator> tempAnimators = new List<Animator>();
    private static readonly HashSet<Transform> tempTransformsSet = new HashSet<Transform>();

    /// <summary>
    /// Bones to hide as key, their parent as value
    /// </summary>
    private readonly Dictionary<Transform, Transform> transformsToHide = new Dictionary<Transform, Transform>();
    private readonly Dictionary<Transform, Transform> headTransformsToHide = new Dictionary<Transform, Transform>();
    private readonly Dictionary<Transform, RendererOptions> m_RendererOptions = new Dictionary<Transform, RendererOptions>();
    private readonly Dictionary<Transform, Transform> boneToZeroBone = new Dictionary<Transform, Transform>();
    private readonly List<JobData> weightResults = new List<JobData>();


    private static readonly ProfilerMarker s_TestMarker = new ProfilerMarker(ProfilerCategory.Scripts, "Tested Marker", Unity.Profiling.LowLevel.MarkerFlags.Script);

    [Flags] public enum MeshOptions : byte
    {
        None = 0,
        Enabled = 1,
        Hide = 2,
        Shrink = 4,
        OverrideHide = 8,
        OverrideShow = 16,
    }

    [BurstCompile]
    struct WeightJob : IJobParallelFor
    {
        [NativeDisableParallelForRestriction] [WriteOnly] public NativeArray<bool> results;
        [ReadOnly] public NativeArray<BoneWeight1> weights;
        [BurstCompile]
        public void Execute(int index)
        {
            results[weights[index].boneIndex] = true;
        }
    }
    class JobData 
    {
        public JobHandle handle;
        public NativeArray<bool> results;
        public MeshData meshData;

        public JobData(MeshData meshData, NativeArray<bool> results, JobHandle handle)
        {
            this.meshData = meshData;
            this.results = results;
            this.handle = handle;
        }
    }
    class MeshData
    {
        public bool isShrinked;
        public MeshOptions meshOptions;
        public Transform[] originalBones;
        public Transform[] hiddenBones;
        public bool[] existingBones;
        public GameObject target;
        public Renderer renderer;
        public SkinnedMeshRenderer skinnedMeshRenderer;
        public SkinnedMeshRenderer visibleCloneSmr;
        public Renderer shadowCloneRenderer;
        public RendererOptions rendererOptions;
        public bool enabledState;
        public bool isSkinned;
        internal bool recalculate;

        public int BlendShapeCount;
        public bool hasBlendshapes;
        public bool hasVisibleClone;
        public bool hasShadowClone;
        public Mesh sharedMesh;
        public Material[] originalMaterials;
        public Material[] cloneMaterials;
        public float[] previousBlendshapeValues;

        public void SetupSync() 
        {
            hasVisibleClone = visibleCloneSmr;
            hasShadowClone = shadowCloneRenderer;
            sharedMesh = skinnedMeshRenderer ? skinnedMeshRenderer.sharedMesh : null;
            BlendShapeCount = sharedMesh ? sharedMesh.blendShapeCount : 0;
            hasBlendshapes = BlendShapeCount > 0;
            originalMaterials = renderer ? renderer.sharedMaterials : null;
            cloneMaterials = shadowCloneRenderer ? shadowCloneRenderer.sharedMaterials : null;
            previousBlendshapeValues = BlendShapeCount > 0 ? new float[BlendShapeCount] : null;
        }
        public MeshData(Renderer original, Renderer shadowClone)
        {
            renderer = original;
            skinnedMeshRenderer = renderer as SkinnedMeshRenderer;
            shadowCloneRenderer = shadowClone;
            isSkinned = skinnedMeshRenderer;
        }
        public MeshData(Renderer original) 
        {
            renderer = original;
            skinnedMeshRenderer = renderer as SkinnedMeshRenderer;
            isSkinned = skinnedMeshRenderer;
        }
    }

    void Start() 
    {
        rendererCloneName = RendererCloneName + GetInstanceID();        
        Initialize();
    }
    private void UpdatePerFrame()
    {
        if (!InitDone)
            Initialize();

        var frameCount = Time.frameCount;
        if (frameCount == frameIndex)
            return;

        syncDone = false;
        cameraRenders = 0;
        frameIndex = frameCount;
        headPosition = head.position;

        if ((wasUseRendererCopies != useRendererCopies) && useRendererCopies)
        {
            wasUseRendererCopies = useRendererCopies;
            useRendererCopies = false;
            UnShrinkMeshes(false);
            useRendererCopies = true;
        }
        return;
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
        if (!m_RendererOptions.ContainsKey(options.transform))
        {
            m_RendererOptions[options.transform] = options;
        }
    }
    internal void RemoveRendererOptions(Transform optionsTransform)
    {
        m_RendererOptions.Remove(optionsTransform);
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
        if (renderersIn.Count == 0)
            return;

        for (int i = 0; i < renderersIn.Count; i++)
        {
            if (!meshDatas.TryGetValue(renderersIn[i], out var meshData) || meshData.renderer != renderersIn[i])            
                continue;
            
            meshData.meshOptions = meshData.meshOptions.SetFlag(MeshOptions.OverrideHide, overrideHide);
            meshData.meshOptions = meshData.meshOptions.SetFlag(MeshOptions.OverrideShow, overrideShow);
        }
    }
    public void Initialize()
    {
        properties = new MaterialPropertyBlock();

        PlayerCloneLayer = LayerMask.NameToLayer(PlayerCloneLayerName);
        if (PlayerCloneLayer < 0)
            PlayerCloneLayer = 0;

        ClearAll();

        gameObject.GetComponentsInChildren(true, allRendererOptions);
        foreach (var option in allRendererOptions)
            m_RendererOptions.Add(option.transform, option);
        allRendererOptions.Clear();

        var animator = GetHumanAnimator(transform);
        if (animator && animator.isHuman)
        {
            isActive = true;
            head = animator.GetBoneTransform(HumanBodyBones.Head);
            headZero = GetZeroTransform(head, HeadZeroBoneName);
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
        gameObject.GetComponentsInChildren(true, transformsUnderGameObject); // all transforms
        SetupTransformOverrides();
        GetRenderes();
        Setup();
        InitDone = true;
    }

    private void Setup()
    {
        SetupSkinnedRenderers();
        SetupOtherRenderers();
        UpdateRenderers();
    }
    private void UpdateRenderers() 
    {
        UpdateShowHide();
        UpdateSkinnedRenderers();
        UpdateOtherRenderers();
    }

    private void ClearAll()
    {
        allRendererOptions.Clear();
        m_RendererOptions.Clear();
        otherRenderers.Clear();
        skinnedMeshRenderers.Clear();
        _renderersToHide.Clear();
        _renderersToShow.Clear();
        removedFromHideList.Clear();
        removedFromShowList.Clear();
        transformsToHide.Clear();
        foreach (var item in meshDatas.Values)
        {
            if (item.shadowCloneRenderer)
                GameObject.Destroy(item.shadowCloneRenderer.gameObject);
            if (item.visibleCloneSmr)
                GameObject.Destroy(item.visibleCloneSmr.gameObject);
        }
        meshDatas.Clear();
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
        var transformsCanBeHidden = tempTransformsSet;
        var rendererCount = otherRenderers.Count;
        for (var i = 0; i < rendererCount; i++)
        {
            var otherRenderer = otherRenderers[i];
            var otherGameObject = otherRenderer.gameObject;
            if (otherGameObject.hideFlags != HideFlags.None || !transformsCanBeHidden.Contains(otherRenderer.transform) || otherRenderer is ParticleSystemRenderer or LineRenderer or TrailRenderer)
                continue;

            var shadowClone = CreateShadowCopy(otherRenderer, null, otherGameObject);
            var meshData = new MeshData(otherRenderer, shadowClone);
            meshDatas.Add(otherRenderer, meshData);
            meshData.SetupSync();
        }
        transformsCanBeHidden.Clear();
    }
    private void UpdateOtherRenderers()
    {
        var rendererCount = otherRenderers.Count;
        for (var i = 0; i < rendererCount; i++)
        {
            var otherRenderer = otherRenderers[i];
            if (!meshDatas.TryGetValue(otherRenderer, out var meshData))
                continue;
            
            meshData.isShrinked = false;
            var gameObject = otherRenderer.gameObject;
            var overrideOptions = GetRendererOptions(gameObject.transform);
            var show = overrideOptions == RendererOptions.RendererOptionsEnum.ForceShow;
            if (show)
                continue;
            var hide = overrideOptions == RendererOptions.RendererOptionsEnum.ForceHide;
            var renderShadowsOnly = otherRenderer.shadowCastingMode == shadowsOnly;
            var underHead = transformsUnderHeadSet.Contains(otherRenderer.gameObject.transform);
            if (hide || underHead && !renderShadowsOnly)
            {
                meshData.meshOptions |= MeshOptions.Hide;
                meshData.shadowCloneRenderer.enabled = true;
                meshData.isShrinked = true;
            }
        }
    }
    private Renderer CreateShadowCopy(Renderer renderer, SkinnedMeshRenderer visibleClone, GameObject target)
    {
        if (!target)
            target = CreateGameObjectAsChild(renderer.gameObject, rendererCloneName, HideFlags.HideAndDontSave);
        var shadowClone = CreateClone(renderer, target, ShadowCloneSuffix, PlayerCloneLayer);
            shadowClone.shadowCastingMode = shadowsOnly;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        
        return shadowClone;
    }

    private T CreateClone<T>(T original, GameObject target, string nameSuffix, int layer) where T : Renderer
    {
        var originalGameObject = original.gameObject;
        var originalTransform = originalGameObject.transform;
        var name = original.name + nameSuffix;
        var detachedChildren = false;
        var targetTransform = target.transform;
        var targetChildCount = targetTransform.childCount;
        var originalChildCount = originalTransform.childCount;
        GameObject _tempGameObject = null;
        try
        {
            if (originalChildCount > 0)
            {
                for (int i = 0; i < targetChildCount; i++)
                {
                    var childTransform = targetTransform.GetChild(i);
                    if (childTransform.TryGetComponent(out T existing) && childTransform.name == name)
                    {
                        return existing;
                    }
                }
                tempTransforms.Clear();
                detachedChildren = true;

                _tempGameObject = new GameObject();
                _tempGameObject.hideFlags = HideFlags.HideAndDontSave;
                _tempGameObject.SetActive(originalGameObject.activeInHierarchy);
                var tempTransform = _tempGameObject.transform;

                for (int i = 0; i < originalChildCount; i++)
                {
                    tempTransforms.Add(originalTransform.GetChild(i));
                }
                for (int i = 0; i < tempTransforms.Count; i++)
                {
                    tempTransforms[i].SetParent(tempTransform);
                }
            }
            T clone = GameObject.Instantiate(original, target.transform);
            var cloneGameObject = clone.gameObject;
            var cloneTransform = cloneGameObject.transform;
            cloneTransform.SetParent(target.transform);
            cloneTransform.localPosition = Vector3.zero;
            cloneTransform.localRotation = Quaternion.identity;
            cloneGameObject.name = name;
            cloneGameObject.layer = layer;
            cloneGameObject.hideFlags = HideFlags.DontSave;
            cloneGameObject.GetComponents(components);
            for (int i = 0; i < components.Count; i++)
            {
                var component = components[i];  
                if (!component || component is Transform or T or MeshFilter)
                    continue;
                // doesn't handle required components
                Destroy(component);
            }
            return clone;
        }
        finally
        {
            if (detachedChildren)
            {
                for (int i = 0; i < tempTransforms.Count; i++)
                {
                    tempTransforms[i].SetParent(originalTransform);
                }
                if (_tempGameObject)
                {
                    Destroy(_tempGameObject);
                }
            }
            tempTransforms.Clear();
        }
    }
    
    private void UpdateSkinnedRenderers() 
    {
        for (var i = 0; i < skinnedMeshRenderers.Count; i++) 
        {
            var skinnedMeshRenderer = skinnedMeshRenderers[i];
            if (!meshDatas.TryGetValue(skinnedMeshRenderer, out var meshData))
                continue;

            meshData.isShrinked = false;
            
            var overrideOptions = GetRendererOptions(gameObject.transform);
            var show = overrideOptions == RendererOptions.RendererOptionsEnum.ForceShow;
            var hide = overrideOptions == RendererOptions.RendererOptionsEnum.ForceHide;
            if (show || skinnedMeshRenderer.shadowCastingMode == shadowsOnly)
            {
                //don't hide/shrink stuff that's shadows only or is tagged to be shown
                if (hide)
                {
                    meshData.meshOptions |= MeshOptions.Hide;
                }
                continue;
            }
            if (meshData.hiddenBones != null && meshData.hiddenBones.Length > 0)
            {
                //it has bones, check all bones if we want to hide them
                CheckMeshForShrinkBones(meshData, hide);
            }
            else
            {
                //has no bones... we'll have to disable it if it's under the head bone
                CheckMeshForShrinkNoBones(meshData, hide);
            }
        }
    }
    private void SetupSkinnedRenderers()
    {
        weightResults.Clear();
        weightResults.Capacity = skinnedMeshRenderers.Count;
        for (var i = 0; i < skinnedMeshRenderers.Count; i++)
        {
            var skinnedMeshRenderer = skinnedMeshRenderers[i];
            var gameObject = skinnedMeshRenderer.gameObject;
            if (gameObject.hideFlags != HideFlags.None)
            {
                continue;
            }
            var meshData = new MeshData(skinnedMeshRenderer);
            meshDatas.Add(skinnedMeshRenderer, meshData);
            meshData.originalBones = skinnedMeshRenderer.bones;
            meshData.hiddenBones = meshData.originalBones?.ToArray();
            meshData.existingBones = new bool[meshData.originalBones?.Length ?? 0];
            var bones = meshData.originalBones;
            if (bones != null && bones.Length > 0)
            {
                var mesh = skinnedMeshRenderer.sharedMesh;
                if (mesh)
                {
                    var weights = mesh.GetAllBoneWeights();
                    var weightsLength = weights.Length;
                    var results = new NativeArray<bool>(bones.Length, Allocator.TempJob);
                    var job = new WeightJob { results = results, weights = weights };
                    var handle = job.Schedule(weightsLength, 1024);
                    JobHandle.ScheduleBatchedJobs();
                    weightResults.Add(new JobData(meshData, results, handle));
                }
            }
        }
        tempTransformsSet.Clear();
        
        var transformCanBeHidden = tempTransformsSet;
        foreach (var options in m_RendererOptions) 
        {
            var transform = options.Key;
            transform.GetComponentsInChildren(true, tempTransforms);
            transformCanBeHidden.Add(transform);
            transformCanBeHidden.UnionWith(tempTransforms);
        }
        transformCanBeHidden.UnionWith(transformsUnderHeadSet);

        for (int i = 0; i < weightResults.Count; i++)
        {
            var results = weightResults[i].results;
            var meshData = weightResults[i].meshData;
            var existingBones = meshData.existingBones.AsSpan();
            var bones = meshData.originalBones.AsSpan();
            var handle = weightResults[i].handle;
            handle.Complete();
            results.AsSpan().CopyTo(existingBones);
            results.Dispose();
            bool needsClone = false;
            for (int j = 0; j < existingBones.Length; j++)
            {
                if (existingBones[j] && transformCanBeHidden.Contains(bones[j])) 
                {
                    needsClone = true;
                    break;
                }
            }
            if (needsClone)
            {
                CreateClones(meshData);
                meshData.SetupSync();
            }
        }
        weightResults.Clear();
    }
    private void CheckMeshForShrinkNoBones(MeshData meshData, bool hide)
    {
        var hideThisMesh = transformsUnderHeadSet.Contains(meshData.skinnedMeshRenderer.gameObject.transform) &&
            meshData.skinnedMeshRenderer.shadowCastingMode != shadowsOnly;
        hideThisMesh |= hide;
        meshData.meshOptions = meshData.meshOptions.SetFlag(MeshOptions.Shrink, hideThisMesh);
        meshData.meshOptions = meshData.meshOptions.SetFlag(MeshOptions.Hide, hideThisMesh);
        meshData.isShrinked = hideThisMesh;

        if (!meshData.hasVisibleClone || !meshData.hasShadowClone)
            return;

        if (hideThisMesh)
        {
            meshData.shadowCloneRenderer.enabled = true;
            meshData.visibleCloneSmr.transform.localScale = useRendererCopies ? Vector3.one : Vector3.zero;
        }
        else
        {
            meshData.shadowCloneRenderer.enabled = false;
            meshData.visibleCloneSmr.transform.localScale = Vector3.zero;
        }
    }
    private void CreateClones(MeshData meshData)
    {
        var skinnedMeshRenderer = meshData.skinnedMeshRenderer;
        var target = meshData.target;
        var visibleClone = meshData.visibleCloneSmr;
        var shadowClone = meshData.shadowCloneRenderer;
        if (!target)
            target = CreateGameObjectAsChild(skinnedMeshRenderer.gameObject, rendererCloneName, HideFlags.HideAndDontSave);
        if (createRendererCopies)
        {
            if (!visibleClone)
            {
                visibleClone = meshData.visibleCloneSmr = CreateClone(skinnedMeshRenderer, target, FirstPersonCloneSuffix, PlayerCloneLayer);
            }
            visibleClone.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            visibleClone.forceMatrixRecalculationPerRender = false;
            visibleClone.transform.localScale = Vector3.zero;
        }
        if (!shadowClone)
            meshData.shadowCloneRenderer = CreateShadowCopy(skinnedMeshRenderer, visibleClone, target);
    }
    private void CheckMeshForShrinkBones(MeshData meshData, bool hideThisMesh)
    {
        var existingBones = meshData.existingBones;
        var bones = meshData.originalBones;
        var noHeadBones = meshData.hiddenBones;
        bones.AsSpan().CopyTo(noHeadBones);
        bool shrinkThisMesh = false;
        for (var j = 0; j < bones.Length; j++)
        {
            if (!existingBones[j])      // skip bones that don't have weights on the mesh
                continue;

            var bone = bones[j];
            if (!bone)
                continue;
            var otherHide = transformsToHide.ContainsKey(bone);
            var headHide = transformsUnderHeadSet.Contains(bone);
            var _overrideOptions = GetRendererOptions(bone);
            var overrideShow = _overrideOptions == RendererOptions.RendererOptionsEnum.ForceShow;
            var overrideHide = _overrideOptions == RendererOptions.RendererOptionsEnum.ForceHide;
            var hide = otherHide | headHide | overrideHide;
            if (hide && noHeadBones[j] != transformsToHide[bone])
            {
                // if not hidden, hide
                noHeadBones[j] = transformsToHide[bone];
            }
            else
            {
                // if not hidden, show
                if (noHeadBones[j] != bones[j])
                {
                    noHeadBones[j] = bones[j];
                }
            }
            if (((!headHide && !otherHide) || overrideShow) && !overrideHide)
                continue;

            if (!shrinkThisMesh)
            {
                shrinkThisMesh = true;
            }
        }
        meshData.isShrinked = shrinkThisMesh;
        meshData.recalculate = shrinkThisMesh;
        if (shrinkThisMesh)
        {
            meshData.meshOptions = meshData.meshOptions.SetFlag(MeshOptions.Shrink, shrinkThisMesh);
            meshData.meshOptions = meshData.meshOptions.SetFlag(MeshOptions.Hide, hideThisMesh);
        }

        if (!shrinkThisMesh || !meshData.hasVisibleClone)
            return;

        var visibleClone = meshData.visibleCloneSmr;
        visibleClone.enabled = useRendererCopies;
        visibleClone.bones = noHeadBones;
        visibleClone.transform.localScale = useRendererCopies ? Vector3.one : Vector3.zero;
    }

    private static GameObject CreateGameObjectAsChild(GameObject gameObject, string name, HideFlags hideFlags)
    {
        var transform = gameObject.transform;
        var childCount = transform.childCount;
        for (int i = 0; i < childCount; i++)
        {
            var item = transform.GetChild(i);
            if (item.hideFlags == hideFlags && item.name == name)
                return item.gameObject;
        }
        var target = new GameObject();
        var targetTransform = target.transform;
        targetTransform.SetParent(transform);
        targetTransform.localPosition = Vector3.zero;
        targetTransform.localRotation = Quaternion.identity;
        targetTransform.localScale = Vector3.one;
        target.hideFlags = hideFlags;
        target.layer = gameObject.layer;
        target.name = name;
        return target;
    }

    private RendererOptions.RendererOptionsEnum GetRendererOptions(Transform search)
    {
        if (m_RendererOptions.TryGetValue(search, out var options)) 
        {
            if (options && options.isActiveAndEnabled)
                return options.firstPersonVisibility;
            else
                m_RendererOptions.Remove(search);
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
        transformsToHide.EnsureCapacity(headTransformsToHide.Count);

        foreach (var item in headTransformsToHide.Keys)
        {
            transformsToHide[item] = headTransformsToHide[item];
        }
        foreach (var item in meshDatas.Values)
        {
            if (item.shadowCloneRenderer)
                item.shadowCloneRenderer.enabled = false;
            if (item.visibleCloneSmr)
                item.visibleCloneSmr.transform.localScale = Vector3.zero;
        }
    }

    private void SetupTransformOverrides()
    {
        ClearTransformOverrides();
        foreach (var bone in transformsUnderGameObject)
        {
            var overrideOptions = GetRendererOptions(bone);
            var forceHide = overrideOptions == RendererOptions.RendererOptionsEnum.ForceHide;
            if (!forceHide || !bone || transformsToHide.ContainsKey(bone))
                continue;
            bone.GetComponentsInChildren(true, tempTransforms);
            var zeroTransform = GetZeroTransform(bone, ZeroBoneName);
            for (int i = 0; i < tempTransforms.Count; i++)
            {
                var childBone = tempTransforms[i];
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
            var zeroBone = CreateGameObjectAsChild(bone.gameObject, name, HideFlags.HideAndDontSave);               
            zeroTransform = zeroBone.transform;
            zeroTransform.localScale = trueZeroScale ? TrueZeroScale : CloseZeroScale;
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

    private void RemoveSMR(List<Renderer> MRList, List<SkinnedMeshRenderer> SMRList) 
    {
        if (MRList == null || SMRList == null || MRList.Count == 0 || SMRList.Count == 0) 
            return;

        var renderers = new HashSet<Renderer>();
        renderers.UnionWith(SMRList);
        for (int i = MRList.Count - 1; i >= 0; i--)
        {
            if (renderers.Contains(MRList[i]))
                MRList.RemoveAt(i);
        }
    }
    private void SyncClones()
    {
        foreach (var item in meshDatas.Values)
        {
            if (!item.isShrinked)
                continue;

            if (!item.skinnedMeshRenderer)
            {
                item.isShrinked = false;
                if (item.shadowCloneRenderer)
                    GameObject.Destroy(item.shadowCloneRenderer);
                if (item.visibleCloneSmr)
                    GameObject.Destroy(item.visibleCloneSmr);
                continue;
            }
            bool isEnabled = item.skinnedMeshRenderer.enabled;

            item.hasShadowClone = item.hasShadowClone && item.shadowCloneRenderer;
            item.hasVisibleClone = item.hasVisibleClone && item.visibleCloneSmr;

            if (item.hasVisibleClone)
                item.visibleCloneSmr.transform.localScale = useRendererCopies ? Vector3.one : Vector3.zero;

            if (!isEnabled)
                continue;

            SyncMaterials(item);

            if (syncBlendshapes && item.hasBlendshapes)
            {
                SyncBlendshapes(item);
            }
        }
    }

    private static void SyncBlendshapes(MeshData meshData)
    {
        var shadowClone = meshData.shadowCloneRenderer as SkinnedMeshRenderer;
        var visibleClone = meshData.visibleCloneSmr;
        var hasVisibleClone = meshData.hasVisibleClone;
        var hasShadowClone = meshData.hasShadowClone && shadowClone;
        var blendShapeCount = meshData.BlendShapeCount;
        var previousValues = meshData.previousBlendshapeValues;
        var originalSmr = meshData.skinnedMeshRenderer;

        for (int i = 0; i < blendShapeCount; i++)
        {
            var value = originalSmr.GetBlendShapeWeight(i);
            if (previousValues[i] != value)
            {
                if (hasShadowClone)
                    shadowClone.SetBlendShapeWeight(i, value);
                if (hasVisibleClone)
                    visibleClone.SetBlendShapeWeight(i, value);
                previousValues[i] = value;
            }
        }
    }
    private void SyncMaterials(MeshData meshData)
    {
        var cloneMaterials = meshData.cloneMaterials;
        var original = meshData.renderer;
        var shadowClone = meshData.shadowCloneRenderer;
        var visibleClone = meshData.visibleCloneSmr;
        var hasVisibleClone = meshData.hasVisibleClone;
        var hasShadowClone = meshData.hasShadowClone;

        if (cloneMaterials == null || !original)
            return;

        // materials
        original.GetSharedMaterials(originalSmrSharedMaterials);
        bool changed = false;
        for (int i = 0; i < originalSmrSharedMaterials.Count; i++)
        {
            if (originalSmrSharedMaterials[i] != cloneMaterials[i])
            {
                cloneMaterials[i] = originalSmrSharedMaterials[i];
                changed = true;
                continue;
            }
        }
        if (changed)
        {
            if (hasShadowClone)
                shadowClone.sharedMaterials = cloneMaterials;
            if (hasVisibleClone)
                visibleClone.sharedMaterials = cloneMaterials;
        }
        // material properties
        original.GetPropertyBlock(properties);
        if (hasShadowClone)
            shadowClone.SetPropertyBlock(properties); 
        if (hasVisibleClone)
            visibleClone.SetPropertyBlock(properties);
    }

    public void OnPreRenderShrink(Camera cam)
    {
        UpdatePerFrame();
        bool isMain = cam.CompareTag(MainCamera);
        cameraRenders++;
        isActive = isMain && Vector3.Distance(cam.transform.position, headPosition) < MaxDistance; 
        if (!isActive)
        {
            if (isShrunk)
            {
                UnShrinkMeshes(true);
            }
            return;
        }
        if (!syncDone)
            SyncOncePerFrame();

        if (!isShrunk)
        {
            ShrinkMeshes();
        }
        else if (!useRendererCopies && singleCameraInLastFrame && forceMatrixRecalculationPerRender)
        { 
            SetRecalculation(false);
        }
    }

    public void OnPostRenderShrink(Camera cam)
    {
        var unshrink = unshrinkAfterRender && cameraRenders > 1;

        if (!cam.CompareTag(MainCamera))
           return;

        singleCameraInLastFrame = cameraRenders == 1;
        if (!isActive)
            return;

        if (isShrunk && unshrink)
        {
            UnShrinkMeshes(false);
        }
    }

    private void SyncOncePerFrame()
    {
        SyncClones();
        syncDone = true;
        if (optionsChanged)
        {
            SetupTransformOverrides();
            UpdateRenderers();
            optionsChanged = false;
            return;
        }
        if (!IsEqual(RenderersToHide, _renderersToHide) || !IsEqual(RenderersToShow, _renderersToShow))
            UpdateShowHide();
    }

    private void ShrinkMeshes()
    {
        isShrunk = true;
        foreach (var meshDataPairs in meshDatas)
        {
            var meshData = meshDataPairs.Value;
            var options = meshData.meshOptions;
            var overrideHide = options.HasFlag(MeshOptions.OverrideHide);
            var overrideShow = options.HasFlag(MeshOptions.OverrideShow);
            var shrink = options.HasFlag(MeshOptions.Shrink);
            if (meshData.isSkinned)
            {
                var skinnedMeshRenderer = meshData.skinnedMeshRenderer;
                if (!skinnedMeshRenderer)
                    continue;

                if (useRendererCopies && meshData.hasVisibleClone && !meshData.visibleCloneSmr.enabled)
                    meshData.visibleCloneSmr.enabled = true;

                meshData.enabledState = skinnedMeshRenderer.enabled;
                if (overrideHide && !overrideShow)
                {
                    skinnedMeshRenderer.enabled = false;
                    continue;   // if the renderer is not show, no reason to change bones
                }

                if (shrink && !overrideHide && !overrideShow && skinnedMeshRenderer.isVisible)
                {
                    if (useRendererCopies)
                    {
                        if (meshData.hasVisibleClone)
                        {
                            skinnedMeshRenderer.enabled = false;
                            skinnedMeshRenderer.forceMatrixRecalculationPerRender = false;

                            meshData.visibleCloneSmr.transform.localScale = Vector3.one;
                            unshrinkAfterRender = true;
                        }
                    }
                    else
                    {
                        forceMatrixRecalculationPerRender = skinnedMeshRenderer.forceMatrixRecalculationPerRender = true;
                        skinnedMeshRenderer.bones = meshData.hiddenBones;
                    }
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
    private void SetRecalculation(bool value)
    {
        foreach (var meshDataPairs in meshDatas)
        {
            var meshData = meshDataPairs.Value;
            var options = meshData.meshOptions;
            var overrideHide = options.HasFlag(MeshOptions.OverrideHide);
            var shrink = options.HasFlag(MeshOptions.Shrink);
            if (meshData.isSkinned && meshData.recalculate)
            {
                var skinnedMeshRenderer = meshData.skinnedMeshRenderer;
                if (skinnedMeshRenderer && shrink && !overrideHide)
                {
                    forceMatrixRecalculationPerRender = skinnedMeshRenderer.forceMatrixRecalculationPerRender = value;
                }
            }
        }
    }

    private void UnShrinkMeshes(bool recalculate)
    {
        foreach (var meshDataPairs in meshDatas)
        {
            var meshData = meshDataPairs.Value;
            var options = meshData.meshOptions;
            var overrideHide = options.HasFlag(MeshOptions.OverrideHide);
            var overrideShow = options.HasFlag(MeshOptions.OverrideShow);
            if (meshData.isSkinned)
            {
                var skinnedMeshRenderer = meshData.skinnedMeshRenderer;
                if (!skinnedMeshRenderer)
                    continue;
                var shrink = options.HasFlag(MeshOptions.Shrink);
                if (shrink && !overrideShow && !overrideHide)
                {
                    if (useRendererCopies)
                    {
                        if (meshData.hasVisibleClone)
                        {
                            if (meshData.enabledState)
                                skinnedMeshRenderer.enabled = true;

                            meshData.visibleCloneSmr.transform.localScale =
#if UNITY_EDITOR
                                Vector3.zero;
#else
                                NaN3;
#endif
                        }
                    }
                    else
                    {
                        if (meshData.enabledState)
                            skinnedMeshRenderer.bones = meshData.originalBones;
                        forceMatrixRecalculationPerRender = skinnedMeshRenderer.forceMatrixRecalculationPerRender = recalculate;
                    }                    
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
        isShrunk = false;
    }
    private void OnEnable()
    {
        Camera.onPreRender += OnPreRenderShrink;
        Camera.onPostRender += OnPostRenderShrink;
    }
    private void OnDisable()
    {
        Camera.onPreRender -= OnPreRenderShrink;
        Camera.onPostRender -= OnPostRenderShrink;
        var prev = useRendererCopies = false;
        UnShrinkMeshes(false);
        useRendererCopies = prev;
    }

    private void OnDestroy()
    {
        foreach (var item in boneToZeroBone.Values)
        {
            if (item.gameObject)
                GameObject.DestroyImmediate(item.gameObject);
        }
        foreach (var item in meshDatas)
        {
            if (item.Value.target)
                GameObject.DestroyImmediate(item.Value.target);
            if (item.Value.visibleCloneSmr)
                GameObject.DestroyImmediate(item.Value.visibleCloneSmr);
            if (item.Value.shadowCloneRenderer)
                GameObject.DestroyImmediate(item.Value.shadowCloneRenderer);

        }
    }
}
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