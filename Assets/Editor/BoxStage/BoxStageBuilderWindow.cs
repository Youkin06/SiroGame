using SiroGame.StageBuilder;
using UnityEditor;
using UnityEngine;

namespace SiroGame.Editor.StageBuilder
{
    public sealed class BoxStageBuilderWindow : EditorWindow
    {
        private const float MinimumSize = 0.01f;

        [SerializeField] private BoxStageData _stageData;
        [SerializeField] private BoxStageRoot _stageRoot;
        [SerializeField] private Vector3 _boxSize = Vector3.one;
        [SerializeField] private int _currentLayer;
        [SerializeField] private int _selectedTileIndex;
        [SerializeField] private int _gridExtent = 10;
        [SerializeField] private bool _showGrid = true;
        [SerializeField] private bool _paletteSettingsFoldout = true;
        private Vector2 _scrollPosition;

        private bool _isPainting;
        private bool _isErasing;
        private Vector3Int _lastEditedCell;
        private int _activeUndoGroup = -1;

        [MenuItem("Tools/SiroGame/Box Stage Builder")]
        public static void Open()
        {
            GetWindow<BoxStageBuilderWindow>("Box Stage Builder");
        }

        private void OnEnable()
        {
            SceneView.duringSceneGui += OnSceneGUI;
            Undo.undoRedoPerformed += OnUndoRedo;
        }

        private void OnDisable()
        {
            SceneView.duringSceneGui -= OnSceneGUI;
            Undo.undoRedoPerformed -= OnUndoRedo;
            FinishPaintStroke();
        }

        private void OnGUI()
        {
            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);

            DrawDataSection();

            if (_stageData != null)
            {
                EditorGUILayout.Space(8f);
                DrawGridSection();
                EditorGUILayout.Space(8f);
                DrawPaletteSection();
                EditorGUILayout.Space(8f);
                DrawGenerationSection();
                EditorGUILayout.Space(8f);
                DrawUsageHelp();
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawDataSection()
        {
            EditorGUILayout.LabelField("ステージ", EditorStyles.boldLabel);

            EditorGUI.BeginChangeCheck();
            _stageData = (BoxStageData)EditorGUILayout.ObjectField(
                "Stage Data",
                _stageData,
                typeof(BoxStageData),
                false
            );
            if (EditorGUI.EndChangeCheck())
            {
                _selectedTileIndex = 0;
                Repaint();
                SceneView.RepaintAll();
            }

            EditorGUI.BeginChangeCheck();
            _stageRoot = (BoxStageRoot)EditorGUILayout.ObjectField(
                "Stage Root",
                _stageRoot,
                typeof(BoxStageRoot),
                true
            );
            if (EditorGUI.EndChangeCheck())
            {
                SceneView.RepaintAll();
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Stage Dataを作成"))
                {
                    CreateStageData();
                }

                using (new EditorGUI.DisabledScope(_stageData == null))
                {
                    if (GUILayout.Button("Stage Rootを作成"))
                    {
                        CreateStageRoot();
                    }
                }
            }

            if (_stageRoot != null && _stageData != null &&
                _stageRoot.StageData != _stageData)
            {
                EditorGUILayout.HelpBox(
                    "Stage RootとStage Dataが関連付いていません。「関連付け」を押してください。",
                    MessageType.Warning
                );

                if (GUILayout.Button("Stage RootにStage Dataを関連付け"))
                {
                    AssignStageDataToRoot();
                }
            }
        }

        private void DrawGridSection()
        {
            EditorGUILayout.LabelField("グリッドと配置", EditorStyles.boldLabel);

            EditorGUI.BeginChangeCheck();
            Vector3 newCellSize = EditorGUILayout.Vector3Field(
                "Cell Size",
                _stageData.CellSize
            );
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(_stageData, "Change Box Stage Cell Size");
                _stageData.SetCellSize(newCellSize);
                EditorUtility.SetDirty(_stageData);
                RebuildStage();
            }

            _boxSize = ClampSize(EditorGUILayout.Vector3Field("Box Size", _boxSize));
            _currentLayer = EditorGUILayout.IntField("Y Layer", _currentLayer);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Layer -"))
                {
                    _currentLayer--;
                    SceneView.RepaintAll();
                }

                if (GUILayout.Button("Layer +"))
                {
                    _currentLayer++;
                    SceneView.RepaintAll();
                }
            }

            _showGrid = EditorGUILayout.Toggle("Show Grid", _showGrid);
            _gridExtent = Mathf.Max(1, EditorGUILayout.IntField("Grid Extent", _gridExtent));
        }

        private void DrawPaletteSection()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("ボックスパレット", EditorStyles.boldLabel);
                if (GUILayout.Button("Rule Tileを作成", GUILayout.Width(130f)))
                {
                    CreateRuleTile();
                }
            }

            SerializedObject serializedData = new SerializedObject(_stageData);
            serializedData.Update();
            SerializedProperty paletteProperty = serializedData.FindProperty("_tilePalette");

            _paletteSettingsFoldout = EditorGUILayout.Foldout(
                _paletteSettingsFoldout,
                "パレットにRule Tileを登録",
                true
            );

            if (_paletteSettingsFoldout)
            {
                EditorGUILayout.PropertyField(paletteProperty, true);
            }

            if (serializedData.ApplyModifiedProperties())
            {
                _selectedTileIndex = Mathf.Clamp(
                    _selectedTileIndex,
                    0,
                    Mathf.Max(0, _stageData.TilePalette.Count - 1)
                );
            }

            EditorGUILayout.Space(4f);

            if (_stageData.TilePalette.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "Rule Tileをパレットに追加してください。",
                    MessageType.Info
                );
                return;
            }

            const int columns = 3;
            for (int i = 0; i < _stageData.TilePalette.Count; i += columns)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    for (int column = 0; column < columns; column++)
                    {
                        int index = i + column;
                        if (index >= _stageData.TilePalette.Count)
                        {
                            GUILayout.FlexibleSpace();
                            continue;
                        }

                        DrawPaletteButton(index);
                    }
                }
            }
        }

        private void DrawPaletteButton(int index)
        {
            BoxRuleTile tile = _stageData.TilePalette[index];
            string label = tile != null ? tile.name : "Missing";
            Texture thumbnail = tile != null && tile.DefaultPrefab != null
                ? AssetPreview.GetMiniThumbnail(tile.DefaultPrefab)
                : null;

            GUIContent content = new GUIContent(label, thumbnail);
            bool selected = _selectedTileIndex == index;
            bool nextSelected = GUILayout.Toggle(
                selected,
                content,
                GUI.skin.button,
                GUILayout.MinWidth(80f),
                GUILayout.Height(52f)
            );

            if (nextSelected && !selected)
            {
                _selectedTileIndex = index;
                SceneView.RepaintAll();
            }
        }

        private void DrawGenerationSection()
        {
            EditorGUILayout.LabelField("生成", EditorStyles.boldLabel);

            bool canGenerate = HasValidStageBinding();
            using (new EditorGUI.DisabledScope(!canGenerate))
            {
                if (GUILayout.Button("Stage Dataから再生成"))
                {
                    RebuildStage();
                }

                if (GUILayout.Button("配置データを全削除"))
                {
                    ClearStage();
                }
            }
        }

        private void DrawUsageHelp()
        {
            EditorGUILayout.HelpBox(
                "Sceneビュー操作\n" +
                "・左クリック／ドラッグ: 配置または置換\n" +
                "・右クリック／ドラッグ: 削除\n" +
                "・Altを押している間: Sceneビューのカメラ操作\n" +
                "・同じマスを再配置すると、TileとBox Sizeを更新",
                MessageType.Info
            );
        }

        private void OnSceneGUI(SceneView sceneView)
        {
            if (_stageData == null || _stageRoot == null)
            {
                return;
            }

            if (!TryGetHoveredCell(out Vector3Int hoveredCell, out Vector3 worldCenter))
            {
                return;
            }

            if (_showGrid)
            {
                DrawGrid(hoveredCell);
            }

            DrawPlacementPreview(worldCenter);

            if (!HasValidStageBinding())
            {
                sceneView.Repaint();
                return;
            }

            Event currentEvent = Event.current;
            if (currentEvent.alt)
            {
                return;
            }

            int controlId = GUIUtility.GetControlID(FocusType.Passive);
            if (currentEvent.type == EventType.Layout)
            {
                HandleUtility.AddDefaultControl(controlId);
            }

            if (currentEvent.type == EventType.MouseDown &&
                (currentEvent.button == 0 || currentEvent.button == 1))
            {
                BeginPaintStroke(currentEvent.button == 1, hoveredCell);
                currentEvent.Use();
            }
            else if (currentEvent.type == EventType.MouseDrag && _isPainting)
            {
                EditCell(hoveredCell, _isErasing);
                currentEvent.Use();
            }
            else if (currentEvent.type == EventType.MouseUp && _isPainting)
            {
                FinishPaintStroke();
                currentEvent.Use();
            }
        }

        private bool TryGetHoveredCell(out Vector3Int cell, out Vector3 worldCenter)
        {
            Vector3 localPlanePoint = new Vector3(
                0f,
                _currentLayer * _stageData.CellSize.y,
                0f
            );
            Vector3 worldPlanePoint = _stageRoot.transform.TransformPoint(localPlanePoint);
            Plane plane = new Plane(_stageRoot.transform.up, worldPlanePoint);
            Ray ray = HandleUtility.GUIPointToWorldRay(Event.current.mousePosition);

            if (!plane.Raycast(ray, out float distance))
            {
                cell = default;
                worldCenter = default;
                return false;
            }

            Vector3 localHit = _stageRoot.transform.InverseTransformPoint(ray.GetPoint(distance));
            cell = new Vector3Int(
                Mathf.RoundToInt(localHit.x / _stageData.CellSize.x),
                _currentLayer,
                Mathf.RoundToInt(localHit.z / _stageData.CellSize.z)
            );
            worldCenter = _stageRoot.transform.TransformPoint(
                Vector3.Scale((Vector3)cell, _stageData.CellSize)
            );
            return true;
        }

        private void DrawGrid(Vector3Int hoveredCell)
        {
            Color previousColor = Handles.color;
            Matrix4x4 previousMatrix = Handles.matrix;
            Handles.color = new Color(0.35f, 0.7f, 1f, 0.45f);
            Handles.matrix = _stageRoot.transform.localToWorldMatrix;

            Vector3 cellSize = _stageData.CellSize;
            float layerY = _currentLayer * cellSize.y;
            int minX = hoveredCell.x - _gridExtent;
            int maxX = hoveredCell.x + _gridExtent;
            int minZ = hoveredCell.z - _gridExtent;
            int maxZ = hoveredCell.z + _gridExtent;

            for (int x = minX; x <= maxX + 1; x++)
            {
                float worldX = (x - 0.5f) * cellSize.x;
                Handles.DrawLine(
                    new Vector3(worldX, layerY, (minZ - 0.5f) * cellSize.z),
                    new Vector3(worldX, layerY, (maxZ + 0.5f) * cellSize.z)
                );
            }

            for (int z = minZ; z <= maxZ + 1; z++)
            {
                float worldZ = (z - 0.5f) * cellSize.z;
                Handles.DrawLine(
                    new Vector3((minX - 0.5f) * cellSize.x, layerY, worldZ),
                    new Vector3((maxX + 0.5f) * cellSize.x, layerY, worldZ)
                );
            }

            Handles.matrix = previousMatrix;
            Handles.color = previousColor;
        }

        private void DrawPlacementPreview(Vector3 worldCenter)
        {
            Color previousColor = Handles.color;
            Matrix4x4 previousMatrix = Handles.matrix;
            Handles.color = GetSelectedTile() != null
                ? new Color(0.2f, 1f, 0.35f, 0.9f)
                : new Color(1f, 0.3f, 0.2f, 0.9f);
            Handles.matrix = _stageRoot.transform.localToWorldMatrix;

            Vector3 localCenter = _stageRoot.transform.InverseTransformPoint(worldCenter);
            Handles.DrawWireCube(localCenter, _boxSize);

            Handles.matrix = previousMatrix;
            Handles.color = previousColor;
        }

        private void BeginPaintStroke(bool erase, Vector3Int cell)
        {
            _isPainting = true;
            _isErasing = erase;
            _lastEditedCell = new Vector3Int(int.MinValue, int.MinValue, int.MinValue);

            Undo.IncrementCurrentGroup();
            _activeUndoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(erase ? "Erase Box Stage Cells" : "Paint Box Stage Cells");

            EditCell(cell, erase);
        }

        private void EditCell(Vector3Int cell, bool erase)
        {
            if (cell == _lastEditedCell)
            {
                return;
            }

            _lastEditedCell = cell;
            Undo.RecordObject(_stageData, erase ? "Erase Box Stage Cell" : "Paint Box Stage Cell");

            bool changed = erase
                ? _stageData.RemoveCell(cell)
                : _stageData.SetCell(cell, GetSelectedTile(), _boxSize);

            if (!changed)
            {
                return;
            }

            EditorUtility.SetDirty(_stageData);
            RebuildStage();
            Repaint();
            SceneView.RepaintAll();
        }

        private void FinishPaintStroke()
        {
            if (_activeUndoGroup >= 0)
            {
                Undo.CollapseUndoOperations(_activeUndoGroup);
            }

            _activeUndoGroup = -1;
            _isPainting = false;
            _isErasing = false;
        }

        private void CreateStageData()
        {
            string path = EditorUtility.SaveFilePanelInProject(
                "Create Box Stage Data",
                "NewBoxStage",
                "asset",
                "ステージデータの保存先を選択してください。"
            );

            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            BoxStageData data = CreateInstance<BoxStageData>();
            AssetDatabase.CreateAsset(data, path);
            AssetDatabase.SaveAssets();
            _stageData = data;
            Selection.activeObject = data;
        }

        private void CreateRuleTile()
        {
            string path = EditorUtility.SaveFilePanelInProject(
                "Create Box Rule Tile",
                "NewBoxRuleTile",
                "asset",
                "Rule Tileの保存先を選択してください。"
            );

            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            BoxRuleTile tile = CreateInstance<BoxRuleTile>();
            AssetDatabase.CreateAsset(tile, path);
            AssetDatabase.SaveAssets();

            Undo.RecordObject(_stageData, "Add Rule Tile To Palette");
            SerializedObject serializedData = new SerializedObject(_stageData);
            SerializedProperty palette = serializedData.FindProperty("_tilePalette");
            int newIndex = palette.arraySize;
            palette.InsertArrayElementAtIndex(newIndex);
            palette.GetArrayElementAtIndex(newIndex).objectReferenceValue = tile;
            serializedData.ApplyModifiedProperties();
            EditorUtility.SetDirty(_stageData);

            _selectedTileIndex = newIndex;
            Selection.activeObject = tile;
        }

        private void CreateStageRoot()
        {
            GameObject rootObject = new GameObject("BoxStageRoot");
            Undo.RegisterCreatedObjectUndo(rootObject, "Create Box Stage Root");
            BoxStageRoot root = Undo.AddComponent<BoxStageRoot>(rootObject);
            _stageRoot = root;
            AssignStageDataToRoot();
            Selection.activeGameObject = rootObject;
        }

        private void AssignStageDataToRoot()
        {
            if (_stageRoot == null || _stageData == null)
            {
                return;
            }

            Undo.RecordObject(_stageRoot, "Assign Box Stage Data");
            _stageRoot.SetStageData(_stageData);
            EditorUtility.SetDirty(_stageRoot);
            RebuildStage();
        }

        private void ClearStage()
        {
            if (!EditorUtility.DisplayDialog(
                    "配置データを全削除",
                    "このStage Dataに保存されている全ボックスを削除します。",
                    "削除",
                    "キャンセル"
                ))
            {
                return;
            }

            Undo.RecordObject(_stageData, "Clear Box Stage");
            _stageData.ClearCells();
            EditorUtility.SetDirty(_stageData);
            RebuildStage();
        }

        private void RebuildStage()
        {
            if (HasValidStageBinding())
            {
                BoxStageGenerator.Rebuild(_stageRoot);
            }
        }

        private void OnUndoRedo()
        {
            RebuildStage();
            Repaint();
            SceneView.RepaintAll();
        }

        private bool HasValidStageBinding()
        {
            return _stageData != null &&
                   _stageRoot != null &&
                   _stageRoot.StageData == _stageData;
        }

        private BoxRuleTile GetSelectedTile()
        {
            if (_stageData == null || _stageData.TilePalette.Count == 0)
            {
                return null;
            }

            _selectedTileIndex = Mathf.Clamp(
                _selectedTileIndex,
                0,
                _stageData.TilePalette.Count - 1
            );
            return _stageData.TilePalette[_selectedTileIndex];
        }

        private static Vector3 ClampSize(Vector3 value)
        {
            return new Vector3(
                Mathf.Max(MinimumSize, value.x),
                Mathf.Max(MinimumSize, value.y),
                Mathf.Max(MinimumSize, value.z)
            );
        }
    }
}
