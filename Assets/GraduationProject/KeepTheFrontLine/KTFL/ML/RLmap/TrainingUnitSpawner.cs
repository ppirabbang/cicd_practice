using System.Collections.Generic;
using System.Linq;
using TurnBasedStrategyFramework.Common.Cells;
using TurnBasedStrategyFramework.Unity.Cells;
using TurnBasedStrategyFramework.Unity.Units;
using TurnBasedStrategyFramework.Unity.Examples.ClashOfHeroes.Cells;
using TurnBasedStrategyFramework.Unity.Examples.ClashOfHeroes.Units;
using UnityEngine;

/// <summary>
/// 학습 전용 유닛 스폰러.
/// 매 에피소드마다 랜덤한 유닛 구성으로 양 진영을 스폰한다.
/// ML 진영과 상대 진영에 서로 다른 프리팹(Brain)을 사용할 수 있다.
/// 
/// 프리팹 매핑:
///   _mlUnitPrefabs[0] ↔ _opponentUnitPrefabs[0]  (같은 유닛 종류, 다른 Brain)
///   _mlUnitPrefabs[1] ↔ _opponentUnitPrefabs[1]
///   ...
/// 
/// 학습 단계별 사용법:
///   1단계 (ML vs 수동): _opponentUnitPrefabs에 EnhancedBT Brain 프리팹 할당
///   2단계 (ML vs ML):   _opponentUnitPrefabs에 ML Brain 프리팹 할당 (또는 _mlUnitPrefabs와 동일)
/// </summary>
public class TrainingUnitSpawner : MonoBehaviour
{
    [Header("참조")]
    [SerializeField] private UnityUnitManager _unitManager;

    [Tooltip("물 타일 CellType. 물 위에는 유닛을 스폰하지 않는다.")]
    [SerializeField] private ScriptableObject _waterCellType;

    [Header("ML Player 유닛 프리팹")]
    [Tooltip("ML Agent가 제어하는 유닛 프리팹 리스트. 인덱스가 상대 프리팹 리스트와 1:1 대응한다.")]
    [SerializeField] private List<Unit> _mlUnitPrefabs = new List<Unit>();

    [Header("상대 진영 유닛 프리팹")]
    [Tooltip("상대 진영 유닛 프리팹 리스트. ML 프리팹과 1:1 대응 (같은 유닛 종류, 다른 Brain). " +
             "비워두면 _mlUnitPrefabs와 동일한 프리팹을 사용한다 (ML vs ML 학습).")]
    [SerializeField] private List<Unit> _opponentUnitPrefabs = new List<Unit>();

    [Header("플레이어 설정")]
    [Tooltip("ML Agent가 제어하는 플레이어 번호.")]
    [SerializeField] private int _mlPlayerNumber = 1;

    [Header("스폰 수 설정")]
    [Tooltip("한 진영의 최소 유닛 수.")]
    [SerializeField] private int _minUnits = 4;

    [Tooltip("한 진영의 최대 유닛 수.")]
    [SerializeField] private int _maxUnits = 10;

    [Header("스폰 구역 설정")]
    [Tooltip("스폰 구역 열 깊이. 2면 맵 끝에서 2열 안쪽까지 스폰 구역으로 사용.")]
    [SerializeField] private int _spawnDepth = 2;

    [Header("다양성 설정")]
    [Tooltip("에피소드당 하나의 유닛 종류를 제외할 확률 (0.0~1.0). " +
             "0.2이면 약 20%의 에피소드에서 하나의 종류가 빠진다.")]
    [SerializeField][Range(0f, 1f)] private float _excludeTypeProbability = 0.2f;

    [Header("방향 설정")]
    [Tooltip("ML Player 유닛이 스폰 시 바라볼 방향.")]
    [SerializeField] private HexDirection _mlPlayerFacingDirection = HexDirection.Dir0;

    [Tooltip("상대 진영 유닛이 스폰 시 바라볼 방향.")]
    [SerializeField] private HexDirection _opponentFacingDirection = HexDirection.Dir3;

    // =========================================================================
    // 정리 API
    // =========================================================================

    /// <summary>
    /// 이전 에피소드의 모든 유닛을 파괴하고 UnitManager에서 제거한다.
    /// </summary>
    public void ClearUnits()
    {
        if (_unitManager == null) return;

        for (int i = _unitManager.transform.childCount - 1; i >= 0; i--)
        {
            Destroy(_unitManager.transform.GetChild(i).gameObject);
        }

        _unitManager.ClearUnits();
        Debug.Log("[TrainingUnitSpawner] 기존 유닛 정리 완료");
    }

    // =========================================================================
    // 스폰 API
    // =========================================================================

    /// <summary>
    /// 맵의 Cell 목록을 받아 양 진영에 랜덤 구성의 유닛을 스폰한다.
    /// ML Player는 _mlUnitPrefabs로, 상대는 _opponentUnitPrefabs로 스폰된다.
    /// 양 진영의 유닛 종류와 개수는 동일하다.
    /// </summary>
    /// <param name="cells">맵의 전체 Cell 목록.</param>
    /// <param name="mapHeight">맵 세로 크기 (호환성 유지용).</param>
    /// <param name="startY">맵 시작 Y 좌표 (호환성 유지용).</param>
    public void SpawnUnits(IEnumerable<ICell> cells, int mapHeight, int startY)
    {
        if (_unitManager == null)
        {
            Debug.LogError("[TrainingUnitSpawner] UnitManager가 연결되지 않았습니다.");
            return;
        }

        if (_mlUnitPrefabs.Count == 0)
        {
            Debug.LogError("[TrainingUnitSpawner] ML 유닛 프리팹이 등록되지 않았습니다.");
            return;
        }

        // 상대 프리팹이 비어있으면 ML 프리팹을 공유 (ML vs ML)
        var opponentPrefabs = _opponentUnitPrefabs.Count > 0 ? _opponentUnitPrefabs : _mlUnitPrefabs;

        if (opponentPrefabs.Count != _mlUnitPrefabs.Count)
        {
            Debug.LogError("[TrainingUnitSpawner] ML 프리팹과 상대 프리팹의 개수가 일치하지 않습니다. " +
                           $"ML: {_mlUnitPrefabs.Count}개, 상대: {opponentPrefabs.Count}개");
            return;
        }

        var cellList = cells.ToList();

        // 셀 목록에서 X 좌표 범위를 직접 계산
        int minX = cellList.Min(c => c.GridCoordinates.x);
        int maxX = cellList.Max(c => c.GridCoordinates.x);

        // ML Player 스폰 구역 (ML Player Number에 따라 위치 결정)
        // ML Player가 낮은 X이면 상대는 높은 X, 반대도 가능
        bool mlIsLowX = true; // 기본: ML Player가 낮은 X

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

        var mlCells = mlIsLowX ? lowXCells : highXCells;
        var opponentCells = mlIsLowX ? highXCells : lowXCells;
        var mlFacing = mlIsLowX ? _mlPlayerFacingDirection : _opponentFacingDirection;
        var opponentFacing = mlIsLowX ? _opponentFacingDirection : _mlPlayerFacingDirection;
        int opponentPlayerNumber = _mlPlayerNumber == 0 ? 1 : 0;

        // 랜덤 유닛 구성 생성
        var composition = GenerateRandomComposition(mlCells.Count, opponentCells.Count);

        // 양 진영 동일한 구성으로 스폰 (프리팹만 다름)
        int totalPerSide = composition.Sum(kvp => kvp.Value);
        mlCells = GetCenteredCells(mlCells, totalPerSide);
        opponentCells = GetCenteredCells(opponentCells, totalPerSide);

        SpawnComposition(composition, _mlUnitPrefabs, mlCells, _mlPlayerNumber, mlFacing);
        SpawnComposition(composition, opponentPrefabs, opponentCells, opponentPlayerNumber, opponentFacing);

        LogComposition(composition);
    }

    // =========================================================================
    // 랜덤 유닛 구성 생성
    // =========================================================================

    /// <summary>
    /// 랜덤한 유닛 구성을 생성한다.
    /// </summary>
    /// <param name="mlCellCount">ML 스폰 구역의 빈 셀 수.</param>
    /// <param name="opponentCellCount">상대 스폰 구역의 빈 셀 수.</param>
    /// <returns>유닛 프리팹 인덱스 → 개수 매핑.</returns>
    private Dictionary<int, int> GenerateRandomComposition(int mlCellCount, int opponentCellCount)
    {
        var composition = new Dictionary<int, int>();

        int maxAvailable = Mathf.Min(mlCellCount, opponentCellCount);
        int totalUnits = Random.Range(_minUnits, _maxUnits + 1);
        totalUnits = Mathf.Min(totalUnits, maxAvailable);

        if (totalUnits <= 0)
        {
            Debug.LogWarning("[TrainingUnitSpawner] 스폰 가능한 셀이 부족합니다.");
            return composition;
        }

        var participatingIndices = GetParticipatingTypes(totalUnits);

        if (participatingIndices.Count == 0)
        {
            Debug.LogWarning("[TrainingUnitSpawner] 참여 가능한 유닛 종류가 없습니다.");
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

    /// <summary>
    /// 이번 에피소드에 참여할 유닛 종류 인덱스 목록을 결정한다.
    /// </summary>
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

        if (totalUnits < indices.Count)
        {
            indices.Clear();
            for (int i = 0; i < _mlUnitPrefabs.Count; i++)
            {
                if (_mlUnitPrefabs[i] != null)
                    indices.Add(i);
            }
        }

        return indices;
    }

    // =========================================================================
    // 스폰 실행
    // =========================================================================

    /// <summary>
    /// 주어진 구성과 프리팹 리스트로 유닛을 스폰한다.
    /// </summary>
    /// <param name="composition">유닛 프리팹 인덱스 → 개수 매핑.</param>
    /// <param name="prefabs">사용할 프리팹 리스트 (ML 또는 상대).</param>
    /// <param name="availableCells">스폰 가능한 셀 목록.</param>
    /// <param name="playerNumber">플레이어 번호.</param>
    /// <param name="facingDirection">스폰 후 바라볼 방향.</param>
    private void SpawnComposition(Dictionary<int, int> composition, List<Unit> prefabs,
                                   List<ICell> availableCells, int playerNumber,
                                   HexDirection facingDirection)
    {
        int cellIndex = 0;
        int spawnedTotal = 0;

        foreach (var kvp in composition)
        {
            Unit prefab = prefabs[kvp.Key];
            int count = kvp.Value;

            if (prefab == null)
            {
                Debug.LogWarning($"[TrainingUnitSpawner] 프리팹 인덱스 {kvp.Key}이 null입니다. 스킵합니다.");
                continue;
            }

            for (int i = 0; i < count; i++)
            {
                while (cellIndex < availableCells.Count && availableCells[cellIndex].IsTaken)
                    cellIndex++;

                if (cellIndex >= availableCells.Count)
                {
                    Debug.LogWarning($"[TrainingUnitSpawner] Player {playerNumber} 스폰 구역에 빈 Cell이 부족합니다. " +
                                     $"({spawnedTotal}개 스폰 후 중단)");
                    return;
                }

                var targetCell = availableCells[cellIndex];
                SpawnUnit(prefab, targetCell, playerNumber, facingDirection);
                cellIndex++;
                spawnedTotal++;
            }
        }

        //Debug.Log($"[TrainingUnitSpawner] Player {playerNumber} 유닛 {spawnedTotal}개 스폰 완료");
    }

    /// <summary>
    /// 유닛을 스폰하고 상대 진영을 바라보는 방향으로 설정한다.
    /// </summary>
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

        if (unit is KTFLUnit ktflUnit)
        {
            ktflUnit.SetFacingDirection(facingDirection);
        }

        go.name = $"{prefab.name}_P{playerNumber}";
    }

    // =========================================================================
    // 스폰 셀 검증
    // =========================================================================

    /// <summary>
    /// 셀이 유닛 스폰 가능한지 확인한다.
    /// 다음 조건을 모두 만족해야 스폰 가능:
    /// 1. 물 타일이 아닐 것
    /// 2. 높이가 고립되지 않을 것 (인접 셀 중 높이 차이 1 이하인 비물 셀이 최소 1개)
    /// </summary>
    /// <param name="cell">확인할 셀.</param>
    /// <param name="allCells">맵의 전체 셀 목록 (인접 셀 확인용).</param>
    /// <returns>스폰 가능하면 true.</returns>
    private bool IsSpawnable(ICell cell, List<ICell> allCells)
    {
        // 물 타일 체크
        if (_waterCellType != null && cell is ITypedCell typedCell)
        {
            if (typedCell.CellType.Equals(_waterCellType))
                return false;
        }

        // 높이 고립 체크: 인접 셀 중 이동 가능한 셀이 최소 1개 있는지 확인
        if (cell is Cell unityCell)
        {
            var cellHeight = unityCell.GetComponent<IHeightComponent>();
            if (cellHeight == null) return true; // 높이 컴포넌트가 없으면 체크 스킵

            int myHeight = cellHeight.Height;

            // 인접 셀 검색 (거리 1인 셀)
            bool hasAccessibleNeighbor = allCells.Any(other =>
            {
                if (other.Equals(cell)) return false;
                if (other.GetDistance(cell) != 1) return false;

                // 인접 셀이 물이면 이동 불가
                if (_waterCellType != null && other is ITypedCell otherTyped)
                {
                    if (otherTyped.CellType.Equals(_waterCellType))
                        return false;
                }

                // 높이 차이 1 이하인지 확인
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

    /// <summary>
    /// 전체 유닛 수에 맞춰 스폰 구역 중앙에서 연속된 Cell 블록을 잘라 반환한다.
    /// </summary>
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

    /// <summary>
    /// 유닛 구성을 콘솔에 출력한다.
    /// </summary>
    private void LogComposition(Dictionary<int, int> composition)
    {
        var parts = new List<string>();

        foreach (var kvp in composition)
        {
            string name = _mlUnitPrefabs[kvp.Key] != null ? _mlUnitPrefabs[kvp.Key].name : "null";
            parts.Add($"{name}:{kvp.Value}");
        }

        int total = composition.Sum(kvp => kvp.Value);
        //Debug.Log($"[TrainingUnitSpawner] 이번 에피소드 유닛 구성: {string.Join(", ", parts)} (총 {total}개, 양 진영 동일)");
    }
}