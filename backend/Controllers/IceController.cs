using HelloDoctor.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HelloDoctor.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class IceController : ControllerBase
{
    private readonly IceServerProvider _provider;
    public IceController(IceServerProvider provider) => _provider = provider;

    // Kimlik doğrulaması şart: TURN kimlik bilgisi bir kotayı harcar, herkese
    // açık olsaydı kolayca tüketilirdi.
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        var config = await _provider.GetAsync(ct);

        // İstemci önbelleğe alabilir ama süresi dolmadan tazelemeli.
        Response.Headers.CacheControl = "private, max-age=300";
        return Ok(config);
    }
}
