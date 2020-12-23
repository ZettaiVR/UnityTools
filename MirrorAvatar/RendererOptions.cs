using UnityEngine;

[RequireComponent(typeof(Renderer))]
public class RendererOptions : MonoBehaviour
{
    public enum RendererOptionsEnum
    {
        Default = 0,
        ForceHideInFirstPerson = 1,
        ForceShowInFirstPerson = 2
    }
    public RendererOptionsEnum FirstPersonVisibility;
    private RendererOptionsEnum _FirstPersonVisibility;    
    private MirrorAvatar MirrorAvatar;
    private Renderer myRenderer;
    private bool scriptFound = false;
    void Start()
    {
        if (MirrorAvatar == null)
        {
            MirrorAvatar = gameObject.GetComponentInParent<MirrorAvatar>();
            
        }
        if (myRenderer == null) 
        {
            myRenderer = gameObject.GetComponent<Renderer>();
        }
        if (MirrorAvatar == null) 
        {
            scriptFound = false;
            myRenderer.enabled = false;
            return;
        }
        scriptFound = true;
    }
    void LateUpdate()
    {
        if (scriptFound && (FirstPersonVisibility != _FirstPersonVisibility))
        {
            switch (FirstPersonVisibility)
            {
                case RendererOptionsEnum.Default:
                    {
                        MirrorAvatar.RenderersToShow.Remove(myRenderer);
                        MirrorAvatar.RenderersToHide.Remove(myRenderer);
                        break;
                    }
                case RendererOptionsEnum.ForceHideInFirstPerson:
                    {
                        if (!MirrorAvatar.RenderersToHide.Contains(myRenderer))
                        {
                            MirrorAvatar.RenderersToHide.Add(myRenderer);
                        }
                        MirrorAvatar.RenderersToShow.Remove(myRenderer);
                        break;
                    }
                case RendererOptionsEnum.ForceShowInFirstPerson:
                    {
                        if (!MirrorAvatar.RenderersToShow.Contains(myRenderer))
                        {
                            MirrorAvatar.RenderersToShow.Add(myRenderer);
                        }
                        MirrorAvatar.RenderersToHide.Remove(myRenderer);
                        break;
                    }
            }
            _FirstPersonVisibility = FirstPersonVisibility;
        }
    }
}
