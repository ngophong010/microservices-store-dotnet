using System.IdentityModel.Tokens.Jwt;
using Asp.Versioning.ApiExplorer;
using Gateway.Config;
using Gateway.Middleware;
using Gateway.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Logging;
using StackExchange.Redis;
using Swashbuckle.AspNetCore.SwaggerGen;

var builder = WebApplication.CreateBuilder(args);

if (builder.Environment.IsDevelopment())
{
    IdentityModelEventSource.ShowPII = true;
} // for dev only
// Disable claim mapping to get claims 1:1 from the tokens
JwtSecurityTokenHandler.DefaultInboundClaimTypeMap.Clear();

// Read config and OIDC discovery document
var config = builder.Configuration.GetGatewayConfig();
builder.Services.AddSingleton<DiscoveryDocument>();

// =================================================================
// API VERSIONING CONFIGURATION
// =================================================================
builder.Services.AddApiVersioning(options =>
    {
        options.AssumeDefaultVersionWhenUnspecified = true;
        options.DefaultApiVersion = new Asp.Versioning.ApiVersion(1, 0);
        options.ReportApiVersions = true;
        options.ApiVersionReader = new Asp.Versioning.UrlSegmentApiVersionReader();
    })
    .AddApiExplorer(options =>
    {
        options.GroupNameFormat = "'v'VVV";
        options.SubstituteApiVersionInUrl = true;
    });

// Add the Swagger configuration helper
builder.Services.AddTransient<SwaggerGenOptions, ConfigureSwaggerOptions>();
builder.Services.AddSwaggerGen(Options => Options.OperationFilter<ProxyOperationFilter>()); // Assuming you might have a filter for the proxy

// Configure Services
builder.Services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = builder.Configuration.GetValue<string>("Redis");
    options.InstanceName = "gw-authn";
});
var redis = ConnectionMultiplexer.Connect(builder.Configuration.GetValue<string>("Redis"));
builder.Services.AddDataProtection()
        .PersistKeysToStackExchangeRedis(redis, "DataProtection-Keys");

//builder.Services.AddTransient<IAuthorizationMiddlewareResultHandler, CustomAuthorizationMiddlewareResultHandler>();
builder.AddGateway(config);

builder.Services.AddCors(options =>
{
    options.AddPolicy("api", policy =>
    {
        policy.AllowAnyMethod()
            .AllowAnyHeader()
            .AllowAnyOrigin();
    });
}); // for demo only

builder.Services.AddHttpClient("oidc");
    //.ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler() // insecure for development
    //{
    //    ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
    //});

// Build App and add Middleware
var app = builder.Build();

var fordwardedHeaderOptions = new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
};
fordwardedHeaderOptions.KnownNetworks.Clear();
fordwardedHeaderOptions.KnownProxies.Clear();
app.UseForwardedHeaders(fordwardedHeaderOptions);

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        var descriptions = app.Services.GetRequiredService<IApiVersionDescriptionProvider>()
            .ApiVersionDescriptions;
        foreach (var description in descriptions)
        {
            options.SwaggerEndpoint($"/swagger/{description.GroupName}/swagger.json",
                description.GroupName.ToUpperInvariant());
        }
    });
}

app.UseHttpsRedirection();

app.UseCors("api"); // for demo only

app.UseGateway();

// Start Gateway
if (string.IsNullOrEmpty(config.Url))
{
    app.Run();
}
else
{
    app.Run(config.Url);
}
