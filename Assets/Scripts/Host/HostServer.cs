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
    public bool EnableCsvLogging = true;

    private Vector2 latestMovementInput;

    public bool IsRunning => driver.IsCreated;

    private NetworkDriver driver;

    // NativeList stores connections in unmanaged memory and is compatible with
    // Unity Transport's job-based API. Since it persists across frames, it must be disposed.
    private NativeList<NetworkConnection> connections;

    private uint latestInputSequence;
    private float lastInputReceivedTime;
    private bool hasReceivedInput;
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
            return;
        }

        driver.Listen();

        // prewarm 16 connections to avoid allocations during gameplay
        connections = new NativeList<NetworkConnection>(16, Allocator.Persistent);

        if (EnableCsvLogging)
        {
            experimentCsvLogger = new ExperimentCsvLogger(InputTimeoutSeconds);
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
                        latestMovementInput = Vector2.zero;
                        connections[i] = default;
                        break;
                }
            }
        }
    }

    private void ProcessMessage(NetworkConnection connection, DataStreamReader reader)
    {
        NetworkMessageType messageType = (NetworkMessageType)reader.ReadByte();

        switch (messageType)
        {
            case NetworkMessageType.Input:
                ProcessInputMessage(reader);
                break;

            default:
                Debug.LogWarning($"Received unknown message type: {messageType}");
                break;
        }
    }

    private void ProcessInputMessage(DataStreamReader reader)
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

        float arrivalIntervalMs = float.NaN;

        if (hasReceivedInput)
        {
            arrivalIntervalMs = (now - lastInputReceivedTime) * 1000f;

            Debug.Log(
                $"Input {sequence} | Arrival interval: {arrivalIntervalMs:F1} ms"
            );
        }

        lastInputReceivedTime = now;
        hasReceivedInput = true;
        experimentCsvLogger?.LogInputArrival(
            sequence,
            now,
            arrivalIntervalMs,
            InputTimeoutSeconds,
            latestMovementInput
        );

        if (isTimedOut)
        {
            isTimedOut = false;
            experimentCsvLogger?.LogTimeoutEnded(
                sequence,
                now,
                InputTimeoutSeconds,
                latestMovementInput
            );
        }
    }

    private void MovePlayer()
    {
        if (!hasReceivedInput) return;

        float inputAge = Time.unscaledTime - lastInputReceivedTime;

        bool timedOut = inputAge > InputTimeoutSeconds;

        if (timedOut && !isTimedOut)
        {
            isTimedOut = true;
            experimentCsvLogger?.LogTimeoutStarted(
                latestInputSequence,
                Time.unscaledTime,
                inputAge * 1000f,
                InputTimeoutSeconds,
                latestMovementInput
            );
        }

        status.SetText(
            $"Host running\n" +
            $"Connected players: {connections.Length}\n" +
            $"Input age: {inputAge * 1000f:F0} ms\n" +
            $"Timeout: {InputTimeoutSeconds * 1000f:F0} ms\n" +
            $"Input state: {(hasReceivedInput ? (timedOut ? "TIMED OUT" : "ACTIVE") : "WAITING")}"
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
