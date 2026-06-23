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
    /// ML 학습 환경을 관리하는 컴포넌트.
    /// 에피소드 전환 시 씬을 재로드하여 모든 오브젝트를 깨끗하게 초기화한다.
    /// DontDestroyOnLoad로 씬 간에 유지되며, 학습 통계와 상태를 보존한다.
    /// 
    /// 에피소드 흐름:
    ///   1. 씬 로드 완료 (sceneLoaded 콜백)
    ///   2. GridController/UnitManager 참조 획득
    ///   3. 이벤트 구독 (GameEnded, 보상 이벤트)
    ///   4. InitializeAndStart() 호출 → 게임 시작
    ///      → GameInitialized → TrainingMapRunner가 맵/유닛 생성
    ///   5. Agent 수집
    ///   6. 게임 진행 (보상 분배)
    ///   7. 게임 종료 → 최종 보상 → SceneManager.LoadScene(현재 씬)
    ///   8. 씬 재로드 → 1번부터 반복
    /// 
    /// 사용법:
    /// - 학습 전용 씬에 빈 오브젝트를 만들고 이 컴포넌트를 부착
    /// - UnityGridController의 _startImmediatelly를 반드시 false로 설정
    /// - 별도의 씬 참조 할당 불필요 (씬 로드 시 자동으로 탐색)
    /// </summary>
    public class TrainingManager : MonoBehaviour
    {
        // ============================================================
        //  싱글턴
        // ============================================================

        /// <summary>
        /// 싱글턴 인스턴스. 씬 재로드 시 중복 생성을 방지한다.
        /// </summary>
        private static TrainingManager _instance;

        // ============================================================
        //  학습 설정
        // ============================================================

        [Header("학습 설정")]
        [Tooltip("ML Agent가 제어하는 플레이어 번호.")]
        [SerializeField] private int _mlPlayerNumber = 1;

        [Tooltip("총 학습 에피소드 수. 0이면 무제한으로 학습한다.")]
        [SerializeField] private int _maxEpisodes = 0;

        [Tooltip("true이면 씬 로드 시 자동으로 학습을 시작한다.")]
        [SerializeField] private bool _autoStart = true;

        // ============================================================
        //  교착 상태 방지 설정
        // ============================================================

        [Header("교착 상태 방지")]
        [Tooltip("최대 턴 수. 이 턴 수를 초과하면 양측 모두 패배로 처리하고 에피소드를 종료한다. 0이면 제한 없음.")]
        [SerializeField] private int _maxTurnsPerEpisode = 100;

        [Tooltip("교착 상태로 종료 시 모든 Agent에게 부여하는 패널티.")]
        [SerializeField] private float _stalematePenalty = -0.5f;

        // ============================================================
        //  팀 보상 설정
        // ============================================================

        [Header("팀 보상 — 모든 ML Agent에게 분배")]
        [Tooltip("적 유닛을 처치했을 때 모든 ML Agent에게 부여하는 보상.")]
        [SerializeField] private float _enemyKilledReward = 0.1f;

        [Tooltip("아군 유닛이 사망했을 때 모든 ML Agent에게 부여하는 패널티 (음수).")]
        [SerializeField] private float _allyDeathPenalty = -0.1f;

        // ============================================================
        //  개인 보상 설정
        // ============================================================

        [Header("개인 보상 — 해당 유닛의 Agent에게만 분배")]
        [Tooltip("적에게 데미지를 가했을 때 보상 계수.")]
        [SerializeField] private float _damageDealtRewardScale = 0.01f;

        [Tooltip("적으로부터 데미지를 받았을 때 패널티 계수. 음수로 설정한다.")]
        [SerializeField] private float _damageTakenPenaltyScale = -0.01f;

        [Tooltip("유리한 위치로 이동했을 때 해당 유닛에게만 부여하는 보상.")]
        [SerializeField] private float _advantageousPositionReward = 0.005f;

        [Tooltip("각 턴 종료 시 생존한 유닛에게 부여하는 생존 보너스.")]
        [SerializeField] private float _turnSurvivalBonus = 0.005f;

        // ============================================================
        //  통계 (씬 재로드 및 학습 재개 간에 유지됨)
        // ============================================================

        /// <summary>현재까지 완료된 에피소드 수.</summary>
        public int CompletedEpisodes { get; private set; }

        /// <summary>ML 플레이어의 총 승리 횟수.</summary>
        public int TotalWins { get; private set; }

        /// <summary>상대 진영의 총 승리 횟수.</summary>
        public int TotalLosses { get; private set; }

        /// <summary>교착 상태로 종료된 횟수.</summary>
        public int TotalStalemates { get; private set; }

        /// <summary>ML 플레이어의 승률 (0.0 ~ 1.0).</summary>
        public float WinRate => CompletedEpisodes > 0 ? (float)TotalWins / CompletedEpisodes : 0f;

        /// <summary>상대 진영의 승률 (0.0 ~ 1.0).</summary>
        public float LossRate => CompletedEpisodes > 0 ? (float)TotalLosses / CompletedEpisodes : 0f;

        /// <summary>교착 비율 (0.0 ~ 1.0).</summary>
        public float StalemateRate => CompletedEpisodes > 0 ? (float)TotalStalemates / CompletedEpisodes : 0f;

        [Header("통계 설정")]
        [Tooltip("true이면 학습 시작 시 이전 통계를 불러온다. false이면 매번 0부터 시작한다.")]
        [SerializeField] private bool _persistStats = true;

        [Tooltip("실험 이름. mlagents-learn의 --run-id와 동일하게 설정한다. 실험별로 통계가 분리 저장된다.")]
        [SerializeField] private string _runId = "test01";

        /// <summary>PlayerPrefs 저장 키 접두사. _runId를 포함하여 실험별로 분리된다.</summary>
        private string StatsKeyPrefix => $"TrainingStats_{_runId}_";

        /// <summary>
        /// 현재 통계를 PlayerPrefs에 저장한다.
        /// 매 에피소드 종료 시 자동 호출된다.
        /// </summary>
        private void SaveStats()
        {
            if (!_persistStats) return;

            PlayerPrefs.SetInt(StatsKeyPrefix + "Episodes", CompletedEpisodes);
            PlayerPrefs.SetInt(StatsKeyPrefix + "Wins", TotalWins);
            PlayerPrefs.SetInt(StatsKeyPrefix + "Losses", TotalLosses);
            PlayerPrefs.SetInt(StatsKeyPrefix + "Stalemates", TotalStalemates);
            PlayerPrefs.Save();
        }

        /// <summary>
        /// PlayerPrefs에서 이전 통계를 불러온다.
        /// 학습 시작 시 호출된다.
        /// </summary>
        private void LoadStats()
        {
            if (!_persistStats) return;

            CompletedEpisodes = PlayerPrefs.GetInt(StatsKeyPrefix + "Episodes", 0);
            TotalWins = PlayerPrefs.GetInt(StatsKeyPrefix + "Wins", 0);
            TotalLosses = PlayerPrefs.GetInt(StatsKeyPrefix + "Losses", 0);
            TotalStalemates = PlayerPrefs.GetInt(StatsKeyPrefix + "Stalemates", 0);

            if (CompletedEpisodes > 0)
            {
                Debug.Log($"[TrainingManager] 이전 통계 불러옴 - " +
                          $"에피소드: {CompletedEpisodes}, " +
                          $"ML 승리: {TotalWins}회 ({WinRate:P1}), " +
                          $"상대 승리: {TotalLosses}회 ({LossRate:P1}), " +
                          $"교착: {TotalStalemates}회 ({StalemateRate:P1})");
            }
        }

        /// <summary>
        /// 저장된 통계를 초기화한다.
        /// 새로운 실험을 시작할 때 수동으로 호출한다.
        /// </summary>
        public void ResetStats()
        {
            CompletedEpisodes = 0;
            TotalWins = 0;
            TotalLosses = 0;
            TotalStalemates = 0;

            PlayerPrefs.DeleteKey(StatsKeyPrefix + "Episodes");
            PlayerPrefs.DeleteKey(StatsKeyPrefix + "Wins");
            PlayerPrefs.DeleteKey(StatsKeyPrefix + "Losses");
            PlayerPrefs.DeleteKey(StatsKeyPrefix + "Stalemates");
            PlayerPrefs.Save();

            Debug.Log("[TrainingManager] 통계 초기화 완료");
        }

        // ============================================================
        //  내부 상태
        // ============================================================

        /// <summary>현재 씬의 GridController 참조. 씬 로드 시 자동 탐색.</summary>
        private UnityGridController _gridController;

        /// <summary>현재 씬의 UnitManager 참조. 씬 로드 시 자동 탐색.</summary>
        private UnityUnitManager _unitManager;

        /// <summary>현재 에피소드에서 활성화된 ML Agent 목록.</summary>
        private List<BehaviourTreeAgent> _activeAgents = new List<BehaviourTreeAgent>();

        /// <summary>유닛 → Agent 매핑. 개인 보상 분배에 사용.</summary>
        private Dictionary<IUnit, BehaviourTreeAgent> _unitToAgentMap = new Dictionary<IUnit, BehaviourTreeAgent>();

        /// <summary>학습 진행 중 여부.</summary>
        private bool _isTraining;

        /// <summary>현재 에피소드의 턴 수.</summary>
        private int _currentTurnCount;

        /// <summary>교착 상태로 종료되었는지 여부. OnGameEnded에서 교착/정상 종료를 구분한다.</summary>
        private bool _isStalemateEnd;

        /// <summary>현재 학습 중인 씬의 이름. 씬 재로드 시 사용.</summary>
        private string _trainingSceneName;

        // ============================================================
        //  생명주기
        // ============================================================

        private void Awake()
        {
            // 싱글턴: 이미 인스턴스가 있으면 중복 생성된 것이므로 파괴
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;
            DontDestroyOnLoad(gameObject);

            // 이전 통계 불러오기
            LoadStats();

            // 씬 로드 콜백 등록
            SceneManager.sceneLoaded += OnSceneLoaded;

            // 현재 씬 이름 저장 (재로드에 사용)
            _trainingSceneName = SceneManager.GetActiveScene().name;
        }

        private void OnDestroy()
        {
            // 싱글턴 인스턴스가 자기 자신이 아닌 경우 (중복 생성된 경우) 이벤트 해제 안 함
            if (_instance != this) return;

            SaveStats();
            SceneManager.sceneLoaded -= OnSceneLoaded;
            _instance = null;
        }

        // ============================================================
        //  씬 로드 콜백
        // ============================================================

        /// <summary>
        /// 씬 로드 완료 시 호출된다.
        /// 첫 로드와 재로드 모두 이 콜백에서 에피소드를 시작한다.
        /// sceneLoaded는 모든 Awake/OnEnable 이후, Start 이전에 호출되므로
        /// TrainingMapRunner의 GameInitialized 구독이 이미 완료된 상태이다.
        /// </summary>
        /// <param name="scene">로드된 씬.</param>
        /// <param name="mode">씬 로드 모드.</param>
        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            // 학습 씬이 아닌 다른 씬이 로드된 경우 무시
            if (scene.name != _trainingSceneName) return;

            if (_autoStart || _isTraining)
            {
                // 1프레임 대기 후 에피소드 시작 (모든 씬 오브젝트의 Start 완료 대기)
                StartCoroutine(BeginEpisodeRoutine());
            }
        }

        // ============================================================
        //  학습 제어
        // ============================================================

        /// <summary>
        /// ML 학습을 시작한다.
        /// _autoStart가 false일 때 외부에서 수동으로 호출할 수 있다.
        /// </summary>
        public void StartTraining()
        {
            if (_isTraining)
            {
                Debug.LogWarning("[TrainingManager] 이미 학습이 진행 중입니다.");
                return;
            }

            _isTraining = true;
        }

        /// <summary>
        /// 학습을 중단한다.
        /// </summary>
        public void StopTraining()
        {
            _isTraining = false;
            UnsubscribeRewardEvents();

            Debug.Log($"[TrainingManager] 학습 중단. 총 {CompletedEpisodes}회 에피소드 완료. " +
                      $"ML 승리: {TotalWins}회 ({WinRate:P1}), " +
                      $"상대 승리: {TotalLosses}회 ({LossRate:P1}), " +
                      $"교착: {TotalStalemates}회 ({StalemateRate:P1})");
        }

        // ============================================================
        //  에피소드 관리
        // ============================================================

        /// <summary>
        /// 에피소드 시작 코루틴.
        /// 씬의 모든 오브젝트가 초기화된 후 (Start 완료 후) 게임을 시작한다.
        /// </summary>
        private IEnumerator BeginEpisodeRoutine()
        {
            // 모든 씬 오브젝트의 Start()가 완료될 때까지 1프레임 대기
            yield return null;

            _isTraining = true;
            _currentTurnCount = 0;
            _isStalemateEnd = false;

            // 씬에서 GridController와 UnitManager 자동 탐색
            _gridController = FindFirstObjectByType<UnityGridController>();
            _unitManager = FindFirstObjectByType<UnityUnitManager>();

            if (_gridController == null || _unitManager == null)
            {
                Debug.LogError("[TrainingManager] GridController 또는 UnitManager를 찾을 수 없습니다.");
                yield break;
            }

            // 게임 종료 이벤트 구독
            _gridController.GameEnded += OnGameEnded;

            // 게임 초기화 및 시작
            // → GameInitialized 이벤트 → TrainingMapRunner가 맵/유닛 생성
            _gridController.InitializeAndStart();

            // Agent 수집 + 보상 이벤트 구독
            CollectAgentsAndBuildMap();
            SubscribeRewardEvents();

            Debug.Log($"[TrainingManager] 에피소드 {CompletedEpisodes + 1} 시작. ML Agent {_activeAgents.Count}개 활성화.");
        }

        /// <summary>
        /// 다음 에피소드를 위해 씬을 재로드한다.
        /// 현재 프레임의 비동기 작업(AI 행동 트리 등)이 완료된 후 재로드하기 위해
        /// 1프레임 지연시킨다.
        /// </summary>
        private void LoadNextEpisode()
        {
            StartCoroutine(DelayedSceneReload());
        }

        /// <summary>
        /// 씬 재로드를 1프레임 지연시키는 코루틴.
        /// 현재 프레임에서 실행 중인 AI 행동 트리, 비동기 하이라이터 등이
        /// 완료된 후 씬을 재로드하여 MissingReferenceException을 방지한다.
        /// </summary>
        private IEnumerator DelayedSceneReload()
        {
            // 현재 프레임 끝까지 대기 (진행 중인 비동기 작업 완료)
            yield return new WaitForEndOfFrame();
            // 추가 1프레임 대기 (Destroy 완료 보장)
            yield return null;
            SceneManager.LoadScene(_trainingSceneName);
        }

        /// <summary>
        /// ML 플레이어 소속 유닛에서 BehaviourTreeAgent 컴포넌트를 수집하고 매핑을 구축한다.
        /// </summary>
        private void CollectAgentsAndBuildMap()
        {
            _activeAgents.Clear();
            _unitToAgentMap.Clear();

            var mlUnits = _gridController.UnitManager.GetFriendlyUnits(_mlPlayerNumber);
            foreach (var unit in mlUnits)
            {
                if (unit is Unit unityUnit)
                {
                    var agent = unityUnit.GetComponentInChildren<BehaviourTreeAgent>();
                    if (agent != null)
                    {
                        _activeAgents.Add(agent);
                        _unitToAgentMap[unit] = agent;
                    }
                }
            }
        }

        // ============================================================
        //  보상 분배 헬퍼
        // ============================================================

        /// <summary>
        /// 팀 전체에 보상을 분배한다.
        /// </summary>
        /// <param name="reward">모든 ML Agent에게 부여할 보상 값.</param>
        private void ApplyTeamReward(float reward)
        {
            foreach (var agent in _activeAgents)
            {
                if (agent != null) agent.ApplyIntermediateReward(reward);
            }
        }

        /// <summary>
        /// 특정 유닛의 Agent에게만 보상을 부여한다.
        /// </summary>
        /// <param name="unit">보상을 받을 유닛.</param>
        /// <param name="reward">해당 유닛의 Agent에게 부여할 보상 값.</param>
        private void ApplyIndividualReward(IUnit unit, float reward)
        {
            if (_unitToAgentMap.TryGetValue(unit, out var agent))
            {
                if (agent != null) agent.ApplyIntermediateReward(reward);
            }
        }

        // ============================================================
        //  보상 이벤트 구독/해제
        // ============================================================

        /// <summary>
        /// 보상 계산에 필요한 모든 게임 이벤트를 구독한다.
        /// </summary>
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

        /// <summary>
        /// 구독한 모든 게임 이벤트를 해제한다.
        /// </summary>
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

        /// <summary>[팀 보상] 유닛 사망 시.</summary>
        private void OnUnitRemoved(IUnit unit)
        {
            if (!_isTraining || _activeAgents.Count == 0) return;

            if (unit.PlayerNumber == _mlPlayerNumber)
            {
                ApplyTeamReward(_allyDeathPenalty);
                _unitToAgentMap.Remove(unit);
            }
            else
            {
                ApplyTeamReward(_enemyKilledReward);
            }
        }

        /// <summary>[개인 보상] 데미지 가함/받음.</summary>
        private void OnUnitAttacked(UnitAttackedEventArgs eventArgs)
        {
            if (!_isTraining || _activeAgents.Count == 0) return;

            if (eventArgs.AttackingUnit.PlayerNumber == _mlPlayerNumber
                && eventArgs.AffectedUnit.PlayerNumber != _mlPlayerNumber)
            {
                float damageRatio = eventArgs.AffectedUnit.MaxHealth > 0
                    ? eventArgs.DamageDealt / eventArgs.AffectedUnit.MaxHealth
                    : 0f;
                ApplyIndividualReward(eventArgs.AttackingUnit, _damageDealtRewardScale * damageRatio);
            }
            else if (eventArgs.AttackingUnit.PlayerNumber != _mlPlayerNumber
                     && eventArgs.AffectedUnit.PlayerNumber == _mlPlayerNumber)
            {
                float damageRatio = eventArgs.AffectedUnit.MaxHealth > 0
                    ? eventArgs.DamageDealt / eventArgs.AffectedUnit.MaxHealth
                    : 0f;
                ApplyIndividualReward(eventArgs.AffectedUnit, _damageTakenPenaltyScale * damageRatio);
            }
        }

        /// <summary>[개인 보상] 유리한 위치 이동.</summary>
        private void OnUnitMoved(UnitMovedEventArgs eventArgs)
        {
            if (!_isTraining || _activeAgents.Count == 0) return;
            if (eventArgs.AffectedUnit.PlayerNumber != _mlPlayerNumber) return;

            if (eventArgs.TargetCell.WorldPosition.y > eventArgs.SourceCell.WorldPosition.y)
            {
                ApplyIndividualReward(eventArgs.AffectedUnit, _advantageousPositionReward);
            }
        }

        /// <summary>[개인 보상] 턴 생존 + 교착 검사.</summary>
        private void OnTurnEnded(TurnTransitionParams turnTransitionParams)
        {
            if (!_isTraining || _activeAgents.Count == 0) return;

            _currentTurnCount++;

            if (turnTransitionParams.TurnContext.CurrentPlayer.PlayerNumber == _mlPlayerNumber)
            {
                foreach (var kvp in _unitToAgentMap)
                {
                    if (kvp.Value != null) kvp.Value.ApplyIntermediateReward(_turnSurvivalBonus);
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
            Debug.Log($"<color=orange>[TrainingManager] 교착 상태 감지! {_currentTurnCount}턴 경과. 에피소드 강제 종료.</color>");

            // 교착 패널티를 먼저 부여
            foreach (var agent in _activeAgents)
            {
                if (agent != null) agent.ApplyIntermediateReward(_stalematePenalty);
            }

            // 교착 플래그 설정 후 프레임워크의 정상 종료 흐름을 호출
            // 승자 없이 GameEnded를 호출하면 DominationVictoryCondition과 동일한 흐름을 탄다
            _isStalemateEnd = true;
            _gridController.InvokeGameEnded(new GameResult(
                new List<IPlayer>(),
                new List<IPlayer>()
            ));
        }

        // ============================================================
        //  게임 종료 처리
        // ============================================================

        /// <summary>[팀 보상] 게임 종료 시 최종 보상.</summary>
        private void OnGameEnded(GameResult gameResult)
        {
            if (!_isTraining) return;

            UnsubscribeRewardEvents();

            string episodeResult;

            if (_isStalemateEnd)
            {
                // 교착 종료: 교착 패널티는 ForceEndStalemate에서 이미 부여됨
                foreach (var agent in _activeAgents)
                {
                    if (agent != null) agent.EndEpisode();
                }

                TotalStalemates++;
                episodeResult = "교착";
                _isStalemateEnd = false;
            }
            else
            {
                // 정상 종료: 승패 보상 부여
                bool mlWon = gameResult.Winners != null && gameResult.Winners.Any(p => p.PlayerNumber == _mlPlayerNumber);

                if (mlWon)
                {
                    TotalWins++;
                    episodeResult = "ML 승리";
                }
                else
                {
                    TotalLosses++;
                    episodeResult = "상대 승리";
                }

                float averageHealthRatio = 0f;
                var aliveUnits = _gridController?.UnitManager?.GetFriendlyUnits(_mlPlayerNumber);
                if (aliveUnits != null && aliveUnits.Any())
                {
                    averageHealthRatio = aliveUnits.Average(u => u.MaxHealth > 0 ? u.Health / u.MaxHealth : 0f);
                }

                foreach (var agent in _activeAgents)
                {
                    if (agent != null) agent.ApplyGameResult(mlWon, averageHealthRatio);
                }
            }

            CompletedEpisodes++;
            SaveStats();

            // 통합 로그 출력
            Debug.Log($"<color=yellow>[TrainingManager] ===== 에피소드 {CompletedEpisodes} 종료 =====</color>\n" +
                      $"  결과: {episodeResult} | 턴 수: {_currentTurnCount}\n" +
                      $"  ML 승리: {TotalWins}회 ({WinRate:P1}) | " +
                      $"상대 승리: {TotalLosses}회 ({LossRate:P1}) | " +
                      $"교착: {TotalStalemates}회 ({StalemateRate:P1})");

            // 파일 로그 (100 에피소드마다 기록)
            if (CompletedEpisodes % 100 == 0)
            {
                WriteLogToFile(episodeResult);
            }

            if (_maxEpisodes > 0 && CompletedEpisodes >= _maxEpisodes)
            {
                StopTraining();
                Debug.Log($"[TrainingManager] 목표 에피소드 수 ({_maxEpisodes}) 달성. 학습 완료.");
                return;
            }

            LoadNextEpisode();
        }

        // ============================================================
        //  파일 로그 (빌드 환경 모니터링용)
        // ============================================================

        /// <summary>
        /// 에피소드 결과를 텍스트 파일에 기록한다.
        /// 빌드 환경에서 Debug.Log를 확인할 수 없으므로
        /// 프로젝트 루트의 training_log_{runId}.txt에 기록한다.
        /// 다중 환경에서 각 인스턴스가 같은 파일에 append하므로
        /// 모든 환경의 결과가 합산되어 기록된다.
        /// </summary>
        private void WriteLogToFile(string episodeResult)
        {
            try
            {
                string logPath = Path.Combine(Application.dataPath, "..", $"training_log_{_runId}.txt");
                string logLine = $"[{System.DateTime.Now:HH:mm:ss}] " +
                                 $"에피소드 {CompletedEpisodes} | {episodeResult} | 턴 {_currentTurnCount} | " +
                                 $"ML {TotalWins}승 ({WinRate:P1}) | " +
                                 $"상대 {TotalLosses}승 ({LossRate:P1}) | " +
                                 $"교착 {TotalStalemates}회 ({StalemateRate:P1})";
                File.AppendAllText(logPath, logLine + "\n");
            }
            catch (System.Exception)
            {
                // 파일 쓰기 실패 시 무시 (학습에 영향 없음)
            }
        }
    }
}