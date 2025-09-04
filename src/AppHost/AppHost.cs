var builder = DistributedApplication.CreateBuilder(args);

var chatModelId = builder.AddConnectionString("chatModelId");
var endpoint = builder.AddConnectionString("endpoint");
var apiKey = builder.AddConnectionString("apiKey");

var product = builder.AddProject<Projects.ProductCatalogService>("product")
    .WithEnvironment("AzureAd__Instance", builder.Configuration["AzureAd:Instance"])
    .WithEnvironment("AzureAd__TenantId", builder.Configuration["AzureAd:TenantId"])
    .WithEnvironment("AzureAd__ClientId", builder.Configuration["AzureAd:ProductClientId"]);

var barista = builder.AddProject<Projects.BaristaService>("barista")
    .WithEnvironment("AzureAd__Instance", builder.Configuration["AzureAd:Instance"])
    .WithEnvironment("AzureAd__TenantId", builder.Configuration["AzureAd:TenantId"])
    .WithEnvironment("AzureAd__ClientId", builder.Configuration["AzureAd:BaristaClientId"]);

var kitchen = builder.AddProject<Projects.KitchenService>("kitchen")
    .WithEnvironment("AzureAd__Instance", builder.Configuration["AzureAd:Instance"])
    .WithEnvironment("AzureAd__TenantId", builder.Configuration["AzureAd:TenantId"])
    .WithEnvironment("AzureAd__ClientId", builder.Configuration["AzureAd:KitchenClientId"]);

var counter = builder.AddProject<Projects.CounterService>("counter")
    .WithEnvironment("AzureAd__Instance", builder.Configuration["AzureAd:Instance"])
    .WithEnvironment("AzureAd__TenantId", builder.Configuration["AzureAd:TenantId"])
    .WithEnvironment("AzureAd__ClientId", builder.Configuration["AzureAd:CounterClientId"])
    .WithEnvironment("AzureAd__ClientSecret", builder.Configuration["AzureAd:CounterClientSecret"])
    .WithEnvironment("AzureAd__ProductClientId", builder.Configuration["AzureAd:ProductClientId"])
    .WithEnvironment("AzureAd__BaristaClientId", builder.Configuration["AzureAd:BaristaClientId"])
    .WithEnvironment("AzureAd__KitchenClientId", builder.Configuration["AzureAd:KitchenClientId"])
    .WithReference(product).WaitFor(product)
    .WithReference(barista).WaitFor(barista)
    .WithReference(kitchen).WaitFor(kitchen);
counter.WithReference(chatModelId);
counter.WithReference(endpoint);
counter.WithReference(apiKey);

builder.Build().Run();
