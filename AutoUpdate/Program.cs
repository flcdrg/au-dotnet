using Actions.Core.Extensions;
using AutoUpdate;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
builder.Services.AddHostedService<Worker>();

builder.Services.AddGitHubActionsCore();

IHost host = builder.Build();
host.Run();
