using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Threading;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

// ===============================
// Inspector ReadOnly Attribute
// ===============================
public class ReadOnlyAttribute : PropertyAttribute { }

#if UNITY_EDITOR
[CustomPropertyDrawer(typeof(ReadOnlyAttribute))]
public class ReadOnlyDrawer : PropertyDrawer
{
    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        bool previousGUIState = GUI.enabled;
        GUI.enabled = false;
        EditorGUI.PropertyField(position, property, label, true);
        GUI.enabled = previousGUIState;
    }

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        return EditorGUI.GetPropertyHeight(property, label, true);
    }
}
#endif

public class ControlerManager : MonoBehaviour
{
    [Header("Serial Settings")]
    public string portName = "/dev/cu.usbmodem11401";
    public int baudRate = 115200;

    [Header("Status")]
    [ReadOnly, SerializeField] private bool isConnected = false;
    [ReadOnly, SerializeField] private int receivedCount = 0;
    [ReadOnly, SerializeField] private string lastJsonLine = "";
    [ReadOnly, SerializeField] private string lastError = "";

    [Header("Bend Sensor")]
    [ReadOnly, SerializeField] private int tMs = 0;
    [ReadOnly, SerializeField] private int bend = 20000;

    [Header("MPU6050 Accel")]
    [ReadOnly, SerializeField] private float accelX = 0f;
    [ReadOnly, SerializeField] private float accelY = 0f;
    [ReadOnly, SerializeField] private float accelZ = 0f;

    [Header("MPU6050 Gyro")]
    [ReadOnly, SerializeField] private float gyroX = 0f;
    [ReadOnly, SerializeField] private float gyroY = 0f;
    [ReadOnly, SerializeField] private float gyroZ = 0f;

    // ImuOrientation 等から読み取る公開API（PicoMpuClient互換）
    public Vector3 Accel => new Vector3(accelX, accelY, accelZ); // g
    public Vector3 Gyro  => new Vector3(gyroX,  gyroY,  gyroZ);  // °/s
    public int Bend => bend;                                     // 曲げセンサ raw値

    private SerialPort serialPort;
    private Thread readThread;
    private bool isRunning = false;

    private readonly object queueLock = new object();
    private readonly Queue<string> lineQueue = new Queue<string>();

    [Serializable]
    public class SensorPacket
    {
        public int t_ms;
        public int bend;
        public Vector3Data accel;
        public Vector3Data gyro;
        public string error;
    }

    [Serializable]
    public class Vector3Data
    {
        public float x;
        public float y;
        public float z;
    }

    void Start()
    {
        OpenSerial();
    }


    void FixedUpdate()
    {
        while (true)
        {
            string line = null;

            lock (queueLock)
            {
                if (lineQueue.Count > 0)
                {
                    line = lineQueue.Dequeue();
                }
            }

            if (line == null)
                break;

            ParseLine(line);
        }
    }

    private void OpenSerial()
    {
        try
        {
            serialPort = new SerialPort(portName, baudRate);
            serialPort.ReadTimeout = 100;
            serialPort.NewLine = "\n";
            serialPort.DtrEnable = true;
            serialPort.Open();
            // コントローラ側のセンサの初期値はゲーム側で使用するとまずい値が入っていたりするので一旦読み捨てる
            serialPort.ReadLine();
            
            isRunning = true;
            isConnected = true;
            lastError = "";

            readThread = new Thread(ReadSerialLoop);
            readThread.IsBackground = true;
            readThread.Start();

            Debug.Log("Serial opened: " + portName);
        }
        catch (Exception e)
        {
            isConnected = false;
            lastError = e.Message;
            Debug.LogError("Failed to open serial port: " + e.Message);
        }
    }

    private void ReadSerialLoop()
    {
        while (isRunning)
        {
            try
            {
                string line = serialPort.ReadLine();

                if (string.IsNullOrWhiteSpace(line))
                    continue;

                lock (queueLock)
                {
                    lineQueue.Enqueue(line);
                }
            }
            catch (TimeoutException)
            {
                // 無視
            }
            catch (Exception e)
            {
                lock (queueLock)
                {
                    lineQueue.Enqueue("{\"error\":\"" + EscapeJson(e.Message) + "\"}");
                }
            }
        }
    }

    private void ParseLine(string line)
    {
        lastJsonLine = line;

        try
        {
            SensorPacket packet = JsonUtility.FromJson<SensorPacket>(line);

            receivedCount++;

            if (!string.IsNullOrEmpty(packet.error))
            {
                lastError = packet.error;
                return;
            }

            lastError = "";

            tMs = packet.t_ms;
            bend = packet.bend;

            if (packet.accel != null)
            {
                accelX = packet.accel.x;
                accelY = packet.accel.y;
                accelZ = packet.accel.z;
            }

            if (packet.gyro != null)
            {
                gyroX = packet.gyro.x;
                gyroY = packet.gyro.y;
                gyroZ = packet.gyro.z;
            }
        }
        catch (Exception e)
        {
            lastError = "JSON parse error: " + e.Message;
        }
    }

    private static string EscapeJson(string text)
    {
        return text.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }

    void OnDestroy()
    {
        CloseSerial();
    }

    void OnApplicationQuit()
    {
        CloseSerial();
    }

    private void CloseSerial()
    {
        isRunning = false;
        isConnected = false;

        if (readThread != null && readThread.IsAlive)
        {
            readThread.Join(500);
            readThread = null;
        }

        if (serialPort != null)
        {
            try
            {
                if (serialPort.IsOpen)
                    serialPort.Close();
            }
            catch { }

            serialPort = null;
        }
    }
}