﻿// See https://aka.ms/new-console-template for more information
using System.Diagnostics;
using System.Management.Automation;
using System.Management.Automation.Runspaces;

// Get all subdirectories that have an existing 'update.ps1' file
string repoPath = Environment.GetEnvironmentVariable("PACKAGES_REPO") ?? @"c:\dev\git\au-packages";


var directories = Directory.GetDirectories(repoPath, "*", SearchOption.TopDirectoryOnly)
    .Where(d => File.Exists(Path.Combine(d, "update.ps1")));

foreach (var directory in directories)
{
    Console.WriteLine($"::group::{directory}");

    try {
        Console.WriteLine(directory);
        Console.WriteLine("=====================================");

        var iss = InitialSessionState.CreateDefault2();
        iss.ExecutionPolicy = Microsoft.PowerShell.ExecutionPolicy.RemoteSigned;
        
        var ps = PowerShell.Create(iss);

        ps.Streams.Debug.DataAdded += (sender, args) =>
        {
            Console.WriteLine("Debug: " + args);
        };
        ps.Streams.Error.DataAdded += (sender, args) =>
        {
            // ::error file={name},line={line},endLine={endLine},title={title}::{message}
            Console.WriteLine($"::error file={directory}::{ps.Streams.Error[args.Index]}");
        };
        ps.Streams.Warning.DataAdded += (sender, args) =>
        {
            Console.WriteLine("Warning: " + ps.Streams.Warning[args.Index]);
        };
        ps.Streams.Information.DataAdded += (sender, args) =>
        {
            Console.WriteLine("Information: " + ps.Streams.Information[args.Index]);
        };
        ps.Streams.Verbose.DataAdded += (sender, args) =>
        {
            Console.WriteLine("Verbose: " + ps.Streams.Verbose[args.Index]);
        };

        // Skip this directory if any existing .nupkg files
        if (Directory.EnumerateFiles(directory, "*.nupkg", SearchOption.TopDirectoryOnly).Any())
        {
            Console.WriteLine("\tSkipping directory with existing .nupkg file");
            continue;
        }

        // Set the working directory
        ps.AddCommand("Set-Location").AddParameter("Path", directory).Invoke();
        var result3 = ps.AddScript("Get-Location").Invoke();

        var result1 = ps.Invoke();

        try { 
            var result4 = ps.AddScript(File.ReadAllText(Path.Combine(directory, "update.ps1")))
            .Invoke();
        } catch (Exception ex)
        {
            Console.WriteLine(ex.Message);
            continue;
        }

        // Check if .nupkg file exists
        var nupkgFile = Directory.GetFiles(directory, "*.nupkg", SearchOption.TopDirectoryOnly).FirstOrDefault();

        if (nupkgFile != null)
        {
            // try pushing to chocolatey
            Console.WriteLine($"choco push {nupkgFile}");

            // if that succeeds, then git commit
            // run git.exe
            var git = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "git.exe",
                    Arguments = "add *",
                    WorkingDirectory = directory,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };

            string eOut = string.Empty;
            git.ErrorDataReceived += new DataReceivedEventHandler((sender, e) =>
            { eOut += e.Data; });

            git.Start();

            // To avoid deadlocks, use an asynchronous read operation on at least one of the streams.  
            git.BeginErrorReadLine();

            var output = git.StandardOutput.ReadToEnd();

            git.WaitForExit(TimeSpan.FromSeconds(30));

            // get output from git process
            Console.WriteLine(output);
            Console.WriteLine($"\nError stream: {eOut}");
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
