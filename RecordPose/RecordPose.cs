using UnityEngine;


namespace Zettai.PoseRecorder
{
    public class RecordPose : MonoBehaviour
    {
        public Avatar targetAvatar;
        public Animator animator;
        public GameObject _gameObject;
        private HumanPose humanPose;
        private HumanPoseHandler humanPoseHandler;
        void Start()
        {
            humanPose = new HumanPose();
        }
        public HumanPose Record()
        {
            if (targetAvatar != null)
            {
                humanPoseHandler = new HumanPoseHandler(targetAvatar, _gameObject.transform);
                humanPoseHandler.GetHumanPose(ref humanPose);
                Debug.Log(humanPose.muscles);
            }
            return humanPose;
        }
    }
}