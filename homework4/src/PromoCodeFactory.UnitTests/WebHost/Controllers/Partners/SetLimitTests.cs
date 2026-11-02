using AwesomeAssertions;
using Bogus;
using Microsoft.AspNetCore.Mvc;
using Moq;
using PromoCodeFactory.Core.Abstractions.Repositories;
using PromoCodeFactory.Core.Domain.Administration;
using PromoCodeFactory.Core.Domain.PromoCodeManagement;
using PromoCodeFactory.Core.Exceptions;
using PromoCodeFactory.WebHost.Controllers;
using PromoCodeFactory.WebHost.Models.Partners;
using Soenneker.Utils.AutoBogus;

namespace PromoCodeFactory.UnitTests.WebHost.Controllers.Partners;

public class SetLimitTests
{
    private readonly Mock<IRepository<Partner>> _partnersRepositoryMock;
    private readonly Mock<IRepository<PartnerPromoCodeLimit>> _partnerLimitsRepositoryMock;
    private readonly PartnersController _sut;
    private readonly Faker _faker = new();

    public SetLimitTests()
    {
        _partnersRepositoryMock = new Mock<IRepository<Partner>>();
        _partnerLimitsRepositoryMock = new Mock<IRepository<PartnerPromoCodeLimit>>();
        _sut = new PartnersController(_partnersRepositoryMock.Object, _partnerLimitsRepositoryMock.Object);
    }

    [Fact]
    public async Task CreateLimit_WhenPartnerNotFound_ReturnsNotFound()
    {
        // Arrange
        var partnerId = Guid.NewGuid();
        var request = CreateRequest();

        _partnersRepositoryMock
            .Setup(r => r.GetById(partnerId, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Partner?)null);

        // Act
        var result = await _sut.CreateLimit(partnerId, request, CancellationToken.None);

        // Assert
        var problemDetails = result.Result.Should().BeOfType<NotFoundObjectResult>()
            .Which.Value.Should().BeOfType<ProblemDetails>()
            .Subject;
        problemDetails.Title.Should().Be("Partner not found");
        problemDetails.Detail.Should().Be($"Partner with Id {partnerId} not found.");

        _partnerLimitsRepositoryMock.Verify(
            r => r.Add(It.IsAny<PartnerPromoCodeLimit>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task CreateLimit_WhenPartnerBlocked_ReturnsUnprocessableEntity()
    {
        // Arrange
        var partner = CreatePartner(isActive: false);
        var request = CreateRequest();

        _partnersRepositoryMock
            .Setup(r => r.GetById(partner.Id, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(partner);

        // Act
        var result = await _sut.CreateLimit(partner.Id, request, CancellationToken.None);

        // Assert
        var problemDetails = result.Result.Should().BeOfType<UnprocessableEntityObjectResult>()
            .Which.Value.Should().BeOfType<ProblemDetails>()
            .Subject;
        problemDetails.Title.Should().Be("Partner blocked");
        problemDetails.Detail.Should().Be("Cannot create limit for a blocked partner.");

        _partnersRepositoryMock.Verify(
            r => r.Update(It.IsAny<Partner>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _partnerLimitsRepositoryMock.Verify(
            r => r.Add(It.IsAny<PartnerPromoCodeLimit>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task CreateLimit_WhenValidRequest_ReturnsCreatedAndAddsLimit()
    {
        // Arrange
        var partner = CreatePartner(isActive: true);
        var request = CreateRequest();

        _partnersRepositoryMock
            .Setup(r => r.GetById(partner.Id, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(partner);

        PartnerPromoCodeLimit? addedLimit = null;
        _partnerLimitsRepositoryMock
            .Setup(r => r.Add(It.IsAny<PartnerPromoCodeLimit>(), It.IsAny<CancellationToken>()))
            .Callback<PartnerPromoCodeLimit, CancellationToken>((limit, _) => addedLimit = limit)
            .Returns(Task.CompletedTask);

        // Act
        var result = await _sut.CreateLimit(partner.Id, request, CancellationToken.None);

        // Assert
        addedLimit.Should().NotBeNull();
        addedLimit!.Id.Should().NotBeEmpty();
        addedLimit.Partner.Should().BeSameAs(partner);
        addedLimit.Limit.Should().Be(request.Limit);
        addedLimit.EndAt.Should().Be(request.EndAt);
        addedLimit.IssuedCount.Should().Be(0);
        addedLimit.CanceledAt.Should().BeNull();
        addedLimit.CreatedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));

        var createdResult = result.Result.Should().BeOfType<CreatedAtActionResult>().Subject;
        createdResult.ActionName.Should().Be(nameof(PartnersController.GetLimit));
        createdResult.RouteValues.Should().Contain("partnerId", partner.Id);
        createdResult.RouteValues.Should().Contain("limitId", addedLimit.Id);
        createdResult.Value.Should().BeEquivalentTo(new PartnerPromoCodeLimitResponse(
            addedLimit.Id,
            addedLimit.CreatedAt,
            request.EndAt,
            CanceledAt: null,
            request.Limit,
            IssuedCount: 0));

        // Активных лимитов не было — сохранять партнера не нужно
        _partnersRepositoryMock.Verify(
            r => r.Update(It.IsAny<Partner>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _partnerLimitsRepositoryMock.Verify(
            r => r.Add(It.IsAny<PartnerPromoCodeLimit>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task CreateLimit_WhenValidRequestWithActiveLimits_CancelsOldLimitsAndAddsNew()
    {
        // Arrange
        var partner = CreatePartner(isActive: true);
        var activeLimit = AddLimit(partner, canceledAt: null);
        var previouslyCanceledAt = DateTimeOffset.UtcNow.AddDays(-10);
        var canceledLimit = AddLimit(partner, canceledAt: previouslyCanceledAt);
        var request = CreateRequest();

        _partnersRepositoryMock
            .Setup(r => r.GetById(partner.Id, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(partner);

        _partnersRepositoryMock
            .Setup(r => r.Update(It.IsAny<Partner>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        PartnerPromoCodeLimit? addedLimit = null;
        _partnerLimitsRepositoryMock
            .Setup(r => r.Add(It.IsAny<PartnerPromoCodeLimit>(), It.IsAny<CancellationToken>()))
            .Callback<PartnerPromoCodeLimit, CancellationToken>((limit, _) => addedLimit = limit)
            .Returns(Task.CompletedTask);

        // Act
        var result = await _sut.CreateLimit(partner.Id, request, CancellationToken.None);

        // Assert
        result.Result.Should().BeOfType<CreatedAtActionResult>();

        activeLimit.CanceledAt.Should().NotBeNull();
        activeLimit.CanceledAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
        canceledLimit.CanceledAt.Should().Be(previouslyCanceledAt);

        addedLimit.Should().NotBeNull();
        addedLimit!.Id.Should().NotBe(activeLimit.Id);
        addedLimit.CanceledAt.Should().BeNull();
        addedLimit.Limit.Should().Be(request.Limit);
        addedLimit.EndAt.Should().Be(request.EndAt);

        _partnersRepositoryMock.Verify(
            r => r.Update(partner, It.IsAny<CancellationToken>()),
            Times.Once);
        _partnerLimitsRepositoryMock.Verify(
            r => r.Add(It.IsAny<PartnerPromoCodeLimit>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task CreateLimit_WhenUpdateThrowsEntityNotFoundException_ReturnsNotFound()
    {
        // Arrange
        var partner = CreatePartner(isActive: true);
        AddLimit(partner, canceledAt: null);
        var request = CreateRequest();

        _partnersRepositoryMock
            .Setup(r => r.GetById(partner.Id, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(partner);

        _partnersRepositoryMock
            .Setup(r => r.Update(It.IsAny<Partner>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new EntityNotFoundException<Partner>(partner.Id));

        // Act
        var result = await _sut.CreateLimit(partner.Id, request, CancellationToken.None);

        // Assert
        result.Result.Should().BeOfType<NotFoundResult>();

        _partnerLimitsRepositoryMock.Verify(
            r => r.Add(It.IsAny<PartnerPromoCodeLimit>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private PartnerPromoCodeLimitCreateRequest CreateRequest() =>
        new(
            EndAt: DateTimeOffset.UtcNow.AddDays(_faker.Random.Int(1, 365)),
            Limit: _faker.Random.Int(1, 1000));

    private static Partner CreatePartner(bool isActive)
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
            .RuleFor(p => p.IsActive, _ => isActive)
            .RuleFor(p => p.Manager, employee)
            .RuleFor(p => p.PartnerLimits, _ => new List<PartnerPromoCodeLimit>())
            .Generate();
    }

    private static PartnerPromoCodeLimit AddLimit(Partner partner, DateTimeOffset? canceledAt)
    {
        var limit = new AutoFaker<PartnerPromoCodeLimit>()
            .RuleFor(l => l.Id, _ => Guid.NewGuid())
            .RuleFor(l => l.Partner, partner)
            .RuleFor(l => l.CanceledAt, _ => canceledAt)
            .RuleFor(l => l.CreatedAt, _ => DateTimeOffset.UtcNow.AddDays(-30))
            .RuleFor(l => l.EndAt, _ => DateTimeOffset.UtcNow.AddDays(30))
            .RuleFor(l => l.Limit, f => f.Random.Int(1, 100))
            .RuleFor(l => l.IssuedCount, _ => 0)
            .Generate();

        partner.PartnerLimits.Add(limit);
        return limit;
    }
}
