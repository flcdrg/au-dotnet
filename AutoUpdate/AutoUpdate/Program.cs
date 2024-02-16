// See https://aka.ms/new-console-template for more information
using System.Management.Automation;

Console.WriteLine("Hello, World!");

// Get all subdirectories that have an existing 'update.ps1' file
const string repoPath = "c:\\dev\\git\\au-packages";

var directories = Directory.GetDirectories(repoPath, "*", SearchOption.TopDirectoryOnly)
    .Where(d => File.Exists(Path.Combine(d, "update.ps1")));

foreach (var directory in directories)
{
    Console.WriteLine(directory);

    var ps = PowerShell.Create();

    // Set the working directory
    ps.AddCommand("Set-Location").AddParameter("Path", directory).Invoke();

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
        Console.WriteLine("Warning: " + args);
    };
    ps.Streams.Information.DataAdded += (sender, args) =>
    {
        Console.WriteLine("Information: " + args);
    };
    ps.Streams.Verbose.DataAdded += (sender, args) =>
    {
        Console.WriteLine("Verbose: " + args);
    };

    var result = ps.AddScript(File.ReadAllText(Path.Combine(directory, "update.ps1")))
        .Invoke();

    
    break;
}
