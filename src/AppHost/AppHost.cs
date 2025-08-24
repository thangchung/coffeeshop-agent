var builder = DistributedApplication.CreateBuilder(args);

builder.AddProject<Projects.ProductCatalogService>("product-catalog");

builder.AddProject<Projects.CounterService>("counter");

builder.AddProject<Projects.BaristaService>("barista");

builder.AddProject<Projects.KitchenService>("kitchen");

builder.Build().Run();
