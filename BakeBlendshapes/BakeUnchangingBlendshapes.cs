using UnityEngine;
using System.Collections.Generic;
using System;
#if UNITY_EDITOR
using UnityEditor;


[CustomEditor(typeof(BakeUnchangingBlendshapes))]
public class BakeUnchangingBlendshapesEditor : Editor
{
    private const string GeneratedFolderName = "_GeneratedMeshes";
    private const string RemovedBlendshapesName = "RemovedBlendshapes";
    private const string GeneratedFolderPath = "Assets/" + GeneratedFolderName;
    private const string RemovedBlendshapesPath = GeneratedFolderPath + "/" + RemovedBlendshapesName;
    private Animator animator;
    private GameObject _targetGameObject;
    private BakeUnchangingBlendshapes script;
    private GUIStyle textStyle;
    private void OnEnable()
    {
        Init();
        FindVisemeBlink();
    }

    private void Init()
    {
        if (!_targetGameObject)
        {
            script = (BakeUnchangingBlendshapes)target;
            _targetGameObject = script.gameObject;
        }
        if (!animator)
            animator = _targetGameObject.GetComponent<Animator>();
        if (textStyle == null)
        {
            try
            {
                textStyle = EditorStyles.label;
                textStyle.wordWrap = true;
            }
            catch (NullReferenceException) { }
        }
    }

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        Init();
        if (GUILayout.Button("Find viseme, blink, and look up/down blendshape names"))
        {
            FindVisemeBlink();
        }
        GUILayout.Label("Please make sure you have all eye movement related shapekeys (blink, looking up and down) and viseme shapkeys if they are not in the '*v_sil' format in the Keep Blendshapes list before baking.", textStyle);
        GUILayout.Space(20);
        if (GUILayout.Button("Bake the unchanging blendshapes"))
        {
            var sw = new System.Diagnostics.Stopwatch();
            sw.Start();
            DoingIt(animator, script.keepBlendshapes, script.controllers, script.keepMmd, script.bakeNonMoving);
            sw.Stop();
            Debug.Log($"BakeUnchangingBlendshapes took {sw.Elapsed.TotalMilliseconds} ms.");
        }
    }

    private void FindVisemeBlink()
    {
        FindVisemeBlink(_targetGameObject, out var names);
        if (names.Count > 0)
        {
            var keepSet = new HashSet<string>(script.keepBlendshapes);
            keepSet.UnionWith(names);
            script.keepBlendshapes.Clear();
            script.keepBlendshapes.AddRange(keepSet);
        }
    }

    private static readonly List<Vector3> verts = new List<Vector3>();
    private static readonly List<Vector3> normals = new List<Vector3>();
    private static readonly List<Vector4> tangents = new List<Vector4>();
    private static readonly List<Animator> animators = new List<Animator>();
    private static readonly List<SkinnedMeshRenderer> skinnedMeshRenderers = new List<SkinnedMeshRenderer>();
    private static readonly Dictionary<SkinnedMeshRenderer, HashSet<int>> blendShapesPerMeshObject = new Dictionary<SkinnedMeshRenderer, HashSet<int>>();
    private static readonly Dictionary<string, AnimatorPaths> blendShapesPerMeshPath = new Dictionary<string, AnimatorPaths>();
    private static readonly Dictionary<string, Mesh> replacementMeshes = new Dictionary<string, Mesh>();
    private static readonly Dictionary<string, List<BlendShapeValues>> staticValues = new Dictionary<string, List<BlendShapeValues>>();
    private static void DoingIt(Animator animator, List<string> keepBlendshapes, List<RuntimeAnimatorController> controllers, bool keepMmd, bool bakeNonMoving)
    {
        if (!animator)
            return;

        var root = animator.gameObject;
        blendShapesPerMeshPath.Clear();
        animators.Clear();
        root.GetComponentsInChildren(true, animators);
        foreach (var item in animators)
        {
            var controller = item.runtimeAnimatorController;
            if (!controller)
                continue;

            ProcessAllClips(animator, root);
        }
        var _overrideController = animator.runtimeAnimatorController;
        for (int i = 0; i < controllers.Count; i++)
        {
            if (!controllers[i])
                continue;
            animator.runtimeAnimatorController = controllers[i];
            ProcessAllClips(animator, root);
        }
        animator.runtimeAnimatorController = _overrideController;
        animators.Clear();
        staticValues.Clear();
        // find meshes and shapekey indicies

        skinnedMeshRenderers.Clear();
        animator.GetComponentsInChildren(true, skinnedMeshRenderers);
        var blendShapeData = new List<BlendShapeData>();
        var keepBlendshapesSet = new HashSet<string>(keepBlendshapes);
        replacementMeshes.Clear();
        foreach (var smr in skinnedMeshRenderers)
        {
            var mesh = smr.sharedMesh;
            if (!mesh)
                continue;
            int blendShapeCount = mesh.blendShapeCount;

            // no blendshapes, no problems
            if (blendShapeCount == 0)
                continue;

            var blendShapeIndiciesToKeep = blendShapesPerMeshObject[smr] = new HashSet<int>();
            var path = AnimationUtility.CalculateTransformPath(smr.transform, root.transform);
            if (blendShapesPerMeshPath.TryGetValue(path, out var animatorPaths))
            {
                var blendShapeNames = animatorPaths.paths;

                foreach (var c in blendShapeNames)
                {
                    if (c == null || c.Length <= 11)
                        continue;
                    var blendShapeName = c.Substring(11);
                    var index = mesh.GetBlendShapeIndex(blendShapeName);
                    if (index < 0)
                        continue;
                    blendShapeIndiciesToKeep.Add(index);
                }
            }
            // add exceptions
            List<BlendShapeValues> staticBlendShapeValues = null;
            for (int i = 0; i < blendShapeCount; i++)
            {
                var blendShapeName = mesh.GetBlendShapeName(i);
                if (keepBlendshapesSet.Contains(blendShapeName))
                {
                    blendShapeIndiciesToKeep.Add(i);
                }
                if (keepMmd && mmdShapeKeys.Contains(blendShapeName))
                {
                    blendShapeIndiciesToKeep.Add(i);
                }
            }

            // if we keep all blendshapes then no point doing the extra work
            if (blendShapeIndiciesToKeep.Count == blendShapeCount)
                continue;

            blendShapeData.Clear();
            var rendererPath = AnimationUtility.CalculateTransformPath(smr.transform, smr.transform.root);
            int vertCount = mesh.vertexCount;
            for (int i = 0; i < blendShapeCount; i++)
            {
                bool isAnimated = blendShapeIndiciesToKeep.Contains(i);
                var weight = smr.GetBlendShapeWeight(i);
                var name = mesh.GetBlendShapeName(i);
                if (weight != 0f)
                {
                    if (staticBlendShapeValues == null)
                    {
                        staticBlendShapeValues = new List<BlendShapeValues>();
                    }
                    staticBlendShapeValues.Add(new BlendShapeValues(name, weight));
                }
                if (!isAnimated && weight == 0f)
                    continue;
                var frames = mesh.GetBlendShapeFrameCount(i);
                var blendShapeFrames = new BlendShapeFrameData[frames];
                blendShapeData.Add(new BlendShapeData(name, frames, blendShapeFrames, isAnimated, weight));
                for (int j = 0; j < frames; j++)
                {
                    Vector3[] dVerts = new Vector3[vertCount];
                    Vector3[] dNormals = new Vector3[vertCount];
                    Vector3[] dTangents = new Vector3[vertCount];
                    var frameWeight = mesh.GetBlendShapeFrameWeight(i, j);
                    mesh.GetBlendShapeFrameVertices(i, j, dVerts, dNormals, dTangents);
                    blendShapeFrames[j] = new BlendShapeFrameData(frameWeight, dVerts, dNormals, dTangents);
                }
            }
            if (staticBlendShapeValues != null)
                staticValues.Add(path, staticBlendShapeValues);

            var newMesh = Instantiate(mesh);
            newMesh.ClearBlendShapes();
            replacementMeshes[rendererPath] = newMesh;
            newMesh.GetVertices(verts);
            newMesh.GetNormals(normals);
            newMesh.GetTangents(tangents);
            bool baked = false;
            for (int i = 0; i < blendShapeData.Count; i++)
            {
                var d = blendShapeData[i];
                if (!d.isAnimated && d.frameCount == 1 && bakeNonMoving)
                {
                    //bake shapekey
                    baked = true;
                    float weight = d.weight / 100f;
                    var fr = d.blendShapeFrames[0];
                    var dVerts = fr.dVerts;
                    var dNormals = fr.dNormals;
                    var dTangents = fr.dTangents;
                    /*
                       vertex.pos += blendShapeVert.pos * g_Weight;
                       vertex.norm += blendShapeVert.norm * g_Weight;
                       vertex.tang.xyz += blendShapeVert.tang * g_Weight;
                     */
                    for (int k = 0; k < verts.Count; k++)
                    {
                        verts[k] += dVerts[k] * weight;
                    }
                    if (dNormals != null && normals != null)
                    {
                        for (int k = 0; k < verts.Count; k++)
                        {
                            normals[k] += dNormals[k] * weight;
                        }
                    }
                    if (dTangents != null && tangents != null)
                    {
                        for (int k = 0; k < verts.Count; k++)
                        {
                            var value = dTangents[k] * weight;
                            var newValue = tangents[k];
                            newValue.x += value.x;
                            newValue.y += value.y;
                            newValue.z += value.z;
                            tangents[k] = newValue;
                        }
                    }
                }
                else
                {
                    // add shapekey
                    for (int j = 0; j < d.frameCount; j++)
                    {
                        var fr = d.blendShapeFrames[j];
                        newMesh.AddBlendShapeFrame(d.name, fr.weight, fr.dVerts, fr.dNormals, fr.dTangents);
                    }
                }
            }
            if (baked)
            {
                newMesh.SetVertices(verts);
                newMesh.SetNormals(normals);
                newMesh.SetTangents(tangents);
            }
        }

        // create copy
        var avatarCopy = Instantiate(root);
        skinnedMeshRenderers.Clear();
        avatarCopy.GetComponentsInChildren(true, skinnedMeshRenderers);
        var avatarName = avatarCopy.name = root.name;
        avatarCopy.transform.Translate(Vector3.left);
        if (avatarCopy.TryGetComponent<BakeUnchangingBlendshapes>(out var thisComponent))
        {
            DestroyImmediate(thisComponent);
        }
        string currentAssetFolder = CreateFolders(avatarName);
        foreach (var renderer in skinnedMeshRenderers)
        {
            try
            {
                if (!renderer || !renderer.sharedMesh)
                    continue;
                var mesh = renderer.sharedMesh;
                var name = AnimationUtility.CalculateTransformPath(renderer.transform, renderer.transform.root);
                if (!replacementMeshes.TryGetValue(name, out var newMesh))
                {
                    continue;
                }
                for (int i = 0; i < mesh.blendShapeCount; i++)
                {
                    renderer.SetBlendShapeWeight(i, 0f);
                }
                var path = $"{currentAssetFolder}/{renderer.name}.asset";
                var uniqueAssetPath = AssetDatabase.GenerateUniqueAssetPath(path);
                AssetDatabase.CreateAsset(newMesh, uniqueAssetPath);
                renderer.sharedMesh = newMesh;

                if (!staticValues.TryGetValue(name, out var blendShapeValues))
                {
                    continue;
                }
                for (int i = 0; i < blendShapeValues.Count; i++)
                {
                    var blendShapeValue = blendShapeValues[i];
                    if (blendShapeValue.baked)
                    {
                        continue;
                    }
                    var index = newMesh.GetBlendShapeIndex(blendShapeValue.name);
                    if (index >= 0)
                    {
                        renderer.SetBlendShapeWeight(index, blendShapeValue.weight);
                    }
                }
            }
            catch (Exception) { }
        }
        AssetDatabase.SaveAssets();
        animators.Clear();
        skinnedMeshRenderers.Clear();
        blendShapesPerMeshObject.Clear();
        blendShapesPerMeshPath.Clear();
        replacementMeshes.Clear();
        staticValues.Clear();
    }

    private static string CreateFolders(string avatarName)
    {
        string date = $"{DateTime.Now:yyyy-MM-dd HH.mm.ss}";
        avatarName = MakeValidFileName(avatarName);
        string folder = RemovedBlendshapesPath + "/" + avatarName;
        if (!AssetDatabase.IsValidFolder(GeneratedFolderPath))
            AssetDatabase.CreateFolder("Assets", GeneratedFolderName);
        if (!AssetDatabase.IsValidFolder(RemovedBlendshapesPath))
            AssetDatabase.CreateFolder(GeneratedFolderPath, RemovedBlendshapesName);
        if (!AssetDatabase.IsValidFolder(folder))
            AssetDatabase.CreateFolder(RemovedBlendshapesPath, avatarName);
        string currentAssetSubFolder = $"{date}";
        string currentAssetFolder = folder + "/" + currentAssetSubFolder;
        AssetDatabase.CreateFolder(folder, currentAssetSubFolder);
        return currentAssetFolder;
    }

    private static string MakeValidFileName(string name)
    {
        string invalidChars = System.Text.RegularExpressions.Regex.Escape(new string(System.IO.Path.GetInvalidFileNameChars()));
        string invalidRegStr = string.Format(@"([{0}]*\.+$)|([{0}]+)", invalidChars);

        return System.Text.RegularExpressions.Regex.Replace(name, invalidRegStr, "_");
    }
    private static void ProcessAllClips(Animator animator, GameObject root)
    {
        var controller = animator.runtimeAnimatorController;
        var clips = controller.animationClips;
        if (clips == null)
            return;
        var pathToAnimator = AnimationUtility.CalculateTransformPath(animator.transform, root.transform);
        foreach (var c in clips)
        {
            var curves = AnimationUtility.GetObjectReferenceCurveBindings(c);
            var curveBindings = AnimationUtility.GetCurveBindings(c);
            foreach (var curveBinding in curveBindings)
            {
                if (curveBinding.type != typeof(SkinnedMeshRenderer) || curveBinding.propertyName?.StartsWith("blendShape") != true)
                    continue;
                var path = pathToAnimator + (string.IsNullOrEmpty(pathToAnimator) ? "" : "/") + curveBinding.path;
                if (!blendShapesPerMeshPath.TryGetValue(path, out var data))
                {
                    data = blendShapesPerMeshPath[path] = new AnimatorPaths(path);
                }
                data.paths.Add(curveBinding.propertyName);
            }
        }
    }
    private static void FindVisemeBlink(GameObject go, out List<string> names)
    {
        names = new List<string>();
        go.GetComponentsInChildren(true, skinnedMeshRenderers);
        foreach (var item in skinnedMeshRenderers)
        {
            if (!item.sharedMesh || item.sharedMesh.blendShapeCount == 0)
                continue;
            var mesh = item.sharedMesh;
            for (int i = 0; i < mesh.blendShapeCount; i++)
            {
                var originalName = mesh.GetBlendShapeName(i);
                if (string.IsNullOrEmpty(originalName))
                    continue;
                var name = originalName.ToLowerInvariant();
                if (name.Contains("blink") || (name.Contains("look") && (name.Contains("up") || name.Contains("down"))))
                {
                    names.Add(originalName);
                    continue;
                }
                for (int j = 0; j < faceShapes.Count; j++)
                {
                    if (name.Contains(faceShapes[j]))
                    {
                        names.Add(originalName);
                        break;
                    }
                }
            }
        }
    }
    private class BlendShapeData
    {
        public string name;
        public int frameCount;
        public bool isAnimated;
        public float weight;
        public BlendShapeFrameData[] blendShapeFrames;

        public BlendShapeData(string name, int frameCount, BlendShapeFrameData[] blendShapeFrames, bool isAnimated, float weight)
        {
            this.name = name;
            this.frameCount = frameCount;
            this.blendShapeFrames = blendShapeFrames;
            this.isAnimated = isAnimated;
            this.weight = weight;
        }
        public override string ToString()
        {
            return $"name: '{name}', {frameCount} frames, {(isAnimated ? "animated" : "not animated")}";
        }
    }
    private class AnimatorPaths 
    {
        public string pathToRenderer;
        public HashSet<string> paths = new HashSet<string>();

        public AnimatorPaths(string pathToRenderer)
        {
            this.pathToRenderer = pathToRenderer;
        }
    }
    private class BlendShapeFrameData
    {
        public float weight;
        public Vector3[] dVerts;
        public Vector3[] dNormals;
        public Vector3[] dTangents;
        public BlendShapeFrameData(float weight, Vector3[] dVerts, Vector3[] dNormals, Vector3[] dTangents)
        {
            this.weight = weight;
            this.dVerts = dVerts;
            this.dNormals = dNormals;
            this.dTangents = dTangents;
        }
        public override string ToString()
        {
            return $"{weight} weight, has {dVerts?.Length ?? 0} verts{(dNormals != null? ", normals" : "")}{(dTangents != null ? ", tangents" : "")}";
        }
    }
    private class BlendShapeValues
    {
        public string name;
        public float weight;
        public bool isAnimated;
        internal bool baked;

        public BlendShapeValues(string name, float weight)
        {
            this.name = name;
            this.weight = weight;
        }
        public override int GetHashCode()
        {
            return name.GetHashCode();
        }
        public override string ToString()
        {
            return $"'{name}': {weight}, {(isAnimated ? "animated" : "not animated")}, {(baked? "baked" : "not baked")}";
        }
    }
    private static readonly List<string> faceShapes = new List<string>
    {
            "v_sil","v_pp","v_ff","v_th","v_dd","v_kk","v_ch","v_ss","v_nn","v_rr","v_aa","v_e","v_ih","v_oh","v_ou","blink"
    };
    private static readonly HashSet<string> mmdShapeKeys = new HashSet<string>
    {
       "困る","怒り","上","下","眉上げ","不機嫌","左眉上げ","左眉不機嫌","左眉怒り","左眉困り","右眉上げ","右眉不機嫌","右眉怒り","右眉困り","まばたき","笑い",
        "ウィンク","ウィンク２","ウインク右","ｳィﾝｸ右２","なごみ","はぅ","こらっ","くわっ","じと目","あ","い","う","え","お","▲","δ","∧","あ２","ああ","いい","瞳小"
    };
}

#endif
[RequireComponent(typeof(Animator))]
public class BakeUnchangingBlendshapes : MonoBehaviour
{
    public bool keepMmd = true;
    public bool bakeNonMoving = true;
    public List<string> keepBlendshapes = new List<string>();
    public List<RuntimeAnimatorController> controllers = new List<RuntimeAnimatorController>();
}