using LicensingApi.Dtos;
using LicensingApi.Services;
using LicensingCore.Entities;
using Xunit;

namespace LicensingSystem.Tests;

public class HardwareMatchServiceTests
{
    private readonly HardwareMatchService _svc = new();

    private static HardwareInfo Hw(string suffix = "1") => new()
    {
        CpuId = $"CPU-{suffix}",
        MotherboardSerial = $"MB-{suffix}",
        TpmId = $"TPM-{suffix}",
        MacAddressPrimary = $"MAC-{suffix}",
    };

    private static Activation NewActivation(HardwareInfo hw, ActivationStatus status) => new()
    {
        Id = Guid.NewGuid(),
        CpuId = hw.CpuId,
        MotherboardSerial = hw.MotherboardSerial,
        TpmId = hw.TpmId,
        MacAddressPrimary = hw.MacAddressPrimary,
        Status = status,
    };

    [Fact]
    public void IsSameMachine_AllFourIdentifiersMatch_ReturnsTrue()
    {
        var hw = Hw();
        var activation = NewActivation(hw, ActivationStatus.Approved);

        Assert.True(_svc.IsSameMachine(activation, hw));
    }

    [Theory]
    [InlineData(nameof(HardwareInfo.CpuId))]
    [InlineData(nameof(HardwareInfo.MotherboardSerial))]
    [InlineData(nameof(HardwareInfo.TpmId))]
    [InlineData(nameof(HardwareInfo.MacAddressPrimary))]
    public void IsSameMachine_AnySingleIdentifierDiffers_ReturnsFalse(string fieldToChange)
    {
        var activation = NewActivation(Hw(), ActivationStatus.Approved);
        var candidate = Hw();
        switch (fieldToChange)
        {
            case nameof(HardwareInfo.CpuId): candidate.CpuId = "DIFFERENT"; break;
            case nameof(HardwareInfo.MotherboardSerial): candidate.MotherboardSerial = "DIFFERENT"; break;
            case nameof(HardwareInfo.TpmId): candidate.TpmId = "DIFFERENT"; break;
            case nameof(HardwareInfo.MacAddressPrimary): candidate.MacAddressPrimary = "DIFFERENT"; break;
        }

        Assert.False(_svc.IsSameMachine(activation, candidate));
    }

    [Theory]
    [InlineData(ActivationStatus.Approved)]
    [InlineData(ActivationStatus.PendingReview)]
    public void FindMatchingActivation_ConsidersApprovedAndPendingReview(ActivationStatus status)
    {
        var hw = Hw();
        var activation = NewActivation(hw, status);

        var match = _svc.FindMatchingActivation(new[] { activation }, hw);

        Assert.Same(activation, match);
    }

    [Theory]
    [InlineData(ActivationStatus.Rejected)]
    [InlineData(ActivationStatus.Revoked)]
    public void FindMatchingActivation_IgnoresRejectedAndRevoked(ActivationStatus status)
    {
        var hw = Hw();
        var activation = NewActivation(hw, status);

        var match = _svc.FindMatchingActivation(new[] { activation }, hw);

        Assert.Null(match);
    }

    [Fact]
    public void FindMatchingActivation_NoHardwareMatch_ReturnsNull()
    {
        var activation = NewActivation(Hw("1"), ActivationStatus.Approved);

        var match = _svc.FindMatchingActivation(new[] { activation }, Hw("2"));

        Assert.Null(match);
    }
}
