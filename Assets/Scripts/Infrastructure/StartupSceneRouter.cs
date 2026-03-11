using System;
using System.IO;
using Unity.Logging;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class StartupSceneRouter
{
    private const string CharacterArgumentKey = "--character";
    private static string s_cachedTargetScene;
    private static bool s_hasRouted;
    private static bool s_subscribedSceneGuard;
    private static bool s_isUiMode;
    private static bool s_isCharacterMode;
    private static bool s_isRoutingNow;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void RouteInitialScene()
    {
        CacheProcessRoleFlags();
        TryRouteToTargetScene("before-load");
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void RouteAfterSceneLoad()
    {
        CacheProcessRoleFlags();

        // Run once again after the first scene has loaded in case another boot path overwrote routing.
        TryRouteToTargetScene("after-load");

        if (s_subscribedSceneGuard)
        {
            return;
        }

        SceneManager.activeSceneChanged += OnActiveSceneChanged;
        s_subscribedSceneGuard = true;
    }

    private static void TryRouteToTargetScene(string phase)
    {
        if (s_isRoutingNow || s_hasRouted)
        {
            return;
        }

        string targetScene = ResolveTargetSceneFromCommandLine();
        if (string.IsNullOrWhiteSpace(targetScene))
        {
            return;
        }

        s_cachedTargetScene = targetScene;

        string currentSceneName = SceneManager.GetActiveScene().name;
        if (string.Equals(currentSceneName, targetScene, StringComparison.OrdinalIgnoreCase))
        {
            s_hasRouted = true;
            return;
        }

        if (TryResolveBuildSceneIndex(targetScene, out int buildIndex))
        {
            LoadSceneByIndex(buildIndex, currentSceneName, targetScene, phase);
            return;
        }

        if (!Application.CanStreamedLevelBeLoaded(targetScene))
        {
            Log.Warning($"[StartupSceneRouter] Requested scene is not in build settings: {targetScene}");
            return;
        }

        LoadSceneByName(targetScene, currentSceneName, phase);
    }

    private static string ResolveTargetSceneFromCommandLine()
    {
        try
        {
            string[] args = Environment.GetCommandLineArgs();
            if (args == null || args.Length == 0)
            {
                return string.Empty;
            }

            for (int i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], Constant.UIProcessArgument, StringComparison.OrdinalIgnoreCase))
                {
                    s_isUiMode = true;
                    return Constant.UIStartupSceneName;
                }

                if (string.Equals(args[i], CharacterArgumentKey, StringComparison.OrdinalIgnoreCase))
                {
                    s_isCharacterMode = true;
                    return Constant.CharacterStartupSceneName;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warning($"[StartupSceneRouter] Failed to parse command line arguments: {ex.Message}");
        }

        return string.Empty;
    }

    private static void CacheProcessRoleFlags()
    {
        try
        {
            if (s_isUiMode || s_isCharacterMode)
            {
                return;
            }

            string[] args = Environment.GetCommandLineArgs();
            if (args == null || args.Length == 0)
            {
                return;
            }

            for (int i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], Constant.UIProcessArgument, StringComparison.OrdinalIgnoreCase))
                {
                    s_isUiMode = true;
                    return;
                }

                if (string.Equals(args[i], CharacterArgumentKey, StringComparison.OrdinalIgnoreCase))
                {
                    s_isCharacterMode = true;
                }
            }
        }
        catch
        {
        }
    }

    private static void OnActiveSceneChanged(Scene previous, Scene current)
    {
        if (!s_isUiMode)
        {
            return;
        }

        string expectedScene = Constant.UIStartupSceneName;
        if (string.IsNullOrWhiteSpace(expectedScene))
        {
            return;
        }

        if (string.Equals(current.name, expectedScene, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (s_isRoutingNow)
        {
            return;
        }

        if (TryResolveBuildSceneIndex(expectedScene, out int uiSceneIndex))
        {
            Log.Warning($"[StartupSceneRouter] Detected scene drift in --ui process: {current.name} -> {expectedScene}. Restoring UI scene.");
            LoadSceneByIndex(uiSceneIndex, current.name, expectedScene, "ui-guard");
            return;
        }

        if (Application.CanStreamedLevelBeLoaded(expectedScene))
        {
            Log.Warning($"[StartupSceneRouter] Detected scene drift in --ui process: {current.name} -> {expectedScene}. Restoring by name.");
            LoadSceneByName(expectedScene, current.name, "ui-guard");
        }
    }

    private static void LoadSceneByIndex(int buildIndex, string currentSceneName, string targetScene, string phase)
    {
        if (buildIndex < 0)
        {
            return;
        }

        s_hasRouted = true;
        s_isRoutingNow = true;
        try
        {
            Log.Info($"[StartupSceneRouter] ({phase}) Switching startup scene by index: {currentSceneName} -> {targetScene} (index={buildIndex})");
            SceneManager.LoadScene(buildIndex, LoadSceneMode.Single);
        }
        finally
        {
            s_isRoutingNow = false;
        }
    }

    private static void LoadSceneByName(string targetScene, string currentSceneName, string phase)
    {
        s_hasRouted = true;
        s_isRoutingNow = true;
        try
        {
            Log.Info($"[StartupSceneRouter] ({phase}) Switching startup scene by name: {currentSceneName} -> {targetScene}");
            SceneManager.LoadScene(targetScene, LoadSceneMode.Single);
        }
        finally
        {
            s_isRoutingNow = false;
        }
    }

    private static bool TryResolveBuildSceneIndex(string targetScene, out int buildIndex)
    {
        buildIndex = -1;

        if (string.IsNullOrWhiteSpace(targetScene))
        {
            return false;
        }

        int count = SceneManager.sceneCountInBuildSettings;
        if (count <= 0)
        {
            return false;
        }

        for (int i = 0; i < count; i++)
        {
            string scenePath = SceneUtility.GetScenePathByBuildIndex(i);
            if (string.IsNullOrWhiteSpace(scenePath))
            {
                continue;
            }

            string sceneName = Path.GetFileNameWithoutExtension(scenePath) ?? string.Empty;
            if (string.Equals(sceneName, targetScene, StringComparison.OrdinalIgnoreCase))
            {
                buildIndex = i;
                return true;
            }
        }

        return false;
    }
}
