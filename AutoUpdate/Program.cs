// See https://aka.ms/new-console-template for more information
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
            Console.WriteLine("Information: " + ps.Streams.Information[args.Index]);
        };
        ps.Streams.Verbose.DataAdded += (_, args) =>
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

        try { 
            var output = ps.AddScript(File.ReadAllText(Path.Combine(directory, "update.ps1")))
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

            var result = RunProcess(directory, "choco.exe", $"search {directory}", false, TimeSpan.FromMinutes(1));

            if (result)
            {
                RunProcess(directory, "git.exe", "add *", true, TimeSpan.FromSeconds(30));
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
