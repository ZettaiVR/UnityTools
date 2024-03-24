﻿using UnityEngine;
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
    private const string ReflectionCameraName = "MirrorReflection Camera";

    public AntiAliasing MockMSAALevel = AntiAliasing.MSAA_x8;
    public bool MoveMirrorCamera = true;
    public bool useVRAMOptimization = true;
    public bool m_DisablePixelLights = true;
    [Range(0.01f, 1f)]
    public float MaxTextureSizePercent = 1.0f;  // this should be a setting in config
    public LayerMask m_ReflectLayers = -1;
    public bool useAverageNormals = true;
    public bool useMask;
    public bool useMesh;
    public bool useFrustum;
    [Unity.XR.CoreUtils.ReadOnly] public bool useMsaaTexture;
    [Unity.XR.CoreUtils.ReadOnly] public bool didRender;

    private bool hasCorners;
    private int actualMsaa;
    private int width;
    private int height;
    private Camera m_ReflectionCamera;
    private CommandBuffer commandBufferClearRenderTargetToZero;
    private CommandBuffer commandBufferClearRenderTargetToOne;
    private CommandBuffer commandBufferDrawMesh;
    private CommandBuffer commandBufferSetRectFull;
    private CommandBuffer commandBufferSetRectLimited;
    private Material[] m_MaterialsInstanced;
    private RenderTexture m_ReflectionTextureMSAA;
    private RenderTexture m_ReflectionTextureLeft;
    private RenderTexture m_ReflectionTextureRight;
    private Renderer m_Renderer;
    private Mesh m_Mesh;
    private Vector3 mirrorNormal = Vector3.zero;
    private Vector3 mirrorNormalAvg = Vector3.zero;
    private Vector3x4 meshCorners;
    private Matrix4x4 m_LocalToWorldMatrix;

#if UNITY_EDITOR
    private readonly List<Material> m_savedSharedMaterials = new List<Material>();   // Only relevant for the editor
#endif

    private static int LeftEyeTextureID = -1;
    private static int RightEyeTextureID = -1;
    private static bool s_InsideRendering = false;
    private static Material m_DepthMaterial;
    private static readonly Dictionary<Camera, Skybox> SkyboxDict = new Dictionary<Camera, Skybox>();
    private static readonly List<Material> m_tempSharedMaterials = new List<Material>();
    private static readonly List<Vector3> vertices = new List<Vector3>();
    private static readonly List<Vector3> mirrorVertices = new List<Vector3>();
    private static readonly List<int> indicies = new List<int>();
    private static readonly HashSet<int> ints = new HashSet<int>();
    private static readonly Matrix4x4 m_InversionMatrix = new Matrix4x4 { m00 = -1, m11 = 1, m22 = 1, m33 = 1 };
    private static readonly Vector3[] outCorners = new Vector3[4];
    private static readonly List<Vector3> allCorners = new List<Vector3>(10);
    private static readonly Plane[] planes = new Plane[6];

    public enum AntiAliasing
    {
        Default = 0,
        MSAA_Off = 1,
        MSAA_x2 = 2,
        MSAA_x4 = 4,
        MSAA_x8 = 8,
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
        // Move mirror to water layer
        gameObject.layer = 4;


        if (!m_DepthMaterial)
        {
            CreateMirrorMaskMaterial();
        }

        m_Renderer = GetComponent<Renderer>();
        if (!m_Renderer)
        {
            enabled = false;
            return;
        }
        else
        {
            if (m_Renderer is SkinnedMeshRenderer smr)
            {
                m_Mesh = smr.sharedMesh; // don't
            }
            else if (m_Renderer.TryGetComponent<MeshFilter>(out var filter))
            {
                m_Mesh = filter.sharedMesh;
            }
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
            if (m_Mesh.isReadable)
            {
                ReadMesh(index);
            }
            else
            {
                mirrorNormal = mirrorNormalAvg = -transform.forward;
            }
            FindMeshCorners();

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

    private void FindMeshCorners()
    {
        (var min, var max) = GetMinMax(mirrorVertices);
        mirrorVertices.Clear();
        var plane = new Plane(mirrorNormalAvg, Vector3.zero);
        var rot = Quaternion.LookRotation(mirrorNormalAvg, Vector3.up);
        var trs = Matrix4x4.TRS(Vector3.zero, rot, Vector3.one);

        vertices.Add(plane.ClosestPointOnPlane(new Vector3(min.x, min.y, min.z)));
        vertices.Add(plane.ClosestPointOnPlane(new Vector3(min.x, min.y, max.z)));
        vertices.Add(plane.ClosestPointOnPlane(new Vector3(min.x, max.y, min.z)));
        vertices.Add(plane.ClosestPointOnPlane(new Vector3(min.x, max.y, max.z)));
        vertices.Add(plane.ClosestPointOnPlane(new Vector3(max.x, min.y, min.z)));
        vertices.Add(plane.ClosestPointOnPlane(new Vector3(max.x, min.y, max.z)));
        vertices.Add(plane.ClosestPointOnPlane(new Vector3(max.x, max.y, min.z)));
        vertices.Add(plane.ClosestPointOnPlane(new Vector3(max.x, max.y, max.z)));

        for (int i = 0; i < vertices.Count; i++)
        {
            // make them face forward on the Z axis
            vertices[i] = trs.MultiplyPoint3x4(vertices[i]);
        }
        (min, max) = GetMinMax(vertices);
        min.z = max.z = min.z + max.z / 2;

        // the four corners of the mesh along it's plane in local space, 

        var meshC0 = plane.ClosestPointOnPlane(trs.inverse.MultiplyPoint3x4(min));
        var meshC1 = plane.ClosestPointOnPlane(trs.inverse.MultiplyPoint3x4(new Vector3(min.x, max.y, min.z)));
        var meshC2 = plane.ClosestPointOnPlane(trs.inverse.MultiplyPoint3x4(max));
        var meshC3 = plane.ClosestPointOnPlane(trs.inverse.MultiplyPoint3x4(new Vector3(max.x, min.y, min.z)));
        meshCorners = new Vector3x4 { c0 = meshC0, c1 = meshC1, c2 = meshC2, c3 = meshC3 };
        hasCorners = true;
    }

    private void ReadMesh(int index)
    {
        Vector3 allNormals = Vector3.zero;
        if (m_Mesh.subMeshCount > 1 && index >= 0 && index < m_Mesh.subMeshCount)
        {
            ints.Clear();
            var subMesh = m_Mesh.GetSubMesh(index);
            m_Mesh.GetIndices(indicies, index);
            for (int i = subMesh.indexStart; i < subMesh.indexCount; i++)
            {
                ints.Add(indicies[i]);
            }
            m_Mesh.GetVertices(vertices);
            foreach (var item in ints)
            {
                mirrorVertices.Add(vertices[item]);
            }
            vertices.Clear();
            m_Mesh.GetNormals(vertices);
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
            m_Mesh.GetVertices(mirrorVertices);
            m_Mesh.GetNormals(vertices);
            foreach (var item in vertices)
            {
                allNormals += item;
            }
            // Get the mirror mesh normal from the first vertex
            mirrorNormal = vertices[0];
            vertices.Clear();
        }

        mirrorNormalAvg = allNormals.normalized;
        if (!float.IsFinite(mirrorNormalAvg.x) || !float.IsFinite(mirrorNormalAvg.y) || !float.IsFinite(mirrorNormalAvg.z))
        {
            mirrorNormalAvg = mirrorNormal;
        }
    }

    private (Vector3 min, Vector3 max) GetMinMax(List<Vector3> list)
    {
        int count = list.Count;
        Vector3 min = Vector3.one * float.PositiveInfinity;
        Vector3 max = Vector3.one * float.NegativeInfinity;
        for (int i = 0; i < count; i++)
        {
            var v = list[i];
            if (v.x < min.x) min.x = v.x;
            if (v.y < min.y) min.y = v.y;
            if (v.z < min.z) min.z = v.z;
            if (v.x > max.x) max.x = v.x;
            if (v.y > max.y) max.y = v.y;
            if (v.z > max.z) max.z = v.z;
        }
        return (min, max);
    }

    private void CreateMirrorMaskMaterial()
    {
        var shader = Shader.Find(MirrorDepthShaderName);
        if (shader)
        {
            m_DepthMaterial = new Material(shader);
        }
    }

    private void CreateCommandBuffer(ref CommandBuffer _commandBuffer, string name)
    {
        if (_commandBuffer == null)
        {
            _commandBuffer = new CommandBuffer();
            _commandBuffer.name = name;
        }
        else
        {
            _commandBuffer.Clear();
        }
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
        m_Renderer.GetSharedMaterials(m_tempSharedMaterials);
        if (m_tempSharedMaterials.Count == 0)
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
        actualMsaa = (int)MockMSAALevel;    //	pretend we have a config setting for this somewhere
        if (actualMsaa == 0)
        {
            // Get actual MSAA level from render texture (if the current camera renders to one) or QualitySettings
            RenderTexture targetTexture = currentCam.targetTexture;
            actualMsaa = !(targetTexture != null) ? (QualitySettings.antiAliasing == 0 ? 1 : QualitySettings.antiAliasing) : targetTexture.antiAliasing;
        }
        // Maximize the mirror texture resolution
        var res = UpdateRenderResolution(currentCam.pixelWidth, currentCam.pixelHeight);
        width = res.x;
        height = res.y;
        var currentCamLtwm = currentCam.transform.localToWorldMatrix;

        useMsaaTexture = useVRAMOptimization && currentCam.actualRenderingPath == RenderingPath.Forward && actualMsaa > 1;
        if (useMsaaTexture)
            SetupRenderTexture(ref m_ReflectionTextureMSAA, width, height, true, actualMsaa, currentCam.allowHDR);
        else if (m_ReflectionTextureMSAA)
        {
            RenderTexture.ReleaseTemporary(m_ReflectionTextureMSAA);
            m_ReflectionTextureMSAA = null;
        }

        // Set flag that we're rendering to a mirror
        s_InsideRendering = true;

        // Optionally disable pixel lights for reflection
        int oldPixelLightCount = QualitySettings.pixelLightCount;

        m_LocalToWorldMatrix = transform.localToWorldMatrix;
        Vector3 mirrorPos = m_LocalToWorldMatrix.GetColumn(3);  // transform.position
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
                    RenderCamera(currentCam, currentCamLtwm, mirrorPos, normal, Camera.MonoOrStereoscopicEye.Left, ref m_ReflectionTextureLeft);
                if (renderRight)
                    RenderCamera(currentCam, currentCamLtwm, mirrorPos, normal, Camera.MonoOrStereoscopicEye.Right, ref m_ReflectionTextureRight);
            }
            else
            {
                RenderCamera(currentCam, currentCamLtwm, mirrorPos, normal, Camera.MonoOrStereoscopicEye.Mono, ref m_ReflectionTextureLeft);
            }
        }
        finally
        {
            s_InsideRendering = false;
            if (m_DisablePixelLights)
                QualitySettings.pixelLightCount = oldPixelLightCount;
            if (useMsaaTexture || m_ReflectionTextureMSAA != null)
            {
                try
                {
                    RenderTexture.ReleaseTemporary(m_ReflectionTextureMSAA);
                }
                catch (NullReferenceException)
                { }
            }
        }
    }

    private Vector2Int UpdateRenderResolution(int width, int height)
    {
        var max = Mathf.Clamp01(MaxTextureSizePercent);
        return new Vector2Int((int)(width * max + 0.5f), (int)(height * max + 0.5f));
    }
    private static void SetupRenderTexture(ref RenderTexture rt, int width, int height, bool depth, int msaa, bool allowHDR)
    {
        if (rt)
            RenderTexture.ReleaseTemporary(rt);
        rt = RenderTexture.GetTemporary(width, height, depth ? 24 : 0,
            allowHDR ? RenderTextureFormat.ARGBHalf : RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default, msaa,
            RenderTextureMemoryless.None, VRTextureUsage.None);
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
    private void RenderCamera(Camera currentCam, in Matrix4x4 localToWorldMatrix, in Vector3 mirrorPos, in Vector3 normal, Camera.MonoOrStereoscopicEye eye, ref RenderTexture reflectionTexture)
    {
        bool isStereo = eye != Camera.MonoOrStereoscopicEye.Mono;
        if (!IsVisible(localToWorldMatrix, currentCam.stereoSeparation, eye, mirrorPos, normal, isStereo))
            return;

        CreateMirrorObjects(useMsaaTexture, ref reflectionTexture, currentCam.allowHDR);
        UpdateCameraModes(currentCam, m_ReflectionCamera);
        var targetTexture = useMsaaTexture ? m_ReflectionTextureMSAA : reflectionTexture;

        m_ReflectionCamera.useOcclusionCulling = false;
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
        if (MoveMirrorCamera)
        {
            var wtinv = worldToCameraMatrix.inverse;
            m_ReflectionCamera.transform.SetPositionAndRotation(wtinv.MultiplyPoint(Vector3.zero), wtinv.rotation);
        }
        // Setup oblique projection matrix so that near plane is our reflection plane.
        // This way we clip everything below/above it for free.
        Vector4 clipPlane = CameraSpacePlane(worldToCameraMatrix, mirrorPos, normal, 1.0f);

        var projectionMatrix = isStereo ? currentCam.GetStereoProjectionMatrix((Camera.StereoscopicEye)eye) : currentCam.projectionMatrix;
        m_ReflectionCamera.projectionMatrix = m_InversionMatrix * projectionMatrix * m_InversionMatrix;


        if (!TryGetRectPixel(m_ReflectionCamera, m_Renderer.localToWorldMatrix, out var frustum, out Rect bounds, out Rect pixelRect))
        {
            return;
        }
        bool willUseFrustum = false;
        bool validRect = false;

        if (hasCorners && pixelRect.height >= 1 && pixelRect.width >= 1 && (bounds.width < 0.95f || bounds.height < 0.95f))
        {
            validRect = true;
        }
        if (useFrustum && validRect)
        {
            m_ReflectionCamera.pixelRect = pixelRect;
            m_ReflectionCamera.projectionMatrix = frustum;
            willUseFrustum = true;
        }
        m_ReflectionCamera.projectionMatrix = m_ReflectionCamera.CalculateObliqueMatrix(clipPlane);
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
            CopyTexture(reflectionTexture);
        foreach (Material mat in m_tempSharedMaterials)
        {
            if (eye != Camera.MonoOrStereoscopicEye.Right)
            {
                if (mat.HasProperty(LeftEyeTextureID))
                    mat.SetTexture(LeftEyeTextureID, reflectionTexture);
            }
            else if (mat.HasProperty(RightEyeTextureID))
                mat.SetTexture(RightEyeTextureID, reflectionTexture);
        }
    }

    private void CopyTexture(RenderTexture reflectionTexture)
    {
        Graphics.CopyTexture(m_ReflectionTextureMSAA, reflectionTexture);
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
        CreateCommandBuffer(ref commandBufferClearRenderTargetToZero, "Occlusion buffer - ClearRenderTarget 0");
        CreateCommandBuffer(ref commandBufferClearRenderTargetToOne, "Occlusion buffer - ClearRenderTarget 1");
        CreateCommandBuffer(ref commandBufferDrawMesh, "Occlusion buffer - DrawMesh");
        CreateCommandBuffer(ref commandBufferSetRectFull, "Occlusion buffer - SetRect full");
        CreateCommandBuffer(ref commandBufferSetRectLimited, "Occlusion buffer - SetRect limited");

        if (!m_DepthMaterial)
            CreateMirrorMaskMaterial();

        if (!m_DepthMaterial)
            return;

        commandBufferClearRenderTargetToZero.ClearRenderTarget(clearDepth: true, clearColor: false, backgroundColor: Color.blue, depth: 0);
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
        if (src.clearFlags == CameraClearFlags.Skybox)
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
        cameraGameObject.hideFlags = HideFlags.DontSave;//HideAnd
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


    public bool TryGetRectPixel(Camera camera, Matrix4x4 ltwm, out Matrix4x4 frustum, out Rect bounds, out Rect pixelRect)
    {
        allCorners.Clear();
        var texture = camera.targetTexture;
        int textureWidth = texture.width;
        var textureHeight = texture.height;
        var corners = ToWorldCorners(ltwm);
        var screenCorners = GetScreenCorners(camera, corners);
        CheckCorners(screenCorners, camera, corners);

        float left = float.MaxValue;
        float right = float.MinValue;
        float top = float.MinValue;
        float bottom = float.MaxValue;
        float minZl = float.MaxValue;
        float minZr = float.MaxValue;
        float minZt = float.MaxValue;
        float minZb = float.MaxValue;

        for (int i = 0; i < allCorners.Count; i++)
        {
            var current = allCorners[i];
            if (right < current.x && current.z > 0.001f) { right = current.x; if (current.z < 0) minZl = current.z; }
            if (left > current.x && current.z > 0.001f) { left = current.x; if (current.z < 0) minZr = current.z; }
            if (top < current.y && current.z > 0.001f) { top = current.y; if (current.z < 0) minZt = current.z; }
            if (bottom > current.y && current.z > 0.001f) { bottom = current.y; if (current.z < 0) minZb = current.z; }
        }
        if (minZl <= 0 || minZr <= 0 || minZt <= 0 || minZb <= 0)
        {
            frustum = Matrix4x4.zero;
            bounds = new Rect();
            pixelRect = new Rect();
            return false;
        }
        var leftClamped = Mathf.Clamp01(left);
        var rightClamped = Mathf.Clamp01(right);
        var topClamped = Mathf.Clamp01(top);
        var bottomClamped = Mathf.Clamp01(bottom);

        var width = rightClamped - leftClamped;
        var height = topClamped - bottomClamped;

        var relevant = RectIsRelevant(left, right, top, bottom) && !(width > 0.95f && height > 0.95f);
        if (!relevant)
        {
            frustum = camera.projectionMatrix;
            bounds = new Rect(0, 0, 1, 1);
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


        bounds = new Rect(leftClamped, bottomClamped, width, height);

        right = float.MinValue;
        left = float.MaxValue;
        top = float.MinValue;
        bottom = float.MaxValue;
        float minZ = float.MaxValue;

        camera.CalculateFrustumCorners(bounds, 0.1f, Camera.MonoOrStereoscopicEye.Mono, outCorners);
        for (int i = 0; i < outCorners.Length; i++)
        {
            var current = outCorners[i];
            if (right < current.x) { right = current.x; }
            if (left > current.x) { left = current.x; }
            if (top < current.y) { top = current.y; }
            if (bottom > current.y) { bottom = current.y; }
            if (minZ > current.z) minZ = current.z;
        }

        frustum = Matrix4x4.Frustum(left, right, bottom, top, minZ, camera.farClipPlane);
        return true;
    }
    /// <summary>
    /// Check if any point has a negative depth, and generate new points from the intersection of camera frustum planes and the edges of the mirror
    /// </summary>
    private void CheckCorners(Vector3x4 screenCorners, Camera camera, Vector3x4 corners)
    {
        Vector3 I;
        var center = camera.worldToCameraMatrix.inverse.MultiplyPoint3x4(Vector3.zero);
        GeometryUtility.CalculateFrustumPlanes(camera, planes);

        for (int i = 0; i < screenCorners.Length; i++)
        {
            if (screenCorners[i].z < 0f)
            {
                // the last two planes are the near and far clip plane, no need to check those.
                for (int j = 0; j < 4; j++)
                {
                    if (SegmentPlane(corners[i], corners[i + 1], center, planes[j].normal, out I))
                    {
                        var point = camera.WorldToViewportPoint(I);
                        if (point.z > 0.001f)
                        {
                            allCorners.Add(point);
                        }
                    }
                    else if (SegmentPlane(corners[i], corners[i - 1], center, planes[j].normal, out I))
                    {
                        var point = camera.WorldToViewportPoint(I);
                        if (point.z > 0.001f)
                        {
                            allCorners.Add(point);
                        }
                    }
                }
            }
        }
    }
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
        I = Vector3.zero;

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
                return false;                   // no intersection
            }
        }

        // they are not parallel
        // compute intersect param
        float sI = N / D;
        if (sI < 0 || sI > 1)
            return false;                       // no intersection

        I = p0 + sI * u;                        // compute segment intersect point
        return true;
    }
    /// <summary>
    /// Check if at least one dimension is between 0 and 1
    /// </summary>
    private bool RectIsRelevant(float left, float right, float top, float bottom)
    {
        return (left > 0 && left < 1) ||
               (right > 0 && right < 1) ||
               (top > 0 && top < 1) ||
               (bottom > 0 && bottom < 1);
    }

    private Vector3x4 ToWorldCorners(Matrix4x4 ltwm)
    {
        return new Vector3x4
        {
            c0 = ltwm.MultiplyPoint3x4(meshCorners.c0),
            c1 = ltwm.MultiplyPoint3x4(meshCorners.c1),
            c2 = ltwm.MultiplyPoint3x4(meshCorners.c2),
            c3 = ltwm.MultiplyPoint3x4(meshCorners.c3),
        };
    }
    private Vector3x4 GetScreenCorners(Camera camera, Vector3x4 corners)
    {
        var screenCorners = new Vector3x4();
        for (int i = 0; i < corners.Length; i++)
        {
            allCorners.Add(camera.WorldToViewportPoint(corners[i]));
            screenCorners[i] = camera.WorldToViewportPoint(corners[i]);
        }
        return screenCorners;
    }

    private struct Vector3x4
    {
        public Vector3 c0;
        public Vector3 c1;
        public Vector3 c2;
        public Vector3 c3;
        public readonly int Length => 4;

        public Vector3 this[int index]
        {
            get
            {
                switch (index)
                {
                    case 0: return c0;
                    case 1: return c1;
                    case 2: return c2;
                    case 3: return c3;
                    case 4: return c0;
                    case -1: return c3;
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
    }
}