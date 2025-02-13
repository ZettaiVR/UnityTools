﻿using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Jobs;
using UnityEngine.Rendering;

public class MirrorAvatar : MonoBehaviour
{
    [Tooltip("Max distance the main camera can be from the head bone to shrink.")][Range(0f, 10f)]
    public float MaxDistance = 10.5f;        //set it based on avatar scale/height
    public List<Renderer> RenderersToHide = new List<Renderer>();  //if a renderer is in both it will be hidden
    public List<Renderer> RenderersToShow = new List<Renderer>();
    public readonly Dictionary<Transform, MirrorAvatarOptions.RendererOptionsEnum> SkinnedMeshRendererTransformOptions = new Dictionary<Transform, MirrorAvatarOptions.RendererOptionsEnum>();
    [Tooltip("When false we scale the head to 0.0001f, when true we scale to 0f. Must be set before initialization.")]
    public bool trueZeroScale = false;
    [Tooltip("Should sync blendshape values to shadow clones?")]
    public bool syncBlendshapes = true;

    private const string MainCamera = "MainCamera";
    private const string PlayerCloneLayerName = "PlayerLocal"; //"PlayerClone";
    private const string HeadZeroBoneName = "HeadZeroBone";
    private const string RendererCloneName = "RendererClones";
    private const string FirstPersonCloneSuffix = "-FirstPersonClone";
    private const string ZeroBoneName = "ZeroBone";
    private static readonly Material[] emptyMaterials = Array.Empty<Material>();
    private static readonly Vector3 CloseZeroScale = new Vector3(0.0001f, 0.0001f, 0.0001f);
    private static readonly Vector3 TrueZeroScale = new Vector3(0f, 0f, 0f);

    private int PlayerCloneLayer = 0;
    private int frameIndex = 0;
    private bool isActive;
    private bool InitDone;
    private bool isShrunk;
    private bool optionsChanged;
    private Vector3 headPosition;
    private Transform head;
    private Transform headZero;
    private MaterialPropertyBlock properties;
    private string rendererCloneName = RendererCloneName;

    private readonly List<Material> originalSmrSharedMaterials = new List<Material>();
    private readonly List<MeshData> meshDatasList = new List<MeshData>();
    private readonly List<Renderer> _renderersToHide = new List<Renderer>();
    private readonly List<Renderer> _renderersToShow = new List<Renderer>();
    private readonly List<Renderer> removedFromHideList = new List<Renderer>();
    private readonly List<Renderer> removedFromShowList = new List<Renderer>();
    private readonly List<Renderer> otherRenderers = new List<Renderer>();
    private readonly List<Transform> transformsUnderGameObject = new List<Transform>();
    private readonly List<WeightJobData> weightResults = new List<WeightJobData>();
    private readonly List<SkinnedMeshRenderer> skinnedMeshRenderers = new List<SkinnedMeshRenderer>();
    private readonly HashSet<Transform> transformsUnderHeadSet = new HashSet<Transform>();
    private readonly Dictionary<Renderer, MeshData> meshDatas = new Dictionary<Renderer, MeshData>();

    /// <summary>
    /// Bones to hide as key, their parent as value
    /// </summary>
    private readonly Dictionary<Transform, Transform> transformsToHide = new Dictionary<Transform, Transform>();
    private readonly Dictionary<Transform, Transform> headTransformsToHide = new Dictionary<Transform, Transform>();
    private readonly Dictionary<Transform, MirrorAvatarOptions> m_RendererOptions = new Dictionary<Transform, MirrorAvatarOptions>();
    private readonly Dictionary<Transform, Transform> boneToZeroBone = new Dictionary<Transform, Transform>();

    private static readonly List<MirrorAvatarOptions> allRendererOptions = new List<MirrorAvatarOptions>();
    private static readonly List<Component> components = new List<Component>();
    private static readonly List<Transform> tempTransforms = new List<Transform>();
    private static readonly List<Animator> tempAnimators = new List<Animator>();
    private static readonly HashSet<Transform> tempTransformsSet = new HashSet<Transform>();
    private static readonly Dictionary<Camera, bool> IsMainCamera = new Dictionary<Camera, bool>();

    private static readonly ProfilerMarker s_MarkerRestoreOriginal = new ProfilerMarker("Restore original materials");
    private static readonly ProfilerMarker s_MarkerDisableOriginal = new ProfilerMarker("Disable original materials");
    private static readonly ProfilerMarker s_MarkerSyncClones = new ProfilerMarker("Sync clones");
    private static readonly ProfilerMarker s_MarkerShrinkMeshes = new ProfilerMarker("Shrink meshes");
    private static readonly ProfilerMarker s_MarkerSyncMaterials = new ProfilerMarker("Sync materials");
    private static readonly ProfilerMarker s_MarkerSyncBlendshapes = new ProfilerMarker("Sync blendshapes");
    private static readonly ProfilerMarker s_MarkerOncePerFrame = new ProfilerMarker("Once per frame");
    private static readonly ProfilerMarker s_MarkeCreateClones = new ProfilerMarker("CreateClones");
    private static readonly ProfilerMarker s_MarkerSetupSkinnedRenderers = new ProfilerMarker("SetupSkinnedRenderers");
    private static readonly ProfilerMarker s_MarkerProcessWeightResults = new ProfilerMarker("Process weight results");
    private static readonly ProfilerMarker s_MarkerCreateWeightJobs = new ProfilerMarker("Create weight jobs");

    [Flags] public enum MeshOptions : byte
    {
        None = 0,
        Enabled = 1,
        Hide = 2,
        Shrink = 4,
        AlwaysHide = 8,
        AlwaysShow = 16,
    }

    [BurstCompile]
    struct WeightJob : IJob
    {
        [NativeDisableParallelForRestriction][WriteOnly] public NativeArray<bool> results;
        [ReadOnly] public NativeArray<BoneWeight1> weights;
        [BurstCompile]
        public void Execute()
        {
            var length = results.Length;
            if (length < 128 * 1024)
            {
                Span<bool> span = stackalloc bool[results.Length];
                for (int i = 0; i < weights.Length; i++)
                {
                    span[weights[i].boneIndex] = true;
                }
                span.CopyTo(results);
                return;
            }
            var array = new NativeArray<bool>(length, Allocator.Temp);
            for (int i = 0; i < weights.Length; i++)
            {
                array[weights[i].boneIndex] = true;
            }
            array.CopyTo(results);
            array.Dispose();
        }
    }
    class WeightJobData
    {
        public JobHandle handle;
        public NativeArray<bool> results;
        public MeshData meshData;
        public int max;

        public WeightJobData(MeshData meshData, NativeArray<bool> results, JobHandle handle, int weightsLength)
        {
            this.meshData = meshData;
            this.results = results;
            this.handle = handle;
            max = weightsLength;
        }
    }
    class MeshData
    {
        public bool isShrinked;
        public bool enabledState;
        public bool isSkinned;
        public bool hasBlendshapes;
        public bool hasClone;
        public bool hasShadow;
        public bool needsClone;
        public MeshOptions meshOptions;
        public int BlendShapeCount;
        public int boneStart;
        public int boneEnd;
        public Transform[] originalBones;
        public Transform[] hiddenBones;
        public bool[] existingBones;
        public GameObject target;
        public Renderer renderer;
        public SkinnedMeshRenderer skinnedMeshRenderer;
        public SkinnedMeshRenderer cloneSmr;
        public MirrorAvatarOptions rendererOptions;
        public ShadowCastingMode originalShadowCastingMode;
        public Mesh sharedMesh;
        public Material[] originalMaterials;
        public Material[] cloneMaterials;
        public float[] previousBlendshapeValues;

        public void SetupSync()
        {
            enabledState = renderer.enabled;
            hasClone = cloneSmr;
            sharedMesh = skinnedMeshRenderer ? skinnedMeshRenderer.sharedMesh : null;
            BlendShapeCount = sharedMesh ? sharedMesh.blendShapeCount : 0;
            hasBlendshapes = BlendShapeCount > 0;
            originalMaterials = renderer ? renderer.sharedMaterials : null;
            cloneMaterials = hasClone ? cloneSmr.sharedMaterials : null;
            previousBlendshapeValues = BlendShapeCount > 0 ? new float[BlendShapeCount] : null;
        }
        public MeshData(Renderer original, bool needsShadows)
        {
            renderer = original;
            originalShadowCastingMode = original.shadowCastingMode;
            skinnedMeshRenderer = renderer as SkinnedMeshRenderer;
            hasShadow = needsShadows;
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
        try
        {
            s_MarkerOncePerFrame.Begin();
            if (!InitDone)
                Initialize();

            var frameCount = Time.frameCount;
            if (frameCount == frameIndex)
                return;

            SyncOncePerFrame();
            frameIndex = frameCount;
            headPosition = head.position;

        }
        finally 
        {
            s_MarkerOncePerFrame.End();
        }
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
    internal void AddRendererOptions(MirrorAvatarOptions options)
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
    internal void UpdateFromOptions(List<Transform> myTransforms, MirrorAvatarOptions.RendererOptionsEnum rendererOptions) 
    {
        for (int i = 0; i < myTransforms.Count; i++)
        {
            SkinnedMeshRendererTransformOptions[myTransforms[i]] = rendererOptions;
        }
        optionsChanged = true;
    }
    private void UpdateShowHide()
    {
        Add(removedFromHideList, _renderersToHide, RenderersToHide);
        Add(removedFromShowList, _renderersToShow, RenderersToShow);
        ModifyOverrides(removedFromHideList, alwaysHide: false, alwaysShow: false);
        ModifyOverrides(removedFromShowList, alwaysHide: false, alwaysShow: false);
        ModifyOverrides(RenderersToShow, alwaysHide: false, alwaysShow: true);
        ModifyOverrides(RenderersToHide, alwaysHide: true, alwaysShow: false);
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

    private void ModifyOverrides(List<Renderer> renderersIn, bool alwaysHide, bool alwaysShow)
    {
        if (renderersIn.Count == 0)
            return;

        for (int i = 0; i < renderersIn.Count; i++)
        {
            if (!meshDatas.TryGetValue(renderersIn[i], out var meshData) || meshData.renderer != renderersIn[i])
                continue;

            meshData.meshOptions = meshData.meshOptions.SetFlag(MeshOptions.AlwaysHide, alwaysHide);
            meshData.meshOptions = meshData.meshOptions.SetFlag(MeshOptions.AlwaysShow, alwaysShow);
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
        meshDatasList.Clear();
        foreach (var item in meshDatasList)
        {
            if (item.cloneSmr)
                GameObject.Destroy(item.cloneSmr.gameObject);
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
            if (otherGameObject.hideFlags != HideFlags.None
                 || otherRenderer is ParticleSystemRenderer
                 || otherRenderer is LineRenderer
                 || otherRenderer is TrailRenderer
                 || !transformsCanBeHidden.Contains(otherRenderer.transform))
                continue;
            Renderer shadowClone = null;
            var meshData = new MeshData(otherRenderer, shadowClone);
            meshData.SetupSync();
            meshDatas.Add(otherRenderer, meshData);
            meshDatasList.Add(meshData);
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

            var gameObject = otherRenderer.gameObject;
            var overrideOptions = GetRendererOptions(gameObject.transform, out var _);
            var show = overrideOptions == MirrorAvatarOptions.RendererOptionsEnum.Show;
            if (show)
                continue;
            var hide = overrideOptions == MirrorAvatarOptions.RendererOptionsEnum.Hide;
            var renderShadowsOnly = otherRenderer.shadowCastingMode == ShadowCastingMode.ShadowsOnly;
            var underHead = transformsUnderHeadSet.Contains(otherRenderer.gameObject.transform);
            if (hide || underHead && !renderShadowsOnly)
            {
                meshData.meshOptions |= MeshOptions.Hide;
            }
        }
    }
    private T CreateClone<T>(T original, GameObject target, string nameSuffix) where T : Renderer
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
            SetChildProperties(target, cloneGameObject, name, HideFlags.HideAndDontSave);
            cloneGameObject.GetComponents(components);
            for (int i = 0; i < components.Count; i++)
            {
                var component = components[i];
                if (!component
                    || component is Transform
                    || component is T
                    || component is MeshFilter)
                    continue;
                // TODO: this doesn't handle required components
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

            meshData.meshOptions = meshData.meshOptions.SetFlag(MeshOptions.Shrink, false);
            meshData.meshOptions = meshData.meshOptions.SetFlag(MeshOptions.Hide, false);
            
            var overrideOptions = GetRendererOptions(skinnedMeshRenderer.transform, out var _);
            var show = overrideOptions == MirrorAvatarOptions.RendererOptionsEnum.Show;
            var hide = overrideOptions == MirrorAvatarOptions.RendererOptionsEnum.Hide;
            if (show || skinnedMeshRenderer.shadowCastingMode == ShadowCastingMode.ShadowsOnly)
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
        s_MarkerSetupSkinnedRenderers.Begin();
        weightResults.Clear();
        weightResults.Capacity = skinnedMeshRenderers.Count;
        s_MarkerCreateWeightJobs.Begin();
        for (var i = 0; i < skinnedMeshRenderers.Count; i++)
        {
            var skinnedMeshRenderer = skinnedMeshRenderers[i];
            var gameObject = skinnedMeshRenderer.gameObject;
            if (gameObject.hideFlags != HideFlags.None)
            {
                continue;
            }
            var needsShadows = skinnedMeshRenderer.shadowCastingMode != ShadowCastingMode.Off;
            var meshData = new MeshData(skinnedMeshRenderer, needsShadows);
            meshDatas.Add(skinnedMeshRenderer, meshData);
            meshDatasList.Add(meshData);
            meshData.originalBones = skinnedMeshRenderer.bones;
            meshData.hiddenBones = meshData.originalBones?.ToArray();
            meshData.existingBones = new bool[meshData.originalBones?.Length ?? 0];
            meshData.SetupSync();
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
                    var handle = job.Schedule();
                    JobHandle.ScheduleBatchedJobs();
                    weightResults.Add(new WeightJobData(meshData, results, handle, bones.Length));
                }
            }
        }
        tempTransformsSet.Clear();
        s_MarkerCreateWeightJobs.End();

        var transformCanBeHidden = tempTransformsSet;
        foreach (var options in m_RendererOptions)
        {
            var transform = options.Key;
            transform.GetComponentsInChildren(true, tempTransforms);
            transformCanBeHidden.Add(transform);
            transformCanBeHidden.UnionWith(tempTransforms);
        }
        transformCanBeHidden.UnionWith(transformsUnderHeadSet);
        s_MarkerProcessWeightResults.Begin();
        for (int i = 0; i < weightResults.Count; i++)
        {
            var weightResult = weightResults[i];    
            var results = weightResult.results;
            var meshData = weightResult.meshData;
            var existingBones = meshData.existingBones;
            var bones = meshData.originalBones.AsSpan();
            var handle = weightResult.handle;
            handle.Complete();            
            results.CopyTo(existingBones);
            results.Dispose();
            int start = 0;
            int end = weightResult.max - 1;
            bool startFound = false;
            for (int j = 0; j < weightResult.max; j++)
            {
                if (existingBones[j])
                {
                    if (!startFound)
                    {
                        start = j;
                        startFound = true;
                    }
                    end = j;
                }
            }
            meshData.boneStart = start;
            meshData.boneEnd = end;
            bool needsClone = false;
            for (int j = 0; j < existingBones.Length; j++)
            {
                if (existingBones[j] && transformCanBeHidden.Contains(bones[j]))
                {
                    needsClone = true;
                    break;
                }
            }
            meshData.needsClone = needsClone;
        }
        s_MarkerProcessWeightResults.End();
        s_MarkeCreateClones.Begin();
        for (int i = 0; i < weightResults.Count; i++)
        {
            var weightResult = weightResults[i];
            var meshData = weightResult.meshData;
            CreateClones(meshData);
            meshData.SetupSync();
        }
        s_MarkeCreateClones.End();
        weightResults.Clear();
        s_MarkerSetupSkinnedRenderers.End();
    }
    private void CheckMeshForShrinkNoBones(MeshData meshData, bool hide)
    {
        var hideThisMesh = transformsUnderHeadSet.Contains(meshData.skinnedMeshRenderer.transform) &&
            meshData.skinnedMeshRenderer.shadowCastingMode != ShadowCastingMode.ShadowsOnly;
        hideThisMesh |= hide;
        meshData.meshOptions = meshData.meshOptions.SetFlag(MeshOptions.Shrink, hideThisMesh);
        meshData.meshOptions = meshData.meshOptions.SetFlag(MeshOptions.Hide, hideThisMesh);

        if (!meshData.hasClone || !meshData.hasShadow)
            return;
    }
    private void CreateClones(MeshData meshData)
    {
        var skinnedMeshRenderer = meshData.skinnedMeshRenderer;
        var target = meshData.target;
        var visibleClone = meshData.cloneSmr;
        if (!target)
            target = CreateGameObjectAsChild(skinnedMeshRenderer.gameObject, rendererCloneName, HideFlags.HideAndDontSave, PlayerCloneLayer);

        if (!visibleClone)
        {
            visibleClone = meshData.cloneSmr = CreateClone(skinnedMeshRenderer, target, FirstPersonCloneSuffix);
        }
        visibleClone.shadowCastingMode = ShadowCastingMode.Off;
        visibleClone.forceMatrixRecalculationPerRender = false;
        if (!meshData.needsClone)
        {
            visibleClone.enabled = false;
        }
    }
    private void CheckMeshForShrinkBones(MeshData meshData, bool hideThisMesh)
    {
        var existingBones = meshData.existingBones;
        var bones = meshData.originalBones;
        var noHeadBones = meshData.hiddenBones;
        bones.AsSpan().CopyTo(noHeadBones);
        bool shrinkThisMesh = false;
        var start = meshData.boneStart;
        var end = meshData.boneEnd + 1;
        for (var j = start; j < end; j++)
        {
            if (!existingBones[j])      // skip bones that don't have weights on the mesh
                continue;

            var bone = bones[j];
            if (!bone)
                continue;

            var otherHide = transformsToHide.ContainsKey(bone);
            var headHide = transformsUnderHeadSet.Contains(bone);
            var _overrideOptions = GetRendererOptions(bone, out var _);
            var alwaysShow = _overrideOptions == MirrorAvatarOptions.RendererOptionsEnum.Show;
            var alwaysHide = _overrideOptions == MirrorAvatarOptions.RendererOptionsEnum.Hide;
            var hide = otherHide | headHide | alwaysHide;
            if (hide && noHeadBones[j] != transformsToHide[bone])
            {
                // if not hidden, hide
                noHeadBones[j] = transformsToHide[bone];
            }
            else if (noHeadBones[j] != bones[j])
            {
                // if not hidden, show
                noHeadBones[j] = bones[j];
            }
            if (((!headHide && !otherHide) || alwaysShow) && !alwaysHide)
                continue;

            shrinkThisMesh = true;
        }
        if (shrinkThisMesh)
        {
            meshData.meshOptions = meshData.meshOptions.SetFlag(MeshOptions.Shrink, shrinkThisMesh);
            meshData.meshOptions = meshData.meshOptions.SetFlag(MeshOptions.Hide, hideThisMesh);
        }

        if (!shrinkThisMesh || !meshData.hasClone)
            return;

        var visibleClone = meshData.cloneSmr;
        visibleClone.enabled = true;
        visibleClone.bones = noHeadBones;
    }

    private static GameObject CreateGameObjectAsChild(GameObject parent, string name, HideFlags hideFlags, int layer)
    {
        var parentTransform = parent.transform;
        var childCount = parentTransform.childCount;
        for (int i = 0; i < childCount; i++)
        {
            var item = parentTransform.GetChild(i);
            if (item.hideFlags == hideFlags && item.name == name)
                return item.gameObject;
        }
        var child = new GameObject();
        SetChildProperties(parent, child, name, hideFlags);
        child.layer = layer;
        return child;
    }

    private static void SetChildProperties(GameObject parent, GameObject child, string name, HideFlags hideFlags)
    {
        var childTransform = child.transform;
        childTransform.SetParent(parent.transform);
        childTransform.localPosition = Vector3.zero;
        childTransform.localRotation = Quaternion.identity;
        childTransform.localScale = Vector3.one;
        child.hideFlags = hideFlags;
        child.layer = parent.layer;
        child.name = name;
    }

    private MirrorAvatarOptions.RendererOptionsEnum GetRendererOptions(Transform search, out List<Transform> _transformsToHide)
    {
        _transformsToHide = null;
        if (m_RendererOptions.TryGetValue(search, out var options))
        {
            if (options)
            {
                if (options.isActiveAndEnabled)
                {
                    _transformsToHide = options.transformsToHide;
                    return options.firstPersonVisibility;
                }
                else
                {
                    m_RendererOptions.Remove(search);
                }
            }
        }
        if (SkinnedMeshRendererTransformOptions.TryGetValue(search, out var value))
        {
            return value;
        }
        return MirrorAvatarOptions.RendererOptionsEnum.Default;
    }

    private void ClearTransformOverrides()
    {
        transformsToHide.Clear();
#if NET_STANDARD_2_1
        transformsToHide.EnsureCapacity(headTransformsToHide.Count);
#endif

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
            var overrideOptions = GetRendererOptions(bone, out var _transformsToHide);
            var forceHide = overrideOptions == MirrorAvatarOptions.RendererOptionsEnum.Hide;
            if (!forceHide || !bone || transformsToHide.ContainsKey(bone))
                continue;
            if (_transformsToHide == null)
            {
                bone.GetComponentsInChildren(true, tempTransforms);
            }
            else 
            {
                tempTransforms.Clear();
                tempTransforms.AddRange(_transformsToHide);
            }
            var zeroTransform = GetZeroTransform(bone, ZeroBoneName);
            for (int i = 0; i < tempTransforms.Count; i++)
            {
                if (tempTransforms[i] == zeroTransform)
                    continue;

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
            var zeroBone = CreateGameObjectAsChild(bone.gameObject, name, HideFlags.HideAndDontSave, transform.gameObject.layer);
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
        s_MarkerSyncClones.Begin();
        for (int i = 0; i < meshDatasList.Count; i++)
        {
            var meshData = meshDatasList[i];
            if (!meshData.renderer)
            {
                if (meshData.cloneSmr)
                    GameObject.Destroy(meshData.cloneSmr);
                continue;
            }
            if (!meshData.renderer.enabled)
                continue;

            SyncMaterials(meshData);

            if (syncBlendshapes && meshData.hasBlendshapes && meshData.hasClone)
            {
                SyncBlendshapes(meshData);
            }
        }
        s_MarkerSyncClones.End();
    }

    private static void SyncBlendshapes(MeshData meshData)
    {
        s_MarkerSyncBlendshapes.Begin();
        var visibleClone = meshData.cloneSmr;
        var blendShapeCount = meshData.BlendShapeCount;
        var previousValues = meshData.previousBlendshapeValues;
        var originalSmr = meshData.skinnedMeshRenderer;

        for (int i = 0; i < blendShapeCount; i++)
        {
            var value = originalSmr.GetBlendShapeWeight(i);
            if (previousValues[i] != value)
            {
                visibleClone.SetBlendShapeWeight(i, value);
                previousValues[i] = value;
            }
        }
        s_MarkerSyncBlendshapes.End();
    }
    private void SyncMaterials(MeshData meshData)
    {
        s_MarkerSyncMaterials.Begin();
        try
        {
            var cloneMaterials = meshData.cloneMaterials;
            var original = meshData.renderer;
            var visibleClone = meshData.cloneSmr;
            var hasVisibleClone = meshData.hasClone;

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
                if (hasVisibleClone)
                    visibleClone.sharedMaterials = cloneMaterials;
            }
            // material properties
            original.GetPropertyBlock(properties);
            if (hasVisibleClone)
                visibleClone.SetPropertyBlock(properties);
        }
        finally
        {
            s_MarkerSyncMaterials.End();
        }
    }
    private static bool IsCameraMain(Camera cam)
    {
        if (!IsMainCamera.TryGetValue(cam, out bool value))
        {
            value = IsMainCamera[cam] = cam.CompareTag(MainCamera);
        }
        return value;
    }
    public void OnPreRenderShrink(Camera cam)
    {
        // only run this on the main camera
        var main = IsCameraMain(cam);
        if (!main)
        {
            if (isShrunk)
                UnShrinkMeshes();
            return;
        }
        UpdatePerFrame();

        isActive = Vector3.Distance(cam.transform.position, headPosition) < MaxDistance;
        if (!isActive)
        {
            UnShrinkMeshes();
            return;
        }
        SyncClones();
        ShrinkMeshes();
    }

    public void OnPostRenderShrink(Camera cam)
    {
        // only run this on the main camera
        var main = IsCameraMain(cam);
        if (!isActive || !main)
            return;

        UnShrinkMeshes();        
    }

    private void SyncOncePerFrame()
    {
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
        s_MarkerShrinkMeshes.Begin();
        for (int i = 0; i < meshDatasList.Count; i++)
        {
            var meshData = meshDatasList[i];
            var renderer = meshData.renderer;
            var options = meshData.meshOptions;
            bool alwaysHide = options.HasFlag(MeshOptions.AlwaysHide);
            var alwaysShow = options.HasFlag(MeshOptions.AlwaysShow);
            var shrink = options.HasFlag(MeshOptions.Shrink);
            var hide = options.HasFlag(MeshOptions.Hide);
            
            if ((!alwaysHide && alwaysShow) || !renderer)
            {
                meshData.isShrinked = false;
                continue;
            }
            if (meshData.isSkinned)
            {
                var skinnedMeshRenderer = meshData.skinnedMeshRenderer;
                if (!skinnedMeshRenderer)
                    continue;
                meshData.enabledState = renderer.enabled;
                if (alwaysHide && !alwaysShow)
                {
                    meshData.isShrinked = true;
                    continue;
                }
                meshData.isShrinked = shrink;
            }
            else if ((hide || alwaysHide) && !alwaysShow)
            {
                var enabled = meshData.enabledState = renderer.enabled;
                if (enabled)
                    meshData.isShrinked = true;
            }
        }
        SwapToClone();
        s_MarkerShrinkMeshes.End();
    }
    private void UnShrinkMeshes()
    {
        s_MarkerRestoreOriginal.Begin();
        if (!isShrunk)
            return;

        for (int i = 0; i < meshDatasList.Count; i++)
        {
            var meshData = meshDatasList[i];
            if (!meshData.isShrinked)
                continue;

            if (meshData.hasClone)
            {
                meshData.cloneSmr.sharedMaterials = emptyMaterials;
            }

            if (meshData.hasShadow)
            {
                meshData.renderer.shadowCastingMode = ShadowCastingMode.Off;
            }
            else
            {
                meshData.renderer.sharedMaterials = meshData.originalMaterials;
            }
            meshData.isShrinked = false;
        }
        isShrunk = false;
        s_MarkerRestoreOriginal.End();
    }
    private void SwapToClone() 
    {
        if (isShrunk)
            return;

        s_MarkerDisableOriginal.Begin();
        for (int i = 0; i < meshDatasList.Count; i++)
        {
            var meshData = meshDatasList[i];
            if (!meshData.isShrinked)
                continue;

            if (meshData.hasClone)
            {
                meshData.cloneSmr.sharedMaterials = meshData.cloneMaterials;
            }

            if (meshData.hasShadow)
            {
                meshData.renderer.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
            }
            else
            {
                meshData.renderer.GetSharedMaterials(originalSmrSharedMaterials);
                originalSmrSharedMaterials.CopyTo(meshData.originalMaterials);
                meshData.renderer.sharedMaterials = emptyMaterials;
            }
        }
        isShrunk = true;
        s_MarkerDisableOriginal.End();
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
        UnShrinkMeshes();
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
            var meshData = item.Value;
            if (meshData.renderer)
            {
                meshData.renderer.shadowCastingMode = meshData.originalShadowCastingMode;
            }
            if (meshData.target)
                GameObject.DestroyImmediate(meshData.target);
            if (meshData.cloneSmr)
            {
                GameObject.DestroyImmediate(meshData.cloneSmr.transform.parent.gameObject);
            }
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