﻿// See https://aka.ms/new-console-template for more information

using System.Collections;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Management.Automation;
using System.Management.Automation.Runspaces;

// Get all subdirectories that have an existing 'update.ps1' file
string repoPath = Environment.GetEnvironmentVariable("PACKAGES_REPO") ?? @"c:\dev\git\au-packages";

int count = 0;

var directories = Directory.GetDirectories(repoPath, "*", SearchOption.TopDirectoryOnly)
    .Where(d => File.Exists(Path.Combine(d, "update.ps1")));

foreach (var directory in directories)
{
#if DEBUG
    if (!directory.EndsWith("resharper-clt.portable"))
    {
        continue;
    }
#endif

    Console.WriteLine($"::group::{directory}");

    try {
        Console.WriteLine(directory);
        Console.WriteLine("=====================================");

        var iss = InitialSessionState.CreateDefault2();
        iss.ExecutionPolicy = Microsoft.PowerShell.ExecutionPolicy.RemoteSigned;
        
        var ps = PowerShell.Create(iss);

        ps.Streams.Debug.DataAdded += (_, args) =>
        {
            Console.WriteLine("Debug: " + args);
        };
        ps.Streams.Error.DataAdded += (_, args) =>
        {
            // ::error file={name},line={line},endLine={endLine},title={title}::{message}
            Console.WriteLine($"::error file={directory}::{ps.Streams.Error[args.Index]}");
        };
        ps.Streams.Warning.DataAdded += (_, args) =>
        {
            Console.WriteLine("Warning: " + ps.Streams.Warning[args.Index]);
        };
        ps.Streams.Information.DataAdded += (_, args) =>
        {
            Console.WriteLine(ps.Streams.Information[args.Index]);
        };
        ps.Streams.Verbose.DataAdded += (_, args) =>
        {
            Console.WriteLine("Verbose: " + ps.Streams.Verbose[args.Index]);
        };

        // Skip this directory if any existing .nupkg files
        if (Directory.EnumerateFiles(directory, "*.nupkg", SearchOption.TopDirectoryOnly).Any())
        {
#if RELEASE
            Console.WriteLine("\tSkipping directory with existing .nupkg file");
            continue;
#endif
        }

        // Set the working directory
        ps.AddCommand("Set-Location").AddParameter("Path", directory).Invoke();

        PSObject? auPackage = null;

        try
        { 
            var output = ps.AddScript(File.ReadAllText(Path.Combine(directory, "update.ps1")))
            .Invoke();

            if (output.Count > 0)
            {
                if (output.Count > 1)
                {
                    Console.WriteLine("Multiple objects returned");
                }

                auPackage = output[0];
                auPackage.Properties.ToList().ForEach(p => Console.WriteLine($"{p.Name}: {p.Value}"));

                // Streams

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
        } catch (Exception ex)
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
            Console.WriteLine($"choco push {nupkgFile}");

            var result = RunProcess(directory, "choco.exe", $"search {directory}", false, TimeSpan.FromMinutes(1));

            if (result)
            {
                RunProcess(directory, "git.exe", "add *", true, TimeSpan.FromSeconds(30));

                string tagName = $"{auPackage.Properties["Name"].Value}-{auPackage.Properties["NuspecVersion"].Value}";

                string tagArguments = $"tag -a {tagName} -m '{tagName}'";

                Console.WriteLine($"git {tagArguments}");

                RunProcess(directory, "git.exe", tagArguments, true, TimeSpan.FromSeconds(10));

                count++;
            }
        }
        else
        {
            Console.WriteLine("No nupkg file found");
        }
        //break;
    } finally {
        Console.WriteLine("::endgroup::");
    }
}

if (count > 0)
{
    //     $message = "AU: $($packages.Length) updated - $($packages | ForEach-Object Name)"
    // $gist_url = $Info.plugin_results.Gist -split '\n' | Select-Object -Last 1
    // $snippet_url = $Info.plugin_results.Snippet -split '\n' | Select-Object -Last 1
    // git commit -m "$message`n[skip ci] $gist_url $snippet_url" --allow-empty
    // git push

    string commitArguments = $"commit -m 'AU: {count} updated\n[skip ci]'";
    Console.WriteLine($"git {commitArguments}");
    RunProcess(repoPath, "git", commitArguments, true, TimeSpan.FromMinutes(1));
}

return;

bool RunProcess(string workingDirectory, string executable, string arguments, bool errorsAsWarnings, TimeSpan timeout)
{
    // if that succeeds, then git commit
    // run git.exe
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
    Console.WriteLine(output);
    // ::warning file={name},line={line},endLine={endLine},title={title}::{message}
    Console.WriteLine($"::{(errorsAsWarnings ? "warning" : "error")} file={workingDirectory}::{eOut}");

    return p.ExitCode == 0;
}
