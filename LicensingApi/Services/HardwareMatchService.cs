using LicensingApi.Dtos;
using LicensingCore.Entities;

namespace LicensingApi.Services;

public interface IHardwareMatchService
{
    /// <summary>True only if all four identifiers match — the "same machine" rule.</summary>
    bool IsSameMachine(Activation existing, HardwareInfo candidate);

    /// <summary>
    /// Finds the best existing activation (if any) for a license that matches the given
    /// hardware exactly. Only considers activations that aren't Rejected/Revoked.
    /// </summary>
    Activation? FindMatchingActivation(IEnumerable<Activation> licenseActivations, HardwareInfo candidate);
}

public class HardwareMatchService : IHardwareMatchService
{
    public bool IsSameMachine(Activation existing, HardwareInfo candidate) =>
        existing.CpuId == candidate.CpuId &&
        existing.MotherboardSerial == candidate.MotherboardSerial &&
        existing.TpmId == candidate.TpmId &&
        existing.MacAddressPrimary == candidate.MacAddressPrimary;

    public Activation? FindMatchingActivation(IEnumerable<Activation> licenseActivations, HardwareInfo candidate) =>
        licenseActivations
            .Where(a => a.Status is ActivationStatus.Approved or ActivationStatus.PendingReview)
            .FirstOrDefault(a => IsSameMachine(a, candidate));
}
