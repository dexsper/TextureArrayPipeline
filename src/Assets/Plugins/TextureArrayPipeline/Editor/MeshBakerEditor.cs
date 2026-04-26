using System;
using System.Collections.Generic;
using System.Linq;
using TextureArrayPipeline.Runtime;
using UnityEditor;
using UnityEditor.Formats.Fbx.Exporter;
using UnityEngine;

namespace TextureArrayPipeline
{
    public class MeshBakerEditor : EditorWindow
    {
        private static readonly string[] SupportedShaderNames =
        {
            ShaderProps.LitArrayShaderName,
            ShaderProps.SimpleLitArrayShaderName
        };

        private Renderer _renderer;
        private GameObject _workingRoot;

        private string _modelAssetPath;
        private string _prefabAssetPath;

        private readonly List<WorkingEntry> _workingEntries = new();

        private Vector2 _scrollPos;
        private bool _previewApplied;

        [MenuItem("Tools/Texture Array/Mesh Baker")]
        public static void Open() => GetWindow<MeshBakerEditor>("Mesh Baker");

        private void OnGUI()
        {
            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);

            DrawRendererField();
            DrawSourceFbxField();
            DrawActionButtons();
            DrawSlots();

            EditorGUILayout.EndScrollView();
        }

        private void DrawRendererField()
        {
            EditorGUI.BeginChangeCheck();

            var newRenderer = (Renderer)EditorGUILayout.ObjectField("Renderer", _renderer, typeof(Renderer), true);
            if (!EditorGUI.EndChangeCheck())
                return;

            SetRenderer(newRenderer);
        }

        private void DrawSourceFbxField()
        {
            if (string.IsNullOrEmpty(_modelAssetPath)) return;
            using (new EditorGUI.DisabledScope(true))
                EditorGUILayout.TextField("Source FBX", _modelAssetPath);
        }

        private void DrawActionButtons()
        {
            if (_workingEntries.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "Assign a Renderer from a prefab asset or prefab instance to get started.",
                    MessageType.Info
                );
                return;
            }

            bool hasSlots = _workingEntries.Any(e => e.Slots.Count > 0);
            if (!hasSlots)
            {
                EditorGUILayout.HelpBox(
                    "No materials using TextureArray2D shader were found on this Renderer.",
                    MessageType.Warning
                );
            }

            EditorGUILayout.Space(4);
            EditorGUILayout.BeginHorizontal();

            using (new EditorGUI.DisabledScope(!hasSlots))
            {
                if (GUILayout.Button("Refresh Slots"))
                    RefreshSlots();
            }

            using (new EditorGUI.DisabledScope(!hasSlots || string.IsNullOrEmpty(_modelAssetPath)))
            {
                if (GUILayout.Button("Save Changes"))
                {
                    if (!_previewApplied) Apply();
                    ExportPatchedFbx();
                }
            }

            EditorGUILayout.EndHorizontal();
            using (new EditorGUI.DisabledScope(!hasSlots))
            {
                if (GUILayout.Button("Reset All Slots"))
                {
                    ResetAllSlots();
                    Apply();
                    SceneView.RepaintAll();
                }
            }

            if (!string.IsNullOrEmpty(_modelAssetPath) || _workingEntries.Count <= 0)
                return;

            EditorGUILayout.HelpBox(
                "Source FBX path could not be determined.",
                MessageType.Warning
            );
        }

        private void DrawSlots()
        {
            if (_workingEntries.Count == 0 || !_workingEntries.Any(e => e.Slots.Count > 0))
                return;

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Slots", EditorStyles.boldLabel);

            foreach (WorkingEntry entry in _workingEntries)
            {
                if (entry.Slots.Count == 0) continue;

                EditorGUILayout.LabelField(
                    string.IsNullOrEmpty(entry.Path) ? "(root)" : entry.Path,
                    EditorStyles.boldLabel
                );

                foreach (Slot slot in entry.Slots)
                {
                    int max = Mathf.Max(0, slot.ArrayDepth - 1);
                    using (new EditorGUI.DisabledScope(max <= 0))
                    {
                        EditorGUI.BeginChangeCheck();
                        int newIndex = EditorGUILayout.IntSlider(
                            $"[{slot.MaterialSlot}] {slot.MaterialName}",
                            slot.ArrayIndex,
                            0,
                            max
                        );

                        if (!EditorGUI.EndChangeCheck())
                            continue;

                        slot.ArrayIndex = newIndex;
                        _previewApplied = false;

                        Apply();

                        SceneView.RepaintAll();
                        _previewApplied = true;
                    }
                }
            }
        }

        private void OnDisable() => CleanupWorkingClone();

        private void SetRenderer(Renderer newRenderer)
        {
            if (_renderer == newRenderer) return;

            _renderer = newRenderer;
            _workingEntries.Clear();
            _modelAssetPath = null;
            _prefabAssetPath = null;
            _previewApplied = false;
            CleanupWorkingClone();

            if (_renderer == null) return;

            if (!IsPrefabRenderer(_renderer))
            {
                EditorUtility.DisplayDialog(
                    "Texture Array Patcher",
                    "The selected Renderer must come from a prefab asset or a prefab instance in the scene.",
                    "OK");
                _renderer = null;
                return;
            }

            GameObject sourceRootObject = PrefabUtility.GetCorrespondingObjectFromSource(
                _renderer.transform.root.gameObject
            );

            if (sourceRootObject != null)
                _modelAssetPath = AssetDatabase.GetAssetPath(sourceRootObject);

            _prefabAssetPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(_renderer.gameObject);

            if (string.IsNullOrEmpty(_prefabAssetPath))
                _prefabAssetPath = AssetDatabase.GetAssetPath(_renderer.transform.root.gameObject);

            CreateWorkingClone();
            RefreshSlots();
        }

        private void RefreshSlots()
        {
            foreach (WorkingEntry entry in _workingEntries)
            {
                entry.Slots.Clear();
                Material[] materials = entry.WorkingRenderer.sharedMaterials;

                for (int i = 0; i < materials.Length; i++)
                {
                    Material mat = materials[i];
                    if (!IsTextureArrayMaterial(mat)) continue;

                    entry.Slots.Add(new Slot
                    {
                        MaterialSlot = i,
                        MaterialName = mat != null ? mat.name : "<Missing>",
                        ArrayDepth = GetArrayDepth(mat),
                        ArrayIndex = 0,
                    });
                }
            }

            _previewApplied = false;
        }

        private void ResetAllSlots()
        {
            foreach (WorkingEntry entry in _workingEntries)
            foreach (Slot slot in entry.Slots)
                slot.ArrayIndex = 0;
        }

        private void Apply()
        {
            foreach (WorkingEntry entry in _workingEntries)
                ApplyEntry(entry);
        }

        private void ApplyEntry(WorkingEntry entry)
        {
            if (entry.WorkingMesh == null || entry.WorkingMesh.vertexCount == 0) return;

            Undo.RecordObject(entry.WorkingMesh, "Apply Texture Array Index");

            int vertexCount = entry.WorkingMesh.vertexCount;
            var colors = new Color[vertexCount];
            for (int i = 0; i < vertexCount; i++)
                colors[i] = new Color(0f, 0f, 0f, 1f);

            foreach (Slot slot in entry.Slots)
                ApplySlot(entry.WorkingMesh, colors, slot.MaterialSlot, slot.ArrayIndex);

            entry.WorkingMesh.colors = colors;
            EditorUtility.SetDirty(entry.WorkingMesh);
        }

        private void ExportPatchedFbx()
        {
            if (_workingEntries.Count == 0) return;

            if (string.IsNullOrEmpty(_modelAssetPath))
            {
                EditorUtility.DisplayDialog(
                    "Texture Array Patcher",
                    "Cannot determine the source FBX path. The Renderer must be linked to a model imported from an FBX file.",
                    "OK"
                );
                return;
            }

            string exportPath = _modelAssetPath.Replace("\\", "/");
            Dictionary<string, Material> materialsByName = CaptureUniqueMaterialsByName();
            List<MeshBindingSnapshot> meshBindings = CapturePrefabMeshBindings();

            EnsureInputPrefabIsNotDirectModelVariant();
            ModelExporter.ExportObject(exportPath, _workingRoot);
            AssetDatabase.Refresh();
            RemapImporterMaterialsByName(exportPath, materialsByName);
            RestorePrefabMeshBindings(meshBindings);

            Debug.Log($"[TextureArrayPipeline] Overwritten source FBX: {exportPath}");
        }

        private void CreateWorkingClone()
        {
            CleanupWorkingClone();
            _workingEntries.Clear();

            Transform selectedRoot = _renderer.transform.root;
            GameObject sourceRootObj = PrefabUtility.GetCorrespondingObjectFromSource(selectedRoot.gameObject);
            Transform sourceRoot = sourceRootObj != null ? sourceRootObj.transform : selectedRoot;

            var materialByPath = new Dictionary<string, Material[]>();
            foreach (MeshRenderer r in selectedRoot.GetComponentsInChildren<MeshRenderer>(true))
            {
                var transformPath = AnimationUtility.CalculateTransformPath(r.transform, selectedRoot);
                materialByPath[transformPath] = r.sharedMaterials;
            }

            _workingRoot = BuildTransformHierarchy(sourceRoot);

            foreach (var sourceRenderer in sourceRoot.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (!HasSourceMesh(sourceRenderer)) continue;

                string path = AnimationUtility.CalculateTransformPath(
                    sourceRenderer.transform, sourceRoot);

                Transform cloneTransform = FindByPath(_workingRoot.transform, path);
                if (cloneTransform == null) continue;

                MeshFilter sourceFilter = sourceRenderer.GetComponent<MeshFilter>();
                Mesh clonedMesh = Instantiate(sourceFilter.sharedMesh);
                clonedMesh.name = $"{sourceFilter.sharedMesh.name}_TextureArrayPatched";

                MeshFilter workingFilter = cloneTransform.gameObject.AddComponent<MeshFilter>();
                MeshRenderer workingRenderer = cloneTransform.gameObject.AddComponent<MeshRenderer>();
                workingFilter.sharedMesh = clonedMesh;
                workingRenderer.sharedMaterials = materialByPath.TryGetValue(path, out Material[] mats)
                    ? mats
                    : sourceRenderer.sharedMaterials;

                _workingEntries.Add(new WorkingEntry
                {
                    Path = path,
                    WorkingMesh = clonedMesh,
                    WorkingRenderer = workingRenderer,
                });
            }

            FocusSceneViewOnWorkingRenderer();
        }

        private void CleanupWorkingClone()
        {
            _workingEntries.Clear();
            if (_workingRoot == null) return;
            DestroyImmediate(_workingRoot);
            _workingRoot = null;
        }

        private void FocusSceneViewOnWorkingRenderer()
        {
            if (_workingEntries.Count == 0) return;
            SceneView sceneView = SceneView.lastActiveSceneView;
            if (sceneView == null) return;

            Bounds bounds = _workingEntries[0].WorkingRenderer.bounds;
            if (bounds.size.sqrMagnitude <= 0f) return;

            sceneView.Frame(bounds, false);
            sceneView.Repaint();
        }

        private static GameObject BuildTransformHierarchy(Transform sourceRoot)
        {
            var cloneFlags = HideFlags.HideInHierarchy | HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild;

            var root = new GameObject($"{sourceRoot.name}_TextureArrayPreview") { hideFlags = cloneFlags };
            root.SetActive(true);

            var cloneByPath = new Dictionary<string, Transform> { [string.Empty] = root.transform };

            foreach (Transform t in sourceRoot.GetComponentsInChildren<Transform>(true))
            {
                string path = AnimationUtility.CalculateTransformPath(t, sourceRoot);

                if (string.IsNullOrEmpty(path))
                {
                    root.transform.localPosition = t.localPosition;
                    root.transform.localRotation = t.localRotation;
                    root.transform.localScale = t.localScale;
                    continue;
                }

                string parentPath = path.Contains("/") ? path[..path.LastIndexOf('/')] : string.Empty;
                if (!cloneByPath.TryGetValue(parentPath, out Transform cloneParent)) continue;

                var child = new GameObject(t.name) { hideFlags = cloneFlags };
                child.transform.SetParent(cloneParent, false);
                child.transform.localPosition = t.localPosition;
                child.transform.localRotation = t.localRotation;
                child.transform.localScale = t.localScale;
                cloneByPath[path] = child.transform;
            }

            return root;
        }

        private static void ApplySlot(Mesh mesh, Color[] colors, int materialSlot, int arrayIndex)
        {
            if (materialSlot < 0 || materialSlot >= mesh.subMeshCount) return;

            float encoded = Mathf.Clamp(arrayIndex, 0, 255) / 255f;
            foreach (int t in mesh.GetTriangles(materialSlot))
            {
                if (t >= 0 && t < colors.Length)
                    colors[t] = new Color(encoded, 0, 0, 1);
            }
        }

        private static bool IsTextureArrayMaterial(Material mat)
        {
            if (mat == null || mat.shader == null) return false;
            foreach (string name in SupportedShaderNames)
            {
                Shader shader = Shader.Find(name);
                if (shader != null && mat.shader == shader) return true;
            }

            return false;
        }

        private static int GetArrayDepth(Material mat)
        {
            if (mat == null)
                return 0;

            Texture texture = mat.GetTexture(ShaderProps.MapArray);
            if (texture == null)
                return 0;

            Texture2DArray texArray = texture as Texture2DArray;
            return texArray == null ? 0 : texArray.depth;
        }

        private static bool IsPrefabRenderer(Renderer renderer)
        {
            if (renderer == null)
                return false;

            PrefabAssetType prefabType = PrefabUtility.GetPrefabAssetType(renderer.gameObject);
            return prefabType != PrefabAssetType.NotAPrefab;
        }

        private static bool HasSourceMesh(MeshRenderer renderer)
        {
            if (renderer == null || !renderer.TryGetComponent(out MeshFilter meshFilter))
                return false;

            Mesh mesh = meshFilter.sharedMesh;
            if (mesh == null)
                return false;

            string assetPath = AssetDatabase.GetAssetPath(mesh);
            if (string.IsNullOrEmpty(assetPath))
                return false;

            return true;
        }

        private static Transform FindByPath(Transform root, string path)
            => string.IsNullOrEmpty(path) ? root : root.Find(path);

        private Dictionary<string, Material> CaptureUniqueMaterialsByName()
        {
            var result = new Dictionary<string, Material>();
            if (_renderer == null) return result;

            foreach (MeshRenderer mr in _renderer.transform.root.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (mr.sharedMaterials == null) continue;
                foreach (Material mat in mr.sharedMaterials)
                {
                    if (mat != null && !string.IsNullOrEmpty(mat.name))
                        result[mat.name] = mat;
                }
            }

            return result;
        }

        private static void RemapImporterMaterialsByName(
            string assetPath, Dictionary<string, Material> materialsByName)
        {
            if (string.IsNullOrEmpty(assetPath) || materialsByName == null || materialsByName.Count == 0)
                return;
            if (AssetImporter.GetAtPath(assetPath) is not ModelImporter importer) return;

            foreach (var remap in importer.GetExternalObjectMap())
                if (remap.Key.type == typeof(Material))
                    importer.RemoveRemap(remap.Key);

            foreach (var pair in materialsByName)
                importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), pair.Key), pair.Value);

            importer.SaveAndReimport();
        }

        private void EnsureInputPrefabIsNotDirectModelVariant()
        {
            if (string.IsNullOrEmpty(_prefabAssetPath)) return;

            GameObject prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(_prefabAssetPath);
            if (prefabAsset == null || PrefabUtility.GetPrefabAssetType(prefabAsset) != PrefabAssetType.Variant)
                return;

            GameObject source = PrefabUtility.GetCorrespondingObjectFromSource(prefabAsset);
            if (source == null || PrefabUtility.GetPrefabAssetType(source) != PrefabAssetType.Model)
                return;

            GameObject prefabRoot = PrefabUtility.LoadPrefabContents(_prefabAssetPath);
            try
            {
                PrefabUtility.UnpackPrefabInstance(
                    prefabRoot,
                    PrefabUnpackMode.Completely,
                    InteractionMode.AutomatedAction
                );

                PrefabUtility.SaveAsPrefabAsset(prefabRoot, _prefabAssetPath);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                Debug.Log($"[TextureArrayPipeline] Converted model variant to regular prefab: {_prefabAssetPath}");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(prefabRoot);
            }
        }

        private List<MeshBindingSnapshot> CapturePrefabMeshBindings()
        {
            var bindings = new List<MeshBindingSnapshot>();
            if (string.IsNullOrEmpty(_prefabAssetPath)) return bindings;

            Dictionary<Mesh, int> sourceIndexByMesh = BuildSourceMeshIndexMap();
            GameObject prefabRoot = PrefabUtility.LoadPrefabContents(_prefabAssetPath);
            try
            {
                foreach (MeshFilter filter in prefabRoot.GetComponentsInChildren<MeshFilter>(true))
                {
                    Mesh mesh = filter?.sharedMesh;
                    int sourceIndex = mesh != null && sourceIndexByMesh.TryGetValue(mesh, out int idx)
                        ? idx
                        : -1;
                    bindings.Add(new MeshBindingSnapshot
                    {
                        SourceIndex = sourceIndex,
                        DirectMesh = sourceIndex >= 0 ? null : mesh,
                    });
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(prefabRoot);
            }

            return bindings;
        }

        private Dictionary<Mesh, int> BuildSourceMeshIndexMap()
        {
            var map = new Dictionary<Mesh, int>();
            if (string.IsNullOrEmpty(_modelAssetPath)) return map;

            GameObject sourceRoot = AssetDatabase.LoadAssetAtPath<GameObject>(_modelAssetPath);
            if (sourceRoot == null) return map;

            MeshFilter[] filters = sourceRoot.GetComponentsInChildren<MeshFilter>(true);
            for (int i = 0; i < filters.Length; i++)
            {
                Mesh mesh = filters[i]?.sharedMesh;
                if (mesh == null)
                    continue;

                map.TryAdd(mesh, i);
            }

            return map;
        }

        private void RestorePrefabMeshBindings(List<MeshBindingSnapshot> bindings)
        {
            if (string.IsNullOrEmpty(_prefabAssetPath) || bindings == null || bindings.Count == 0)
                return;

            MeshFilter[] sourceFilters = GetSourceMeshFilters();
            MeshRenderer[] sourceRenderers = GetSourceMeshRenderers(sourceFilters);

            GameObject prefabRoot = PrefabUtility.LoadPrefabContents(_prefabAssetPath);
            try
            {
                MeshFilter[] targetFilters = prefabRoot.GetComponentsInChildren<MeshFilter>(true);
                int count = Mathf.Min(targetFilters.Length, bindings.Count);
                bool changed = false;

                for (int i = 0; i < count; i++)
                {
                    MeshFilter target = targetFilters[i];
                    if (target == null) continue;

                    Mesh desiredMesh = ResolveMeshFromBinding(bindings[i], sourceFilters);
                    if (target.sharedMesh != desiredMesh)
                    {
                        target.sharedMesh = desiredMesh;
                        changed = true;
                    }

                    changed |= SyncMaterials(target, bindings[i], sourceRenderers);
                }

                if (!changed)
                    return;

                PrefabUtility.SaveAsPrefabAsset(prefabRoot, _prefabAssetPath);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(prefabRoot);
            }
        }

        private MeshFilter[] GetSourceMeshFilters()
        {
            if (string.IsNullOrEmpty(_modelAssetPath)) return Array.Empty<MeshFilter>();
            GameObject root = AssetDatabase.LoadAssetAtPath<GameObject>(_modelAssetPath);
            return root != null ? root.GetComponentsInChildren<MeshFilter>(true) : Array.Empty<MeshFilter>();
        }

        private static MeshRenderer[] GetSourceMeshRenderers(MeshFilter[] sourceFilters)
        {
            if (sourceFilters.Length == 0) return Array.Empty<MeshRenderer>();
            var renderers = new MeshRenderer[sourceFilters.Length];
            for (int i = 0; i < sourceFilters.Length; i++)
                renderers[i] = sourceFilters[i] != null ? sourceFilters[i].GetComponent<MeshRenderer>() : null;
            return renderers;
        }

        private static Mesh ResolveMeshFromBinding(MeshBindingSnapshot binding, MeshFilter[] sourceFilters)
        {
            var index = binding.SourceIndex;
            var isIndexValid = index >= 0 && index < sourceFilters.Length;
            var hasFilter = isIndexValid && sourceFilters[index] != null;

            return hasFilter ? sourceFilters[index].sharedMesh : binding.DirectMesh;
        }

        private static bool SyncMaterials(MeshFilter target, MeshBindingSnapshot binding,
            MeshRenderer[] sourceRenderers)
        {
            if (binding.SourceIndex < 0 || binding.SourceIndex >= sourceRenderers.Length) return false;

            MeshRenderer src = sourceRenderers[binding.SourceIndex];
            if (src == null) return false;

            MeshRenderer dst = target.GetComponent<MeshRenderer>();
            if (dst == null) return false;

            Material[] srcMats = src.sharedMaterials;
            if (dst.sharedMaterials.Length == srcMats.Length && dst.sharedMaterials.SequenceEqual(srcMats))
                return false;

            dst.sharedMaterials = srcMats;
            return true;
        }

        private class Slot
        {
            public int MaterialSlot;
            public string MaterialName;
            public int ArrayDepth;
            public int ArrayIndex;
        }

        private class WorkingEntry
        {
            public string Path;
            public Mesh WorkingMesh;
            public MeshRenderer WorkingRenderer;
            public readonly List<Slot> Slots = new();
        }

        private class MeshBindingSnapshot
        {
            public int SourceIndex;
            public Mesh DirectMesh;
        }
    }
}