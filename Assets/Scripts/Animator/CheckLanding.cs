using Unity.Logging;
using UnityEngine;
using uDesktopMascot;

public class CheckLanding : StateMachineBehaviour
{
    public override void OnStateEnter(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
    {
        var manager = FindFirstObjectByType<CharacterManager>();
        if (manager != null)
        {
            manager.OnEnterLandingAnimation();
            return;
        }

        TestActionDecisionManager testManager = null;
        if (animator != null)
        {
            testManager = animator.GetComponentInParent<TestActionDecisionManager>();
        }

        if (testManager == null)
        {
            testManager = FindFirstObjectByType<TestActionDecisionManager>();
        }

        testManager?.OnEnterLandingAnimation();
    }

    public override void OnStateExit(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
    {
        var manager = FindFirstObjectByType<CharacterManager>();
        if (manager != null)
        {
            manager.OnExitLandingAnimation();
            return;
        }

        TestActionDecisionManager testManager = null;
        if (animator != null)
        {
            testManager = animator.GetComponentInParent<TestActionDecisionManager>();
        }

        if (testManager == null)
        {
            testManager = FindFirstObjectByType<TestActionDecisionManager>();
        }

        testManager?.OnExitLandingAnimation();
    }
}
