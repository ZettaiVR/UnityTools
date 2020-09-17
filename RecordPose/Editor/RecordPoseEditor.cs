using UnityEngine;
using UnityEditor;
using System;

namespace Zettai.PoseRecorder
{
    [CustomEditor(typeof(RecordPose))]
    public class RecordPoseEditor : Editor
    {
        public RecordPose _RecordPose;
        private AnimationCurve CreateCurve(float value, float length) 
        {
            var curve = new AnimationCurve();
            curve.AddKey(0f, value);
            curve.AddKey(length, value);
            return curve;
        }
        private string GetCurveName(string name) 
        {
            if (name.Contains("Stretched") || name.Contains("Spread"))
            {
                name = name.Replace(" ", ".");
                name = name.Replace("Left.","LeftHand.");
                name = name.Replace("Right.", "RightHand.");
                name = name.Replace(".Stretched", " Stretched");
            }
            return name;
        }
        public override void OnInspectorGUI()
        {
            if (_RecordPose == null)
            {
                _RecordPose = (RecordPose)target;
            }
            if (_RecordPose.targetAvatar == null)
            {
                _RecordPose.animator = ((RecordPose)target).gameObject.GetComponent<Animator>();
                _RecordPose.targetAvatar = _RecordPose.animator.avatar;
                _RecordPose._gameObject = ((RecordPose)target).gameObject;
            }
            if (GUILayout.Button("Create humanoid animation"))
            {
                Vector3 originalPos = _RecordPose.animator.gameObject.transform.position;
                _RecordPose.animator.gameObject.transform.position = Vector3.zero;
                HumanPose _humanPose = _RecordPose.Record();
                _RecordPose.animator.gameObject.transform.position = originalPos;
                float frameRate = 60f;
                float length = 1 / frameRate;
                var clip = new AnimationClip { frameRate = frameRate };
                AnimationUtility.SetAnimationClipSettings(clip, new AnimationClipSettings { loopTime = false });
                //set muscle values
                for (int i = 0; i < HumanTrait.MuscleCount; i++)
                {
                    var muscle = GetCurveName(HumanTrait.MuscleName[i]);
                    clip.SetCurve("", typeof(Animator), muscle, CreateCurve(_humanPose.muscles[i], length));
                }
                Vector3 Position = _humanPose.bodyPosition;
                Quaternion Rotation = _humanPose.bodyRotation;// _RecordPose.animator.bodyRotation;
                //set root pos/rot
                clip.SetCurve("", typeof(Animator), "RootT.x", CreateCurve(Position.x, length));
                clip.SetCurve("", typeof(Animator), "RootT.y", CreateCurve(Position.y, length));
                clip.SetCurve("", typeof(Animator), "RootT.z", CreateCurve(Position.z, length));
                clip.SetCurve("", typeof(Animator), "RootQ.x", CreateCurve(Rotation.x, length));
                clip.SetCurve("", typeof(Animator), "RootQ.y", CreateCurve(Rotation.y, length));
                clip.SetCurve("", typeof(Animator), "RootQ.z", CreateCurve(Rotation.z, length));
                clip.SetCurve("", typeof(Animator), "RootQ.w", CreateCurve(Rotation.w, length));
                clip.EnsureQuaternionContinuity();
                var path = string.Format("Assets/Recorded_{0}_{1:yyyy_MM_dd_HH_mm_ss}_Humanoid.anim", _RecordPose._gameObject.name, DateTime.Now);
                var uniqueAssetPath = AssetDatabase.GenerateUniqueAssetPath(path);
                AssetDatabase.CreateAsset(clip, uniqueAssetPath);
                AssetDatabase.SaveAssets();
            }
        }
    }
}