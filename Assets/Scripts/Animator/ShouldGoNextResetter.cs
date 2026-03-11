using Unity.Logging;
using UnityEngine;
using uDesktopMascot;

public sealed class ShouldGoNextResetter : StateMachineBehaviour
{
    public bool resetOnEnter = false;

    public override void OnStateEnter(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
    {
        if (animator.GetBool("IsGrabOnce"))
        {
            animator.SetBool("IsGrabOnce", false);
        }

        if (animator.GetBool("IsFallOnce"))
        {
            animator.SetBool("IsFallOnce", false);
        }

        if (resetOnEnter)
        {
            var manager = FindFirstObjectByType<CharacterManager>();
            if (manager != null)
            {
                if (!manager.AnimatorIsFalling)
                {
                    animator.SetBool(Constant.AnimatorShouldGoNext, false);
                    manager.OnEnterGroundActionState();
                }

                return;
            }

            var testManager = ResolveTestManager(animator);
            if (testManager != null)
            {
                animator.SetBool(Constant.AnimatorShouldGoNext, false);
                testManager.OnEnterGroundActionState();
            }
        }
    }

    public override void OnStateExit(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
    {
        if (!resetOnEnter)
        {
            var manager = FindFirstObjectByType<CharacterManager>();
            if (manager != null)
            {
                if (!manager.AnimatorIsFalling)
                {
                    animator.SetBool(Constant.AnimatorShouldGoNext, false);
                    manager.OnEnterGroundActionState();
                }

                return;
            }

            var testManager = ResolveTestManager(animator);
            if (testManager != null)
            {
                animator.SetBool(Constant.AnimatorShouldGoNext, false);
                testManager.OnEnterGroundActionState();
            }
        }
    }

    private static TestActionDecisionManager ResolveTestManager(Animator animator)
    {
        TestActionDecisionManager manager = null;
        if (animator != null)
        {
            manager = animator.GetComponentInParent<TestActionDecisionManager>();
        }

        if (manager == null)
        {
            manager = FindFirstObjectByType<TestActionDecisionManager>();
        }

        return manager;
    }
}
