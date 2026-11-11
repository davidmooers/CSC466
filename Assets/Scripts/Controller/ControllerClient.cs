using TMPro;
using Unity.Collections;
using Unity.Networking.Transport;
using UnityEngine;
using UnityEngine.InputSystem;

public class ControllerClient : MonoBehaviour
{

    public ushort Port = 7777;
    public float SendRate = 30f;

    public TMP_InputField AddressInputField;
    public StatusText status;

    public bool PauseInputSending;

    private NetworkDriver driver;
    private NetworkConnection connection;
    private bool isConnected;

    private Vector2 movementInput;
    private float sendTimer;
    private uint inputSequence;

    public void Connect()
    {
        if (driver.IsCreated)
        {
            Debug.LogWarning("Client is already running.");
            return;
        }

        driver = NetworkDriver.Create(new WebSocketNetworkInterface());

        NetworkEndpoint endpoint = NetworkEndpoint.Parse(AddressInputField.text, Port);

        connection = driver.Connect(endpoint);

        status.SetText($"Connecting to {AddressInputField.text}:{Port}...");
    }

    private void Update()
    {
        if (!driver.IsCreated) return;

        driver.ScheduleUpdate().Complete();

        if (!connection.IsCreated) return;

        ProcessEvents();

        if (!isConnected) return;

        ReadMovementInput();

        // Toggle input sending pause with P so it does not conflict with movement controls.
        if (Keyboard.current != null && Keyboard.current.pKey.wasPressedThisFrame)
        {
            PauseInputSending = !PauseInputSending;
            Debug.Log($"Input sending paused: {PauseInputSending}");
        }

        SendInputAtFixedRate();
    }

    private void ProcessEvents()
    {
        while (true)
        {
            var eventType = driver.PopEventForConnection(connection, out var reader);

            if (eventType == NetworkEvent.Type.Empty) break;

            switch (eventType)
            {
                case NetworkEvent.Type.Connect:
                    OnConnected();
                    break;

                case NetworkEvent.Type.Data:
                    OnMessageReceived(reader);
                    break;

                case NetworkEvent.Type.Disconnect:
                    var reason = reader.ReadByte();

                    Debug.LogError($"Disconnected from host: {reason}");
                    OnDisconnected();
                    break;
                    
            }
        }
    }

    private void OnConnected()
    {
        isConnected = true;

        Debug.Log("Connected to host.");
        status.SetText("Connected");
    }

    private void SendHello()
    {
        if (!isConnected) return;

        int result = driver.BeginSend(
            connection,
            out DataStreamWriter writer
        );

        if (result != 0)
        {
            Debug.LogError($"Could not begin sending: {result}");
            return;
        }

        var message = new FixedString128Bytes("Hello from controller");

        writer.WriteFixedString128(message);

        int sendResult = driver.EndSend(writer);

        if (sendResult != 0)
        {
            Debug.LogError($"Could not send message: {sendResult}");
            return;
        }

        Debug.Log("Sent hello message.");
    }

    private void ReadMovementInput()
    {
        if (Keyboard.current == null) return;

        float x = 0f;
        float y = 0f;

        if (Keyboard.current.aKey.isPressed)
            x -= 1f;

        if (Keyboard.current.dKey.isPressed)
            x += 1f;

        if (Keyboard.current.sKey.isPressed)
            y -= 1f;

        if (Keyboard.current.wKey.isPressed)
            y += 1f;

        movementInput = Vector2.ClampMagnitude(
            new Vector2(x, y),
            1f
        );
    }

    private void SendInputAtFixedRate()
    {
        if (!isConnected || PauseInputSending || SendRate <= 0f)
        {
            sendTimer = 0f;
            return;
        }

        sendTimer += Time.unscaledDeltaTime;

        float sendInterval = 1f / SendRate;

        if (sendTimer < sendInterval) return;

        // Send at most once per frame; avoid a burst after a slow frame.
        sendTimer %= sendInterval;
        SendInput();
    }

    /*
     * 1 byte message type
     * 4 bytes sequence number
     * 4 bytes horizontal input
     * 4 bytes vertical input
     */
    private void SendInput()
    {
        int result = driver.BeginSend(
            connection,
            out DataStreamWriter writer
        );

        if (result != 0)
        {
            Debug.LogError($"Could not begin sending: {result}");
            return;
        }

        writer.WriteByte((byte)NetworkMessageType.Input);
        writer.WriteUInt(inputSequence);
        writer.WriteFloat(movementInput.x);
        writer.WriteFloat(movementInput.y);

        int sendResult = driver.EndSend(writer);

        if (sendResult < 0)
        {
            Debug.LogError($"Could not send input: {sendResult}");
            return;
        }

        inputSequence++;
    }

    private void OnMessageReceived(DataStreamReader reader)
    {
        FixedString128Bytes message = reader.ReadFixedString128();

        Debug.Log($"Host says: {message}");
        status.SetText($"Host says: {message}");
    }

    private void OnDisconnected()
    {
        isConnected = false;
        connection = default;

        Debug.Log("Disconnected from host.");
        status.SetText("Disconnected");
    }

    private void OnDestroy()
    {
        if (driver.IsCreated) driver.Dispose();
    }

}
