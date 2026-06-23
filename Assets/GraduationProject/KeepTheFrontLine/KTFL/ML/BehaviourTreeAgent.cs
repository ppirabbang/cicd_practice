using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;
using System.Linq;
using TurnBasedStrategyFramework.Common.Cells;
using TurnBasedStrategyFramework.Common.Controllers;
using TurnBasedStrategyFramework.Common.Units;
using UnityEngine;

namespace TurnBasedStrategyFramework.ML
{
    /// <summary>
    /// ML-Agents의 Agent를 상속하여 행동 트리의 Evaluator 가중치를 학습하는 에이전트.
    /// 
    /// 동작 흐름:
    /// 1. 유닛이 행동하기 직전에 MLBehaviourTreeResource가 RequestWeights()를 호출
    /// 2. Agent가 현재 게임 상태를 관찰 (CollectObservations)
    /// 3. ML 모델(또는 Heuristic)이 관찰값을 기반으로 가중치를 출력 (OnActionReceived)
    /// 4. 출력된 가중치가 행동 트리의 Evaluator에 전달되어 AI 행동을 결정
    /// 5. 게임 이벤트에 따라 보상을 부여하여 모델을 학습
    /// 
    /// [확장 가이드]
    /// 새로운 관찰값을 추가할 때:
    /// 1. CollectUnitObservations() 또는 CollectBattlefieldObservations()에 sensor.AddObservation() 추가
    /// 2. OBSERVATION_COUNT 상수 업데이트
    /// 3. Unity 에디터의 Behavior Parameters 컴포넌트에서 Vector Observation Size 업데이트
    /// 
    /// 사전 요구사항:
    /// - Unity ML-Agents 패키지가 설치되어 있어야 한다 (com.unity.ml-agents)
    /// </summary>
    public class BehaviourTreeAgent : Agent
    {
        // ============================================================
        //  상수
        // ============================================================

        /// <summary>
        /// 총 관찰값 개수. CollectObservations에서 추가하는 값의 수와 일치해야 한다.
        /// Unity 에디터의 Behavior Parameters > Vector Observation > Space Size에도 이 값을 설정한다.
        /// 
        /// [확장 시] 관찰값을 추가하면 이 값을 함께 증가시킨다.
        /// </summary>
        public const int OBSERVATION_COUNT = 13;

        /// <summary>
        /// 관찰값 정규화에 사용하는 최대 유닛 수 기준.
        /// 게임 내 한 진영의 최대 유닛 수에 맞게 조정한다.
        /// </summary>
        private const float MAX_UNITS_FOR_NORMALIZATION = 10f;

        /// <summary>
        /// 공격 사거리 정규화에 사용하는 최대 사거리 기준.
        /// </summary>
        private const float MAX_ATTACK_RANGE = 10f;

        /// <summary>
        /// 거리 정규화에 사용하는 맵 최대 거리 기준.
        /// 맵 크기에 맞게 조정한다.
        /// </summary>
        private const float MAX_MAP_DISTANCE = 20f;

        /// <summary>
        /// 이동 가능 셀 수 정규화에 사용하는 최대 셀 수 기준.
        /// </summary>
        private const float MAX_REACHABLE_CELLS = 30f;

        /// <summary>
        /// 높이 정규화에 사용하는 최대 높이 기준.
        /// </summary>
        private const float MAX_HEIGHT = 10f;

        // ============================================================
        //  참조 및 설정
        // ============================================================

        /// <summary>
        /// 이 Agent가 제어하는 유닛. Setup()에서 설정된다.
        /// </summary>
        private IUnit _unit;

        /// <summary>
        /// 게임 상태를 조회하기 위한 그리드 컨트롤러. Setup()에서 설정된다.
        /// </summary>
        private IGridController _gridController;

        [Header("설정")]
        [Tooltip("Decay, Threshold 등 ML이 제어하지 않는 고정 파라미터의 기본값.")]
        [SerializeField] private EvaluatorWeights _fixedDefaults = EvaluatorWeights.Default;

        // ============================================================
        //  내부 상태
        // ============================================================

        /// <summary>
        /// 가장 최근에 ML 모델이 출력한 가중치 값.
        /// [수정] 초기값을 Default로 설정하여 첫 응답 전에도 합리적인 행동을 보장한다.
        /// </summary>
        private EvaluatorWeights _lastWeights = EvaluatorWeights.Default;

        /// <summary>
        /// RequestWeights 호출 시 Agent가 결정을 완료했는지 추적하는 플래그.
        /// </summary>
        private bool _weightsReady;

        // ============================================================
        //  초기화
        // ============================================================

        /// <summary>
        /// Agent를 초기화한다. 유닛과 그리드 컨트롤러의 참조를 설정한다.
        /// MLBehaviourTreeResource.Initialize()에서 호출된다.
        /// </summary>
        /// <param name="unit">이 Agent가 제어할 유닛.</param>
        /// <param name="gridController">게임 상태를 관리하는 그리드 컨트롤러.</param>
        public void Setup(IUnit unit, IGridController gridController)
        {
            _unit = unit;
            _gridController = gridController;
        }

        /// <summary>
        /// 에피소드가 시작될 때 호출된다.
        /// 가중치를 기본값으로 초기화하고 내부 상태를 리셋한다.
        /// </summary>
        public override void OnEpisodeBegin()
        {
            _lastWeights = EvaluatorWeights.Default;
            _weightsReady = false;
        }

        // ============================================================
        //  관찰값 수집 (Observations)
        // ============================================================

        /// <summary>
        /// 현재 게임 상태를 관찰값으로 수집한다.
        /// ML 모델은 이 관찰값을 입력받아 최적의 가중치를 출력한다.
        /// 
        /// 총 13개의 관찰값을 수집한다:
        /// [유닛 자체 상태 - 4개]
        ///   1. 체력 비율 (0.0 ~ 1.0)
        ///   2. 이동력 비율 (0.0 ~ 1.0)
        ///   3. 행동력 비율 (0.0 ~ 1.0)
        ///   4. 공격 사거리 (정규화)
        /// [전장 유닛 상태 - 4개]
        ///   5. 주변 아군 유닛 수 (정규화)
        ///   6. 주변 적 유닛 수 (정규화)
        ///   7. 전체 아군 유닛 수 (정규화)
        ///   8. 전체 적 유닛 수 (정규화)
        /// [공간/지형 상태 - 5개]
        ///   9. 이동 가능 셀 수 (정규화)
        ///   10. 현재 위치에서 공격 가능 적 수 (정규화)
        ///   11. 가장 가까운 적까지 거리 (정규화)
        ///   12. 현재 셀 이동 비용 (정규화)
        ///   13. 주변 셀 평균 높이 (정규화)
        /// </summary>
        /// <param name="sensor">관찰값을 추가할 센서 객체.</param>
        public override void CollectObservations(VectorSensor sensor)
        {
            if (_unit == null || _gridController == null)
            {
                // 참조가 아직 설정되지 않았으면 0으로 채운다
                for (int i = 0; i < OBSERVATION_COUNT; i++)
                {
                    sensor.AddObservation(0f);
                }
                return;
            }

            CollectUnitObservations(sensor);
            CollectBattlefieldObservations(sensor);
            CollectSpatialObservations(sensor);
        }

        /// <summary>
        /// 유닛 자체의 상태를 관찰값으로 수집한다. (4개)
        /// 
        /// [확장 시] 유닛의 새로운 속성(예: 리더쉽)을 관찰값으로 추가하려면 이 메서드에 추가한다.
        /// </summary>
        /// <param name="sensor">관찰값을 추가할 센서 객체.</param>
        private void CollectUnitObservations(VectorSensor sensor)
        {
            // 1. 체력 비율
            float healthRatio = _unit.MaxHealth > 0 ? _unit.Health / _unit.MaxHealth : 0f;
            sensor.AddObservation(healthRatio);

            // 2. 이동력 비율
            float movementRatio = _unit.MaxMovementPoints > 0 ? _unit.MovementPoints / _unit.MaxMovementPoints : 0f;
            sensor.AddObservation(movementRatio);

            // 3. 행동력 비율
            float actionRatio = _unit.MaxActionPoints > 0 ? _unit.ActionPoints / _unit.MaxActionPoints : 0f;
            sensor.AddObservation(actionRatio);

            // 4. 공격 사거리 (정규화)
            float attackRangeNormalized = _unit.AttackRange / MAX_ATTACK_RANGE;
            sensor.AddObservation(attackRangeNormalized);
        }

        /// <summary>
        /// 전장의 유닛 분포 상태를 관찰값으로 수집한다. (4개)
        /// 
        /// [확장 시] 전장 상태에 대한 새로운 관찰값(예: 아군 평균 체력)을 추가하려면 이 메서드에 추가한다.
        /// </summary>
        /// <param name="sensor">관찰값을 추가할 센서 객체.</param>
        private void CollectBattlefieldObservations(VectorSensor sensor)
        {
            var friendlyUnits = _gridController.UnitManager.GetFriendlyUnits(_unit.PlayerNumber).ToList();
            var enemyUnits = _gridController.UnitManager.GetEnemyUnits(_unit.PlayerNumber).ToList();

            // 주변 유닛 수 계산 (공격 사거리 × 2 범위 내)
            int nearbyRange = _unit.AttackRange * 2;

            // 5. 주변 아군 수
            int nearbyFriendlyCount = friendlyUnits
                .Count(u => !u.Equals(_unit) && u.CurrentCell != null && _unit.CurrentCell != null
                    && u.CurrentCell.GetDistance(_unit.CurrentCell) <= nearbyRange);
            sensor.AddObservation(nearbyFriendlyCount / MAX_UNITS_FOR_NORMALIZATION);

            // 6. 주변 적 수
            int nearbyEnemyCount = enemyUnits
                .Count(u => u.CurrentCell != null && _unit.CurrentCell != null
                    && u.CurrentCell.GetDistance(_unit.CurrentCell) <= nearbyRange);
            sensor.AddObservation(nearbyEnemyCount / MAX_UNITS_FOR_NORMALIZATION);

            // 7. 전체 아군 수
            sensor.AddObservation(friendlyUnits.Count / MAX_UNITS_FOR_NORMALIZATION);

            // 8. 전체 적 수
            sensor.AddObservation(enemyUnits.Count / MAX_UNITS_FOR_NORMALIZATION);
        }

        /// <summary>
        /// 공간 및 지형 관련 상태를 관찰값으로 수집한다. (5개)
        /// 
        /// [확장 시] 지형에 대한 새로운 관찰값(예: 주변 장애물 셀 비율)을 추가하려면 이 메서드에 추가한다.
        /// </summary>
        /// <param name="sensor">관찰값을 추가할 센서 객체.</param>
        private void CollectSpatialObservations(VectorSensor sensor)
        {
            // [추가] CurrentCell이 아직 설정되지 않았으면 기본값으로 채운다
            if (_unit.CurrentCell == null)
            {
                sensor.AddObservation(0f); // 9. 이동 가능 셀 수
                sensor.AddObservation(0f); // 10. 공격 가능 적 수
                sensor.AddObservation(1f); // 11. 최근접 적 거리 (최대)
                sensor.AddObservation(0f); // 12. 이동 비용
                sensor.AddObservation(0f); // 13. 주변 평균 높이
                return;
            }

            var allCells = _gridController.CellManager.GetCells();
            var enemyUnits = _gridController.UnitManager.GetEnemyUnits(_unit.PlayerNumber).ToList();

            // 9. 이동 가능 셀 수
            int reachableCellCount = 0;
            try
            {
                reachableCellCount = _unit.GetAvailableDestinations(allCells).Count();
            }
            catch (System.Exception)
            {
                // MoveComponent 내부 초기화 전이면 기본값 사용
            }
            sensor.AddObservation(reachableCellCount / MAX_REACHABLE_CELLS);

            // 10. 현재 위치에서 공격 가능 적 수
            int attackableCount = 0;
            if (_unit.CurrentCell != null)
            {
                try
                {
                    attackableCount = enemyUnits
                        .Count(u => u.CurrentCell != null
                            && _unit.IsUnitAttackable(u, u.CurrentCell, _unit.CurrentCell));
                }
                catch (System.Exception)
                {
                    // 초기화 전이면 기본값 사용
                }
            }
            sensor.AddObservation(attackableCount / MAX_UNITS_FOR_NORMALIZATION);

            // 11. 가장 가까운 적까지 거리
            float closestEnemyDistance = MAX_MAP_DISTANCE;
            if (_unit.CurrentCell != null && enemyUnits.Any())
            {
                try
                {
                    closestEnemyDistance = enemyUnits
                        .Where(u => u.CurrentCell != null)
                        .Select(u => (float)u.CurrentCell.GetDistance(_unit.CurrentCell))
                        .DefaultIfEmpty(MAX_MAP_DISTANCE)
                        .Min();
                }
                catch (System.Exception) { }
            }
            sensor.AddObservation(closestEnemyDistance / MAX_MAP_DISTANCE);

            // 12. 현재 셀 이동 비용
            float movementCost = 1f;
            try
            {
                movementCost = _unit.CurrentCell != null ? _unit.CurrentCell.MovementCost : 1f;
            }
            catch (System.Exception) { }
            sensor.AddObservation(movementCost / 5f);

            // 13. 주변 셀 평균 높이
            float avgHeight = 0f;
            if (_unit.CurrentCell != null)
            {
                try
                {
                    var nearbyCells = allCells
                        .Where(c => c.GetDistance(_unit.CurrentCell) <= _unit.AttackRange);
                    if (nearbyCells.Any())
                    {
                        avgHeight = nearbyCells.Average(c => c.WorldPosition.y);
                    }
                }
                catch (System.Exception) { }
            }
            sensor.AddObservation(avgHeight / MAX_HEIGHT);
        }

        // ============================================================
        //  행동 수신 (Actions)
        // ============================================================

        /// <summary>
        /// ML 모델이 결정한 행동(가중치 값)을 수신한다.
        /// continuous action 6개를 EvaluatorWeights로 변환하여 저장한다.
        /// 
        /// Action 매핑:
        ///   actions[0]: DamageDealtPositionWeight
        ///   actions[1]: DamageReceivedPositionWeight
        ///   actions[2]: DistancePositionWeight
        ///   actions[3]: HeightPositionWeight
        ///   actions[4]: HealthTargetWeight
        ///   actions[5]: DamageGivenTargetWeight
        /// 
        /// [확장 시] 새 Evaluator 가중치를 추가하면 다음 인덱스에서 읽도록 수정한다.
        /// </summary>
        /// <param name="actions">ML 모델이 출력한 행동 버퍼. ContinuousActions에 float가 담겨 있다.</param>
        public override void OnActionReceived(ActionBuffers actions)
        {
            float[] weightValues = new float[EvaluatorWeights.MLActionCount];
            bool allZero = true;

            for (int i = 0; i < EvaluatorWeights.MLActionCount; i++)
            {
                weightValues[i] = actions.ContinuousActions[i];
                if (weightValues[i] != 0f) allZero = false;
            }

            // [추가] 씬 로드 직후 Academy가 Python 응답 전에 보내는 기본값(all 0) 응답을 무시한다.
            // 모델이 학습 중 6개 값을 정확히 전부 0.000000으로 출력할 확률은 사실상 0이므로
            // 안전한 판별 기준이다. 이 경우 _weightsReady를 설정하지 않아
            // RequestWeightsAsync가 진짜 응답을 계속 대기한다.
            if (allZero) return;

            _lastWeights = EvaluatorWeights.FromMLActions(weightValues, _fixedDefaults);
            _weightsReady = true;
        }

        /// <summary>
        /// ML 모델 없이 수동으로 테스트할 때 사용되는 휴리스틱 행동.
        /// 기본 가중치 값을 action 버퍼에 채워넣는다.
        /// Behavior Parameters에서 Behavior Type을 "Heuristic Only"로 설정하면 이 메서드가 호출된다.
        /// </summary>
        /// <param name="actionsOut">채워넣을 행동 버퍼.</param>
        public override void Heuristic(in ActionBuffers actionsOut)
        {
            var defaultActions = EvaluatorWeights.Default.ToMLActions();
            var continuousActions = actionsOut.ContinuousActions;
            for (int i = 0; i < EvaluatorWeights.MLActionCount; i++)
            {
                continuousActions[i] = defaultActions[i];
            }
        }

        // ============================================================
        //  가중치 요청
        // ============================================================

        /// <summary>
        /// MLBehaviourTreeResource에서 호출하여 현재 상황에 맞는 가중치를 비동기적으로 요청한다.
        /// 유닛이 행동하기 직전(BehaviourTree.Execute 직전)에 매번 호출된다.
        /// 
        /// Agent에게 결정을 요청하고 (RequestDecision), Python 프로세스가
        /// 관찰값을 받아 최적의 가중치를 계산하여 돌려줄 때까지 대기한다.
        /// 턴제 게임이므로 대기해도 게임 진행에 문제없다.
        /// </summary>
        /// <returns>현재 게임 상태에 기반하여 ML 모델이 결정한 Evaluator 가중치.</returns>
        public async Awaitable<EvaluatorWeights> RequestWeightsAsync()
        {
            _weightsReady = false;
            RequestDecision();

            // Python 프로세스로부터 응답(OnActionReceived)이 올 때까지 대기
            // 추론 모드(.onnx 로드)에서는 즉시 완료되고,
            // 학습 모드(Python 통신)에서는 1~2프레임 대기할 수 있다
            int maxWaitFrames = 300; // 무한 대기 방지 (약 5초 at 60fps)
            int waitedFrames = 0;

            while (!_weightsReady && waitedFrames < maxWaitFrames)
            {
                await Awaitable.NextFrameAsync();
                waitedFrames++;
            }

            if (_weightsReady)
            {
                return _lastWeights;
            }

            // 타임아웃 시 기본값 반환
            Debug.LogWarning($"[BehaviourTreeAgent] 가중치 응답 타임아웃 ({waitedFrames}프레임 대기). 기본값을 반환합니다.");
            return EvaluatorWeights.Default;
        }

        // ============================================================
        //  보상 (Rewards)
        // ============================================================

        /// <summary>
        /// 게임 결과에 따라 최종 보상을 부여하고 에피소드를 종료한다.
        /// TrainingManager에서 게임 종료 시 호출한다.
        /// </summary>
        /// <param name="won">이 Agent의 유닛이 속한 팀이 승리했는지 여부.</param>
        /// <param name="averageHealthRatio">게임 종료 시 아군 유닛들의 평균 체력 비율 (0.0 ~ 1.0).</param>
        public void ApplyGameResult(bool won, float averageHealthRatio)
        {
            if (won)
            {
                // 승리 보상: 기본 1.0 + 체력 잔여분 보너스 (최대 0.5)
                AddReward(1.0f + averageHealthRatio * 0.5f);
            }
            else
            {
                // 패배 페널티
                AddReward(-1.0f);
            }

            EndEpisode();
        }

        /// <summary>
        /// 전투 중 발생하는 중간 보상을 부여한다.
        /// TrainingManager가 게임 이벤트(데미지, 처치, 이동 등)를 감지하여 호출한다.
        /// 
        /// [확장 시] 새로운 보상 이벤트는 TrainingManager에서 이 메서드를 호출하도록 추가한다.
        /// </summary>
        /// <param name="reward">부여할 보상 값. 양수면 긍정적, 음수면 부정적 보상.</param>
        public void ApplyIntermediateReward(float reward)
        {
            AddReward(reward);
        }
    }
}