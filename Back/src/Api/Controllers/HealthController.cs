using Microsoft.AspNetCore.Mvc;

namespace Service.Api.Controllers;

/// <summary>
/// Health endpoints for availability checks.
/// </summary>
[ApiController]
[Route("api/health")]
public sealed class HealthController : ControllerBase
{
    /// <summary>
    /// Returns application health and version info.
    /// </summary>
    /// <returns>Health status response.</returns>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<HealthResponse> Get()
    {
        return Ok(new HealthResponse("ok", "1.0.0"));
    }
}

/// <summary>
/// Health response payload.
/// </summary>
/// <param name="Status">Health status.</param>
/// <param name="Version">Application version.</param>
public sealed record HealthResponse(string Status, string Version);
