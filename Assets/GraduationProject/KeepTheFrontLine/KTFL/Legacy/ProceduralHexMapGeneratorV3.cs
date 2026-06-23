using System.Collections.Generic;
using System.Linq;
using TurnBasedStrategyFramework.Common.Cells;
using TurnBasedStrategyFramework.Common.Utilities;
using TurnBasedStrategyFramework.Unity.Cells;
using UnityEngine;

public class ProceduralHexMapGeneratorV3 : MonoBehaviour
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

        var hexSize = _cellPrefab.CellDimensions;
        var existingCoords = cellManager.GetCells()
            .Select(c => new Vector2IntImpl(c.GridCoordinates.x, c.GridCoordinates.y))
            .ToHashSet();

        var cubeOrigin = HexagonHelper.OffsetToCubeCoordinates(
            originCell.GridCoordinates, hexOrigin.GridType);

        // originCell 실제 월드 위치와 공식 계산값의 차이를 보정값으로 사용
        Vector3 originWorldPos = hexOrigin.transform.position;
        Vector3 originFormula  = GridToWorld(originCell.GridCoordinates.x,
                                             originCell.GridCoordinates.y,
                                             hexOrigin.GridType, hexSize);
        Vector3 correction = originWorldPos - originFormula;
        correction.y = 0f;

        int addedCount = 0;
        foreach (var direction in _hexRadius1)
        {
            var cubeTarget = new Vector3IntImpl(
                cubeOrigin.x + direction.x,
                cubeOrigin.y + direction.y,
                cubeOrigin.z + direction.z
            );

            var offsetTarget     = HexagonHelper.CubeToOffsetCoordinates(cubeTarget, hexOrigin.GridType);
            var offsetTargetImpl = new Vector2IntImpl(offsetTarget.x, offsetTarget.y);

            if (existingCoords.Contains(offsetTargetImpl))
                continue;

            // 제너레이터와 동일한 공식 + 씬 배치 보정
            Vector3 worldPos = GridToWorld(offsetTarget.x, offsetTarget.y,
                                           hexOrigin.GridType, hexSize)
                               + correction;
            worldPos.y = originWorldPos.y;

            var newCell = CreateCell(worldPos, offsetTarget, hexOrigin, cellManager);
            if (newCell == null) continue;

            cellManager.AddCell(newCell);
            existingCoords.Add(offsetTargetImpl);
            addedCount++;
        }

        Debug.Log($"[ProceduralHexMapGenerator] {addedCount}개 Cell 추가 완료 (중심: {originCell.GridCoordinates})");
    }

    /// HexagonalHexGridGenerator와 동일한 공식
    private Vector3 GridToWorld(int col, int row, HexGridType gridType, Vector3 hexSize)
    {
        switch (gridType)
        {
            case HexGridType.odd_q:
            case HexGridType.even_q:
                return new Vector3(
                    col * hexSize.x * 0.75f,
                    0f,
                    col * hexSize.z * 0.5f + row * hexSize.z
                );
            case HexGridType.odd_r:
            case HexGridType.even_r:
            default:
                return new Vector3(
                    row * hexSize.z * 0.5f + col * hexSize.z,
                    0f,
                    row * hexSize.x * 0.75f
                );
        }
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
