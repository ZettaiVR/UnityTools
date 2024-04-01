using UnityEngine;
using System;
using System.Collections.Generic;
using UnityEngine.Rendering;
using UnityEngine.XR;
#if UNITY_EDITOR
using UnityEditor;
#endif

// Mirror that works in VR. Multipass rendering, works with multipass and single pass stereo modes

[System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE0090:Use 'new(...)'", Justification = "Not in C# 7.2")]
[ExecuteInEditMode] // Make mirror live-update even when not in play mode
public class MirrorReflection : MonoBehaviour
{
    private const string MirrorShaderName = "FX/MirrorReflection2";
    private const string MirrorDepthShaderName = "FX/MirrorDepth";
    private const string LeftEyeTextureName = "_ReflectionTexLeft";
    private const string RightEyeTextureName = "_ReflectionTexRight";
    private const string ReflectionCameraName = "MirrorReflection camera";
    private const string CullingCameraName = "Mirror culling camera";

    public AntiAliasing MockMSAALevel = AntiAliasing.MSAA_x4;
    public int resolutionLimit = 4096;
    public bool MoveMirrorCamera = true;
    public bool useVRAMOptimization = true;
    public bool m_DisablePixelLights = true;
    [Range(0.01f, 1f)]
    public float MaxTextureSizePercent = 1.0f;  // this should be a setting in config
    public LayerMask m_ReflectLayers = -1;
    public bool useAverageNormals = true;
    public bool useMask;
    public bool useMesh;
    public bool useFrustum = true;
    public bool useOcclusionCulling;
    public bool useNearPlane;

    public ClearFlags clearFlags = ClearFlags.Default;
    public Color backgroundColor = Color.gray;
#if UNITY_EDITOR
    public bool didRender;
#else
    private bool didRender;
#endif

    private bool hasCorners;
    private int actualMsaa;
    private bool useMsaaTexture;
    private int width;
    private int height;
    private Camera m_ReflectionCamera;
    private Camera m_CullingCamera;
    private CommandBuffer commandBufferClearRenderTargetToZero;
    private CommandBuffer commandBufferClearRenderTargetToOne;
    private CommandBuffer commandBufferDrawMesh;
    private CommandBuffer commandBufferSetRectFull;
    private CommandBuffer commandBufferSetRectLimited;
    private Material[] m_MaterialsInstanced;
    private RenderTexture m_ReflectionTextureMSAA;
    private RenderTexture m_ReflectionTextureLeft;
    private RenderTexture m_ReflectionTextureRight;
    private MaterialPropertyBlock materialPropertyBlock;
    private Renderer m_Renderer;
    private Mesh m_Mesh;
    private Vector3 mirrorNormal = Vector3.up;
    private Vector3 mirrorNormalAvg = Vector3.up;
    private Vector3 mirrorPlaneOffset = Vector3.zero;
    private Vector3x4 meshCorners;
    private Bounds boundsLocalSpace;
    private Matrix4x4 m_LocalToWorldMatrix;
    private Matrix4x4 meshTrs;

    private readonly List<Material> m_savedSharedMaterials = new List<Material>();   // Only relevant for the editor

    private static int LeftEyeTextureID = -1;
    private static int RightEyeTextureID = -1;
    private static bool s_InsideRendering = false;
    private static bool copySupported;
    private static Material m_DepthMaterial;
    private static RenderTexture cullingTex;
    private static readonly Quaternion rotateCamera = Quaternion.Euler(Vector3.up * 180f);
    private static readonly Dictionary<Camera, Skybox> SkyboxDict = new Dictionary<Camera, Skybox>();
    private static readonly List<Material> m_tempSharedMaterials = new List<Material>();
    private static readonly List<Vector3> vertices = new List<Vector3>();
    private static readonly List<Vector3> mirrorVertices = new List<Vector3>();
    private static readonly List<int> indicies = new List<int>();
    private static readonly HashSet<int> ints = new HashSet<int>();
    private static readonly Matrix4x4 m_InversionMatrix = new Matrix4x4 { m00 = -1, m11 = 1, m22 = 1, m33 = 1 };
    private static readonly Vector3[] outCorners = new Vector3[4];
    private static readonly Plane[] planes = new Plane[6];

    public enum AntiAliasing
    {
        Default = 0,
        MSAA_Off = 1,
        MSAA_x2 = 2,
        MSAA_x4 = 4,
        MSAA_x8 = 8,
    }
    public enum ClearFlags 
    {
        Default = 0,
        Skybox = 1,
        Color = 2,
    }
#if UNITY_EDITOR
    // Revert the instantiated materials to the originals
    void ModeChanged(PlayModeStateChange playModeState)
    {
        if (playModeState == PlayModeStateChange.EnteredEditMode)
        {
            if (m_Renderer != null && m_savedSharedMaterials.Count > 0)
                m_Renderer.sharedMaterials = m_savedSharedMaterials.ToArray();
        }
    }

#endif
    private void Start()
    {
#if UNITY_EDITOR
        EditorApplication.playModeStateChanged += ModeChanged;
#endif
        copySupported = SystemInfo.copyTextureSupport.HasFlag(CopyTextureSupport.Basic);

        // Move mirror to water layer
        gameObject.layer = 4;
        materialPropertyBlock = new MaterialPropertyBlock();

        if (!m_DepthMaterial)
        {
            CreateMirrorMaskMaterial();
        }
        if (!cullingTex)
        {
            cullingTex = RenderTexture.GetTemporary(1, 1);
        }

        m_Renderer = GetComponent<MeshRenderer>();
        if (!m_Renderer)
        {
            enabled = false;
            return;
        }
        if (m_Renderer.TryGetComponent<MeshFilter>(out var filter))
        {
            m_Mesh = filter.sharedMesh;
        }
        if (LeftEyeTextureID < 0 || RightEyeTextureID < 0)
        {
            LeftEyeTextureID = Shader.PropertyToID(LeftEyeTextureName);
            RightEyeTextureID = Shader.PropertyToID(RightEyeTextureName);
        }
        if (m_Mesh)
        {
            // find the first material that has mirror shader properties
            m_Renderer.GetSharedMaterials(m_savedSharedMaterials);
            m_tempSharedMaterials.Clear();
            m_tempSharedMaterials.AddRange(m_savedSharedMaterials);
            bool isEditor = Application.isEditor;
            int index = -1;
            for (int i = 0; i < m_tempSharedMaterials.Count; i++)
            {
                var material = m_tempSharedMaterials[i];
                if (material.HasProperty(LeftEyeTextureID) && material.HasProperty(RightEyeTextureID))
                {
                    index = i;
                    break;
                }
            }
            m_tempSharedMaterials.Clear();
            var mesh = m_Mesh;
            try
            {
                if (!mesh.isReadable)
                {
                    mesh = ReadItAnyway(mesh);
                }
                ReadMesh(mesh, index);
                FindMeshCorners();
            }
            catch (Exception) { }
            if (!mesh.isReadable)
            {
                mirrorNormal = mirrorNormalAvg = Vector3.up;
            }
        }


        // Make sure the mirror's material is unique
#if UNITY_EDITOR
        if (Application.isEditor)
        {
            // backup original material
            m_Renderer.GetSharedMaterials(m_savedSharedMaterials);

            if (m_tempSharedMaterials.Count == 0)
                return;
            for (int i = 0; i < m_tempSharedMaterials.Count; i++)
            {
                var material = m_tempSharedMaterials[i];
                if (material.shader.name == MirrorShaderName && !material.name.Contains("(Clone)"))
                    material = Instantiate(material);
            }
            m_Renderer.sharedMaterials = m_tempSharedMaterials.ToArray();
        }
        else
#endif
        {
            m_MaterialsInstanced = m_Renderer.materials;
        }
    }
    private Mesh ReadItAnyway(Mesh mesh) 
    {
        GameObject go = new GameObject();
        var smr = go.AddComponent<SkinnedMeshRenderer>();
        smr.sharedMesh = mesh;
        Mesh meshCopy = new Mesh();
        smr.BakeMesh(meshCopy);
        meshCopy.RecalculateBounds();
        GameObject.DestroyImmediate(go);
        return meshCopy;
    }
    private void FindMeshCorners()
    {
        (var min, var max, var mid) = GetMinMax(mirrorVertices);
        
        var plane = new Plane(mirrorNormalAvg, mid);
        var rot = Quaternion.FromToRotation(mirrorNormalAvg, Vector3.up);
        meshTrs = Matrix4x4.TRS(Vector3.zero, rot, Vector3.one);
        mirrorPlaneOffset = mid;

        vertices.Add(plane.ClosestPointOnPlane(min));
        vertices.Add(plane.ClosestPointOnPlane(new Vector3(min.x, min.y, max.z)));
        vertices.Add(plane.ClosestPointOnPlane(new Vector3(min.x, max.y, min.z)));
        vertices.Add(plane.ClosestPointOnPlane(new Vector3(min.x, max.y, max.z)));
        vertices.Add(plane.ClosestPointOnPlane(new Vector3(max.x, min.y, min.z)));
        vertices.Add(plane.ClosestPointOnPlane(new Vector3(max.x, min.y, max.z)));
        vertices.Add(plane.ClosestPointOnPlane(new Vector3(max.x, max.y, min.z)));
        vertices.Add(plane.ClosestPointOnPlane(max));

        for (int i = 0; i < vertices.Count; i++)
        {
            // make them face forward on the Z axis
            vertices[i] = meshTrs.MultiplyPoint3x4(vertices[i]);
        }
        (min, max, _) = GetMinMax(vertices);

        // the four corners of the mesh along it's plane in local space, 
        plane = new Plane(Vector3.up, meshTrs.MultiplyPoint3x4(mirrorPlaneOffset));
        var meshC0 = plane.ClosestPointOnPlane(min);
        var meshC1 = plane.ClosestPointOnPlane(new Vector3(min.x, min.y, max.z));
        var meshC2 = plane.ClosestPointOnPlane(max);
        var meshC3 = plane.ClosestPointOnPlane(new Vector3(max.x, min.y, min.z));
        meshCorners = new Vector3x4 { c0 = meshC0, c1 = meshC1, c2 = meshC2, c3 = meshC3 };

        boundsLocalSpace = new Bounds(meshCorners[0], Vector3.zero);
        boundsLocalSpace.Encapsulate(meshCorners[0]);
        boundsLocalSpace.Encapsulate(meshCorners[1]);
        boundsLocalSpace.Encapsulate(meshCorners[2]);
        boundsLocalSpace.Encapsulate(meshCorners[3]);
        var size = boundsLocalSpace.size;
        size.y = size.x;
        boundsLocalSpace.Expand(Vector3.one * (0.001f / size.magnitude));
        hasCorners = true;
        mirrorVertices.Clear();
    }

    private void ReadMesh(Mesh mesh, int index)
    {
        Vector3 allNormals = Vector3.zero;
        if (mesh.subMeshCount > 1 && index >= 0 && index < mesh.subMeshCount)
        {
            ints.Clear();
            mesh.GetIndices(indicies, index);
            for (int i = 0; i < indicies.Count; i++)
            {
                ints.Add(indicies[i]);
            }
            mesh.GetVertices(vertices);
            foreach (var item in ints)
            {
                mirrorVertices.Add(vertices[item]);
            }
            vertices.Clear();
            mesh.GetNormals(vertices);
            if (vertices.Count > 0)
                mirrorNormal = vertices[0];
            foreach (var item in ints)
            {
                allNormals += vertices[item];
            }
            ints.Clear();
            vertices.Clear();
        }
        else
        {
            mesh.GetVertices(mirrorVertices);
            mesh.GetNormals(vertices);
            foreach (var item in vertices)
            {
                allNormals += item;
            }
            // Get the mirror mesh normal from the first vertex
            if (vertices.Count > 0)
                mirrorNormal = vertices[0];
            vertices.Clear();
        }

        mirrorNormalAvg = allNormals.normalized;
        if (float.IsNaN(mirrorNormalAvg.x) || float.IsInfinity(mirrorNormalAvg.x) || float.IsNaN(mirrorNormalAvg.y) ||
            float.IsInfinity(mirrorNormalAvg.y) || float.IsNaN(mirrorNormalAvg.z) || float.IsInfinity(mirrorNormalAvg.z))
        {
            mirrorNormalAvg = mirrorNormal;
        }
    }

    private (Vector3 min, Vector3 max, Vector3 mid) GetMinMax(List<Vector3> list)
    {
        int count = list.Count;
        Vector3 min = Vector3.one * float.PositiveInfinity;
        Vector3 max = Vector3.one * float.NegativeInfinity;
        Vector3 mid = Vector3.zero;
        for (int i = 0; i < count; i++)
        {
            var v = list[i];
            mid += v;
            if (v.x < min.x) min.x = v.x;
            if (v.y < min.y) min.y = v.y;
            if (v.z < min.z) min.z = v.z;
            if (v.x > max.x) max.x = v.x;
            if (v.y > max.y) max.y = v.y;
            if (v.z > max.z) max.z = v.z;
        }
        return (min, max, mid / count);
    }

    private void CreateMirrorMaskMaterial()
    {
        var shader = Shader.Find(MirrorDepthShaderName);
        if (shader)
        {
            m_DepthMaterial = new Material(shader);
        }
    }

    private bool CreateCommandBuffer(ref CommandBuffer _commandBuffer, string name)
    {
        if (_commandBuffer == null)
        {
            _commandBuffer = new CommandBuffer { name = name };
            return true;
        }
        else
        {
            _commandBuffer.Clear();
        }
        return false;
    }

    private void OnDestroy()
    {
        if (m_MaterialsInstanced == null || m_MaterialsInstanced.Length == 0)
            return;
        for (int i = 0; i < m_MaterialsInstanced.Length; i++)
        {
            DestroyImmediate(m_MaterialsInstanced[i]);
        }
    }

    // This is called when it's known that the object will be rendered by some
    // camera. We render reflections and do other updates here.
    // Because the script executes in edit mode, reflections for the scene view
    // camera will just work!
    public void OnWillRenderObject()
    {
        didRender = false;
        if (!enabled || !m_Renderer || !m_Renderer.enabled)
            return;

        Camera currentCam = Camera.current;
        bool isStereo = currentCam.stereoEnabled;

        // Safeguard from recursive reflections.        
        if (s_InsideRendering || !currentCam)
            return;
        // Rendering will happen.

        // Force mirrors to Water layer
        if (gameObject.layer != 4)
            gameObject.layer = 4;

#if UNITY_EDITOR
        if (LeftEyeTextureID < 0 || RightEyeTextureID < 0)
        {
            LeftEyeTextureID = Shader.PropertyToID(LeftEyeTextureName);
            RightEyeTextureID = Shader.PropertyToID(RightEyeTextureName);
        }
        if (materialPropertyBlock == null)
            materialPropertyBlock = new MaterialPropertyBlock();
#endif
        actualMsaa = GetActualMSAA(currentCam, (int)MockMSAALevel);
        
        // Maximize the mirror texture resolution
        var res = UpdateRenderResolution(currentCam.pixelWidth, currentCam.pixelHeight);
        width = res.x;
        height = res.y;
        var currentCamLtwm = currentCam.transform.localToWorldMatrix;

        useMsaaTexture = useVRAMOptimization && currentCam.actualRenderingPath == RenderingPath.Forward && actualMsaa > 1 && copySupported;

        // Set flag that we're rendering to a mirror
        s_InsideRendering = true;

        // Optionally disable pixel lights for reflection
        int oldPixelLightCount = QualitySettings.pixelLightCount;

        m_LocalToWorldMatrix = transform.localToWorldMatrix;
        Vector3 mirrorPos = m_LocalToWorldMatrix.MultiplyPoint3x4(mirrorPlaneOffset);  // transform.position
        Vector3 normal = m_LocalToWorldMatrix.MultiplyVector(useAverageNormals ? mirrorNormalAvg : mirrorNormal).normalized;    // transform.TransformDirection

        if (m_DisablePixelLights)
            QualitySettings.pixelLightCount = 0;
        try
        {
            if (isStereo)
            {
                var eye = currentCam.stereoActiveEye;
                bool singlePass = XRSettings.stereoRenderingMode != XRSettings.StereoRenderingMode.MultiPass;
                bool renderLeft = singlePass || eye == Camera.MonoOrStereoscopicEye.Left;
                bool renderRight = singlePass || eye == Camera.MonoOrStereoscopicEye.Right;
                if (renderLeft)
                    RenderCamera(currentCam, currentCamLtwm, mirrorPos, normal, Camera.MonoOrStereoscopicEye.Left);
                if (renderRight)
                    RenderCamera(currentCam, currentCamLtwm, mirrorPos, normal, Camera.MonoOrStereoscopicEye.Right);
            }
            else
            {
                RenderCamera(currentCam, currentCamLtwm, mirrorPos, normal, Camera.MonoOrStereoscopicEye.Mono);
            }
            if (m_ReflectionTextureLeft)
                materialPropertyBlock.SetTexture(LeftEyeTextureID, m_ReflectionTextureLeft);
            if (m_ReflectionTextureRight)
                materialPropertyBlock.SetTexture(RightEyeTextureID, m_ReflectionTextureRight);
            m_Renderer.SetPropertyBlock(materialPropertyBlock);
        }
        finally
        {
            s_InsideRendering = false;
            if (m_DisablePixelLights)
                QualitySettings.pixelLightCount = oldPixelLightCount;
#if !UNITY_EDITOR
            if (useMsaaTexture || m_ReflectionTextureMSAA != null)
            {
                try
                {
                    RenderTexture.ReleaseTemporary(m_ReflectionTextureMSAA);
                }
                catch (NullReferenceException)
                { }
            }
#endif
        }
    }

    /// <summary>
    /// Get actual MSAA level from render texture (if the current camera renders to one) or QualitySettings
    /// </summary>
    private static int GetActualMSAA(Camera currentCam, int maxMSAA)
    {
        var targetTexture = currentCam.targetTexture;
        int cameraMsaa;
        if (targetTexture != null)
        {
            cameraMsaa = targetTexture.antiAliasing;
        }
        else
        {
            cameraMsaa = QualitySettings.antiAliasing == 0 ? 1 : QualitySettings.antiAliasing;
        }
        var actualMsaa = Mathf.Max(cameraMsaa, 1);
        if (maxMSAA > 0)
        {
            actualMsaa = Mathf.Min(actualMsaa, maxMSAA);
        }
        return actualMsaa;
    }

    private Vector2Int UpdateRenderResolution(int width, int height)
    {
        var max = Mathf.Clamp(MaxTextureSizePercent, 0.01f, 1f);
        var size = width * height;
        var limit = resolutionLimit * resolutionLimit;

        if (size > limit && resolutionLimit < 4096)
        {
            var _maxRes = Math.Sqrt(limit / (float)size);
            max *= (float)_maxRes;
        }
        max = Mathf.Clamp(max, 0.01f, 1f);
        int w = (int)(width * max + 0.5f);
        int h = (int)(height * max + 0.5f);
        return new Vector2Int(w, h);
    }
    private static void SetupMsaaTexture(ref RenderTexture rt, bool useMsaaTexture, int width, int height, int msaa, bool allowHDR) 
    {
        if (!useMsaaTexture && rt)
        {
            RenderTexture.ReleaseTemporary(rt);
            rt = null;
            return;
        }
        if (rt && rt.IsCreated() && rt.width == width && rt.height == height && rt.antiAliasing == msaa &&
            rt.format == (allowHDR ? RenderTextureFormat.ARGBHalf : RenderTextureFormat.ARGB32))
            return;
        SetupRenderTexture(ref rt, width, height, true, msaa, allowHDR);
    }
    private static void SetupRenderTexture(ref RenderTexture rt, int width, int height, bool depth, int msaa, bool allowHDR)
    {
        if (rt)
            RenderTexture.ReleaseTemporary(rt);
        rt = RenderTexture.GetTemporary(width, height, depth ? 24 : 0,
            allowHDR ? RenderTextureFormat.ARGBHalf : RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default, msaa);
    }
    private static bool IsVisible(Matrix4x4 localToWorldMatrix, float stereoSep, Camera.MonoOrStereoscopicEye eye, Vector3 mirrorPos, Vector3 normal, bool isStereo)
    {
        Vector4 currentCamPos = localToWorldMatrix.GetColumn(3);
        if (!isStereo)
        {
            if (!Visible(currentCamPos, mirrorPos, normal))
                return false;
        }        // Replace with proper eye positions if possible
        else
        {
            var v3_eye = currentCamPos + localToWorldMatrix * ((eye == Camera.MonoOrStereoscopicEye.Left ? Vector3.left : Vector3.right) * stereoSep / 2f);
            var v3_eye2 = currentCamPos + localToWorldMatrix * ((eye == Camera.MonoOrStereoscopicEye.Left ? Vector3.left : Vector3.right) * stereoSep);

            if (!(Visible(v3_eye, mirrorPos, normal) || Visible(v3_eye2, mirrorPos, normal)))
                return false;
        }
        return true;
    }
    private void RenderCamera(Camera currentCam, in Matrix4x4 localToWorldMatrix, in Vector3 mirrorPos, in Vector3 normal, Camera.MonoOrStereoscopicEye eye)
    {
        bool isStereo = eye != Camera.MonoOrStereoscopicEye.Mono;
        bool isRightEye = eye == Camera.MonoOrStereoscopicEye.Right;
        if (!IsVisible(localToWorldMatrix, currentCam.stereoSeparation, eye, mirrorPos, normal, isStereo))
            return;

        SetupMsaaTexture(ref m_ReflectionTextureMSAA, useMsaaTexture, width, height, actualMsaa, currentCam.allowHDR);       

        var reflectionTexture = isRightEye ? m_ReflectionTextureRight : m_ReflectionTextureLeft;
        CreateMirrorObjects(useMsaaTexture, ref reflectionTexture, currentCam.allowHDR);
        if (isRightEye)
            m_ReflectionTextureRight = reflectionTexture;
        else
            m_ReflectionTextureLeft = reflectionTexture;

        UpdateCameraModes(currentCam, m_ReflectionCamera);

        var targetTexture = useMsaaTexture ? m_ReflectionTextureMSAA : reflectionTexture;

        m_ReflectionCamera.depthTextureMode = currentCam.depthTextureMode | DepthTextureMode.Depth;
        m_ReflectionCamera.stereoTargetEye = StereoTargetEyeMask.None;
        m_ReflectionCamera.targetTexture = targetTexture;
        m_ReflectionCamera.cullingMask = -17 & m_ReflectLayers.value; // mirrors never render the water layer, as they are on the water layer. mask: 1111 1111 1111 1111 1111 1111 1110 1111

        // find out the reflection plane: position and normal in world space
        float d = -Vector3.Dot(normal, mirrorPos);
        Vector4 reflectionPlane = new Vector4(normal.x, normal.y, normal.z, d);

        // Reflect camera around reflection plane
        Matrix4x4 worldToCameraMatrix = CalculateReflectionMatrix(reflectionPlane);

        var viewMatrix = isStereo ? currentCam.GetStereoViewMatrix((Camera.StereoscopicEye)eye) : currentCam.worldToCameraMatrix;
        worldToCameraMatrix = m_InversionMatrix * viewMatrix * worldToCameraMatrix;
        m_ReflectionCamera.worldToCameraMatrix = worldToCameraMatrix;        
        var wtinv = worldToCameraMatrix.inverse;
        var reflectedRot = (wtinv * m_InversionMatrix).rotation * rotateCamera;
        m_ReflectionCamera.transform.SetPositionAndRotation(wtinv.MultiplyPoint(Vector3.zero), reflectedRot);
        
        // Setup oblique projection matrix so that near plane is our reflection plane.
        // This way we clip everything below/above it for free.
        Vector4 clipPlane = CameraSpacePlane(worldToCameraMatrix, mirrorPos, normal, 1.0f);

        var projectionMatrix = isStereo ? currentCam.GetStereoProjectionMatrix((Camera.StereoscopicEye)eye) : currentCam.projectionMatrix;
        m_ReflectionCamera.projectionMatrix = m_InversionMatrix * projectionMatrix * m_InversionMatrix;
        if (!TryGetRectPixel(m_ReflectionCamera, meshCorners, boundsLocalSpace, m_Renderer.localToWorldMatrix * meshTrs.inverse, out var frustum, out float nearDistance, out Rect pixelRect))
        {
            return;
        }

        bool willUseFrustum = false;
        bool validRect = false;

        if (hasCorners && pixelRect.height >= 1 && pixelRect.width >= 1)
        {
            validRect = true;
        }
        if (useFrustum && validRect)
        {
            m_ReflectionCamera.pixelRect = pixelRect;
            m_ReflectionCamera.projectionMatrix = frustum;
            m_ReflectionCamera.useOcclusionCulling = useOcclusionCulling;
            willUseFrustum = true;
        }
        if (nearDistance > 0f && useNearPlane)
        {
            m_ReflectionCamera.nearClipPlane = nearDistance;
        }
        m_ReflectionCamera.projectionMatrix = m_ReflectionCamera.CalculateObliqueMatrix(clipPlane);
        m_CullingCamera.farClipPlane = currentCam.farClipPlane;
        m_ReflectionCamera.cullingMatrix = m_CullingCamera.cullingMatrix;
        if (useMask)
        {
            var pixelWidth = targetTexture.width;
            var pixelHeight = targetTexture.height;
            RenderWithCommanBuffers(new Rect(0, 0, pixelWidth, pixelHeight), pixelRect, willUseFrustum, validRect);
        }
        else
        {
            m_ReflectionCamera.Render();
        }
        didRender = true;

        if (useMsaaTexture)
            CopyTexture(reflectionTexture, pixelRect, validRect);
    }

    private void CopyTexture(RenderTexture rt, Rect rect, bool validRect)
    {
        if (validRect)
        {
            var pos = rect.position;
            var size = rect.size;
            int x = (int)pos.x;
            int y = (int)pos.y;
            int w = (int)size.x;
            int h = (int)size.y;
            Graphics.CopyTexture(m_ReflectionTextureMSAA, 0, 0, x, y, w, h, rt, 0, 0, x, y);
        }
        else
        {
            Graphics.CopyTexture(m_ReflectionTextureMSAA, rt);
        }
    }

    private void RenderWithCommanBuffers(Rect fullRect, Rect pixelRect, bool willUseFrustum, bool validRect)
    {
        CreateCommandBuffers(fullRect, pixelRect);
        m_ReflectionCamera.AddCommandBuffer(CameraEvent.BeforeForwardOpaque, commandBufferSetRectFull);
        m_ReflectionCamera.AddCommandBuffer(CameraEvent.BeforeForwardOpaque, commandBufferClearRenderTargetToZero);
        if (validRect)
        {
            m_ReflectionCamera.AddCommandBuffer(CameraEvent.BeforeForwardOpaque, commandBufferSetRectLimited);
            m_ReflectionCamera.AddCommandBuffer(CameraEvent.BeforeForwardOpaque, commandBufferClearRenderTargetToOne);
        }
        if (!willUseFrustum || useMesh)
            m_ReflectionCamera.AddCommandBuffer(CameraEvent.BeforeForwardOpaque, commandBufferSetRectFull);
        if (useMesh)
            m_ReflectionCamera.AddCommandBuffer(CameraEvent.BeforeForwardOpaque, commandBufferDrawMesh);
        if (willUseFrustum)
            m_ReflectionCamera.AddCommandBuffer(CameraEvent.BeforeForwardOpaque, commandBufferSetRectLimited);
        m_ReflectionCamera.Render();
        m_ReflectionCamera.RemoveCommandBuffer(CameraEvent.BeforeForwardOpaque, commandBufferClearRenderTargetToZero);
        m_ReflectionCamera.RemoveCommandBuffer(CameraEvent.BeforeForwardOpaque, commandBufferDrawMesh);
        m_ReflectionCamera.RemoveCommandBuffer(CameraEvent.BeforeForwardOpaque, commandBufferSetRectFull);
        m_ReflectionCamera.RemoveCommandBuffer(CameraEvent.BeforeForwardOpaque, commandBufferSetRectLimited);
        m_ReflectionCamera.RemoveCommandBuffer(CameraEvent.BeforeForwardOpaque, commandBufferClearRenderTargetToOne);
    }

    private void CreateCommandBuffers(Rect pixelRectFull, Rect pixelRectLimited)
    {
        var zeroCreated = CreateCommandBuffer(ref commandBufferClearRenderTargetToZero, "Occlusion buffer - ClearRenderTarget 0");
        var oneCreate = CreateCommandBuffer(ref commandBufferClearRenderTargetToOne, "Occlusion buffer - ClearRenderTarget 1");
        CreateCommandBuffer(ref commandBufferDrawMesh, "Occlusion buffer - DrawMesh");
        CreateCommandBuffer(ref commandBufferSetRectFull, "Occlusion buffer - SetRect full");
        CreateCommandBuffer(ref commandBufferSetRectLimited, "Occlusion buffer - SetRect limited");

        if (!m_DepthMaterial)
            CreateMirrorMaskMaterial();

        if (!m_DepthMaterial)
            return;

        if (zeroCreated)
            commandBufferClearRenderTargetToZero.ClearRenderTarget(clearDepth: true, clearColor: false, backgroundColor: Color.blue, depth: 0);
        if (oneCreate)
            commandBufferClearRenderTargetToOne.ClearRenderTarget(clearDepth: true, clearColor: false, backgroundColor: Color.blue, depth: 1);


        if (m_Mesh)
        {
            commandBufferDrawMesh.DrawMesh(m_Mesh, m_LocalToWorldMatrix, m_DepthMaterial);
        }
        else
        {
            commandBufferDrawMesh.DrawRenderer(m_Renderer, m_DepthMaterial);
        }
        commandBufferSetRectFull.SetViewport(pixelRectFull);
        commandBufferSetRectLimited.SetViewport(pixelRectLimited);
    }

    // Cleanup all the objects we possibly have created
    void OnDisable()
    {
        if (m_ReflectionTextureLeft)
        {
            RenderTexture.ReleaseTemporary(m_ReflectionTextureLeft);
            m_ReflectionTextureLeft = null;
        }
        if (m_ReflectionTextureRight)
        {
            RenderTexture.ReleaseTemporary(m_ReflectionTextureRight);
            m_ReflectionTextureRight = null;
        }
        if (m_ReflectionTextureMSAA)
        {
            RenderTexture.ReleaseTemporary(m_ReflectionTextureMSAA);
            m_ReflectionTextureMSAA = null;
        }
    }
    private void UpdateCameraModes(Camera src, Camera dest)
    {
        if (!dest)
            return;
        // set camera to clear the same way as current camera
        dest.CopyFrom(src);
        dest.enabled = false;
        if (clearFlags != ClearFlags.Color && src.clearFlags == CameraClearFlags.Skybox)
        {
            Skybox sky = GetSkybox(src);
            Skybox mysky = GetSkybox(dest);
            if (!mysky)
                return;
            if (!sky || !sky.material)
            {
                mysky.enabled = false;
            }
            else
            {
                mysky.enabled = true;
                mysky.material = sky.material;
            }
        }
        else if (clearFlags == ClearFlags.Color)
        {
            dest.clearFlags = CameraClearFlags.Color;
            dest.backgroundColor = backgroundColor;
        }
    }
    private Skybox GetSkybox(Camera camera)
    {
        if (!SkyboxDict.TryGetValue(camera, out Skybox skybox))
        {
            SkyboxDict[camera] = camera.GetComponent<Skybox>();
        }
        return skybox;
    }

    // On-demand create any objects we need
    private void CreateMirrorObjects(bool useMsaaTexture, ref RenderTexture reflectionTexture, bool allowHDR)
    {
        // Reflection render texture
        int msaa = useMsaaTexture ? 1 : actualMsaa;
        SetupRenderTexture(ref reflectionTexture, width, height, !useMsaaTexture, msaa, allowHDR);

       
        if (!m_CullingCamera)
        {
            GameObject _culling = new GameObject(CullingCameraName);
            _culling.transform.SetParent(transform);
            _culling.transform.localPosition = -mirrorNormalAvg * 0.01f;
            _culling.transform.LookAt(transform);
            m_CullingCamera = _culling.AddComponent<Camera>();
            m_CullingCamera.fieldOfView = 175f;
            m_CullingCamera.nearClipPlane = 0.01f;
            m_CullingCamera.enabled = false;
            m_CullingCamera.targetTexture = cullingTex;
            _culling.hideFlags = HideFlags.DontSave;
        }
        if (m_ReflectionCamera != null)
            return; // Camera already exists
        // Create Camera for reflection

        GameObject cameraGameObject = new GameObject(ReflectionCameraName);
        cameraGameObject.transform.SetParent(transform);
        cameraGameObject.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        m_ReflectionCamera = cameraGameObject.AddComponent<Camera>();
        SkyboxDict[m_ReflectionCamera] = cameraGameObject.AddComponent<Skybox>();
        m_ReflectionCamera.enabled = false;
        m_ReflectionCamera.gameObject.AddComponent<FlareLayer>();
        m_ReflectionCamera.clearFlags = CameraClearFlags.Nothing;
        cameraGameObject.hideFlags =
#if UNITY_EDITOR
            HideFlags.DontSave;
#else
            HideFlags.HideAndDontSave;
#endif
      

    }

    public static bool Visible(in Vector3 viewPosition, in Vector3 objectPosition, in Vector3 objectNormal)
    {
        return Vector3.Dot(viewPosition - objectPosition, objectNormal) > 0;
    }


    // Given position/normal of the plane, calculates plane in camera space.
    public static Vector4 CameraSpacePlane(Matrix4x4 worldToCameraMatrix, Vector3 pos, Vector3 normal, float sideSign)
    {
        Vector3 lhs = worldToCameraMatrix.MultiplyPoint(pos);
        Vector3 rhs = worldToCameraMatrix.MultiplyVector(normal).normalized * sideSign;
        return new Vector4(rhs.x, rhs.y, rhs.z, -Vector3.Dot(lhs, rhs));
    }

    // Calculates reflection matrix around the given plane
    public static Matrix4x4 CalculateReflectionMatrix(Vector4 plane)
    {
        float x = plane.x;
        float y = plane.y;
        float z = plane.z;
        float w = plane.w;

        column0.x = (1F - 2F * x * x);
        column1.x = (-2F * x * y);
        column2.x = (-2F * x * z);
        column3.x = (-2F * w * x);

        column0.y = (-2F * y * x);
        column1.y = (1F - 2F * y * y);
        column2.y = (-2F * y * z);
        column3.y = (-2F * w * y);

        column0.z = (-2F * z * x);
        column1.z = (-2F * z * y);
        column2.z = (1F - 2F * z * z);
        column3.z = (-2F * w * z);

        return new Matrix4x4(column0, column1, column2, column3);
    }
    private static Vector4 column0 = new Vector4(0, 0, 0, 0);
    private static Vector4 column1 = new Vector4(0, 0, 0, 0);
    private static Vector4 column2 = new Vector4(0, 0, 0, 0);
    private static Vector4 column3 = new Vector4(0, 0, 0, 1);
    private static bool TryGetRectPixel(Camera camera, Vector3x4 meshCorners, Bounds boundsLocalSpace, Matrix4x4 ltwm, out Matrix4x4 frustum, out float nearDistance, out Rect pixelRect)
    {
        Span<Vector3> allCornersSpan = stackalloc Vector3[24];
        Span<Vector3> worldCornersSpan = stackalloc Vector3[24];

        var texture = camera.targetTexture;
        int textureWidth = texture.width;
        var textureHeight = texture.height;
        var index = CheckCorners(camera, meshCorners, boundsLocalSpace, ltwm, allCornersSpan, worldCornersSpan);
        var allCorners = allCornersSpan.Slice(0, index);
        var worldCorners = worldCornersSpan.Slice(0, index);
        nearDistance = -1f;
        frustum = Matrix4x4.identity;
        pixelRect = new Rect();
        bool negativeZ = FindExtremesZ(allCorners, out float left, out float right, out float top, out float bottom);
        if (negativeZ)
        {
            return false;
        }
        var leftClamped = Mathf.Clamp01(left);
        var rightClamped = Mathf.Clamp01(right);
        var topClamped = Mathf.Clamp01(top);
        var bottomClamped = Mathf.Clamp01(bottom);
        var width = rightClamped - leftClamped;
        var height = topClamped - bottomClamped;

        if (width <= 0 || height <= 0)
        {
            return false;
        }
        var relevant = RectIsRelevant(left, right, top, bottom) && !(width > 0.95f && height > 0.95f);
        if (!relevant)
        {
            frustum = camera.projectionMatrix;
            pixelRect = new Rect(0, 0, textureWidth, textureHeight);
            return true;
        }

        leftClamped = (int)((leftClamped * textureWidth) + 0.5f);
        rightClamped = (int)((rightClamped * textureWidth) + 0.5f);
        topClamped = (int)((topClamped * textureHeight) + 0.5f);
        bottomClamped = (int)((bottomClamped * textureHeight) + 0.5f);
        width = rightClamped - leftClamped;
        height = topClamped - bottomClamped;

        pixelRect = new Rect(leftClamped, bottomClamped, width, height);

        leftClamped /= textureWidth;
        rightClamped /= textureWidth;
        topClamped /= textureHeight;
        bottomClamped /= textureHeight;
        width = rightClamped - leftClamped;
        height = topClamped - bottomClamped;

        var bounds = new Rect(leftClamped, bottomClamped, width, height);
        nearDistance = float.MaxValue;
        var wtcm = camera.transform.worldToLocalMatrix;
        var scale = wtcm.lossyScale.z;
        for (int i = 0; i < worldCorners.Length; i++)
        {
            nearDistance = Mathf.Min(nearDistance, wtcm.MultiplyPoint3x4(worldCorners[i]).z / scale);
        }
        camera.CalculateFrustumCorners(bounds, nearDistance, Camera.MonoOrStereoscopicEye.Mono, outCorners);
        FindExtremes(out left, out right, out top, out bottom);

        frustum = Matrix4x4.Frustum(left, right, bottom, top, nearDistance, camera.farClipPlane);
        return true;
    }

    private static void FindExtremes(out float left, out float right, out float top, out float bottom)
    {
        right = float.MinValue;
        left = float.MaxValue;
        top = float.MinValue;
        bottom = float.MaxValue;
        float minZ = float.MaxValue;

        for (int i = 0; i < outCorners.Length; i++)
        {
            var current = outCorners[i];
            if (right < current.x) { right = current.x; }
            if (left > current.x) { left = current.x; }
            if (top < current.y) { top = current.y; }
            if (bottom > current.y) { bottom = current.y; }
            if (minZ > current.z) minZ = current.z;
        }
    }

    private static bool FindExtremesZ(Span<Vector3> allCorners, out float left, out float right, out float top, out float bottom)
    {
        left = float.MaxValue;
        right = float.MinValue;
        top = float.MinValue;
        bottom = float.MaxValue;
        var minZl = float.MaxValue;
        var minZr = float.MaxValue;
        var minZt = float.MaxValue;
        var minZb = float.MaxValue;
        for (int i = 0; i < allCorners.Length; i++)
        {
            var current = allCorners[i];
            if (right < current.x && current.z > 0.001f) { right = current.x; if (current.z < minZl) minZl = current.z; }
            if (left > current.x && current.z > 0.001f) { left = current.x; if (current.z < minZr) minZr = current.z; }
            if (top < current.y && current.z > 0.001f) { top = current.y; if (current.z < minZt) minZt = current.z; }
            if (bottom > current.y && current.z > 0.001f) { bottom = current.y; if (current.z < minZb) minZb = current.z; }
        }
        return (minZl <= 0 || minZr <= 0 || minZt <= 0 || minZb <= 0);
    }



    /// <summary>
    /// Check if any point has a negative depth, and generate new points from the intersection of camera frustum planes and the edges of the mirror
    /// </summary>
    private static int CheckCorners(Camera camera, Vector3x4 meshCorners, Bounds boundsLocalSpace, Matrix4x4 ltwm, Span<Vector3> allCornersSpan, Span<Vector3> worldCornersSpan)
    {
        int index = 0;
        var worldCorners = meshCorners.MultiplyPoint3x4(ltwm);
        var cameraPos = camera.cameraToWorldMatrix.MultiplyPoint3x4(Vector3.zero);
        var mirrorPlane = new Plane(worldCorners.c0, worldCorners.c1, worldCorners.c2);
        GeometryUtility.CalculateFrustumPlanes(camera, planes);
        Span<Vector3> span = stackalloc Vector3[4];
        span[0] = worldCorners[0];
        span[1] = worldCorners[1];
        span[2] = worldCorners[2];
        span[3] = worldCorners[3];
        index += GetScreenCorners(camera, span, allCornersSpan, worldCornersSpan);
        if (index < 4)
            index += CalculateAdditionalPoints(ltwm, boundsLocalSpace, worldCorners, cameraPos, mirrorPlane, camera, allCornersSpan.Slice(index), worldCornersSpan.Slice(index));
        return index;
    }
    private static int CalculateAdditionalPoints(Matrix4x4 ltwm, Bounds boundsLocalSpace, Vector3x4 corners, Vector3 center, Plane mirrorPlane, Camera camera, Span<Vector3> allCornersSpan, Span<Vector3> worldCornersSpan)
    {
        Span<Vector3> span = stackalloc Vector3[20];
        int index = 0;
        var wtlm = ltwm.inverse;
        Vector3 I;
        if (PlanesIntersectAtSinglePoint(mirrorPlane, planes[0], planes[2], out I) && boundsLocalSpace.Contains(wtlm.MultiplyPoint3x4(I))) span[index++] = I;
        if (PlanesIntersectAtSinglePoint(mirrorPlane, planes[0], planes[3], out I) && boundsLocalSpace.Contains(wtlm.MultiplyPoint3x4(I))) span[index++] = I;
        if (PlanesIntersectAtSinglePoint(mirrorPlane, planes[1], planes[2], out I) && boundsLocalSpace.Contains(wtlm.MultiplyPoint3x4(I))) span[index++] = I;
        if (PlanesIntersectAtSinglePoint(mirrorPlane, planes[1], planes[3], out I) && boundsLocalSpace.Contains(wtlm.MultiplyPoint3x4(I))) span[index++] = I;
        for (int i = 0; i < 4; i++)
        {
            var corner = corners[i];
            var nextCorner = corners[i + 1];
            var prevCorner = corners[i - 1];
            for (int j = 0; j < 4; j++)
            {
                if (SegmentPlane(corner, nextCorner, center, planes[j].normal, out I) && boundsLocalSpace.Contains(wtlm.MultiplyPoint3x4(I))) span[index++] = I;
            }
        }
        if (index > 0)
            return GetScreenCorners(camera, span.Slice(0, index), allCornersSpan, worldCornersSpan);
        return 0;
    }
    private static int GetScreenCorners(Camera camera, Span<Vector3> worldPoints, Span<Vector3> allCornersSpan, Span<Vector3> worldCornersSpan)
    {
        int index = 0;
        for (int i = 0; i < worldPoints.Length; i++)
        {
            var value = camera.WorldToViewportPoint(worldPoints[i]);
            if (CornerWithin01(value))
            {
                allCornersSpan[index] = value;
                worldCornersSpan[index++] = worldPoints[i];
            }
        }
        return index;
    }
    private static bool CornerWithin01(Vector3 point) => 
        point.z >= 0f 
        && point.x < 1.0001f && point.x > -0.0001f 
        && point.y <= 1.0001f && point.y >= -0.0001f;

    /// <summary>
    /// Find the intersection between a line segment and a plane.
    /// </summary>
    /// <param name="p0">Start point of the line segment</param>
    /// <param name="p1">End point of the line segment</param>
    /// <param name="planeCenter">Position of the plane</param>
    /// <param name="planeNormal">Normal of the plane</param>
    /// <param name="I">Intersection point</param>
    /// <returns>true if there is an intersection</returns>
    /// https://gamedev.stackexchange.com/questions/178477/whats-wrong-with-my-line-segment-plane-intersection-code
    private static bool SegmentPlane(Vector3 p0, Vector3 p1, Vector3 planeCenter, Vector3 planeNormal, out Vector3 I)
    {
        Vector3 u = p1 - p0;
        Vector3 w = p0 - planeCenter;

        float D = Vector3.Dot(planeNormal, u);
        float N = -Vector3.Dot(planeNormal, w);

        if (Mathf.Abs(D) < Mathf.Epsilon)
        {
            // segment is parallel to plane
            if (N == 0)                         // segment lies in plane
            {
                I = p0;                         // We could return anything between p0 and p1 here, all points are on the plane
                return true;
            }
            else
            {
                I = Vector3.zero;
                return false;                   // no intersection
            }
        }

        // they are not parallel
        // compute intersect param
        float sI = N / D;
        if (sI < 0 || sI > 1)
        {
            I = Vector3.zero;
            return false;                       // no intersection
        }
        I = p0 + sI * u;                        // compute segment intersect point
        return true;
    }
    // https://gist.github.com/StagPoint/2eaa878f151555f9f96ae7190f80352e
    private static bool PlanesIntersectAtSinglePoint(in Plane p0, in Plane p1, in Plane p2, out Vector3 intersectionPoint)
    {
        const float EPSILON = 1e-4f;

        var p0normal = p0.normal;
        var p1normal = p1.normal;
        var p2normal = p2.normal;

        var det = (Vector3.Dot(Vector3.Cross(p0normal, p1normal), p2normal));
        if (Math.Abs(det) < EPSILON)
        {
            intersectionPoint = Vector3.zero;
            return false;
        }

        intersectionPoint =
            (-(p0.distance * Vector3.Cross(p1normal, p2normal)) -
            (p1.distance * Vector3.Cross(p2normal, p0normal)) -
            (p2.distance * Vector3.Cross(p0normal, p1normal))) / det;

        return true;
    }

    /// <summary>
    /// Check if at least one dimension is between 0 and 1
    /// </summary>
    private static bool RectIsRelevant(float left, float right, float top, float bottom)
    {
        return (left > 0 && left < 1) ||
               (right > 0 && right < 1) ||
               (top > 0 && top < 1) ||
               (bottom > 0 && bottom < 1);
    }



    private struct Vector3x4
    {
        public Vector3 c0;
        public Vector3 c1;
        public Vector3 c2;
        public Vector3 c3;
        public int Length => 4;

        public Vector3 this[int index]
        {
            get
            {
                switch (index)
                {
                    case -1: return c3;
                    case 0: return c0;
                    case 1: return c1;
                    case 2: return c2;
                    case 3: return c3;
                    case 4: return c0;
                    default: return Vector3.zero;
                }
            }
            set
            {
                switch (index)
                {
                    case 0: c0 = value; return;
                    case 1: c1 = value; return;
                    case 2: c2 = value; return;
                    case 3: c3 = value; return;
                    default: return;
                }
            }
        }
        public Vector3x4 MultiplyPoint3x4(Matrix4x4 ltwm) => new Vector3x4 { c0 = ltwm.MultiplyPoint3x4(c0), c1 = ltwm.MultiplyPoint3x4(c1), c2 = ltwm.MultiplyPoint3x4(c2), c3 = ltwm.MultiplyPoint3x4(c3) };
    }
}