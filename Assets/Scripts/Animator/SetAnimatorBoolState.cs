using UnityEngine;

public class SetAnimatorBoolState : StateMachineBehaviour
{
    public string parameterName;
    public bool stateOnEnter;

    override public void OnStateEnter(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
    {
        animator.SetBool(parameterName, stateOnEnter);
    }
}
