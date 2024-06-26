﻿using UnityEngine;
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
    private readonly string[] EyeLids = new string[3];

    private void Init()
    {
        if (!_targetGameObject)
        {
            script = (ManualBakeBlendshapes)target;
            _targetGameObject = script.gameObject;
            if (!animator)
                animator = _targetGameObject.GetComponent<Animator>();
            if (script && animator && animator.runtimeAnimatorController)
            {
                var controller = animator.runtimeAnimatorController;
                if (!script.controllers.Contains(controller))
                    script.controllers.Add(controller);
            }
        }
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
        GUILayout.Space(20);
        if (GUILayout.Button("Guess viseme, blink, and look up/down blendshape names"))
        {
            FindVisemeBlink();
        }
        GUILayout.Space(20);
        if (GUILayout.Button("Find controllers and blendshapes from avatar"))
        {
            FindControllersFromAvatar();
        }
        GUILayout.Space(20);
        GUILayout.Label("Please make sure you have all eye movement related shapekeys (blink, looking up and down) and viseme shapkeys if they are not in the '*v_sil' format in the Keep Blendshapes list before baking.", textStyle);
        GUILayout.Space(20);
        if (GUILayout.Button("Bake the unchanging blendshapes"))
        {
            var sw = new System.Diagnostics.Stopwatch();
            sw.Start();
            BakeBlendshapes.Process(animator, script.keepBlendshapes, script.controllers, keepMmd: script.keepMmd, bakeNonMoving: script.bakeNonMoving, makeCopy: true, addToClenup: false);
            BakeBlendshapesBuildIntegration.ReassignEyelids(animator.gameObject, EyeLids);
            sw.Stop();
            Debug.Log($"BakeUnchangingBlendshapes took {sw.Elapsed.TotalMilliseconds} ms.");
        }
    }

    private void FindControllersFromAvatar()
    {
        if (!script)
            return;
        var keepBlendshapes = script.keepBlendshapes;
        var controllers = script.controllers;
        BakeBlendshapesBuildIntegration.FindControllersVRCSDK2(_targetGameObject, keepBlendshapes, controllers);
        BakeBlendshapesBuildIntegration.FindControllersVRCSDK3(_targetGameObject, keepBlendshapes, EyeLids, controllers);
        BakeBlendshapesBuildIntegration.FindControllersCVRAvatar(_targetGameObject, keepBlendshapes, controllers);
        BakeBlendshapesBuildIntegration.FindControllersCVRProp(_targetGameObject, keepBlendshapes, controllers);
        var tempBlendshapes = new HashSet<string>(keepBlendshapes);
        keepBlendshapes.Clear();
        keepBlendshapes.AddRange(tempBlendshapes);
        var tempControllers = new HashSet<RuntimeAnimatorController>(controllers);
        controllers.Clear();
        controllers.AddRange(tempControllers);
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
        FindControllersFromAvatar();
    }
}