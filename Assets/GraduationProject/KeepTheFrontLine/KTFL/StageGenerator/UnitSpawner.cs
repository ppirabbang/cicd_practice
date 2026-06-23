using System.Collections.Generic;
using System.Linq;
using TurnBasedStrategyFramework.Common.Cells;
using TurnBasedStrategyFramework.Unity.Units;
using UnityEngine;

/// <summary>
/// 유닛 스폰 데이터 인터페이스.
/// Inspector 직렬화 또는 ScriptableObject 방식 모두 이 인터페이스를 구현합니다.
/// </summary>
public interface IUnitSpawnData
{
    Unit UnitPrefab { get; }
    int Count { get; }
    int PlayerNumber { get; }
}

/// <summary>
/// Inspector에서 직접 설정하는 유닛 스폰 데이터.
/// 추후 ScriptableObject 방식으로 교체하거나 병행 사용 가능합니다.
/// </summary>
[System.Serializable]
public class UnitSpawnEntry : IUnitSpawnData
{
    [SerializeField] private Unit _unitPrefab;
    [SerializeField] private int _count = 1;
    [SerializeField] private int _playerNumber = 0;

    public Unit UnitPrefab => _unitPrefab;
    public int Count => _count;
    public int PlayerNumber => _playerNumber;
}

/// <summary>
/// 맵 생성 후 유닛을 스폰 구역에 배치합니다.
/// 플레이어 0 -> 맵 하단, 플레이어 1 -> 맵 상단에 스폰됩니다.
/// </summary>
public class UnitSpawner : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private UnityUnitManager _unitManager;

    [Header("Spawn Settings")]
    [Tooltip("스폰 구역 행 깊이. 2면 맵 끝에서 2행 안쪽까지 스폰 구역으로 사용")]
    [SerializeField] private int _spawnDepth = 2;

    [Header("Player 0 Units (하단 스폰)")]
    [SerializeField] private List<UnitSpawnEntry> _player0Units = new List<UnitSpawnEntry>();

    [Header("Player 1 Units (상단 스폰)")]
    [SerializeField] private List<UnitSpawnEntry> _player1Units = new List<UnitSpawnEntry>();

    /// <summary>
    /// 맵의 Cell 목록을 받아 각 플레이어 유닛을 스폰합니다.
    /// ProceduralMapRunner에서 맵 생성 직후 호출합니다.
    /// </summary>
    public void SpawnUnits(IEnumerable<ICell> cells, int mapHeight, int startY)
    {
        if (_unitManager == null)
        {
            Debug.LogError("[UnitSpawner] UnitManager가 연결되지 않았습니다.");
            return;
        }

        var cellList = cells.ToList();

        int bottomMinRow = startY;
        int bottomMaxRow = startY + _spawnDepth - 1;
        int topMinRow = startY + mapHeight - _spawnDepth;
        int topMaxRow = startY + mapHeight - 1;

        var bottomCells = cellList
            .Where(c => c.GridCoordinates.y >= bottomMinRow &&
                        c.GridCoordinates.y <= bottomMaxRow &&
                        !c.IsTaken)
            .ToList();

        var topCells = cellList
            .Where(c => c.GridCoordinates.y >= topMinRow &&
                        c.GridCoordinates.y <= topMaxRow &&
                        !c.IsTaken)
            .ToList();

        int player0Count = _player0Units.Sum(u => u.Count);
        int player1Count = _player1Units.Sum(u => u.Count);

        bottomCells = GetCenteredCells(bottomCells, player0Count);
        topCells = GetCenteredCells(topCells, player1Count);

        SpawnGroup(_player0Units.Cast<IUnitSpawnData>().ToList(), bottomCells, 0);
        SpawnGroup(_player1Units.Cast<IUnitSpawnData>().ToList(), topCells, 1);
    }

    /// <summary>
    /// ScriptableObject 기반 스폰 데이터로도 호출 가능한 오버로드.
    /// 추후 ScriptableObject 방식으로 확장 시 이 메서드를 사용하세요.
    /// </summary>
    public void SpawnUnits(IEnumerable<ICell> cells, int mapHeight, int startY,
                           List<IUnitSpawnData> player0Data, List<IUnitSpawnData> player1Data)
    {
        if (_unitManager == null)
        {
            Debug.LogError("[UnitSpawner] UnitManager가 연결되지 않았습니다.");
            return;
        }

        var cellList = cells.ToList();

        int bottomMinRow = startY;
        int bottomMaxRow = startY + _spawnDepth - 1;
        int topMinRow = startY + mapHeight - _spawnDepth;
        int topMaxRow = startY + mapHeight - 1;

        var bottomCells = cellList
            .Where(c => c.GridCoordinates.y >= bottomMinRow &&
                        c.GridCoordinates.y <= bottomMaxRow &&
                        !c.IsTaken)
            .ToList();

        var topCells = cellList
            .Where(c => c.GridCoordinates.y >= topMinRow &&
                        c.GridCoordinates.y <= topMaxRow &&
                        !c.IsTaken)
            .ToList();

        // 중심 col 기준으로 가까운 순서로 정렬
        int p0Count = player0Data.Sum(u => u.Count);
        int p1Count = player1Data.Sum(u => u.Count);

        bottomCells = GetCenteredCells(bottomCells, p0Count);
        topCells = GetCenteredCells(topCells, p1Count);

        SpawnGroup(player0Data, bottomCells, 0);
        SpawnGroup(player1Data, topCells, 1);
    }

    /// <summary>
    /// 전체 유닛 수에 맞춰 스폰 구역 중앙에서 연속된 Cell 블록을 잘라 반환합니다.
    /// Cell은 x 오름차순(왼→오른) 정렬이 유지되어 Inspector 순서 = 배치 순서가 됩니다.
    /// </summary>
    private List<ICell> GetCenteredCells(List<ICell> cells, int unitCount)
    {
        if (cells.Count == 0 || unitCount <= 0) return new List<ICell>();

        // x 오름차순, 같은 x면 y 오름차순 정렬
        var sorted = cells
            .OrderBy(c => c.GridCoordinates.x)
            .ThenBy(c => c.GridCoordinates.y)
            .ToList();

        int total = sorted.Count;
        int take = Mathf.Min(unitCount, total);
        int startIndex = Mathf.Max(0, (total - take) / 2);

        return sorted.Skip(startIndex).Take(take).ToList();
    }

    private void SpawnGroup(List<IUnitSpawnData> spawnData, List<ICell> availableCells, int playerNumber)
    {
        int cellIndex = 0;
        int spawnedTotal = 0;

        foreach (var data in spawnData)
        {
            if (data.UnitPrefab == null)
            {
                Debug.LogWarning($"[UnitSpawner] Player {playerNumber}의 유닛 Prefab이 null입니다. 스킵합니다.");
                continue;
            }

            for (int i = 0; i < data.Count; i++)
            {
                // 빈 Cell 탐색
                while (cellIndex < availableCells.Count && availableCells[cellIndex].IsTaken)
                    cellIndex++;

                if (cellIndex >= availableCells.Count)
                {
                    Debug.LogWarning($"[UnitSpawner] Player {playerNumber} 스폰 구역에 빈 Cell이 부족합니다. " +
                                     $"({spawnedTotal}개 스폰 후 중단)");
                    return;
                }

                var targetCell = availableCells[cellIndex];
                SpawnUnit(data.UnitPrefab, targetCell, playerNumber);
                cellIndex++;
                spawnedTotal++;
            }
        }

        Debug.Log($"[UnitSpawner] Player {playerNumber} 유닛 {spawnedTotal}개 스폰 완료");
    }

    private void SpawnUnit(Unit prefab, ICell targetCell, int playerNumber)
    {
        var go = Instantiate(prefab.gameObject, _unitManager.transform);
        var unit = go.GetComponent<Unit>();

        unit.PlayerNumber = playerNumber;
        unit.CurrentCell = targetCell;
        go.transform.position = (targetCell as TurnBasedStrategyFramework.Unity.Cells.Cell).transform.position;

        targetCell.IsTaken = true;
        targetCell.CurrentUnits.Add(unit);

        _unitManager.AddUnit(unit);

        go.name = $"{prefab.name}_P{playerNumber}";
    }
}
