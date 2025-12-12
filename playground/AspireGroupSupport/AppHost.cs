using Aspire.Hosting.Groups;

var builder = DistributedApplication.CreateBuilder(args);


var group = builder.AddGroup("my-group");

// Just to demonstrate parent relationship:
var step = builder.AddExecutable("setup", "powershell.exe", builder.AppHostDirectory)
        .WithArgs(
            "-NoLogo",
            "-NoProfile",
            "-NonInteractive",
            "-ExecutionPolicy", "Bypass",
            "-Command",
            "Write-Host 'Setup...'; Start-Sleep -Seconds 3; Write-Host 'Done.'")
    .WithParentRelationship(group);





builder.Build().Run();
