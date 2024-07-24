using System.Collections.Generic;
using UnityEngine;

#pragma warning disable IDE0051
public class RendererOptions : MonoBehaviour
{
    public enum RendererOptionsEnum
    {
        Default = 0,
        ForceHide = 1,
        ForceShow = 2
    }
    public RendererOptionsEnum firstPersonVisibility = RendererOptionsEnum.Default;
    private RendererOptionsEnum previousFirstPersonVisibility = RendererOptionsEnum.Default;
    private MirrorAvatar mirrorAvatar;
    internal Renderer myRenderer;
    private List<Transform> myTransforms = new List<Transform>();
    void Start()
    {
        FindScript();
        if (!gameObject.TryGetComponent(out myRenderer))
        {
            transform.GetComponentsInChildren(true, myTransforms); 
            UpdateTransforms();
        }
        if (mirrorAvatar)
            mirrorAvatar.AddRendererOptions(this);
    }
    private void FindScript()
    {
        mirrorAvatar = gameObject.GetComponentInParent<MirrorAvatar>();
    }
    private void OnDestroy()
    {
        if (mirrorAvatar)
        {
            mirrorAvatar.RemoveRendererOptions(transform); 
            foreach (var t in myTransforms)
            {
                mirrorAvatar.skinnedMeshRendererTransformOptions[t] = RendererOptionsEnum.Default;
            }
        }
    }
    private void OnEnable()
    {
        OnDidApplyAnimationProperties();
    }
    void OnDidApplyAnimationProperties()
    {
        if (firstPersonVisibility == previousFirstPersonVisibility)
            return;

        if (!mirrorAvatar)
            FindScript();

        if (!mirrorAvatar)
            return;

        previousFirstPersonVisibility = firstPersonVisibility;

        UpdateTransforms();

        if (!myRenderer)
            return;

        switch (firstPersonVisibility)
        {
            case RendererOptionsEnum.Default:
                {
                    mirrorAvatar.RenderersToShow.Remove(myRenderer);
                    mirrorAvatar.RenderersToHide.Remove(myRenderer);
                    break;
                }
            case RendererOptionsEnum.ForceHide:
                {

                    if (!mirrorAvatar.RenderersToHide.Contains(myRenderer))
                    {
                        mirrorAvatar.RenderersToHide.Add(myRenderer);
                    }
                    mirrorAvatar.RenderersToShow.Remove(myRenderer);
                    break;
                }
            case RendererOptionsEnum.ForceShow:
                {
                    if (!mirrorAvatar.RenderersToShow.Contains(myRenderer))
                    {
                        mirrorAvatar.RenderersToShow.Add(myRenderer);
                    }
                    mirrorAvatar.RenderersToHide.Remove(myRenderer);
                    break;
                }
        }
    }

    private void UpdateTransforms()
    {
        foreach (var t in myTransforms)
        {
            mirrorAvatar.skinnedMeshRendererTransformOptions[t] = firstPersonVisibility;
        }
        mirrorAvatar.optionsChanged = true;
    }
}

#pragma warning restore IDE0051