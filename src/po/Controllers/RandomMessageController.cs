using Microsoft.AspNetCore.Mvc;
using po.Attributes;
using po.DataAccess;
using po.Utilities;

namespace po.Controllers
{
    [ApiController]
    [Route("[controller]")]
    [ApiKeyOrSignedUrlAuthorization]
    public sealed class RandomMessageController : ControllerBase
    {
        private readonly Delays delays;
        private readonly PoContext dbContext;

        public RandomMessageController(
            Delays delays,
            PoContext dbContext)
        {
            this.delays = delays;
            this.dbContext = dbContext;
        }

        [HttpPost]
        public async Task<IActionResult> Create(Models.RandomMessage message, CancellationToken cancellationToken)
        {
            _ = this.dbContext.RandomMessages.Add(message);
            _ = await this.dbContext.SaveChangesAsync(cancellationToken);
            this.delays.RandomMessage.CancelDelay();
            return this.Ok(message);
        }
    }
}
