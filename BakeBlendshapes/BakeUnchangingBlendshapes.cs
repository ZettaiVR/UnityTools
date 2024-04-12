using UnityEngine;
using System.Collections.Generic;
using System;
using UnityEditor.Animations;
#if UNITY_EDITOR
using UnityEditor;


[CustomEditor(typeof(BakeUnchangingBlendshapes))]
public class BakeUnchangingBlendshapesEditor : Editor
{
    private const string NewFolderName = "_GeneratedMeshes";
    private const string AssetFolder = "Assets/" + NewFolderName;
    private Animator animator;
    private GameObject _targetGameObject;
    private BakeUnchangingBlendshapes script;
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        if (!_targetGameObject)
        {
            script = (BakeUnchangingBlendshapes)target;
            _targetGameObject = script.gameObject;
        }
        if (!animator)
            animator = _targetGameObject.GetComponent<Animator>();

        if (GUILayout.Button("dew it"))
        {
            DoingIt(animator, script.keepBlendshapes, script.controllers, script.keepMmd, script.bakeNonMoving);
        }
    }
    private static readonly List<Vector3> verts = new List<Vector3>();
    private static readonly List<Vector3> normals = new List<Vector3>();
    private static readonly List<Vector4> tangents = new List<Vector4>();
    private static readonly List<Animator> animators = new List<Animator>();
    private static readonly List<SkinnedMeshRenderer> skinnedMeshRenderers = new List<SkinnedMeshRenderer>();
    private static readonly Dictionary<SkinnedMeshRenderer, HashSet<int>> blendShapesPerMeshObject = new Dictionary<SkinnedMeshRenderer, HashSet<int>>();
    private static readonly Dictionary<string, HashSet<string>> blendShapesPerMeshPath = new Dictionary<string, HashSet<string>>();
    private static readonly Dictionary<string, Mesh> replacementMeshes = new Dictionary<string, Mesh>();
    private static readonly Dictionary<string, List<BlendShapeValues>> staticValues = new Dictionary<string, List<BlendShapeValues>>();
    private static void DoingIt(Animator animator, List<string> keepBlendshapes, List<AnimatorController> controllers, bool keepMmd, bool bakeNonMoving)
    {
        if (!animator)
            return;
        blendShapesPerMeshPath.Clear();
        animators.Clear();
        animator.gameObject.GetComponentsInChildren(true, animators);
        animators.Add(animator);
        foreach (var item in animators)
        {
            var controller = animator.runtimeAnimatorController;
            if (!controller)
                continue;

            ProcessAllClips(controller);
        }
        var _overrideController = animator.runtimeAnimatorController;
        for (int i = 0; i < controllers.Count; i++)
        {
            if (!controllers[i])
                continue;
            animator.runtimeAnimatorController = controllers[i];
            ProcessAllClips(animator.runtimeAnimatorController);
        }
        animator.runtimeAnimatorController = _overrideController;
        animators.Clear();
        staticValues.Clear();
        // find meshes and shapekey indicies

        var blendShapeData = new List<BlendShapeData>();
        var keepBlendshapesSet = new HashSet<string>(keepBlendshapes);
        replacementMeshes.Clear();
        foreach (var meshShapes in blendShapesPerMeshPath)
        {
            var meshTransform = animator.transform.Find(meshShapes.Key);
            if (!meshTransform)
                continue;

            if (meshTransform.TryGetComponent<SkinnedMeshRenderer>(out var smr) && smr.sharedMesh)
                blendShapesPerMeshObject[smr] = new HashSet<int>();
            else
                continue;


            var mesh = smr.sharedMesh;
            int blendShapeCount = mesh.blendShapeCount;

            // no blendshapes, no problems
            if (blendShapeCount == 0)
                continue;

            var blendShapeNames = meshShapes.Value;
            var blendShapeIndiciesToKeep = blendShapesPerMeshObject[smr];

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
            // add exceptions
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
                if (visemes.Contains(blendShapeName))
                {
                    blendShapeIndiciesToKeep.Add(i);
                }
            }
            // if we keep all blendshapes then no point doing the extra work
            if (blendShapeIndiciesToKeep.Count == blendShapeCount)
                continue;

            blendShapeData.Clear();
            var rendererPath = AnimationUtility.CalculateTransformPath(smr.transform, smr.transform.root);
            var blendShapeValues = new List<BlendShapeValues>();
            // only save blendshape data if we keep any of them
            if (blendShapeIndiciesToKeep.Count > 0)
            {
                staticValues.Add(rendererPath, blendShapeValues);
                int vertCount = mesh.vertexCount;
                for (int i = 0; i < blendShapeCount; i++)
                {
                    bool isAnimated = blendShapeIndiciesToKeep.Contains(i);
                    var weight = smr.GetBlendShapeWeight(i);
                    if (weight != 0f)
                    {
                        blendShapeValues.Add(new BlendShapeValues(mesh.GetBlendShapeName(i), weight));
                    }
                    else if (!isAnimated)
                        continue;
                    var frames = mesh.GetBlendShapeFrameCount(i);
                    var name = mesh.GetBlendShapeName(i);
                    var blendShapeFrames = new BlendShapeFrameData[frames];
                    blendShapeData.Add(new BlendShapeData(name, frames, blendShapeFrames, isAnimated));
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
            }
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
                var bakedShape = blendShapeValues.Find(a => a.name == d.name);
                if (!d.isAnimated && d.frameCount == 1 && bakedShape != null && bakeNonMoving)
                {
                    //bake shapekey
                    bakedShape.baked = true;
                    baked = true;
                    float weight = bakedShape.weight / 100f;
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
        var avatarCopy = Instantiate(animator.gameObject);
        skinnedMeshRenderers.Clear();
        avatarCopy.GetComponentsInChildren(true, skinnedMeshRenderers);
        avatarCopy.name = animator.gameObject.name;
        avatarCopy.transform.Translate(Vector3.left);
        var avatarName = animator.gameObject.name;
        if (!AssetDatabase.IsValidFolder(AssetFolder))
            AssetDatabase.CreateFolder("Assets", NewFolderName);
        string currentAssetSubFolder = $"RemovedBlendshapes-{DateTime.Now:yyyy_MM_dd_HH_mm_ss} - {avatarName}";
        string currentAssetFolder = AssetFolder + "/" + currentAssetSubFolder;
        AssetDatabase.CreateFolder(AssetFolder, currentAssetSubFolder);
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
                var path = $"{currentAssetFolder}/{renderer.name}-{mesh.name}.asset";
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

    private static void ProcessAllClips(RuntimeAnimatorController controller)
    {
        var clips = controller.animationClips;
        if (clips == null)
            return;

        foreach (var c in clips)
        {
            var curves = AnimationUtility.GetObjectReferenceCurveBindings(c);
            var aa = AnimationUtility.GetCurveBindings(c);
            foreach (var a in aa)
            {
                if (a.type != typeof(SkinnedMeshRenderer) || a.propertyName?.StartsWith("blendShape") != true)
                    continue;
                if (!blendShapesPerMeshPath.TryGetValue(a.path, out var set))
                {
                    set = blendShapesPerMeshPath[a.path] = new HashSet<string>();
                }
                set.Add(a.propertyName);
            }
        }
    }

    private class BlendShapeData
    {
        public string name;
        public int frameCount;
        public bool isAnimated;
        public BlendShapeFrameData[] blendShapeFrames;

        public BlendShapeData(string name, int frameCount, BlendShapeFrameData[] blendShapeFrames, bool isAnimated)
        {
            this.name = name;
            this.frameCount = frameCount;
            this.blendShapeFrames = blendShapeFrames;
            this.isAnimated = isAnimated;
        }
        public override string ToString()
        {
            return $"name: '{name}', {frameCount} frames, {(isAnimated ? "animated" : "not animated")}";
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
    private static readonly HashSet<string> visemes = new HashSet<string>
    {
            "v_sil","v_pp","v_ff","v_th","v_dd","v_kk","v_ch","v_ss","v_nn","v_rr","v_aa","v_e","v_ih","v_oh","v_ou"
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
    public List<AnimatorController> controllers = new List<AnimatorController>();
}