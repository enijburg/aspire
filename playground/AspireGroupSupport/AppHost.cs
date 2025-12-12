using Aspire.Hosting.Groups;

var builder = DistributedApplication.CreateBuilder(args);

// make sure the group status reflects the status of its children
builder.Services.AddAggregateParentStatusFromChildren();

var group = builder.AddGroup("my-group");

// Just to demonstrate parent relationship:
var step1 = builder.AddExecutable("step1", "powershell.exe", builder.AppHostDirectory)
        .WithArgs(
            "-NoLogo",
            "-NoProfile",
            "-NonInteractive",
            "-ExecutionPolicy", "Bypass",
            "-Command",
            "Write-Host 'Setup...'; Start-Sleep -Seconds 3; Write-Host 'Done.'")
    .WithParentRelationship(group);

var step2 = builder.AddExecutable("step2", "powershell.exe", builder.AppHostDirectory)
    .WithArgs(
        "-NoLogo",
        "-NoProfile",
        "-NonInteractive",
        "-ExecutionPolicy", "Bypass",
        "-Command",
        "Write-Host 'Setup...'; Start-Sleep -Seconds 3; Write-Host 'Done.'")
    .WaitForCompletion(step1)
    .WithParentRelationship(group);

builder.Build().Run();
