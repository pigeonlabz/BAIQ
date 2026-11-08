using System.Diagnostics;
using System.Reflection;
using MelonLoader;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

#if IL2CPP
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.UI.MainMenu;
using Il2CppTMPro;
#elif MONO
using ScheduleOne.DevUtilities;
using ScheduleOne.UI.MainMenu;
using TMPro;
#endif

namespace Backpack.Compatibility;

internal sealed class LegacyConflict
{
    public string DisplayName { get; init; }
    public string SourcePath { get; init; }
    public LegacyConflictConfidence Confidence { get; init; }
    public string Fingerprint { get; init; }
    public bool CanQuarantine => Confidence == LegacyConflictConfidence.Positive
                                 && !string.IsNullOrWhiteSpace(SourcePath)
                                 && !string.IsNullOrWhiteSpace(Fingerprint);
}

internal static class LegacyConflictGuard
{
    private const string PopupTitle = "Conflicting Backpack Mod Detected";
    private const string PopupBody =
        "An older Backpack mod is installed alongside Inventory Expanded.\n\n" +
        "Running both mods together can cause inventory, transfer, or UI problems. " +
        "Inventory Expanded already includes the Backpack functionality.\n\n" +
        "You can quarantine the conflicting mod automatically. This does not delete it.\n\n" +
        "Restart Schedule I after resolving the conflict.";
    private const string UnknownPopupBody =
        "An older or incompatible Backpack mod appears to be installed alongside Inventory Expanded.\n\n" +
        "Running both mods together can cause inventory, transfer, or UI problems. " +
        "Inventory Expanded already includes the Backpack functionality.\n\n" +
        "Inventory Expanded has disabled itself for safety. This DLL could not be positively " +
        "identified as a known legacy build, so it will not be moved automatically. Remove it " +
        "manually from the Mods folder.\n\n" +
        "Restart Schedule I after resolving the conflict.";
    private const string UnverifiedKnownPopupBody =
        "An older Backpack mod is installed alongside Inventory Expanded.\n\n" +
        "Running both mods together can cause inventory, transfer, or UI problems. " +
        "Inventory Expanded already includes the Backpack functionality.\n\n" +
        "Inventory Expanded has disabled itself for safety. The legacy mod was recognized, " +
        "but its exact active DLL could not be verified, so it will not be moved automatically. " +
        "Remove it manually from the Mods folder.\n\n" +
        "Restart Schedule I after resolving the conflict.";
    private static LegacyConflict _conflict;
    private static bool _dismissed;
    private static bool _popupShown;
    private static bool _quarantined;
    private static string _quarantinedFileName;
    private static string _status;
    private static float _nextCreateAttempt;
    private static GameObject _popupRoot;
    private static RectTransform _canvasRect;
    private static RectTransform _panelRect;
    private static TextMeshProUGUI _titleLabel;
    private static TextMeshProUGUI _bodyLabel;
    private static GameObject _actionsRoot;
    private static Button _quarantineButton;
    private static Button _quitButton;
    private static Button _nativeButtonTemplate;
    private static TextMeshProUGUI _nativeLabelTemplate;
    private static string _lastPopupError;
    private static readonly RestartRequiredCoordinator RestartCoordinator = new();

    public static bool Detect(BackpackMod current)
    {
        _conflict = FindConflict(current);
        if (_conflict == null)
            return false;

        var path = string.IsNullOrWhiteSpace(_conflict.SourcePath) ? "unknown DLL path" : _conflict.SourcePath;
        Logger.Error("============================================================");
        Logger.Error("CONFLICTING BACKPACK MOD DETECTED");
        Logger.Error($"Detected: {_conflict.DisplayName} ({path})");
        Logger.Error("Inventory Expanded runtime initialization was stopped. Remove or quarantine the older Backpack mod, then restart Schedule I.");
        if (_conflict.Confidence != LegacyConflictConfidence.Positive)
            Logger.Error("The apparent Backpack DLL was not positively identified and will not be moved automatically.");
        Logger.Error("No saves, preferences, legacy configuration, or inventory data were changed.");
        Logger.Error("============================================================");
        return true;
    }

    public static void UpdatePrompt()
    {
        if (_popupShown)
        {
            UpdatePanelSize();
            return;
        }

        if (_conflict == null || _dismissed || Time.unscaledTime < _nextCreateAttempt)
            return;

        _nextCreateAttempt = Time.unscaledTime + 0.5f;
        if (EventSystem.current == null)
            return;

        try
        {
            var nativePopup = Singleton<MainMenuPopup>.Instance;
            var nativeScreen = nativePopup?.Screen;
            var nativeTitle = nativePopup?.Title;
            var nativeDescription = nativePopup?.Description;
            var canvas = nativeScreen?.GetComponentInParent<Canvas>();
            _canvasRect = canvas?.transform.GetComponent<RectTransform>();
            if (nativeScreen == null || nativeTitle == null || nativeDescription == null
                || canvas == null || !canvas.isActiveAndEnabled || _canvasRect == null
                || _canvasRect.rect.width < 1f || _canvasRect.rect.height < 1f)
            {
                return;
            }

            CreatePrompt(canvas, nativeScreen.GetComponentInChildren<Button>(true), nativeTitle, nativeDescription);
            _popupShown = true;
            _lastPopupError = null;
            Logger.Info("[ConflictGuard] Displayed the conflict popup on the native main-menu canvas.");
        }
        catch (Exception exception)
        {
            DestroyPromptRoot();
            if (!string.Equals(_lastPopupError, exception.Message, StringComparison.Ordinal))
            {
                _lastPopupError = exception.Message;
                Logger.Warning($"[ConflictGuard] Conflict popup creation is not ready yet: {exception.Message}");
            }
        }
    }

    private static void CreatePrompt(
        Canvas parentCanvas,
        Button nativeButton,
        TextMeshProUGUI nativeTitle,
        TextMeshProUGUI nativeDescription)
    {
        _popupRoot = new GameObject("InventoryExpanded_LegacyConflictPopup");
        _popupRoot.AddComponent<RectTransform>();
        _popupRoot.AddComponent<Canvas>();
        _popupRoot.AddComponent<GraphicRaycaster>();
        _popupRoot.AddComponent<Image>();

        var rootRect = _popupRoot.GetComponent<RectTransform>();
        rootRect.SetParent(_canvasRect, false);
        rootRect.anchorMin = Vector2.zero;
        rootRect.anchorMax = Vector2.one;
        rootRect.offsetMin = Vector2.zero;
        rootRect.offsetMax = Vector2.zero;
        rootRect.SetAsLastSibling();

        var popupCanvas = _popupRoot.GetComponent<Canvas>();
        popupCanvas.overrideSorting = true;
        popupCanvas.sortingLayerID = parentCanvas.sortingLayerID;
        popupCanvas.sortingOrder = parentCanvas.sortingOrder + 100;
        _popupRoot.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.68f);

        var panel = new GameObject("Panel");
        panel.AddComponent<RectTransform>();
        panel.AddComponent<Image>();
        panel.AddComponent<Outline>();
        _panelRect = panel.GetComponent<RectTransform>();
        _panelRect.SetParent(rootRect, false);
        _panelRect.anchorMin = new Vector2(0.5f, 0.5f);
        _panelRect.anchorMax = new Vector2(0.5f, 0.5f);
        _panelRect.pivot = new Vector2(0.5f, 0.5f);
        _panelRect.anchoredPosition = Vector2.zero;
        UpdatePanelSize();

        panel.GetComponent<Image>().color = new Color(0.035f, 0.045f, 0.055f, 0.99f);
        var outline = panel.GetComponent<Outline>();
        outline.effectColor = new Color(0.35f, 0.43f, 0.5f, 0.9f);
        outline.effectDistance = new Vector2(1f, -1f);

        _nativeButtonTemplate = nativeButton;
        _nativeLabelTemplate = nativeTitle;

        _titleLabel = CreateLabel("Title", _panelRect, nativeTitle);
        var titleRect = _titleLabel.rectTransform;
        titleRect.anchorMin = new Vector2(0f, 1f);
        titleRect.anchorMax = new Vector2(1f, 1f);
        titleRect.pivot = new Vector2(0.5f, 1f);
        titleRect.offsetMin = new Vector2(26f, -68f);
        titleRect.offsetMax = new Vector2(-26f, -18f);
        _titleLabel.text = PopupTitle;
        _titleLabel.fontSize = 28f;
        _titleLabel.alignment = TextAlignmentOptions.Center;

        _bodyLabel = CreateLabel("Message", _panelRect, nativeDescription);
        var bodyRect = _bodyLabel.rectTransform;
        bodyRect.anchorMin = Vector2.zero;
        bodyRect.anchorMax = Vector2.one;
        bodyRect.offsetMin = new Vector2(30f, 92f);
        bodyRect.offsetMax = new Vector2(-30f, -82f);
        _bodyLabel.alignment = TextAlignmentOptions.TopLeft;
        _bodyLabel.enableWordWrapping = true;
        _bodyLabel.enableAutoSizing = true;
        _bodyLabel.fontSizeMin = 14f;
        _bodyLabel.fontSizeMax = 20f;
        _bodyLabel.overflowMode = TextOverflowModes.Ellipsis;

        var buttonsRect = CreateActionsContainer("InitialActions");
        if (_conflict.CanQuarantine)
            _quarantineButton = CreateButton("Quarantine for me", buttonsRect, nativeButton, nativeTitle, QuarantineConflict);
        CreateButton("Open Mods Folder", buttonsRect, nativeButton, nativeTitle, OpenModsFolder);
        CreateButton("Close", buttonsRect, nativeButton, nativeTitle, ClosePrompt);
        RefreshMessage();
    }

    private static RectTransform CreateActionsContainer(string name)
    {
        var buttons = new GameObject(name);
        buttons.AddComponent<RectTransform>();
        buttons.AddComponent<HorizontalLayoutGroup>();
        var buttonsRect = buttons.GetComponent<RectTransform>();
        buttonsRect.SetParent(_panelRect, false);
        buttonsRect.anchorMin = new Vector2(0f, 0f);
        buttonsRect.anchorMax = new Vector2(1f, 0f);
        buttonsRect.pivot = new Vector2(0.5f, 0f);
        buttonsRect.offsetMin = new Vector2(28f, 24f);
        buttonsRect.offsetMax = new Vector2(-28f, 68f);
        var buttonLayout = buttons.GetComponent<HorizontalLayoutGroup>();
        buttonLayout.spacing = 12f;
        buttonLayout.childAlignment = TextAnchor.MiddleCenter;
        buttonLayout.childControlWidth = true;
        buttonLayout.childControlHeight = true;
        buttonLayout.childForceExpandWidth = true;
        buttonLayout.childForceExpandHeight = true;
        _actionsRoot = buttons;
        return buttonsRect;
    }

    private static TextMeshProUGUI CreateLabel(string name, RectTransform parent, TextMeshProUGUI template)
    {
        var obj = new GameObject(name);
        obj.AddComponent<RectTransform>();
        obj.AddComponent<TextMeshProUGUI>();
        var rect = obj.GetComponent<RectTransform>();
        rect.SetParent(parent, false);

        var label = obj.GetComponent<TextMeshProUGUI>();
        label.font = template.font;
        label.fontStyle = template.fontStyle;
        label.color = Color.white;
        label.raycastTarget = false;
        return label;
    }

    private static Button CreateButton(
        string text,
        RectTransform parent,
        Button nativeButton,
        TextMeshProUGUI labelTemplate,
        Action onClick)
    {
        var obj = new GameObject("Button_" + text.Replace(" ", string.Empty));
        obj.AddComponent<RectTransform>();
        obj.AddComponent<Image>();
        obj.AddComponent<Button>();
        var rect = obj.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        var layout = obj.AddComponent<LayoutElement>();
        layout.minWidth = 130f;
        layout.preferredWidth = 180f;
        layout.minHeight = 42f;
        layout.preferredHeight = 42f;

        var image = obj.GetComponent<Image>();
        var nativeImage = nativeButton?.GetComponent<Image>();
        if (nativeImage != null)
        {
            image.sprite = nativeImage.sprite;
            image.type = nativeImage.type;
            image.material = nativeImage.material;
            image.color = nativeImage.color;
        }
        else
        {
            image.color = new Color(0.12f, 0.15f, 0.18f, 1f);
        }

        var button = obj.GetComponent<Button>();
        button.targetGraphic = image;
        button.transition = nativeButton?.transition ?? Selectable.Transition.ColorTint;
        if (nativeButton != null)
        {
            button.colors = nativeButton.colors;
            button.navigation = nativeButton.navigation;
        }
        button.onClick.AddListener(onClick);

        var label = CreateLabel("Label", rect, labelTemplate);
        label.rectTransform.anchorMin = Vector2.zero;
        label.rectTransform.anchorMax = Vector2.one;
        label.rectTransform.offsetMin = new Vector2(8f, 4f);
        label.rectTransform.offsetMax = new Vector2(-8f, -4f);
        label.text = text;
        label.fontSize = 18f;
        label.enableAutoSizing = true;
        label.fontSizeMin = 12f;
        label.fontSizeMax = 18f;
        label.alignment = TextAlignmentOptions.Center;
        return button;
    }

    private static void UpdatePanelSize()
    {
        if (_canvasRect == null || _panelRect == null)
            return;

        var availableWidth = Mathf.Max(280f, _canvasRect.rect.width - 32f);
        var availableHeight = Mathf.Max(300f, _canvasRect.rect.height - 32f);
        var desiredHeight = _conflict?.CanQuarantine == true || _quarantined ? 500f : 540f;
        _panelRect.sizeDelta = new Vector2(Mathf.Min(680f, availableWidth), Mathf.Min(desiredHeight, availableHeight));
    }

    private static void RefreshMessage()
    {
        if (_bodyLabel == null || _conflict == null)
            return;

        if (_quarantined)
        {
            var quarantineBody = $"{_quarantinedFileName} has been moved to Mods\\Disabled.\n\n" +
                                 "Schedule I needs to restart before Inventory Expanded can safely start. " +
                                 "Choose Quit Game to close it safely now, then relaunch Schedule I through Steam. " +
                                 "Choose Later to keep the game open; the change remains saved, but Inventory Expanded stays inactive until you close and relaunch the game.";
            _bodyLabel.text = string.IsNullOrWhiteSpace(_status)
                ? quarantineBody
                : $"{quarantineBody}\n\n{_status}";
            return;
        }

        var detectedFile = string.IsNullOrWhiteSpace(_conflict.SourcePath)
            ? null
            : Path.GetFileName(_conflict.SourcePath);
        var detected = string.IsNullOrWhiteSpace(detectedFile)
            ? $"Detected: {_conflict.DisplayName}"
            : $"Detected: {_conflict.DisplayName} ({detectedFile})";
        var body = _conflict.Confidence != LegacyConflictConfidence.Positive
            ? UnknownPopupBody
            : _conflict.CanQuarantine
                ? PopupBody
                : UnverifiedKnownPopupBody;
        _bodyLabel.text = string.IsNullOrWhiteSpace(_status)
            ? $"{body}\n\n{detected}"
            : $"{body}\n\n{detected}\n\n{_status}";
    }

    private static LegacyConflict FindConflict(BackpackMod current)
    {
        var currentAssembly = typeof(BackpackMod).Assembly;
        var currentPath = current.MelonAssembly?.Location ?? currentAssembly.Location;
        LegacyConflict possible = null;

        foreach (var mod in MelonMod.RegisteredMelons)
        {
            if (ReferenceEquals(mod, current))
                continue;

            var assembly = mod.MelonAssembly?.Assembly;
            var assemblyName = assembly?.GetName().Name;
            var typeName = mod.Info?.SystemType?.FullName;
            var nexusId = GetNexusModId(assembly);
            var confidence = LegacyConflictPolicy.Classify(
                assemblyName,
                typeName,
                mod.Info?.Name,
                mod.Info?.Author,
                nexusId);
            if (confidence == LegacyConflictConfidence.None)
                continue;

            var conflict = CreateConflict(
                $"{mod.Info?.Name ?? assemblyName ?? "Unknown Backpack mod"} {mod.Info?.Version}".Trim(),
                mod.MelonAssembly?.Location,
                confidence);
            if (confidence == LegacyConflictConfidence.Positive)
                return conflict;
            possible ??= conflict;
        }

        var modsDirectory = MelonLoader.Utils.MelonEnvironment.ModsDirectory;
        if (!string.IsNullOrWhiteSpace(modsDirectory) && Directory.Exists(modsDirectory))
        {
            foreach (var path in Directory.EnumerateFiles(modsDirectory, "*.dll", SearchOption.TopDirectoryOnly))
            {
                if (SamePath(path, currentPath))
                    continue;

                try
                {
                    var assemblyName = AssemblyName.GetAssemblyName(path).Name;
                    var confidence = LegacyConflictPolicy.Classify(assemblyName, null, null, null, null);
                    if (confidence == LegacyConflictConfidence.Positive)
                        return CreateConflict($"legacy assembly {assemblyName}", path, confidence);

                    if (LegacyConflictPolicy.IsApparentBackpackDll(path))
                        possible ??= CreateConflict($"unrecognized assembly {assemblyName}", path, LegacyConflictConfidence.Possible);
                }
                catch (BadImageFormatException)
                {
                    if (LegacyConflictPolicy.IsApparentBackpackDll(path))
                        possible ??= CreateConflict($"unrecognized file {Path.GetFileName(path)}", path, LegacyConflictConfidence.Possible);
                }
                catch (Exception exception)
                {
                    Logger.Warning($"[ConflictGuard] Could not inspect {Path.GetFileName(path)}: {exception.Message}");
                    if (LegacyConflictPolicy.IsApparentBackpackDll(path))
                        possible ??= CreateConflict($"unrecognized file {Path.GetFileName(path)}", path, LegacyConflictConfidence.Possible);
                }
            }
        }

        return possible;
    }

    private static LegacyConflict CreateConflict(
        string displayName,
        string sourcePath,
        LegacyConflictConfidence confidence)
    {
        string fingerprint = null;
        if (confidence == LegacyConflictConfidence.Positive
            && !string.IsNullOrWhiteSpace(sourcePath)
            && !SamePath(sourcePath, typeof(BackpackMod).Assembly.Location))
        {
            if (!LegacyConflictPolicy.TryFingerprintActiveDll(
                    sourcePath,
                    MelonLoader.Utils.MelonEnvironment.ModsDirectory,
                    out fingerprint,
                    out var fingerprintError))
            {
                Logger.Warning($"[ConflictGuard] {fingerprintError} Automatic quarantine will not be offered.");
            }
        }

        return new LegacyConflict
        {
            DisplayName = displayName,
            SourcePath = sourcePath,
            Confidence = confidence,
            Fingerprint = fingerprint
        };
    }

    private static string GetNexusModId(Assembly assembly)
    {
        if (assembly == null)
            return null;

        try
        {
            return assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
                .FirstOrDefault(attribute => string.Equals(attribute.Key, "NexusModID", StringComparison.OrdinalIgnoreCase))
                ?.Value;
        }
        catch
        {
            return null;
        }
    }

    private static void QuarantineConflict()
    {
        if (_conflict == null || !_conflict.CanQuarantine || _quarantined)
            return;

        var source = _conflict.SourcePath;
        if (_quarantineButton != null)
        {
            _quarantineButton.interactable = false;
            SetButtonText(_quarantineButton, "Quarantining...");
        }

        if (!LegacyConflictPolicy.TryDisable(
                source,
                MelonLoader.Utils.MelonEnvironment.ModsDirectory,
                _conflict.Fingerprint,
                out var destination,
                out var error))
        {
            _status = error;
            Logger.Error($"[ConflictGuard] {error}");
            if (_quarantineButton != null)
            {
                _quarantineButton.interactable = true;
                SetButtonText(_quarantineButton, "Quarantine for me");
            }
            RefreshMessage();
            return;
        }

        if (!VerifyQuarantine(source, destination, MelonLoader.Utils.MelonEnvironment.ModsDirectory, out var verificationError))
        {
            _status = verificationError;
            Logger.Error($"[ConflictGuard] {verificationError} Inventory Expanded remains inactive; restart Schedule I manually after inspecting the Mods folder.");
            if (_quarantineButton != null)
                SetButtonText(_quarantineButton, "Verification failed");
            RefreshMessage();
            return;
        }

        var quarantinedFile = Path.GetFileName(source);
        Logger.Error($"[ConflictGuard] {quarantinedFile} was quarantined to {destination}. A game restart is required; Inventory Expanded remains inactive.");
        _quarantined = true;
        _quarantinedFileName = quarantinedFile;
        _status = null;
        RestartCoordinator.MarkActionApplied();
        _conflict = new LegacyConflict
        {
            DisplayName = _conflict.DisplayName,
            SourcePath = destination,
            Confidence = _conflict.Confidence
        };
        ShowRestartActions();
        RefreshMessage();
    }

    private static bool VerifyQuarantine(string source, string destination, string modsDirectory, out string error)
    {
        error = null;
        try
        {
            var disabledDirectory = Path.GetFullPath(Path.Combine(modsDirectory, "Disabled"))
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var destinationPath = Path.GetFullPath(destination);
            if (File.Exists(source) || !File.Exists(destinationPath)
                                    || !destinationPath.StartsWith(disabledDirectory, StringComparison.OrdinalIgnoreCase))
            {
                error = "The quarantine move could not be verified. Please inspect the Mods folder before restarting Schedule I.";
                return false;
            }

            return true;
        }
        catch (Exception exception)
        {
            error = $"The quarantine move could not be verified: {exception.Message}";
            return false;
        }
    }

    private static void ShowRestartActions()
    {
        if (_titleLabel != null)
            _titleLabel.text = "Legacy Backpack quarantined";

        if (_actionsRoot != null)
        {
            _actionsRoot.SetActive(false);
            UnityEngine.Object.Destroy(_actionsRoot);
        }

        var buttonsRect = CreateActionsContainer("RestartActions");
        _quitButton = CreateButton(
            "Quit Game",
            buttonsRect,
            _nativeButtonTemplate,
            _nativeLabelTemplate,
            QuitGame);
        CreateButton("Later", buttonsRect, _nativeButtonTemplate, _nativeLabelTemplate, DismissRestartRequired);
        _quarantineButton = null;
        if (EventSystem.current != null && _quitButton != null)
            EventSystem.current.SetSelectedGameObject(_quitButton.gameObject);
    }

    private static void QuitGame()
    {
        if (!RestartCoordinator.CanQuit)
            return;

        Logger.Info("[ConflictGuard] Quit Game was requested after verified legacy DLL quarantine; relaunch Schedule I manually through Steam.");
        if (_quitButton != null)
        {
            _quitButton.interactable = false;
            SetButtonText(_quitButton, "Quitting...");
        }

        if (RestartCoordinator.TryQuit(Application.Quit, out var error))
            return;

        _status = error;
        Logger.Error($"[ConflictGuard] {error}");
        if (_quitButton != null)
        {
            _quitButton.interactable = true;
            SetButtonText(_quitButton, "Quit Game");
        }
        RefreshMessage();
    }

    private static void DismissRestartRequired()
    {
        Logger.Info("[ConflictGuard] Restart reminder dismissed; the quarantined DLL remains disabled and Schedule I will keep running until the user closes it.");
        ClosePrompt();
    }

    private static void SetButtonText(Button button, string text)
    {
        var label = button?.GetComponentInChildren<TextMeshProUGUI>(true);
        if (label != null)
            label.text = text;
    }

    private static void OpenModsFolder()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = MelonLoader.Utils.MelonEnvironment.ModsDirectory,
                UseShellExecute = true
            });
        }
        catch (Exception exception)
        {
            _status = $"Could not open the Mods folder: {exception.Message}";
            Logger.Error($"[ConflictGuard] {_status}");
            RefreshMessage();
        }
    }

    private static void ClosePrompt()
    {
        _dismissed = true;
        DestroyPromptRoot();
    }

    private static void DestroyPromptRoot()
    {
        if (_popupRoot != null)
            UnityEngine.Object.Destroy(_popupRoot);
        _popupRoot = null;
        _canvasRect = null;
        _panelRect = null;
        _titleLabel = null;
        _bodyLabel = null;
        _actionsRoot = null;
        _quarantineButton = null;
        _quitButton = null;
        _nativeButtonTemplate = null;
        _nativeLabelTemplate = null;
    }

    private static bool SamePath(string left, string right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
            return false;

        try
        {
            return string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
}
