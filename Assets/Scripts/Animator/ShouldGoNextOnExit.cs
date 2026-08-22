using UnityEngine;
using uDesktopMascot;

public sealed class ShouldGoNextOnExit : StateMachineBehaviour
{
    public override void OnStateEnter(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
    {
        var manager = FindAnyObjectByType<CharacterManager>();
        if (manager != null)
        {
            manager.ApplyPendingGroundAction();
            return;
        }

        TestActionDecisionManager testManager = null;
        if (animator != null)
        {
            testManager = animator.GetComponentInParent<TestActionDecisionManager>();
        }

        if (testManager == null)
        {
            testManager = FindAnyObjectByType<TestActionDecisionManager>();
        }

        testManager?.ApplyPendingGroundAction();
    }
}