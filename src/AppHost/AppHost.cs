var builder = DistributedApplication.CreateBuilder(args);

var chatModelId = builder.AddConnectionString("chatModelId");
var endpoint = builder.AddConnectionString("endpoint");
var apiKey = builder.AddConnectionString("apiKey");

builder.AddProject<Projects.ProductCatalogService>("product-catalog");

var counter = builder.AddProject<Projects.CounterService>("counter");
counter.WithReference(chatModelId);
counter.WithReference(endpoint);
counter.WithReference(apiKey);

builder.AddProject<Projects.BaristaService>("barista");

builder.AddProject<Projects.KitchenService>("kitchen");

builder.Build().Run();
