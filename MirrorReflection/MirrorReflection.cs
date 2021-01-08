using UnityEngine;
using System.Collections;
using System;
//using Unity

// This is in fact just the Water script from Pro Standard Assets,
// just with refraction stuff removed.

[ExecuteInEditMode] // Make mirror live-update even when not in play mode
public class MirrorReflection : MonoBehaviour
{
	public int MockMirrorMaxRes = 4096;
	public int MockMSAALevel = 8;
	public bool MoveMirrorCam = false;

	public bool m_DisablePixelLights = true;
	public int m_TextureSize = 2048;
	private int usedTextureSize = 4096;

	public LayerMask m_ReflectLayers = -1;
	private Vector3 mirrorNormal = Vector3.zero;
	//private Hashtable m_ReflectionCameras = new Hashtable(); // Camera -> Camera table
	private Camera m_ReflectionCamera;
	private RenderTexture m_ReflectionTextureLeft;
	private RenderTexture m_ReflectionTextureRight;
	private int usedMsaa;
	private static bool s_InsideRendering = false; 
	private Transform cameraTransform;
	private Transform Leye;
	private Transform Leye2;
	private Transform Reye;
	private Transform Reye2;
	float stereoSep;

	private void Start()
	{
		updateRenderResolution();
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
			//cameraObject.transform.SetParent(transform);
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
	private void updateRenderResolution()
	{
		usedTextureSize = Math.Min(m_TextureSize, MockMirrorMaxRes);
		usedMsaa = MockMSAALevel;
	}
	// This is called when it's known that the object will be rendered by some
	// camera. We render reflections and do other updates here.
	// Because the script executes in edit mode, reflections for the scene view
	// camera will just work!
	public void OnWillRenderObject()
	{       

		if (mirrorNormal == Vector3.zero)
		{
			mirrorNormal = Vector3.up;
			MeshFilter component = GetComponent<MeshFilter>();
			if (component != null)
			{
				Mesh mesh = component.sharedMesh;
				if (mesh != null && mesh.normals.Length != 0)
					mirrorNormal = mesh.normals[0];
			}
		}
		Renderer rend = GetComponent<Renderer>();
		if (!enabled || !rend || !rend.sharedMaterial || !rend.enabled)
			return;

		Camera currentCam = Camera.current;
		if (!currentCam)
			return;
		// Safeguard from recursive reflections.        
		if (s_InsideRendering)
			return;
		cameraTransform.SetPositionAndRotation(currentCam.transform.position, currentCam.transform.rotation);
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
		s_InsideRendering = true;
		// Optionally disable pixel lights for reflection
		int oldPixelLightCount = QualitySettings.pixelLightCount;
		if (m_DisablePixelLights)
			QualitySettings.pixelLightCount = 0;
		try
		{
			RenderCamera(currentCam, rend, Camera.StereoscopicEye.Left, ref m_ReflectionTextureLeft);
			if (!currentCam.stereoEnabled)
				return;
			RenderCamera(currentCam, rend, Camera.StereoscopicEye.Right, ref m_ReflectionTextureRight);
		}
		finally
		{
			s_InsideRendering = false;
			if (m_DisablePixelLights)
				QualitySettings.pixelLightCount = oldPixelLightCount;
		}
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
		else if (!(Visible(eye == Camera.StereoscopicEye.Left? Leye.position : Reye.position) || (Visible(eye == Camera.StereoscopicEye.Left ? Leye2.position : Reye2.position))))
			return;
		CreateMirrorObjects(currentCam, eye, ref ReflectionTexture);

		// find out the reflection plane: position and normal in world space
		Vector3 pos = transform.position;
		Vector3 normal = transform.TransformDirection(mirrorNormal);
		UpdateCameraModes(currentCam, m_ReflectionCamera);

		m_ReflectionCamera.useOcclusionCulling = false;
		m_ReflectionCamera.depthTextureMode = currentCam.depthTextureMode | DepthTextureMode.Depth;
		m_ReflectionCamera.stereoTargetEye = StereoTargetEyeMask.None;
		m_ReflectionCamera.cullingMask = m_ReflectLayers.value;


		// Render reflection
		// Reflect camera around reflection plane
		float d = -Vector3.Dot(normal, pos);
		Vector4 reflectionPlane = new Vector4(normal.x, normal.y, normal.z, d);

		Matrix4x4 reflection = Matrix4x4.zero;
		CalculateReflectionMatrix(ref reflection, reflectionPlane);
		Matrix4x4 worldToCameraMatrix = !currentCam.stereoEnabled ? currentCam.worldToCameraMatrix * reflection : currentCam.GetStereoViewMatrix(eye) * reflection;
		m_ReflectionCamera.worldToCameraMatrix = worldToCameraMatrix;
		// Setup oblique projection matrix so that near plane is our reflection
		// plane. This way we clip everything below/above it for free.
		Vector4 clipPlane = CameraSpacePlane(worldToCameraMatrix, pos, normal, 1.0f);
		m_ReflectionCamera.projectionMatrix = currentCam.stereoEnabled ? currentCam.GetStereoProjectionMatrix(eye) : currentCam.projectionMatrix;
		m_ReflectionCamera.projectionMatrix = m_ReflectionCamera.CalculateObliqueMatrix(clipPlane);
		m_ReflectionCamera.targetTexture = ReflectionTexture;
		//int num = GL.invertCulling ? 1 : 0;
		bool invertCulling = GL.invertCulling;
		GL.invertCulling = !invertCulling;// num == 0;
		try
		{
			if (MoveMirrorCam)
			{
				Vector3 position2 = currentCam.transform.position;
				Vector3 vector3 = m_ReflectionCamera.projectionMatrix.MultiplyPoint(position2);
				m_ReflectionCamera.transform.position = vector3;
				Vector3 eulerAngles = currentCam.transform.eulerAngles;
				m_ReflectionCamera.transform.eulerAngles = new Vector3(0.0f, eulerAngles.y, eulerAngles.z);
				m_ReflectionCamera.Render();
				m_ReflectionCamera.transform.position = position2;
			}
			else
			{
				m_ReflectionCamera.Render();
			}
		}
		catch (UnityException e) 
		{
			Debug.LogWarning(e.Message);
			Debug.LogWarning(m_ReflectionCamera.transform.position);
		}

		GL.invertCulling = invertCulling;// num != 0;
		Material[] materials = rend.sharedMaterials;
		string name = "_ReflectionTex" + eye.ToString();

		foreach (Material mat in materials)
		{
			if (mat.HasProperty(name))
				mat.SetTexture(name, ReflectionTexture);
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
	}
    private void OnDestroy()
    {
		DestroyImmediate(cameraTransform.gameObject);
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
		//dest.cullingMask = ??
	}

	// On-demand create any objects we need
	private void CreateMirrorObjects(Camera currentCamera, Camera.StereoscopicEye eye, ref RenderTexture reflectionTexture)
	{
		int width = Math.Min(usedTextureSize, currentCamera.pixelWidth);
		int height = Math.Min(usedTextureSize, currentCamera.pixelHeight);
		int antiAliasing = usedMsaa;
		if (antiAliasing == 0)
		{
			RenderTexture targetTexture = currentCamera.targetTexture;
			antiAliasing = !(targetTexture != null) ? (QualitySettings.antiAliasing == 0 ? 1 : QualitySettings.antiAliasing) : targetTexture.antiAliasing;
		}

		// Reflection render texture

		if (reflectionTexture)
			RenderTexture.ReleaseTemporary(reflectionTexture);
		reflectionTexture = RenderTexture.GetTemporary(width, height, 24,
			RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Default, antiAliasing,
			RenderTextureMemoryless.None, VRTextureUsage.None);
		reflectionTexture.name = "__MirrorReflection" + eye.ToString() + GetInstanceID();
		if (m_ReflectionCamera != null)
			return;

		// Camera for reflection

		GameObject gameObject = new GameObject("MirrorReflection Camera id" + GetInstanceID(), typeof(Camera), typeof(Skybox));
		gameObject.transform.SetParent(transform);
		m_ReflectionCamera = gameObject.GetComponent<Camera>();
		m_ReflectionCamera.enabled = false;
		m_ReflectionCamera.gameObject.AddComponent<FlareLayer>();
		gameObject.hideFlags = HideFlags.HideAndDontSave;
		//m_ReflectionCameras[currentCamera] = m_ReflectionCamera;

	}

	// Extended sign: returns -1, 0 or 1 based on sign of a
	private static float sgn(float a)
	{
		if (a > 0.0f) return 1.0f;
		if (a < 0.0f) return -1.0f;
		return 0.0f;
	}

	// Given position/normal of the plane, calculates plane in camera space.
	private Vector4 CameraSpacePlane(Camera cam, Vector3 pos, Vector3 normal, float sideSign)
	{
		Vector3 offsetPos = pos + normal * 0f;
		Matrix4x4 m = cam.worldToCameraMatrix;
		Vector3 cpos = m.MultiplyPoint(offsetPos);
		Vector3 cnormal = m.MultiplyVector(normal).normalized * sideSign;
		return new Vector4(cnormal.x, cnormal.y, cnormal.z, -Vector3.Dot(cpos, cnormal));
	}
	private Vector4 CameraSpacePlane(
  Matrix4x4 worldToCameraMatrix,
  Vector3 pos,
  Vector3 normal,
  float sideSign)
	{ 
		//Vector3 point = pos + normal * 0f;
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