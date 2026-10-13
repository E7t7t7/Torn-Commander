namespace TornCommander;
public sealed class LossCommitReceipt
{
    public string ClaimId { get; set; } = "";
    public string ProfileId { get; set; } = "";
    public string Slot { get; set; } = "";
    public DateTime CommittedUtc { get; set; } = DateTime.UtcNow;
}
public sealed record LossCommitResult(bool Ok, int Remaining, string Message, bool Duplicate = false);
public static class LossAccounting
{
    public static LossCommitResult? Replay(LossOrder order, string claimId, string profileId, string slot)
    {
        order.CommitReceipts ??= new();
        var receipt = order.CommitReceipts.FirstOrDefault(x => x.ClaimId == claimId);
        if (receipt is null) return null;
        return receipt.ProfileId.Equals(profileId, StringComparison.OrdinalIgnoreCase) && receipt.Slot.Equals(slot, StringComparison.OrdinalIgnoreCase)
            ? new(true, order.Remaining, "This loss was already saved; acknowledgement restored without counting again.", true)
            : new(false, order.Remaining, "Saved loss receipt belongs to a different worker.");
    }
    // Caller must authorize a live claim before invoking a new commit.
    public static LossCommitResult Commit(LossOrder order, string claimId, string profileId, string slot, Action save)
    {
        var replay = Replay(order, claimId, profileId, slot);
        if (replay is not null) return replay;
        if (string.IsNullOrWhiteSpace(claimId) || slot is not ("A" or "B") ||
            !(slot == "A" ? order.ProfileAId : order.ProfileBId).Equals(profileId, StringComparison.OrdinalIgnoreCase))
            return new(false, order.Remaining, "Invalid worker or claim.");
        if (order.Status == "Complete" || order.Remaining <= 0)
            return new(false, order.Remaining, "Requested total was already reached; no loss counted.");
        var previous = (order.CompletedA, order.CompletedB, order.NextA, order.NextB, order.Status);
        var receipt = new LossCommitReceipt { ClaimId = claimId, ProfileId = profileId, Slot = slot };
        if (slot == "A") { order.CompletedA++; order.NextA = "Auto recovery"; }
        else { order.CompletedB++; order.NextB = "Auto recovery"; }
        order.CommitReceipts.Add(receipt);
        if (order.Status != "Paused") order.Status = order.Remaining <= 0 ? "Final recovery" : "Auto cycling";
        try { save(); }
        catch
        {
            (order.CompletedA, order.CompletedB, order.NextA, order.NextB, order.Status) = previous;
            order.CommitReceipts.Remove(receipt);
            throw;
        }
        return new(true, order.Remaining, $"Loss saved. {order.Remaining} remaining.");
    }
}
