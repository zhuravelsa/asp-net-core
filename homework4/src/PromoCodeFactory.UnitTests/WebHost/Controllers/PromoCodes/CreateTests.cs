using System.Linq.Expressions;
using AwesomeAssertions;
using Bogus;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using PromoCodeFactory.Core.Abstractions.Repositories;
using PromoCodeFactory.Core.Domain.Administration;
using PromoCodeFactory.Core.Domain.PromoCodeManagement;
using PromoCodeFactory.WebHost.Controllers;
using PromoCodeFactory.WebHost.Models.PromoCodes;
using Soenneker.Utils.AutoBogus;

namespace PromoCodeFactory.UnitTests.WebHost.Controllers.PromoCodes;

public class CreateTests
{
    private readonly Mock<IRepository<PromoCode>> _promoCodesRepositoryMock;
    private readonly Mock<IRepository<Customer>> _customersRepositoryMock;
    private readonly Mock<IRepository<CustomerPromoCode>> _customerPromoCodesRepositoryMock;
    private readonly Mock<IRepository<Partner>> _partnersRepositoryMock;
    private readonly Mock<IRepository<Preference>> _preferencesRepositoryMock;
    private readonly PromoCodesController _sut;
    private readonly Faker _faker = new();

    public CreateTests()
    {
        _promoCodesRepositoryMock = new Mock<IRepository<PromoCode>>();
        _customersRepositoryMock = new Mock<IRepository<Customer>>();
        _customerPromoCodesRepositoryMock = new Mock<IRepository<CustomerPromoCode>>();
        _partnersRepositoryMock = new Mock<IRepository<Partner>>();
        _preferencesRepositoryMock = new Mock<IRepository<Preference>>();
        _sut = new PromoCodesController(
            _promoCodesRepositoryMock.Object,
            _customersRepositoryMock.Object,
            _customerPromoCodesRepositoryMock.Object,
            _partnersRepositoryMock.Object,
            _preferencesRepositoryMock.Object);
    }

    [Fact]
    public async Task Create_WhenPartnerNotFound_ReturnsNotFound()
    {
        // Arrange
        var request = CreateRequest(Guid.NewGuid(), Guid.NewGuid());

        _partnersRepositoryMock
            .Setup(r => r.GetById(request.PartnerId, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Partner?)null);

        // Act
        var result = await _sut.Create(request, CancellationToken.None);

        // Assert
        var problemDetails = result.Result.Should().BeOfType<NotFoundObjectResult>()
            .Which.Value.Should().BeOfType<ProblemDetails>()
            .Subject;
        problemDetails.Title.Should().Be("Partner not found");
        problemDetails.Detail.Should().Be($"Partner with Id {request.PartnerId} not found.");

        VerifyPromoCodeNotCreated();
    }

    [Fact]
    public async Task Create_WhenPreferenceNotFound_ReturnsNotFound()
    {
        // Arrange
        var partner = CreatePartner();
        AddLimit(partner, limit: 10, issuedCount: 0);
        var request = CreateRequest(partner.Id, Guid.NewGuid());

        SetupPartner(partner);
        _preferencesRepositoryMock
            .Setup(r => r.GetById(request.PreferenceId, It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Preference?)null);

        // Act
        var result = await _sut.Create(request, CancellationToken.None);

        // Assert
        var problemDetails = result.Result.Should().BeOfType<NotFoundObjectResult>()
            .Which.Value.Should().BeOfType<ProblemDetails>()
            .Subject;
        problemDetails.Title.Should().Be("Preference not found");
        problemDetails.Detail.Should().Be($"Preference with Id {request.PreferenceId} not found.");

        VerifyPromoCodeNotCreated();
    }

    [Fact]
    public async Task Create_WhenNoActiveLimit_ReturnsUnprocessableEntity()
    {
        // Arrange
        var partner = CreatePartner();
        // Отмененный лимит и лимит с истекшим сроком не считаются активными
        AddLimit(partner, limit: 10, issuedCount: 0, canceledAt: DateTimeOffset.UtcNow.AddDays(-1));
        AddLimit(partner, limit: 10, issuedCount: 0, endAt: DateTimeOffset.UtcNow.AddDays(-1));
        var preference = CreatePreference();
        var request = CreateRequest(partner.Id, preference.Id);

        SetupPartner(partner);
        SetupPreference(preference);
        SetupCustomers([]);

        // Act
        var result = await _sut.Create(request, CancellationToken.None);

        // Assert
        var objectResult = result.Result.Should().BeOfType<ObjectResult>().Subject;
        objectResult.StatusCode.Should().Be(StatusCodes.Status422UnprocessableEntity);
        var problemDetails = objectResult.Value.Should().BeOfType<ProblemDetails>().Subject;
        problemDetails.Title.Should().Be("No active limit");
        problemDetails.Detail.Should().Be("Partner has no active promo code limit.");

        VerifyPromoCodeNotCreated();
    }

    [Fact]
    public async Task Create_WhenLimitExceeded_ReturnsUnprocessableEntity()
    {
        // Arrange
        var partner = CreatePartner();
        var limitValue = _faker.Random.Int(1, 100);
        var activeLimit = AddLimit(partner, limit: limitValue, issuedCount: limitValue);
        var preference = CreatePreference();
        var request = CreateRequest(partner.Id, preference.Id);

        SetupPartner(partner);
        SetupPreference(preference);
        SetupCustomers([]);

        // Act
        var result = await _sut.Create(request, CancellationToken.None);

        // Assert
        var objectResult = result.Result.Should().BeOfType<ObjectResult>().Subject;
        objectResult.StatusCode.Should().Be(StatusCodes.Status422UnprocessableEntity);
        var problemDetails = objectResult.Value.Should().BeOfType<ProblemDetails>().Subject;
        problemDetails.Title.Should().Be("Limit exceeded");
        problemDetails.Detail.Should().Be(
            $"Cannot create promo code. Limit would be exceeded (current: {limitValue}/{limitValue}).");

        activeLimit.IssuedCount.Should().Be(limitValue);
        VerifyPromoCodeNotCreated();
    }

    [Fact]
    public async Task Create_WhenValidRequest_ReturnsCreatedAndIncrementsIssuedCount()
    {
        // Arrange
        var partner = CreatePartner();
        var activeLimit = AddLimit(partner, limit: 10, issuedCount: 3);
        var preference = CreatePreference();
        var customers = new AutoFaker<Customer>()
            .RuleFor(c => c.Id, _ => Guid.NewGuid())
            .RuleFor(c => c.Preferences, _ => new List<Preference> { preference })
            .RuleFor(c => c.CustomerPromoCodes, _ => new List<CustomerPromoCode>())
            .Generate(2);
        var request = CreateRequest(partner.Id, preference.Id);

        SetupPartner(partner);
        SetupPreference(preference);
        SetupCustomers(customers);

        PromoCode? addedPromoCode = null;
        _promoCodesRepositoryMock
            .Setup(r => r.Add(It.IsAny<PromoCode>(), It.IsAny<CancellationToken>()))
            .Callback<PromoCode, CancellationToken>((promoCode, _) => addedPromoCode = promoCode)
            .Returns(Task.CompletedTask);

        _partnersRepositoryMock
            .Setup(r => r.Update(It.IsAny<Partner>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _sut.Create(request, CancellationToken.None);

        // Assert
        activeLimit.IssuedCount.Should().Be(4);

        addedPromoCode.Should().NotBeNull();
        addedPromoCode!.Code.Should().Be(request.Code);
        addedPromoCode.ServiceInfo.Should().Be(request.ServiceInfo);
        addedPromoCode.Partner.Should().BeSameAs(partner);
        addedPromoCode.Preference.Should().BeSameAs(preference);
        addedPromoCode.BeginDate.Should().Be(request.BeginDate);
        addedPromoCode.EndDate.Should().Be(request.EndDate);
        addedPromoCode.CustomerPromoCodes.Select(cpc => cpc.CustomerId)
            .Should().BeEquivalentTo(customers.Select(c => c.Id));
        addedPromoCode.CustomerPromoCodes
            .Should().AllSatisfy(cpc =>
            {
                cpc.PromoCodeId.Should().Be(addedPromoCode.Id);
                cpc.AppliedAt.Should().BeNull();
            });

        var createdResult = result.Result.Should().BeOfType<CreatedAtActionResult>().Subject;
        createdResult.ActionName.Should().Be(nameof(PromoCodesController.GetById));
        createdResult.RouteValues.Should().Contain("id", addedPromoCode.Id);
        createdResult.Value.Should().BeEquivalentTo(new PromoCodeShortResponse(
            addedPromoCode.Id,
            request.Code,
            request.ServiceInfo,
            partner.Id,
            request.BeginDate,
            request.EndDate,
            preference.Id));

        _promoCodesRepositoryMock.Verify(
            r => r.Add(It.IsAny<PromoCode>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _partnersRepositoryMock.Verify(
            r => r.Update(partner, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    private void SetupPartner(Partner partner) =>
        _partnersRepositoryMock
            .Setup(r => r.GetById(partner.Id, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(partner);

    private void SetupPreference(Preference preference) =>
        _preferencesRepositoryMock
            .Setup(r => r.GetById(preference.Id, It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(preference);

    private void SetupCustomers(IReadOnlyCollection<Customer> customers) =>
        _customersRepositoryMock
            .Setup(r => r.GetWhere(
                It.IsAny<Expression<Func<Customer, bool>>>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(customers);

    private void VerifyPromoCodeNotCreated()
    {
        _promoCodesRepositoryMock.Verify(
            r => r.Add(It.IsAny<PromoCode>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _partnersRepositoryMock.Verify(
            r => r.Update(It.IsAny<Partner>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private PromoCodeCreateRequest CreateRequest(Guid partnerId, Guid preferenceId)
    {
        var beginDate = DateTimeOffset.UtcNow.AddDays(_faker.Random.Int(0, 10));
        return new PromoCodeCreateRequest(
            Code: _faker.Random.AlphaNumeric(8).ToUpperInvariant(),
            ServiceInfo: _faker.Lorem.Sentence(),
            PartnerId: partnerId,
            BeginDate: beginDate,
            EndDate: beginDate.AddDays(_faker.Random.Int(1, 90)),
            PreferenceId: preferenceId);
    }

    private static Preference CreatePreference() =>
        new AutoFaker<Preference>()
            .RuleFor(p => p.Id, _ => Guid.NewGuid())
            .RuleFor(p => p.Customers, _ => new List<Customer>())
            .Generate();

    private static Partner CreatePartner()
    {
        var role = new AutoFaker<Role>()
            .RuleFor(r => r.Id, _ => Guid.NewGuid())
            .Generate();

        var employee = new AutoFaker<Employee>()
            .RuleFor(e => e.Id, _ => Guid.NewGuid())
            .RuleFor(e => e.Role, role)
            .Generate();

        return new AutoFaker<Partner>()
            .RuleFor(p => p.Id, _ => Guid.NewGuid())
            .RuleFor(p => p.IsActive, _ => true)
            .RuleFor(p => p.Manager, employee)
            .RuleFor(p => p.PartnerLimits, _ => new List<PartnerPromoCodeLimit>())
            .Generate();
    }

    private static PartnerPromoCodeLimit AddLimit(
        Partner partner,
        int limit,
        int issuedCount,
        DateTimeOffset? canceledAt = null,
        DateTimeOffset? endAt = null)
    {
        var partnerLimit = new AutoFaker<PartnerPromoCodeLimit>()
            .RuleFor(l => l.Id, _ => Guid.NewGuid())
            .RuleFor(l => l.Partner, partner)
            .RuleFor(l => l.CreatedAt, _ => DateTimeOffset.UtcNow.AddDays(-30))
            .RuleFor(l => l.EndAt, _ => endAt ?? DateTimeOffset.UtcNow.AddDays(30))
            .RuleFor(l => l.CanceledAt, _ => canceledAt)
            .RuleFor(l => l.Limit, _ => limit)
            .RuleFor(l => l.IssuedCount, _ => issuedCount)
            .Generate();

        partner.PartnerLimits.Add(partnerLimit);
        return partnerLimit;
    }
}
