using System.Diagnostics;
using AiRepoKit.Cli.Models;

namespace AiRepoKit.Cli.Services;

public sealed class ProgressReporter : IDisposable
{
    private static readonly string[] Frames = ["-", "\\", "|", "/"];
    private readonly bool enabled;
    private readonly bool spinnerEnabled;
    private readonly bool verbose;
    private readonly object gate = new();
    private readonly Stopwatch totalStopwatch = Stopwatch.StartNew();
    private readonly Stopwatch phaseStopwatch = new();
    private readonly List<CommandPhaseTiming> phaseTimings = [];
    private Timer? timer;
    private string currentMessage = string.Empty;
    private int frameIndex;
    private bool lineActive;
    private bool disposed;

    private ProgressReporter(bool enabled_, bool spinnerEnabled_, bool verbose_)
    {
        this.enabled = enabled_;
        this.spinnerEnabled = spinnerEnabled_;
        this.verbose = verbose_;
    }

    public static ProgressReporter Create(BootstrapOptions options_)
    {
        bool enabled = !options_.NoProgress && !options_.AuditJson;
        bool interactive = enabled && !Console.IsErrorRedirected && Environment.UserInteractive;
        return new ProgressReporter(enabled && interactive, interactive, options_.Verbose);
    }

    public void StartPhase(string message_)
    {
        if (!this.enabled)
        {
            this.TrackImplicitPhaseTransition(message_);
            return;
        }

        lock (this.gate)
        {
            this.RecordCurrentPhase("Completed");
            this.StopTimer();
            this.currentMessage = message_;
            this.frameIndex = 0;
            this.phaseStopwatch.Restart();
            if (this.spinnerEnabled)
            {
                if (!this.TryWriteSpinner())
                {
                    this.WritePlain(message_);
                    return;
                }

                this.timer = new Timer(_ => this.Tick(), null, TimeSpan.FromMilliseconds(120), TimeSpan.FromMilliseconds(120));
            }
            else
            {
                this.WritePlain(message_);
            }
        }
    }

    public void CompletePhase(string message_)
    {
        this.EndPhase("OK", message_);
    }

    public void WarnPhase(string message_)
    {
        this.EndPhase("WARN", message_);
    }

    public void FailPhase(string message_)
    {
        this.EndPhase("FAIL", message_);
    }

    public void Detail(string message_)
    {
        if (!this.enabled || !this.verbose)
        {
            return;
        }

        lock (this.gate)
        {
            this.ClearActiveLine();
            Console.Error.WriteLine($"   {message_}");
            if (this.spinnerEnabled && !string.IsNullOrWhiteSpace(this.currentMessage))
            {
                this.TryWriteSpinner();
            }
        }
    }

    public void Dispose()
    {
        if (this.disposed)
        {
            return;
        }

        lock (this.gate)
        {
            this.RecordCurrentPhase("Completed");
            this.StopTimer();
            this.ClearActiveLine();
            this.disposed = true;
        }
    }

    public CommandTimingReport GetTimingReport()
    {
        lock (this.gate)
        {
            IReadOnlyList<CommandPhaseTiming> phases = this.phaseTimings.ToArray();
            return new CommandTimingReport(this.totalStopwatch.ElapsedMilliseconds, phases);
        }
    }

    private void Tick()
    {
        if (!this.enabled || !this.spinnerEnabled)
        {
            return;
        }

        lock (this.gate)
        {
            if (!this.disposed && !string.IsNullOrWhiteSpace(this.currentMessage))
            {
                this.TryWriteSpinner();
            }
        }
    }

    private bool TryWriteSpinner()
    {
        try
        {
            string frame = Frames[this.frameIndex++ % Frames.Length];
            Console.Error.Write($"\r{frame} {this.currentMessage}");
            this.lineActive = true;
            return true;
        }
        catch
        {
            this.StopTimer();
            this.lineActive = false;
            return false;
        }
    }

    private void EndPhase(string status_, string message_)
    {
        if (!this.enabled)
        {
            this.TrackImplicitPhaseTransition(string.Empty, status_);
            return;
        }

        lock (this.gate)
        {
            this.RecordCurrentPhase(status_);
            this.StopTimer();
            this.ClearActiveLine();
            Console.Error.WriteLine($"[{status_}] {message_}");
            this.currentMessage = string.Empty;
        }
    }

    private void WritePlain(string message_)
    {
        Console.Error.WriteLine($"... {message_}");
        this.lineActive = false;
    }

    private void ClearActiveLine()
    {
        if (!this.lineActive)
        {
            return;
        }

        try
        {
            int width = Math.Clamp(Console.BufferWidth - 1, 20, 240);
            Console.Error.Write("\r" + new string(' ', width) + "\r");
        }
        catch
        {
            Console.Error.WriteLine();
        }

        this.lineActive = false;
    }

    private void StopTimer()
    {
        this.timer?.Dispose();
        this.timer = null;
    }

    private void TrackImplicitPhaseTransition(string nextMessage_, string currentStatus_ = "Completed")
    {
        lock (this.gate)
        {
            this.RecordCurrentPhase(currentStatus_);
            if (!string.IsNullOrWhiteSpace(nextMessage_))
            {
                this.currentMessage = nextMessage_;
                this.phaseStopwatch.Restart();
            }
            else
            {
                this.currentMessage = string.Empty;
            }
        }
    }

    private void RecordCurrentPhase(string status_)
    {
        if (string.IsNullOrWhiteSpace(this.currentMessage))
        {
            return;
        }

        if (!this.phaseStopwatch.IsRunning)
        {
            return;
        }

        this.phaseStopwatch.Stop();
        this.phaseTimings.Add(new CommandPhaseTiming(this.currentMessage, status_, this.phaseStopwatch.ElapsedMilliseconds));
    }
}
