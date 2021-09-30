using UnityEngine;
using System;
using System.Collections.Generic;
#if UNITY_EDITOR
using UnityEditor;
#endif

// Mirror that works in VR. Multipass rendering, works with multipass and single pass stereo modes, doesn't work with single pass instanced

[ExecuteInEditMode] // Make mirror live-update even when not in play mode
public class MirrorReflection : MonoBehaviour
{
	private const string MirrorShaderName = "FX/MirrorReflection2";
	private const string LeftEyeTextureName = "_ReflectionTexLeft";
	private const string RightEyeTextureName = "_ReflectionTexRight";

	public int MockMirrorMaxRes = 4096;
	public AntiAliasing MockMSAALevel = AntiAliasing.MSAA_x8;
	public bool MoveMirrorCam = false;
	public bool useVRAMOptimization = false;
	public bool m_DisablePixelLights = true;
	public int MaxTextureSize = 2048;
	public LayerMask m_ReflectLayers = -1;
	public bool UseAverageNormals = false;

	private int usedTextureSize = 4096;
	private Vector3 mirrorNormal = Vector3.zero;
	private Vector3 mirrorNormalAvg;
	private Camera m_ReflectionCamera;
	private RenderTexture m_ReflectionTextureMSAA;
	private RenderTexture m_ReflectionTextureLeft;
	private RenderTexture m_ReflectionTextureRight;
	private int actualMsaa;
	private float stereoSep;
	private int width;
	private int height;
	private Renderer m_Renderer;
	private string m_ReflectionCameraName;
#if UNITY_EDITOR
	private readonly List<Material> m_savedSharedMaterials = new List<Material>();   // Only relevant for the editor
#endif
	private Material[] m_Materials;

	private static readonly List<Material> m_tempSharedMaterials = new List<Material>();
	private static bool s_InsideRendering = false;

	private const int m_MaxUnoptimizedMSAALevelVr = (int)AntiAliasing.MSAA_Off;
	private const int m_MaxUnoptimizedMSAALevelDesktop = (int)AntiAliasing.MSAA_x2;
    private static readonly Matrix4x4 m_InversionMatrix = new Matrix4x4 { m00 = -1, m11 = 1, m22 = 1, m33 = 1 };
	private static readonly List<Material> materials = new List<Material>();
	private static readonly Dictionary<Camera, Skybox> SkyboxDict = new Dictionary<Camera, Skybox>();

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

		m_Renderer = GetComponent<Renderer>();
		if (!m_Renderer)
		{
			enabled = false;
			return;
		}
		// Get the mirror mesh normal from the first vertex
		if (mirrorNormal == Vector3.zero)
		{
			mirrorNormal = Vector3.up;
			var meshFilter = GetComponent<MeshFilter>();
			if (meshFilter != null)
			{
				Mesh mesh = meshFilter.sharedMesh;
				if (mesh != null && mesh.normals.Length != 0)
				{
					mirrorNormal = mesh.normals[0];
					mirrorNormalAvg = GetObjectNormal(mesh);
					if (mirrorNormalAvg == Vector3.zero)
						mirrorNormalAvg = mirrorNormal;
				}
			}
		}

		// Make sure the mirror's material is unique

		if (Application.isEditor)
		{
#if UNITY_EDITOR
			// backup original material
			m_Renderer.GetSharedMaterials(m_savedSharedMaterials);
#endif
			m_tempSharedMaterials.Clear();
			m_tempSharedMaterials.AddRange(m_savedSharedMaterials);
			if (m_tempSharedMaterials.Count == 0)
				return;
			for (int i = 0; i < m_tempSharedMaterials.Count; i++)
			{
				if (m_tempSharedMaterials[i].shader.name == MirrorShaderName)
				{
					m_tempSharedMaterials[i] = Instantiate(m_tempSharedMaterials[i]);

				}
			}
			m_Renderer.sharedMaterials = m_tempSharedMaterials.ToArray();
		}
		else 
		{
			m_Materials = m_Renderer.materials;
		}

		UpdateRenderResolution();
		int instanceID = GetInstanceID();
		m_ReflectionCameraName = "MirrorReflection Camera id" + instanceID;

		// Use existing camera rig with L/R eyes instead if available

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
    private void OnDestroy()
    {
		if (m_Materials == null || m_Materials.Length == 0)
			return;
        for (int i = 0; i < m_Materials.Length; i++)
        {
			DestroyImmediate(m_Materials[i]);
		}
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
		UpdateRenderResolution();
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
		bool isStereo = currentCam.stereoEnabled;
		if (isStereo)
		{
			cameraTransform.SetPositionAndRotation(currentCam.transform.position, currentCam.transform.rotation);
			if (stereoSep != currentCam.stereoSeparation)
			{
				stereoSep = currentCam.stereoSeparation;
				Leye.localPosition = -Vector3.right * stereoSep / 2f;
				Reye.localPosition = Vector3.right * stereoSep / 2f;
				Leye2.localPosition = -Vector3.right * stereoSep;
				Reye2.localPosition = Vector3.right * stereoSep;
			}
		}
		bool useMsaaTexture = useVRAMOptimization && ((!isStereo && (actualMsaa > m_MaxUnoptimizedMSAALevelDesktop)) || (isStereo && (actualMsaa > m_MaxUnoptimizedMSAALevelVr)));
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
			RenderCamera(currentCam, m_Renderer, Camera.StereoscopicEye.Left, isStereo, useMsaaTexture, ref m_ReflectionTextureLeft);
			if (!isStereo)
				return;
			RenderCamera(currentCam, m_Renderer, Camera.StereoscopicEye.Right, isStereo, useMsaaTexture, ref m_ReflectionTextureRight);
			
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

	public static Vector3 GetObjectNormal(Mesh mesh)
	{
		List<Vector3> normals = new List<Vector3>();
		mesh.GetNormals(normals);
		Vector3 allNormals = Vector3.zero;
		for (int i = 0; i < normals.Count; i++)
		{
			allNormals += normals[i];
		}
		return (allNormals / normals.Count).normalized;
	}
	private void UpdateRenderResolution()
	{
		usedTextureSize = Math.Min(MaxTextureSize, MockMirrorMaxRes);
	}
	private void SetupMSAAtexture()
	{
		if (m_ReflectionTextureMSAA)
			RenderTexture.ReleaseTemporary(m_ReflectionTextureMSAA);
		m_ReflectionTextureMSAA = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Default, actualMsaa, RenderTextureMemoryless.None, VRTextureUsage.None);
	}
	private bool Visible(Vector3 viewPosition)
	{
		return Vector3.Dot(viewPosition - transform.position, transform.TransformDirection(mirrorNormal)) > 0;
	}
	public bool Visible(in Vector3 viewPosition, in Vector3 objectPosition, in Vector3 objectNormal)
	{
		return Vector3.Dot(viewPosition - objectPosition, objectNormal) > 0;
	}
	private void RenderCamera(Camera currentCam, Renderer rend, Camera.StereoscopicEye eye, bool isStereo, bool useMsaaTexture, ref RenderTexture ReflectionTexture)
    {
		Vector3 mirrorPos = transform.position;
		Vector3 normal = transform.TransformDirection(UseAverageNormals ? mirrorNormalAvg : mirrorNormal);
		if (!isStereo)
		{
			if (!Visible(currentCam.transform.position, mirrorPos, normal))
				return;
		}        // Replace with proper eye positions if possible
		else if (!(Visible(eye == Camera.StereoscopicEye.Left ? Leye.position : Reye.position, mirrorPos, normal) || Visible(eye == Camera.StereoscopicEye.Left ? Leye2.position : Reye2.position, mirrorPos, normal)))
			return;

		CreateMirrorObjects(eye, useMsaaTexture, ref ReflectionTexture);

		// find out the reflection plane: position and normal in world space
		
		UpdateCameraModes(currentCam, m_ReflectionCamera);

		m_ReflectionCamera.useOcclusionCulling = false;
		m_ReflectionCamera.depthTextureMode = currentCam.depthTextureMode | DepthTextureMode.Depth;
		m_ReflectionCamera.stereoTargetEye = StereoTargetEyeMask.None;
		//mirrors never render the water layer, as they are on the water layer
		m_ReflectionCamera.cullingMask = -17 & m_ReflectLayers.value;
		// Render reflection
		// Reflect camera around reflection plane
		float d = -Vector3.Dot(normal, mirrorPos);
		Vector4 reflectionPlane = new Vector4(normal.x, normal.y, normal.z, d);

		Matrix4x4 worldToCameraMatrix = CalculateReflectionMatrix(reflectionPlane);

		worldToCameraMatrix = m_InversionMatrix * (isStereo ? currentCam.GetStereoViewMatrix(eye) * worldToCameraMatrix : currentCam.worldToCameraMatrix * worldToCameraMatrix);
		m_ReflectionCamera.worldToCameraMatrix = worldToCameraMatrix;
		if (MoveMirrorCam)
		{
			m_ReflectionCamera.transform.position = worldToCameraMatrix.inverse.MultiplyPoint(Vector3.zero);
		}
		// Setup oblique projection matrix so that near plane is our reflection
		// plane. This way we clip everything below/above it for free.
		Vector4 clipPlane = CameraSpacePlane(worldToCameraMatrix, mirrorPos, normal, 1.0f);

		m_ReflectionCamera.projectionMatrix = m_InversionMatrix * (isStereo ? currentCam.GetStereoProjectionMatrix(eye) : currentCam.projectionMatrix) * m_InversionMatrix;
		m_ReflectionCamera.projectionMatrix = m_ReflectionCamera.CalculateObliqueMatrix(clipPlane);
	
		m_ReflectionCamera.targetTexture = useMsaaTexture ? m_ReflectionTextureMSAA : ReflectionTexture;
		m_ReflectionCamera.Render();
		rend.GetSharedMaterials(materials);
        if (useMsaaTexture)
			Graphics.CopyTexture(m_ReflectionTextureMSAA, 0, 0, 0, 0, width, height, ReflectionTexture, 0, 0, 0, 0);
		foreach (Material mat in materials)
		{
			if (eye == Camera.StereoscopicEye.Left)
			{
				if (mat.HasProperty(LeftEyeTextureName))
					mat.SetTexture(LeftEyeTextureName, ReflectionTexture);
			}
			else if (mat.HasProperty(RightEyeTextureName))
				mat.SetTexture(RightEyeTextureName, ReflectionTexture);
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
	private void CreateMirrorObjects(Camera.StereoscopicEye eye, bool useMsaaTexture, ref RenderTexture reflectionTexture) //Camera currentCamera, 
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
		if (m_ReflectionCamera != null)
			return;	// Camera already exists

		// Create Camera for reflection

		GameObject cameraGameObject = new GameObject(m_ReflectionCameraName);
		cameraGameObject.transform.SetParent(transform);
		cameraGameObject.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
		m_ReflectionCamera = cameraGameObject.AddComponent<Camera>();
		SkyboxDict[m_ReflectionCamera] = cameraGameObject.AddComponent<Skybox>();
		m_ReflectionCamera.enabled = false;
		m_ReflectionCamera.gameObject.AddComponent<FlareLayer>();
		cameraGameObject.hideFlags = HideFlags.HideAndDontSave;
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
	public Vector4 CameraSpacePlane(Matrix4x4 worldToCameraMatrix, Vector3 pos, Vector3 normal, float sideSign)
	{
		//Vector3 point = pos + normal * 0f; //offset
		Vector3 lhs = worldToCameraMatrix.MultiplyPoint(pos);
		Vector3 rhs = worldToCameraMatrix.MultiplyVector(normal).normalized * sideSign;
		return new Vector4(rhs.x, rhs.y, rhs.z, -Vector3.Dot(lhs, rhs));
	}

	// Calculates reflection matrix around the given plane
	public static Matrix4x4 CalculateReflectionMatrix(Vector4 plane)
	{
		float x = plane[0];
		float y = plane[1];
		float z = plane[2];
		float w = plane[3];	
	
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
}