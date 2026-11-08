using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

#if IL2CPP
using Il2CppTMPro;
#elif MONO
using TMPro;
#endif

namespace Backpack.UI;

public sealed class BackpackSearchBar
{
    private static readonly List<BackpackSearchBar> Instances = new();

    private readonly List<int> _projectedIndices = new();
    private readonly List<int> _scratchIndices = new();
    private readonly Action _onSearchChanged;
    private readonly TMP_InputField _input;
    private bool _suppressInputCallback;
    private string _query = string.Empty;

    private BackpackSearchBar(RectTransform rect, TMP_InputField input, Action onSearchChanged)
    {
        Rect = rect;
        _input = input;
        _onSearchChanged = onSearchChanged;
        Instances.Add(this);
    }

    public RectTransform Rect { get; }
    public bool IsFiltering => !string.IsNullOrWhiteSpace(_query);
    public bool IsFocused => _input != null && _input.isFocused;
    public int ProjectedCount => _projectedIndices.Count;

    public static bool AnyInputFocused
    {
        get
        {
            for (var i = Instances.Count - 1; i >= 0; i--)
            {
                var instance = Instances[i];
                if (instance == null || instance._input == null)
                {
                    Instances.RemoveAt(i);
                    continue;
                }

                if (instance.IsFocused)
                    return true;
            }

            return false;
        }
    }

    public static void ResetSession()
    {
        Instances.Clear();
    }

    public static BackpackSearchBar Create(
        string name,
        RectTransform parent,
        TextMeshProUGUI textTemplate,
        Action onSearchChanged,
        bool showClearButton = true)
    {
        var root = new GameObject(name);
        root.AddComponent<RectTransform>();
        var rootRect = root.GetComponent<RectTransform>();
        rootRect.SetParent(parent, false);
        rootRect.anchorMin = new Vector2(0.5f, 0.5f);
        rootRect.anchorMax = new Vector2(0.5f, 0.5f);
        rootRect.pivot = new Vector2(0.5f, 0.5f);
        rootRect.sizeDelta = new Vector2(520f, 36f);

        var inputObject = new GameObject("SearchInput");
        inputObject.AddComponent<RectTransform>();
        inputObject.AddComponent<Image>();
        inputObject.AddComponent<TMP_InputField>();
        var inputRect = inputObject.GetComponent<RectTransform>();
        inputRect.SetParent(rootRect, false);
        inputRect.anchorMin = Vector2.zero;
        inputRect.anchorMax = Vector2.one;
        inputRect.offsetMin = Vector2.zero;
        inputRect.offsetMax = new Vector2(showClearButton ? -42f : 0f, 0f);
        inputObject.GetComponent<Image>().color = new Color(0.055f, 0.065f, 0.075f, 0.96f);

        var viewportObject = new GameObject("Text Area");
        viewportObject.AddComponent<RectTransform>();
        viewportObject.AddComponent<RectMask2D>();
        var viewport = viewportObject.GetComponent<RectTransform>();
        viewport.SetParent(inputRect, false);
        viewport.anchorMin = Vector2.zero;
        viewport.anchorMax = Vector2.one;
        viewport.offsetMin = new Vector2(12f, 4f);
        viewport.offsetMax = new Vector2(-12f, -4f);

        var text = CreateInputText("Text", viewport, string.Empty, textTemplate, Color.white);
        var placeholder = CreateInputText(
            "Placeholder",
            viewport,
            "Search Backpack",
            textTemplate,
            new Color(0.72f, 0.74f, 0.77f, 0.9f));

        var input = inputObject.GetComponent<TMP_InputField>();
        input.textViewport = viewport;
        input.textComponent = text;
        input.placeholder = placeholder;
        input.lineType = TMP_InputField.LineType.SingleLine;
        input.contentType = TMP_InputField.ContentType.Standard;
        input.characterLimit = 64;
        input.customCaretColor = true;
        input.caretColor = Color.white;
        input.selectionColor = new Color(0.2f, 0.55f, 0.8f, 0.55f);

        var searchBar = new BackpackSearchBar(rootRect, input, onSearchChanged);
        input.onValueChanged.AddListener((UnityAction<string>) searchBar.HandleInputChanged);
        if (showClearButton)
        {
            var clearObject = new GameObject("ClearSearch");
            clearObject.AddComponent<RectTransform>();
            clearObject.AddComponent<Image>();
            clearObject.AddComponent<Button>();
            var clearRect = clearObject.GetComponent<RectTransform>();
            clearRect.SetParent(rootRect, false);
            clearRect.anchorMin = new Vector2(1f, 0.5f);
            clearRect.anchorMax = new Vector2(1f, 0.5f);
            clearRect.pivot = new Vector2(1f, 0.5f);
            clearRect.sizeDelta = new Vector2(36f, 34f);
            clearRect.anchoredPosition = Vector2.zero;
            clearObject.GetComponent<Image>().color = new Color(0.08f, 0.09f, 0.1f, 0.96f);
            var clearLabel = CreateInputText("Label", clearRect, "X", textTemplate, Color.white);
            clearLabel.alignment = TextAlignmentOptions.Center;
            clearObject.GetComponent<Button>().onClick.AddListener((UnityAction) searchBar.Clear);
        }
        return searchBar;
    }

    public bool RefreshProjection(PlayerBackpack backpack)
    {
        _scratchIndices.Clear();
        if (backpack != null)
        {
            var query = _query.Trim();
            for (var i = 0; i < backpack.BackpackSlotCount; i++)
            {
                if (query.Length == 0)
                {
                    _scratchIndices.Add(i);
                    continue;
                }

                var displayName = backpack.GetBackpackSlot(i)?.ItemInstance?.Definition?.Name;
                if (!string.IsNullOrEmpty(displayName) && displayName.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                    _scratchIndices.Add(i);
            }
        }

        var changed = _projectedIndices.Count != _scratchIndices.Count;
        if (!changed)
        {
            for (var i = 0; i < _projectedIndices.Count; i++)
            {
                if (_projectedIndices[i] == _scratchIndices[i])
                    continue;

                changed = true;
                break;
            }
        }

        if (!changed)
            return false;

        _projectedIndices.Clear();
        _projectedIndices.AddRange(_scratchIndices);
        return true;
    }

    public int GetInventorySlotIndex(int projectedIndex)
    {
        return projectedIndex >= 0 && projectedIndex < _projectedIndices.Count
            ? _projectedIndices[projectedIndex]
            : -1;
    }

    public void Reset()
    {
        _query = string.Empty;
        _projectedIndices.Clear();
        _suppressInputCallback = true;
        if (_input != null)
            _input.text = string.Empty;
        _suppressInputCallback = false;
    }

    private void Clear()
    {
        if (_query.Length == 0 && string.IsNullOrEmpty(_input?.text))
            return;

        _suppressInputCallback = true;
        _input.text = string.Empty;
        _suppressInputCallback = false;
        _query = string.Empty;
        _onSearchChanged?.Invoke();
    }

    private void HandleInputChanged(string value)
    {
        if (_suppressInputCallback)
            return;

        _query = value ?? string.Empty;
        _onSearchChanged?.Invoke();
    }

    private static TextMeshProUGUI CreateInputText(
        string name,
        RectTransform parent,
        string value,
        TextMeshProUGUI template,
        Color color)
    {
        var obj = new GameObject(name);
        obj.AddComponent<RectTransform>();
        obj.AddComponent<TextMeshProUGUI>();
        var rect = obj.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        var label = obj.GetComponent<TextMeshProUGUI>();
        label.font = template.font;
        label.fontSize = 16f;
        label.alignment = TextAlignmentOptions.MidlineLeft;
        label.color = color;
        label.text = value;
        label.enableWordWrapping = false;
        label.raycastTarget = false;
        return label;
    }
}
