using PromoCodeFactory.Core.Domain.PromoCodeManagement;
using PromoCodeFactory.WebHost.Models.Customers;

namespace PromoCodeFactory.WebHost.Mapping;

public static class CustomersMapper
{
    public static CustomerShortResponse ToCustomerShortResponse(Customer customer)
    {
        return new CustomerShortResponse(
            customer.Id,
            customer.FirstName,
            customer.LastName,
            customer.Email,
            customer.Preferences.Select(PreferencesMapper.ToPreferenceShortResponse).ToList());
    }

    public static CustomerResponse ToCustomerResponse(Customer customer, IReadOnlyCollection<PromoCode> promoCodes)
    {
        var promoCodesById = promoCodes.ToDictionary(p => p.Id);

        var customerPromoCodes = customer.CustomerPromoCodes
            .Where(cp => promoCodesById.ContainsKey(cp.PromoCodeId))
            .Select(cp => PromoCodesMapper.ToCustomerPromoCodeResponse(promoCodesById[cp.PromoCodeId], cp))
            .ToList();

        return new CustomerResponse(
            customer.Id,
            customer.FirstName,
            customer.LastName,
            customer.Email,
            customer.Preferences.Select(PreferencesMapper.ToPreferenceShortResponse).ToList(),
            customerPromoCodes);
    }

    public static Customer ToCustomer(CustomerCreateRequest request, IEnumerable<Preference> preferences)
    {
        return new Customer
        {
            Id = Guid.NewGuid(),
            FirstName = request.FirstName,
            LastName = request.LastName,
            Email = request.Email,
            Preferences = preferences.ToList()
        };
    }
}
