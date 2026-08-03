using ITBees.Ksef.Credentials.Api;
using ITBees.RestfulApiControllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace ITBees.Ksef.Credentials.Controllers;

/// <summary>
/// A company's KSeF credential — token or certificate. Responses never contain the secret,
/// only the metadata the UI needs (masked token, certificate subject and validity).
/// </summary>
[Authorize]
public class KsefCredentialController : RestfulControllerBase<KsefCredentialController>
{
    private readonly IKsefCredentialService _ksefCredentialService;

    public KsefCredentialController(ILogger<KsefCredentialController> logger,
        IKsefCredentialService ksefCredentialService) : base(logger)
    {
        _ksefCredentialService = ksefCredentialService;
    }

    [HttpGet]
    [Produces(typeof(KsefCredentialVm))]
    public IActionResult Get() => ReturnOkResult(() => _ksefCredentialService.GetStatus());

    [HttpPost]
    [Produces(typeof(KsefCredentialVm))]
    public IActionResult Post([FromBody] KsefCredentialIm im) =>
        // Deliberately not logging the input — it carries the token or the certificate.
        ReturnOkResult(() => _ksefCredentialService.Save(im));

    [HttpDelete]
    public IActionResult Delete() => ReturnOkResult(() => _ksefCredentialService.Delete());
}

/// <summary>Checks whether the stored credential can actually log in to KSeF.</summary>
[Authorize]
public class KsefConnectionTestController : RestfulControllerBase<KsefConnectionTestController>
{
    private readonly IKsefCredentialService _ksefCredentialService;

    public KsefConnectionTestController(ILogger<KsefConnectionTestController> logger,
        IKsefCredentialService ksefCredentialService) : base(logger)
    {
        _ksefCredentialService = ksefCredentialService;
    }

    [HttpPost]
    [Produces(typeof(KsefConnectionTestVm))]
    public Task<IActionResult> Post(CancellationToken ct) =>
        ReturnOkResultAsync(async () => await _ksefCredentialService.TestConnectionAsync(ct));
}
