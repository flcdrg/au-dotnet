// See https://aka.ms/new-console-template for more information
using System.Diagnostics;
using System.Management.Automation;
using System.Management.Automation.Runspaces;

// Get all subdirectories that have an existing 'update.ps1' file
const string repoPath = "c:\\dev\\git\\au-packages";

var directories = Directory.GetDirectories(repoPath, "*", SearchOption.TopDirectoryOnly)
    .Where(d => File.Exists(Path.Combine(d, "update.ps1")));

foreach (var directory in directories)
{
    if (!directory.EndsWith("tflint"))
    {
        continue;
    }

    Console.WriteLine(directory);

    var iss = InitialSessionState.CreateDefault2();
    iss.ExecutionPolicy = Microsoft.PowerShell.ExecutionPolicy.RemoteSigned;
    
    var ps = PowerShell.Create(iss);

    ps.Streams.Debug.DataAdded += (sender, args) =>
    {
        Console.WriteLine("Debug: " + args);
    };
    ps.Streams.Error.DataAdded += (sender, args) =>
    {
        Console.WriteLine("Error: " + ps.Streams.Error[args.Index]);
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

    // Delete any existing .nupkg files
    var nupkgFiles = Directory.GetFiles(directory, "*.nupkg", SearchOption.TopDirectoryOnly);
    foreach (var file in nupkgFiles)
    {
        File.Delete(file);
    }

    // Set the working directory
    ps.AddCommand("Set-Location").AddParameter("Path", directory).Invoke();
    var result3 = ps.AddScript("Get-Location").Invoke();

    ps.AddScript("Import-Module C:\\dev\\git\\chocolatey-au\\AU");

    var result1 = ps.Invoke();

    var result = ps.AddScript("Get-Module").Invoke();

    var result4 = ps.AddScript(File.ReadAllText(Path.Combine(directory, "update.ps1")))
        .Invoke();

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
    break;
}
