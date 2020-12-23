using System;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;

public class MirrorAvatar : MonoBehaviour
{
    private enum OverrideValue
    {
        Default = 0,
        Hide = 1,
        Show = 2
    }
    private SkinnedMeshRenderer[] skinnedMeshRenderers;
    private Renderer[] otherRenderers;  //includes all renderers
    private bool[] shrinkSkinnedMesh;
    private bool[] hideSkinnedMesh;
    private bool[] hideMesh;
    private bool[] enabledStateMesh;
    private bool[] enabledStateSkinnedMesh;
    private bool isActive;
    private OverrideValue[] shrinkSkinnedMeshOverride;
    private OverrideValue[] hideMeshOverride;
    private Transform head;
    private GameObject zeroBone;
    private Transform[][] original;         //first: index in skinnedMeshRenderers[] second: Transform[] to replace SMR bones with 
    private Transform[][] noHead;
    public float MaxDistance = 0.5f;        //set it based on avatar scale/height
    public List<Renderer> RenderersToHide;  //if a renderer is in both it will be hidden
    public List<Renderer> RenderersToShow;
    private List<Renderer> _renderersToHide = new List<Renderer>();
    private List<Renderer> _renderersToShow = new List<Renderer>();
    private void ModifyOverrides(List<Renderer> renderersIn, List<Renderer> smrlist, List<Renderer> rendererlist, OverrideValue overrideValue) 
    {
        for (int i = 0; i < renderersIn.Count; i++)
        {
            int index = smrlist.IndexOf(renderersIn[i]);
            if (index >= 0)
            {
                shrinkSkinnedMeshOverride[index] = overrideValue;
            }
            index = rendererlist.IndexOf(renderersIn[i]);
            if (index >= 0)
            {
                hideMeshOverride[index] = overrideValue;
            }
        }
    }
    private void LateUpdate()
    {
        if (!RenderersToHide.SequenceEqual(_renderersToHide) || !RenderersToShow.SequenceEqual(_renderersToShow))
        {
            var smrlist = skinnedMeshRenderers.ToList<Renderer>();
            var rendererlist = otherRenderers.ToList();
            var removedFromShowList = _renderersToShow.ToList();
            removedFromShowList.RemoveAll(m => RenderersToShow.Contains(m));
            var removedFromHideList = _renderersToHide.ToList();
            removedFromHideList.RemoveAll(m => RenderersToHide.Contains(m));
            ModifyOverrides(removedFromHideList, smrlist, rendererlist, OverrideValue.Default);
            ModifyOverrides(removedFromShowList, smrlist, rendererlist, OverrideValue.Default);
            ModifyOverrides(RenderersToShow, smrlist, rendererlist, OverrideValue.Show); 
            ModifyOverrides(RenderersToHide, smrlist, rendererlist, OverrideValue.Hide);
            _renderersToHide = RenderersToHide.ToList();
            _renderersToShow = RenderersToShow.ToList();
        }
    }
    // Start is called before the first frame update
    void Start() 
    {
        Initialize();
    }
    public void Initialize()
    {
        Animator animator;
        Transform[] transformsUnderHead;    //all bones under the head bone (incl)
        if ((animator = gameObject.GetComponent<Animator>()) != null && animator.isHuman)
        {
            isActive = true;
            head = animator.GetBoneTransform(HumanBodyBones.Head);
            transformsUnderHead = head.GetComponentsInChildren<Transform>();
            zeroBone = new GameObject
            {
                name = head.name + "-ZeroBone"
            };
            zeroBone.transform.localScale = new Vector3(0.0001f, 0.0001f, 0.0001f);
            zeroBone.transform.SetParent(head);
            zeroBone.transform.localPosition = Vector3.zero;
        }
        else
        {
            //only works on humanoids (needs a head to hide)
            isActive = false;
            return;
        }
        var smrs = gameObject.GetComponentsInChildren<SkinnedMeshRenderer>();
        var smr = gameObject.GetComponent<SkinnedMeshRenderer>();
        var renderers = gameObject.GetComponentsInChildren<Renderer>();
        var renderer = gameObject.GetComponent<Renderer>();
        List<SkinnedMeshRenderer> SMRList = new List<SkinnedMeshRenderer>();
        if (smrs != null && smrs.Length > 0) { SMRList.AddRange(smrs); }
        if (smr != null ) { SMRList.Add(smr); }
        skinnedMeshRenderers = SMRList.ToArray();
        List<Renderer> MRList = new List<Renderer>();
        if (renderers != null && renderers.Length > 0) { MRList.AddRange(renderers); }
        if (renderer != null) { MRList.Add(renderer); }


        original = new Transform[skinnedMeshRenderers.Length][];
        noHead = new Transform[skinnedMeshRenderers.Length][];
        shrinkSkinnedMesh = new bool[skinnedMeshRenderers.Length];
        shrinkSkinnedMeshOverride = new OverrideValue[skinnedMeshRenderers.Length];
        for (int i = 0; i < skinnedMeshRenderers.Length; i++)
        {
            shrinkSkinnedMeshOverride[i] = OverrideValue.Default;
        }
        hideSkinnedMesh = new bool[skinnedMeshRenderers.Length];
        enabledStateSkinnedMesh = new bool[skinnedMeshRenderers.Length];
        SkinnedMeshRenderer tempSkinnedMeshRenderer;
        for (var i = 0; i < skinnedMeshRenderers.Length; i++)
        {
            shrinkSkinnedMesh[i] = false;
            tempSkinnedMeshRenderer = skinnedMeshRenderers[i];
            if (tempSkinnedMeshRenderer.bones != null && tempSkinnedMeshRenderer.bones.Length > 0)
            {
                hideSkinnedMesh[i] = false;
                //it has bones
                for (var j = 0; j < tempSkinnedMeshRenderer.bones.Length; j++)
                {
                    //don't hide/shrink stuff that's shadows only
                    if (transformsUnderHead.Contains(tempSkinnedMeshRenderer.bones[j]) &&
                    skinnedMeshRenderers[i].shadowCastingMode != UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly)
                    {
                        if (!shrinkSkinnedMesh[i])
                        {
                            shrinkSkinnedMesh[i] = true;
                            //knah: "In most cases when unity returns a T[], it creates a new one for each get"
                            original[i] = tempSkinnedMeshRenderer.bones;
                            noHead[i] = tempSkinnedMeshRenderer.bones;
                        }
                        noHead[i][j] = zeroBone.transform;
                    }
                }
                tempSkinnedMeshRenderer.forceMatrixRecalculationPerRender = shrinkSkinnedMesh[i];
            }
            else
            {
                //has no bones... we'll have to disable it if it's under the head bone

                if (transformsUnderHead.Contains(skinnedMeshRenderers[i].gameObject.transform) &&
                    skinnedMeshRenderers[i].shadowCastingMode != UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly)
                {
                    shrinkSkinnedMesh[i] = true;
                    hideSkinnedMesh[i] = true;
                }
                else
                {
                    shrinkSkinnedMesh[i] = false;
                    hideSkinnedMesh[i] = false;
                }
            }
        }
        otherRenderers = RemoveSMR(MRList, SMRList);
        hideMesh = new bool[otherRenderers.Length];
        hideMeshOverride = new OverrideValue[otherRenderers.Length];
        for (int i = 0; i < otherRenderers.Length; i++)
        {
            hideMeshOverride[i] = OverrideValue.Default;
        }
        enabledStateMesh = new bool[otherRenderers.Length];
        for (var i = 0; i < otherRenderers.Length; i++)
        {
            if (transformsUnderHead.Contains(otherRenderers[i].gameObject.transform) &&
                otherRenderers[i].shadowCastingMode != UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly)
            {
                hideMesh[i] = true;
            }
            else if (otherRenderers[i].GetType().Equals(typeof(SkinnedMeshRenderer))) 
            {
                hideMesh[i] = true;
            }
            else
            {
                hideMesh[i] = false;
            }
        }
        Camera.onPreRender += OnPreRenderShrink;
        Camera.onPostRender += OnPostRenderShrink;
    }
    private void OnDestroy()
    {
        Camera.onPreRender -= OnPreRenderShrink;
        Camera.onPostRender -= OnPostRenderShrink;
        //clean up for the editor
        Destroy(zeroBone); 
    }
    private Renderer[] RemoveSMR(List<Renderer> MRList, List<SkinnedMeshRenderer> SMRList) 
    {
        for (int i = 0; i < SMRList.Count; i++)
        {
            if (!hideSkinnedMesh[i])
            {
                Renderer item = SMRList[i];
                MRList.Remove(item);
            }
        } 
        return MRList.ToArray();
    }
    public void OnPreRenderShrink(Camera cam)
    {
        isActive = (cam.transform.position - head.position).magnitude < MaxDistance;
        //Debug.Log($"OnPre, cam = {cam.name}, isActive = {isActive}, dist: {(cam.transform.position - Head.position).magnitude}, time {Time.time}");
        if (isActive) 
        {
            if (cam.CompareTag("MainCamera"))
            {
                for (var i = 0; i < shrinkSkinnedMesh.Length; i++) 
                {
                    if (shrinkSkinnedMesh[i] && (shrinkSkinnedMeshOverride[i] == OverrideValue.Default))
                    {
                        skinnedMeshRenderers[i].bones = noHead[i];
                        continue;
                    }
                    else
                    {
                        //  skinnedMeshRenderers[i].bones = original[i]; 
                    }
                    if (shrinkSkinnedMeshOverride[i] == OverrideValue.Hide)
                    {
                        enabledStateSkinnedMesh[i] = skinnedMeshRenderers[i].enabled;
                        skinnedMeshRenderers[i].enabled = false;
                    }
                }
                for (var i = 0; i < hideMesh.Length; i++)
                {
                    if (hideMesh[i] && (hideMeshOverride[i] != OverrideValue.Show) || hideMeshOverride[i] == OverrideValue.Hide)
                    {
                        enabledStateMesh[i] = otherRenderers[i].enabled;
                        if (otherRenderers[i].enabled)
                        {
                            otherRenderers[i].enabled = false;
                        }
                    }
                }
            } 
        }
    }
    public void OnPostRenderShrink(Camera cam)
    {
        //Debug.Log($"OnPost, cam = {cam.name}, isActive = {isActive}, time {Time.time}");
        if (isActive)
        {
            if (cam.CompareTag("MainCamera"))
            {
                for (var i = 0; i < shrinkSkinnedMesh.Length; i++)
                {
                    if (shrinkSkinnedMesh[i] && (shrinkSkinnedMeshOverride[i] == OverrideValue.Default))
                    {
                        skinnedMeshRenderers[i].bones = original[i];
                        continue;
                    }
                    if (shrinkSkinnedMeshOverride[i] == OverrideValue.Hide)
                    {
                        skinnedMeshRenderers[i].enabled = enabledStateSkinnedMesh[i];
                    }
                }
                for (var i = 0; i < hideMesh.Length; i++)
                {
                    if (hideMesh[i] && (hideMeshOverride[i] != OverrideValue.Show) || hideMeshOverride[i] == OverrideValue.Hide)
                    {
                        otherRenderers[i].enabled = enabledStateMesh[i];
                    }
                }
            }
        }
    }
}