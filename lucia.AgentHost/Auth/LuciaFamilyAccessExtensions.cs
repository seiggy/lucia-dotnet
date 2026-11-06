using System.Threading.RateLimiting;
using lucia.AgentHost.Apis;
using lucia.AgentHost.Family;
using lucia.Agents.Abstractions;
using lucia.Agents.Auth;
using lucia.Agents.Family;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;

namespace lucia.AgentHost.Auth;

public static class LuciaFamilyAccessExtensions
{
    /// <summary>
    /// Registers authentication (API key/session, internal token, optional OIDC), authorization
    /// policies, pairing rate limits, and the family services they depend on. Stores come from
    /// the data provider registration.
    /// </summary>
    public static IServiceCollection AddLuciaFamilyAccess(this IServiceCollection services, OidcAuthOptions oidcOptions)
    {
        services.AddSingleton<MemberPrincipalResolver>();
        services.AddSingleton<ActionBroker>();
        services.AddSingleton<DevicePairingService>();
        services.AddSingleton(sp => new FamilyMemberService(
            sp.GetRequiredService<IFamilyMemberStore>(),
            sp.GetRequiredService<IApiKeyService>(),
            sp.GetRequiredService<ILlmUsageStore>(),
            sp.GetRequiredService<IModelProviderRepository>(),
            sp.GetService<lucia.Wyoming.Diarization.ISpeakerProfileStore>(),
            sp.GetRequiredService<TimeProvider>()));
        services.AddSingleton(oidcOptions);

        var authentication = services.AddAuthentication(options =>
            {
                options.DefaultScheme = "MultiScheme";
                options.DefaultChallengeScheme = "MultiScheme";
            })
            .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(
                AuthOptions.AuthenticationScheme, _ => { })
            .AddScheme<AuthenticationSchemeOptions, InternalTokenAuthenticationHandler>(
                InternalTokenDefaults.AuthenticationScheme, _ => { })
            .AddPolicyScheme("MultiScheme", "API Key, Internal Token or OIDC", options =>
            {
                options.ForwardDefaultSelector = context =>
                    LuciaAuthorization.SelectScheme(context, oidcOptions.IsConfigured);
            });
        if (oidcOptions.IsConfigured)
        {
            authentication.AddJwtBearer(LuciaAuthorization.OidcScheme, options =>
            {
                options.Authority = oidcOptions.Authority;
                options.Audience = oidcOptions.Audience;
                options.RequireHttpsMetadata = oidcOptions.RequireHttpsMetadata;
                options.MapInboundClaims = false;
                options.Events = new JwtBearerEvents
                {
                    OnTokenValidated = async context =>
                    {
                        var resolver = context.HttpContext.RequestServices.GetRequiredService<MemberPrincipalResolver>();
                        if (!await OidcMemberMapper.MapAsync(context.Principal!, resolver, context.HttpContext.RequestAborted)
                                .ConfigureAwait(false))
                        {
                            context.Fail("OIDC identity is not linked to an active family member.");
                        }
                    },
                };
            });
        }

        services.AddAuthorization(LuciaAuthorization.ConfigurePolicies);
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy(PairingApi.RedeemRateLimitPolicy, context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1) }));
        });
        return services;
    }
}
