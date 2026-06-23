using System;

namespace TurnBasedStrategyFramework.ML
{
    /// <summary>
    /// 행동 트리의 각 Evaluator에 전달되는 가중치 값을 담는 데이터 구조체.
    /// WeightProfile, BehaviourTreeAgent, MLBehaviourTreeResource 간의 데이터 전달에 사용된다.
    /// 
    /// [확장 가이드]
    /// 새로운 Evaluator를 추가할 때:
    /// 1. 이 구조체에 새 가중치 필드를 추가
    /// 2. Default 프로퍼티에 기본값 설정
    /// 3. ML이 제어할 값이면 MLActionCount 증가 + FromMLActions/ToMLActions 수정
    /// 4. 고정값(Decay, Threshold 등)이면 FromMLActions의 defaults에서 가져오도록 설정
    /// </summary>
    [Serializable]
    public struct EvaluatorWeights
    {
        // ============================================================
        //  위치 평가 가중치 (Position Evaluators)
        //  MoveActionNode에서 이동 목적지를 결정할 때 사용
        // ============================================================

        /// <summary>
        /// 해당 위치에서 적에게 가할 수 있는 데미지를 평가하는 가중치.
        /// 양수일수록 공격적 위치를 선호한다.
        /// </summary>
        public float DamageDealtPositionWeight;

        /// <summary>
        /// DamageDealtPositionEvaluator의 거리 감쇠율.
        /// 0에 가까울수록 먼 셀의 영향이 유지되고, 1에 가까울수록 가까운 셀만 반영된다.
        /// [고정값] ML이 제어하지 않음.
        /// </summary>
        public float DamageDealtPositionDecay;

        /// <summary>
        /// 해당 위치에서 적으로부터 받을 수 있는 데미지를 평가하는 가중치.
        /// 음수로 설정하면 피해를 적게 받는 위치를 선호한다.
        /// </summary>
        public float DamageReceivedPositionWeight;

        /// <summary>
        /// DamageReceivedPositionEvaluator의 거리 감쇠율.
        /// [고정값] ML이 제어하지 않음.
        /// </summary>
        public float DamageReceivedPositionDecay;

        /// <summary>
        /// 현재 위치로부터의 거리를 평가하는 가중치.
        /// 음수로 설정하면 가까운 위치를 선호한다.
        /// </summary>
        public float DistancePositionWeight;

        /// <summary>
        /// DistancePositionEvaluator에서 사용하는 거리 임계값.
        /// 이 값을 초과하는 거리의 셀은 평가 점수가 0이 된다.
        /// [고정값] ML이 제어하지 않음.
        /// </summary>
        public int DistancePositionThreshold;

        /// <summary>
        /// 셀의 높이(지형 고도)를 평가하는 가중치.
        /// 양수일수록 높은 지형을 선호한다.
        /// </summary>
        public float HeightPositionWeight;

        // ============================================================
        //  타겟 평가 가중치 (Target Evaluators)
        //  AttackActionNode에서 공격 대상을 결정할 때 사용
        // ============================================================

        /// <summary>
        /// 적 유닛의 현재 체력을 기반으로 타겟 우선순위를 평가하는 가중치.
        /// 양수이면 체력이 낮은 적을 우선 공격한다.
        /// </summary>
        public float HealthTargetWeight;

        /// <summary>
        /// 적 유닛에게 가할 수 있는 데미지를 기반으로 타겟 우선순위를 평가하는 가중치.
        /// 양수이면 더 큰 데미지를 줄 수 있는 적을 우선 공격한다.
        /// </summary>
        public float DamageGivenTargetWeight;

        // ============================================================
        //  [확장 포인트] 새로운 Evaluator의 가중치 필드를 여기에 추가
        //  예: public float NewEvaluatorWeight;
        // ============================================================

        /// <summary>
        /// 프레임워크의 기본 가중치 값을 반환한다.
        /// ClashOfHeroesBehaviourTree의 기본 SerializeField 값과 동일하다.
        /// </summary>
        public static EvaluatorWeights Default => new EvaluatorWeights
        {
            DamageDealtPositionWeight = 1f,
            DamageDealtPositionDecay = 0.5f,

            DamageReceivedPositionWeight = -0.1f,
            DamageReceivedPositionDecay = 0.5f,

            DistancePositionWeight = -0.1f,
            DistancePositionThreshold = 10,

            HeightPositionWeight = 0.2f,

            HealthTargetWeight = 1f,
            DamageGivenTargetWeight = 1f,
        };

        /// <summary>
        /// ML Agent가 출력해야 하는 continuous action의 개수.
        /// Decay, Threshold 같은 고정 파라미터는 제외하고 가중치 값만 포함한다.
        /// 
        /// [확장 시] 새 Evaluator의 가중치를 ML이 제어하려면 이 값을 1 증가시킨다.
        /// </summary>
        public static int MLActionCount => 6;

        /// <summary>
        /// ML Agent가 출력한 float 배열을 EvaluatorWeights로 변환한다.
        /// 가중치 값은 ML 출력에서 가져오고, Decay/Threshold 같은 고정 파라미터는 defaults에서 가져온다.
        /// 
        /// [확장 시] 새 가중치를 추가하면 actions 배열의 다음 인덱스에서 값을 읽도록 수정한다.
        /// </summary>
        /// <param name="actions">ML Agent가 출력한 float 배열. 길이는 MLActionCount와 같아야 한다.</param>
        /// <param name="defaults">Decay, Threshold 등 고정 파라미터를 제공하는 기본값 구조체.</param>
        /// <returns>ML 출력값과 고정 파라미터가 결합된 EvaluatorWeights.</returns>
        public static EvaluatorWeights FromMLActions(float[] actions, EvaluatorWeights defaults)
        {
            return new EvaluatorWeights
            {
                // ML이 제어하는 가중치 (actions 배열에서 읽음)
                DamageDealtPositionWeight = actions[0],
                DamageReceivedPositionWeight = actions[1],
                DistancePositionWeight = actions[2],
                HeightPositionWeight = actions[3],
                HealthTargetWeight = actions[4],
                DamageGivenTargetWeight = actions[5],

                // 고정 파라미터 (defaults에서 복사)
                DamageDealtPositionDecay = defaults.DamageDealtPositionDecay,
                DamageReceivedPositionDecay = defaults.DamageReceivedPositionDecay,
                DistancePositionThreshold = defaults.DistancePositionThreshold,

                // [확장 포인트] 새 가중치: NewEvaluatorWeight = actions[6],
            };
        }

        /// <summary>
        /// ML이 제어하는 가중치 값만 float 배열로 내보낸다.
        /// 학습 결과를 저장하거나, 현재 가중치를 로그로 출력할 때 사용한다.
        /// 
        /// [확장 시] 새 가중치를 배열 끝에 추가한다.
        /// </summary>
        /// <returns>ML 제어 대상 가중치를 담은 float 배열. 길이는 MLActionCount와 같다.</returns>
        public readonly float[] ToMLActions()
        {
            return new float[]
            {
                DamageDealtPositionWeight,
                DamageReceivedPositionWeight,
                DistancePositionWeight,
                HeightPositionWeight,
                HealthTargetWeight,
                DamageGivenTargetWeight,
                // [확장 포인트] 새 가중치 추가: NewEvaluatorWeight,
            };
        }
    }
}
