using System.Text.Json;
using FlyRank.Auth.Api.Triage;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FlyRank.Auth.Api.Controllers;

/// <summary>
/// One request in, one structured answer out. No conversation, no memory.
/// A model call is slow and sometimes wrong, so this is treated as untrusted input:
/// validate the request, call the model, validate its answer, repair once, else 422.
/// </summary>
[ApiController]
[Route("triage")]
[Produces("application/json")]
[AllowAnonymous]
public class TriageController : ControllerBase
{
    private readonly TriageInputValidator _validator;
    private readonly TriageService _service;

    public TriageController(TriageInputValidator validator, TriageService service)
    {
        _validator = validator;
        _service = service;
    }

    /// <summary>Classify one support message into a category, urgency and team.</summary>
    /// <response code="200">Validated classification.</response>
    /// <response code="400">Invalid input (names the offending field).</response>
    /// <response code="422">The model's answer failed validation twice.</response>
    /// <response code="502">The model provider failed.</response>
    /// <response code="503">Triage is disabled or not configured.</response>
    /// <response code="504">The model call timed out.</response>
    [HttpPost]
    [ProducesResponseType(typeof(TriageResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    [ProducesResponseType(StatusCodes.Status504GatewayTimeout)]
    public async Task<IActionResult> Post([FromBody] JsonElement body, CancellationToken cancellationToken)
    {
        var validation = _validator.Validate(body);
        if (!validation.Ok)
        {
            return BadRequest(new { error = validation.Error, field = validation.Field });
        }

        var text = body.GetProperty("text").GetString()!;
        var outcome = await _service.TriageAsync(text, cancellationToken);
        if (!outcome.Ok)
        {
            return StatusCode(outcome.StatusCode, new { error = outcome.Error });
        }

        Response.Headers["X-Triage-Repaired"] = outcome.Repaired ? "true" : "false";
        return Ok(outcome.Value);
    }
}
