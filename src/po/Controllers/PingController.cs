using Microsoft.AspNetCore.Mvc;

namespace po.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public sealed class PingController : ControllerBase
    {
        [HttpGet]
        public IActionResult Get()
        {
            return this.Ok();
        }
    }
}
