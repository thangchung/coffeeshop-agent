using Microsoft.Extensions.Configuration;

var builder = DistributedApplication.CreateBuilder(args);

var chatModelId = builder.AddConnectionString("chatModelId");
var embeddingModelId = builder.AddConnectionString("embeddingModelId");
var endpoint = builder.AddConnectionString("endpoint");
var apiKey = builder.AddConnectionString("apiKey");

var ignoreAuth = false; // devui: true; ag-ui: false

//var cache = builder.AddRedis("cache")
//                .WithLifetime(ContainerLifetime.Persistent)
//                .WithRedisInsight();

var product = builder.AddProject<Projects.ProductCatalogService>("product")
    .WithEnvironment("AzureAd__Instance", builder.Configuration["AzureAd:Instance"])
    .WithEnvironment("AzureAd__TenantId", builder.Configuration["AzureAd:TenantId"])
    .WithEnvironment("AzureAd__ClientId", builder.Configuration["AzureAd:ProductClientId"])
    .WithEnvironment("IgnoreAuth", ignoreAuth.ToString());

var barista = builder.AddProject<Projects.BaristaService>("barista")
    .WithEnvironment("AzureAd__Instance", builder.Configuration["AzureAd:Instance"])
    .WithEnvironment("AzureAd__TenantId", builder.Configuration["AzureAd:TenantId"])
    .WithEnvironment("AzureAd__ClientId", builder.Configuration["AzureAd:BaristaClientId"])
    .WithEnvironment("IgnoreAuth", ignoreAuth.ToString());

var kitchen = builder.AddProject<Projects.KitchenService>("kitchen")
    .WithEnvironment("AzureAd__Instance", builder.Configuration["AzureAd:Instance"])
    .WithEnvironment("AzureAd__TenantId", builder.Configuration["AzureAd:TenantId"])
    .WithEnvironment("AzureAd__ClientId", builder.Configuration["AzureAd:KitchenClientId"])
    .WithEnvironment("IgnoreAuth", ignoreAuth.ToString());

var counter = builder.AddProject<Projects.CounterService>("counter")
    .WithEnvironment("AzureAd__Instance", builder.Configuration["AzureAd:Instance"])
    .WithEnvironment("AzureAd__TenantId", builder.Configuration["AzureAd:TenantId"])
    .WithEnvironment("AzureAd__ClientId", builder.Configuration["AzureAd:CounterClientId"])
    .WithEnvironment("AzureAd__ClientSecret", builder.Configuration["AzureAd:CounterClientSecret"])
    .WithEnvironment("IgnoreAuth", ignoreAuth.ToString())
    .WithReference(product).WaitFor(product)
    .WithReference(barista).WaitFor(barista)
    .WithReference(kitchen).WaitFor(kitchen);
counter.WithReference(chatModelId);
counter.WithReference(embeddingModelId);
counter.WithReference(endpoint);
counter.WithReference(apiKey);
// counter.WithReference(cache).WaitFor(cache);

builder.AddProject<Projects.ChatApp>("web")
    .WithEnvironment("AzureAd__Domain", builder.Configuration["AzureAd:Domain"])
    .WithEnvironment("AzureAd__Instance", builder.Configuration["AzureAd:Instance"])
    .WithEnvironment("AzureAd__TenantId", builder.Configuration["AzureAd:TenantId"])
    .WithEnvironment("AzureAd__ClientId", builder.Configuration["AzureAd:CounterClientId"])
    .WithEnvironment("AzureAd__ClientSecret", builder.Configuration["AzureAd:CounterClientSecret"])
    .WithReference(counter).WaitFor(counter);

builder.Build().Run();
