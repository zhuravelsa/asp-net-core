using Microsoft.AspNetCore.Mvc;
using PromoCodeFactory.Core.Domain.PromoCodeManagement;
using PromoCodeFactory.WebHost.Mapping;
using PromoCodeFactory.WebHost.Models.PromoCodes;

namespace PromoCodeFactory.WebHost.Controllers;

/// <summary>
/// Промокоды
/// </summary>
public class PromoCodesController(
    IRepository<PromoCode> promoCodeRepository,
    IRepository<Employee> employeeRepository,
    IRepository<Preference> preferenceRepository,
    IRepository<Customer> customerRepository,
    IRepository<CustomerPromoCode> customerPromoCodeRepository
    ) : BaseController
{
    /// <summary>
    /// Получить все промокоды
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<PromoCodeShortResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IEnumerable<PromoCodeShortResponse>>> Get(CancellationToken ct)
    {
        var promoCodes = await promoCodeRepository.GetAll(true, ct);

        var promoCodesModels = promoCodes.Select(PromoCodesMapper.ToPromoCodeShortResponse).ToList();

        return Ok(promoCodesModels);
    }

    /// <summary>
    /// Получить промокод по id
    /// </summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(PromoCodeShortResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PromoCodeShortResponse>> GetById(Guid id, CancellationToken ct)
    {
        var promoCode = await promoCodeRepository.GetById(id, true, ct);
        if (promoCode is null)
            return NotFound();

        return Ok(PromoCodesMapper.ToPromoCodeShortResponse(promoCode));
    }

    /// <summary>
    /// Создать промокод и выдать его клиентам с указанным предпочтением
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(PromoCodeShortResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PromoCodeShortResponse>> Create(PromoCodeCreateRequest request, CancellationToken ct)
    {
        if (request.BeginDate >= request.EndDate)
            return BadRequest(new ProblemDetails { Title = "Invalid dates", Detail = "BeginDate must be earlier than EndDate." });

        var existing = await promoCodeRepository.GetWhere(p => p.Code == request.Code, ct: ct);
        if (existing.Count > 0)
            return BadRequest(new ProblemDetails { Title = "Duplicate code", Detail = $"Promo code '{request.Code}' already exists." });

        var partnerManager = await employeeRepository.GetById(request.PartnerManagerId, ct: ct);
        if (partnerManager is null)
            return NotFound(new ProblemDetails { Title = "Partner manager not found", Detail = $"Employee with Id {request.PartnerManagerId} not found." });

        var preference = await preferenceRepository.GetById(request.PreferenceId, ct: ct);
        if (preference is null)
            return NotFound(new ProblemDetails { Title = "Preference not found", Detail = $"Preference with Id {request.PreferenceId} not found." });

        var promoCode = PromoCodesMapper.ToPromoCode(request, partnerManager, preference);

        var preferenceId = preference.Id;
        var customers = await customerRepository.GetWhere(c => c.Preferences.Any(p => p.Id == preferenceId), ct: ct);

        var now = DateTimeOffset.UtcNow;
        foreach (var customer in customers)
        {
            promoCode.CustomerPromoCodes.Add(new CustomerPromoCode
            {
                Id = Guid.NewGuid(),
                CustomerId = customer.Id,
                PromoCodeId = promoCode.Id,
                CreatedAt = now
            });
        }

        await promoCodeRepository.Add(promoCode, ct);

        return CreatedAtAction(nameof(GetById), new { id = promoCode.Id }, PromoCodesMapper.ToPromoCodeShortResponse(promoCode));
    }

    /// <summary>
    /// Применить промокод (отметить, что клиент использовал промокод)
    /// </summary>
    [HttpPost("{id:guid}/apply")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Apply(
        [FromRoute] Guid id,
        [FromBody] PromoCodeApplyRequest request,
        CancellationToken ct)
    {
        var promoCode = await promoCodeRepository.GetById(id, ct: ct);
        if (promoCode is null)
            return NotFound(new ProblemDetails { Title = "Promo code not found", Detail = $"Promo code with Id {id} not found." });

        var customer = await customerRepository.GetById(request.CustomerId, ct: ct);
        if (customer is null)
            return NotFound(new ProblemDetails { Title = "Customer not found", Detail = $"Customer with Id {request.CustomerId} not found." });

        var customerPromoCode = (await customerPromoCodeRepository.GetWhere(
            cp => cp.PromoCodeId == id && cp.CustomerId == request.CustomerId, ct: ct)).FirstOrDefault();

        if (customerPromoCode is null)
            return BadRequest(new ProblemDetails { Title = "Promo code not issued", Detail = "Promo code was not issued to this customer." });

        if (customerPromoCode.AppliedAt is not null)
            return BadRequest(new ProblemDetails { Title = "Promo code already applied", Detail = $"Promo code was already applied at {customerPromoCode.AppliedAt:O}." });

        var now = DateTimeOffset.UtcNow;
        if (now < promoCode.BeginDate || now > promoCode.EndDate)
            return BadRequest(new ProblemDetails { Title = "Promo code is not active", Detail = $"Promo code is valid from {promoCode.BeginDate:O} to {promoCode.EndDate:O}." });

        customerPromoCode.AppliedAt = now;

        try
        {
            await customerPromoCodeRepository.Update(customerPromoCode, ct);
        }
        catch (EntityNotFoundException)
        {
            return NotFound();
        }

        return NoContent();
    }
}
