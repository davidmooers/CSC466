using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Builds the editor test role selector when the Menu scene is loaded.
/// Each Multiplayer Play Mode player chooses its own scene independently.
/// </summary>
public static class MenuSceneNavigator
{
    private const string MenuSceneName = "Menu";
    private const string HostSceneName = "_HostScene";
    private const string ControllerSceneName = "_ControllerScene";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void RegisterSceneLoadHandler()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode loadSceneMode)
    {
        if (scene.name != MenuSceneName)
        {
            return;
        }

        Canvas existingCanvas = Object.FindFirstObjectByType<Canvas>();

        if (existingCanvas != null)
        {
            existingCanvas.gameObject.SetActive(false);
        }

        CreateMenu();
    }

    private static void CreateMenu()
    {
        GameObject canvasObject = new GameObject("RoleMenu", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        Canvas canvas = canvasObject.GetComponent<Canvas>();
        CanvasScaler canvasScaler = canvasObject.GetComponent<CanvasScaler>();

        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvasScaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        canvasScaler.referenceResolution = new Vector2(900f, 600f);

        CreateText(canvas.transform, "CSC 466\nPhone Controller Timeout Experiments", new Vector2(0f, 145f), new Vector2(700f, 120f), 36f, FontStyles.Bold);
        CreateText(canvas.transform, "Choose a role for this Multiplayer Play Mode instance", new Vector2(0f, 55f), new Vector2(700f, 45f), 22f, FontStyles.Normal);
        CreateButton(canvas.transform, "Start Host", "Player 1: start this first", new Vector2(0f, -35f), () => LoadScene(HostSceneName));
        CreateButton(canvas.transform, "Start Controller", "Player 2: select this, then click Connect", new Vector2(0f, -165f), () => LoadScene(ControllerSceneName));
        CreateText(canvas.transform, "Use 127.0.0.1 in the controller scene. The host listens on WebSocket port 7777.", new Vector2(0f, -265f), new Vector2(760f, 45f), 17f, FontStyles.Italic);
    }

    private static void CreateText(Transform parent, string content, Vector2 position, Vector2 size, float fontSize, FontStyles fontStyle)
    {
        GameObject textObject = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
        RectTransform rectTransform = textObject.GetComponent<RectTransform>();
        TextMeshProUGUI text = textObject.GetComponent<TextMeshProUGUI>();

        rectTransform.SetParent(parent, false);
        rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
        rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        rectTransform.anchoredPosition = position;
        rectTransform.sizeDelta = size;

        text.text = content;
        text.font = TMP_Settings.defaultFontAsset;
        text.fontSize = fontSize;
        text.fontStyle = fontStyle;
        text.alignment = TextAlignmentOptions.Center;
        text.color = Color.white;
    }

    private static void CreateButton(Transform parent, string label, string detail, Vector2 position, UnityEngine.Events.UnityAction onClick)
    {
        GameObject buttonObject = new GameObject(label, typeof(RectTransform), typeof(Image), typeof(Button));
        RectTransform rectTransform = buttonObject.GetComponent<RectTransform>();
        Image image = buttonObject.GetComponent<Image>();
        Button button = buttonObject.GetComponent<Button>();

        rectTransform.SetParent(parent, false);
        rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
        rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        rectTransform.anchoredPosition = position;
        rectTransform.sizeDelta = new Vector2(500f, 100f);

        image.color = new Color(0.12f, 0.36f, 0.60f, 1f);
        button.targetGraphic = image;
        button.onClick.AddListener(onClick);

        CreateText(buttonObject.transform, label + "\n<size=55%>" + detail + "</size>", Vector2.zero, new Vector2(470f, 80f), 30f, FontStyles.Bold);
    }

    private static void LoadScene(string sceneName)
    {
        SceneManager.LoadScene(sceneName);
    }
}
