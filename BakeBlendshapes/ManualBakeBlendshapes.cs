using UnityEngine;
using System.Collections.Generic;

[RequireComponent(typeof(Animator))]
public class ManualBakeBlendshapes : MonoBehaviour
{
    public bool keepMmd = true;
    public bool bakeNonMoving = true;
    public List<string> keepBlendshapes = new List<string>();
    public List<RuntimeAnimatorController> controllers = new List<RuntimeAnimatorController>();
}