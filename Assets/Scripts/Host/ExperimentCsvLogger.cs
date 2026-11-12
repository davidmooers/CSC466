using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// Writes one CSV file per host session for fixed and adaptive timeout experiments.
/// </summary>
public sealed class ExperimentCsvLogger : IDisposable
{
    private readonly StreamWriter writer;
    private readonly InputTimeoutMode mode;

    public string FilePath { get; }

    public ExperimentCsvLogger(float inputTimeoutSeconds, InputTimeoutMode mode)
    {
        this.mode = mode;
        string directory = Path.Combine(Application.persistentDataPath, "ExperimentLogs");
        Directory.CreateDirectory(directory);

        string fileName = $"input-timeout-{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}.csv";
        FilePath = Path.Combine(directory, fileName);
        writer = new StreamWriter(FilePath, false, new UTF8Encoding(false));

        writer.WriteLine("utc_timestamp,elapsed_seconds,event_type,sequence,arrival_interval_ms,input_age_ms,timeout_seconds,input_x,input_y,timeout_mode");
        LogEvent("SessionStarted", 0f, 0, float.NaN, float.NaN, inputTimeoutSeconds, Vector2.zero);
    }

    public void LogInputArrival(uint sequence, float elapsedSeconds, float arrivalIntervalMs, float timeoutSeconds, Vector2 input)
    {
        LogEvent("InputArrival", elapsedSeconds, sequence, arrivalIntervalMs, 0f, timeoutSeconds, input);
    }

    public void LogTimeoutStarted(uint sequence, float elapsedSeconds, float inputAgeMs, float timeoutSeconds, Vector2 input)
    {
        LogEvent("TimeoutStarted", elapsedSeconds, sequence, float.NaN, inputAgeMs, timeoutSeconds, input);
    }

    public void LogTimeoutEnded(uint sequence, float elapsedSeconds, float timeoutSeconds, Vector2 input)
    {
        LogEvent("TimeoutEnded", elapsedSeconds, sequence, float.NaN, 0f, timeoutSeconds, input);
    }

    public void Dispose()
    {
        writer?.Dispose();
    }

    private void LogEvent(string eventType, float elapsedSeconds, uint sequence, float arrivalIntervalMs, float inputAgeMs, float timeoutSeconds, Vector2 input)
    {
        string row = string.Join(",",
            DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
            FormatNumber(elapsedSeconds),
            eventType,
            sequence.ToString(CultureInfo.InvariantCulture),
            FormatNumber(arrivalIntervalMs),
            FormatNumber(inputAgeMs),
            FormatNumber(timeoutSeconds),
            FormatNumber(input.x),
            FormatNumber(input.y),
            mode.ToString());

        writer.WriteLine(row);
        writer.Flush();
    }

    private static string FormatNumber(float value)
    {
        return float.IsNaN(value)
            ? string.Empty
            : value.ToString("F4", CultureInfo.InvariantCulture);
    }
}
