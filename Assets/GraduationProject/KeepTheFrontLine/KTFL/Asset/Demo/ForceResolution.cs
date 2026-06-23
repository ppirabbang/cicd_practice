using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 앱 시작 시 및 매 씬 전환마다 해상도와 품질을 강제 설정한다.
/// 씬 전환 시 해상도가 축소되는 문제를 방지한다.
/// </summary>
public static class ForceResolution
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSplashScreen)]
    static void Init()
    {
        QualitySettings.SetQualityLevel(5, true);
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        var nativeRes = Screen.currentResolution;
        Screen.SetResolution(nativeRes.width, nativeRes.height, FullScreenMode.FullScreenWindow);
    }
}