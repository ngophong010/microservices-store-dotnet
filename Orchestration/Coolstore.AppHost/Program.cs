var builder = DistributedApplication.CreateBuilder(args);

// Add your infrastructure containers
var postgres = builder.AddPostgres("postgres-db");
var redis = builder.AddRedis("redis-cache");

// Add your .NET projects (microservices)
var identityApi = builder.AddProject<Projects.IdentityServer>("identity-api")
    .WithReference(postgres);

var shoppingCartApi = builder.AddProject<Projects.ShoppingCart>("shoppingcart-api")
    .WithReference(redis);

var gateway = builder.AddProject<Projects.Gateway>("api-gateway")
    .WithReference(identityApi)
    .WithReference(shoppingCartApi);

// ... add other services as you modernize them ...

builder.Build().Run();
