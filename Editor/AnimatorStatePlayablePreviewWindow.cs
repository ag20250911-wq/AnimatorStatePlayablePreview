using UnityEditor;
using UnityEngine;
using UnityEditor.Animations;
using UnityEngine.Animations;
using UnityEngine.Playables;

public class AnimatorStatePlayablePreviewWindow : EditorWindow
{
    // --- Fields ---
    private PreviewRenderUtility _previewRenderUtility;
    private PlayableGraph _graph;
    private AnimationPlayableOutput _output;
    private AnimationClipPlayable _clipPlayable;

    private AnimationClip _selectedClip;

    private static GameObject _previewTargetObject;
    private GameObject _previewInstance;
    private Animator _previewAnimator;

    private Vector2 _cameraRotation = new Vector2(120f, 20f);
    private float _cameraDistance = 5f;

    private double _lastUpdateTime = 0;
    private bool _isLooping = true;

    // --- Methods ---

    [MenuItem("Window/Animator State Playable Preview")]
    public static void OpenWindow()
    {
        var window = GetWindow<AnimatorStatePlayablePreviewWindow>("Playable Preview");
        window.minSize = new Vector2(400, 400);
        window.wantsMouseMove = true;
    }

    private void OnEnable()
    {
        _previewRenderUtility = new PreviewRenderUtility();
        _previewRenderUtility.camera.fieldOfView = 30.0f;
        _previewRenderUtility.lights[0].intensity = 1.2f;
        _previewRenderUtility.lights[0].transform.rotation = Quaternion.Euler(30f, 30f, 0);

        _lastUpdateTime = EditorApplication.timeSinceStartup;

        Selection.selectionChanged += OnSelectionChanged;
        EditorApplication.update += OnUpdate;
        OnSelectionChanged();
    }

    private void OnDisable()
    {
        Selection.selectionChanged -= OnSelectionChanged;
        EditorApplication.update -= OnUpdate;

        if (_graph.IsValid()) _graph.Destroy();
        if (_previewInstance != null) DestroyImmediate(_previewInstance);
        _previewRenderUtility?.Cleanup();
    }

    private void OnUpdate()
    {
        double currentTime = EditorApplication.timeSinceStartup;
        float deltaTime = (float)(currentTime - _lastUpdateTime);
        _lastUpdateTime = currentTime;

        if (_graph.IsValid() && _graph.IsPlaying())
        {
            _graph.Evaluate(deltaTime);

            if (_isLooping && _clipPlayable.IsValid() && _selectedClip != null)
            {
                if (_clipPlayable.GetTime() >= _selectedClip.length)
                {
                    _clipPlayable.SetTime(0);
                }
            }
        }

        Repaint();
    }

    private void OnSelectionChanged()
    {
        AnimationClip newClip = null;
        if (Selection.activeObject is AnimatorState state)
        {
            newClip = state.motion as AnimationClip;
        }

        if (newClip != _selectedClip)
        {
            _selectedClip = newClip;
            RebuildAnimationClipPlayable();
        }
    }

    private void OnGUI()
    {
        DrawToolbarGUI();

        UpdatePreviewInstance();
        UpdatePlayableGraph();

        if (_previewTargetObject != null && _previewAnimator == null)
        {
            EditorGUILayout.HelpBox("プレビュー対象のGameObject、またはその子オブジェクトにAnimatorコンポーネントが必要です。", MessageType.Warning);
        }

        DrawAnimationControlsGUI();

        var previewRect = GUILayoutUtility.GetRect(position.width, position.height - 65);

        HandleCameraEvents(previewRect);

        _previewRenderUtility.BeginPreview(previewRect, GUIStyle.none);

        if (_previewInstance != null && _previewAnimator != null)
        {
            UpdateCamera();
            _previewRenderUtility.Render();
        }

        _previewRenderUtility.EndAndDrawPreview(previewRect);
    }

    private void DrawToolbarGUI()
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

        EditorGUI.BeginChangeCheck();
        var newPreviewTarget = (GameObject)EditorGUILayout.ObjectField("プレビュー対象", _previewTargetObject, typeof(GameObject), true, GUILayout.Width(300));
        if (EditorGUI.EndChangeCheck())
        {
            SetPreviewTarget(newPreviewTarget);
        }

        if (GUILayout.Button("None", EditorStyles.toolbarButton, GUILayout.Width(40)))
        {
            SetPreviewTarget(null);
        }

        GUILayout.FlexibleSpace();

        EditorGUI.BeginDisabledGroup(true);
        EditorGUILayout.ObjectField(_selectedClip, typeof(AnimationClip), false, GUILayout.Width(200));
        EditorGUI.EndDisabledGroup();

        EditorGUILayout.EndHorizontal();
    }

    private void SetPreviewTarget(GameObject newTarget)
    {
        if (newTarget != null && newTarget.GetComponentInChildren<Animator>() == null)
        {
            EditorUtility.DisplayDialog("エラー", "選択されたGameObject、またはその子オブジェクトにAnimatorコンポーネントが見つかりません。", "OK");
            return;
        }

        _previewTargetObject = newTarget;
        if (_previewInstance != null) DestroyImmediate(_previewInstance);
        _previewInstance = null;
        _previewAnimator = null;
    }

    private void DrawAnimationControlsGUI()
    {
        if (!_clipPlayable.IsValid() || _selectedClip == null || !_graph.IsValid())
        {
            GUILayout.Space(21);
            return;
        }

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button(_clipPlayable.GetSpeed() == 0 ? "▶" : "❚❚", EditorStyles.toolbarButton, GUILayout.Width(28)))
        {
            _clipPlayable.SetSpeed(_clipPlayable.GetSpeed() == 0 ? 1 : 0);
        }


        EditorGUI.BeginChangeCheck();
        var timeControl = _clipPlayable.GetTime();
        var newTime = EditorGUILayout.Slider((float)timeControl, 0f, _selectedClip.length);
        if (EditorGUI.EndChangeCheck())
        {
            _clipPlayable.SetTime(newTime);
            _graph.Evaluate(0);
        }

        _isLooping = GUILayout.Toggle(_isLooping, "Loop", EditorStyles.toolbarButton, GUILayout.Width(50));

        EditorGUILayout.EndHorizontal();
    }

    private void UpdatePreviewInstance()
    {
        if (_previewInstance == null && _previewTargetObject != null)
        {
            _previewInstance = Instantiate(_previewTargetObject);
            _previewInstance.hideFlags = HideFlags.HideAndDontSave;
            _previewRenderUtility.AddSingleGO(_previewInstance);
            _previewInstance.transform.position = Vector3.zero;
        }
        if (_previewInstance != null && _previewTargetObject == null)
        {
            DestroyImmediate(_previewInstance);
            _previewInstance = null;
        }
    }

    private void UpdatePlayableGraph()
    {
        if (_previewInstance == null)
        {
            if (_graph.IsValid()) _graph.Destroy();
            _previewAnimator = null;
            return;
        }

        var currentAnimator = _previewInstance.GetComponentInChildren<Animator>();
        if (currentAnimator == null)
        {
            // GameObjectはあるが、Animatorがない場合
            if (_graph.IsValid()) _graph.Destroy();
            _previewAnimator = null;
            return;
        }

        if (currentAnimator == _previewAnimator && _graph.IsValid()) return;

        if (_graph.IsValid()) _graph.Destroy();
        _previewAnimator = currentAnimator;

        _graph = PlayableGraph.Create("PlayablePreviewGraph");
        _graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
        _output = AnimationPlayableOutput.Create(_graph, "Output", _previewAnimator);
        RebuildAnimationClipPlayable();
    }

    private void RebuildAnimationClipPlayable()
    {
        if (!_graph.IsValid()) return;

        if (_clipPlayable.IsValid()) _clipPlayable.Destroy();

        if (_selectedClip != null)
        {
            _clipPlayable = AnimationClipPlayable.Create(_graph, _selectedClip);
            _output.SetSourcePlayable(_clipPlayable);
            _clipPlayable.SetSpeed(1);
            _graph.Play();
        }
        else
        {
            _graph.Stop();
        }
    }

    private void UpdateCamera()
    {
        var cam = _previewRenderUtility.camera;
        cam.transform.rotation = Quaternion.Euler(_cameraRotation.y, _cameraRotation.x, 0);
        cam.transform.position = cam.transform.forward * -_cameraDistance;
        cam.nearClipPlane = 0.1f;
        cam.farClipPlane = 100f;
    }

    private void HandleCameraEvents(Rect previewRect)
    {
        Event e = Event.current;
        if (previewRect.Contains(e.mousePosition))
        {
            if (e.type == EventType.MouseDrag && e.button == 0)
            {
                _cameraRotation += e.delta * 2f;
                e.Use();
            }
            if (e.type == EventType.ScrollWheel)
            {
                _cameraDistance *= 1f + e.delta.y * 0.1f;
                _cameraDistance = Mathf.Max(0.1f, _cameraDistance);
                e.Use();
            }
        }
    }
}