using ProductCatalogService.Tools;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddMcpServer()
    .WithHttpTransport()
    .WithTools<McpTools>();

builder.AddServiceDefaults();

var app = builder.Build();

app.MapDefaultEndpoints();

app.MapMcp("/mcp");

app.Run();
