using Microsoft.AspNetCore.Mvc;

namespace po.Controllers
{
    [ApiController]
    public class RootLevelController : ControllerBase
    {
        private static ulong rootSequence = 0;

        [HttpGet("/")]
        public IActionResult Root()
        {
            return this.Ok(++rootSequence);
        }

        [HttpGet("/robots933456.txt")]
        public IActionResult AzureAppServiceRobotsTxt()
        {
            // https://github.com/MicrosoftDocs/azure-docs/blob/f71ba01af68bd4859cecce515e7eeeab2d8dd298/includes/app-service-web-configure-robots933456.md
            return this.NoContent();
        }
    }
}
