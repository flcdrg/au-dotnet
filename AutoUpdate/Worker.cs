using System.Collections.Specialized;
using System.Collections;
using System.Diagnostics;
using System.Management.Automation.Runspaces;
using System.Management.Automation;
using Actions.Core.Services;
using Actions.Core.Summaries;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

internal class Worker(ICoreService core, IConfiguration configuration, IHostApplicationLifetime lifetime) : IHostedService
{
    private readonly string? _chocolateyApiKey = Environment.GetEnvironmentVariable("api_key");
    private readonly string _repoPath = configuration["PACKAGES_REPO"] ?? @"c:\dev\git\au-packages";

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // Get all subdirectories that have an existing 'update.ps1' file
        int count = 0;

        var directories = Directory.GetDirectories(_repoPath, "*", SearchOption.TopDirectoryOnly)
            .Where(d => File.Exists(Path.Combine(d, "update.ps1")));

        var summaryRows = new List<SummaryTableRow>();
        foreach (var directory in directories)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                core.WriteWarning("Cancellation requested");
                break;
            }

#if DEBUG
            if (!directory.EndsWith("iguana"))
            {
                continue;
            }
#endif

            core.StartGroup(directory);

            try
            {
                var iss = InitialSessionState.CreateDefault2();
                iss.ExecutionPolicy = Microsoft.PowerShell.ExecutionPolicy.RemoteSigned;

                var ps = PowerShell.Create(iss);

                ps.Streams.Debug.DataAdded += (_, args) =>
                {
                    core.WriteDebug(ps.Streams.Debug[args.Index].ToString());
                };
                ps.Streams.Error.DataAdded += (_, args) =>
                {
                    core.WriteError(ps.Streams.Error[args.Index].ToString());
                };
                ps.Streams.Warning.DataAdded += (_, args) =>
                {
                    core.WriteWarning(ps.Streams.Warning[args.Index].ToString());
                };
                ps.Streams.Information.DataAdded += (_, args) =>
                {
                    core.WriteInfo(ps.Streams.Information[args.Index].ToString());
                };
                ps.Streams.Verbose.DataAdded += (_, args) =>
                {
                    core.WriteDebug(ps.Streams.Verbose[args.Index].ToString());
                };

                // Skip this directory if any existing .nupkg files
                if (Directory.EnumerateFiles(directory, "*.nupkg", SearchOption.TopDirectoryOnly).Any())
                {
#if RELEASE
                    core.WriteInfo("Skipping directory with existing .nupkg file");
                    continue;
#endif
                }

                // Set the working directory
                ps.AddCommand("Set-Location").AddParameter("Path", directory).Invoke();

                PSObject? auPackage = null;

                try
                {
                    // Get detailed error messages
                    ps.AddScript("$ErrorView = 'DetailedView'").Invoke();

                    var output = ps.AddScript(File.ReadAllText(Path.Combine(directory, "update.ps1")))
                    .Invoke();

                    // Expect an AUPackage object to be returned
                    if (output.Count > 0)
                    {
                        if (output.Count > 1)
                        {
                            core.WriteInfo("Multiple objects returned");
                        }

                        auPackage = output[0];

                        // Handle Ignore
                        var text = auPackage.BaseObject as string;
                        if (text != null && text.Equals("Ignore", StringComparison.InvariantCultureIgnoreCase))
                        {
                            continue;
                        }

                        auPackage.Properties.ToList().ForEach(p => {
                            if (p.Value == null)
                            {
                                return;
                            }

                            // when p.Value is string array, print out array, otherwise print out value
                            if (p.Value is string[] array)
                            {
                                Console.WriteLine($"\t{p.Name}: {string.Join("\n\t\t", array)}");
                            }
                            else
                            {
                                Console.WriteLine($"\t{p.Name}: {p.Value}");

                            }

                        });

                        // Streams logging
                        if (auPackage.Properties["Streams"].Value is OrderedDictionary streams)
                        {
                            foreach (DictionaryEntry stream in streams)
                            {
                                Console.WriteLine($"{stream.Key}: {stream.Value}");
                                if (stream.Value is Hashtable streamObj)
                                {
                                    streamObj.Keys.Cast<string>().ToList().ForEach(k => Console.WriteLine($"\t{k}: {streamObj[k]}"));
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.Message);
                    continue;
                }

                if (ps.HadErrors)
                {
                    continue;
                }

                // Check if .nupkg file exists
                var nupkgFile = Directory.GetFiles(directory, "*.nupkg", SearchOption.TopDirectoryOnly).FirstOrDefault();

                if (nupkgFile != null && auPackage != null)
                {
                    // try pushing to chocolatey
                    bool result = true;
                    if (_chocolateyApiKey != null)
                    {
                        string chocoArguments = $"push {nupkgFile} --api-key {_chocolateyApiKey} --source https://push.chocolatey.org/ --verbose";
                        core.WriteDebug($"choco {chocoArguments}");
                        result = RunProcess(directory, "choco.exe", chocoArguments, false, TimeSpan.FromMinutes(3));
                    }
                    else
                    {
                        core.WriteDebug($"[whatif] choco push {nupkgFile}");
                    }

                    if (result)
                    {
                        RunProcess(directory, "git.exe", "add *", true, TimeSpan.FromSeconds(30));

                        // RemoteVersion: 2024.1-EAP05
                        // NuspecVersion: 2024.1-EAP02
                        string name = (string) auPackage.Properties["Name"].Value;
                        string tagName = $"{name}-{auPackage.Properties["RemoteVersion"].Value}";

                        string tagArguments = $"tag -a {tagName} -m '{tagName}'";

                        core.WriteDebug($"git {tagArguments}");

                        RunProcess(directory, "git.exe", tagArguments, true, TimeSpan.FromSeconds(10));

                        count++;

                        summaryRows.Add(new SummaryTableRow([new(name), new(tagName)]));
                    }
                }
                else
                {
                    Console.WriteLine("No nupkg file found");
                }
                //break;
            }
            finally
            {
                core.EndGroup();
            }
        }

        if (count > 0)
        {
            string commitArguments = $"commit -m \"AU-dotnet: {count} updated\n[skip ci]\"";
            Console.WriteLine($"git {commitArguments}");
            RunProcess(_repoPath, "git", commitArguments, true, TimeSpan.FromMinutes(1));

            var table = new SummaryTable(new SummaryTableRow([new("Package"), new SummaryTableCell("Version")]), summaryRows.ToArray());

            core.Summary.AddMarkdownHeading("Updated Packages");
            core.Summary.AddRawMarkdown($"{count} packages were updated:", true);
            core.Summary.AddMarkdownTable(table);
        } else
        {
            core.Summary.AddRawMarkdown("No packages were updated");
        }

        await core.Summary.WriteAsync(new SummaryWriteOptions { Overwrite = true });

        lifetime.StopApplication();
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    bool RunProcess(string workingDirectory, string executable, string arguments, bool errorsAsWarnings, TimeSpan timeout)
    {
        var p = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = executable,
                Arguments = arguments,
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };

        string eOut = string.Empty;
        p.ErrorDataReceived += (_, e) =>
        { eOut += e.Data; };

        p.Start();

        // To avoid deadlocks, use an asynchronous read operation on at least one of the streams.  
        p.BeginErrorReadLine();

        var output = p.StandardOutput.ReadToEnd();

        p.WaitForExit(timeout);

        // get output from process
        core.WriteDebug(output);

        if (errorsAsWarnings)
        {
            core.WriteWarning(eOut);
        } else
        {
            core.WriteError(eOut);
        }

        return p.ExitCode == 0;
    }
}