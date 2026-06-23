using System;
using System.Linq;
using TurnBasedStrategyFramework.Common.Controllers;
using TurnBasedStrategyFramework.Common.Units;
using TurnBasedStrategyFramework.Unity.Controllers;
using TurnBasedStrategyFramework.Unity.Units;
using TurnBasedStrategyFramework.Unity.Examples.ClashOfHeroes.Units;
using TurnBasedStrategyFramework.Unity.Utilities;
using UnityEngine;

namespace TurnBasedStrategyFramework.Logging
{
    // ============================================================
    //  전투 로그 데이터 구조
    // ============================================================

    /// <summary>
    /// 한 번의 전투 이벤트에 대한 로그 정보를 담는 구조체.
    /// 콘솔 로그와 미래의 UI 시스템(데미지 팝업 등) 모두 이 데이터를 소비한다.
    /// 
    /// [확장 시] 새로운 전투 정보(예: 속성 피해, 크리티컬 여부)가 추가되면
    /// 이 구조체에 필드를 추가한다.
    /// </summary>
    [Serializable]
    public struct CombatLogEntry
    {
        /// <summary>
        /// 공격한 유닛.
        /// </summary>
        public IUnit Attacker;

        /// <summary>
        /// 공격받은 유닛.
        /// </summary>
        public IUnit Defender;

        /// <summary>
        /// 실제 적용된 데미지량.
        /// CalculateTotalDamage의 결과로, 공격력/방어력/후방공격 등 모든 보정이 반영된 값.
        /// </summary>
        public float DamageApplied;

        /// <summary>
        /// 피격 전 방어자의 체력.
        /// </summary>
        public float DefenderHealthBefore;

        /// <summary>
        /// 피격 후 방어자의 체력.
        /// </summary>
        public float DefenderHealthAfter;

        /// <summary>
        /// 후방 공격이었는지 여부.
        /// 방어자가 KTFLUnit이 아니면 항상 false.
        /// </summary>
        public bool IsBackstab;

        /// <summary>
        /// 이 공격으로 방어자가 사망했는지 여부.
        /// </summary>
        public bool IsKill;

        /// <summary>
        /// 로그 발생 시각 (게임 시작 후 경과 시간).
        /// </summary>
        public float Timestamp;
    }

    // ============================================================
    //  CombatLogger 컴포넌트
    // ============================================================

    /// <summary>
    /// 전투 이벤트를 감지하여 구조화된 로그를 생성하는 컴포넌트.
    /// 콘솔에 로그를 출력하며, OnCombatLogCreated 이벤트를 통해
    /// 외부 시스템(데미지 팝업 UI 등)이 같은 데이터를 소비할 수 있다.
    /// 
    /// 사용법:
    /// - 씬에 빈 오브젝트를 만들고 이 컴포넌트를 부착
    /// - _gridController 필드에 씬의 UnityGridController를 할당
    /// - 콘솔 로그를 끄려면 _enableConsoleLog를 false로 설정
    /// 
    /// [확장: 데미지 팝업 UI 연결]
    /// 1. UI 스크립트에서 CombatLogger의 참조를 가져온다
    /// 2. OnCombatLogCreated 이벤트를 구독한다
    /// 3. CombatLogEntry 데이터로 팝업을 생성한다
    /// 예:
    ///   combatLogger.OnCombatLogCreated += (entry) => {
    ///       ShowDamagePopup(entry.Defender, entry.DamageApplied, entry.IsBackstab);
    ///   };
    /// </summary>
    public class CombatLogger : MonoBehaviour
    {
        [Header("씬 참조")]
        [Tooltip("게임 이벤트를 구독하기 위한 UnityGridController.")]
        [SerializeField] private UnityGridController _gridController;

        [Header("설정")]
        [Tooltip("콘솔에 전투 로그를 출력할지 여부.")]
        [SerializeField] private bool _enableConsoleLog = true;

        [Tooltip("사망 로그를 별도로 출력할지 여부.")]
        [SerializeField] private bool _enableKillLog = true;

        /// <summary>
        /// 전투 로그가 생성될 때 발생하는 이벤트.
        /// UI 시스템(데미지 팝업 등)이 이 이벤트를 구독하여 시각적 피드백을 제공할 수 있다.
        /// </summary>
        public event Action<CombatLogEntry> OnCombatLogCreated;

        /// <summary>
        /// 유닛 사망 시 발생하는 이벤트.
        /// 사망 연출 UI 등이 구독할 수 있다.
        /// </summary>
        public event Action<CombatLogEntry> OnUnitKilled;

        private void OnEnable()
        {
            if (_gridController != null)
            {
                _gridController.GameInitialized += OnGameInitialized;
            }
        }

        private void OnDisable()
        {
            if (_gridController != null)
            {
                _gridController.GameInitialized -= OnGameInitialized;
                UnsubscribeAllUnits();
            }
        }

        /// <summary>
        /// 게임 초기화 완료 시 호출된다.
        /// 모든 유닛의 UnitAttacked 이벤트를 구독한다.
        /// </summary>
        private void OnGameInitialized()
        {
            SubscribeAllUnits();
        }

        /// <summary>
        /// 현재 게임의 모든 유닛에 대해 전투 이벤트를 구독한다.
        /// </summary>
        private void SubscribeAllUnits()
        {
            var allUnits = _gridController.UnitManager.GetUnits();
            foreach (var unit in allUnits)
            {
                unit.UnitAttacked += OnUnitAttacked;
            }
        }

        /// <summary>
        /// 모든 유닛의 전투 이벤트 구독을 해제한다.
        /// </summary>
        private void UnsubscribeAllUnits()
        {
            var allUnits = _gridController.UnitManager?.GetUnits();
            if (allUnits != null)
            {
                foreach (var unit in allUnits)
                {
                    unit.UnitAttacked -= OnUnitAttacked;
                }
            }
        }

        /// <summary>
        /// 유닛이 공격받았을 때 호출된다.
        /// CombatLogEntry를 생성하고, 콘솔 출력 및 이벤트를 발생시킨다.
        /// </summary>
        /// <param name="eventArgs">공격 이벤트 정보.</param>
        private void OnUnitAttacked(UnitAttackedEventArgs eventArgs)
        {
            // [추가] 씬 전환 시 파괴된 유닛 방지
            if (eventArgs.AttackingUnit == null || eventArgs.AffectedUnit == null) return;
            if (eventArgs.AttackingUnit is UnityEngine.Object obj1 && obj1 == null) return;
            if (eventArgs.AffectedUnit is UnityEngine.Object obj2 && obj2 == null) return;

            // 피격 후 체력 계산
            float healthAfter = eventArgs.AffectedUnit.Health;
            float healthBefore = healthAfter + eventArgs.DamageDealt;
            bool isKill = healthAfter <= 0f;

            // 후방 공격 판정
            bool isBackstab = CheckBackstab(eventArgs);

            // 로그 엔트리 생성
            var entry = new CombatLogEntry
            {
                Attacker = eventArgs.AttackingUnit,
                Defender = eventArgs.AffectedUnit,
                DamageApplied = eventArgs.DamageDealt,
                DefenderHealthBefore = healthBefore,
                DefenderHealthAfter = healthAfter,
                IsBackstab = isBackstab,
                IsKill = isKill,
                Timestamp = Time.time,
            };

            // 콘솔 출력
            if (_enableConsoleLog)
            {
                LogToConsole(entry);
            }

            // 이벤트 발생 (UI 등 외부 시스템용)
            OnCombatLogCreated?.Invoke(entry);

            if (isKill)
            {
                if (_enableKillLog)
                {
                    LogKillToConsole(entry);
                }
                OnUnitKilled?.Invoke(entry);
            }
        }

        /// <summary>
        /// 공격이 후방 공격이었는지 확인한다.
        /// 공격자와 방어자가 모두 KTFLUnit인 경우에만 판정한다.
        /// KTFLUnit의 HexDirection 기반 방향 시스템을 사용한다.
        /// 
        /// [참고] 이 판정은 로그용으로만 사용된다. 실제 데미지 계산은 KTFLUnit.CalculateDamageDealt에서 수행된다.
        /// </summary>
        /// <param name="eventArgs">공격 이벤트 정보.</param>
        /// <returns>후방 공격이면 true.</returns>
        private bool CheckBackstab(UnitAttackedEventArgs eventArgs)
        {
            if (eventArgs.AttackingUnit is KTFLUnit attacker
                && eventArgs.AffectedUnit is KTFLUnit defender)
            {
                if (attacker.CurrentCell != null && defender.CurrentCell != null)
                {
                    // 방어자에서 공격자 방향을 6각형 방향으로 계산
                    Vector3 defenderPos = defender.CurrentCell.WorldPosition.ToVector3();
                    Vector3 attackerPos = attacker.CurrentCell.WorldPosition.ToVector3();
                    Vector3 diff = attackerPos - defenderPos;
                    float angle = Mathf.Atan2(diff.x, diff.z) * Mathf.Rad2Deg;
                    if (angle < 0f) angle += 360f;
                    int attackerDir = Mathf.RoundToInt(angle / 60f) % 6;

                    // 방어자의 정후방 방향
                    int rearDir = ((int)defender.CurrentFacingDirection + 3) % 6;

                    // 방향 차이 계산
                    int dirDiff = Mathf.Abs(attackerDir - rearDir);
                    if (dirDiff > 3) dirDiff = 6 - dirDiff;

                    return dirDiff <= 1; // 정후방 ± 1방향 이내면 후방 판정
                }
            }
            return false;
        }

        /// <summary>
        /// 전투 로그를 콘솔에 출력한다.
        /// </summary>
        /// <param name="entry">출력할 전투 로그 엔트리.</param>
        private void LogToConsole(CombatLogEntry entry)
        {
            string attackerName = GetUnitDisplayName(entry.Attacker);
            string defenderName = GetUnitDisplayName(entry.Defender);
            string backstabTag = entry.IsBackstab ? " [후방 공격!]" : "";

            Debug.Log($"[전투] {attackerName} → {defenderName} | " +
                      $"데미지: {entry.DamageApplied:F1}{backstabTag} | " +
                      $"체력: {entry.DefenderHealthBefore:F1} → {entry.DefenderHealthAfter:F1}");
        }

        /// <summary>
        /// 사망 로그를 콘솔에 출력한다.
        /// </summary>
        /// <param name="entry">사망을 유발한 전투 로그 엔트리.</param>
        private void LogKillToConsole(CombatLogEntry entry)
        {
            string attackerName = GetUnitDisplayName(entry.Attacker);
            string defenderName = GetUnitDisplayName(entry.Defender);

            Debug.Log($"<color=red>[사망] {defenderName}이(가) {attackerName}에 의해 쓰러졌습니다!</color>");
        }

        /// <summary>
        /// 유닛의 표시 이름을 반환한다.
        /// IUnitDetails를 구현한 유닛이면 UnitName을, 아니면 GameObject 이름을 사용한다.
        /// </summary>
        /// <param name="unit">이름을 가져올 유닛.</param>
        /// <returns>표시용 유닛 이름.</returns>
        private string GetUnitDisplayName(IUnit unit)
        {
            if (unit is IUnitDetails details && !string.IsNullOrEmpty(details.UnitName))
            {
                return $"{details.UnitName}(P{unit.PlayerNumber})";
            }
            if (unit is Unit unityUnit)
            {
                return $"{unityUnit.gameObject.name}(P{unit.PlayerNumber})";
            }
            return $"Unit(P{unit.PlayerNumber})";
        }
    }
}