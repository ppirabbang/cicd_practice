using System.Linq;
using TurnBasedStrategyFramework.Common.Cells;
using TurnBasedStrategyFramework.Common.Utilities;
using TurnBasedStrategyFramework.Unity.Cells;
using UnityEngine;

public class ProceduralHexMapGenerator : MonoBehaviour
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

        var existingCoords = cellManager.GetCells()
            .Select(c => new Vector2IntImpl(c.GridCoordinates.x, c.GridCoordinates.y))
            .ToHashSet();

        var cubeOrigin = HexagonHelper.OffsetToCubeCoordinates(
            originCell.GridCoordinates,
            hexOrigin.GridType
        );

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

            Vector3 worldPos = CalculateWorldPosition(offsetTarget, hexOrigin);

            var newCell = CreateCell(worldPos, offsetTarget, hexOrigin, cellManager);
            if (newCell == null) continue;

            cellManager.AddCell(newCell);
            existingCoords.Add(offsetTargetImpl);
            addedCount++;
        }

        Debug.Log($"[ProceduralHexMapGenerator] {addedCount}개 Cell 추가 완료 (중심: {originCell.GridCoordinates})");
    }

    private Hexagon CreateCell(Vector3 worldPos, IVector2Int gridCoords, Hexagon reference, RegularCellManager cellManager)
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

    private Vector3 CalculateWorldPosition(IVector2Int coords, Hexagon reference)
    {
        var dim = reference.CellDimensions;
        float col = coords.x;
        float row = coords.y;
        float worldX, worldZ;

        switch (reference.GridType)
        {
            case HexGridType.odd_r:
                worldX = col * dim.z + (((int)row % 2 != 0) ? dim.z * 0.5f : 0f);
                worldZ = row * dim.x * 0.75f;
                break;
            case HexGridType.even_r:
                worldX = col * dim.z + (((int)row % 2 == 0) ? dim.z * 0.5f : 0f);
                worldZ = row * dim.x * 0.75f;
                break;
            case HexGridType.odd_q:
                worldX = col * dim.x * 0.75f;
                worldZ = row * dim.z + (((int)col % 2 != 0) ? dim.z * 0.5f : 0f);
                break;
            case HexGridType.even_q:
                worldX = col * dim.x * 0.75f;
                worldZ = row * dim.z + (((int)col % 2 == 0) ? dim.z * 0.5f : 0f);
                break;
            default:
                worldX = col * dim.z;
                worldZ = row * dim.x * 0.75f;
                break;
        }

        return new Vector3(worldX, reference.transform.position.y, worldZ);
    }
}
