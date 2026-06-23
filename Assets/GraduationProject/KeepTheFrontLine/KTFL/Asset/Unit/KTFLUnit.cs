using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TurnBasedStrategyFramework.Common.Cells;
using TurnBasedStrategyFramework.Common.Controllers;
using TurnBasedStrategyFramework.Common.Units;
using TurnBasedStrategyFramework.Unity.Cells;
using TurnBasedStrategyFramework.Unity.Examples.ClashOfHeroes.Cells;
using TurnBasedStrategyFramework.Unity.Units;
using TurnBasedStrategyFramework.Unity.Utilities;
using UnityEngine;

namespace TurnBasedStrategyFramework.Unity.Examples.ClashOfHeroes.Units
{
    // ============================================================
    //  6각형 방향 열거형
    // ============================================================

    /// <summary>
    /// 6각형 그리드에서 유닛이 바라볼 수 있는 6가지 방향.
    /// 각 방향은 60도 간격이며, Y축 회전 각도에 매핑된다.
    /// 
    ///        1 (60°)    0 (0°)
    ///           \       /
    ///   2 (120°) --- ● --- 5 (300°)
    ///           /       \
    ///        3 (180°)   4 (240°)
    /// </summary>
    public enum HexDirection
    {
        Dir0 = 0,   //   0도
        Dir1 = 1,   //  60도
        Dir2 = 2,   // 120도
        Dir3 = 3,   // 180도
        Dir4 = 4,   // 240도
        Dir5 = 5,   // 300도
    }

    // ============================================================
    //  KTFLUnit 클래스
    // ============================================================

    /// <summary>
    /// Represents a unit in the Clash of Heroes demo.
    /// 6각형 방향 시스템과 후방 공격 기믹을 포함한다.
    /// 
    /// [프리팹 설정 필수사항]
    /// 다음 하이라이터를 유닛 프리팹의 해당 리스트에서 제거해야 한다:
    /// - DirectionRotationHighlighter: _unMarkFn 리스트에서 제거 (이동/공격 후 고정 방향으로 리셋하는 원인)
    /// - FaceEnemyHighlighter: _markAsDefendingFn 리스트에서 제거 (피격 시 공격자를 바라보는 원인)
    /// 
    /// 이 두 하이라이터의 역할은 HexDirection 기반 방향 시스템이 대체한다.
    /// </summary>
    public class KTFLUnit : Unit, IUnitDetails, ITurnAbilityLimit
    {
        // ============================================================
        //  기존 필드
        // ============================================================

        [SerializeField] private Transform _unitModel;
        [SerializeField] private string _unitName;
        [SerializeField] private Sprite _unitPortrait;
        [SerializeField] private int _maxAbilityUsesPerTurn;
        [SerializeField] private ScriptableObject _waterCellType;

        // ============================================================
        //  [추가] 방향 시스템 필드
        // ============================================================

        [Header("방향 시스템")]
        [Tooltip("유닛의 초기 바라보는 방향 (0~5). 프리팹에서 설정한다.")]
        [SerializeField] private HexDirection _initialFacingDirection = HexDirection.Dir0;

        /// <summary>
        /// 유닛이 현재 바라보고 있는 6각형 방향.
        /// 이동 완료 시 마지막 이동 방향으로, 공격 시 대상 방향으로 업데이트된다.
        /// 이 값이 유닛 방향의 유일한 진실 원천(single source of truth)이다.
        /// </summary>
        private HexDirection _currentFacingDirection;

        // ============================================================
        //  [추가] 후방 공격 설정
        // ============================================================

        [Header("후방 공격 설정")]
        [Tooltip("후방 공격 시 적용되는 데미지 배율. 1.5이면 150%의 데미지를 가한다.")]
        [SerializeField] private float _backstabDamageMultiplier = 1.5f;

        [Tooltip("후방으로 판정하는 방향 수. 0이면 정후방 1방향만, 1이면 정후방+좌우후방 (총 3방향).")]
        [SerializeField] private int _backstabArcSize = 1;

        // ============================================================
        //  기존 프로퍼티
        // ============================================================

        public string UnitName { get => _unitName; set => _unitName = value; }
        public Sprite UnitPortrait { get => _unitPortrait; set => _unitPortrait = value; }
        public int AbilityUsePoints { get; set; }

        /// <summary>
        /// [추가] 유닛이 현재 바라보고 있는 6각형 방향.
        /// 외부에서 후방 공격 판정 등에 사용한다.
        /// </summary>
        public HexDirection CurrentFacingDirection => _currentFacingDirection;

        /// <summary>
        /// [추가] 유닛의 바라보는 방향을 외부에서 설정한다.
        /// Initialize 이후에 호출하여 프리팹의 기본 방향을 덮어쓸 수 있다.
        /// 주로 TrainingUnitSpawner에서 상대 진영을 바라보도록 설정할 때 사용한다.
        /// </summary>
        /// <param name="direction">설정할 6각형 방향.</param>
        public void SetFacingDirection(HexDirection direction)
        {
            _currentFacingDirection = direction;
            ApplyFacingRotation();
        }

        // ============================================================
        //  초기화
        // ============================================================

        /// <summary>
        /// 유닛 생성 시 초기 방향을 설정하고 모델에 적용한다.
        /// </summary>
        /// <param name="gridController">게임 상태를 관리하는 그리드 컨트롤러.</param>
        public override void Initialize(IGridController gridController)
        {
            base.Initialize(gridController);
            _currentFacingDirection = _initialFacingDirection;
            ApplyFacingRotation();
        }

        // ============================================================
        //  [추가] 방향 시스템 핵심 메서드
        // ============================================================

        /// <summary>
        /// 두 월드 위치 사이의 방향을 6각형 방향으로 변환한다.
        /// XZ 평면에서의 각도를 계산하고, 가장 가까운 60도 단위로 스냅한다.
        /// </summary>
        /// <param name="from">출발 위치 (월드 좌표).</param>
        /// <param name="to">목표 위치 (월드 좌표).</param>
        /// <returns>6각형 방향 (0~5).</returns>
        private HexDirection CalculateHexDirection(Vector3 from, Vector3 to)
        {
            Vector3 diff = to - from;
            float angle = Mathf.Atan2(diff.x, diff.z) * Mathf.Rad2Deg;
            if (angle < 0f) angle += 360f;

            int index = Mathf.RoundToInt(angle / 60f) % 6;
            return (HexDirection)index;
        }

        /// <summary>
        /// 6각형 방향을 Unity의 Y축 회전(Quaternion)으로 변환한다.
        /// </summary>
        /// <param name="dir">6각형 방향.</param>
        /// <returns>해당 방향을 바라보는 회전값.</returns>
        private Quaternion HexDirectionToRotation(HexDirection dir)
        {
            float angle = (int)dir * 60f;
            return Quaternion.Euler(0f, angle, 0f);
        }

        /// <summary>
        /// _currentFacingDirection에 따라 _unitModel의 회전을 즉시 적용한다.
        /// 이동 완료, 공격 시 등 방향이 확정되는 시점에 호출한다.
        /// </summary>
        private void ApplyFacingRotation()
        {
            if (_unitModel != null)
            {
                _unitModel.rotation = HexDirectionToRotation(_currentFacingDirection);
            }
        }

        // ============================================================
        //  [추가] 후방 공격 판정
        // ============================================================

        /// <summary>
        /// 공격이 후방 공격인지 판정한다.
        /// 6각형 방향 기반으로, 공격자가 방어자의 후방 호(arc)에 위치하는지 확인한다.
        /// 
        /// 판정 방법:
        /// 1. 방어자에서 공격자 방향의 6각형 방향(A)을 계산한다.
        /// 2. 방어자의 정후방 방향(B)을 계산한다. B = (방어자 시선 + 3) % 6
        /// 3. A와 B의 방향 차이가 _backstabArcSize 이내이면 후방 공격이다.
        /// 
        /// 예시 (_backstabArcSize = 1, 방어자가 Dir0을 바라볼 때):
        ///   정후방 = Dir3 (180도)
        ///   후방 호 = {Dir2, Dir3, Dir4}
        ///   → 이 3방향에서 공격하면 후방 공격 판정
        /// </summary>
        /// <param name="defender">방어 유닛.</param>
        /// <param name="aggressorCell">공격자가 위치한 셀.</param>
        /// <param name="defenderCell">방어자가 위치한 셀.</param>
        /// <returns>후방 공격이면 true.</returns>
        private bool IsBackstab(KTFLUnit defender, ICell aggressorCell, ICell defenderCell)
        {
            HexDirection attackerDirection = CalculateHexDirection(
                defenderCell.WorldPosition.ToVector3(),
                aggressorCell.WorldPosition.ToVector3()
            );

            int rearDirection = ((int)defender.CurrentFacingDirection + 3) % 6;

            int diff = Math.Abs((int)attackerDirection - rearDirection);
            if (diff > 3) diff = 6 - diff;

            return diff <= _backstabArcSize;
        }

        // ============================================================
        //  Cell 관련 오버라이드 (기존)
        // ============================================================

        public override bool IsCellTraversable(ICell source, ICell destination)
        {
            var sourceHeight = (source as Cell).GetComponent<IHeightComponent>().Height;
            var destinationHeight = (destination as Cell).GetComponent<IHeightComponent>().Height;
            var destinationType = (destination as Cell).GetComponent<ITypedCell>().CellType;

            return base.IsCellTraversable(source, destination)
                && (sourceHeight == destinationHeight || (Math.Abs(sourceHeight - destinationHeight) == 1))
                && destinationType != _waterCellType;
        }

        public override bool IsCellMovableTo(ICell cell)
        {
            return base.IsCellMovableTo(cell) && !(cell as ITypedCell).CellType.Equals(_waterCellType);
        }

        public override bool IsUnitAttackable(IUnit otherUnit, ICell otherUnitCell, ICell attackSourceCell)
        {
            var attackSourceCellHeight = (attackSourceCell as Cell).GetComponent<IHeightComponent>().Height;
            var otherUnitCellHeight = (otherUnitCell as Cell).GetComponent<IHeightComponent>().Height;
            var isRangedAttack = AttackRange > 1;

            return base.IsUnitAttackable(otherUnit, otherUnitCell, attackSourceCell)
                && (isRangedAttack || Math.Abs(otherUnitCellHeight - attackSourceCellHeight) <= 1);
        }

        // ============================================================
        //  전투 관련 오버라이드
        // ============================================================

        /// <summary>
        /// 기존: 높은 지형에서 공격 시 추가 피해.
        /// </summary>
        public override float CalculateDamageTaken(IUnit aggressor, float damageDealt, ICell aggressorCell, ICell defenderCell)
        {
            var agressorCellHeight = (aggressorCell as Cell).GetComponent<IHeightComponent>().Height;
            var defenderCellHeight = (defenderCell as Cell).GetComponent<IHeightComponent>().Height;

            return agressorCellHeight > defenderCellHeight
                ? (damageDealt * 2) - DefenceFactor
                : base.CalculateDamageTaken(aggressor, damageDealt, aggressorCell, defenderCell);
        }

        /// <summary>
        /// [추가] 후방 공격 시 추가 피해를 적용한다.
        /// base.CalculateDamageDealt로 기본 데미지를 구한 뒤,
        /// 방어자가 KTFLUnit이고 후방 공격이면 _backstabDamageMultiplier를 곱한다.
        /// </summary>
        /// <param name="defender">공격 대상 유닛.</param>
        /// <param name="defenderCell">방어자가 위치한 셀.</param>
        /// <param name="aggressorCell">공격자가 위치한 셀.</param>
        /// <returns>후방 공격 보정이 적용된 데미지 값.</returns>
        public override float CalculateDamageDealt(IUnit defender, ICell defenderCell, ICell aggressorCell)
        {
            float baseDamage = base.CalculateDamageDealt(defender, defenderCell, aggressorCell);

            if (defender is KTFLUnit defenderUnit)
            {
                if (IsBackstab(defenderUnit, aggressorCell, defenderCell))
                {
                    return baseDamage * _backstabDamageMultiplier;
                }
            }

            return baseDamage;
        }

        // ============================================================
        //  [추가] 공격 시 방향 처리
        // ============================================================

        /// <summary>
        /// [추가] 공격 시 대상 유닛 방향으로 _currentFacingDirection을 업데이트한 뒤
        /// 회전을 적용하고 기존 공격 하이라이트를 실행한다.
        /// 
        /// 참고: 데미지 계산(CalculateDamageDealt)은 AttackCommand.Execute에서
        /// MarkAsAttacking보다 먼저 호출되므로, 이 방향 변경은 후방 판정에 영향 없다.
        /// </summary>
        /// <param name="otherUnit">공격 대상 유닛.</param>
        /// <returns>비동기 작업.</returns>
        public override async Task MarkAsAttacking(Unit otherUnit)
        {
            // [추가] 에피소드 전환 시 유닛이 파괴되면 안전하게 중단
            if (this == null || otherUnit == null) return;

            _currentFacingDirection = CalculateHexDirection(
                transform.position,
                otherUnit.transform.position
            );
            ApplyFacingRotation();

            await base.MarkAsAttacking(otherUnit);

            // [추가] 공격 하이라이트 완료 후 null 체크
            if (this == null) return;

            // 공격 하이라이트 완료 후 방향 재적용 (SwayHighlighter 등이 위치를 변경했을 수 있음)
            ApplyFacingRotation();
        }

        // ============================================================
        //  이동 애니메이션 (기존 + 방향 저장 추가)
        // ============================================================

        /// <summary>
        /// 이동 애니메이션을 실행한다.
        /// 이동 중에는 Slerp으로 부드러운 회전을 적용하고,
        /// 이동 완료 후 마지막 이동 방향을 HexDirection으로 저장하여 즉시 적용한다.
        /// </summary>
        /// <param name="path">이동 경로 셀 목록.</param>
        /// <param name="destination">최종 목적지 셀.</param>
        /// <returns>비동기 작업.</returns>
        public override async Task MovementAnimation(IEnumerable<ICell> path, ICell destination)
        {
            var currentCell = CurrentCell;
            foreach (var cell in path)
            {
                // [추가] 에피소드 전환 시 유닛이 파괴되면 안전하게 중단
                if (this == null) return;

                // 이동할 때마다 방향을 업데이트 (마지막 이동 방향이 최종 방향이 됨)
                _currentFacingDirection = CalculateHexDirection(
                    WorldPosition.ToVector3(),
                    cell.WorldPosition.ToVector3()
                );

                InvokeUnitLeftCell(new UnitChangedGridPositionEventArgs(this, currentCell, cell));
                Vector3 direction = (cell.WorldPosition.ToVector3() - WorldPosition.ToVector3()).normalized;

                while (!WorldPosition.Equals(cell.WorldPosition))
                {
                    // [추가] 이동 도중 유닛이 파괴되면 안전하게 중단
                    if (this == null || _unitModel == null) return;

                    WorldPosition = Vector3.MoveTowards(
                        WorldPosition.ToVector3(),
                        cell.WorldPosition.ToVector3(),
                        Time.deltaTime * MovementAnimationSpeed
                    ).ToIVector3();

                    if (direction != Vector3.zero)
                    {
                        Quaternion targetRotation = Quaternion.LookRotation(new Vector3(direction.x, 0, direction.z));
                        _unitModel.rotation = Quaternion.Slerp(
                            _unitModel.rotation,
                            targetRotation,
                            Time.deltaTime * MovementAnimationSpeed * 2
                        );
                    }

                    await Awaitable.NextFrameAsync();
                }

                // [추가] 셀 진입 이벤트 전 null 체크
                if (this == null) return;

                InvokeUnitEnteredCell(new UnitChangedGridPositionEventArgs(this, currentCell, cell));
                currentCell = cell;
            }

            // [추가] 최종 위치/방향 설정 전 null 체크
            if (this == null) return;

            WorldPosition = destination.WorldPosition;
            ApplyFacingRotation();
        }

        // ============================================================
        //  턴 관련 (기존)
        // ============================================================

        public int GetMaxAbilityUsesPerTurn()
        {
            return _maxAbilityUsesPerTurn;
        }

        public int GetAbilityUsePoints()
        {
            return AbilityUsePoints;
        }

        public override void OnTurnStart(IGridController gridController)
        {
            base.OnTurnStart(gridController);
            AbilityUsePoints = _maxAbilityUsesPerTurn;
        }
    }
}