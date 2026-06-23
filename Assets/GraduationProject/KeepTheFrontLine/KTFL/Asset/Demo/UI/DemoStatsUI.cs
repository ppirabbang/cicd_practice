using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 데모 씬 2의 통계 및 TimeScale UI.
/// 
/// 배치:
///   좌상단: 수동 가중치 진영 승리 횟수 (수동 유닛 배치 영역)
///   좌중단: 무승부 횟수
///   좌하단: 강화학습 진영 승리 횟수 (ML 유닛 배치 영역)
///   우측:   TimeScale 슬라이더 + 초기화 버튼
/// </summary>
public class DemoStatsUI : MonoBehaviour
{
    // =========================================================================
    // 진영별 통계 텍스트 (화면 위치에 맞게 분리)
    // =========================================================================

    [Header("수동 가중치 진영 (좌상단)")]
    [SerializeField] private TextMeshProUGUI _manualWinsText;

    [Header("무승부 (좌중단)")]
    [SerializeField] private TextMeshProUGUI _drawText;

    [Header("강화학습 진영 (좌하단)")]
    [SerializeField] private TextMeshProUGUI _mlWinsText;

    [Header("마지막 결과")]
    [SerializeField] private TextMeshProUGUI _lastResultText;

    [Header("에피소드 수")]
    [SerializeField] private TextMeshProUGUI _episodeText;

    // =========================================================================
    // TimeScale 조절
    // =========================================================================

    [Header("TimeScale 조절")]
    [SerializeField] private Slider _timeScaleSlider;
    [SerializeField] private TextMeshProUGUI _timeScaleValueText;

    [Header("초기화 버튼")]
    [SerializeField] private Button _resetButton;

    // =========================================================================
    // 초기화
    // =========================================================================

    private void Start()
    {
        Time.timeScale = 30.0f;

        if (_timeScaleSlider != null)
        {
            _timeScaleSlider.minValue = 1f;
            _timeScaleSlider.maxValue = 50f;
            _timeScaleSlider.wholeNumbers = true;
            _timeScaleSlider.value = Time.timeScale;
            _timeScaleSlider.onValueChanged.AddListener(OnTimeScaleChanged);
        }

        if (_resetButton != null)
        {
            _resetButton.onClick.AddListener(OnResetClicked);
        }
    }

    private void Update()
    {
        UpdateStatsDisplay();
        UpdateTimeScaleDisplay();
    }

    // =========================================================================
    // 통계 표시
    // =========================================================================

    private void UpdateStatsDisplay()
    {
        var dm = DemoManager.Instance;

        // 수동 가중치 진영 승리 (좌상단)
        if (_manualWinsText != null)
        {
            _manualWinsText.text = $"Manual Weight Units Win: {DemoManager.ManualWins}\n" +
                                   $" ({(DemoManager.CompletedEpisodes > 0 ? (float)DemoManager.ManualWins / DemoManager.CompletedEpisodes : 0f):P1})";
        }

        // 무승부 (좌중단)
        if (_drawText != null)
        {
            _drawText.text = $"Draw: {DemoManager.Stalemates}\n" +
                              $" ({(DemoManager.CompletedEpisodes > 0 ? (float)DemoManager.Stalemates / DemoManager.CompletedEpisodes : 0f):P1})";
        }

        // 강화학습 진영 승리 (좌하단)
        if (_mlWinsText != null)
        {
            _mlWinsText.text = $"RL Units Win: {DemoManager.MLWins} \n" +
                                $" ({(DemoManager.CompletedEpisodes > 0 ? (float)DemoManager.MLWins / DemoManager.CompletedEpisodes : 0f):P1})";
        }

        // 에피소드 수
        if (_episodeText != null)
        {
            _episodeText.text = $"Total Episode: {DemoManager.CompletedEpisodes}";
        }

        // 마지막 결과
        if (_lastResultText != null && !string.IsNullOrEmpty(DemoManager.LastResult))
        {
            _lastResultText.text = $"Recent: {DemoManager.LastResult}";

            if (DemoManager.LastResult.Contains("ML"))
                _lastResultText.color = new Color(0.3f, 0.8f, 0.3f);
            else if (DemoManager.LastResult.Contains("Manual") || DemoManager.LastResult.Contains("수동"))
                _lastResultText.color = new Color(0.9f, 0.3f, 0.3f);
            else
                _lastResultText.color = new Color(0.9f, 0.9f, 0.3f);
        }
    }

    // =========================================================================
    // TimeScale
    // =========================================================================

    private void OnTimeScaleChanged(float value)
    {
        var dm = DemoManager.Instance;
        if (dm != null)
            dm.SetTimeScale(value);
        else
            Time.timeScale = value;
    }

    private void UpdateTimeScaleDisplay()
    {
        if (_timeScaleValueText != null)
        {
            _timeScaleValueText.text = $"TimeScale: {Time.timeScale:F0}x";
        }
    }

    // =========================================================================
    // 초기화
    // =========================================================================

    private void OnResetClicked()
    {
        DemoManager.ResetStats();
    }
}