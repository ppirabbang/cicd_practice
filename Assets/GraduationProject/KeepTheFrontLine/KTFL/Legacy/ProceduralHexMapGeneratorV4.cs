using System.Linq;
using TurnBasedStrategyFramework.Common.Cells;
using TurnBasedStrategyFramework.Common.Utilities;
using TurnBasedStrategyFramework.Unity.Cells;
using UnityEngine;

public class ProceduralHexMapGeneratorV4 : MonoBehaviour
{
    [SerializeField] private Hexagon _cellPrefab;
    [SerializeField] private Grid _grid; // 씬의 Grid 컴포넌트 연결

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
        if (_cellPrefab == null || _grid == null)
        {
            Debug.LogError("[ProceduralHexMapGenerator] CellPrefab 또는 Grid가 연결되지 않았습니다.");
            return;
        }

        var hexOrigin = originCell as Hexagon;
        if (hexOrigin == null)
        {
            Debug.LogError("[ProceduralHexMapGenerator] originCell이 Hexagon 타입이 아닙니다.");
            return;
        }

        var existingCoords = cellManager.GetCells()
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

            var offsetTarget     = HexagonHelper.CubeToOffsetCoordinates(cubeTarget, hexOrigin.GridType);
            var offsetTargetImpl = new Vector2IntImpl(offsetTarget.x, offsetTarget.y);

            if (existingCoords.Contains(offsetTargetImpl))
                continue;

            // Grid 컴포넌트로 월드 좌표 계산
            Vector3 worldPos = _grid.CellToWorld(new Vector3Int(offsetTarget.x, offsetTarget.y, 0));
            worldPos.y = hexOrigin.transform.position.y;

            var newCell = CreateCell(worldPos, offsetTarget, hexOrigin, cellManager);
            if (newCell == null) continue;

            cellManager.AddCell(newCell);
            existingCoords.Add(offsetTargetImpl);
            addedCount++;
        }

        Debug.Log($"[ProceduralHexMapGenerator] {addedCount}개 Cell 추가 완료 (중심: {originCell.GridCoordinates})");
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
