using Microsoft.AspNetCore.Mvc;
using po.Attributes;
using po.DataAccess;

namespace po.Controllers
{
    [ApiController]
    [Route("[controller]")]
    [ApiKeyOrSignedUrlAuthorization]
    public sealed class RandomMessageController : ControllerBase
    {
        private readonly PoContext dbContext;

        public RandomMessageController(PoContext dbContext)
        {
            this.dbContext = dbContext;
        }

        [HttpPost]
        public async Task<IActionResult> Create(Models.RandomMessage message, CancellationToken cancellationToken)
        {
            _ = this.dbContext.RandomMessages.Add(message);
            _ = await this.dbContext.SaveChangesAsync(cancellationToken);
            return this.Ok(message);
        }
    }
}
