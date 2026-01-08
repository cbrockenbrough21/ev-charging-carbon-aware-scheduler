using Microsoft.AspNetCore.Mvc;

namespace EvCharging.API.Controllers
{
    [ApiController]
    [Route("v1/[controller]")]
    public class HealthController : ControllerBase
    {
        [HttpGet]
        public IActionResult Get()
        {
            return Ok();
        }
    }
}
