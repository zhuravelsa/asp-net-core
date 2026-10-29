using System.ComponentModel.DataAnnotations;

namespace PromoCodeFactory.WebHost.Models.PromoCodes;

public record PromoCodeCreateRequest(
    [Required(ErrorMessage = "Code is required")]
    [StringLength(100, MinimumLength = 2, ErrorMessage = "Code must be between 2 and 100 characters")]
    string Code,

    [Required(ErrorMessage = "ServiceInfo is required")]
    [StringLength(256, ErrorMessage = "ServiceInfo must be at most 256 characters")]
    string ServiceInfo,

    [Required(ErrorMessage = "PartnerName is required")]
    [StringLength(256, ErrorMessage = "PartnerName must be at most 256 characters")]
    string PartnerName,

    DateTimeOffset BeginDate,
    DateTimeOffset EndDate,
    Guid PartnerManagerId,
    Guid PreferenceId);
