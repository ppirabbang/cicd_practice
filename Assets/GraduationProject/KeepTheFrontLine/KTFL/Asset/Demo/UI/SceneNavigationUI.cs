using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 모든 데모 씬에서 사용하는 네비게이션 UI.
/// 우측 상단에 메뉴 버튼을 표시하고, 클릭 시 씬 전환 패널을 열어
/// 다른 씬으로 이동하거나 앱을 종료할 수 있다.
/// 
/// 사용법:
/// 1. Canvas 하위에 이 스크립트를 부착한 오브젝트를 배치한다.
/// 2. Inspector에서 각 UI 요소를 연결한다.
/// 3. 프리팹으로 저장하여 모든 데모 씬에 배치한다.
/// </summary>
public class SceneNavigationUI : MonoBehaviour
{
    // =========================================================================
    // 씬 이름 (Build Settings 순서와 일치해야 함)
    // =========================================================================

    private const string SCENE_1 = "DemoScene1";
    private const string SCENE_2 = "DemoScene2";
    private const string SCENE_3 = "DemoScene3";

    // =========================================================================
    // UI 참조
    // =========================================================================

    [Header("메뉴 버튼 (항상 표시)")]
    [SerializeField] private Button _menuButton;

    [Header("메뉴 패널 (토글)")]
    [SerializeField] private GameObject _menuPanel;

    [Header("씬 전환 버튼")]
    [SerializeField] private Button _scene1Button;
    [SerializeField] private Button _scene2Button;
    [SerializeField] private Button _scene3Button;

    [Header("종료 버튼")]
    [SerializeField] private Button _quitButton;

    [Header("현재 씬 버튼 비활성화")]
    [Tooltip("true이면 현재 씬에 해당하는 버튼을 비활성화한다.")]
    [SerializeField] private bool _disableCurrentSceneButton = true;

    // =========================================================================
    // 초기화
    // =========================================================================

    private void Awake()
    {
      
        // 시작 시 패널 닫기
        if (_menuPanel != null)
            _menuPanel.SetActive(false);

        // 버튼 이벤트 연결
        if (_menuButton != null)
            _menuButton.onClick.AddListener(ToggleMenu);

        if (_scene1Button != null)
            _scene1Button.onClick.AddListener(() => LoadScene(SCENE_1));

        if (_scene2Button != null)
            _scene2Button.onClick.AddListener(() => LoadScene(SCENE_2));

        if (_scene3Button != null)
            _scene3Button.onClick.AddListener(() => LoadScene(SCENE_3));

        if (_quitButton != null)
            _quitButton.onClick.AddListener(QuitApplication);

        // 현재 씬 버튼 비활성화
        if (_disableCurrentSceneButton)
            DisableCurrentSceneButton();
    }

    // =========================================================================
    // 메뉴 토글
    // =========================================================================

    /// <summary>메뉴 패널을 열거나 닫는다.</summary>
    public void ToggleMenu()
    {
        if (_menuPanel != null)
            _menuPanel.SetActive(!_menuPanel.activeSelf);
    }

    /// <summary>메뉴 패널을 닫는다.</summary>
    public void CloseMenu()
    {
        if (_menuPanel != null)
            _menuPanel.SetActive(false);
    }

    // =========================================================================
    // 씬 전환
    // =========================================================================

    private void LoadScene(string sceneName)
    {
        StartCoroutine(LoadSceneRoutine(sceneName));
    }

    /// <summary>
    /// 씬 전환 전에 게임을 정지시켜 비동기 작업이 파괴된 오브젝트에
    /// 접근하는 것을 방지한다.
    /// </summary>
    private System.Collections.IEnumerator LoadSceneRoutine(string sceneName)
    {
        // DontDestroyOnLoad 오브젝트 정리
        CleanupPersistentObjects();

        // 씬 즉시 로드
        // 이전 씬의 비동기 콜백이 MissingReferenceException을 발생시킬 수 있으나
        // 새 씬의 동작에는 영향 없음 (에디터에서는 Error Pause 해제 필요)
        Time.timeScale = 1f;
        SceneManager.LoadScene(sceneName);
        yield break;
    }

    /// <summary>
    /// 씬 전환 시 이전 씬의 DontDestroyOnLoad 오브젝트를 정리한다.
    /// </summary>
    private void CleanupPersistentObjects()
    {
        var demoManager = FindFirstObjectByType<DemoManager>();
        if (demoManager != null)
            Destroy(demoManager.gameObject);
    }

    // =========================================================================
    // 종료
    // =========================================================================

    private void QuitApplication()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    // =========================================================================
    // 현재 씬 버튼 비활성화
    // =========================================================================

    private void DisableCurrentSceneButton()
    {
        string currentScene = SceneManager.GetActiveScene().name;

        if (_scene1Button != null && currentScene == SCENE_1)
            _scene1Button.interactable = false;

        if (_scene2Button != null && currentScene == SCENE_2)
            _scene2Button.interactable = false;

        if (_scene3Button != null && currentScene == SCENE_3)
            _scene3Button.interactable = false;
    }
}