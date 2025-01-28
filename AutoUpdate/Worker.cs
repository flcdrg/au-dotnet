using System.Collections;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using Actions.Core.Services;
using Actions.Core.Summaries;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace AutoUpdate;

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
            if (!directory.EndsWith("azure-functions-core-tools"))
            {
                continue;
            }
#endif

            core.StartGroup(directory);

            try
            {
                var ps = CreatePowerShell();

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

                    var output = ps.AddScript(await File.ReadAllTextAsync(Path.Combine(directory, "update.ps1"), cancellationToken))
                        .Invoke();

                    if (cancellationToken.IsCancellationRequested)
                    {
                        core.WriteWarning("Cancellation requested");
                        break;
                    }   

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

                        Console.WriteLine("Package properties");
                        Console.WriteLine("------------------");

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
                            Console.WriteLine("Stream properties");
                            Console.WriteLine("-----------------");
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
                    core.WriteWarning("PowerShell reported some errors");
                    continue;
                }

                // Check if .nupkg file(s) exists
                var nupkgFiles = Directory.GetFiles(directory, "*.nupkg", SearchOption.TopDirectoryOnly).ToList();

                if (nupkgFiles.Any() && auPackage != null)
                {
                    foreach (var nupkgFile in nupkgFiles) {
                        core.WriteInfo($"Found nupkg file: {nupkgFile}");

                        // try pushing to chocolatey
                        bool result = true;
                        if (_chocolateyApiKey != null || !nupkgFile.Contains("azure-functions-core-tools")) // azure-functions-core-tools is not our package, but we want to submit to VirusTotal
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

                            string tagArguments = $"tag -f -a {tagName} -m '{tagName}'";

                            core.WriteDebug($"git {tagArguments}");

                            RunProcess(directory, "git.exe", tagArguments, true, TimeSpan.FromSeconds(10));

                            count++;

                            summaryRows.Add(new SummaryTableRow([new(name), new(tagName)]));
                        }
                    }
                    // submit to VirusTotal
                    if (auPackage.Properties["Files"]?.Value is string[] files)
                    {
                        foreach (var file in files.Distinct())
                        {
                            if (!File.Exists(file))
                            {
                                continue;
                            }

                            var f = new FileInfo(file);
                            // get file size
                            Console.WriteLine($"\t{file}, Size: {f.Length:N0} bytes");

                            var key = Environment.GetEnvironmentVariable("VT_APIKEY");

                            // if file size is greater than 650M, skip as VirusTotal has a limit of 650M
                            if (f.Length > 650 * 1024 * 1024)
                            {
                                core.WriteWarning("File size is greater than 650M, skipping VirusTotal scan");
                                continue;
                            }

                            if (key == null)
                            {
                                core.WriteWarning("No VirusTotal API key found");
                                continue;
                            }

                            core.WriteInfo("Submitting file to VirusTotal");
                            // vt.exe scan file $File --apikey $env:VT_APIKEY
                            var vtResult = RunProcess(directory, "vt.exe", $"scan file {file} --apikey {key}", true, TimeSpan.FromMinutes(7));

                            if (!vtResult)
                            {
                                core.WriteWarning("VirusTotal scan failed");
                            }
                        }
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

        if (cancellationToken.IsCancellationRequested)
        {
            core.WriteWarning("Cancellation requested");
            return;
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

        if (Summary.IsAvailable)
        {
            await core.Summary.WriteAsync(new SummaryWriteOptions { Overwrite = true });
        }

        lifetime.StopApplication();
    }

    private PowerShell CreatePowerShell()
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
        return ps;
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
        if (!string.IsNullOrEmpty(output))
        {
            core.WriteInfo(output);
        }

        if (errorsAsWarnings)
        {
            if (!string.IsNullOrEmpty(eOut))
            {
                core.WriteWarning(eOut);
            }
        } else
        {
            if (!string.IsNullOrEmpty(eOut))
            {
                core.WriteError(eOut);
            }
        }

        return p.ExitCode == 0;
    }
}