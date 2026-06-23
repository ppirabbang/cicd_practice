using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TurnBasedStrategyFramework.Common.Controllers;
using TurnBasedStrategyFramework.Common.Controllers.GameResolvers;
using TurnBasedStrategyFramework.Common.Players;
using TurnBasedStrategyFramework.Common.Units;
using TurnBasedStrategyFramework.Unity.Controllers;
using TurnBasedStrategyFramework.Unity.Units;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TurnBasedStrategyFramework.ML
{
    /// <summary>
    /// Self-Play ML 학습 환경을 관리하는 컴포넌트.
    /// 양쪽 진영 모두 ML Agent로 구성되며, ML-Agents의 Self-Play 시스템과 연동된다.
    /// 
    /// TrainingManager와의 차이:
    /// - 양쪽 모두 ML Agent (수동 AI 없음)
    /// - 보상이 양쪽에 대칭적으로 적용
    /// - ML-Agents가 자동으로 현재 모델 vs 고스트 모델 관리
    /// - ELO 레이팅으로 성장 추적 (TensorBoard에서 확인)
    /// 
    /// 에피소드 흐름:
    ///   씬 로드 → InitializeAndStart → GameInitialized
    ///   → SelfPlayMapRunner: 맵 생성 + 양쪽 ML 유닛 스폰
    ///   → 양쪽 Agent 수집 + 보상 이벤트 구독
    ///   → 게임 진행 (양쪽 모두 보상 분배)
    ///   → 게임 종료 → 최종 보상 → 씬 재로드
    /// </summary>
    public class SelfPlayTrainingManager : MonoBehaviour
    {
        // ============================================================
        //  싱글턴
        // ============================================================

        private static SelfPlayTrainingManager _instance;

        // ============================================================
        //  학습 설정
        // ============================================================

        [Header("학습 설정")]
        [Tooltip("총 학습 에피소드 수. 0이면 무제한.")]
        [SerializeField] private int _maxEpisodes = 0;

        [Tooltip("true이면 씬 로드 시 자동으로 학습을 시작한다.")]
        [SerializeField] private bool _autoStart = true;

        // ============================================================
        //  교착 상태 방지
        // ============================================================

        [Header("교착 상태 방지")]
        [Tooltip("최대 턴 수. 초과 시 양측 모두 패배 처리.")]
        [SerializeField] private int _maxTurnsPerEpisode = 100;

        [Tooltip("교착 시 모든 Agent에게 부여하는 패널티.")]
        [SerializeField] private float _stalematePenalty = -0.5f;

        // ============================================================
        //  팀 보상 (각 팀 관점에서 적용)
        // ============================================================

        [Header("팀 보상")]
        [Tooltip("적 유닛 처치 시 해당 팀 모든 Agent에게 부여.")]
        [SerializeField] private float _enemyKilledReward = 0.3f;

        [Tooltip("아군 유닛 사망 시 해당 팀 모든 Agent에게 부여 (음수).")]
        [SerializeField] private float _allyDeathPenalty = -0.3f;

        // ============================================================
        //  개인 보상
        // ============================================================

        [Header("개인 보상")]
        [Tooltip("데미지 가함 보상 계수.")]
        [SerializeField] private float _damageDealtRewardScale = 0.5f;

        [Tooltip("데미지 받음 패널티 계수.")]
        [SerializeField] private float _damageTakenPenaltyScale = -0.5f;

        [Tooltip("유리한 위치 이동 보상.")]
        [SerializeField] private float _advantageousPositionReward = 0.05f;

        [Tooltip("턴 생존 보너스.")]
        [SerializeField] private float _turnSurvivalBonus = 0.02f;

        // ============================================================
        //  통계 설정
        // ============================================================

        [Header("통계 설정")]
        [Tooltip("실험 이름.")]
        [SerializeField] private string _runId = "selfplay01";

        [Tooltip("파일 로그 기록 간격 (에피소드 수).")]
        [SerializeField] private int _logInterval = 100;

        // ============================================================
        //  통계
        // ============================================================

        public int CompletedEpisodes { get; private set; }
        public int Team0Wins { get; private set; }
        public int Team1Wins { get; private set; }
        public int TotalStalemates { get; private set; }

        // ============================================================
        //  내부 상태
        // ============================================================

        private UnityGridController _gridController;
        private UnityUnitManager _unitManager;

        /// <summary>팀별 Agent 목록. Key: PlayerNumber (0 또는 1).</summary>
        private Dictionary<int, List<BehaviourTreeAgent>> _teamAgents = new Dictionary<int, List<BehaviourTreeAgent>>();

        /// <summary>유닛 → Agent 매핑.</summary>
        private Dictionary<IUnit, BehaviourTreeAgent> _unitToAgentMap = new Dictionary<IUnit, BehaviourTreeAgent>();

        private bool _isTraining;
        private int _currentTurnCount;
        private bool _isStalemateEnd;
        private string _trainingSceneName;

        // ============================================================
        //  생명주기
        // ============================================================

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;
            DontDestroyOnLoad(gameObject);
            SceneManager.sceneLoaded += OnSceneLoaded;
            _trainingSceneName = SceneManager.GetActiveScene().name;
        }

        private void OnDestroy()
        {
            if (_instance != this) return;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            _instance = null;
        }

        // ============================================================
        //  씬 로드
        // ============================================================

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.name != _trainingSceneName) return;

            if (_autoStart || _isTraining)
            {
                StartCoroutine(BeginEpisodeRoutine());
            }
        }

        private IEnumerator BeginEpisodeRoutine()
        {
            yield return null;

            _isTraining = true;
            _currentTurnCount = 0;
            _isStalemateEnd = false;

            _gridController = FindFirstObjectByType<UnityGridController>();
            _unitManager = FindFirstObjectByType<UnityUnitManager>();

            if (_gridController == null || _unitManager == null)
            {
                Debug.LogError("[SelfPlayTrainingManager] GridController 또는 UnitManager를 찾을 수 없습니다.");
                yield break;
            }

            _gridController.GameEnded += OnGameEnded;
            _gridController.InitializeAndStart();

            CollectAllAgents();
            SubscribeRewardEvents();

            int totalAgents = _teamAgents.Values.Sum(list => list.Count);
            Debug.Log($"[SelfPlayTrainingManager] 에피소드 {CompletedEpisodes + 1} 시작. " +
                      $"Team 0: {GetTeamAgentCount(0)}개, Team 1: {GetTeamAgentCount(1)}개 Agent.");
        }

        // ============================================================
        //  Agent 수집
        // ============================================================

        /// <summary>
        /// 양쪽 플레이어의 모든 ML Agent를 수집한다.
        /// </summary>
        private void CollectAllAgents()
        {
            _teamAgents.Clear();
            _unitToAgentMap.Clear();
            _teamAgents[0] = new List<BehaviourTreeAgent>();
            _teamAgents[1] = new List<BehaviourTreeAgent>();

            var allUnits = _gridController.UnitManager.GetUnits();
            foreach (var unit in allUnits)
            {
                if (unit is Unit unityUnit)
                {
                    var agent = unityUnit.GetComponentInChildren<BehaviourTreeAgent>();
                    if (agent != null)
                    {
                        int team = unit.PlayerNumber;
                        if (!_teamAgents.ContainsKey(team))
                            _teamAgents[team] = new List<BehaviourTreeAgent>();

                        _teamAgents[team].Add(agent);
                        _unitToAgentMap[unit] = agent;
                    }
                }
            }
        }

        private int GetTeamAgentCount(int team)
        {
            return _teamAgents.ContainsKey(team) ? _teamAgents[team].Count : 0;
        }

        // ============================================================
        //  보상 분배 헬퍼
        // ============================================================

        /// <summary>특정 팀의 모든 Agent에게 보상을 분배한다.</summary>
        private void ApplyTeamReward(int teamNumber, float reward)
        {
            if (!_teamAgents.ContainsKey(teamNumber)) return;
            foreach (var agent in _teamAgents[teamNumber])
            {
                if (agent != null) agent.ApplyIntermediateReward(reward);
            }
        }

        /// <summary>특정 유닛의 Agent에게만 보상을 부여한다.</summary>
        private void ApplyIndividualReward(IUnit unit, float reward)
        {
            if (_unitToAgentMap.TryGetValue(unit, out var agent))
            {
                if (agent != null) agent.ApplyIntermediateReward(reward);
            }
        }

        /// <summary>모든 Agent에게 보상을 분배한다.</summary>
        private void ApplyAllReward(float reward)
        {
            foreach (var kvp in _unitToAgentMap)
            {
                if (kvp.Value != null) kvp.Value.ApplyIntermediateReward(reward);
            }
        }

        // ============================================================
        //  보상 이벤트 구독/해제
        // ============================================================

        private void SubscribeRewardEvents()
        {
            if (_unitManager != null)
                _unitManager.UnitRemoved += OnUnitRemoved;

            if (_gridController != null)
                _gridController.TurnEnded += OnTurnEnded;

            var allUnits = _gridController?.UnitManager?.GetUnits();
            if (allUnits != null)
            {
                foreach (var unit in allUnits)
                {
                    unit.UnitAttacked += OnUnitAttacked;
                    unit.UnitMoved += OnUnitMoved;
                }
            }
        }

        private void UnsubscribeRewardEvents()
        {
            if (_unitManager != null)
                _unitManager.UnitRemoved -= OnUnitRemoved;

            if (_gridController != null)
                _gridController.TurnEnded -= OnTurnEnded;

            var allUnits = _gridController?.UnitManager?.GetUnits();
            if (allUnits != null)
            {
                foreach (var unit in allUnits)
                {
                    unit.UnitAttacked -= OnUnitAttacked;
                    unit.UnitMoved -= OnUnitMoved;
                }
            }
        }

        // ============================================================
        //  보상 이벤트 핸들러
        // ============================================================

        /// <summary>
        /// [팀 보상] 유닛 사망 시.
        /// 사망한 유닛의 팀: 아군 사망 패널티.
        /// 상대 팀: 적 처치 보상.
        /// </summary>
        private void OnUnitRemoved(IUnit unit)
        {
            if (!_isTraining) return;

            int deadTeam = unit.PlayerNumber;
            int enemyTeam = deadTeam == 0 ? 1 : 0;

            // 사망한 유닛의 팀에 패널티
            ApplyTeamReward(deadTeam, _allyDeathPenalty);

            // 상대 팀에 보상
            ApplyTeamReward(enemyTeam, _enemyKilledReward);

            // 사망한 유닛을 매핑에서 제거
            _unitToAgentMap.Remove(unit);
        }

        /// <summary>
        /// [개인 보상] 데미지 가함/받음.
        /// 공격자: 데미지 가함 보상. 방어자: 데미지 받음 패널티.
        /// 양쪽 모두 ML이므로 양쪽 모두 보상을 받는다.
        /// </summary>
        private void OnUnitAttacked(UnitAttackedEventArgs eventArgs)
        {
            if (!_isTraining) return;

            // 공격자 보상 (적에게 데미지 가함)
            if (eventArgs.AttackingUnit.PlayerNumber != eventArgs.AffectedUnit.PlayerNumber)
            {
                float damageRatio = eventArgs.AffectedUnit.MaxHealth > 0
                    ? eventArgs.DamageDealt / eventArgs.AffectedUnit.MaxHealth
                    : 0f;

                ApplyIndividualReward(eventArgs.AttackingUnit, _damageDealtRewardScale * damageRatio);
                ApplyIndividualReward(eventArgs.AffectedUnit, _damageTakenPenaltyScale * damageRatio);
            }
        }

        /// <summary>
        /// [개인 보상] 유리한 위치 이동.
        /// </summary>
        private void OnUnitMoved(UnitMovedEventArgs eventArgs)
        {
            if (!_isTraining) return;

            if (eventArgs.TargetCell.WorldPosition.y > eventArgs.SourceCell.WorldPosition.y)
            {
                ApplyIndividualReward(eventArgs.AffectedUnit, _advantageousPositionReward);
            }
        }

        /// <summary>
        /// [개인 보상] 턴 생존 + 교착 검사.
        /// 현재 턴의 플레이어 소속 유닛에게 생존 보너스.
        /// </summary>
        private void OnTurnEnded(TurnTransitionParams turnTransitionParams)
        {
            if (!_isTraining) return;

            _currentTurnCount++;

            int currentTeam = turnTransitionParams.TurnContext.CurrentPlayer.PlayerNumber;
            if (_teamAgents.ContainsKey(currentTeam))
            {
                foreach (var agent in _teamAgents[currentTeam])
                {
                    if (agent != null) agent.ApplyIntermediateReward(_turnSurvivalBonus);
                }
            }

            if (_maxTurnsPerEpisode > 0 && _currentTurnCount >= _maxTurnsPerEpisode)
            {
                ForceEndStalemate();
            }
        }

        /// <summary>교착 상태 강제 종료.</summary>
        private void ForceEndStalemate()
        {
            Debug.Log($"<color=orange>[SelfPlayTrainingManager] 교착 상태 감지! {_currentTurnCount}턴 경과.</color>");

            ApplyAllReward(_stalematePenalty);

            _isStalemateEnd = true;
            _gridController.InvokeGameEnded(new GameResult(
                new List<IPlayer>(),
                new List<IPlayer>()
            ));
        }

        // ============================================================
        //  게임 종료
        // ============================================================

        private void OnGameEnded(GameResult gameResult)
        {
            if (!_isTraining) return;

            UnsubscribeRewardEvents();

            string episodeResult;

            if (_isStalemateEnd)
            {
                foreach (var kvp in _unitToAgentMap)
                {
                    if (kvp.Value != null) kvp.Value.EndEpisode();
                }

                TotalStalemates++;
                episodeResult = "교착";
                _isStalemateEnd = false;
            }
            else
            {
                // 승리 팀 판별
                bool hasWinners = gameResult.Winners != null && gameResult.Winners.Any();
                int winningTeam = -1;

                if (hasWinners)
                {
                    winningTeam = gameResult.Winners.First().PlayerNumber;
                }

                if (winningTeam == 0)
                {
                    Team0Wins++;
                    episodeResult = "Team 0 승리";
                }
                else if (winningTeam == 1)
                {
                    Team1Wins++;
                    episodeResult = "Team 1 승리";
                }
                else
                {
                    episodeResult = "무승부";
                }

                // 양쪽 Agent에게 승패 보상
                foreach (var kvp in _unitToAgentMap)
                {
                    if (kvp.Value == null) continue;

                    bool isWinner = hasWinners && kvp.Key.PlayerNumber == winningTeam;

                    // 승리 시 잔여 체력 비율에 따른 보너스
                    float healthBonus = 0f;
                    if (isWinner)
                    {
                        var aliveUnits = _gridController?.UnitManager?.GetFriendlyUnits(kvp.Key.PlayerNumber);
                        if (aliveUnits != null && aliveUnits.Any())
                        {
                            healthBonus = aliveUnits.Average(u => u.MaxHealth > 0 ? u.Health / u.MaxHealth : 0f) * 0.5f;
                        }
                    }

                    float reward = isWinner ? (1.0f + healthBonus) : -1.0f;
                    kvp.Value.ApplyIntermediateReward(reward);
                    kvp.Value.EndEpisode();
                }
            }

            CompletedEpisodes++;

            // 로그 출력
            float team0Rate = CompletedEpisodes > 0 ? (float)Team0Wins / CompletedEpisodes : 0f;
            float team1Rate = CompletedEpisodes > 0 ? (float)Team1Wins / CompletedEpisodes : 0f;
            float stalemateRate = CompletedEpisodes > 0 ? (float)TotalStalemates / CompletedEpisodes : 0f;

            Debug.Log($"<color=yellow>[SelfPlayTrainingManager] ===== 에피소드 {CompletedEpisodes} 종료 =====</color>\n" +
                      $"  결과: {episodeResult} | 턴 수: {_currentTurnCount}\n" +
                      $"  Team 0: {Team0Wins}회 ({team0Rate:P1}) | " +
                      $"Team 1: {Team1Wins}회 ({team1Rate:P1}) | " +
                      $"교착: {TotalStalemates}회 ({stalemateRate:P1})");

            // 파일 로그
            if (CompletedEpisodes % _logInterval == 0)
            {
                WriteLogToFile(episodeResult, team0Rate, team1Rate, stalemateRate);
            }

            if (_maxEpisodes > 0 && CompletedEpisodes >= _maxEpisodes)
            {
                _isTraining = false;
                Debug.Log($"[SelfPlayTrainingManager] 목표 에피소드 수 ({_maxEpisodes}) 달성.");
                return;
            }

            LoadNextEpisode();
        }

        // ============================================================
        //  씬 재로드
        // ============================================================

        private void LoadNextEpisode()
        {
            StartCoroutine(DelayedSceneReload());
        }

        private IEnumerator DelayedSceneReload()
        {
            yield return new WaitForEndOfFrame();
            yield return null;
            SceneManager.LoadScene(_trainingSceneName);
        }

        // ============================================================
        //  파일 로그
        // ============================================================

        private void WriteLogToFile(string episodeResult, float team0Rate, float team1Rate, float stalemateRate)
        {
            try
            {
                string logPath = Path.Combine(Application.dataPath, "..", $"selfplay_log_{_runId}.txt");
                string logLine = $"[{System.DateTime.Now:HH:mm:ss}] " +
                                 $"에피소드 {CompletedEpisodes} | {episodeResult} | 턴 {_currentTurnCount} | " +
                                 $"Team0 {Team0Wins}승 ({team0Rate:P1}) | " +
                                 $"Team1 {Team1Wins}승 ({team1Rate:P1}) | " +
                                 $"교착 {TotalStalemates}회 ({stalemateRate:P1})";
                File.AppendAllText(logPath, logLine + "\n");
            }
            catch (System.Exception) { }
        }
    }
}
