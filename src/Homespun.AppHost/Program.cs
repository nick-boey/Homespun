var builder = DistributedApplication.CreateBuilder(args);

var server = builder.AddProject<Projects.Homespun_Server>("server");

var worker = builder.AddNodeApp("worker", "../Homespun.Worker", "src/index.ts")
    .WithNpm(install: false)
    .WithRunScript("dev")
    .WithHttpEndpoint(port: 8080, env: "PORT");

var web = builder.AddViteApp("web", "../Homespun.Web")
    .WithNpm(install: false)
    .WithHttpEndpoint(port: 5173);

builder.Build().Run();
