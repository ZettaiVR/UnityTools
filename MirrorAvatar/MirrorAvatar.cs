using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

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
    private enum OverrideValue 
    {
        Default,
        Show,
        Hide,
    }
    [Flags]
    public enum MeshOptions : byte
    {
        None = 0,
        Enabled = 1,
        Hide = 2,
        Shrink = 4,
        OverrideHide = 8,
        OverrideShow = 16,
    }

    private const UnityEngine.Rendering.ShadowCastingMode shadowsOnly = UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly;
    private MeshOptions[] skinnedMeshOptions;
    private MeshOptions[] otherMeshOptions;
    private bool[] enabledStateMesh;
    private bool[] enabledStateSkinnedMesh;
    private bool isActive;
    private Transform head;
    public float MaxDistance = 0.5f;        //set it based on avatar scale/height
    public List<Renderer> RenderersToHide = new List<Renderer>();  //if a renderer is in both it will be hidden
    public List<Renderer> RenderersToShow = new List<Renderer>();
    private readonly List<Renderer> _renderersToHide = new List<Renderer>();
    private readonly List<Renderer> _renderersToShow = new List<Renderer>();
    private readonly List<Renderer> removedFromHideList = new List<Renderer>();
    private readonly List<Renderer> removedFromShowList = new List<Renderer>();
    private readonly List<Renderer> otherRenderers = new List<Renderer>();
    private readonly List<Transform[]> originalBones = new List<Transform[]>();
    private readonly List<Transform[]> hiddenBones = new List<Transform[]>();
    private static readonly List<Transform> transformsUnderGameObject = new List<Transform>();
    private static readonly List<Transform> tempTransforms = new List<Transform>();
    private static readonly HashSet<Transform> transformsUnderHeadSet = new HashSet<Transform>();
    /// <summary>
    /// Bones to hide as key, their parent as value
    /// </summary>
    private static readonly Dictionary<Transform, Transform> transformsToHide = new Dictionary<Transform, Transform>();
    private readonly List<SkinnedMeshRenderer> skinnedMeshRenderers = new List<SkinnedMeshRenderer>();
  
    private void LateUpdate()
    {
        if (RenderersToHide.SequenceEqual(_renderersToHide) && RenderersToShow.SequenceEqual(_renderersToShow))
            return;
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

    void Start() 
    {
        Initialize();
    }
    private void ModifyOverrides(List<Renderer> renderersIn, bool overrideHide, bool overrideShow)
    {
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
        transformsToHide.Clear();
        Animator animator = gameObject.GetComponent<Animator>();
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
        GetRenderes();
        InitializeArrays();
        SetupSkinnedRenderers();
        SetupOtherRenderers();
        Camera.onPreRender += OnPreRenderShrink;
        Camera.onPostRender += OnPostRenderShrink;
    }

    private void SetupOtherRenderers()
    {
        var rendererCount = otherRenderers.Count;
        for (var i = 0; i < rendererCount; i++)
        {
            var otherRenderer = otherRenderers[i];
            var name = otherRenderer.name;
            var show = name.Contains(OverrideShow);
            if (show)
                continue;
            var hide = name.Contains(OverrideHide);
            var renderShadowsOnly = otherRenderer.shadowCastingMode == shadowsOnly;
            var underHead = transformsUnderHeadSet.Contains(otherRenderer.gameObject.transform);
            if (hide || underHead && !renderShadowsOnly)
                otherMeshOptions[i] |= MeshOptions.Hide;
        }
    }

    private void SetupSkinnedRenderers()
    {
        var empty = Array.Empty<Transform>();
        for (var i = 0; i < skinnedMeshRenderers.Count; i++)
        {
            var skinnedMeshRenderer = skinnedMeshRenderers[i];
            var name = skinnedMeshRenderer.name;
            if (name.Contains(OverrideShow) || skinnedMeshRenderer.shadowCastingMode == shadowsOnly)
            {
                //don't hide/shrink stuff that's shadows only or is tagged to be shown
                if (name.Contains(OverrideHide))
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
                    var boneName = bone.name;
                    var otherHide = transformsToHide.ContainsKey(bone);
                    var headHide = transformsUnderHeadSet.Contains(bone);
                    var overrideShow = boneName.Contains(OverrideShow);
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
            hideThisMesh |= name.Contains(OverrideHide);
            hiddenBones.Add(noHeadBones);
            if (shrinkThisMesh)
                skinnedMeshOptions[i] |= MeshOptions.Shrink;
            if (hideThisMesh)
                skinnedMeshOptions[i] |= MeshOptions.Hide;
        }
    }

    private void InitializeArrays()
    {
        skinnedMeshOptions = new MeshOptions[skinnedMeshRenderers.Count];
        enabledStateSkinnedMesh = new bool[skinnedMeshRenderers.Count];
        otherMeshOptions = new MeshOptions[otherRenderers.Count];
        enabledStateMesh = new bool[otherRenderers.Count];
    }

    private void GetRenderes()
    {
        gameObject.GetComponentsInChildren(true, skinnedMeshRenderers);
        var smr = gameObject.GetComponent<SkinnedMeshRenderer>();
        if (smr != null)
            skinnedMeshRenderers.Add(smr);
        gameObject.GetComponentsInChildren(true, otherRenderers);
        var renderer = gameObject.GetComponent<Renderer>();
        if (renderer != null)
            otherRenderers.Add(renderer);
        RemoveSMR(otherRenderers, skinnedMeshRenderers);
        gameObject.GetComponentsInChildren(true, transformsUnderGameObject); // all transforms
        foreach (var bone in transformsUnderGameObject)
        {
            if (!bone || !bone.name.Contains(OverrideHide))
                continue;
            bone.GetComponentsInChildren(true, tempTransforms);
            foreach (var childBone in tempTransforms)
            {
                var zeroBone = new GameObject
                {
                    hideFlags = HideFlags.HideAndDontSave,
                    name = "ZeroBone",
                };
                var zeroTransform = zeroBone.transform;
                zeroTransform.SetParent(bone);
                zeroTransform.localScale = ZeroScale;
                zeroTransform.localPosition = Vector3.zero;
                transformsToHide[childBone] = zeroTransform;
            }
        }
        transformsUnderGameObject.Clear();
        tempTransforms.Clear();
    }

    private void OnDestroy()
    {
        Camera.onPreRender -= OnPreRenderShrink;
        Camera.onPostRender -= OnPostRenderShrink;
    }
    private void RemoveSMR(List<Renderer> MRList, List<SkinnedMeshRenderer> SMRList) 
    {
        for (int i = SMRList.Count - 1; i >= 0; i--)
        {
            if (skinnedMeshOptions[i].HasFlag(MeshOptions.Hide))
            {
                var item = SMRList[i];
                MRList.Remove(item);
            }
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
            if (hide && !overrideShow || overrideHide)
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
                continue;
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

            if (hide && (!overrideShow || overrideHide))
            {
                renderer.enabled = enabledStateMesh[i];
            }
        }
    }
}