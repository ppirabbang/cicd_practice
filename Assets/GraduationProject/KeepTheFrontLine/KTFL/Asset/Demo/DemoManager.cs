using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TurnBasedStrategyFramework.Common.Controllers.GameResolvers;
using TurnBasedStrategyFramework.Common.Players;
using TurnBasedStrategyFramework.Unity.Controllers;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 데모 씬 2 (ML vs 수동) 전용 매니저.
/// 에피소드를 자동 반복하며 누적 승률을 추적한다.
/// ML-Agents 보상 분배 없이 순수 시연 목적으로 사용한다.
/// 
/// DontDestroyOnLoad로 씬 재로드 간 통계를 유지하며,
/// 씬 2를 벗어나면 자동 파괴되어 승률이 초기화된다.
/// </summary>
public class DemoManager : MonoBehaviour
{
    // =========================================================================
    // 싱글턴
    // =========================================================================

    private static DemoManager _instance;
    public static DemoManager Instance => _instance;

    // =========================================================================
    // 설정
    // =========================================================================

    [Header("플레이어 설정")]
    [Tooltip("ML Agent가 제어하는 플레이어 번호.")]
    [SerializeField] private int _mlPlayerNumber = 1;

    [Header("교착 상태 방지")]
    [Tooltip("최대 턴 수. 초과 시 교착으로 처리.")]
    [SerializeField] private int _maxTurnsPerEpisode = 70;

    [Header("에피소드 간 대기")]
    [Tooltip("게임 종료 후 다음 에피소드까지 대기 시간 (초). 결과를 확인할 시간.")]
    [SerializeField] private float _delayBeforeReload = 2f;

    // =========================================================================
    // 통계 (static으로 씬 이동 간에도 유지)
    // =========================================================================

    public static int CompletedEpisodes { get; private set; }
    public static int MLWins { get; private set; }
    public static int ManualWins { get; private set; }
    public static int Stalemates { get; private set; }

    public float MLWinRate => CompletedEpisodes > 0 ? (float)MLWins / CompletedEpisodes : 0f;
    public float ManualWinRate => CompletedEpisodes > 0 ? (float)ManualWins / CompletedEpisodes : 0f;
    public float StalemateRate => CompletedEpisodes > 0 ? (float)Stalemates / CompletedEpisodes : 0f;

    /// <summary>마지막 에피소드 결과. UI 표시용.</summary>
    public static string LastResult { get; private set; } = "";

    /// <summary>통계를 초기화한다. UI 리셋 버튼에서 호출.</summary>
    public static void ResetStats()
    {
        CompletedEpisodes = 0;
        MLWins = 0;
        ManualWins = 0;
        Stalemates = 0;
        LastResult = "";
    }

    // =========================================================================
    // 내부 상태
    // =========================================================================

    private UnityGridController _gridController;
    private string _demoSceneName;
    private int _currentTurnCount;
    private bool _gameEnded;

    // =========================================================================
    // 생명주기
    // =========================================================================

    private void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }

        _instance = this;
        DontDestroyOnLoad(gameObject);
        _demoSceneName = SceneManager.GetActiveScene().name;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDestroy()
    {
        if (_instance != this) return;
        SceneManager.sceneLoaded -= OnSceneLoaded;
        _instance = null;
    }

    // =========================================================================
    // 씬 로드
    // =========================================================================

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // 데모 씬이 아닌 다른 씬으로 이동하면 자기 자신 파괴
        if (scene.name != _demoSceneName)
        {
            Destroy(gameObject);
            return;
        }

        StartCoroutine(BeginEpisodeRoutine());
    }

    private IEnumerator BeginEpisodeRoutine()
    {
        yield return null;

        _currentTurnCount = 0;
        _gameEnded = false;

        _gridController = FindFirstObjectByType<UnityGridController>();

        if (_gridController == null)
        {
            Debug.LogError("[DemoManager] GridController를 찾을 수 없습니다.");
            yield break;
        }

        _gridController.GameEnded += OnGameEnded;
        _gridController.TurnEnded += OnTurnEnded;
        _gridController.InitializeAndStart();
    }

    // =========================================================================
    // 턴/교착 관리
    // =========================================================================

    private void OnTurnEnded(TurnBasedStrategyFramework.Common.Controllers.TurnTransitionParams turnParams)
    {
        if (_gameEnded) return;

        _currentTurnCount++;

        if (_maxTurnsPerEpisode > 0 && _currentTurnCount >= _maxTurnsPerEpisode)
        {
            ForceEndStalemate();
        }
    }

    private void ForceEndStalemate()
    {
        if (_gameEnded) return;
        _gameEnded = true;

        _gridController.InvokeGameEnded(new GameResult(
            new List<IPlayer>(),
            new List<IPlayer>()
        ));
    }

    // =========================================================================
    // 게임 종료
    // =========================================================================

    private void OnGameEnded(GameResult gameResult)
    {
        if (_gameEnded && gameResult.Winners != null && gameResult.Winners.Any())
        {
            // ForceEndStalemate로 종료했지만 승자가 있는 경우 (정상 종료로 취급)
        }

        _gameEnded = true;

        // 이벤트 해제
        if (_gridController != null)
        {
            _gridController.GameEnded -= OnGameEnded;
            _gridController.TurnEnded -= OnTurnEnded;
        }

        // 승패 판정
        bool hasWinners = gameResult.Winners != null && gameResult.Winners.Any();

        if (hasWinners)
        {
            int winnerPlayerNumber = gameResult.Winners.First().PlayerNumber;

            if (winnerPlayerNumber == _mlPlayerNumber)
            {
                MLWins++;
                LastResult = "ML win";
            }
            else
            {
                ManualWins++;
                LastResult = "manual AI win";
            }
        }
        else
        {
            Stalemates++;
            LastResult = "draw";
        }

        CompletedEpisodes++;

        Debug.Log($"[DemoManager] 에피소드 {CompletedEpisodes} | {LastResult} | " +
                  $"ML {MLWins}승 ({MLWinRate:P1}) | 수동 {ManualWins}승 ({ManualWinRate:P1}) | " +
                  $"교착 {Stalemates}회 ({StalemateRate:P1})");

        // 다음 에피소드
        StartCoroutine(DelayedReload());
    }

    private IEnumerator DelayedReload()
    {
        // 실제 시간으로 대기 (TimeScale 영향 받지 않음)
        yield return new WaitForSecondsRealtime(_delayBeforeReload);
        SceneManager.LoadScene(_demoSceneName);
    }

    // =========================================================================
    // TimeScale 조절
    // =========================================================================

    /// <summary>TimeScale을 설정한다. UI 슬라이더에서 호출.</summary>
    public void SetTimeScale(float scale)
    {
        Time.timeScale = Mathf.Clamp(scale, 0.5f, 50f);
    }

    /// <summary>현재 TimeScale을 반환한다.</summary>
    public float GetTimeScale()
    {
        return Time.timeScale;
    }
}