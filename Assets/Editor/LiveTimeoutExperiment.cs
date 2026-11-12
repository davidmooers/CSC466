using System.Globalization;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class LiveTimeoutExperiment
{
    private const string SessionKey = "CSC466.LiveTimeoutExperiment";
    private static readonly string Output = Path.GetFullPath("output/experiments");
    private static HostServer host;
    private static ControllerClient controller;
    private static GameObject player;
    private static StreamWriter phases;
    private static string logDirectory;
    private static string[] priorLogs;
    private static InputTimeoutMode mode;
    private static float next;
    private static int step;
    private static int trial;

    static LiveTimeoutExperiment()
    {
        EditorApplication.playModeStateChanged += state =>
        {
            if (!SessionState.GetBool(SessionKey, false)) return;
            if (state == PlayModeStateChange.EnteredPlayMode) StartRun();
            if (state != PlayModeStateChange.EnteredEditMode) return;
            SessionState.EraseBool(SessionKey);
            EditorApplication.Exit(SessionState.GetInt(SessionKey + ".exit", 1));
        };
    }

    // Run without -quit so Unity can enter Play Mode and exit after both runs.
    public static void Run()
    {
        if (!Application.isBatchMode)
            throw new System.InvalidOperationException("Run this experiment with Unity -batchmode.");
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        SessionState.SetBool(SessionKey, true);
        SessionState.SetInt(SessionKey + ".exit", 1);
        EditorApplication.EnterPlaymode();
    }

    private static void StartRun()
    {
        try
        {
            Directory.CreateDirectory(Output);
            logDirectory = Path.Combine(Application.persistentDataPath, "ExperimentLogs");
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = 120;
            StartMode(InputTimeoutMode.Fixed);
            EditorApplication.update += Tick;
        }
        catch (System.Exception error)
        {
            Fail(error);
        }
    }

    private static void StartMode(InputTimeoutMode selected)
    {
        mode = selected;
        priorLogs = Directory.Exists(logDirectory) ? Directory.GetFiles(logDirectory, "input-timeout-*.csv") : new string[0];
        phases = new StreamWriter(Path.Combine(Output, $"{mode}-phases.csv"));
        phases.WriteLine("trial,phase,elapsed_seconds");

        player = new GameObject("Experiment player");
        host = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/NetworkHost.prefab"))
            .GetComponent<HostServer>();
        host.status = CreateStatus(host.transform);
        host.Player = player.transform;
        host.TimeoutMode = mode;
        host.port = mode == InputTimeoutMode.Fixed ? (ushort)7777 : (ushort)7778;

        controller = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/NetworkClient.prefab"))
            .GetComponent<ControllerClient>();
        controller.status = CreateStatus(controller.transform);
        controller.AddressInputField = new GameObject("Address", typeof(RectTransform), typeof(TMP_InputField))
            .GetComponent<TMP_InputField>();
        controller.AddressInputField.transform.SetParent(controller.transform);
        controller.AddressInputField.text = "127.0.0.1";
        controller.Port = host.port;
        controller.PauseInputSending = true;
        trial = 1;
        step = 0;
        next = Time.unscaledTime + 0.3f;
    }

    private static StatusText CreateStatus(Transform parent)
    {
        var label = new GameObject("Status", typeof(TextMeshProUGUI), typeof(StatusText));
        label.transform.SetParent(parent);
        return label.GetComponent<StatusText>();
    }

    private static void Tick()
    {
        try
        {
            if (Time.unscaledTime < next) return;
            switch (step++)
            {
                case 0:
                    controller.Connect();
                    Schedule(0.5f);
                    break;
                case 1:
                    controller.ExperimentMovementInput = Vector2.right;
                    controller.PauseInputSending = false;
                    Schedule(1.2f);
                    break;
                case 2:
                    if (player.transform.position.x <= 0f)
                        throw new System.InvalidOperationException("Controller did not move the host player.");
                    Record("HoldPause");
                    controller.PauseInputSending = true;
                    Schedule(0.2f);
                    break;
                case 3:
                    Record("HoldResume");
                    controller.PauseInputSending = false;
                    Schedule(0.6f);
                    break;
                case 4:
                    Record("ReleasePause");
                    controller.PauseInputSending = true;
                    controller.ExperimentMovementInput = Vector2.zero;
                    Schedule(0.7f);
                    break;
                case 5:
                    Record("ReleaseResume");
                    controller.PauseInputSending = false;
                    Schedule(0.5f);
                    break;
                case 6:
                    if (trial++ < 3)
                    {
                        step = 1;
                        Schedule(0f);
                        break;
                    }
                    phases.Dispose();
                    phases = null;
                    Object.Destroy(controller.gameObject);
                    Object.Destroy(host.gameObject);
                    Object.Destroy(player);
                    Schedule(0.3f);
                    break;
                case 7:
                    string log = Directory.GetFiles(logDirectory, "input-timeout-*.csv").Except(priorLogs).Single();
                    File.Copy(log, Path.Combine(Output, $"{mode}-host.csv"), true);
                    if (mode == InputTimeoutMode.Fixed) StartMode(InputTimeoutMode.Adaptive);
                    else Finish(0);
                    break;
            }
        }
        catch (System.Exception error)
        {
            Fail(error);
        }
    }

    private static void Record(string phase)
    {
        phases.WriteLine(string.Format(CultureInfo.InvariantCulture, "{0},{1},{2:F6}", trial, phase, Time.unscaledTime));
    }

    private static void Schedule(float seconds) => next = Time.unscaledTime + seconds;

    private static void Fail(System.Exception error)
    {
        Debug.LogException(error);
        Finish(1);
    }

    private static void Finish(int exitCode)
    {
        EditorApplication.update -= Tick;
        phases?.Dispose();
        SessionState.SetInt(SessionKey + ".exit", exitCode);
        EditorApplication.ExitPlaymode();
    }
}
