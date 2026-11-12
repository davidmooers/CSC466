using Unity.Collections;
using Unity.Networking.Transport;
using UnityEngine;

public class HostServer : MonoBehaviour
{

    public ushort port = 7777;
    public StatusText status;

    public Transform Player;
    public float MoveSpeed = 5f;

    public float InputTimeoutSeconds = 0.25f;
    public InputTimeoutMode TimeoutMode = InputTimeoutMode.Fixed;
    [Min(0.001f)] public float AdaptiveMinSeconds = 0.08f;
    [Min(0.001f)] public float AdaptiveMaxSeconds = 0.5f;
    public bool EnableCsvLogging = true;

    private Vector2 latestMovementInput;

    public bool IsRunning => driver.IsCreated;

    private NetworkDriver driver;

    // NativeList stores connections in unmanaged memory and is compatible with
    // Unity Transport's job-based API. Since it persists across frames, it must be disposed.
    private NativeList<NetworkConnection> connections;

    private uint latestInputSequence;
    private NetworkConnection inputSource;
    private InputTimeoutPolicy timeoutPolicy;
    private bool isTimedOut;
    private ExperimentCsvLogger experimentCsvLogger;

    private void Start()
    {
        Application.runInBackground = true;
        StartServer();
    }

    private void StartServer()
    {
        if (driver.IsCreated)
        {
            Debug.LogWarning("Tried to call StartServer on an already running server.");
            return;
        }

        driver = NetworkDriver.Create(new WebSocketNetworkInterface());

        NetworkEndpoint endpoint = NetworkEndpoint.AnyIpv4.WithPort(port);

        // bindResult will be 0 if the bind was successful, or a negative error code if it failed
        int bindResult = driver.Bind(endpoint);

        if (bindResult != 0)
        {
            string message = $"Failed to bind server to port {port}.";

            Debug.LogError(message);
            status.SetText(message);
            driver.Dispose();
            driver = default;
            return;
        }

        driver.Listen();

        // prewarm 16 connections to avoid allocations during gameplay
        connections = new NativeList<NetworkConnection>(16, Allocator.Persistent);
        timeoutPolicy = new InputTimeoutPolicy(
            TimeoutMode, InputTimeoutSeconds, AdaptiveMinSeconds, AdaptiveMaxSeconds
        );

        if (EnableCsvLogging)
        {
            experimentCsvLogger = new ExperimentCsvLogger(timeoutPolicy.TimeoutSeconds, TimeoutMode);
            Debug.Log($"CSV experiment logging started: {experimentCsvLogger.FilePath}");
        }

        string successMessage = $"Host running\nWebSocket port: {port}\nConnected players: 0";

        Debug.Log(successMessage);
        status.SetText(successMessage);
    }

    private void Update()
    {
        if (!IsRunning) return;

        // complete Unity Transport's network update
        driver.ScheduleUpdate().Complete();

        RemoveDisconnectedClients();
        AcceptNewClients();
        ProcessClientEvents();

        status.SetText(
            $"Host running\n" +
            $"WebSocket port: {port}\n" +
            $"Connected players: {connections.Length}"
        );

        MovePlayer();
    }

    private void RemoveDisconnectedClients()
    {
        for (int i = connections.Length - 1; i >= 0; i--)
        {
            if (!connections[i].IsCreated) connections.RemoveAtSwapBack(i);
        }
    }

    private void AcceptNewClients()
    {
        NetworkConnection connection;

        while ((connection = driver.Accept()) != default)
        {
            connections.Add(connection);

            Debug.Log("Client connected.");
        }
    }

    private void ProcessClientEvents()
    {
        for (int i = 0; i < connections.Length; i++)
        {

            while (true)
            {
                var eventType = driver.PopEventForConnection(connections[i], out var reader);

                if (eventType == NetworkEvent.Type.Empty) break;

                switch (eventType)
                {
                    case NetworkEvent.Type.Data:
                        ProcessMessage(connections[i], reader);
                        break;

                    case NetworkEvent.Type.Disconnect:
                        var reason = reader.ReadByte();

                        Debug.Log($"Client disconnected: {reason}");
                        if (inputSource.Equals(connections[i]))
                        {
                            latestMovementInput = Vector2.zero;
                            inputSource = default;
                            timeoutPolicy.Reset();
                            isTimedOut = false;
                        }
                        connections[i] = default;
                        break;
                }

                if (!connections[i].IsCreated) break;
            }
        }
    }

    private void ProcessMessage(NetworkConnection connection, DataStreamReader reader)
    {
        NetworkMessageType messageType = (NetworkMessageType)reader.ReadByte();

        switch (messageType)
        {
            case NetworkMessageType.Input:
                ProcessInputMessage(connection, reader);
                break;

            default:
                Debug.LogWarning($"Received unknown message type: {messageType}");
                break;
        }
    }

    private void ProcessInputMessage(NetworkConnection connection, DataStreamReader reader)
    {
        uint sequence = reader.ReadUInt();
        float x = reader.ReadFloat();
        float y = reader.ReadFloat();

        latestInputSequence = sequence;
        latestMovementInput = Vector2.ClampMagnitude(
            new Vector2(x, y),
            1f
        );

        float now = Time.unscaledTime;

        if (!inputSource.Equals(connection))
        {
            inputSource = connection;
            timeoutPolicy.Reset();
            isTimedOut = false;
        }

        float arrivalIntervalMs = timeoutPolicy.ObserveArrival(now) * 1000f;
        if (!float.IsNaN(arrivalIntervalMs))
        {

            Debug.Log(
                $"Input {sequence} | Arrival interval: {arrivalIntervalMs:F1} ms"
            );
        }

        experimentCsvLogger?.LogInputArrival(
            sequence,
            now,
            arrivalIntervalMs,
            timeoutPolicy.TimeoutSeconds,
            latestMovementInput
        );

        if (isTimedOut)
        {
            isTimedOut = false;
            experimentCsvLogger?.LogTimeoutEnded(
                sequence,
                now,
                timeoutPolicy.TimeoutSeconds,
                latestMovementInput
            );
        }
    }

    private void MovePlayer()
    {
        if (!timeoutPolicy.HasInput) return;

        float now = Time.unscaledTime;
        float inputAge = now - timeoutPolicy.LastArrival;

        bool timedOut = timeoutPolicy.IsExpired(now);

        if (timedOut && !isTimedOut)
        {
            isTimedOut = true;
            experimentCsvLogger?.LogTimeoutStarted(
                latestInputSequence,
                now,
                inputAge * 1000f,
                timeoutPolicy.TimeoutSeconds,
                latestMovementInput
            );
        }

        status.SetText(
            $"Host running\n" +
            $"Connected players: {connections.Length}\n" +
            $"Input age: {inputAge * 1000f:F0} ms\n" +
            $"Timeout ({TimeoutMode}): {timeoutPolicy.TimeoutSeconds * 1000f:F0} ms\n" +
            $"Input state: {(timedOut ? "TIMED OUT" : "ACTIVE")}"
        );

        // Stop applying old input when updates stop arriving.
        if (Player == null || timedOut) return;

        Vector3 direction = new Vector3(
            latestMovementInput.x,
            0f,
            latestMovementInput.y
        );

        Player.position += direction * MoveSpeed * Time.deltaTime;
    }

    private void OnDestroy()
    {
        experimentCsvLogger?.Dispose();

        if (driver.IsCreated) driver.Dispose();

        if (connections.IsCreated) connections.Dispose();
    }

}
