using Homespun.Shared.Models.Observability;
using Microsoft.AspNetCore.Mvc;

namespace Homespun.Features.Observability;

[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class TelemetryConfigController(IConfiguration configuration) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<TelemetryConfigDto>(StatusCodes.Status200OK)]
    public ActionResult<TelemetryConfigDto> GetConfig()
    {
        var connectionString = configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"];

        return Ok(new TelemetryConfigDto
        {
            ApplicationInsightsConnectionString = connectionString,
            IsEnabled = !string.IsNullOrEmpty(connectionString)
        });
    }
}
