using System;
using System.Collections.Generic;
using TextureArrayPipeline.Runtime;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace TextureArrayPipeline
{
    [CustomEditor(typeof(TextureArrayDefinition))]
    internal class TextureArrayDefinitionEditor : Editor
    {
        private SerializedProperty _guidsProp;
        private SerializedProperty _buildModeProp;
        private SerializedProperty _settingsProp;
        private SerializedProperty _quickResProp;
        private SerializedProperty _advWidthProp;
        private SerializedProperty _advHeightProp;
        private SerializedProperty _advFormatProp;
        private SerializedProperty _generateMipsProp;
        private SerializedProperty _filterModeProp;
        private SerializedProperty _wrapModeProp;
        private SerializedProperty _isNormalMapProp;
        private SerializedProperty _autoFixNormalProp;

        private Vector2 _scrollPos;
        private readonly Dictionary<string, Texture2D> _thumbnailCache = new();

        private static readonly GUIContent[] ResolutionLabels =
        {
            new("256\nLow", "256×256 — small icons, distant detail"),
            new("512\nStandard", "512×512 — balanced quality, good default"),
            new("1024\nHigh", "1024×1024 — high quality close-up surfaces"),
            new("2048\nUltra", "2048×2048 — maximum detail, high VRAM cost"),
        };

        private static readonly int[] ResolutionValues = { 256, 512, 1024, 2048 };

        private GUIStyle _warningLabelStyle;
        private GUIStyle _dropZoneStyle;

        private static GUIContent[] _formatLabels;
        private static GraphicsFormat[] _supportedFormats;

        private static void EnsureFormatOptions()
        {
            if (_formatLabels != null) return;

            var formats = new List<GraphicsFormat> { GraphicsFormat.None };
            var labels = new List<GUIContent> { new("None  (auto-detect)") };

            foreach (GraphicsFormat fmt in Enum.GetValues(typeof(GraphicsFormat)))
            {
                if (fmt == GraphicsFormat.None || !SystemInfo.IsFormatSupported(fmt, FormatUsage.Sample))
                    continue;

                formats.Add(fmt);
                labels.Add(new GUIContent(fmt.ToString()));
            }

            _supportedFormats = formats.ToArray();
            _formatLabels = labels.ToArray();
        }

        private void OnEnable()
        {
            _guidsProp = serializedObject.FindProperty("_textureGuids");
            _buildModeProp = serializedObject.FindProperty("_buildMode");
            _settingsProp = serializedObject.FindProperty("_settings");
            _quickResProp = _settingsProp.FindPropertyRelative("QuickTargetResolution");
            _advWidthProp = _settingsProp.FindPropertyRelative("AdvancedWidth");
            _advHeightProp = _settingsProp.FindPropertyRelative("AdvancedHeight");
            _advFormatProp = _settingsProp.FindPropertyRelative("AdvancedFormat");
            _generateMipsProp = _settingsProp.FindPropertyRelative("GenerateMipmaps");
            _filterModeProp = _settingsProp.FindPropertyRelative("FilterMode");
            _wrapModeProp = _settingsProp.FindPropertyRelative("WrapMode");
            _isNormalMapProp = _settingsProp.FindPropertyRelative("IsNormalMap");
            _autoFixNormalProp = _settingsProp.FindPropertyRelative("AutoFixNormalMapImport");
        }

        public override void OnInspectorGUI()
        {
            EnsureStyles();
            serializedObject.Update();

            DrawModeToggle();
            EditorGUILayout.Space(4);
            DrawSettings();
            EditorGUILayout.Space(6);
            DrawTextureList();
            EditorGUILayout.Space(6);
            DrawStatusAndButton();

            serializedObject.ApplyModifiedProperties();

            var def = (TextureArrayDefinition)target;
            if ((def.hideFlags & HideFlags.DontSaveInBuild) != 0)
                return;

            def.hideFlags |= HideFlags.DontSaveInBuild;
            EditorUtility.SetDirty(def);
        }

        private void DrawModeToggle()
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("Mode", GUILayout.Width(40));

            int current = _buildModeProp.enumValueIndex;
            int next = GUILayout.Toolbar(current, new[] { "Quick", "Advanced" }, GUILayout.Height(22));

            if (next == current)
            {
                EditorGUILayout.EndHorizontal();
                return;
            }

            _buildModeProp.enumValueIndex = next;
            EditorGUILayout.EndHorizontal();
        }

        private void DrawSettings()
        {
            if (_buildModeProp.enumValueIndex == (int)TextureArrayBuildMode.Quick)
            {
                DrawQuickSettings();
                return;
            }

            DrawAdvancedSettings();
        }

        private void DrawQuickSettings()
        {
            EditorGUILayout.LabelField("Target Resolution", EditorStyles.boldLabel);

            int currentIdx = Array.IndexOf(ResolutionValues, _quickResProp.intValue);
            if (currentIdx < 0) currentIdx = 1;

            int newIdx = GUILayout.Toolbar(currentIdx, ResolutionLabels, GUILayout.Height(46));
            if (newIdx != currentIdx)
                _quickResProp.intValue = ResolutionValues[newIdx];

            EditorGUILayout.HelpBox(
                "Format, mipmaps, and filter settings are detected automatically from your source textures.",
                MessageType.None
            );
        }

        private void DrawAdvancedSettings()
        {
            EditorGUILayout.LabelField("Advanced Settings", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PrefixLabel("Size (W × H)");
            _advWidthProp.intValue = EditorGUILayout.IntField(_advWidthProp.intValue);
            GUILayout.Label("×", GUILayout.Width(14));
            _advHeightProp.intValue = EditorGUILayout.IntField(_advHeightProp.intValue);
            EditorGUILayout.EndHorizontal();

            if (!Mathf.IsPowerOfTwo(_advWidthProp.intValue) || !Mathf.IsPowerOfTwo(_advHeightProp.intValue))
                EditorGUILayout.HelpBox("Width and Height should be powers of two.", MessageType.Warning);

            DrawFormatPopup();
            EditorGUILayout.PropertyField(_generateMipsProp, new GUIContent("Generate Mipmaps"));
            EditorGUILayout.PropertyField(_filterModeProp, new GUIContent("Filter Mode"));
            EditorGUILayout.PropertyField(_wrapModeProp, new GUIContent("Wrap Mode"));
            EditorGUILayout.Space(2);
            EditorGUILayout.PropertyField(_isNormalMapProp, new GUIContent("Is Normal Map"));

            using (new EditorGUI.DisabledScope(!_isNormalMapProp.boolValue))
            {
                EditorGUILayout.PropertyField(_autoFixNormalProp,
                    new GUIContent("Auto Fix Import Settings",
                        "Automatically set TextureImporter type to NormalMap for all source textures.")
                );
            }
        }

        private void DrawFormatPopup()
        {
            EnsureFormatOptions();

            var current = (GraphicsFormat)_advFormatProp.intValue;
            int currentIdx = Array.IndexOf(_supportedFormats, current);

            if (currentIdx < 0)
            {
                currentIdx = 0;
                EditorGUILayout.HelpBox(
                    $"Format '{current}' is not supported on this platform — reset to auto-detect.",
                    MessageType.Warning
                );
            }

            int newIdx = EditorGUILayout.Popup(new GUIContent("Format",
                    "Output format for the Texture2DArray.\n\n" +
                    "When set, all source textures are reimported to this format before building.\n" +
                    "Only formats supported for sampling on the current platform are listed."),
                currentIdx, _formatLabels
            );

            if (newIdx == currentIdx)
                return;

            _advFormatProp.intValue = (int)_supportedFormats[newIdx];
            serializedObject.ApplyModifiedProperties();
        }

        private void DrawTextureList()
        {
            int count = _guidsProp.arraySize;
            EditorGUILayout.LabelField($"Source Textures ({count})", EditorStyles.boldLabel);

            int targetRes = GetTargetResForWarning();
            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos, GUILayout.MaxHeight(220));

            for (int i = 0; i < _guidsProp.arraySize; i++)
            {
                if (!DrawTextureRow(i, targetRes))
                    continue;

                i--;
            }

            EditorGUILayout.EndScrollView();

            DrawDropZone();
            HandleDragAndDrop();
        }

        private bool DrawTextureRow(int index, int targetRes)
        {
            SerializedProperty guidProp = _guidsProp.GetArrayElementAtIndex(index);
            string guid = guidProp.stringValue;

            string assetPath = AssetDatabase.GUIDToAssetPath(guid);
            Texture2D tex = string.IsNullOrEmpty(assetPath)
                ? null
                : AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);

            EditorGUILayout.BeginHorizontal(GUILayout.Height(36));
            GUILayout.Space(2);

            Texture2D thumb = GetThumbnail(guid, tex);
            Rect thumbRect = GUILayoutUtility.GetRect(32, 32, GUILayout.Width(32), GUILayout.Height(32));
            GUI.DrawTexture(thumbRect, thumb, ScaleMode.ScaleToFit);

            GUILayout.Space(4);

            if (tex == null)
            {
                EditorGUILayout.LabelField(new GUIContent($"Missing  ({guid})", guid), EditorStyles.miniLabel);
                if (!GUILayout.Button("✕", EditorStyles.miniButton, GUILayout.Width(22)))
                    return false;

                _guidsProp.DeleteArrayElementAtIndex(index);
                return true;
            }

            EditorGUILayout.LabelField(new GUIContent(tex.name, assetPath), GUILayout.ExpandWidth(true));

            int srcW = tex.width, srcH = tex.height;
            if (AssetImporter.GetAtPath(assetPath) is TextureImporter ti)
                ti.GetSourceTextureWidthAndHeight(out srcW, out srcH);

            string sizeStr = $"{srcW}×{srcH}";
            if (srcW < targetRes || srcH < targetRes)
            {
                EditorGUILayout.LabelField(
                    new GUIContent(
                        $"{sizeStr} ⚠",
                        $"Source is smaller than target ({targetRes}×{targetRes}). Upscaling will be used."
                    ),
                    _warningLabelStyle,
                    GUILayout.Width(110)
                );
            }
            else
                EditorGUILayout.LabelField(sizeStr, GUILayout.Width(80));

            bool removed = false;
            if (GUILayout.Button("✕", EditorStyles.miniButton, GUILayout.Width(22)))
            {
                _guidsProp.DeleteArrayElementAtIndex(index);
                removed = true;
            }

            EditorGUILayout.EndHorizontal();
            return removed;
        }

        private void DrawDropZone()
        {
            Rect dropRect = GUILayoutUtility.GetRect(0, 36, GUILayout.ExpandWidth(true));
            GUI.Box(dropRect, "Drop textures or a folder here", _dropZoneStyle);
        }

        private void HandleDragAndDrop()
        {
            Event evt = Event.current;
            if (evt.type != EventType.DragUpdated && evt.type != EventType.DragPerform) return;

            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
            if (evt.type != EventType.DragPerform) return;

            DragAndDrop.AcceptDrag();
            bool changed = false;

            foreach (UnityEngine.Object dragObj in DragAndDrop.objectReferences)
            {
                string path = AssetDatabase.GetAssetPath(dragObj);
                if (string.IsNullOrEmpty(path)) continue;

                if (AssetDatabase.IsValidFolder(path))
                {
                    foreach (string g in AssetDatabase.FindAssets("t:Texture2D", new[] { path }))
                        changed |= AddGuidIfNew(g);

                    continue;
                }

                if (dragObj is not Texture2D)
                    continue;

                changed |= AddGuidIfNew(AssetDatabase.AssetPathToGUID(path));
            }

            if (changed)
            {
                serializedObject.ApplyModifiedProperties();
                serializedObject.Update();
            }

            evt.Use();
        }

        private bool AddGuidIfNew(string guid)
        {
            if (string.IsNullOrEmpty(guid)) return false;

            for (int i = 0; i < _guidsProp.arraySize; i++)
            {
                if (_guidsProp.GetArrayElementAtIndex(i).stringValue != guid)
                    continue;

                return false;
            }

            int idx = _guidsProp.arraySize;
            _guidsProp.InsertArrayElementAtIndex(idx);
            _guidsProp.GetArrayElementAtIndex(idx).stringValue = guid;
            return true;
        }

        private void DrawStatusAndButton()
        {
            var definition = (TextureArrayDefinition)target;
            Texture2DArray sub = TextureArrayBuilder.GetSubAsset(definition);

            if (sub != null)
            {
                bool stale = TextureArrayBuilder.IsStale(definition);
                string info = $"Texture2DArray  {sub.width}×{sub.height}  ×{sub.depth}  {sub.graphicsFormat}";
                EditorGUILayout.HelpBox(
                    (stale ? "⚠ Outdated — " : "✓ ") + info,
                    stale ? MessageType.Warning : MessageType.Info
                );

                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Ping in Project", EditorStyles.miniButton, GUILayout.Width(110)))
                    EditorGUIUtility.PingObject(sub);

                EditorGUILayout.EndHorizontal();
            }
            else
            {
                EditorGUILayout.HelpBox(
                    "No Texture2DArray generated yet. Click 'Generate' to build it.",
                    MessageType.Warning);
            }

            EditorGUILayout.Space(4);

            bool hasTextures = _guidsProp.arraySize > 0;
            using (new EditorGUI.DisabledScope(!hasTextures))
            {
                GUI.backgroundColor = hasTextures ? new Color(0.4f, 0.8f, 0.4f) : Color.white;
                if (GUILayout.Button("Generate Texture Array", GUILayout.Height(30)))
                    TextureArrayBuilder.Build(definition);

                GUI.backgroundColor = Color.white;
            }

            if (!hasTextures)
                EditorGUILayout.HelpBox("Add at least one texture to generate.", MessageType.Info);
        }

        private Texture2D GetThumbnail(string guid, Texture2D tex)
        {
            if (tex == null) return null;
            if (_thumbnailCache.TryGetValue(guid, out Texture2D cached) && cached != null) return cached;

            Texture2D thumb = AssetPreview.GetAssetPreview(tex);
            if (thumb == null)
                return thumb;

            _thumbnailCache[guid] = thumb;
            return thumb;
        }

        private int GetTargetResForWarning()
        {
            return _buildModeProp.enumValueIndex == (int)TextureArrayBuildMode.Quick
                ? _quickResProp.intValue
                : Mathf.Max(_advWidthProp.intValue, _advHeightProp.intValue);
        }

        private void EnsureStyles()
        {
            if (_warningLabelStyle != null) return;

            _warningLabelStyle = new GUIStyle(EditorStyles.label)
            {
                normal = { textColor = new Color(0.9f, 0.7f, 0.1f) },
            };

            _dropZoneStyle = new GUIStyle(GUI.skin.box)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Italic,
                normal = { textColor = Color.gray },
            };
        }
    }
}