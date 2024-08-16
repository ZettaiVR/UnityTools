using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR;
using static MirrorReflection;

/// <summary>
/// 3D, VR safe, visual portal effect
/// </summary>
[ExecuteInEditMode]
public class PortalReflection : MonoBehaviour
{
    public AntiAliasing MockMSAALevel = AntiAliasing.MSAA_x4;
    public int resolutionLimit = 4096;
    [Range(0.01f, 1f)]
    public float MaxTextureSizePercent = 1.0f;  // this should be a setting in config
    public LayerMask m_ReflectLayers = -1;
    public bool useVRAMOptimization = true;
    public bool m_DisablePixelLights = true;
    public bool useFrustum = true;
    public bool useOcclusionCulling = true;
    public bool keepNearClip = true;
    public bool disableOcclusionWhenTransparent = false;
    public bool copyStreamingController = true;
    public ClearFlags clearFlags = ClearFlags.Default;
    public Color backgroundColor = Color.clear;
    public Transform portalTarget;
    public CullDistance cullDistance = CullDistance.Default;
    public float maxCullDistance = 1000f;
    public float[] maxCullDistances = new float[32];

    private bool hasCorners;
    private bool useMsaaTexture;
    private Mesh m_Mesh;
    private Camera m_ReflectionCamera;
    private Camera m_CullingCamera;
    private Bounds boundsLocalSpace;
    private Renderer m_Renderer;
    private Vector3 portalNormalAvg = Vector3.up;
    private Vector3 portalPlaneOffset = Vector3.zero;
    private Vector3 meshMid;
    private Matrix4x4 meshTrs;
    private Vector3x4 meshCorners;
    private Transform scaleOffset;
    private Material[] m_MaterialsInstanced;
    private RenderTexture m_ReflectionTextureMSAA;
    private RenderTexture m_ReflectionTextureLeft;
    private RenderTexture m_ReflectionTextureRight;
    private MaterialPropertyBlock materialPropertyBlock;

    private const int PortalLayer = 4;
    private const int PortalLayerExcludeMask = ~(1 << PortalLayer);
    private static bool s_InsideRendering = false;
    private readonly List<Material> m_savedSharedMaterials = new List<Material>();   // Only relevant for the editor
    private static readonly Quaternion rotatePortal = Quaternion.Euler(new Vector3(90, 180, 0));
    private static readonly Matrix4x4 rotatePortalTrs = Matrix4x4.TRS(Vector3.zero, rotatePortal, Vector3.one);

    private void Start()
    {
        copySupported = SystemInfo.copyTextureSupport.HasFlag(CopyTextureSupport.Basic);

        // Move portal to water layer
        gameObject.layer = PortalLayer;
        materialPropertyBlock = new MaterialPropertyBlock();
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
        SetGlobalIdIfNeeded();
        if (m_Mesh)
        {
            // find the first material that has mirror shader properties
            m_Renderer.GetSharedMaterials(m_savedSharedMaterials);
            m_tempSharedMaterials.Clear();
            m_tempSharedMaterials.AddRange(m_savedSharedMaterials);
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
                (meshTrs, meshMid, meshCorners, boundsLocalSpace, hasCorners) = FindMeshCorners(portalNormalAvg);
            }
            catch (Exception) { }
            if (!mesh.isReadable)
            {
                portalNormalAvg = Vector3.up;
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

    private void OnDestroy()
    {
        if (m_MaterialsInstanced == null || m_MaterialsInstanced.Length == 0)
            return;
        for (int i = 0; i < m_MaterialsInstanced.Length; i++)
        {
            DestroyImmediate(m_MaterialsInstanced[i]);
        }
    }

    public void OnWillRenderObject()
    {
        if (!enabled || !m_Renderer || !m_Renderer.enabled || !portalTarget || s_InsideRendering)
            return;

        Camera currentCam = Camera.current;
        bool isStereo = currentCam.stereoEnabled;

        // Safeguard from recursive reflections.        
        if (!currentCam)
            return;

#if UNITY_EDITOR
        SetGlobalIdIfNeeded();
        if (materialPropertyBlock == null)
            materialPropertyBlock = new MaterialPropertyBlock();
#endif

        if (currentCam.orthographic)
        {
            materialPropertyBlock.SetTexture(LeftEyeTextureID, cullingTex);
            materialPropertyBlock.SetTexture(RightEyeTextureID, cullingTex);
            m_Renderer.SetPropertyBlock(materialPropertyBlock);
            return;
        }
        // Force portal to Water layer
        if (gameObject.layer != PortalLayer)
            gameObject.layer = PortalLayer;

        // Maximize texture resolution
        var res = UpdateRenderResolution(currentCam.pixelWidth, currentCam.pixelHeight, MaxTextureSizePercent, resolutionLimit);
        var widthHeightMsaa = new Vector3Int(res.x, res.y, GetActualMSAA(currentCam, (int)MockMSAALevel));

        var currentCamLtwm = currentCam.transform.localToWorldMatrix;

        useMsaaTexture = useVRAMOptimization && currentCam.actualRenderingPath == RenderingPath.Forward && widthHeightMsaa.z > 1 && copySupported;

        // Set flag that we're rendering to a portal
        s_InsideRendering = true;

        // Optionally disable pixel lights for reflection
        int oldPixelLightCount = QualitySettings.pixelLightCount;

        var m_LocalToWorldMatrix = transform.localToWorldMatrix;
        var portalPos = m_LocalToWorldMatrix.MultiplyPoint3x4(portalPlaneOffset);
        var normal = m_LocalToWorldMatrix.MultiplyVector(portalNormalAvg).normalized;

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
                    RenderCamera(currentCam, currentCamLtwm, portalPos, normal, widthHeightMsaa, Camera.MonoOrStereoscopicEye.Left);
                if (renderRight)
                    RenderCamera(currentCam, currentCamLtwm, portalPos, normal, widthHeightMsaa, Camera.MonoOrStereoscopicEye.Right);
            }
            else
            {
                RenderCamera(currentCam, currentCamLtwm, portalPos, normal, widthHeightMsaa, Camera.MonoOrStereoscopicEye.Mono);
            }
            if (m_ReflectionTextureLeft)
                materialPropertyBlock.SetTexture(LeftEyeTextureID, m_ReflectionTextureLeft);
            if (m_ReflectionTextureRight)
                materialPropertyBlock.SetTexture(RightEyeTextureID, m_ReflectionTextureRight);
            m_Renderer.SetPropertyBlock(materialPropertyBlock);
            materialPropertyBlock.SetFloat(PortalModeID, 1);
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
                catch (NullReferenceException) { }
            }
#endif
        }
    }
    private void RenderCamera(Camera currentCam, in Matrix4x4 localToWorldMatrix, Vector3 portalPos, Vector3 normal, Vector3Int widthHeightMsaa, Camera.MonoOrStereoscopicEye eye)
    {
        bool isStereo = eye != Camera.MonoOrStereoscopicEye.Mono;
        bool isRightEye = eye == Camera.MonoOrStereoscopicEye.Right;
        if (!IsVisible(localToWorldMatrix, currentCam.stereoSeparation, eye, portalPos, normal, isStereo))
            return;

        SetupMsaaTexture(ref m_ReflectionTextureMSAA, useMsaaTexture, widthHeightMsaa, currentCam.allowHDR);

        var reflectionTexture = isRightEye ? m_ReflectionTextureRight : m_ReflectionTextureLeft;
        CreateMirrorObjects(transform, ref reflectionTexture, ref scaleOffset, ref m_CullingCamera, ref m_ReflectionCamera, currentCam.allowHDR, widthHeightMsaa, portalNormalAvg, useMsaaTexture);
        if (isRightEye)
            m_ReflectionTextureRight = reflectionTexture;
        else
            m_ReflectionTextureLeft = reflectionTexture;

        UpdateCameraModes(currentCam, m_ReflectionCamera, m_CullingCamera, clearFlags, backgroundColor, copyStreamingController);

        var targetTexture = useMsaaTexture ? m_ReflectionTextureMSAA : reflectionTexture;

        bool isTransparent = clearFlags == ClearFlags.Color && backgroundColor.a < 1f;
        bool useOcclusion = useOcclusionCulling && !(disableOcclusionWhenTransparent && isTransparent);

        m_ReflectionCamera.useOcclusionCulling = useOcclusion;
        m_ReflectionCamera.depthTextureMode = currentCam.depthTextureMode | DepthTextureMode.Depth;
        m_ReflectionCamera.stereoTargetEye = StereoTargetEyeMask.None;
        m_ReflectionCamera.targetTexture = targetTexture;
        m_ReflectionCamera.cullingMask = PortalLayerExcludeMask & m_ReflectLayers.value; // mirrors and portals never render their own layer.

        RenderPortal(currentCam, portalPos, reflectionTexture, useOcclusion, isStereo, eye);
    }
    private void RenderPortal(Camera currentCam, Vector3 portalPos, RenderTexture reflectionTexture, bool useOcclusion, bool isStereo, Camera.MonoOrStereoscopicEye eye)
    {
        var viewMatrix = isStereo ? currentCam.GetStereoViewMatrix((Camera.StereoscopicEye)eye) : currentCam.worldToCameraMatrix;
        var targetPos = portalTarget.localPosition;
        portalTarget.position -= portalPos;
        scaleOffset.transform.position -= portalPos;
        var viewMatrix0 = isStereo ? m_CullingCamera.GetStereoViewMatrix((Camera.StereoscopicEye)eye) : m_CullingCamera.worldToCameraMatrix;
        var portalSurfaceRot = Quaternion.FromToRotation(portalNormalAvg, Vector3.forward);
        var portalSurfaceLtwm = Matrix4x4.TRS(Vector3.zero, portalSurfaceRot, Vector3.one);
        var portalLtwm = portalTarget.localToWorldMatrix * rotatePortalTrs;
        var portalUp = portalLtwm.MultiplyVector(Vector3.up);
        var portalNewPos = (Vector3)portalLtwm.GetColumn(3);
        var portalCameraToWorldMatrix = portalLtwm * portalSurfaceLtwm.inverse * viewMatrix0.inverse;
        var portalLocalToWorld = portalLtwm * Matrix4x4.Scale(m_Renderer.localToWorldMatrix.lossyScale);
        portalLocalToWorld = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, Vector3.one).inverse * portalLocalToWorld;
        portalLocalToWorld *= meshTrs.inverse;
        var portalCorners = meshCorners.MultiplyPoint3x4(portalLocalToWorld);
        var rotationDiff = portalLtwm.rotation * Quaternion.Inverse(portalSurfaceRot);
        var portalCamPos = portalCameraToWorldMatrix.GetColumn(3);
        var portalCamRot = rotationDiff * currentCam.transform.rotation;
        m_ReflectionCamera.transform.SetPositionAndRotation(portalCamPos, portalCamRot);
        m_ReflectionCamera.ResetWorldToCameraMatrix();
        m_ReflectionCamera.projectionMatrix = isStereo ? currentCam.GetStereoProjectionMatrix((Camera.StereoscopicEye)eye) : currentCam.projectionMatrix;

        if (!TryGetRectPixel(m_ReflectionCamera, portalCorners, boundsLocalSpace, portalLocalToWorld, keepNearClip, out var portalFrustum, out float portalNearDistance, out Rect portalRect, out var portalSurfacePlane))
        {
            scaleOffset.localPosition = Vector3.zero;
            portalTarget.localPosition = targetPos;
            return;
        }

        bool _validRect = false;

        if (hasCorners && portalRect.height >= 1 && portalRect.width >= 1)
        {
            _validRect = true;
        }
        if (useFrustum && _validRect)
        {
            m_ReflectionCamera.pixelRect = portalRect;
            m_ReflectionCamera.projectionMatrix = portalFrustum;
            m_ReflectionCamera.nearClipPlane = portalNearDistance;
        }

        if (useOcclusion)
        {
            var cullingPos = m_ReflectionCamera.transform.position;
            ResetCullingCamera(m_CullingCamera, cullingPos);
            var planePortal = new Plane(portalUp, portalNewPos);
            m_CullingCamera.transform.LookAt(planePortal.ClosestPointOnPlane(cullingPos), portalUp);
            var farclip = m_CullingCamera.farClipPlane = currentCam.farClipPlane;
            useOcclusion = SetCullingCameraProjectionMatrix(m_CullingCamera, m_ReflectionCamera, portalCorners, portalSurfacePlane, portalLocalToWorld.MultiplyPoint3x4(meshMid), farclip);
        }

        scaleOffset.localPosition = Vector3.zero;
        portalTarget.localPosition = targetPos;
        portalSurfaceLtwm = Matrix4x4.TRS(portalPos, portalSurfaceRot, Vector3.one);
        portalLtwm = portalTarget.localToWorldMatrix * rotatePortalTrs;
        portalUp = portalLtwm.MultiplyVector(Vector3.up);
        portalNewPos = (Vector3)portalLtwm.GetColumn(3);
        portalCameraToWorldMatrix = portalLtwm * portalSurfaceLtwm.inverse * viewMatrix.inverse;
        rotationDiff = portalLtwm.rotation * Quaternion.Inverse(portalSurfaceRot);
        portalCamPos = portalCameraToWorldMatrix.GetColumn(3);
        portalCamRot = rotationDiff * currentCam.transform.rotation;
        m_ReflectionCamera.transform.SetPositionAndRotation(portalCamPos, portalCamRot);
        SetCullDistance(m_ReflectionCamera, cullDistance, maxCullDistance, maxCullDistances, portalCamPos);

        var portalClipPlane = CameraSpacePlane(m_ReflectionCamera.worldToCameraMatrix, portalNewPos, -portalUp);
        m_ReflectionCamera.projectionMatrix = m_ReflectionCamera.CalculateObliqueMatrix(portalClipPlane);
        m_ReflectionCamera.useOcclusionCulling = useOcclusion;
        if (useOcclusion)
        {
            m_ReflectionCamera.cullingMatrix = m_CullingCamera.cullingMatrix;
        }
        SetGlobalShaderRect(m_ReflectionCamera.rect);
        m_ReflectionCamera.Render();

        ResetGlobalShaderRect();
        if (useMsaaTexture)
            CopyTexture(reflectionTexture, m_ReflectionTextureMSAA, portalRect, _validRect);
    }
}