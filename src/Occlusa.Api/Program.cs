using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Occlusa.Api.Hubs;
using Occlusa.Api.Options;
using Occlusa.Api.Repositories;
using Occlusa.Api.Security;
using Occlusa.Api.Services;

var builder = WebApplication.CreateBuilder(args);

if (string.IsNullOrWhiteSpace(builder.Configuration["urls"])
    && string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ASPNETCORE_URLS")))
{
    builder.WebHost.UseUrls("http://localhost:5188");
}

builder.Services.Configure<SupabaseOptions>(builder.Configuration.GetSection(SupabaseOptions.SectionName));
builder.Services.Configure<WhatsAppOptions>(builder.Configuration.GetSection(WhatsAppOptions.SectionName));
builder.Services.Configure<GeminiOptions>(builder.Configuration.GetSection(GeminiOptions.SectionName));
builder.Services.Configure<ClinicOptions>(builder.Configuration.GetSection(ClinicOptions.SectionName));

builder.Services.AddHttpContextAccessor();
builder.Services.AddHttpClient();
builder.Services.AddScoped<RequestClaimsContext>();

var supabaseConnectionString = builder.Configuration.GetSection(SupabaseOptions.SectionName)["ConnectionString"];
if (string.IsNullOrWhiteSpace(supabaseConnectionString))
{
    builder.Services.AddSingleton<IOcclusaRepository, InMemoryOcclusaRepository>();
}
else
{
    builder.Services.AddScoped<IOcclusaRepository, NpgsqlSupabaseRepository>();
}

builder.Services.AddSingleton<IRealtimeNotifier, QueueRealtimeNotifier>();
builder.Services.AddScoped<IWhatsAppAiReceptionistService, WhatsAppAiReceptionistService>();

var jwtSecret = builder.Configuration.GetSection(SupabaseOptions.SectionName)["JwtSecret"];
var useJwtBearer = !string.IsNullOrWhiteSpace(jwtSecret);
if (useJwtBearer)
{
    builder.Services
        .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(options =>
        {
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret!)),
                ValidateIssuer = false,
                ValidateAudience = false,
                ValidateLifetime = true
            };
        });
}

builder.Services.AddCors(options =>
{
    options.AddPolicy("OcclusaLocal", policy =>
    {
        policy.SetIsOriginAllowed(origin =>
                Uri.TryCreate(origin, UriKind.Absolute, out var uri) && uri.IsLoopback)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

builder.Services.AddControllers(options =>
{
    options.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true;
});
builder.Services.AddSignalR();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors("OcclusaLocal");

if (useJwtBearer)
{
    app.UseAuthentication();
}

app.UseMiddleware<JwtClaimsMiddleware>();

app.MapControllers();
app.MapHub<QueueHub>("/hubs/queue");

app.Run();

public partial class Program { }
