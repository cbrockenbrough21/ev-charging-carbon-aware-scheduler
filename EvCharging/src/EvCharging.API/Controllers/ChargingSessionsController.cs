using Microsoft.AspNetCore.Mvc;

namespace EvCharging.API.Controllers
{
    [ApiController]
    [Route("v1/charging-sessions")]
    public class ChargingSessionsController : ControllerBase
    {
        [HttpPost("recommendation")]
        public IActionResult PostRecommendation()
        {
            return StatusCode(StatusCodes.Status501NotImplemented);
        }
    }
}
