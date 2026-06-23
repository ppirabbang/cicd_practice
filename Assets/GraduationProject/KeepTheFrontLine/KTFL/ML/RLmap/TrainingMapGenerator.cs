using System.Collections.Generic;
using System.Linq;
using TurnBasedStrategyFramework.Common.Cells;
using TurnBasedStrategyFramework.Common.Utilities;
using TurnBasedStrategyFramework.Unity.Cells;
using UnityEngine;

/// <summary>
/// 학습 전용 대칭 맵 생성기.
/// 맵 우측 절반을 절차적으로 생성한 뒤 좌측에 거울 복사하여
/// 양 진영이 공평한 지형을 배정받도록 한다.
/// 
/// 고립 지형 방지:
/// 1. FloodFill 기반 연결성 검사 (높이 차이 1 이하)
/// 2. 브릿지 높이를 점진적으로 전환 (각 셀 간 높이 차이 ≤1 보장)
/// 3. 스폰 구역 간 도달 가능 검증
/// 4. 검증 실패 시 새 시드로 재생성
/// </summary>
public class TrainingMapGenerator : MonoBehaviour
{
    // =========================================================================
    // Inspector 설정값
    // =========================================================================

    [Header("참조")]
    [SerializeField] private Grid _grid;
    [SerializeField] private RegularCellManager _cellManager;

    [Header("맵 설정")]
    [SerializeField] private int _defaultWidth = 10;
    [SerializeField] private int _defaultHeight = 10;
    [SerializeField] private Vector2Int _startCoords = Vector2Int.zero;

    [Tooltip("0이면 매 에피소드마다 랜덤 시드 사용.")]
    [SerializeField] private int _seed = 0;

    [Header("지형 프리팹 - 평지 (index 0~3 = 높이 0~3)")]
    [SerializeField] private Hexagon[] _plainsPrefabs = new Hexagon[4];

    [Header("지형 프리팹 - 숲 (index 0~3 = 높이 0~3)")]
    [SerializeField] private Hexagon[] _forestPrefabs = new Hexagon[4];

    [Header("지형 프리팹 - 물 (높이 0 고정)")]
    [SerializeField] private Hexagon _waterPrefab;

    [Header("노이즈 설정")]
    [SerializeField] private float _heightNoiseScale = 0.08f;
    [SerializeField] private float _forestNoiseScale = 0.15f;
    [SerializeField][Range(0f, 1f)] private float _forestThreshold = 0.62f;

    [Header("강 설정 (가로 방향 강)")]
    [Tooltip("강을 배치할지 여부.")]
    [SerializeField] private bool _useRivers = true;

    [Tooltip("강이 생성될 확률 (0~1). _useRivers가 true일 때만 적용.")]
    [SerializeField][Range(0f, 1f)] private float _riverChance = 0.3f;

    [Tooltip("Player 0 스폰 구역에서 안쪽으로 몇 행 떨어진 위치에 강을 배치할지.")]
    [SerializeField][Range(1, 5)] private int _riverInsetFromSpawn = 2;

    [Tooltip("강에 생성할 도하 지점(gap) 개수. Y축 중앙 기준 대칭 배치.")]
    [SerializeField][Range(1, 4)] private int _riverGapCount = 2;

    [Tooltip("각 도하 지점의 너비 (셀 수).")]
    [SerializeField][Range(1, 3)] private int _riverGapWidth = 1;

    [Header("맵 검증")]
    [Tooltip("맵 생성 실패(고립 지형) 시 최대 재생성 시도 횟수.")]
    [SerializeField] private int _maxRegenerateAttempts = 10;

    // =========================================================================
    // 내부 타입
    // =========================================================================

    private enum TerrainType { Plains, Forest, Water }

    // =========================================================================
    // 생성 결과
    // =========================================================================

    private List<ICell> _lastGeneratedCells = new List<ICell>();

    public IEnumerable<ICell> GetGeneratedCells() => _lastGeneratedCells;
    public int LastHeight { get; private set; }
    public int LastStartY { get; private set; }

    // =========================================================================
    // 맵 정리 API
    // =========================================================================

    public void ClearMap()
    {
        for (int i = _cellManager.transform.childCount - 1; i >= 0; i--)
        {
            Destroy(_cellManager.transform.GetChild(i).gameObject);
        }
        _cellManager.ClearCells();
        _lastGeneratedCells.Clear();
    }

    // =========================================================================
    // 맵 생성 API
    // =========================================================================

    public void GenerateMap() => GenerateMap(_defaultWidth, _defaultHeight, _startCoords);

    /// <summary>
    /// 좌우 대칭 맵을 생성한다.
    /// 스폰 구역 간 도달 불가 시 새 시드로 재생성한다 (최대 _maxRegenerateAttempts회).
    /// </summary>
    public void GenerateMap(int width, int height, Vector2Int centerCoords)
    {
        if (!ValidateReferences()) return;

        int startX = centerCoords.x - width / 2;
        int startY = centerCoords.y - height / 2;

        for (int attempt = 0; attempt < _maxRegenerateAttempts; attempt++)
        {
            int actualSeed = _seed == 0 ? Random.Range(1, int.MaxValue) : _seed;

            _lastGeneratedCells.Clear();
            LastHeight = height;
            LastStartY = startY;

            // 1. 노이즈 맵 생성
            float[,] heightMap = GenerateNoiseMap(width, height, _heightNoiseScale, actualSeed);
            float[,] forestMap = GenerateNoiseMap(width, height, _forestNoiseScale, actualSeed + 9999);

            // 1.5 높이맵 스무딩: 인접 셀 간 높이 차이가 1을 초과하지 않도록 보정
            SmoothHeightMap(width, height, heightMap);

            // 2. 강 생성
            var riverCells = GenerateHorizontalRivers(width, height);

            // 3. 지형 타입 결정
            var terrainMap = BuildTerrainMap(width, height, heightMap, forestMap, riverCells);

            // 4. X축 좌우 대칭
            ApplyHorizontalSymmetry(width, height, terrainMap, heightMap);

            // 5. 연결성 보장 (브릿지 높이 점진 전환)
            EnsureConnectivity(width, height, terrainMap, heightMap);

            // 6. 스폰 구역 도달 가능 검증
            if (ValidateSpawnReachability(width, height, terrainMap, heightMap))
            {
                int createdCount = PlaceCells(width, height, startX, startY, terrainMap, heightMap);
                Debug.Log($"[TrainingMapGenerator] 완료 - {createdCount}개 Cell, Seed: {actualSeed}" +
                          (attempt > 0 ? $" (재생성 {attempt}회)" : ""));
                return;
            }

            Debug.LogWarning($"[TrainingMapGenerator] 스폰 구역 도달 불가. 재생성 시도 {attempt + 1}/{_maxRegenerateAttempts}");
        }

        // 모든 시도 실패 → 강 없이 평탄한 맵 생성
        Debug.LogWarning("[TrainingMapGenerator] 재생성 한도 초과. 강 없는 맵을 생성합니다.");
        GenerateFallbackMap(width, height, startX, startY);
    }

    /// <summary>
    /// 모든 재생성 시도가 실패했을 때 사용하는 안전한 맵.
    /// 강 없이, 높이 변화를 줄여 고립이 발생하지 않도록 한다.
    /// </summary>
    private void GenerateFallbackMap(int width, int height, int startX, int startY)
    {
        _lastGeneratedCells.Clear();

        int fallbackSeed = Random.Range(1, int.MaxValue);
        // 높이 스케일을 줄여 높이 변화를 완만하게
        float[,] heightMap = GenerateNoiseMap(width, height, _heightNoiseScale * 0.5f, fallbackSeed);
        float[,] forestMap = GenerateNoiseMap(width, height, _forestNoiseScale, fallbackSeed + 9999);

        SmoothHeightMap(width, height, heightMap);

        // 강 없이 생성
        var terrainMap = BuildTerrainMap(width, height, heightMap, forestMap, new HashSet<Vector2Int>());
        ApplyHorizontalSymmetry(width, height, terrainMap, heightMap);
        EnsureConnectivity(width, height, terrainMap, heightMap);

        int createdCount = PlaceCells(width, height, startX, startY, terrainMap, heightMap);
        Debug.Log($"[TrainingMapGenerator] Fallback 맵 완료 - {createdCount}개 Cell");
    }

    // =========================================================================
    // 스폰 구역 도달 검증
    // =========================================================================

    /// <summary>
    /// 좌측 스폰 구역(Player 0)에서 우측 스폰 구역(Player 1)까지
    /// 높이 차이 1 이하로 이동 가능한지 검증한다.
    /// </summary>
    private bool ValidateSpawnReachability(int width, int height,
                                           TerrainType[,] terrainMap, float[,] heightMap)
    {
        // 좌측 스폰 구역에서 이동 가능한 첫 셀 찾기
        Vector2Int? leftStart = null;
        for (int x = 0; x < 3 && leftStart == null; x++)
        {
            for (int y = 0; y < height && leftStart == null; y++)
            {
                if (terrainMap[x, y] != TerrainType.Water)
                    leftStart = new Vector2Int(x, y);
            }
        }

        if (leftStart == null) return false;

        // FloodFill로 도달 가능 영역 계산
        var visited = new bool[width, height];
        var reachable = FloodFill(leftStart.Value.x, leftStart.Value.y,
                                   width, height, terrainMap, heightMap, visited);
        var reachableSet = new HashSet<Vector2Int>(reachable);

        // 우측 스폰 구역에 도달 가능한 셀이 있는지 확인
        for (int x = width - 3; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                if (terrainMap[x, y] != TerrainType.Water
                    && reachableSet.Contains(new Vector2Int(x, y)))
                    return true;
            }
        }

        return false;
    }

    // =========================================================================
    // X축 좌우 대칭 적용
    // =========================================================================

    private void ApplyHorizontalSymmetry(int width, int height,
                                          TerrainType[,] terrainMap, float[,] heightMap)
    {
        int midCol = width / 2;
        for (int y = 0; y < height; y++)
        {
            for (int x = midCol; x < width; x++)
            {
                int mirrorX = width - 1 - x;
                terrainMap[mirrorX, y] = terrainMap[x, y];
                heightMap[mirrorX, y] = heightMap[x, y];
            }
        }
    }

    // =========================================================================
    // 강 생성
    // =========================================================================

    private HashSet<Vector2Int> GenerateHorizontalRivers(int width, int height)
    {
        var riverCells = new HashSet<Vector2Int>();
        if (!_useRivers) return riverCells;

        // 에피소드마다 확률적으로 강 생성 여부 결정
        if (Random.value > _riverChance) return riverCells;

        int leftRiverRow = _riverInsetFromSpawn;
        int rightRiverRow = width - 1 - _riverInsetFromSpawn;

        var gapColumns = CalculateSymmetricGapColumns(height);

        var riverRows = new List<int>();
        if (leftRiverRow >= 0 && leftRiverRow < width)
            riverRows.Add(leftRiverRow);
        if (rightRiverRow >= 0 && rightRiverRow < width && rightRiverRow != leftRiverRow)
            riverRows.Add(rightRiverRow);

        foreach (int row in riverRows)
        {
            for (int y = 0; y < height; y++)
            {
                if (!gapColumns.Contains(y))
                    riverCells.Add(new Vector2Int(row, y));
            }
        }

        return riverCells;
    }

    private HashSet<int> CalculateSymmetricGapColumns(int height)
    {
        var gapCols = new HashSet<int>();
        int midCol = height / 2;
        int upperHalfSize = height - midCol;

        for (int i = 0; i < _riverGapCount; i++)
        {
            int gapCenter = midCol + (upperHalfSize * (i + 1)) / (_riverGapCount + 1);
            int mirrorCenter = height - 1 - gapCenter;

            for (int w = 0; w < _riverGapWidth; w++)
            {
                int upper = gapCenter + w;
                int lower = mirrorCenter - w;
                if (upper >= 0 && upper < height) gapCols.Add(upper);
                if (lower >= 0 && lower < height) gapCols.Add(lower);
            }
        }

        return gapCols;
    }

    // =========================================================================
    // 높이맵 스무딩
    // =========================================================================

    /// <summary>
    /// 높이맵을 반복 스무딩하여 인접 셀 간 높이 인덱스 차이가 1을 초과하지 않도록 한다.
    /// Perlin Noise는 연속적이지만, 높이 인덱스로 양자화하면 인접 셀 간 차이가 2 이상 될 수 있다.
    /// 이 메서드가 그 차이를 해소하여 높이 기반 고립을 원천 차단한다.
    /// 
    /// 알고리즘:
    /// 1. 모든 셀을 순회하며 인접 셀과 높이 인덱스 차이가 2 이상인 경우를 찾는다.
    /// 2. 높은 쪽의 높이를 낮은 쪽 + 1로 낮춘다.
    /// 3. 위반 사항이 없을 때까지 반복한다 (최대 20회).
    /// </summary>
    /// <param name="width">맵 가로 크기.</param>
    /// <param name="height">맵 세로 크기.</param>
    /// <param name="heightMap">스무딩할 높이맵 (직접 수정됨).</param>
    private void SmoothHeightMap(int width, int height, float[,] heightMap)
    {
        int maxPasses = 20;

        for (int pass = 0; pass < maxPasses; pass++)
        {
            bool changed = false;

            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    int myHeight = HeightValueToIndex(heightMap[x, y]);

                    foreach (var neighbor in GetLocalNeighbors(x, y, width, height))
                    {
                        int neighborHeight = HeightValueToIndex(heightMap[neighbor.x, neighbor.y]);
                        int diff = myHeight - neighborHeight;

                        // 이 셀이 이웃보다 2 이상 높으면 낮춘다
                        if (diff >= 2)
                        {
                            int newHeight = neighborHeight + 1;
                            heightMap[x, y] = HeightIndexToValue(newHeight);
                            changed = true;
                            break; // 이 셀은 수정했으므로 다음 셀로
                        }
                        // 이 셀이 이웃보다 2 이상 낮으면 높인다
                        else if (diff <= -2)
                        {
                            int newHeight = neighborHeight - 1;
                            heightMap[x, y] = HeightIndexToValue(newHeight);
                            changed = true;
                            break;
                        }
                    }
                }
            }

            if (!changed)
            {
                return;
            }
        }

        Debug.LogWarning($"[TrainingMapGenerator] 높이맵 스무딩 {maxPasses}회 반복 후에도 완료되지 않았습니다.");
    }

    // =========================================================================
    // 지형 생성
    // =========================================================================

    private float[,] GenerateNoiseMap(int width, int height, float scale, int seed)
    {
        float[,] map = new float[width, height];
        float offsetX = seed * 0.001f;
        float offsetY = seed * 0.0013f;

        for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++)
                map[x, y] = Mathf.PerlinNoise(x * scale + offsetX, y * scale + offsetY);

        return map;
    }

    private TerrainType[,] BuildTerrainMap(int width, int height,
                                            float[,] heightMap, float[,] forestMap,
                                            HashSet<Vector2Int> riverCells)
    {
        var terrainMap = new TerrainType[width, height];
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                if (riverCells.Contains(new Vector2Int(x, y)))
                {
                    terrainMap[x, y] = TerrainType.Water;
                    continue;
                }
                terrainMap[x, y] = forestMap[x, y] > _forestThreshold ? TerrainType.Forest : TerrainType.Plains;
            }
        }
        return terrainMap;
    }

    // =========================================================================
    // 연결성 보장
    // =========================================================================

    private void EnsureConnectivity(int width, int height,
                                     TerrainType[,] terrainMap, float[,] heightMap)
    {
        int iteration = 0;
        int maxIteration = 10;
        int totalFixed = 0;

        while (iteration++ < maxIteration)
        {
            var visited = new bool[width, height];
            var allGroups = new List<List<Vector2Int>>();
            var largestGroup = new List<Vector2Int>();

            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    if (visited[x, y] || terrainMap[x, y] == TerrainType.Water) continue;

                    var group = FloodFill(x, y, width, height, terrainMap, heightMap, visited);
                    allGroups.Add(group);
                    if (group.Count > largestGroup.Count)
                        largestGroup = group;
                }
            }

            var isolatedGroups = allGroups.Where(g => g != largestGroup).ToList();
            if (isolatedGroups.Count == 0)
            {
                Debug.Log($"[TrainingMapGenerator] 연결성 검사 통과 (보정 {totalFixed}회, {iteration - 1}번 반복)");
                return;
            }

            var largestSet = new HashSet<Vector2Int>(largestGroup);
            int fixedThisRound = 0;

            foreach (var isolated in isolatedGroups)
            {
                var result = FindBridgePathWithEndpoints(isolated, largestSet, width, height);
                if (result == null) continue;

                var bridge = result.Value.path;
                int startHeight = HeightValueToIndex(heightMap[result.Value.isolatedEdge.x, result.Value.isolatedEdge.y]);
                int endHeight = HeightValueToIndex(heightMap[result.Value.targetEdge.x, result.Value.targetEdge.y]);

                // 브릿지 높이를 점진적으로 전환 (각 셀 간 높이 차이 ≤1 보장)
                for (int i = 0; i < bridge.Count; i++)
                {
                    var cell = bridge[i];
                    terrainMap[cell.x, cell.y] = TerrainType.Plains;

                    // 시작-끝 높이 사이를 선형 보간하여 점진 전환
                    float t = bridge.Count > 1 ? (float)i / (bridge.Count - 1) : 0f;
                    int targetHeightIndex = Mathf.RoundToInt(Mathf.Lerp(startHeight, endHeight, t));

                    // 이전 셀과의 높이 차이가 1 이하가 되도록 클램프
                    if (i > 0)
                    {
                        int prevHeight = HeightValueToIndex(heightMap[bridge[i - 1].x, bridge[i - 1].y]);
                        targetHeightIndex = Mathf.Clamp(targetHeightIndex, prevHeight - 1, prevHeight + 1);
                    }

                    heightMap[cell.x, cell.y] = HeightIndexToValue(targetHeightIndex);

                    // X축 대칭 위치도 함께 수정
                    int mirrorX = width - 1 - cell.x;
                    if (mirrorX >= 0 && mirrorX < width)
                    {
                        terrainMap[mirrorX, cell.y] = TerrainType.Plains;
                        heightMap[mirrorX, cell.y] = heightMap[cell.x, cell.y];
                    }

                    fixedThisRound++;
                    totalFixed++;
                }
            }

            if (fixedThisRound == 0)
            {
                Debug.LogWarning("[TrainingMapGenerator] 연결성 보정 실패");
                return;
            }
        }

        Debug.LogWarning($"[TrainingMapGenerator] 연결성 보정 최대 반복({maxIteration}회) 초과");
    }

    /// <summary>
    /// 브릿지 경로와 양 끝점(고립측 가장자리, 메인측 가장자리)을 함께 반환한다.
    /// 높이 점진 전환에 양 끝점의 높이가 필요하다.
    /// </summary>
    private (List<Vector2Int> path, Vector2Int isolatedEdge, Vector2Int targetEdge)?
        FindBridgePathWithEndpoints(List<Vector2Int> isolatedGroup,
                                     HashSet<Vector2Int> targetSet,
                                     int width, int height)
    {
        var parentMap = new Dictionary<Vector2Int, Vector2Int>();
        var queue = new Queue<Vector2Int>();
        var visited = new HashSet<Vector2Int>();

        foreach (var cell in isolatedGroup)
        {
            queue.Enqueue(cell);
            visited.Add(cell);
            parentMap[cell] = cell;
        }

        Vector2Int? goalCell = null;

        while (queue.Count > 0 && goalCell == null)
        {
            var current = queue.Dequeue();
            foreach (var neighbor in GetLocalNeighbors(current.x, current.y, width, height))
            {
                if (visited.Contains(neighbor)) continue;
                visited.Add(neighbor);
                parentMap[neighbor] = current;

                if (targetSet.Contains(neighbor))
                {
                    goalCell = neighbor;
                    break;
                }
                queue.Enqueue(neighbor);
            }
        }

        if (goalCell == null) return null;

        // 경로 역추적
        var isolatedSet = new HashSet<Vector2Int>(isolatedGroup);
        var path = new List<Vector2Int>();
        var current2 = parentMap[goalCell.Value];

        while (!isolatedSet.Contains(current2) && !targetSet.Contains(current2))
        {
            path.Add(current2);
            current2 = parentMap[current2];
        }

        // 고립측 가장자리 = 역추적이 끝난 지점
        Vector2Int isolatedEdge = current2;
        // 메인측 가장자리 = goalCell
        Vector2Int targetEdge = goalCell.Value;

        path.Reverse();
        return (path, isolatedEdge, targetEdge);
    }

    // =========================================================================
    // FloodFill
    // =========================================================================

    private List<Vector2Int> FloodFill(int startX, int startY, int width, int height,
                                        TerrainType[,] terrainMap, float[,] heightMap,
                                        bool[,] visited)
    {
        var group = new List<Vector2Int>();
        var queue = new Queue<Vector2Int>();
        queue.Enqueue(new Vector2Int(startX, startY));
        visited[startX, startY] = true;

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            group.Add(current);

            int currentHeight = HeightValueToIndex(heightMap[current.x, current.y]);

            foreach (var neighbor in GetLocalNeighbors(current.x, current.y, width, height))
            {
                if (visited[neighbor.x, neighbor.y]) continue;
                if (terrainMap[neighbor.x, neighbor.y] == TerrainType.Water) continue;

                int neighborHeight = HeightValueToIndex(heightMap[neighbor.x, neighbor.y]);
                if (Mathf.Abs(currentHeight - neighborHeight) <= 1)
                {
                    visited[neighbor.x, neighbor.y] = true;
                    queue.Enqueue(neighbor);
                }
            }
        }

        return group;
    }

    // =========================================================================
    // Cell 배치
    // =========================================================================

    private int PlaceCells(int width, int height, int startX, int startY,
                            TerrainType[,] terrainMap, float[,] heightMap)
    {
        int createdCount = 0;
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                int col = startX + x;
                int row = startY + y;
                var coords = new Vector2IntImpl(col, row);

                Hexagon prefab = SelectPrefab(terrainMap[x, y], heightMap[x, y]);
                if (prefab == null) continue;

                var newCell = CreateAndRegisterCell(coords, prefab);
                if (newCell != null) _lastGeneratedCells.Add(newCell);
                createdCount++;
            }
        }
        return createdCount;
    }

    private Hexagon SelectPrefab(TerrainType terrain, float heightValue)
    {
        if (terrain == TerrainType.Water) return _waterPrefab;

        int heightIndex = HeightValueToIndex(heightValue);
        return terrain == TerrainType.Forest
            ? _forestPrefabs[Mathf.Clamp(heightIndex, 0, _forestPrefabs.Length - 1)]
            : _plainsPrefabs[Mathf.Clamp(heightIndex, 0, _plainsPrefabs.Length - 1)];
    }

    private ICell CreateAndRegisterCell(Vector2IntImpl coords, Hexagon prefab)
    {
        Vector3 worldPos = _grid.CellToWorld(new Vector3Int(coords.x, coords.y, 0));
        worldPos.y = prefab.transform.position.y;

        var go = Instantiate(prefab.gameObject, _cellManager.transform);
        var newCell = go.GetComponent<Hexagon>();

        newCell.transform.position = worldPos;
        newCell.GridCoordinates = coords;
        newCell.GridType = prefab.GridType;
        newCell.MovementCost = 1;
        go.name = $"{prefab.name}_({coords.x},{coords.y})";

        _cellManager.AddCell(newCell);
        return newCell;
    }

    // =========================================================================
    // 유틸리티
    // =========================================================================

    private int HeightValueToIndex(float heightValue)
    {
        if (heightValue < 0.35f) return 0;
        if (heightValue < 0.55f) return 1;
        if (heightValue < 0.75f) return 2;
        return 3;
    }

    /// <summary>
    /// 높이 인덱스(0~3)를 heightMap의 float 값으로 변환한다.
    /// 각 구간의 중앙값을 반환한다.
    /// </summary>
    private float HeightIndexToValue(int index)
    {
        switch (index)
        {
            case 0: return 0.2f;   // 0.00 ~ 0.35 구간 중앙
            case 1: return 0.45f;  // 0.35 ~ 0.55 구간 중앙
            case 2: return 0.65f;  // 0.55 ~ 0.75 구간 중앙
            default: return 0.85f; // 0.75 ~ 1.00 구간 중앙
        }
    }

    private IEnumerable<Vector2Int> GetLocalNeighbors(int x, int y, int width, int height)
    {
        bool isOddRow = Mathf.Abs(y) % 2 != 0;
        var offsets = isOddRow
            ? new[] { new Vector2Int(1,0),  new Vector2Int(1,1),  new Vector2Int(0,1),
                      new Vector2Int(-1,0), new Vector2Int(0,-1), new Vector2Int(1,-1) }
            : new[] { new Vector2Int(1,0),  new Vector2Int(0,1),  new Vector2Int(-1,1),
                      new Vector2Int(-1,0), new Vector2Int(-1,-1),new Vector2Int(0,-1) };

        foreach (var offset in offsets)
        {
            int nx = x + offset.x;
            int ny = y + offset.y;
            if (nx >= 0 && nx < width && ny >= 0 && ny < height)
                yield return new Vector2Int(nx, ny);
        }
    }

    private bool ValidateReferences()
    {
        if (_grid == null)
        { Debug.LogError("[TrainingMapGenerator] Grid가 연결되지 않았습니다."); return false; }
        if (_cellManager == null)
        { Debug.LogError("[TrainingMapGenerator] CellManager가 연결되지 않았습니다."); return false; }
        if (_waterPrefab == null)
        { Debug.LogError("[TrainingMapGenerator] Water Prefab이 연결되지 않았습니다."); return false; }
        if (_plainsPrefabs.Any(p => p == null))
        { Debug.LogError("[TrainingMapGenerator] Plains Prefab이 누락되었습니다."); return false; }
        if (_forestPrefabs.Any(p => p == null))
        { Debug.LogError("[TrainingMapGenerator] Forest Prefab이 누락되었습니다."); return false; }
        return true;
    }
}