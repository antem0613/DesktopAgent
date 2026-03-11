using Mono.WebBrowser;
using uDesktopMascot;
using UnityEngine;

public class SetCharacterYOffset : StateMachineBehaviour
{
    public float yOffset;
    public bool useDefaultOffset;

    override public void OnStateEnter(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
    {
        var manager = FindFirstObjectByType<CharacterManager>();
        var testManager = ResolveTestManager(animator);

        if (manager == null && testManager == null)
        {
            Debug.LogWarning("SetCharacterYOffset: Manager not ready");
            return;
        }

        if (!useDefaultOffset)
        {
            if (manager != null)
            {
                manager.SetSitYOffset(yOffset);
            } else
            {
                testManager.SetSitYOffset(yOffset);
            }

            Debug.Log($"SetCharacterYOffset: sit offset applied {yOffset}");
        }
    }

    override public void OnStateExit(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
    {
        var manager = FindFirstObjectByType<CharacterManager>();
        var testManager = ResolveTestManager(animator);

        if (manager == null && testManager == null)
        {
            Debug.LogWarning("SetCharacterYOffset: Manager not ready");
            return;
        }

        if (useDefaultOffset)
        {
            if (manager != null)
            {
                manager.ResetSitYOffset();
            }
            else
            {
                testManager.ResetSitYOffset();
            }

            Debug.Log("SetCharacterYOffset: reset offsets (default)");
        }
    }

    private static TestActionDecisionManager ResolveTestManager(Animator animator)
    {
        var candidates = FindObjectsByType<TestActionDecisionManager>(FindObjectsSortMode.None);
        if (candidates == null || candidates.Length == 0)
        {
            return null;
        }

        if (animator != null)
        {
            for (int i = 0; i < candidates.Length; i++)
            {
                var candidate = candidates[i];
                if (candidate == null)
                {
                    continue;
                }

                if (animator.transform.IsChildOf(candidate.transform))
                {
                    return candidate;
                }

                var candidateAnimator = candidate.GetComponentInChildren<Animator>(true);
                if (candidateAnimator == animator)
                {
                    return candidate;
                }
            }
        }

        return candidates[0];
    }
}
