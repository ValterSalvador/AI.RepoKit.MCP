namespace AiRepoKit.CoordinationBroker;

using AiRepoKit.WorkflowExecution;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length < 1)
        {
            Console.Error.WriteLine("Usage: AiRepoKit.CoordinationBroker <storageRoot> [--max-concurrent <N>] OR --worker <storageRoot> <workflowId> OR --probe-identity");
            return 1;
        }

        // Support deterministic OS identity probe for multiprocess security verification
        if (args.Length >= 1 && string.Equals(args[0], "--probe-identity", StringComparison.OrdinalIgnoreCase))
        {
            string actualIdentity = string.Empty;
            string actualName = string.Empty;
            if (OperatingSystem.IsWindows())
            {
                var current = System.Security.Principal.WindowsIdentity.GetCurrent();
                actualIdentity = current.User?.Value ?? string.Empty;
                actualName = current.Name;
            }
            else if (OperatingSystem.IsLinux())
            {
                actualIdentity = WorkflowBrokerIpcTransport.GetCurrentLinuxUserId().ToString(System.Globalization.CultureInfo.InvariantCulture);
                actualName = Environment.UserName;
            }
            Console.WriteLine($"WORKER_IDENTITY:{actualIdentity}:{actualName}");
            Console.Out.Flush();
            return 0;
        }

        // Support worker mode for genuine multiprocess isolation verification
        if (args.Length >= 3 && string.Equals(args[0], "--worker", StringComparison.OrdinalIgnoreCase))
        {
            string workerStorageRoot = args[1];
            string workflowId = args[2];
            string? expectedPrincipal = null;
            bool runOnce = false;

            for (int i = 3; i < args.Length; i++)
            {
                if (string.Equals(args[i], "--once", StringComparison.OrdinalIgnoreCase))
                {
                    runOnce = true;
                }
                else if (string.Equals(args[i], "--expected-principal", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    expectedPrincipal = args[i + 1];
                    i++;
                }
                else if (!args[i].StartsWith("--", StringComparison.Ordinal) && expectedPrincipal is null)
                {
                    expectedPrincipal = args[i];
                }
            }

            if (string.IsNullOrWhiteSpace(expectedPrincipal))
            {
                expectedPrincipal = Environment.GetEnvironmentVariable("AIREPO_BROKER_SERVICE_PRINCIPAL");
            }

            try
            {
                string actualIdentity = string.Empty;
                string actualName = string.Empty;
                if (OperatingSystem.IsWindows())
                {
                    var current = System.Security.Principal.WindowsIdentity.GetCurrent();
                    actualIdentity = current.User?.Value ?? string.Empty;
                    actualName = current.Name;
                }
                else if (OperatingSystem.IsLinux())
                {
                    actualIdentity = WorkflowBrokerIpcTransport.GetCurrentLinuxUserId().ToString(System.Globalization.CultureInfo.InvariantCulture);
                    actualName = Environment.UserName;
                }
                Console.WriteLine($"WORKER_IDENTITY:{actualIdentity}:{actualName}");
                Console.Out.Flush();

                WorkflowSchedulerPaths paths = new(workerStorageRoot);
                await using WorkflowBrokerClient client = await WorkflowBrokerClient.ConnectAsync(
                    paths,
                    expectedServerIdentity_: expectedPrincipal);
                WorkflowResourceClaim[] claims = [new($"fs:/repo/{workflowId}", WorkflowResourceAccessKind.ExclusiveWrite)];
                await using WorkflowBrokerLeaseHandle lease = await client.AcquireLeaseAsync(workflowId, claims, "Normal");

                Console.WriteLine("WORKER_ACQUIRED");
                Console.Out.Flush();

                if (runOnce || args.Any(a => string.Equals(a, "--once", StringComparison.OrdinalIgnoreCase)))
                {
                    return 0;
                }

                using CancellationTokenSource workerCts = new();
                Console.CancelKeyPress += (_, e) => { e.Cancel = true; workerCts.Cancel(); };

                if (Console.IsInputRedirected)
                {
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            while (!workerCts.IsCancellationRequested)
                            {
                                string? line = await Console.In.ReadLineAsync(workerCts.Token).ConfigureAwait(false);
                                if (line is null)
                                {
                                    break;
                                }
                                if (string.Equals(line.Trim(), "STOP", StringComparison.OrdinalIgnoreCase))
                                {
                                    workerCts.Cancel();
                                    break;
                                }
                            }
                        }
                        catch
                        {
                        }
                    });
                }

                await Task.Delay(Timeout.Infinite, workerCts.Token).ConfigureAwait(false);
                return 0;
            }
            catch (OperationCanceledException)
            {
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"WORKER_ERROR: {ex.Message}");
                Console.WriteLine($"WORKER_REJECTED: {ex.Message}");
                Console.Out.Flush();
                return 1;
            }
        }

        string storageRoot = args[0];
        int maxConcurrent = 4;
        string? authorizedWorker = null;

        for (int i = 1; i < args.Length; i++)
        {
            if (string.Equals(args[i], "--max-concurrent", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                if (int.TryParse(args[i + 1], out int parsed))
                {
                    maxConcurrent = parsed;
                }
                i++;
            }
            else if (string.Equals(args[i], "--authorized-worker", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                authorizedWorker = args[i + 1];
                i++;
            }
        }

        using CancellationTokenSource cts = new();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cts.Cancel();
        };

        try
        {
            WorkflowSchedulerPaths paths = new(storageRoot);
            await using WorkflowCoordinationBroker broker = new(
                paths,
                maxConcurrent,
                authorizedWorkerIdentity_: authorizedWorker);

            await broker.StartAsync(cts.Token).ConfigureAwait(false);

            // Signal to parent process / supervisor that broker is active
            Console.WriteLine($"BROKER_READY {broker.ServicePrincipal}");
            Console.Out.Flush();

            if (Console.IsInputRedirected)
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        while (!cts.IsCancellationRequested)
                        {
                            string? line = await Console.In.ReadLineAsync(cts.Token).ConfigureAwait(false);
                            if (line is null)
                            {
                                break;
                            }
                            if (string.Equals(line.Trim(), "STOP", StringComparison.OrdinalIgnoreCase))
                            {
                                cts.Cancel();
                                break;
                            }
                        }
                    }
                    catch
                    {
                    }
                });
            }

            await Task.Delay(Timeout.Infinite, cts.Token).ConfigureAwait(false);
            return 0;
        }
        catch (OperationCanceledException)
        {
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"BROKER_ERROR: {ex.Message}");
            return 1;
        }
    }
}
