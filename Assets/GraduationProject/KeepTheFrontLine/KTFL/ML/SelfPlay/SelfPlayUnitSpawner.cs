using System.Collections.Generic;
using System.Linq;
using TurnBasedStrategyFramework.Common.Cells;
using TurnBasedStrategyFramework.Unity.Cells;
using TurnBasedStrategyFramework.Unity.Units;
using TurnBasedStrategyFramework.Unity.Examples.ClashOfHeroes.Cells;
using TurnBasedStrategyFramework.Unity.Examples.ClashOfHeroes.Units;
using Unity.MLAgents.Policies;
using UnityEngine;

/// <summary>
/// Self-Play 학습 전용 유닛 스폰러.
/// TrainingUnitSpawner의 스폰 로직을 기반으로 하되,
/// 양쪽 진영 모두 동일한 ML 프리팹을 사용하고
/// BehaviorParameters의 TeamId를 설정하여 Self-Play가 작동하도록 한다.
/// </summary>
public class SelfPlayUnitSpawner : MonoBehaviour
{
    [Header("참조")]
    [SerializeField] private UnityUnitManager _unitManager;

    [Tooltip("물 타일 CellType. 물 위에는 유닛을 스폰하지 않는다.")]
    [SerializeField] private ScriptableObject _waterCellType;

    [Header("ML 유닛 프리팹 (양쪽 동일하게 사용)")]
    [Tooltip("ML Agent가 내장된 유닛 프리팹 목록.")]
    [SerializeField] private List<Unit> _mlUnitPrefabs = new List<Unit>();

    [Header("스폰 수 설정")]
    [SerializeField] private int _minUnits = 4;
    [SerializeField] private int _maxUnits = 10;

    [Header("스폰 구역 설정")]
    [Tooltip("스폰 구역 열 깊이. 2면 맵 끝에서 2열 안쪽까지 스폰 구역으로 사용.")]
    [SerializeField] private int _spawnDepth = 2;

    [Header("다양성 설정")]
    [SerializeField][Range(0f, 1f)] private float _excludeTypeProbability = 0.2f;

    [Header("방향 설정")]
    [SerializeField] private HexDirection _team0FacingDirection = HexDirection.Dir0;
    [SerializeField] private HexDirection _team1FacingDirection = HexDirection.Dir3;

    // =========================================================================
    // 스폰 API
    // =========================================================================

    public void SpawnUnits(IEnumerable<ICell> cells, int mapHeight, int startY)
    {
        if (_unitManager == null)
        {
            Debug.LogError("[SelfPlayUnitSpawner] UnitManager가 연결되지 않았습니다.");
            return;
        }

        if (_mlUnitPrefabs.Count == 0)
        {
            Debug.LogError("[SelfPlayUnitSpawner] ML 유닛 프리팹이 등록되지 않았습니다.");
            return;
        }

        var cellList = cells.ToList();

        // 셀 목록에서 X 좌표 범위 계산
        int minX = cellList.Min(c => c.GridCoordinates.x);
        int maxX = cellList.Max(c => c.GridCoordinates.x);

        // 스폰 구역 필터링
        var lowXCells = cellList
            .Where(c => c.GridCoordinates.x >= minX &&
                        c.GridCoordinates.x <= minX + _spawnDepth - 1 &&
                        !c.IsTaken &&
                        IsSpawnable(c, cellList))
            .ToList();

        var highXCells = cellList
            .Where(c => c.GridCoordinates.x >= maxX - _spawnDepth + 1 &&
                        c.GridCoordinates.x <= maxX &&
                        !c.IsTaken &&
                        IsSpawnable(c, cellList))
            .ToList();

        // 랜덤 유닛 구성 생성 (양쪽 동일)
        var composition = GenerateRandomComposition(lowXCells.Count, highXCells.Count);
        int totalPerSide = composition.Sum(kvp => kvp.Value);

        // 중앙 정렬된 셀 선택
        var team0Cells = GetCenteredCells(lowXCells, totalPerSide);
        var team1Cells = GetCenteredCells(highXCells, totalPerSide);

        // Team 0 (왼쪽) 스폰 — ML 프리팹 + TeamId 0
        SpawnComposition(composition, team0Cells, 0, _team0FacingDirection);

        // Team 1 (오른쪽) 스폰 — 동일 ML 프리팹 + TeamId 1
        SpawnComposition(composition, team1Cells, 1, _team1FacingDirection);

        LogComposition(composition);
    }

    // =========================================================================
    // 랜덤 유닛 구성 생성
    // =========================================================================

    private Dictionary<int, int> GenerateRandomComposition(int lowCellCount, int highCellCount)
    {
        var composition = new Dictionary<int, int>();

        int maxAvailable = Mathf.Min(lowCellCount, highCellCount);
        int totalUnits = Random.Range(_minUnits, _maxUnits + 1);
        totalUnits = Mathf.Min(totalUnits, maxAvailable);

        if (totalUnits <= 0)
        {
            Debug.LogWarning("[SelfPlayUnitSpawner] 스폰 가능한 셀이 부족합니다.");
            return composition;
        }

        var participatingIndices = GetParticipatingTypes(totalUnits);

        if (participatingIndices.Count == 0)
        {
            Debug.LogWarning("[SelfPlayUnitSpawner] 참여 가능한 유닛 종류가 없습니다.");
            return composition;
        }

        // 참여하는 종류에 최소 1개씩 배분
        int assigned = 0;
        foreach (int idx in participatingIndices)
        {
            composition[idx] = 1;
            assigned++;
        }

        // 나머지를 랜덤 배분
        int remaining = totalUnits - assigned;
        for (int i = 0; i < remaining; i++)
        {
            int randomIdx = participatingIndices[Random.Range(0, participatingIndices.Count)];
            composition[randomIdx]++;
        }

        return composition;
    }

    private List<int> GetParticipatingTypes(int totalUnits)
    {
        var indices = new List<int>();
        for (int i = 0; i < _mlUnitPrefabs.Count; i++)
        {
            if (_mlUnitPrefabs[i] != null)
                indices.Add(i);
        }

        if (indices.Count <= 2) return indices;
        if (totalUnits < indices.Count) return indices;

        if (Random.value < _excludeTypeProbability)
        {
            int excludeIdx = Random.Range(0, indices.Count);
            indices.RemoveAt(excludeIdx);
        }

        return indices;
    }

    // =========================================================================
    // 스폰 실행
    // =========================================================================

    private void SpawnComposition(Dictionary<int, int> composition,
                                  List<ICell> availableCells, int playerNumber,
                                  HexDirection facingDirection)
    {
        int cellIndex = 0;
        int spawnedTotal = 0;

        foreach (var kvp in composition)
        {
            Unit prefab = _mlUnitPrefabs[kvp.Key];
            int count = kvp.Value;

            if (prefab == null) continue;

            for (int i = 0; i < count; i++)
            {
                while (cellIndex < availableCells.Count && availableCells[cellIndex].IsTaken)
                    cellIndex++;

                if (cellIndex >= availableCells.Count)
                {
                    Debug.LogWarning($"[SelfPlayUnitSpawner] Player {playerNumber} 스폰 구역 부족. " +
                                     $"({spawnedTotal}개 스폰 후 중단)");
                    return;
                }

                var targetCell = availableCells[cellIndex];
                SpawnUnit(prefab, targetCell, playerNumber, facingDirection);
                cellIndex++;
                spawnedTotal++;
            }
        }

        Debug.Log($"[SelfPlayUnitSpawner] Player {playerNumber} (Team {playerNumber}) 유닛 {spawnedTotal}개 스폰 완료");
    }

    private void SpawnUnit(Unit prefab, ICell targetCell, int playerNumber, HexDirection facingDirection)
    {
        var go = Instantiate(prefab.gameObject, _unitManager.transform);
        var unit = go.GetComponent<Unit>();

        unit.PlayerNumber = playerNumber;
        unit.CurrentCell = targetCell;
        go.transform.position = (targetCell as Cell).transform.position;

        targetCell.IsTaken = true;
        targetCell.CurrentUnits.Add(unit);

        _unitManager.AddUnit(unit);

        // Self-Play TeamId 설정
        var behaviorParams = unit.GetComponentInChildren<BehaviorParameters>();
        if (behaviorParams != null)
        {
            behaviorParams.TeamId = playerNumber;
        }

        // 방향 설정
        if (unit is KTFLUnit ktflUnit)
        {
            ktflUnit.SetFacingDirection(facingDirection);
        }

        go.name = $"{prefab.name}_P{playerNumber}";
    }

    // =========================================================================
    // 스폰 셀 검증
    // =========================================================================

    private bool IsSpawnable(ICell cell, List<ICell> allCells)
    {
        // 물 타일 체크
        if (_waterCellType != null && cell is ITypedCell typedCell)
        {
            if (typedCell.CellType.Equals(_waterCellType))
                return false;
        }

        // 높이 고립 체크
        if (cell is Cell unityCell)
        {
            var cellHeight = unityCell.GetComponent<IHeightComponent>();
            if (cellHeight == null) return true;

            int myHeight = cellHeight.Height;

            bool hasAccessibleNeighbor = allCells.Any(other =>
            {
                if (other.Equals(cell)) return false;
                if (other.GetDistance(cell) != 1) return false;

                if (_waterCellType != null && other is ITypedCell otherTyped)
                {
                    if (otherTyped.CellType.Equals(_waterCellType))
                        return false;
                }

                var otherCell = other as Cell;
                if (otherCell == null) return false;
                var otherHeight = otherCell.GetComponent<IHeightComponent>();
                if (otherHeight == null) return true;

                return Mathf.Abs(myHeight - otherHeight.Height) <= 1;
            });

            return hasAccessibleNeighbor;
        }

        return true;
    }

    // =========================================================================
    // 유틸리티
    // =========================================================================

    private List<ICell> GetCenteredCells(List<ICell> cells, int unitCount)
    {
        if (cells.Count == 0 || unitCount <= 0) return new List<ICell>();

        var sorted = cells
            .OrderBy(c => c.GridCoordinates.x)
            .ThenBy(c => c.GridCoordinates.y)
            .ToList();

        int total = sorted.Count;
        int take = Mathf.Min(unitCount, total);
        int startIndex = Mathf.Max(0, (total - take) / 2);

        return sorted.Skip(startIndex).Take(take).ToList();
    }

    public void ClearUnits()
    {
        if (_unitManager == null) return;
        for (int i = _unitManager.transform.childCount - 1; i >= 0; i--)
        {
            Destroy(_unitManager.transform.GetChild(i).gameObject);
        }
        _unitManager.ClearUnits();
    }

    private void LogComposition(Dictionary<int, int> composition)
    {
        var parts = new List<string>();
        foreach (var kvp in composition)
        {
            string name = _mlUnitPrefabs[kvp.Key] != null ? _mlUnitPrefabs[kvp.Key].name : "null";
            parts.Add($"{name}:{kvp.Value}");
        }
        int total = composition.Sum(kvp => kvp.Value);
        Debug.Log($"[SelfPlayUnitSpawner] 유닛 구성: {string.Join(", ", parts)} (총 {total}개, 양 진영 동일)");
    }
}