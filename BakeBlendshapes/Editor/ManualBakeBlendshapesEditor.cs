using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System;

[CustomEditor(typeof(ManualBakeBlendshapes))]
public class ManualBakeBlendshapesEditor : Editor
{
    private Animator animator;
    private GameObject _targetGameObject;
    private ManualBakeBlendshapes script;
    private GUIStyle textStyle;

    private void Init()
    {
        if (!_targetGameObject)
        {
            script = (ManualBakeBlendshapes)target;
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
            BakeBlendshapes.Process(animator, script.keepBlendshapes, script.controllers, script.keepMmd, script.bakeNonMoving, true, false);
            sw.Stop();
            Debug.Log($"BakeUnchangingBlendshapes took {sw.Elapsed.TotalMilliseconds} ms.");
        }
    }

    private void FindVisemeBlink()
    {
        BakeBlendshapes.FindVisemeBlink(_targetGameObject, out var names);
        if (names.Count > 0)
        {
            var keepSet = new HashSet<string>(script.keepBlendshapes);
            keepSet.UnionWith(names);
            script.keepBlendshapes.Clear();
            script.keepBlendshapes.AddRange(keepSet);
        }
    }
    private void OnEnable()
    {
        Init();
        FindVisemeBlink();
    }
}