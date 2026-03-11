using UnityEngine;
using Unity.Logging;

public class SetAnimationOnce : StateMachineBehaviour
{
    public string parameterName;
    public override void OnStateEnter(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
    {
        if (!animator.GetBool("IsGrabOnce"))
        {
            animator.SetBool("IsGrabOnce", false);
        }

        if(animator.GetBool("IsFallOnce"))
        {
            animator.SetBool("IsFallOnce", false);
        }

        animator.SetBool(parameterName, true);
    }
}
