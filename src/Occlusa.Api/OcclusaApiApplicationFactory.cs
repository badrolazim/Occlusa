using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Occlusa.Api.Controllers;
using Occlusa.Api.Hubs;
using Occlusa.Api.Options;
using Occlusa.Api.Repositories;
using Occlusa.Api.Security;
using Occlusa.Api.Services;

namespace Occlusa.Api;

/// <summary>
/// Shared builder for Occlusa.Api used by both standalone execution (Program.cs)
/// and embedded in-process hosting inside Occlusa.Desktop.
/// </summary>
public static class OcclusaApiApplicationFactory
{
    public static WebApplication BuildApplication(
        string[]? args = null,
        string? contentRootPath = null,
        string? urls = null,
        string? environmentName = null)
    {
        var options = new WebApplicationOptions
        {
            Args = args,
            ApplicationName = typeof(AppointmentsController).Assembly.GetName().Name,
            ContentRootPath = string.IsNullOrWhiteSpace(contentRootPath) ? null : contentRootPath,
            EnvironmentName = string.IsNullOrWhiteSpace(environmentName) ? null : environmentName
        };

        var builder = WebApplication.CreateBuilder(options);

        if (!string.IsNullOrWhiteSpace(urls))
        {
            builder.WebHost.UseUrls(urls);
        }
        else if (string.IsNullOrWhiteSpace(builder.Configuration["urls"])
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
                .AddJwtBearer(jwtOptions =>
                {
                    jwtOptions.TokenValidationParameters = new TokenValidationParameters
                    {
                        ValidateIssuerSigningKey = true,
                        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret!)),
                        ValidateIssuer = false,
                        ValidateAudience = false,
                        ValidateLifetime = true
                    };
                });
        }

        builder.Services.AddCors(corsOptions =>
        {
            corsOptions.AddPolicy("OcclusaLocal", policy =>
            {
                policy.SetIsOriginAllowed(origin =>
                        Uri.TryCreate(origin, UriKind.Absolute, out var uri) && uri.IsLoopback)
                    .AllowAnyHeader()
                    .AllowAnyMethod()
                    .AllowCredentials();
            });
        });

        builder.Services.AddControllers(mvcOptions =>
            {
                mvcOptions.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true;
            })
            .AddApplicationPart(typeof(AppointmentsController).Assembly);

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

        return app;
    }
}
