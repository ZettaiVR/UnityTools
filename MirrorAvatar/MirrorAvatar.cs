using System;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;

public class MirrorAvatar : MonoBehaviour
{
    //doesn't include other kinds of renderers like line, trail, or particle system renderers.
    private SkinnedMeshRenderer[] skinnedMeshRenderers;
    private MeshRenderer[] meshRenderers;
    private bool[] shrink;
    private bool[] hideSkinnedMesh;
    private bool[] hideMesh;
    private bool[] enabledStateMesh;
    private bool[] enabledStateSkinnedMesh;
    private bool isActive;
    private Transform head;
    private GameObject zeroBone;
    private Transform[][] original;  // first: index in skinnedMeshRenderers[] second: Transform[] to replace SMR bones with 
    private Transform[][] noHead;
    public float MaxDistance = 0.5f;

    // Start is called before the first frame update
    void Start() 
    {
        Initialize();
    }
    public void Initialize()
    {
        Transform[] transformsUnderHead;    //all bones under the head bone (incl)
        Animator animator;
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
        var renderers = gameObject.GetComponentsInChildren<MeshRenderer>();
        var renderer = gameObject.GetComponent<MeshRenderer>();
        List<SkinnedMeshRenderer> SMRList = new List<SkinnedMeshRenderer>();
        if (smrs != null && smrs.Length > 0) { SMRList.AddRange(smrs); }
        if (smr != null ) { SMRList.Add(smr); }
        skinnedMeshRenderers = SMRList.ToArray();
        List<MeshRenderer> MRList = new List<MeshRenderer>();
        if (renderers != null && renderers.Length > 0) { MRList.AddRange(renderers); }
        if (renderer != null) { MRList.Add(renderer); }
        meshRenderers = MRList.ToArray();
        hideMesh = new bool[meshRenderers.Length];
        original = new Transform[skinnedMeshRenderers.Length][];
        noHead = new Transform[skinnedMeshRenderers.Length][];
        shrink = new bool[skinnedMeshRenderers.Length];
        hideSkinnedMesh = new bool[skinnedMeshRenderers.Length];
        enabledStateSkinnedMesh = new bool[skinnedMeshRenderers.Length];
        SkinnedMeshRenderer tempSkinnedMeshRenderer;
        for (var i = 0; i < skinnedMeshRenderers.Length; i++)
        {
            shrink[i] = false;
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
                        if (!shrink[i])
                        {
                            shrink[i] = true;
                            //knah: "In most cases when unity returns a T[], it creates a new one for each get"
                            original[i] = tempSkinnedMeshRenderer.bones; 
                            noHead[i] = tempSkinnedMeshRenderer.bones;
                        }
                        noHead[i][j] = zeroBone.transform;
                    }
                }
                tempSkinnedMeshRenderer.forceMatrixRecalculationPerRender = shrink[i];
            }
            else
            {
                //has no bones... we'll have to disable it if it's under the head bone

                if (transformsUnderHead.Contains(skinnedMeshRenderers[i].gameObject.transform) &&
                    skinnedMeshRenderers[i].shadowCastingMode != UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly)
                {
                    shrink[i] = true;
                    hideSkinnedMesh[i] = true;
                }
                else
                {
                    shrink[i] = false;
                    hideSkinnedMesh[i] = false;
                }
            }
        }
        hideMesh = new bool[meshRenderers.Length];
        enabledStateMesh = new bool[meshRenderers.Length];
        for (var i = 0; i < meshRenderers.Length; i++)
        {
            if (transformsUnderHead.Contains(meshRenderers[i].gameObject.transform) &&
                meshRenderers[i].shadowCastingMode != UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly)
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
    public void OnPreRenderShrink(Camera cam)
    {
        isActive = (cam.transform.position - head.position).magnitude < MaxDistance;
        //Debug.Log($"OnPre, cam = {cam.name}, isActive = {isActive}, dist: {(cam.transform.position - Head.position).magnitude}, time {Time.time}");
        if (isActive) 
        {
            if (cam.CompareTag("MainCamera"))
            {
                for (var i = 0; i < shrink.Length; i++) 
                {
                    if (hideSkinnedMesh[i]) 
                    {
                        enabledStateSkinnedMesh[i] = skinnedMeshRenderers[i].enabled;
                        skinnedMeshRenderers[i].enabled = false;
                        continue;
                    }
                    if (shrink[i])
                    {
                        skinnedMeshRenderers[i].bones = noHead[i];
                    }
                    else 
                    {
                      //  skinnedMeshRenderers[i].bones = original[i]; 
                    }
                }
                for (var i = 0; i < hideMesh.Length; i++)
                {
                    enabledStateMesh[i] = meshRenderers[i].enabled;
                    if (meshRenderers[i].enabled)
                    {
                        meshRenderers[i].enabled = !hideMesh[i];
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
                for (var i = 0; i < shrink.Length; i++)
                {
                    if (hideSkinnedMesh[i])
                    {
                        skinnedMeshRenderers[i].enabled = enabledStateSkinnedMesh[i];
                        continue;
                    }
                    if (shrink[i])
                    {
                        skinnedMeshRenderers[i].bones = original[i];
                    }
                }
                for (var i = 0; i < hideMesh.Length; i++)
                {
                    meshRenderers[i].enabled = enabledStateMesh[i];
                }
            }
        }
    }
}