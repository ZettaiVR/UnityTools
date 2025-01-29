using System.Collections.Generic;
using UnityEngine;

#pragma warning disable IDE0051
public class MirrorAvatarOptions : MonoBehaviour
{
    public enum RendererOptionsEnum
    {
        Default = 0,
        Hide = 1,
        Show = 2
    }
    public RendererOptionsEnum firstPersonVisibility = RendererOptionsEnum.Default;
    public bool HideAllChildren = true;
    public List<Transform> transformsToHide = new List<Transform>();

    private RendererOptionsEnum previousFirstPersonVisibility = RendererOptionsEnum.Default;
    private MirrorAvatar mirrorAvatar;
    internal Renderer myRenderer;
    void Start()
    {
        FindScript();
        if (!gameObject.TryGetComponent(out myRenderer))
        {
            if (HideAllChildren)
            {
                transform.GetComponentsInChildren(true, transformsToHide);
            }
            else if (transformsToHide.Count == 0)
            {
                transformsToHide.Add(transform);
            }
            mirrorAvatar.UpdateFromOptions(transformsToHide, firstPersonVisibility);
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
            foreach (var t in transformsToHide)
            {
                mirrorAvatar.SkinnedMeshRendererTransformOptions.Remove(t);
            }
        }
    }
    private void OnDisable()
    {
        mirrorAvatar.RemoveRendererOptions(transform);
        mirrorAvatar.UpdateFromOptions(transformsToHide, RendererOptionsEnum.Default);
    }
    private void OnEnable()
    {
        OnDidApplyAnimationProperties();
    }
    void OnDidApplyAnimationProperties()
    {
        if (!mirrorAvatar)
            FindScript();

        if (!mirrorAvatar)
            return;

        previousFirstPersonVisibility = firstPersonVisibility;

        mirrorAvatar.UpdateFromOptions(transformsToHide, firstPersonVisibility);

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
            case RendererOptionsEnum.Hide:
                {

                    if (!mirrorAvatar.RenderersToHide.Contains(myRenderer))
                    {
                        mirrorAvatar.RenderersToHide.Add(myRenderer);
                    }
                    mirrorAvatar.RenderersToShow.Remove(myRenderer);
                    break;
                }
            case RendererOptionsEnum.Show:
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
}

#pragma warning restore IDE0051