var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHttpContextAccessor();

builder.AddServiceDefaults();

var app = builder.Build();

app.MapDefaultEndpoints();

app.MapGet("/", () => "Hello World!");

app.Run();
