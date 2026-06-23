using System.Collections.Generic;
using System.Linq;
using TurnBasedStrategyFramework.Common.Cells;
using TurnBasedStrategyFramework.Common.Utilities;
using TurnBasedStrategyFramework.Unity.Cells;
using UnityEngine;

public class ProceduralHexMapGeneratorV2 : MonoBehaviour
{
    [SerializeField] private Hexagon _cellPrefab;

    private static readonly Vector3IntImpl[] _hexRadius1 = new[]
    {
        new Vector3IntImpl( 0,  0,  0),
        new Vector3IntImpl(+1, -1,  0),
        new Vector3IntImpl(+1,  0, -1),
        new Vector3IntImpl( 0, +1, -1),
        new Vector3IntImpl(-1, +1,  0),
        new Vector3IntImpl(-1,  0, +1),
        new Vector3IntImpl( 0, -1, +1),
    };

    public void AddCellsAround(ICell originCell, RegularCellManager cellManager)
    {
        if (_cellPrefab == null)
        {
            Debug.LogError("[ProceduralHexMapGenerator] CellPrefab이 연결되지 않았습니다.");
            return;
        }

        var hexOrigin = originCell as Hexagon;
        if (hexOrigin == null)
        {
            Debug.LogError("[ProceduralHexMapGenerator] originCell이 Hexagon 타입이 아닙니다.");
            return;
        }

        var allCells = cellManager.GetCells().Cast<Hexagon>().ToList();

        // 기존 Cell들의 실제 월드 포지션 차이로 기저 벡터 역산
        if (!TryExtractWorldBasis(hexOrigin, allCells, out Vector3 basisX, out Vector3 basisZ))
        {
            Debug.LogError("[ProceduralHexMapGenerator] 이웃 Cell이 부족해 기저 벡터를 구할 수 없습니다.");
            return;
        }

        var existingCoords = allCells
            .Select(c => new Vector2IntImpl(c.GridCoordinates.x, c.GridCoordinates.y))
            .ToHashSet();

        var cubeOrigin = HexagonHelper.OffsetToCubeCoordinates(
            originCell.GridCoordinates, hexOrigin.GridType);

        int addedCount = 0;
        foreach (var direction in _hexRadius1)
        {
            var cubeTarget = new Vector3IntImpl(
                cubeOrigin.x + direction.x,
                cubeOrigin.y + direction.y,
                cubeOrigin.z + direction.z
            );

            var offsetTarget = HexagonHelper.CubeToOffsetCoordinates(cubeTarget, hexOrigin.GridType);
            var offsetTargetImpl = new Vector2IntImpl(offsetTarget.x, offsetTarget.y);

            if (existingCoords.Contains(offsetTargetImpl))
                continue;

            // originCell의 실제 월드 위치 기준으로 상대 오프셋 계산
            int dx = offsetTarget.x - originCell.GridCoordinates.x;
            int dz = offsetTarget.y - originCell.GridCoordinates.y;
            Vector3 worldPos = hexOrigin.transform.position
                   + basisX * dx
                   + basisZ * dz;

            var newCell = CreateCell(worldPos, offsetTarget, hexOrigin, cellManager);
            if (newCell == null) continue;

            cellManager.AddCell(newCell);
            existingCoords.Add(offsetTargetImpl);
            addedCount++;
        }

        Debug.Log($"[ProceduralHexMapGenerator] {addedCount}개 Cell 추가 완료 (중심: {originCell.GridCoordinates})");
    }

    /// <summary>
    /// 기존 Cell들의 월드 포지션 차이로부터
    /// 그리드 좌표 +1당 월드 이동량(기저 벡터)을 역산합니다.
    /// </summary>
    private bool TryExtractWorldBasis(Hexagon origin, List<Hexagon> allCells,
                                      out Vector3 basisX, out Vector3 basisZ)
    {
        basisX = Vector3.zero;
        basisZ = Vector3.zero;
        bool foundX = false, foundZ = false;

        foreach (var cell in allCells)
        {
            if (cell == origin) continue;
            int dx = cell.GridCoordinates.x - origin.GridCoordinates.x;
            int dz = cell.GridCoordinates.y - origin.GridCoordinates.y;
            Vector3 worldDiff = cell.transform.position - origin.transform.position;

            if (!foundX && dz == 0 && dx != 0)
            {
                basisX = worldDiff / dx;
                foundX = true;
            }
            else if (!foundZ && dx == 0 && dz != 0)
            {
                basisZ = worldDiff / dz;
                foundZ = true;
            }

            if (foundX && foundZ) break;
        }

        // 축방향 이웃이 없는 경우 대각선으로 보완
        if (foundX && !foundZ)
        {
            foreach (var cell in allCells)
            {
                if (cell == origin) continue;
                int dx = cell.GridCoordinates.x - origin.GridCoordinates.x;
                int dz = cell.GridCoordinates.y - origin.GridCoordinates.y;
                if (dx == 0 || dz == 0) continue;
                Vector3 worldDiff = cell.transform.position - origin.transform.position;
                basisZ = (worldDiff - basisX * dx) / dz;
                foundZ = true;
                break;
            }
        }
        else if (!foundX && foundZ)
        {
            foreach (var cell in allCells)
            {
                if (cell == origin) continue;
                int dx = cell.GridCoordinates.x - origin.GridCoordinates.x;
                int dz = cell.GridCoordinates.y - origin.GridCoordinates.y;
                if (dx == 0 || dz == 0) continue;
                Vector3 worldDiff = cell.transform.position - origin.transform.position;
                basisX = (worldDiff - basisZ * dz) / dx;
                foundX = true;
                break;
            }
        }

        return foundX && foundZ;
    }

    private Hexagon CreateCell(Vector3 worldPos, IVector2Int gridCoords,
                               Hexagon reference, RegularCellManager cellManager)
    {
        var go = Instantiate(_cellPrefab.gameObject, cellManager.transform);
        var newCell = go.GetComponent<Hexagon>();

        if (newCell == null)
        {
            Debug.LogError("[ProceduralHexMapGenerator] 프리팹에 Hexagon 컴포넌트가 없습니다.");
            Destroy(go);
            return null;
        }

        newCell.transform.position = worldPos;
        newCell.GridCoordinates = gridCoords;
        newCell.GridType = reference.GridType;
        go.name = $"{_cellPrefab.name}_{gridCoords}";

        return newCell;
    }
}
