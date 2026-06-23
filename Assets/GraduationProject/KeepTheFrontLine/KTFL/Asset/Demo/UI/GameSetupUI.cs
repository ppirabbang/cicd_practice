using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 씬 3 (사람 vs AI) 게임 설정 UI.
/// 게임 시작 전에 AI 상대 선택과 유닛 구성을 설정하고,
/// 게임 종료 후 결과를 표시한다.
/// 
/// UI 구성:
/// - SetupPanel: 게임 시작 전 설정 패널
///   - AI 선택 드롭다운
///   - AI 설명 텍스트
///   - 유닛 종류별 +/- 버튼과 수량 표시
///   - 총 유닛 수 표시
///   - 게임 시작 버튼
/// - ResultPanel: 게임 종료 후 결과 패널
///   - 결과 텍스트
///   - 다시 하기 버튼
/// </summary>
public class GameSetupUI : MonoBehaviour
{
    // =========================================================================
    // 참조
    // =========================================================================

    [Header("매니저")]
    [SerializeField] private PlayerVsAIManager _manager;

    [Header("설정 패널")]
    [SerializeField] private GameObject _setupPanel;

    [Header("AI 선택")]
    [SerializeField] private TMP_Dropdown _aiDropdown;
    [SerializeField] private TextMeshProUGUI _aiDescriptionText;

    [Header("유닛 수량 UI")]
    [Tooltip("유닛 종류별 수량 표시 텍스트 (종류 수만큼 할당).")]
    [SerializeField] private TextMeshProUGUI[] _unitCountTexts;
    [Tooltip("유닛 종류별 + 버튼.")]
    [SerializeField] private Button[] _unitPlusButtons;
    [Tooltip("유닛 종류별 - 버튼.")]
    [SerializeField] private Button[] _unitMinusButtons;
    [Tooltip("유닛 종류 이름 텍스트.")]
    [SerializeField] private TextMeshProUGUI[] _unitNameTexts;

    [Header("총 유닛 수")]
    [SerializeField] private TextMeshProUGUI _totalCountText;

    [Header("시작 버튼")]
    [SerializeField] private Button _startButton;

    [Header("결과 패널")]
    [SerializeField] private GameObject _resultPanel;
    [SerializeField] private TextMeshProUGUI _resultText;
    [SerializeField] private Button _restartButton;

    // =========================================================================
    // 내부 상태
    // =========================================================================

    private int[] _unitCounts;
    private int _selectedAIIndex;

    // =========================================================================
    // 초기화
    // =========================================================================

    private void Start()
    {
        // 유닛 수량 배열 초기화
        _unitCounts = new int[_manager.UnitTypes.Count];
        for (int i = 0; i < _unitCounts.Length; i++)
        {
            _unitCounts[i] = 1; // 각 종류 기본 1개
        }

        // AI 드롭다운 초기화
        InitializeAIDropdown();

        // 유닛 이름 표시
        InitializeUnitNames();

        // 버튼 이벤트 연결
        SetupButtons();

        // 시작 시 설정 패널 표시, 결과 패널 숨김
        _setupPanel.SetActive(true);
        _resultPanel.SetActive(false);

        UpdateUI();
    }

    private void Update()
    {
        // 게임 종료 감지
        if (_manager.IsGameRunning == false &&
            !string.IsNullOrEmpty(_manager.GameResultText) &&
            !_resultPanel.activeSelf &&
            !_setupPanel.activeSelf)
        {
            ShowResult();
        }
    }

    // =========================================================================
    // AI 드롭다운
    // =========================================================================

    private void InitializeAIDropdown()
    {
        if (_aiDropdown == null) return;

        _aiDropdown.ClearOptions();
        var options = _manager.AIOpponents
            .Select(ai => ai.displayName)
            .ToList();
        _aiDropdown.AddOptions(options);
        _aiDropdown.onValueChanged.AddListener(OnAISelectionChanged);

        _selectedAIIndex = 0;
        UpdateAIDescription();
    }

    private void OnAISelectionChanged(int index)
    {
        _selectedAIIndex = index;
        UpdateAIDescription();
    }

    private void UpdateAIDescription()
    {
        if (_aiDescriptionText == null) return;
        if (_selectedAIIndex < _manager.AIOpponents.Count)
        {
            _aiDescriptionText.text = _manager.AIOpponents[_selectedAIIndex].description;
        }
    }

    // =========================================================================
    // 유닛 수량 조절
    // =========================================================================

    private void InitializeUnitNames()
    {
        for (int i = 0; i < _unitNameTexts.Length && i < _manager.UnitTypes.Count; i++)
        {
            if (_unitNameTexts[i] != null)
                _unitNameTexts[i].text = _manager.UnitTypes[i].typeName;
        }
    }

    private void SetupButtons()
    {
        for (int i = 0; i < _unitPlusButtons.Length && i < _unitCounts.Length; i++)
        {
            int index = i; // 클로저 캡처용
            if (_unitPlusButtons[i] != null)
                _unitPlusButtons[i].onClick.AddListener(() => ChangeUnitCount(index, 1));
            if (_unitMinusButtons[i] != null)
                _unitMinusButtons[i].onClick.AddListener(() => ChangeUnitCount(index, -1));
        }

        if (_startButton != null)
            _startButton.onClick.AddListener(OnStartClicked);

        if (_restartButton != null)
            _restartButton.onClick.AddListener(OnRestartClicked);
    }

    private void ChangeUnitCount(int typeIndex, int delta)
    {
        int newCount = _unitCounts[typeIndex] + delta;
        int currentTotal = _unitCounts.Sum();
        int newTotal = currentTotal + delta;

        // 각 종류 최소 0, 최대는 총합 제한으로
        if (newCount < 0) return;
        if (newTotal > _manager.MaxTotalUnits) return;
        if (newTotal < 1) return; // 최소 1개는 있어야 함

        _unitCounts[typeIndex] = newCount;
        UpdateUI();
    }

    // =========================================================================
    // UI 업데이트
    // =========================================================================

    private void UpdateUI()
    {
        // 수량 표시
        for (int i = 0; i < _unitCountTexts.Length && i < _unitCounts.Length; i++)
        {
            if (_unitCountTexts[i] != null)
                _unitCountTexts[i].text = _unitCounts[i].ToString();
        }

        // 총 수량 표시
        int total = _unitCounts.Sum();
        if (_totalCountText != null)
            _totalCountText.text = $"Total Unit: {total} / {_manager.MaxTotalUnits}";

        // +/- 버튼 활성화/비활성화
        for (int i = 0; i < _unitCounts.Length; i++)
        {
            if (i < _unitMinusButtons.Length && _unitMinusButtons[i] != null)
                _unitMinusButtons[i].interactable = _unitCounts[i] > 0;
            if (i < _unitPlusButtons.Length && _unitPlusButtons[i] != null)
                _unitPlusButtons[i].interactable = total < _manager.MaxTotalUnits;
        }

        // 시작 버튼: 유닛이 1개 이상이어야 활성화
        if (_startButton != null)
            _startButton.interactable = total >= 1;
    }

    // =========================================================================
    // 게임 시작/종료
    // =========================================================================

    private void OnStartClicked()
    {
        _setupPanel.SetActive(false);
        _resultPanel.SetActive(false);
        _manager.StartGame(_selectedAIIndex, _unitCounts);
    }

    private void ShowResult()
    {
        _resultPanel.SetActive(true);
        if (_resultText != null)
            _resultText.text = _manager.GameResultText;
    }

    private void OnRestartClicked()
    {
        _resultPanel.SetActive(false);
        _setupPanel.SetActive(true);

        // 씬 재로드로 깨끗한 상태 복원
        UnityEngine.SceneManagement.SceneManager.LoadScene(
            UnityEngine.SceneManagement.SceneManager.GetActiveScene().name);
    }
}
