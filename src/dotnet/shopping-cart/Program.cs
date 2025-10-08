using ShoppingCart;
using Asp.Versioning;
using Asp.Versioning.ApiExplorer;
using Microsoft.Extensions.Options;
using ShoppingCart.Core.Gateways;
using ShoppingCart.Infrastructure.Gateways;
using ShoppingCart.UseCases.Checkout;
using ShoppingCart.UseCases.CreateShoppingCartWithProduct;
using ShoppingCart.UseCases.GetCartByUserId;
using ShoppingCart.UseCases.UpdateAmountOfProductInShoppingCart;
using Swashbuckle.AspNetCore.SwaggerGen;

var builder = WebApplication.CreateBuilder(args);

builder.Host.AddSerilog("ShoppingCart");

// =================================================================
// API VERSIONING CONFIGURATION
// =================================================================
builder.Services.AddApiVersioning(options =>
    {
        options.AssumeDefaultVersionWhenUnspecified = true;
        options.DefaultApiVersion = new ApiVersion(1, 0);
        options.ReportApiVersions = true;
    })
    .AddApiExplorer(options =>
    {
        options.GroupNameFormat = "'v'VVV";
        options.SubstituteApiVersionInUrl = true;
    });

builder.Services.AddTransient<IConfigureOptions<SwaggerGenOptions>, ConfigureSwaggerOptions>();

builder.Services
    .AddCustomCors()
    .AddEndpointsApiExplorer()
    .AddHttpContextAccessor()
    .AddCustomMediatR(new[] {typeof(Anchor)})
    .AddCustomValidators(new[] {typeof(Anchor)})
    .AddSwaggerGen()
    .AddCustomDaprClient()
    .AddHealthChecks();

// builder.Services.AddCustomAuth<Anchor>(builder.Configuration);

builder.Services.AddScoped<ISecurityContextAccessor, SecurityContextAccessor>();
builder.Services.AddScoped<IProductCatalogGateway, ProductCatalogGateway>();
builder.Services.AddScoped<IPromoGateway, PromoGateway>();
builder.Services.AddScoped<IShippingGateway, ShippingGateway>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
}

app.UseSerilogRequestLogging();

app.MapGet("/error", () => Results.Problem("An error occurred.", statusCode: 500))
    .ExcludeFromDescription();

app.UseMiddleware<ExceptionMiddleware>();

app.UseCustomCors();
app.UseRouting();

app.UseCloudEvents();

// app.UseAuthentication();
// app.UseAuthorization();

app.UseSwagger();
app.UseSwaggerUI(options =>
{
    var descriptions = app.Services.GetRequiredService<IApiVersionDescriptionProvider>().ApiVersionDescriptions;
    foreach (var description in descriptions)
    {
        options.SwaggerEndpoint($"/swagger/{description.GroupName}/swagger.json", description.GroupName.ToUpperInvariant());
    }
});

app.MapHealthChecks("/api/healthz");
app.MapFallback(() => Results.Redirect("/swagger"));
app.MapSubscribeHandler();

app.MapGet("/info", (IConfiguration config) => Results.Content(config.BuildAppStatus()));

// Create a version set for our APIs
var versionSet = app.NewApiVersionSet()
    .HasApiVersion(new ApiVersion(1, 0))
    .ReportApiVersions()
    .Build();

// Create a route group for API version 1
var v1 = app.MapGroup("/api/v{version:apiVersion}")
    .WithApiVersionSet(versionSet);

// Map the original endpoints to the versioned group
v1.MapGet("/carts", async (ISender sender) => await sender.Send(new GetCartByUserIdQuery()));
v1.MapPost("/carts", async (CreateShoppingCartWithProductCommand command, ISender sender) => await sender.Send(command));
v1.MapPut("/carts", async (UpdateAmountOfProductInShoppingCartCommand command, ISender sender) => await sender.Send(command));
v1.MapPut("/carts/checkout", async (ISender sender) => await sender.Send(new CheckOutCommand()));

await WithSeriLog(async () => await app.RunAsync());
