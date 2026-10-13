using FlyRank.Auth.Api.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FlyRank.Auth.Api.Controllers;

/// <summary>
/// Public endpoints. No authentication required.
/// </summary>
[ApiController]
[Route("public")]
[Produces("application/json")]
[AllowAnonymous]
public class PublicController : ControllerBase
{
    /// <summary>Health/info endpoint that is reachable without a token.</summary>
    /// <response code="200">Always returns service information.</response>
    [HttpGet("info")]
    [ProducesResponseType(typeof(PublicInfoResponse), StatusCodes.Status200OK)]
    public ActionResult<PublicInfoResponse> Info()
    {
        return Ok(new PublicInfoResponse(Message: "Welcome stranger! This info is public."));
    }
}
