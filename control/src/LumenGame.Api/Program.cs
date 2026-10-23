using System.Net.Sockets;
using LumenGame.Api;
using LumenGame.Contracts;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(options =>
{
    options.SingleLine = true;
    options.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ ";
    options.UseUtcTimestamp = true;
});
builder.Services.Configure<ControlOptions>(builder.Configuration.GetSection(ControlOptions.SectionName));
builder.Services.AddSingleton<UserStore>();
builder.Services.AddSingleton<SessionStore>();
builder.Services.AddSingleton<CatalogStore>();
builder.Services.AddSingleton<AgentClient>();
builder.Services.AddAuthentication("Bearer")
    .AddScheme<AuthenticationSchemeOptions, TokenAuthenticationHandler>("Bearer", _ => { });
builder.Services.AddAuthorizationBuilder()
    .AddPolicy("AdminOnly", policy => policy.RequireRole("Admin"));
builder.Services.AddProblemDetails();

WebApplication app = builder.Build();
app.UseExceptionHandler();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new { state = "healthy", observedAt = DateTimeOffset.UtcNow }));

app.MapPost("/api/login", async (LoginRequest request, UserStore users, SessionStore sessions) =>
{
    StoredUser? user = await users.ValidateAsync(request.UserName, request.Password);
    if (user is null)
    {
        return Results.Unauthorized();
    }

    (string token, SessionRecord session) = sessions.Create(user);
    return Results.Ok(new LoginResponse(token, session.ExpiresAt, session.UserName, session.Role));
});

app.MapGet("/api/status", async (CatalogStore catalog, CancellationToken cancellationToken) =>
    Results.Ok(await catalog.ReadStatusAsync(cancellationToken))).RequireAuthorization();

app.MapGet("/api/library", async (CatalogStore catalog, CancellationToken cancellationToken) =>
    Results.Ok(await catalog.ReadLibraryAsync(cancellationToken))).RequireAuthorization();

app.MapGet("/api/admin/users", async (UserStore users) => Results.Ok(await users.ListAsync()))
    .RequireAuthorization("AdminOnly");

app.MapPost("/api/admin/users", async (CreateUserRequest request, UserStore users) =>
{
    try
    {
        return Results.Ok(await users.CreateOrUpdateUserAsync(request));
    }
    catch (InvalidOperationException exception)
    {
        return Results.BadRequest(new ApiError("invalid_user", exception.Message));
    }
}).RequireAuthorization("AdminOnly");

app.MapPost("/api/admin/activate", async (ActivationRequest request, AgentClient agent, IOptions<ControlOptions> options, System.Security.Claims.ClaimsPrincipal principal, CancellationToken cancellationToken) =>
{
    if (!options.Value.ControlAgentEnabled)
    {
        return Results.Json(
            new ApiError("control_agent_disabled", "World activation is locked until the transactional host agent passes validation."),
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    string actor = principal.Identity?.Name ?? "unknown-admin";
    try
    {
        AgentActivationResponse response = await agent.ActivateAsync(request, actor, cancellationToken);
        return response.Success
            ? Results.Ok(response)
            : Results.Json(response, statusCode: StatusCodes.Status409Conflict);
    }
    catch (Exception exception) when (exception is SocketException or IOException or TimeoutException or OperationCanceledException)
    {
        return Results.Json(
            new ApiError("control_agent_unavailable", "The control agent is not available."),
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }
}).RequireAuthorization("AdminOnly");

app.Run();
