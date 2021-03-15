using UnityEngine;
using System;
using System.Collections.Generic;
#if UNITY_EDITOR
using UnityEditor;
#endif

[ExecuteInEditMode] // Make mirror live-update even when not in play mode
public class MirrorReflection : MonoBehaviour
{
	public int MockMirrorMaxRes = 4096;
	public AntiAliasing MockMSAALevel = AntiAliasing.MSAA_x8;
	public bool MoveMirrorCam = false;
	public bool useVRAMOptimization = false;
	public bool m_DisablePixelLights = true;
	public int MaxTextureSize = 2048;
	public LayerMask m_ReflectLayers = -1;


	private int usedTextureSize = 4096;
	private Vector3 mirrorNormal = Vector3.zero;
	private Camera m_ReflectionCamera;
	private RenderTexture m_ReflectionTextureMSAA;
	private RenderTexture m_ReflectionTextureLeft;
	private RenderTexture m_ReflectionTextureRight;
	private int actualMsaa;
	private float stereoSep;
	private int width;
	private int height;
	private Renderer m_Renderer;
	private MeshFilter meshFilter;
	private bool useMsaaTexture;
	private const string m_leftEyeName = "_ReflectionTexLeft";
	private const string m_rightEyeName = "_ReflectionTexRight";
	private string m_reflectionTextureNameL;
	private string m_reflectionTextureNameR;
	private string m_ReflectionTextureMSAAName;
	private string m_ReflectionCameraName;
#if UNITY_EDITOR
	private Material[] m_sharedMaterials;   // Only relevant for the editor
#endif
	private static List<Material> s_MirrorMaterials;
	private static bool s_InsideRendering = false;

	private const int m_MaxUnoptimizedMSAALevelVr = (int)AntiAliasing.MSAA_Off;
	private const int m_MaxUnoptimizedMSAALevelDesktop = (int)AntiAliasing.MSAA_x2;
	private readonly Matrix4x4 m_InversionMatrix = new Matrix4x4 { m00 = -1, m11 = 1, m22 = 1, m33 = 1 };

private Transform cameraTransform;
	private Transform Leye;
	private Transform Leye2;
	private Transform Reye;
	private Transform Reye2;
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
			if (m_sharedMaterials != null && m_Renderer  != null && m_sharedMaterials.Length > 0)
				m_Renderer.sharedMaterials = m_sharedMaterials;
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

		m_Renderer = GetComponent<Renderer>();

		// Get the mirror mesh normal from the first vertex
		if (mirrorNormal == Vector3.zero)
		{
			mirrorNormal = Vector3.up;
			meshFilter = GetComponent<MeshFilter>();
			if (meshFilter != null)
			{
				Mesh mesh = meshFilter.sharedMesh;
				if (mesh != null && mesh.normals.Length != 0)
					mirrorNormal = mesh.normals[0];
			}
		}

		// Make sure the mirror's material is unique
		if (s_MirrorMaterials == null)
			s_MirrorMaterials = new List<Material>();

#if UNITY_EDITOR
		m_sharedMaterials = m_Renderer.sharedMaterials;
#endif
		Material[] sharedMaterials = m_Renderer.sharedMaterials;
		if (sharedMaterials == null || sharedMaterials.Length == 0) 
			return;
		for (int i = 0; i < sharedMaterials.Length; i++)
		{
			if (sharedMaterials[i].shader.name == "FX/MirrorReflectionBasic")
			{
				if (s_MirrorMaterials.Contains(sharedMaterials[i]))
				{
					sharedMaterials[i] = Instantiate(sharedMaterials[i]);
					s_MirrorMaterials.Add(sharedMaterials[i]);
				}
				else
				{
					s_MirrorMaterials.Add(sharedMaterials[i]);
				}
			}
		}
		m_Renderer.materials = sharedMaterials;

		updateRenderResolution();
		int instanceID = GetInstanceID();
		m_reflectionTextureNameL = "__MirrorReflectionLeft" + instanceID;
		m_reflectionTextureNameR = "__MirrorReflectionRight" + instanceID;
		m_ReflectionTextureMSAAName = "__MirrorReflectionMSAA" + instanceID;
		m_ReflectionCameraName = "MirrorReflection Camera id" + instanceID;
		// Use steamvr/oculus camera rig instead if available
		{
			string name = "MirrorCamera helper " + GetInstanceID();
			for (int i = 0; transform.childCount > i; i++)
			{
				if (transform.GetChild(i).name == name)
				{
					cameraTransform = transform.GetChild(i);
					break;
				}
			}
			if (cameraTransform == null)
			{
				GameObject cameraObject = new GameObject() { name = name, hideFlags = HideFlags.HideAndDontSave };
				cameraTransform = cameraObject.transform;
			}
			for (int i = 0; cameraTransform.childCount > 0; i++)
			{
				DestroyImmediate(cameraTransform.GetChild(i).gameObject);
			}
			GameObject cameraObjectL = new GameObject() { name = "L", hideFlags = HideFlags.HideAndDontSave };
			Leye = cameraObjectL.transform;
			Leye.SetParent(cameraTransform);
			GameObject cameraObjectR = new GameObject() { name = "R", hideFlags = HideFlags.HideAndDontSave };
			Reye = cameraObjectR.transform;
			Reye.SetParent(cameraTransform);
			GameObject cameraObjectL2 = new GameObject() { name = "L2", hideFlags = HideFlags.HideAndDontSave };
			Leye2 = cameraObjectL2.transform;
			Leye2.SetParent(cameraTransform);
			GameObject cameraObjectR2 = new GameObject() { name = "R2", hideFlags = HideFlags.HideAndDontSave };
			Reye2 = cameraObjectR2.transform;
			Reye2.SetParent(cameraTransform);
		}
	}
    private void updateRenderResolution()
	{
		usedTextureSize = Math.Min(MaxTextureSize, MockMirrorMaxRes);
	}
	// This is called when it's known that the object will be rendered by some
	// camera. We render reflections and do other updates here.
	// Because the script executes in edit mode, reflections for the scene view
	// camera will just work!
	public void OnWillRenderObject()
	{
		if (!enabled || !m_Renderer || !m_Renderer.sharedMaterial || !m_Renderer.enabled)
			return;
		Camera currentCam = Camera.current;
		// Safeguard from recursive reflections.        
		if (s_InsideRendering || !currentCam)
			return;
		// Rendering will happen.

		// Force mirrors to Water layer
		if (gameObject.layer != 4) 
			gameObject.layer = 4;
		updateRenderResolution();
		actualMsaa = (int)MockMSAALevel;
		if (actualMsaa == 0)
		{
			// Get actual MSAA level from render texture (if the current camera render to one) or QualitySettings
			RenderTexture targetTexture = currentCam.targetTexture;
			actualMsaa = !(targetTexture != null) ? (QualitySettings.antiAliasing == 0 ? 1 : QualitySettings.antiAliasing) : targetTexture.antiAliasing;
		}
		// Maximize the mirror texture resolution
		width = Math.Min(usedTextureSize, currentCam.pixelWidth);
		height = Math.Min(usedTextureSize, currentCam.pixelHeight);
		// Remove if steamvr/oculus camera rig is present and use that instead
		{
			cameraTransform.SetPositionAndRotation(currentCam.transform.position, currentCam.transform.rotation);
		}
		if (currentCam.stereoEnabled)
		{
			if (stereoSep != currentCam.stereoSeparation)
			{
				stereoSep = currentCam.stereoSeparation;
				Leye.localPosition = -Vector3.right * currentCam.stereoSeparation / 2;
				Reye.localPosition = Vector3.right * currentCam.stereoSeparation / 2;
				Leye2.localPosition = -Vector3.right * currentCam.stereoSeparation;
				Reye2.localPosition = Vector3.right * currentCam.stereoSeparation;
			}
			
		}
		useMsaaTexture = useVRAMOptimization && ((!currentCam.stereoEnabled && (actualMsaa > m_MaxUnoptimizedMSAALevelDesktop)) || (currentCam.stereoEnabled && (actualMsaa > m_MaxUnoptimizedMSAALevelVr)));
		if (useMsaaTexture)
			SetupMSAAtexture();
		else if (m_ReflectionTextureMSAA)
			m_ReflectionTextureMSAA = null;
		// Set flag that we're rendering to a mirror
		s_InsideRendering = true;
		// Optionally disable pixel lights for reflection
		int oldPixelLightCount = QualitySettings.pixelLightCount;
		if (m_DisablePixelLights)
			QualitySettings.pixelLightCount = 0;
		try
		{
			RenderCamera(currentCam, m_Renderer, Camera.StereoscopicEye.Left, ref m_ReflectionTextureLeft);
			if (!currentCam.stereoEnabled)
				return;
			RenderCamera(currentCam, m_Renderer, Camera.StereoscopicEye.Right, ref m_ReflectionTextureRight);
			
		}
		finally
		{
			s_InsideRendering = false;
			if (m_ReflectionTextureMSAA)
				RenderTexture.ReleaseTemporary(m_ReflectionTextureMSAA);
			if (m_DisablePixelLights)
				QualitySettings.pixelLightCount = oldPixelLightCount;
		}
	}

	private void SetupMSAAtexture()
	{
		if (m_ReflectionTextureMSAA)
			RenderTexture.ReleaseTemporary(m_ReflectionTextureMSAA);
		m_ReflectionTextureMSAA = RenderTexture.GetTemporary(width, height, 24,RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Default, actualMsaa, RenderTextureMemoryless.None, VRTextureUsage.None);
		m_ReflectionTextureMSAA.name = m_ReflectionTextureMSAAName;
	}
	private bool Visible(Vector3 position)
	{
		return Vector3.Dot(position - transform.position, transform.TransformDirection(mirrorNormal)) > 0;
	}
	private void RenderCamera(Camera currentCam, Renderer rend, Camera.StereoscopicEye eye, ref RenderTexture ReflectionTexture)
    {
		if (!currentCam.stereoEnabled)
		{
			if (!Visible(cameraTransform.position))
				return;
		}
		// Replace with proper eye positions if possible
		else if (!(Visible(eye == Camera.StereoscopicEye.Left ? Leye.position : Reye.position) || Visible(eye == Camera.StereoscopicEye.Left ? Leye2.position : Reye2.position)))
			return;
		CreateMirrorObjects(eye, ref ReflectionTexture);

		// find out the reflection plane: position and normal in world space
		Vector3 pos = transform.position;
		Vector3 normal = transform.TransformDirection(mirrorNormal);
		UpdateCameraModes(currentCam, m_ReflectionCamera);

		m_ReflectionCamera.useOcclusionCulling = false;
		m_ReflectionCamera.depthTextureMode = currentCam.depthTextureMode | DepthTextureMode.Depth;
		m_ReflectionCamera.stereoTargetEye = StereoTargetEyeMask.None;
		//mirrors never render the water layer, as they are on the water layer
		m_ReflectionCamera.cullingMask = -17 & m_ReflectLayers.value;

		if (MoveMirrorCam)
		{
			Vector3 camPosition = currentCam.transform.position;
			m_ReflectionCamera.transform.position = m_ReflectionCamera.projectionMatrix.MultiplyPoint(camPosition);
			Vector3 eulerAngles = currentCam.transform.eulerAngles;
			m_ReflectionCamera.transform.eulerAngles = new Vector3(0.0f, eulerAngles.y, eulerAngles.z);
			//m_ReflectionCamera.transform.position = camPosition;
		}

		// Render reflection
		// Reflect camera around reflection plane
		float d = -Vector3.Dot(normal, pos);
		Vector4 reflectionPlane = new Vector4(normal.x, normal.y, normal.z, d);
		Matrix4x4 worldToCameraMatrix = Matrix4x4.zero;
		CalculateReflectionMatrix(ref worldToCameraMatrix, reflectionPlane);
		worldToCameraMatrix = m_InversionMatrix * (!currentCam.stereoEnabled ? currentCam.worldToCameraMatrix * worldToCameraMatrix : currentCam.GetStereoViewMatrix(eye) * worldToCameraMatrix);
		m_ReflectionCamera.worldToCameraMatrix = worldToCameraMatrix;
		// Setup oblique projection matrix so that near plane is our reflection
		// plane. This way we clip everything below/above it for free.
		Vector4 clipPlane = CameraSpacePlane(worldToCameraMatrix, pos, normal, 1.0f);
		m_ReflectionCamera.projectionMatrix = m_InversionMatrix * (currentCam.stereoEnabled ? currentCam.GetStereoProjectionMatrix(eye) : currentCam.projectionMatrix) * m_InversionMatrix;
		m_ReflectionCamera.projectionMatrix = m_ReflectionCamera.CalculateObliqueMatrix(clipPlane);
		
		if (useMsaaTexture)
		{
			m_ReflectionCamera.targetTexture = m_ReflectionTextureMSAA;
		}
		else
		{
			m_ReflectionCamera.targetTexture = ReflectionTexture;
		}
		m_ReflectionCamera.Render();
		Material[] materials = rend.sharedMaterials;
        if (useMsaaTexture)
			Graphics.CopyTexture(m_ReflectionTextureMSAA, 0, 0, 0, 0, width, height, ReflectionTexture, 0, 0, 0, 0);
		foreach (Material mat in materials)
		{
			if (eye == Camera.StereoscopicEye.Left)
			{
				if (mat.HasProperty(m_leftEyeName))
					mat.SetTexture(m_leftEyeName, ReflectionTexture);
			}
			else if (mat.HasProperty(m_rightEyeName))
				mat.SetTexture(m_rightEyeName, ReflectionTexture);
		}
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
    private void OnDestroy()
    {
		if (s_MirrorMaterials != null)
		{
			Material[] sharedMaterials = m_Renderer.sharedMaterials;
			for (int i = 0; i < sharedMaterials.Length; i++)
			{
				if (sharedMaterials[i].shader.name == "FX/MirrorReflectionBasic")
				{
					s_MirrorMaterials.Remove(sharedMaterials[i]);
				}
			}
		}
	}
    private void UpdateCameraModes(Camera src, Camera dest)
	{
		if (dest == null)
			return;
		// set camera to clear the same way as current camera
		dest.CopyFrom(src);
		if (src.clearFlags == CameraClearFlags.Skybox)
		{
			Skybox sky = src.GetComponent(typeof(Skybox)) as Skybox;
			Skybox mysky = dest.GetComponent(typeof(Skybox)) as Skybox;
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

	// On-demand create any objects we need
	private void CreateMirrorObjects(Camera.StereoscopicEye eye, ref RenderTexture reflectionTexture) //Camera currentCamera, 
	{
		//width = Math.Min(usedTextureSize, currentCamera.pixelWidth);
		//height = Math.Min(usedTextureSize, currentCamera.pixelHeight);


		// Reflection render texture
		int msaa = useMsaaTexture ? 1 : actualMsaa;
		if (reflectionTexture)
			RenderTexture.ReleaseTemporary(reflectionTexture);
		reflectionTexture = RenderTexture.GetTemporary(width, height, 24,
			RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Default, msaa,
			RenderTextureMemoryless.None, VRTextureUsage.None);
		reflectionTexture.name = eye == 0 ? m_reflectionTextureNameL : m_reflectionTextureNameR;
		if (m_ReflectionCamera != null)
			return;	// Camera already exists

		// Create Camera for reflection

		GameObject gameObject = new GameObject(m_ReflectionCameraName, typeof(Camera), typeof(Skybox));
		gameObject.transform.SetParent(transform);
		m_ReflectionCamera = gameObject.GetComponent<Camera>();
		m_ReflectionCamera.enabled = false;
		m_ReflectionCamera.gameObject.AddComponent<FlareLayer>();
		gameObject.hideFlags = HideFlags.HideAndDontSave;
	}

	// Given position/normal of the plane, calculates plane in camera space.
	private Vector4 CameraSpacePlane(Camera cam, Vector3 pos, Vector3 normal, float sideSign)
	{
		Vector3 offsetPos = pos + normal;
		Matrix4x4 m = cam.worldToCameraMatrix;
		Vector3 cpos = m.MultiplyPoint(offsetPos);
		Vector3 cnormal = m.MultiplyVector(normal).normalized * sideSign;
		return new Vector4(cnormal.x, cnormal.y, cnormal.z, -Vector3.Dot(cpos, cnormal));
	}
	private Vector4 CameraSpacePlane(Matrix4x4 worldToCameraMatrix, Vector3 pos, Vector3 normal, float sideSign)
	{
		//Vector3 point = pos + normal * 0f; //offset
		Vector3 lhs = worldToCameraMatrix.MultiplyPoint(pos);
		Vector3 rhs = worldToCameraMatrix.MultiplyVector(normal).normalized * sideSign;
		return new Vector4(rhs.x, rhs.y, rhs.z, -Vector3.Dot(lhs, rhs));
	}

	// Calculates reflection matrix around the given plane
	private static void CalculateReflectionMatrix(ref Matrix4x4 reflectionMat, Vector4 plane)
	{
		reflectionMat.m00 = (1F - 2F * plane[0] * plane[0]);
		reflectionMat.m01 = (-2F * plane[0] * plane[1]);
		reflectionMat.m02 = (-2F * plane[0] * plane[2]);
		reflectionMat.m03 = (-2F * plane[3] * plane[0]);

		reflectionMat.m10 = (-2F * plane[1] * plane[0]);
		reflectionMat.m11 = (1F - 2F * plane[1] * plane[1]);
		reflectionMat.m12 = (-2F * plane[1] * plane[2]);
		reflectionMat.m13 = (-2F * plane[3] * plane[1]);

		reflectionMat.m20 = (-2F * plane[2] * plane[0]);
		reflectionMat.m21 = (-2F * plane[2] * plane[1]);
		reflectionMat.m22 = (1F - 2F * plane[2] * plane[2]);
		reflectionMat.m23 = (-2F * plane[3] * plane[2]);

		reflectionMat.m30 = 0F;
		reflectionMat.m31 = 0F;
		reflectionMat.m32 = 0F;
		reflectionMat.m33 = 1F;
	}
}