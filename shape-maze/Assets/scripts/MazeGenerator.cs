using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class MazeGenerator : MonoBehaviour
{
    public static MazeGenerator Instance { get; private set; }
    public Transform GeneratedRoot => _mazeRoot;
    public bool GameStarted => _started;

    [Header("迷宫")]
    [Range(7, 13)] public int columns = 9;
    [Range(7, 13)] public int rows = 9;
    [Min(2f)] public float cellSize = 3.2f;
    [Min(2.5f)] public float wallHeight = 5.2f;
    public int seed;

    private bool[,] _open;
    private List<Vector2Int> _solution;
    private Transform _mazeRoot;
    private System.Random _random;
    private bool _completed;
    private Vector3 _startPosition;
    private Vector3 _goalPosition;
    private float _worldSize;
    private int _generationCount;
    private int _activeSeed;
    private HashSet<Vector2Int> _pitTiles;
    private int _sphereGateOne;
    private int _sphereGateTwo;
    private int _cubeGate;
    private int _jumpHurdle;
    private int _pitEdge;
    private int _shapeHole;
    private int _hammerWallIndex = -1;
    private int _hammerPickupIndex = -1;
    private int _healPickupIndex = -1;
    private int _shieldPickupIndex = -1;
    private Vector2Int _mandatoryHammerWallTile = new Vector2Int(-1, -1);
    private HashSet<Vector2Int> _fakeWallTiles = new HashSet<Vector2Int>();
    private readonly List<UnityEngine.Object> _generatedAssets = new List<UnityEngine.Object>();
    private readonly Dictionary<Vector3Int, GameObject> _markers = new Dictionary<Vector3Int, GameObject>();
    private bool _started;
    private bool _challengeMode;
    private bool _resultOpen;
    private bool _showKeybinds;
    private MazeAction? _rebindingAction;
    private string _noticeMessage;
    private float _noticeTime;
    private readonly HashSet<Vector3Int> _itemPositions = new HashSet<Vector3Int>();
    private readonly HashSet<Vector2Int> _decorCells = new HashSet<Vector2Int>();

    static Texture2D _uiPixel, _uiClearPixel;
    static GUIStyle _uiTitle, _uiSubtitle, _uiBody, _uiSmall, _uiCardTitle, _uiCardSub, _uiHudTitle, _uiHudBody, _uiHudSmall, _uiButton, _uiSmallButton;

    void Awake()
    {
        Instance = this;
        if (GetComponent<PlayerHealth>() == null) gameObject.AddComponent<PlayerHealth>();
        if (GetComponent<PlayerInventory>() == null) gameObject.AddComponent<PlayerInventory>();
        if (GetComponent<GameSfx>() == null) gameObject.AddComponent<GameSfx>();
        Time.timeScale = 1f;
        GenerateNewLevel();
        _started = false;
        Time.timeScale = 0f;
        SetCursorVisible(true);
    }

    void Update()
    {
        if (_rebindingAction.HasValue)
        {
            if (MazeKeybinds.TryReadPressedKey(out Key pressedKey))
            {
                MazeKeybinds.SetBinding(_rebindingAction.Value, pressedKey);
                _rebindingAction = null;
            }
            return;
        }
        _noticeTime = Mathf.Max(0f, _noticeTime - Time.unscaledDeltaTime);
        if (_started && !_resultOpen)
        {
            PlayerHealth health = GetComponent<PlayerHealth>();
            if (_completed || (health != null && health.CurrentHealth <= 0))
                OpenResultOverlay();
        }
        if (_started && !_resultOpen && MazeKeybinds.WasPressed(MazeAction.Pause))
            OpenMainMenu();
    }

    void OpenResultOverlay()
    {
        if (_resultOpen) return;
        _resultOpen = true;
        Time.timeScale = 0f;
        SetCursorVisible(true);
    }

    public void GenerateNewLevel()
    {
        for (int i = 0; i < _generatedAssets.Count; i++)
            if (_generatedAssets[i] != null) Destroy(_generatedAssets[i]);
        _generatedAssets.Clear();
        if (_mazeRoot != null)
        {
            _mazeRoot.gameObject.SetActive(false);
            Destroy(_mazeRoot.gameObject);
        }
        _markers.Clear();
        _itemPositions.Clear();
        _decorCells.Clear();

        _generationCount++;
        int actualSeed = seed == 0
            ? unchecked(Environment.TickCount + _generationCount * 7919)
            : unchecked(seed + _generationCount - 1);
        _activeSeed = actualSeed;
        _random = new System.Random(actualSeed);
        _completed = false;
        _resultOpen = false;

        GameObject oldPlane = GameObject.Find("Plane");
        if (oldPlane != null) oldPlane.SetActive(false);

        _mazeRoot = new GameObject("GeneratedMaze").transform;
        GeneratePassages();
        AddAlternateRoutes();
        _solution = FindSolution();
        if (_solution.Count == 0)
        {
            for (int x = 0; x < columns - 1; x++)
                OpenCellLink(new Vector2Int(x, 0), new Vector2Int(x + 1, 0));
            for (int y = 0; y < rows - 1; y++)
                OpenCellLink(new Vector2Int(columns - 1, y), new Vector2Int(columns - 1, y + 1));
            _solution = FindSolution();
        }
        EnsureAlternateSolutionRoute();
        _solution = FindSolution();
        PlanMandatoryObstacles();
        PlanFakeWalls();
        BuildFloorAndWalls();
        BuildMarkersAndGoal();
        BuildObstacles();
        PlacePlayerAndCamera();
    }

    public void StartGame(bool challenge)
    {
        _challengeMode = challenge;
        _resultOpen = false;
        PlayerHealth health = GetComponent<PlayerHealth>();
        if (health != null) health.SetStartingHealth(challenge ? 1 : 3);
        EnsureStarEnemyCount(challenge ? 9 : 6);
        _started = true;
        Time.timeScale = 1f;
        SetCursorVisible(false);
        GameSfx.Play(gameObject, GameSound.LevelStart);
    }

    public void OpenMainMenu()
    {
        _resultOpen = false;
        _started = false;
        _showKeybinds = false;
        Time.timeScale = 0f;
        SetCursorVisible(true);
    }

    public void ShowNotice(string message, float seconds = 2f)
    {
        _noticeMessage = message;
        _noticeTime = seconds;
    }

    public void ToggleCursor(Vector2 pointerPosition)
    {
        if (!_started || _resultOpen) return;
        if (Cursor.visible && pointerPosition.x >= 0f && pointerPosition.y >= 0f)
        {
            Vector2 guiPoint = new Vector2(pointerPosition.x, Screen.height - pointerPosition.y);
            if (guiPoint.x <= 490f && guiPoint.y <= 360f) return;
        }
        SetCursorVisible(!Cursor.visible);
    }

    void SetCursorVisible(bool visible)
    {
        Cursor.visible = visible;
        Cursor.lockState = visible ? CursorLockMode.None : CursorLockMode.Locked;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        Time.timeScale = 1f;
        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;
    }

    public void MarkPlayerFoot(Vector3 position)
    {
        if (_mazeRoot == null) return;
        const float spacing = 0.8f;
        RaycastHit[] hits = Physics.RaycastAll(position + Vector3.up * 0.25f, Vector3.down, 4f,
            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        bool surfaceFound = false;
        for (int i = 0; i < hits.Length; i++)
        {
            if (hits[i].collider.transform == transform || hits[i].collider.transform.IsChildOf(transform)) continue;
            position = hits[i].point + Vector3.up * 0.035f;
            surfaceFound = true;
            break;
        }
        if (!surfaceFound) position.y -= 0.45f;
        Vector3Int key = new Vector3Int(Mathf.RoundToInt(position.x / spacing), Mathf.RoundToInt(position.y / 0.25f), Mathf.RoundToInt(position.z / spacing));
        GameObject marker;
        Vector3 markerPosition = position;
        if (_markers.TryGetValue(key, out marker) && marker != null) { marker.transform.position = markerPosition; return; }
        marker = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        marker.name = "PlayerFloorMarker";
        marker.transform.SetParent(_mazeRoot);
        marker.transform.position = markerPosition;
        marker.transform.localScale = new Vector3(0.78f, 0.018f, 0.78f);
        marker.GetComponent<Collider>().enabled = false;
        SetColor(marker.GetComponent<Renderer>(), new Color(0.12f, 0.9f, 1f));
        _markers[key] = marker;
        GameSfx.Play(gameObject, GameSound.Mark);
    }

    void PlanFakeWalls()
    {
        _fakeWallTiles.Clear();
        _mandatoryHammerWallTile = new Vector2Int(-1, -1);
        if (_hammerWallIndex >= 0 && _hammerWallIndex + 1 < _solution.Count)
        {
            Vector2Int from = _solution[_hammerWallIndex];
            Vector2Int to = _solution[_hammerWallIndex + 1];
            int x = from.x * 2 + 1 + to.x - from.x;
            int y = from.y * 2 + 1 + to.y - from.y;
            _mandatoryHammerWallTile = new Vector2Int(x, y);
            _fakeWallTiles.Add(_mandatoryHammerWallTile);
            _open[x, y] = false;
        }
        List<Vector2Int> candidates = new List<Vector2Int>();
        for (int x = 1; x < _open.GetLength(0) - 1; x++)
        for (int y = 1; y < _open.GetLength(1) - 1; y++)
        {
            Vector2Int tile = new Vector2Int(x, y);
            if (_open[x, y] || _fakeWallTiles.Contains(tile)) continue;
            if (_open[x + 1, y] || _open[x - 1, y] || _open[x, y + 1] || _open[x, y - 1])
                candidates.Add(tile);
        }
        for (int i = candidates.Count - 1; i > 0; i--)
        {
            int swap = _random.Next(i + 1);
            Vector2Int temp = candidates[i]; candidates[i] = candidates[swap]; candidates[swap] = temp;
        }
        int decorativeFakeWallCount = _challengeMode ? 18 : 14;
        for (int i = 0; i < Mathf.Min(decorativeFakeWallCount, candidates.Count); i++)
            _fakeWallTiles.Add(candidates[i]);
    }

    void PlanMandatoryObstacles()
    {
        int last = _solution.Count - 2;
        // The player starts as a sphere, so put the sphere-only tunnel first,
        // then a cube-only gate to make the first shape switch meaningful.
        _sphereGateOne = Mathf.Clamp(Mathf.RoundToInt((_solution.Count - 1) * 0.16f), 2, last - 9);
        _cubeGate = Mathf.Clamp(Mathf.RoundToInt((_solution.Count - 1) * 0.34f), _sphereGateOne + 2, last - 7);
        _jumpHurdle = Mathf.Clamp(Mathf.RoundToInt((_solution.Count - 1) * 0.50f), _cubeGate + 2, last - 5);
        _hammerWallIndex = -1;
        _hammerPickupIndex = -1;
        _healPickupIndex = _sphereGateOne + 1;
        if (_solution.Count >= 17)
        {
            _hammerWallIndex = Mathf.Clamp(Mathf.RoundToInt((_solution.Count - 1) * 0.60f), _jumpHurdle + 2, last - 5);
            _hammerPickupIndex = _hammerWallIndex;
            _pitEdge = Mathf.Clamp(Mathf.RoundToInt((_solution.Count - 1) * 0.73f), _hammerWallIndex + 2, last - 3);
        }
        else
        {
            _pitEdge = Mathf.Clamp(Mathf.RoundToInt((_solution.Count - 1) * 0.70f), _jumpHurdle + 2, last - 3);
        }
        _sphereGateTwo = Mathf.Clamp(Mathf.RoundToInt((_solution.Count - 1) * 0.85f), _pitEdge + 2, last - 1);
        _shapeHole = Mathf.Clamp(Mathf.RoundToInt((_solution.Count - 1) * 0.94f), _sphereGateTwo + 1, last);
        _shieldPickupIndex = _pitEdge - 1;

        Vector2Int from = _solution[_pitEdge];
        Vector2Int to = _solution[_pitEdge + 1];
        int gridX = from.x * 2 + 1 + (to.x - from.x);
        int gridY = from.y * 2 + 1 + (to.y - from.y);
        _pitTiles = new HashSet<Vector2Int> { new Vector2Int(gridX, gridY) };

        // Add optional, clearly marked pit rooms off the guaranteed solution route.
        // This raises hazard density without making any of these pits mandatory.
        HashSet<Vector2Int> solutionCells = new HashSet<Vector2Int>(_solution);
        List<Vector2Int> optionalPitCandidates = new List<Vector2Int>();
        for (int x = 0; x < columns; x++)
        for (int y = 0; y < rows; y++)
        {
            Vector2Int cell = new Vector2Int(x, y);
            if (solutionCells.Contains(cell) || !_open[x * 2 + 1, y * 2 + 1]) continue;
            optionalPitCandidates.Add(new Vector2Int(x * 2 + 1, y * 2 + 1));
        }
        for (int i = optionalPitCandidates.Count - 1; i > 0; i--)
        {
            int swap = _random.Next(i + 1);
            Vector2Int temp = optionalPitCandidates[i];
            optionalPitCandidates[i] = optionalPitCandidates[swap];
            optionalPitCandidates[swap] = temp;
        }
        int optionalPitCount = Mathf.Min(_challengeMode ? 5 : 3, optionalPitCandidates.Count);
        for (int i = 0; i < optionalPitCount; i++) _pitTiles.Add(optionalPitCandidates[i]);
    }

    void GeneratePassages()
    {
        int width = columns * 2 + 1;
        int height = rows * 2 + 1;
        _open = new bool[width, height];
        bool[,] visited = new bool[columns, rows];
        List<Vector2Int> stack = new List<Vector2Int>();
        Vector2Int current = Vector2Int.zero;
        visited[0, 0] = true;
        OpenCell(current);
        stack.Add(current);

        Vector2Int[] directions = { Vector2Int.right, Vector2Int.up, Vector2Int.left, Vector2Int.down };
        while (stack.Count > 0)
        {
            current = stack[stack.Count - 1];
            List<Vector2Int> available = new List<Vector2Int>();
            for (int i = 0; i < directions.Length; i++)
            {
                Vector2Int next = current + directions[i];
                if (next.x >= 0 && next.x < columns && next.y >= 0 && next.y < rows && !visited[next.x, next.y])
                    available.Add(directions[i]);
            }

            if (available.Count == 0)
            {
                stack.RemoveAt(stack.Count - 1);
                continue;
            }

            Vector2Int direction = available[_random.Next(available.Count)];
            Vector2Int nextCell = current + direction;
            int currentX = current.x * 2 + 1;
            int currentY = current.y * 2 + 1;
            _open[currentX + direction.x, currentY + direction.y] = true;
            OpenCell(nextCell);
            visited[nextCell.x, nextCell.y] = true;
            stack.Add(nextCell);
        }
    }

    void AddAlternateRoutes()
    {
        List<Vector2Int> closedLinks = new List<Vector2Int>();
        int width = _open.GetLength(0);
        int height = _open.GetLength(1);
        for (int x = 1; x < width - 1; x++)
        for (int y = 1; y < height - 1; y++)
        {
            if (_open[x, y]) continue;
            bool joinsHorizontalCells = (x & 1) == 0 && (y & 1) == 1 && _open[x - 1, y] && _open[x + 1, y];
            bool joinsVerticalCells = (x & 1) == 1 && (y & 1) == 0 && _open[x, y - 1] && _open[x, y + 1];
            if (joinsHorizontalCells || joinsVerticalCells) closedLinks.Add(new Vector2Int(x, y));
        }

        for (int i = closedLinks.Count - 1; i > 0; i--)
        {
            int swap = _random.Next(i + 1);
            Vector2Int temp = closedLinks[i]; closedLinks[i] = closedLinks[swap]; closedLinks[swap] = temp;
        }

        int routeCount = Mathf.Min(closedLinks.Count, Mathf.Clamp(columns * rows / 9, 6, 16));
        for (int i = 0; i < routeCount; i++)
            _open[closedLinks[i].x, closedLinks[i].y] = true;
    }

    void EnsureAlternateSolutionRoute()
    {
        List<Vector2Int> route = FindSolution();
        for (int i = 0; i + 1 < route.Count; i++)
        {
            Vector2Int from = route[i];
            Vector2Int to = route[i + 1];
            int linkX = from.x * 2 + 1 + to.x - from.x;
            int linkY = from.y * 2 + 1 + to.y - from.y;
            _open[linkX, linkY] = false;
            bool hasDetour = FindSolution().Count > 0;
            _open[linkX, linkY] = true;
            if (hasDetour) return;
        }

        for (int i = 1; i + 1 < route.Count; i++)
        {
            Vector2Int from = route[i];
            Vector2Int to = route[i + 1];
            Vector2Int direction = to - from;
            Vector2Int perpendicular = new Vector2Int(-direction.y, direction.x);
            for (int side = -1; side <= 1; side += 2)
            {
                Vector2Int offset = perpendicular * side;
                Vector2Int detourFrom = from + offset;
                Vector2Int detourTo = to + offset;
                if (!InsideMaze(detourFrom) || !InsideMaze(detourTo)) continue;
                OpenCellLink(from, detourFrom);
                OpenCellLink(detourFrom, detourTo);
                OpenCellLink(detourTo, to);
                int directX = from.x * 2 + 1 + direction.x;
                int directY = from.y * 2 + 1 + direction.y;
                _open[directX, directY] = false;
                bool hasDetour = FindSolution().Count > 0;
                _open[directX, directY] = true;
                if (hasDetour) return;
            }
        }
    }

    bool InsideMaze(Vector2Int cell)
    {
        return cell.x >= 0 && cell.x < columns && cell.y >= 0 && cell.y < rows;
    }

    void OpenCellLink(Vector2Int from, Vector2Int to)
    {
        int x = from.x * 2 + 1 + to.x - from.x;
        int y = from.y * 2 + 1 + to.y - from.y;
        _open[x, y] = true;
    }

    void OpenCell(Vector2Int cell)
    {
        _open[cell.x * 2 + 1, cell.y * 2 + 1] = true;
    }

    List<Vector2Int> FindSolution()
    {
        Vector2Int start = Vector2Int.zero;
        Vector2Int goal = new Vector2Int(columns - 1, rows - 1);
        Queue<Vector2Int> queue = new Queue<Vector2Int>();
        Dictionary<Vector2Int, Vector2Int> previous = new Dictionary<Vector2Int, Vector2Int>();
        queue.Enqueue(start);
        previous[start] = start;
        Vector2Int[] directions = { Vector2Int.right, Vector2Int.up, Vector2Int.left, Vector2Int.down };

        while (queue.Count > 0 && !previous.ContainsKey(goal))
        {
            Vector2Int cell = queue.Dequeue();
            for (int i = 0; i < directions.Length; i++)
            {
                Vector2Int next = cell + directions[i];
                if (next.x < 0 || next.x >= columns || next.y < 0 || next.y >= rows || previous.ContainsKey(next))
                    continue;
                int x = cell.x * 2 + 1;
                int y = cell.y * 2 + 1;
                if (!_open[x + directions[i].x, y + directions[i].y]) continue;
                previous[next] = cell;
                queue.Enqueue(next);
            }
        }

        List<Vector2Int> path = new List<Vector2Int>();
        Vector2Int cursor = goal;
        if (!previous.ContainsKey(goal)) return path;
        path.Add(cursor);
        int reconstructionSteps = 0;
        while (cursor != start && reconstructionSteps++ < columns * rows)
        {
            cursor = previous[cursor];
            path.Add(cursor);
        }
        if (cursor != start) return new List<Vector2Int>();
        path.Reverse();
        return path;
    }

    void BuildFloorAndWalls()
    {
        int width = _open.GetLength(0);
        int height = _open.GetLength(1);
        _worldSize = Mathf.Max(width, height) * cellSize;
        List<Vector3> floorVertices = new List<Vector3>();
        List<int> floorTriangles = new List<int>();
        List<Vector2> floorUvs = new List<Vector2>();
        List<CombineInstance> wallParts = new List<CombineInstance>();
        GameObject cubePrototype = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Mesh cubeMesh = cubePrototype.GetComponent<MeshFilter>().sharedMesh;
        Material cubeMaterial = cubePrototype.GetComponent<Renderer>().sharedMaterial;

        for (int x = 0; x < width; x++)
        for (int y = 0; y < height; y++)
        {
            Vector3 position = GridToWorld(x, y);
            if (_open[x, y] || _fakeWallTiles.Contains(new Vector2Int(x, y)))
            {
                Vector2Int tile = new Vector2Int(x, y);
                if (_pitTiles.Contains(tile))
                {
                    GameObject pit = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    pit.name = "Obstacle1_PitBottom";
                    pit.transform.SetParent(_mazeRoot);
                    pit.transform.position = position + Vector3.down * 1.5f;
                    pit.transform.localScale = new Vector3(cellSize, 0.08f, cellSize);
                    pit.GetComponent<Collider>().enabled = false;
                    SetColor(pit.GetComponent<Renderer>(), new Color(0.08f, 0.1f, 0.12f));
                    BuildPitWarningBorder(position);
                    continue;
                }
                AddFloorQuad(floorVertices, floorTriangles, floorUvs, position, cellSize);
            }
            else
            {
                if (_fakeWallTiles.Contains(new Vector2Int(x, y))) continue;
                float segmentHeight = GetWallSegmentHeight(x, y);
                position.y = segmentHeight * 0.5f;
                wallParts.Add(new CombineInstance
                {
                    mesh = cubeMesh,
                    transform = Matrix4x4.TRS(position, Quaternion.identity,
                        new Vector3(cellSize, segmentHeight, cellSize))
                });
            }
        }

        Mesh floorMesh = new Mesh { name = "MazeWalkableFloorMesh" };
        floorMesh.SetVertices(floorVertices);
        floorMesh.SetUVs(0, floorUvs);
        floorMesh.SetTriangles(floorTriangles, 0);
        floorMesh.RecalculateNormals();
        _generatedAssets.Add(floorMesh);
        GameObject floor = new GameObject("MazeFloor");
        floor.transform.SetParent(_mazeRoot);
        floor.AddComponent<MeshFilter>().sharedMesh = floorMesh;
        Material floorMaterial = new Material(cubeMaterial);
        floorMaterial.mainTexture = CreateGroundTexture();
        floorMaterial.mainTexture.wrapMode = TextureWrapMode.Repeat;
        floor.AddComponent<MeshRenderer>().sharedMaterial = floorMaterial;
        _generatedAssets.Add(floorMaterial);
        floor.AddComponent<MeshCollider>().sharedMesh = floorMesh;
        SetColor(floor.GetComponent<Renderer>(), new Color(0.22f, 0.29f, 0.34f));

        Mesh wallMesh = new Mesh { name = "MazeWallsMesh" };
        wallMesh.CombineMeshes(wallParts.ToArray(), true, true);
        _generatedAssets.Add(wallMesh);
        GameObject walls = new GameObject("MazeWalls");
        walls.transform.SetParent(_mazeRoot);
        walls.AddComponent<MeshFilter>().sharedMesh = wallMesh;
        walls.AddComponent<MeshRenderer>().sharedMaterial = cubeMaterial;
        walls.AddComponent<MeshCollider>().sharedMesh = wallMesh;
        SetColor(walls.GetComponent<Renderer>(), new Color(0.66f, 0.72f, 0.76f));
        Destroy(cubePrototype);
        foreach (Vector2Int fakeTile in _fakeWallTiles)
            BuildFakeWall(GridToWorld(fakeTile.x, fakeTile.y), GetWallSegmentHeight(fakeTile.x, fakeTile.y));

        GameObject catchFloor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        catchFloor.name = "MazeFallResetTrigger";
        catchFloor.transform.SetParent(_mazeRoot);
        catchFloor.transform.position = Vector3.down * 6f;
        catchFloor.transform.localScale = new Vector3(width * cellSize + 8f, 0.4f, height * cellSize + 8f);
        catchFloor.GetComponent<Collider>().isTrigger = true;
        catchFloor.GetComponent<Renderer>().enabled = false;
        catchFloor.AddComponent<MazeFallReset>();
    }

    float GetWallSegmentHeight(int x, int y)
    {
        uint hash = unchecked((uint)_activeSeed ^ ((uint)x * 0x9E3779B9u) ^ ((uint)y * 0x85EBCA6Bu));
        hash ^= hash >> 16;
        hash = unchecked(hash * 0x7FEB352Du);
        hash ^= hash >> 15;
        float tier = (hash & 0xFFFFu) / 65535f;
        hash = unchecked(hash * 1664525u + 1013904223u);
        float variation = (hash & 0xFFFFu) / 65535f;

        // Keep the outside boundary safely above the flight jump; only interior
        // walls may be short enough for the pickup to clear.
        if (x == 0 || y == 0 || x == _open.GetLength(0) - 1 || y == _open.GetLength(1) - 1)
            return wallHeight * Mathf.Lerp(1.45f, 1.75f, variation);
        if (tier < 0.52f) return wallHeight * Mathf.Lerp(0.52f, 0.70f, variation);
        if (tier < 0.82f) return wallHeight * Mathf.Lerp(0.90f, 1.12f, variation);
        return wallHeight * Mathf.Lerp(1.45f, 1.75f, variation);
    }

    void BuildFakeWall(Vector3 center, float segmentHeight)
    {
        GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wall.name = "Obstacle2_FakeWall_Cracked";
        wall.transform.SetParent(_mazeRoot);
        wall.transform.position = center + Vector3.up * (segmentHeight * 0.5f);
        wall.transform.localScale = new Vector3(cellSize, segmentHeight, cellSize);
        SetColor(wall.GetComponent<Renderer>(), new Color(0.64f, 0.7f, 0.74f));
        FakeWall fakeWall = wall.AddComponent<FakeWall>();
        int gridX = Mathf.RoundToInt(center.x / cellSize + _open.GetLength(0) * 0.5f - 0.5f);
        int gridY = Mathf.RoundToInt(center.z / cellSize + _open.GetLength(1) * 0.5f - 0.5f);
        fakeWall.Configure(new Vector2Int(gridX, gridY) == _mandatoryHammerWallTile);
        for (int face = 0; face < 4; face++)
        for (int i = 0; i < 3; i++)
        {
            GameObject crack = GameObject.CreatePrimitive(PrimitiveType.Cube);
            crack.name = "FineCrack";
            crack.transform.SetParent(wall.transform, false);
            Vector3 facePosition = face == 0 ? new Vector3(0f, 0f, -0.505f) : face == 1 ? new Vector3(0f, 0f, 0.505f) : face == 2 ? new Vector3(-0.505f, 0f, 0f) : new Vector3(0.505f, 0f, 0f);
            crack.transform.localPosition = facePosition + (face < 2 ? new Vector3((i - 1) * 0.18f, 0.08f + i * 0.12f, 0f) : new Vector3(0f, 0.08f + i * 0.12f, (i - 1) * 0.18f));
            crack.transform.localRotation = Quaternion.Euler(0f, face < 2 ? 0f : 90f, (i - 1) * 24f);
            crack.transform.localScale = new Vector3(0.025f, 0.42f, 0.012f);
            crack.GetComponent<Collider>().enabled = false;
            SetColor(crack.GetComponent<Renderer>(), new Color(0.22f, 0.25f, 0.26f));
        }
        if (_hammerWallIndex >= 0 && _hammerWallIndex + 1 < _solution.Count)
        {
            Vector2Int from = _solution[_hammerWallIndex];
            Vector2Int to = _solution[_hammerWallIndex + 1];
            int wallGridX = from.x * 2 + 1 + to.x - from.x;
            int wallGridY = from.y * 2 + 1 + to.y - from.y;
            Vector3 requiredWall = GridToWorld(wallGridX, wallGridY);
            if ((requiredWall - center).sqrMagnitude < 0.01f)
                AddGateSign(center, new Vector3(to.x - from.x, 0f, to.y - from.y), "E 锤击破坏", new Color(1f, 0.58f, 0.22f), 2.2f, cellSize * 0.5f + 0.04f);
        }
    }

    Texture2D CreateGroundTexture()
    {
        const int resolution = 64;
        Texture2D texture = new Texture2D(resolution, resolution, TextureFormat.RGB24, false);
        texture.name = "MazeGroundTexture";
        texture.filterMode = FilterMode.Point;
        for (int y = 0; y < resolution; y++)
        for (int x = 0; x < resolution; x++)
        {
            bool tileLine = x % 16 == 0 || y % 16 == 0;
            float grain = UnityEngine.Random.Range(-0.035f, 0.035f);
            Color color = tileLine ? new Color(0.52f, 0.61f, 0.65f) : new Color(0.72f + grain, 0.77f + grain, 0.78f + grain);
            texture.SetPixel(x, y, color);
        }
        texture.Apply(false, true);
        _generatedAssets.Add(texture);
        return texture;
    }

    void AddFloorQuad(List<Vector3> vertices, List<int> triangles, List<Vector2> uvs, Vector3 center, float size)
    {
        int start = vertices.Count;
        float half = size * 0.5f;
        vertices.Add(new Vector3(center.x - half, 0f, center.z - half));
        vertices.Add(new Vector3(center.x - half, 0f, center.z + half));
        vertices.Add(new Vector3(center.x + half, 0f, center.z + half));
        vertices.Add(new Vector3(center.x + half, 0f, center.z - half));
        uvs.Add(new Vector2(0f, 0f)); uvs.Add(new Vector2(0f, 1f));
        uvs.Add(new Vector2(1f, 1f)); uvs.Add(new Vector2(1f, 0f));
        triangles.Add(start);
        triangles.Add(start + 1);
        triangles.Add(start + 2);
        triangles.Add(start);
        triangles.Add(start + 2);
        triangles.Add(start + 3);
    }

    void BuildMarkersAndGoal()
    {
        _startPosition = CellToWorld(Vector2Int.zero);
        _goalPosition = CellToWorld(new Vector2Int(columns - 1, rows - 1));
        CreateMarker("Start", _startPosition, new Color(0.2f, 0.85f, 0.35f));
        GameObject goal = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        goal.name = "GoalTrigger";
        goal.transform.SetParent(_mazeRoot);
        goal.transform.position = _goalPosition + Vector3.up * 0.85f;
        goal.transform.localScale = new Vector3(1.15f, 0.85f, 1.15f);
        Collider trigger = goal.GetComponent<Collider>();
        trigger.isTrigger = true;
        SetColor(goal.GetComponent<Renderer>(), new Color(1f, 0.76f, 0.12f));
        goal.AddComponent<MazeGoal>();

        GameObject beacon = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        beacon.name = "GoalBeacon";
        beacon.transform.SetParent(_mazeRoot);
        beacon.transform.position = _goalPosition + Vector3.up * 2.1f;
        beacon.transform.localScale = new Vector3(0.26f, 1.1f, 0.26f);
        beacon.GetComponent<Collider>().enabled = false;
        SetColor(beacon.GetComponent<Renderer>(), new Color(1f, 0.9f, 0.3f));
    }

    void BuildObstacles()
    {
        HashSet<int> used = new HashSet<int>();
        used.Add(_sphereGateOne);
        used.Add(_jumpHurdle);
        used.Add(_cubeGate);
        used.Add(_sphereGateTwo);
        used.Add(_pitEdge);
        used.Add(_pitEdge + 1);
        used.Add(_shapeHole);
        if (_hammerPickupIndex >= 0) used.Add(_hammerPickupIndex);
        used.Add(_healPickupIndex);
        used.Add(_shieldPickupIndex);
        BuildSphereOnlyGate(_sphereGateOne);
        BuildJumpHurdle(_jumpHurdle);
        BuildCubeRequiredGate(_cubeGate);
        BuildSphereOnlyGate(_sphereGateTwo);
        BuildShapeHole(_shapeHole);

        int extraJumpIndex = FindAdditionalObstacleIndex(0.43f, used, 2);
        if (extraJumpIndex >= 0)
        {
            used.Add(extraJumpIndex);
            BuildJumpHurdle(extraJumpIndex);
        }
        int extraShapeIndex = FindAdditionalObstacleIndex(0.78f, used, 2);
        if (extraShapeIndex >= 0)
        {
            used.Add(extraShapeIndex);
            BuildCubeRequiredGate(extraShapeIndex);
        }

        int spikeTarget = _challengeMode ? 14 : 10;
        int spikesBuilt = 0;
        int spikeAttempts = 0;
        while (spikesBuilt < spikeTarget && spikeAttempts++ < _solution.Count * 8)
        {
            int index = _random.Next(1, Mathf.Max(2, _solution.Count - 1));
            if (used.Contains(index)) continue;
            used.Add(index);
            BuildMovingSpike(index);
            spikesBuilt++;
        }
        if (spikesBuilt < spikeTarget)
        {
            HashSet<Vector2Int> occupiedSpikeCells = new HashSet<Vector2Int>(_decorCells);
            foreach (int index in used)
                if (index >= 0 && index < _solution.Count) occupiedSpikeCells.Add(_solution[index]);
            foreach (Vector3Int item in _itemPositions) occupiedSpikeCells.Add(new Vector2Int(item.x, item.z));
            List<Vector2Int> spareSpikeCells = new List<Vector2Int>();
            for (int x = 0; x < columns; x++)
            for (int y = 0; y < rows; y++)
            {
                Vector2Int cell = new Vector2Int(x, y);
                if (cell == Vector2Int.zero || cell == new Vector2Int(columns - 1, rows - 1) || occupiedSpikeCells.Contains(cell)) continue;
                spareSpikeCells.Add(cell);
            }
            for (int i = spareSpikeCells.Count - 1; i > 0; i--)
            {
                int swap = _random.Next(i + 1);
                Vector2Int temp = spareSpikeCells[i]; spareSpikeCells[i] = spareSpikeCells[swap]; spareSpikeCells[swap] = temp;
            }
            Vector2Int[] axes = { Vector2Int.right, Vector2Int.up, Vector2Int.left, Vector2Int.down };
            int additional = Mathf.Min(spikeTarget - spikesBuilt, spareSpikeCells.Count);
            for (int i = 0; i < additional; i++)
            {
                Vector2Int cell = spareSpikeCells[i];
                _decorCells.Add(cell);
                int solutionIndex = _solution.IndexOf(cell);
                if (solutionIndex >= 0) used.Add(solutionIndex);
                BuildMovingSpikeAt(CellToWorld(cell), axes[_random.Next(axes.Length)]);
                spikesBuilt++;
            }
        }

        SpawnExtraItems(used);
        BuildStarEnemies(used);
    }

    int FindAdditionalObstacleIndex(float pathFraction, HashSet<int> used, int minGap)
    {
        int desired = Mathf.RoundToInt((_solution.Count - 1) * pathFraction);
        for (int offset = 0; offset < _solution.Count; offset++)
        {
            int left = desired - offset;
            if (IsFreeObstacleIndex(left, used, minGap)) return left;
            int right = desired + offset;
            if (offset > 0 && IsFreeObstacleIndex(right, used, minGap)) return right;
        }
        return -1;
    }

    bool IsFreeObstacleIndex(int index, HashSet<int> used, int minGap)
    {
        if (index < 1 || index >= _solution.Count - 1) return false;
        foreach (int occupiedIndex in used)
            if (Mathf.Abs(index - occupiedIndex) < minGap) return false;
        return true;
    }

    void BuildStarEnemies(HashSet<int> used)
    {
        List<Vector2Int> cells = new List<Vector2Int>();
        HashSet<Vector2Int> occupied = new HashSet<Vector2Int>(_decorCells);
        foreach (int index in used)
            if (index >= 0 && index < _solution.Count) occupied.Add(_solution[index]);
        for (int x = 0; x < columns; x++)
        for (int y = 0; y < rows; y++)
        {
            Vector2Int cell = new Vector2Int(x, y);
            if (cell.sqrMagnitude > 5 && cell != new Vector2Int(columns - 1, rows - 1) &&
                !_itemPositions.Contains(new Vector3Int(x, 0, y)) && !occupied.Contains(cell) && !IsPitRoomCell(cell)) cells.Add(cell);
        }
        for (int i = cells.Count - 1; i > 0; i--)
        {
            int swap = _random.Next(i + 1); Vector2Int temp = cells[i]; cells[i] = cells[swap]; cells[swap] = temp;
        }
        int count = Mathf.Min(_challengeMode ? 12 : 8, cells.Count);
        for (int i = 0; i < count; i++)
            CreateStarEnemy(cells[i]);
    }

    void EnsureStarEnemyCount(int targetCount)
    {
        if (_mazeRoot == null) return;
        StarEnemy[] current = _mazeRoot.GetComponentsInChildren<StarEnemy>(true);
        if (current.Length > targetCount)
        {
            for (int i = targetCount; i < current.Length; i++)
                if (current[i] != null) Destroy(current[i].gameObject);
            return;
        }

        HashSet<Vector2Int> occupied = new HashSet<Vector2Int>(_decorCells);
        foreach (Vector3Int item in _itemPositions) occupied.Add(new Vector2Int(item.x, item.z));
        for (int i = 0; i < current.Length; i++)
            if (current[i] != null) occupied.Add(WorldToCell(current[i].transform.position));
        occupied.Add(Vector2Int.zero);
        occupied.Add(new Vector2Int(columns - 1, rows - 1));
        for (int i = 0; i < _solution.Count; i++)
            if (i == _sphereGateOne || i == _sphereGateTwo || i == _cubeGate ||
                i == _jumpHurdle || i == _pitEdge || i == _pitEdge + 1 || i == _shapeHole)
                occupied.Add(_solution[i]);

        List<Vector2Int> candidates = new List<Vector2Int>();
        for (int x = 0; x < columns; x++)
        for (int y = 0; y < rows; y++)
        {
            Vector2Int cell = new Vector2Int(x, y);
            if (cell.sqrMagnitude > 5 && !occupied.Contains(cell) && !IsPitRoomCell(cell)) candidates.Add(cell);
        }
        for (int i = candidates.Count - 1; i > 0; i--)
        {
            int swap = _random.Next(i + 1);
            Vector2Int temp = candidates[i]; candidates[i] = candidates[swap]; candidates[swap] = temp;
        }
        int toAdd = Mathf.Min(targetCount - current.Length, candidates.Count);
        for (int i = 0; i < toAdd; i++) CreateStarEnemy(candidates[i]);
    }

    void CreateStarEnemy(Vector2Int cell)
    {
        GameObject enemy = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        enemy.name = "Obstacle2_StarEnemy";
        enemy.transform.SetParent(_mazeRoot);
        enemy.transform.position = CellToWorld(cell) + Vector3.up * 0.42f;
        enemy.transform.localScale = Vector3.one * 0.95f;
        enemy.GetComponent<SphereCollider>().isTrigger = true;
        enemy.AddComponent<StarEnemy>().Configure(transform);
    }

    void BuildMovingSpike(int index)
    {
        Vector2Int direction = _solution[index + 1] - _solution[index];
        BuildMovingSpikeAt(CellToWorld(_solution[index]), direction);
    }

    void BuildMovingSpikeAt(Vector3 center, Vector2Int direction)
    {
        Vector3 axis = direction.x != 0 ? Vector3.forward : Vector3.right;
        GameObject spike = GameObject.CreatePrimitive(PrimitiveType.Cube);
        spike.name = "Obstacle2_MovingSpikeStrip";
        spike.transform.SetParent(_mazeRoot);
        spike.transform.position = center + Vector3.up * 0.24f;
        spike.transform.localScale = direction.x != 0 ? new Vector3(1.3f, 0.42f, 0.42f) : new Vector3(0.42f, 0.42f, 1.3f);
        spike.GetComponent<Collider>().isTrigger = true;
        SetColor(spike.GetComponent<Renderer>(), new Color(0.68f, 0.12f, 0.16f));
        spike.AddComponent<MovingSpike>().Configure(center + Vector3.up * 0.24f, axis);
        for (int i = -1; i <= 1; i++)
        {
            GameObject tooth = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            tooth.name = "SpikeTooth";
            tooth.transform.SetParent(spike.transform, false);
            tooth.transform.localPosition = direction.x != 0 ? new Vector3(0f, 0.55f, i * 0.36f) : new Vector3(i * 0.36f, 0.55f, 0f);
            tooth.transform.localScale = new Vector3(0.15f, 0.28f, 0.15f);
            tooth.GetComponent<Collider>().enabled = false;
            SetColor(tooth.GetComponent<Renderer>(), new Color(0.82f, 0.82f, 0.78f));
        }
    }

    void SpawnExtraItems(HashSet<int> used)
    {
        // Extra shield and healing pickups make the longer route recoverable while
        // keeping the one-item carrying limit meaningful.
        List<MazeItem> items = new List<MazeItem>
        {
            MazeItem.Fly, MazeItem.Fly, MazeItem.Shield, MazeItem.Hammer, MazeItem.Heal, MazeItem.Shield, MazeItem.Heal
        };
        List<Vector2Int> cells = new List<Vector2Int>();
        HashSet<Vector2Int> occupied = new HashSet<Vector2Int>
        {
            _solution[_sphereGateOne], _solution[_cubeGate], _solution[_jumpHurdle],
            _solution[_sphereGateTwo], _solution[_pitEdge], _solution[_pitEdge + 1], _solution[_shapeHole]
        };
        foreach (Vector2Int cell in _decorCells) occupied.Add(cell);

        if (_hammerPickupIndex >= 0 && _hammerPickupIndex < _solution.Count)
        {
            Vector2Int hammerCell = _solution[_hammerPickupIndex];
            _itemPositions.Add(new Vector3Int(hammerCell.x, 0, hammerCell.y));
            BuildItemPickup(MazeItem.Hammer, CellToWorld(hammerCell) + Vector3.up * 0.65f);
            occupied.Add(hammerCell);
            items.Remove(MazeItem.Hammer);
        }
        PlaceGuaranteedItem(MazeItem.Heal, _healPickupIndex, occupied, items);
        PlaceGuaranteedItem(MazeItem.Shield, _shieldPickupIndex, occupied, items);
        foreach (int index in used)
            if (index >= 0 && index < _solution.Count) occupied.Add(_solution[index]);
        for (int x = 0; x < columns; x++)
        for (int y = 0; y < rows; y++)
        {
            Vector2Int cell = new Vector2Int(x, y);
            if (cell != Vector2Int.zero && cell != new Vector2Int(columns - 1, rows - 1) && !IsPitRoomCell(cell)) cells.Add(cell);
        }
        for (int i = cells.Count - 1; i > 0; i--)
        {
            int swap = _random.Next(i + 1); Vector2Int temp = cells[i]; cells[i] = cells[swap]; cells[swap] = temp;
        }
        int nextCell = 0;
        items.Add(MazeItem.Fly);
        items.Add(MazeItem.Fly);
        items.Add(MazeItem.Fly);
        items.Add(MazeItem.Hammer);
        items.Add(MazeItem.Heal);
        items.Add(MazeItem.Shield);
        for (int i = 0; i < items.Count; i++)
        {
            while (nextCell < cells.Count && occupied.Contains(cells[nextCell])) nextCell++;
            if (nextCell < cells.Count)
            {
                Vector2Int cell = cells[nextCell++];
                _itemPositions.Add(new Vector3Int(cell.x, 0, cell.y));
                BuildItemPickup(items[i], CellToWorld(cell) + Vector3.up * 0.65f);
            }
        }
    }

    void PlaceGuaranteedItem(MazeItem item, int pathIndex, HashSet<Vector2Int> occupied, List<MazeItem> remainingItems)
    {
        if (pathIndex < 0 || pathIndex >= _solution.Count) return;
        Vector2Int cell = _solution[pathIndex];
        if (occupied.Contains(cell)) return;
        _itemPositions.Add(new Vector3Int(cell.x, 0, cell.y));
        BuildItemPickup(item, CellToWorld(cell) + Vector3.up * 0.65f);
        occupied.Add(cell);
        remainingItems.Remove(item);
    }

    void BuildItemPickup(MazeItem item, Vector3 position)
    {
        PrimitiveType primitive = item == MazeItem.Hammer || item == MazeItem.Heal ? PrimitiveType.Cube : PrimitiveType.Sphere;
        GameObject pickup = GameObject.CreatePrimitive(primitive);
        pickup.name = "ItemPickup_" + item;
        pickup.transform.SetParent(_mazeRoot);
        pickup.transform.position = position;
        pickup.transform.localScale = item == MazeItem.Hammer ? new Vector3(0.42f, 0.42f, 0.22f) : item == MazeItem.Heal ? Vector3.one * 0.38f : Vector3.one * 0.48f;
        pickup.GetComponent<Collider>().isTrigger = true;
        Color color = item == MazeItem.Hammer ? new Color(0.45f, 0.25f, 0.12f) : item == MazeItem.Fly ? new Color(0.2f, 0.85f, 1f) : item == MazeItem.Shield ? new Color(0.45f, 0.3f, 1f) : new Color(0.2f, 0.95f, 0.35f);
        SetColor(pickup.GetComponent<Renderer>(), color);
        pickup.AddComponent<ItemPickup>().item = item;
    }

    void BuildShapeHole(int index)
    {
        Vector3 center = CellToWorld(_solution[index]);
        Vector2Int direction = _solution[index + 1] - _solution[index];
        Vector3 normal = new Vector3(direction.x, 0f, direction.y);
        Vector3 side = direction.x != 0 ? Vector3.forward : Vector3.right;
        Quaternion wallRotation = Quaternion.LookRotation(normal, Vector3.up);
        float opening = 1.06f;
        float sideWidth = (cellSize - opening) * 0.5f;
        for (int sign = -1; sign <= 1; sign += 2)
        {
            GameObject panel = GameObject.CreatePrimitive(PrimitiveType.Cube);
            panel.name = "Obstacle1_ShapeGate_Side";
            panel.transform.SetParent(_mazeRoot);
            panel.transform.position = center + side * sign * (opening * 0.5f + sideWidth * 0.5f) + Vector3.up * (wallHeight * 0.5f);
            panel.transform.rotation = wallRotation;
            panel.transform.localScale = new Vector3(sideWidth, wallHeight, 0.24f);
            SetColor(panel.GetComponent<Renderer>(), new Color(0.13f, 0.63f, 0.82f));
        }
        GameObject lintel = GameObject.CreatePrimitive(PrimitiveType.Cube);
        lintel.name = "Obstacle1_ShapeGate_Top";
        lintel.transform.SetParent(_mazeRoot);
        lintel.transform.position = center + Vector3.up * (opening + (wallHeight - opening) * 0.5f);
        lintel.transform.rotation = wallRotation;
        lintel.transform.localScale = new Vector3(cellSize, wallHeight - opening, 0.24f);
        SetColor(lintel.GetComponent<Renderer>(), new Color(0.13f, 0.63f, 0.82f));

        const int segments = 12;
        for (int i = 0; i < segments; i++)
        {
            float angle = (i + 0.5f) * Mathf.PI * 2f / segments;
            Vector3 radial = side * Mathf.Cos(angle) + Vector3.up * Mathf.Sin(angle);
            GameObject frame = GameObject.CreatePrimitive(PrimitiveType.Cube);
            frame.name = "Obstacle1_ShapeGate_RoundFrame";
            frame.transform.SetParent(_mazeRoot);
            frame.transform.position = center + Vector3.up * (opening * 0.5f) + radial * 0.72f;
            frame.transform.rotation = wallRotation * Quaternion.Euler(0f, 0f, -angle * Mathf.Rad2Deg);
            frame.transform.localScale = new Vector3(0.34f, 0.22f, 0.28f);
            SetColor(frame.GetComponent<Renderer>(), new Color(0.1f, 0.82f, 1f));
        }
        AddGateSign(center, normal, "圆形通过", new Color(0.1f, 0.82f, 1f), 2.12f);
    }

    void BuildSphereOnlyGate(int index)
    {
        Vector3 center = CellToWorld(_solution[index]);
        Vector2Int direction = _solution[index + 1] - _solution[index];
        Vector3 size = direction.x != 0
            ? new Vector3(0.38f, 0.32f, cellSize)
            : new Vector3(cellSize, 0.32f, 0.38f);
        GameObject lintel = GameObject.CreatePrimitive(PrimitiveType.Cube);
        lintel.name = "Obstacle1_SphereOnlyLowGate";
        lintel.transform.SetParent(_mazeRoot);
        lintel.transform.position = center + Vector3.up * 1.11f;
        lintel.transform.localScale = size;
        SetColor(lintel.GetComponent<Renderer>(), new Color(0.62f, 0.42f, 0.24f));
        Vector3 normal = new Vector3(direction.x, 0f, direction.y);
        AddGateSign(center, normal, "变为圆形通过", new Color(1f, 0.68f, 0.3f), 1.72f);
    }

    void BuildCubeRequiredGate(int index)
    {
        Vector3 center = CellToWorld(_solution[index]);
        Vector2Int direction = _solution[index + 1] - _solution[index];
        Vector3 size = direction.x != 0
            ? new Vector3(0.48f, 1.55f, cellSize)
            : new Vector3(cellSize, 1.55f, 0.48f);
        GameObject gate = GameObject.CreatePrimitive(PrimitiveType.Cube);
        gate.name = "Obstacle1_CubeRequiredGate";
        gate.transform.SetParent(_mazeRoot);
        gate.transform.position = center + Vector3.up * (size.y * 0.5f);
        gate.transform.localScale = size;
        SetColor(gate.GetComponent<Renderer>(), new Color(0.72f, 0.2f, 0.15f));
        gate.AddComponent<ShapeRequirementGate>().Configure(true);
        Vector3 normal = new Vector3(direction.x, 0f, direction.y);
        AddGateSign(center, normal, "变为方形通过", new Color(1f, 0.35f, 0.3f), 1.8f);
    }

    void AddGateSign(Vector3 center, Vector3 normal, string message, Color accent, float height, float faceOffset = 0.31f)
    {
        if (normal.sqrMagnitude < 0.01f) return;
        normal.Normalize();
        for (int side = -1; side <= 1; side += 2)
        {
            Vector3 facing = normal * side;
            Quaternion rotation = Quaternion.LookRotation(facing, Vector3.up);
            Vector3 position = center + facing * faceOffset + Vector3.up * height;

            GameObject signRoot = new GameObject("ShapeGateInstructionSign");
            signRoot.transform.SetParent(_mazeRoot);
            signRoot.transform.position = position;
            signRoot.transform.rotation = rotation;

            GameObject plaque = GameObject.CreatePrimitive(PrimitiveType.Cube);
            plaque.name = "ShapeGateInstructionPlate";
            plaque.transform.SetParent(signRoot.transform, false);
            plaque.transform.localScale = new Vector3(2.4f, 0.46f, 0.07f);
            Collider plaqueCollider = plaque.GetComponent<Collider>();
            if (plaqueCollider != null) plaqueCollider.enabled = false;
            SetColor(plaque.GetComponent<Renderer>(), Color.Lerp(new Color(0.025f, 0.045f, 0.07f), accent, 0.28f));

            GameObject label = new GameObject("ShapeGateInstructionText");
            label.transform.SetParent(signRoot.transform, false);
            label.transform.localPosition = Vector3.forward * 0.045f;
            // TextMesh's readable face points along local -Z; flip it to match
            // the sign's outward-facing side before the whole sign billboards.
            label.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            TextMesh text = label.AddComponent<TextMesh>();
            text.text = message;
            text.anchor = TextAnchor.MiddleCenter;
            text.alignment = TextAlignment.Center;
            text.fontSize = 48;
            text.characterSize = 0.065f;
            text.color = Color.white;
            MeshRenderer textRenderer = label.GetComponent<MeshRenderer>();
            if (textRenderer != null) textRenderer.sortingOrder = 3;
            WorldLabelVisibility visibility = signRoot.AddComponent<WorldLabelVisibility>();
            visibility.maxVisibleDistance = 5.5f;
            visibility.SetFacing(facing);
        }
    }

    void BuildPitWarningBorder(Vector3 center)
    {
        const float thickness = 0.13f;
        const float height = 0.08f;
        Color warning = new Color(1f, 0.08f, 0.12f);
        for (int side = -1; side <= 1; side += 2)
        {
            CreatePitEdge(center + new Vector3(side * (cellSize - thickness) * 0.5f, height * 0.5f, 0f),
                new Vector3(thickness, height, cellSize), warning);
            CreatePitEdge(center + new Vector3(0f, height * 0.5f, side * (cellSize - thickness) * 0.5f),
                new Vector3(cellSize, height, thickness), warning);
        }
    }

    void CreatePitEdge(Vector3 position, Vector3 size, Color color)
    {
        GameObject edge = GameObject.CreatePrimitive(PrimitiveType.Cube);
        edge.name = "PitWarning_RedBorder";
        edge.transform.SetParent(_mazeRoot);
        edge.transform.position = position;
        edge.transform.localScale = size;
        Collider edgeCollider = edge.GetComponent<Collider>();
        if (edgeCollider != null) edgeCollider.enabled = false;
        SetColor(edge.GetComponent<Renderer>(), color);
    }

    void BuildJumpHurdle(int index)
    {
        Vector3 center = CellToWorld(_solution[index]);
        Vector2Int direction = _solution[index + 1] - _solution[index];
        Vector3 size = direction.x != 0
            ? new Vector3(0.5f, 0.56f, cellSize)
            : new Vector3(cellSize, 0.56f, 0.5f);
        GameObject hurdle = GameObject.CreatePrimitive(PrimitiveType.Cube);
        hurdle.name = "Obstacle1_JumpHurdle";
        hurdle.transform.SetParent(_mazeRoot);
        hurdle.transform.position = center + Vector3.up * (size.y * 0.5f);
        hurdle.transform.localScale = size;
        SetColor(hurdle.GetComponent<Renderer>(), new Color(0.96f, 0.48f, 0.08f));
        AddGateSign(center, new Vector3(direction.x, 0f, direction.y), "方形跳跃", new Color(1f, 0.58f, 0.22f), 1.45f);
    }

    void PlacePlayerAndCamera()
    {
        transform.position = _startPosition + Vector3.up * 0.5f;
        transform.rotation = Quaternion.identity;
        ShapeChanger shapeChanger = GetComponent<ShapeChanger>();
        if (shapeChanger != null) shapeChanger.ResetToSphere();
        ResetPlayerMotion();
        PlayerHealth health = GetComponent<PlayerHealth>(); if (health != null) health.ResetHealth();
        PlayerInventory inventory = GetComponent<PlayerInventory>(); if (inventory != null) inventory.ResetInventory();
        Camera mainCamera = Camera.main;
        if (mainCamera == null) return;
        ThirdPersonCam follow = mainCamera.GetComponent<ThirdPersonCam>();
        if (follow != null)
        {
            follow.SetMazeView(Vector3.zero, _worldSize);
            follow.ReturnToThirdPerson();
            follow.ResetQUses();
        }
    }

    public void RestartLevel()
    {
        int previousSeed = seed;
        int previousGenerationCount = _generationCount;
        ThirdPersonCam cameraMode = Camera.main != null ? Camera.main.GetComponent<ThirdPersonCam>() : null;
        int remainingQUses = cameraMode != null ? cameraMode.QUsesLeft : 3;
        seed = _activeSeed;
        _generationCount = 0;
        GenerateNewLevel();
        seed = previousSeed;
        _generationCount = previousGenerationCount;
        if (cameraMode != null) cameraMode.SetQUsesLeft(remainingQUses);
    }

    public void HandlePlayerFall(Vector3 fallPosition)
    {
        if (!_started || fallPosition.y > -4f) return;

        PlayerHealth health = GetComponent<PlayerHealth>();
        if (health != null) health.TakeFallDamage(1);

        Vector2Int destination = Vector2Int.zero;
        float closestDistance = float.PositiveInfinity;
        HashSet<Vector2Int> occupied = new HashSet<Vector2Int>(_decorCells);
        foreach (Vector3Int item in _itemPositions) occupied.Add(new Vector2Int(item.x, item.z));
        StarEnemy[] enemies = _mazeRoot != null ? _mazeRoot.GetComponentsInChildren<StarEnemy>() : new StarEnemy[0];
        for (int i = 0; i < enemies.Length; i++)
            if (enemies[i] != null) occupied.Add(WorldToCell(enemies[i].transform.position));

        for (int i = 0; i < _solution.Count; i++)
        {
            Vector2Int cell = _solution[i];
            if (occupied.Contains(cell) || i == _sphereGateOne || i == _sphereGateTwo ||
                i == _cubeGate || i == _jumpHurdle || i == _pitEdge || i == _pitEdge + 1 || i == _shapeHole)
                continue;
            Vector3 candidate = CellToWorld(cell);
            float distance = (new Vector2(candidate.x - fallPosition.x, candidate.z - fallPosition.z)).sqrMagnitude;
            if (distance < closestDistance)
            {
                closestDistance = distance;
                destination = cell;
            }
        }

        transform.position = CellToWorld(destination) + Vector3.up * 0.55f;
        ResetPlayerMotion();
        if (health != null && health.CurrentHealth <= 0)
        {
            PlayerController controller = GetComponent<PlayerController>();
            if (controller != null) controller.SetDead(true);
        }
        Physics.SyncTransforms();
    }

    void ResetPlayerMotion()
    {
        PlayerController controller = GetComponent<PlayerController>();
        if (controller != null) controller.ResetMotion();
        Rigidbody body = GetComponent<Rigidbody>();
        if (body != null)
        {
            body.position = transform.position;
            body.velocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
        }
        Physics.SyncTransforms();
    }

    void CreateMarker(string markerName, Vector3 position, Color color)
    {
        GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        marker.name = markerName;
        marker.transform.SetParent(_mazeRoot);
        marker.transform.position = position + Vector3.up * 0.025f;
        marker.transform.localScale = new Vector3(1.2f, 0.025f, 1.2f);
        marker.GetComponent<Collider>().enabled = false;
        SetColor(marker.GetComponent<Renderer>(), color);
    }

    Vector3 CellToWorld(Vector2Int cell)
    {
        return GridToWorld(cell.x * 2 + 1, cell.y * 2 + 1);
    }

    public Vector3 GetChaseWaypoint(Vector3 from, Vector3 to)
    {
        Vector2Int start = WorldToCell(from);
        Vector2Int goal = WorldToCell(to);
        if (start == goal) return new Vector3(to.x, from.y, to.z);
        Queue<Vector2Int> queue = new Queue<Vector2Int>();
        Dictionary<Vector2Int, Vector2Int> previous = new Dictionary<Vector2Int, Vector2Int>();
        Vector2Int[] directions = { Vector2Int.right, Vector2Int.up, Vector2Int.left, Vector2Int.down };
        queue.Enqueue(start); previous[start] = start;
        while (queue.Count > 0 && !previous.ContainsKey(goal))
        {
            Vector2Int cell = queue.Dequeue();
            for (int i = 0; i < directions.Length; i++)
            {
                Vector2Int next = cell + directions[i];
                if (next.x < 0 || next.x >= columns || next.y < 0 || next.y >= rows || previous.ContainsKey(next)) continue;
                int x = cell.x * 2 + 1, y = cell.y * 2 + 1;
                if (!_open[x + directions[i].x, y + directions[i].y]) continue;
                previous[next] = cell; queue.Enqueue(next);
            }
        }
        if (!previous.ContainsKey(goal)) return from;
        Vector2Int cursor = goal;
        int steps = 0;
        while (previous[cursor] != start && cursor != start && steps++ < columns * rows) cursor = previous[cursor];
        return CellToWorld(cursor) + Vector3.up * from.y;
    }

    public Vector3 GetChaseRecoveryPoint(Vector3 position)
    {
        return CellToWorld(WorldToCell(position)) + Vector3.up * position.y;
    }

    Vector2Int WorldToCell(Vector3 position)
    {
        int x = Mathf.RoundToInt((position.x / cellSize + columns - 1f) * 0.5f);
        int y = Mathf.RoundToInt((position.z / cellSize + rows - 1f) * 0.5f);
        return new Vector2Int(Mathf.Clamp(x, 0, columns - 1), Mathf.Clamp(y, 0, rows - 1));
    }

    bool IsPitRoomCell(Vector2Int cell)
    {
        return _pitTiles != null && _pitTiles.Contains(new Vector2Int(cell.x * 2 + 1, cell.y * 2 + 1));
    }

    Vector3 GridToWorld(int x, int y)
    {
        int width = columns * 2 + 1;
        int height = rows * 2 + 1;
        return new Vector3((x - (width - 1) * 0.5f) * cellSize, 0f,
            (y - (height - 1) * 0.5f) * cellSize);
    }

    void SetColor(Renderer renderer, Color color)
    {
        if (renderer == null) return;
        MaterialPropertyBlock properties = new MaterialPropertyBlock();
        renderer.GetPropertyBlock(properties);
        properties.SetColor("_Color", color);
        properties.SetColor("_BaseColor", color);
        renderer.SetPropertyBlock(properties);
    }

    public void CompleteMaze()
    {
        if (_completed) return;
        _completed = true;
        GameSfx.Play(gameObject, GameSound.Goal);
    }

    public void NotifyMandatoryFakeWallBroken()
    {
        if (_mandatoryHammerWallTile.x < 0 || _open == null) return;
        _open[_mandatoryHammerWallTile.x, _mandatoryHammerWallTile.y] = true;
        _mandatoryHammerWallTile = new Vector2Int(-1, -1);
    }

    void OnGUI()
    {
        EnsureUIStyles();
        if (!_started)
        {
            if (_showKeybinds)
            {
                DrawKeybindSettings();
                return;
            }
            DrawUIRect(new Rect(0f, 0f, Screen.width, Screen.height), new Color(0.025f, 0.045f, 0.075f, 0.78f));
            float width = Mathf.Min(720f, Screen.width - 32f);
            float height = Mathf.Min(472f, Screen.height - 24f);
            Rect panel = new Rect((Screen.width - width) * 0.5f, (Screen.height - height) * 0.5f, width, height);
            DrawUIPanel(panel, new Color(0.035f, 0.065f, 0.105f, 0.98f));
            float pad = Mathf.Max(22f, width * 0.045f);
            GUI.Label(new Rect(panel.x + pad, panel.y + 24f, width - pad * 2f, 20f), "PROCEDURAL ACTION  /  MAZE RUN", _uiSmall);
            GUI.Label(new Rect(panel.x + pad, panel.y + 48f, width - pad * 2f, 44f), "形态迷宫", _uiTitle);
            GUI.Label(new Rect(panel.x + pad, panel.y + 96f, width - pad * 2f, 30f), "每局随机生成 · 多条路线通向出口 · 找到出口完成挑战", _uiSubtitle);

            float gap = 12f;
            float cardWidth = (width - pad * 2f - gap * 2f) / 3f;
            float cardY = panel.y + 150f;
            DrawInfoCard(new Rect(panel.x + pad, cardY, cardWidth, 76f), "01  形态解谜", MazeKeybinds.GetKeyLabel(MazeKeybinds.GetBinding(MazeAction.Transform)) + " 变形适应专属通道", new Color(0.24f, 0.82f, 1f));
            DrawInfoCard(new Rect(panel.x + pad + cardWidth + gap, cardY, cardWidth, 76f), "02  道具取舍", "一次携带一件 · " + MazeKeybinds.GetKeyLabel(MazeKeybinds.GetBinding(MazeAction.UseItem)) + " 使用", new Color(0.77f, 0.56f, 1f));
            DrawInfoCard(new Rect(panel.x + pad + (cardWidth + gap) * 2f, cardY, cardWidth, 76f), "03  活着抵达", "躲避尖刺与追击者", new Color(1f, 0.65f, 0.27f));

            float buttonY = panel.y + Mathf.Min(252f, height * 0.55f);
            float buttonGap = 14f;
            float buttonWidth = (width - pad * 2f - buttonGap) * 0.5f;
            Rect normalButton = new Rect(panel.x + pad, buttonY, buttonWidth, 78f);
            Rect challengeButton = new Rect(normalButton.xMax + buttonGap, buttonY, buttonWidth, 78f);
            DrawModeButton(normalButton, "普通模式", "3 点生命 · 6 名追击者", false);
            DrawModeButton(challengeButton, "挑战模式", "1 点生命 · 9 名追击者", true);
            if (ConsumeGuiClick(normalButton)) StartFreshRun(false);
            if (ConsumeGuiClick(challengeButton)) StartFreshRun(true);

            Rect bindingsButton = new Rect(panel.xMax - pad - 164f, panel.y + height - 92f, 164f, 38f);
            DrawSmallButton(bindingsButton);
            if (ConsumeGuiClick(bindingsButton)) _showKeybinds = true;
            GUI.Label(new Rect(bindingsButton.x, bindingsButton.y, bindingsButton.width, bindingsButton.height), "按键设置", _uiSmallButton);
            GUI.Label(new Rect(panel.x + pad, panel.y + height - 44f, bindingsButton.x - panel.x - pad - 12f, 22f),
                MazeKeybinds.GetKeyLabel(MazeKeybinds.GetBinding(MazeAction.MoveForward)) + " 前进   鼠标转向   " +
                MazeKeybinds.GetKeyLabel(MazeKeybinds.GetBinding(MazeAction.Jump)) + " 跳跃   " +
                MazeKeybinds.GetKeyLabel(MazeKeybinds.GetBinding(MazeAction.Transform)) + " 变形", _uiSmall);
            return;
        }

        PlayerHealth health = GetComponent<PlayerHealth>();
        PlayerInventory inventory = GetComponent<PlayerInventory>();
        ThirdPersonCam cameraMode = Camera.main != null ? Camera.main.GetComponent<ThirdPersonCam>() : null;
        ShapeChanger shape = GetComponent<ShapeChanger>();
        if (_resultOpen)
        {
            DrawResultOverlay();
            return;
        }
        DrawHUD(health, inventory, cameraMode, shape);
        if (_noticeTime > 0f && !string.IsNullOrEmpty(_noticeMessage))
        {
            Rect notice = new Rect(Screen.width - 436f, 22f, 420f, 42f);
            DrawUIPanel(notice, new Color(0.055f, 0.10f, 0.15f, 0.96f));
            GUI.Label(new Rect(notice.x + 14f, notice.y + 9f, notice.width - 28f, 24f), _noticeMessage, _uiHudBody);
        }
        if (health != null && health.CurrentHealth <= 0)
        {
            Rect gameOver = new Rect(Screen.width * 0.5f - 180f, 70f, 360f, 74f);
            DrawUIPanel(gameOver, new Color(0.20f, 0.045f, 0.065f, 0.96f));
            GUI.Label(new Rect(gameOver.x + 18f, gameOver.y + 10f, gameOver.width - 36f, 28f), "RUN ENDED", _uiHudTitle);
            GUI.Label(new Rect(gameOver.x + 18f, gameOver.y + 42f, gameOver.width - 36f, 22f), "生命耗尽 · 重开本关再试一次", _uiHudSmall);
        }
        if (_completed)
        {
            Rect clear = new Rect(Screen.width * 0.5f - 180f, 70f, 360f, 64f);
            DrawUIPanel(clear, new Color(0.045f, 0.18f, 0.14f, 0.96f));
            GUI.Label(new Rect(clear.x + 18f, clear.y + 15f, clear.width - 36f, 30f), "迷宫已通关  ·  NICE RUN", _uiHudTitle);
        }
        if (!Cursor.visible && Event.current.type == EventType.Repaint)
            DrawCrosshair();
    }

    void DrawResultOverlay()
    {
        DrawUIRect(new Rect(0f, 0f, Screen.width, Screen.height), new Color(0.008f, 0.018f, 0.03f, 0.82f));
        float width = Mathf.Min(560f, Screen.width - 32f);
        float height = 300f;
        Rect panel = new Rect((Screen.width - width) * 0.5f, (Screen.height - height) * 0.5f, width, height);
        DrawUIPanel(panel, new Color(0.035f, 0.065f, 0.105f, 0.99f), _completed ? new Color(0.24f, 0.88f, 0.72f) : new Color(1f, 0.32f, 0.3f));

        GUI.Label(new Rect(panel.x + 28f, panel.y + 24f, width - 56f, 34f),
            _completed ? "迷宫已通关" : "本局结束", _uiTitle);
        GUI.Label(new Rect(panel.x + 28f, panel.y + 68f, width - 56f, 26f),
            _completed ? "选择接下来要挑战的模式" : "生命耗尽了，选择重试或开始新模式", _uiSubtitle);

        float gap = 14f;
        float buttonWidth = (width - 56f - gap) * 0.5f;
        float buttonY = panel.y + 112f;
        Rect normal = new Rect(panel.x + 28f, buttonY, buttonWidth, 64f);
        Rect challenge = new Rect(normal.xMax + gap, buttonY, buttonWidth, 64f);
        DrawModeButton(normal, "普通模式", "3 点生命 · 6 名追击者", false);
        DrawModeButton(challenge, "挑战模式", "1 点生命 · 9 名追击者", true);
        if (ConsumeGuiClick(normal)) StartFreshRun(false);
        if (ConsumeGuiClick(challenge)) StartFreshRun(true);

        Rect retry = new Rect(panel.x + 28f, panel.y + 196f, buttonWidth, 48f);
        Rect menu = new Rect(retry.xMax + gap, retry.y, buttonWidth, 48f);
        DrawUIPanel(retry, new Color(0.06f, 0.105f, 0.15f, 1f), new Color(0.3f, 0.62f, 0.78f));
        DrawUIPanel(menu, new Color(0.06f, 0.105f, 0.15f, 1f));
        if (ConsumeGuiClick(retry)) RetryCurrentMode();
        if (ConsumeGuiClick(menu)) OpenMainMenu();
    }

    void DrawKeybindSettings()
    {
        DrawUIRect(new Rect(0f, 0f, Screen.width, Screen.height), new Color(0.025f, 0.045f, 0.075f, 0.9f));
        float width = Mathf.Min(680f, Screen.width - 32f);
        float height = Mathf.Min(640f, Screen.height - 24f);
        Rect panel = new Rect((Screen.width - width) * 0.5f, (Screen.height - height) * 0.5f, width, height);
        DrawUIPanel(panel, new Color(0.035f, 0.065f, 0.105f, 0.99f), new Color(0.24f, 0.82f, 1f));
        GUI.Label(new Rect(panel.x + 28f, panel.y + 20f, width - 56f, 38f), "按键设置", _uiTitle);
        GUI.Label(new Rect(panel.x + 28f, panel.y + 58f, width - 56f, 24f),
            "点击操作右侧按键，再按下要绑定的键；重复键位会自动与原操作交换。", _uiSubtitle);

        float rowHeight = Mathf.Min(36f, (height - 168f) / Mathf.Max(1, MazeKeybinds.ActionCount));
        float rowY = panel.y + 92f;
        for (int i = 0; i < MazeKeybinds.ActionCount; i++)
        {
            MazeAction action = MazeKeybinds.GetActionAt(i);
            Rect row = new Rect(panel.x + 28f, rowY + i * rowHeight, width - 56f, rowHeight - 3f);
            DrawUIPanel(row, new Color(0.055f, 0.085f, 0.125f, 0.96f));
            GUI.Label(new Rect(row.x + 12f, row.y, row.width - 205f, row.height), MazeKeybinds.GetActionLabel(action), _uiBody);
            Rect bindingButton = new Rect(row.xMax - 184f, row.y + 2f, 174f, row.height - 4f);
            DrawSmallButton(bindingButton);
            string keyLabel = _rebindingAction == action ? "按下任意键…" : MazeKeybinds.GetKeyLabel(MazeKeybinds.GetBinding(action));
            GUI.Label(bindingButton, keyLabel, _uiSmallButton);
            if (ConsumeGuiClick(bindingButton)) _rebindingAction = action;
        }

        float buttonY = panel.yMax - 52f;
        Rect reset = new Rect(panel.x + 28f, buttonY, 180f, 34f);
        Rect back = new Rect(panel.xMax - 208f, buttonY, 180f, 34f);
        DrawSmallButton(reset);
        DrawSmallButton(back);
        GUI.Label(reset, "恢复默认", _uiSmallButton);
        GUI.Label(back, "返回主菜单", _uiSmallButton);
        if (ConsumeGuiClick(reset))
        {
            MazeKeybinds.ResetDefaults();
            _rebindingAction = null;
        }
        if (ConsumeGuiClick(back))
        {
            _rebindingAction = null;
            _showKeybinds = false;
        }
    }

    void StartFreshRun(bool challenge)
    {
        _challengeMode = challenge;
        _showKeybinds = false;
        _rebindingAction = null;
        GenerateNewLevel();
        StartGame(challenge);
    }

    void RetryCurrentMode()
    {
        bool challenge = _challengeMode;
        RestartLevel();
        StartGame(challenge);
    }

    void DrawHUD(PlayerHealth health, PlayerInventory inventory, ThirdPersonCam cameraMode, ShapeChanger shape)
    {
        Rect panel = new Rect(16f, 16f, 376f, 260f);
        DrawUIPanel(panel, new Color(0.025f, 0.045f, 0.07f, 0.88f));
        string mode = _challengeMode ? "CHALLENGE RUN" : "STANDARD RUN";
        GUI.Label(new Rect(30f, 26f, 270f, 24f), mode, _uiHudTitle);
        GUI.Label(new Rect(298f, 29f, 78f, 20f), "MAZE 01", _uiHudSmall);

        GUI.Label(new Rect(30f, 58f, 94f, 23f), "VITALS", _uiHudSmall);
        int currentHealth = health != null ? health.CurrentHealth : 3;
        GUI.Label(new Rect(30f, 78f, 118f, 30f), "生命  " + currentHealth, _uiHudBody);
        int visiblePips = Mathf.Min(currentHealth, 12);
        for (int i = 0; i < visiblePips; i++)
            DrawUIRect(new Rect(154f + i * 16f, 84f, 11f, 12f), i < 3 ? new Color(0.24f, 0.88f, 1f) : new Color(0.42f, 1f, 0.62f));
        if (health != null && health.Shielded)
            GUI.Label(new Rect(266f, 78f, 110f, 24f), "护盾 " + Mathf.CeilToInt(health.ShieldTimeLeft) + " 秒", _uiHudSmall);

        string form = shape != null && shape.IsCube ? "方形" : "圆形";
        string qState = cameraMode == null ? "3 次" : cameraMode.LimitedGodViewActive ? cameraMode.QTimeLeft.ToString("0.0") + " 秒" : cameraMode.QUsesLeft + " 次";
        GUI.Label(new Rect(30f, 106f, 336f, 22f), "形态  " + form + "     Q  俯视剩余  " + qState, _uiHudSmall);

        bool hasItem = inventory != null && inventory.HeldItem != MazeItem.None;
        Color itemAccent = hasItem ? GetItemAccent(inventory.HeldItem) : new Color(0.25f, 0.38f, 0.47f);
        Rect itemCard = new Rect(28f, 134f, 352f, 52f);
        DrawUIPanel(itemCard, new Color(itemAccent.r * 0.24f, itemAccent.g * 0.24f, itemAccent.b * 0.24f, 0.9f), itemAccent);
        GUI.Label(new Rect(itemCard.x + 13f, itemCard.y + 7f, 250f, 20f), hasItem ? "已携带  ·  " + inventory.ItemLabel : "道具槽为空", _uiHudBody);
        GUI.Label(new Rect(itemCard.x + 13f, itemCard.y + 29f, 320f, 17f), hasItem ? "按 E 使用 · 道具栏满时先用掉再捡" : "靠近发光道具拾取 · 一次携带一件", _uiHudSmall);

        GUI.Label(new Rect(30f, 194f, 340f, 20f),
            MazeKeybinds.GetKeyLabel(MazeKeybinds.GetBinding(MazeAction.MoveForward)) + "/" +
            MazeKeybinds.GetKeyLabel(MazeKeybinds.GetBinding(MazeAction.MoveLeft)) + "/" +
            MazeKeybinds.GetKeyLabel(MazeKeybinds.GetBinding(MazeAction.MoveBackward)) + "/" +
            MazeKeybinds.GetKeyLabel(MazeKeybinds.GetBinding(MazeAction.MoveRight)) + " 移动   鼠标转向   " +
            MazeKeybinds.GetKeyLabel(MazeKeybinds.GetBinding(MazeAction.Jump)) + " 跳跃   " +
            MazeKeybinds.GetKeyLabel(MazeKeybinds.GetBinding(MazeAction.Transform)) + " 变形", _uiHudSmall);
        GUI.Label(new Rect(30f, 214f, 340f, 20f),
            "左键/" + MazeKeybinds.GetKeyLabel(MazeKeybinds.GetBinding(MazeAction.Shoot)) + " 射击   " +
            MazeKeybinds.GetKeyLabel(MazeKeybinds.GetBinding(MazeAction.UseItem)) + " 道具   " +
            MazeKeybinds.GetKeyLabel(MazeKeybinds.GetBinding(MazeAction.Mark)) + " 标记   " +
            MazeKeybinds.GetKeyLabel(MazeKeybinds.GetBinding(MazeAction.GodView)) + " 俯视   " +
            MazeKeybinds.GetKeyLabel(MazeKeybinds.GetBinding(MazeAction.CheatView)) + " 透视", _uiHudSmall);

        float buttonY = 238f;
        float buttonWidth = 108f;
        Rect newMaze = new Rect(28f, buttonY, buttonWidth, 28f);
        Rect restart = new Rect(150f, buttonY, buttonWidth, 28f);
        Rect menu = new Rect(272f, buttonY, buttonWidth, 28f);
        DrawSmallButton(newMaze);
        DrawSmallButton(restart);
        DrawSmallButton(menu);
        if (GUI.Button(newMaze, "新迷宫", _uiSmallButton)) GenerateNewLevel();
        if (GUI.Button(restart, "重新开始", _uiSmallButton)) RestartLevel();
        if (GUI.Button(menu, "菜单  Esc", _uiSmallButton)) OpenMainMenu();
    }

    static void DrawSmallButton(Rect rect)
    {
        bool hovered = rect.Contains(Event.current.mousePosition);
        DrawUIRect(rect, hovered ? new Color(0.13f, 0.29f, 0.38f, 0.98f) : new Color(0.07f, 0.12f, 0.17f, 0.98f));
        DrawUIRect(new Rect(rect.x, rect.y, rect.width, 1f), new Color(0.34f, 0.56f, 0.68f, 0.6f));
    }

    void DrawInfoCard(Rect rect, string title, string detail, Color accent)
    {
        DrawUIPanel(rect, new Color(0.055f, 0.085f, 0.125f, 0.96f), accent);
        GUI.Label(new Rect(rect.x + 12f, rect.y + 14f, rect.width - 24f, 22f), title, _uiCardTitle);
        GUI.Label(new Rect(rect.x + 12f, rect.y + 42f, rect.width - 24f, 20f), detail, _uiCardSub);
    }

    void DrawModeButton(Rect rect, string title, string detail, bool challenge)
    {
        Color accent = challenge ? new Color(1f, 0.57f, 0.27f) : new Color(0.24f, 0.82f, 1f);
        bool hovered = rect.Contains(Event.current.mousePosition);
        DrawUIPanel(rect, hovered ? new Color(0.09f, 0.15f, 0.21f, 1f) : new Color(0.06f, 0.105f, 0.15f, 1f), accent);
        GUI.Label(new Rect(rect.x + 18f, rect.y + 12f, rect.width - 36f, 28f), title, _uiCardTitle);
        GUI.Label(new Rect(rect.x + 18f, rect.y + 44f, rect.width - 36f, 22f), detail, _uiCardSub);
    }

    bool ConsumeGuiClick(Rect rect)
    {
        Event current = Event.current;
        if (current == null || current.type != EventType.MouseDown || current.button != 0 || !rect.Contains(current.mousePosition))
            return false;
        current.Use();
        return true;
    }

    static Color GetItemAccent(MazeItem item)
    {
        switch (item)
        {
            case MazeItem.Hammer: return new Color(1f, 0.55f, 0.18f);
            case MazeItem.Fly: return new Color(0.2f, 0.85f, 1f);
            case MazeItem.Shield: return new Color(0.68f, 0.45f, 1f);
            case MazeItem.Heal: return new Color(0.24f, 1f, 0.48f);
            default: return new Color(0.25f, 0.38f, 0.47f);
        }
    }

    static void EnsureUIStyles()
    {
        if (_uiPixel == null)
        {
            _uiPixel = new Texture2D(1, 1, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
            _uiPixel.SetPixel(0, 0, Color.white);
            _uiPixel.Apply();
        }
        if (_uiClearPixel == null)
        {
            _uiClearPixel = new Texture2D(1, 1, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
            _uiClearPixel.SetPixel(0, 0, new Color(1f, 1f, 1f, 0f));
            _uiClearPixel.Apply();
        }
        if (_uiTitle != null) return;
        _uiTitle = MakeLabelStyle(34, Color.white, FontStyle.Bold);
        _uiSubtitle = MakeLabelStyle(16, new Color(0.67f, 0.76f, 0.84f), FontStyle.Normal);
        _uiBody = MakeLabelStyle(16, Color.white, FontStyle.Bold);
        _uiSmall = MakeLabelStyle(13, new Color(0.59f, 0.72f, 0.82f), FontStyle.Normal);
        _uiCardTitle = MakeLabelStyle(17, Color.white, FontStyle.Bold);
        _uiCardSub = MakeLabelStyle(12, new Color(0.67f, 0.77f, 0.84f), FontStyle.Normal);
        _uiHudTitle = MakeLabelStyle(15, Color.white, FontStyle.Bold);
        _uiHudBody = MakeLabelStyle(14, new Color(0.93f, 0.97f, 1f), FontStyle.Bold);
        _uiHudSmall = MakeLabelStyle(11, new Color(0.66f, 0.77f, 0.85f), FontStyle.Normal);
        _uiButton = MakeButtonStyle(16);
        _uiSmallButton = MakeButtonStyle(11);
    }

    static GUIStyle MakeLabelStyle(int fontSize, Color color, FontStyle fontStyle)
    {
        GUIStyle style = new GUIStyle(GUI.skin.label);
        style.fontSize = fontSize;
        style.fontStyle = fontStyle;
        style.normal.textColor = color;
        style.wordWrap = true;
        return style;
    }

    static GUIStyle MakeButtonStyle(int fontSize)
    {
        GUIStyle style = new GUIStyle(GUI.skin.button);
        style.fontSize = fontSize;
        style.fontStyle = FontStyle.Bold;
        style.alignment = TextAnchor.MiddleCenter;
        style.normal.textColor = Color.white;
        style.hover.textColor = Color.white;
        style.active.textColor = Color.white;
        style.normal.background = _uiClearPixel;
        style.hover.background = _uiClearPixel;
        style.active.background = _uiClearPixel;
        style.border = new RectOffset(0, 0, 0, 0);
        style.padding = new RectOffset(6, 6, 4, 4);
        return style;
    }

    static void DrawUIPanel(Rect rect, Color fill, Color? accent = null)
    {
        DrawUIRect(rect, fill);
        DrawUIRect(new Rect(rect.x, rect.y, rect.width, 1f), new Color(0.35f, 0.55f, 0.68f, 0.45f));
        if (accent.HasValue) DrawUIRect(new Rect(rect.x, rect.y, 3f, rect.height), accent.Value);
    }

    static void DrawUIRect(Rect rect, Color color)
    {
        Color old = GUI.color;
        GUI.color = color;
        GUI.DrawTexture(rect, _uiPixel != null ? _uiPixel : Texture2D.whiteTexture);
        GUI.color = old;
    }

    static void DrawCrosshair()
    {
        float x = Screen.width * 0.5f;
        float y = Screen.height * 0.5f;
        Color previous = GUI.color;
        GUI.color = new Color(0f, 0f, 0f, 0.8f);
        GUI.DrawTexture(new Rect(x - 11f, y - 1f, 22f, 3f), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(x - 1f, y - 11f, 3f, 22f), Texture2D.whiteTexture);
        GUI.color = new Color(0.35f, 0.95f, 1f, 0.95f);
        GUI.DrawTexture(new Rect(x - 9f, y, 18f, 1f), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(x, y - 9f, 1f, 18f), Texture2D.whiteTexture);
        GUI.color = previous;
    }
}
