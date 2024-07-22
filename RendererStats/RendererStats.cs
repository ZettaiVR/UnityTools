using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Keep track of active renderers and their related statistics under a gameObject, and on individual renderers
/// </summary>
public class RendererStats : MonoBehaviour
{
    public bool isVisible;
    public int RendererCount;
    public int SkinnedRendererCount;
    public int MaterialCount;
    public int TrailLineRendererCount;
    public int ParticleSystemCount;
    public int FaceCount;
    public int TriangleCount;
    public int IndexCount;

    private bool ScanForMore = true;
    private bool hasMainStatsComponent = false;
    private Renderer myRenderer;
    private RendererStats mainStatsComponent;

    private static readonly List<Renderer> allRenderers = new List<Renderer>();

    // Start is called before the first frame update
    void Start()
    {
        Init();
    }
    public void Init()
    {
        if (TryGetComponent(out myRenderer))
        {
            isVisible = myRenderer.isVisible;
            GetStats();
            if (hasMainStatsComponent && isVisible)
            {
                OnBecameVisible();
            }
        }
        if (transform.childCount == 0 || !ScanForMore)
            return;

        gameObject.GetComponentsInChildren(true, allRenderers);
        for (int i = 0; i < allRenderers.Count; i++)
        {
            if (!allRenderers[i].TryGetComponent(out RendererStats stats))
            {
                stats = allRenderers[i].gameObject.AddComponent<RendererStats>();
            }
            stats.ScanForMore = false;
            stats.hasMainStatsComponent = true;
            stats.mainStatsComponent = this;
        }
        allRenderers.Clear();
    }
    private void GetStats() 
    {
        RendererCount = 1;
        MaterialCount = myRenderer.sharedMaterials.Length;
        Mesh mesh = null;
        switch (myRenderer)
        {
            case SkinnedMeshRenderer smr:
                SkinnedRendererCount = 1;
                mesh = smr.sharedMesh;
                break;
            case MeshRenderer meshRenderer:
                if (meshRenderer.TryGetComponent(out MeshFilter meshFilter)) 
                {
                    mesh = meshFilter.sharedMesh;
                }
                break;
            case ParticleSystemRenderer particleSystemRenderer:
                mesh = particleSystemRenderer.mesh;
                ParticleSystemCount = 1;
                break;
            case TrailRenderer:
            case LineRenderer:
                TrailLineRendererCount = 1; 
                break;
            default:
                break;
        }
        GetMeshFaceCount(mesh);
    }
    private void GetMeshFaceCount(Mesh mesh)
    {
        if (!mesh)
            return;
        
        for (int i = 0, length = mesh.subMeshCount; i < length; i++)
        {
            MeshTopology topology;
            SubMeshDescriptor submesh = mesh.GetSubMesh(i);
            topology = submesh.topology;
            switch (topology)
            {
                case MeshTopology.Lines:
                case MeshTopology.Points:
                case MeshTopology.LineStrip:
                    break; // these don't have triangles
                case MeshTopology.Quads:
                    IndexCount += submesh.indexCount;
                    int _faceCount = submesh.indexCount / 4;
                    FaceCount += _faceCount;            // a quad is one face,
                    TriangleCount += _faceCount * 2;    // but two triangles
                    break;
                case MeshTopology.Triangles:
                    IndexCount += submesh.indexCount;
                    int _triangleCount = submesh.indexCount / 3;
                    FaceCount += _triangleCount;
                    TriangleCount += _triangleCount;
                    break;
            }
        }
    }
    private void AddStats(RendererStats stats) 
    {
        RendererCount += stats.RendererCount;
        SkinnedRendererCount += stats.SkinnedRendererCount;
        MaterialCount += stats.MaterialCount;
        TriangleCount += stats.TriangleCount;
        IndexCount += stats.IndexCount;
        FaceCount += stats.FaceCount;
        ParticleSystemCount += stats.ParticleSystemCount;
        TrailLineRendererCount += stats.TrailLineRendererCount;
    }
    private void RemoveStats(RendererStats stats) 
    {
        RendererCount -= stats.RendererCount;
        SkinnedRendererCount -= stats.SkinnedRendererCount;
        MaterialCount -= stats.MaterialCount;
        TriangleCount -= stats.TriangleCount;
        IndexCount -= stats.IndexCount;
        FaceCount -= stats.FaceCount;
        ParticleSystemCount -= stats.ParticleSystemCount;
        TrailLineRendererCount -= stats.TrailLineRendererCount;
    }
    private void OnBecameVisible()
    {
        isVisible = true;
        if (hasMainStatsComponent)
            mainStatsComponent.AddStats(this);
    }
    private void OnBecameInvisible()
    {
        isVisible = false;
        if (hasMainStatsComponent)
            mainStatsComponent.RemoveStats(this);
    }
}