using Unity.Logging;
using UnityEngine;

public class SetStateManagingParameter : StateMachineBehaviour
{
    [SerializeField] private string parameterName;
    [SerializeField] private bool status;

    override public void OnStateEnter(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
    {
        Log.Info("SetStateManagingParameter: Reset IsIdling/IsSitting/IsWalking");
        animator.SetBool("IsSitting", false);
        animator.SetBool("IsHanding", false);
        animator.SetBool("IsWalking", false);
        animator.SetBool("IsIdling", false);
        Log.Info("SetStateManagingParameter: Set IsFalling -> false");
        animator.SetBool("IsFalling", false);

        Log.Info("SetStateManagingParameter: Set {0} -> {1}", parameterName, status);
        animator.SetBool(parameterName, status);
    }
}
