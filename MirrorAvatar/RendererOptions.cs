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
    void Start()
    {
        FindScript();
        gameObject.TryGetComponent(out myRenderer);
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
            mirrorAvatar.RemoveRendererOptions(transform);
    }
    void OnDidApplyAnimationProperties()
    {
        if (!mirrorAvatar)
            FindScript();
         
        if (!mirrorAvatar || firstPersonVisibility == previousFirstPersonVisibility)
            return;
         
        previousFirstPersonVisibility = firstPersonVisibility;
        if (mirrorAvatar)
            mirrorAvatar.optionsChanged = true;
        switch (firstPersonVisibility)
        {
            case RendererOptionsEnum.Default:
                {
                    if (myRenderer)
                    {
                        mirrorAvatar.RenderersToShow.Remove(myRenderer);
                        mirrorAvatar.RenderersToHide.Remove(myRenderer);
                    }
                    return;
                }
            case RendererOptionsEnum.ForceHide:
                {
                    if (myRenderer)
                    {
                        if (!mirrorAvatar.RenderersToHide.Contains(myRenderer))
                        {
                            mirrorAvatar.RenderersToHide.Add(myRenderer);
                        }
                        mirrorAvatar.RenderersToShow.Remove(myRenderer);
                    }
                    return;
                }
            case RendererOptionsEnum.ForceShow:
                {
                    if (myRenderer)
                    {
                        if (!mirrorAvatar.RenderersToShow.Contains(myRenderer))
                        {
                            mirrorAvatar.RenderersToShow.Add(myRenderer);
                        }
                        mirrorAvatar.RenderersToHide.Remove(myRenderer);
                    }
                    return;
                }
        }
    }
}

#pragma warning restore IDE0051