namespace TornCommander;
public sealed class LossCommitReceipt
{
    public long AttackId {get;set;}
    public bool VisibleOnly {get;set;}
    public DateTime? FightStartedUtc {get;set;}
    public DateTime? FightEndedUtc {get;set;}
    public string ClaimId { get; set; } = "";
    public string ProfileId { get; set; } = "";
    public string Slot { get; set; } = "";
    public DateTime CommittedUtc { get; set; } = DateTime.UtcNow;
}
public sealed record LossCommitResult(bool Ok, int Remaining, string Message, bool Duplicate = false);
public static class LossAccounting
{
    public static bool NormalizeCompletion(LossOrder order)
    {
        if(order.ClosedUtc.HasValue||order.SourceService=="FFScouter")return false;
        if (order.RequestedLosses <= 0 || order.CompletedTotal < order.RequestedLosses) return false;
        bool changed = order.Status != "Complete" || order.NextA != "Done" || order.NextB != "Done";
        order.Status = "Complete"; order.NextA = "Done"; order.NextB = "Done";
        return changed;
    }
    public static LossCommitResult? Replay(LossOrder order, string claimId, string profileId, string slot)
    {
        order.CommitReceipts ??= new();
        var receipt = order.CommitReceipts.FirstOrDefault(x => x.ClaimId == claimId);
        if (receipt is null) return null;
        return receipt.ProfileId.Equals(profileId, StringComparison.OrdinalIgnoreCase) && receipt.Slot.Equals(slot, StringComparison.OrdinalIgnoreCase)
            ? new(true, order.Remaining, "This loss was already saved; acknowledgement restored without counting again.", true)
            : new(false, order.Remaining, "Saved loss receipt belongs to a different worker.");
    }
    public static LossCommitResult CommitVisible(LossOrder order,string claimId,string profile,string slot,DateTime started,DateTime ended,Action save){
        var replay=Replay(order,claimId,profile,slot);if(replay!=null)return replay;
        if(string.IsNullOrWhiteSpace(claimId)||order.SourceService!="FFScouter"||order.RemoteProfileId!=profile||order.IsClosed||order.Remaining<=0||
           (slot=="A"?order.ProfileAId:slot=="B"?order.ProfileBId:"")!=profile||started>ended||ended-started>TimeSpan.FromMinutes(15))
            return new(false,order.Remaining,"Visible loss did not match an active owning claim.");
        var receipt=new LossCommitReceipt{ClaimId=claimId,ProfileId=profile,Slot=slot,VisibleOnly=true,FightStartedUtc=started,FightEndedUtc=ended};
        var previousBaseline=order.LocalBatchBaseline;
        order.LocalBatchBaseline??=order.TrackedTotal;
        order.LocalBatchClaims.Add(claimId);
        order.CommitReceipts.Add(receipt);try{save();}catch{order.CommitReceipts.Remove(receipt);order.LocalBatchClaims.Remove(claimId);order.LocalBatchBaseline=previousBaseline;throw;}
        return new(true,order.Remaining,"Visible loss tracked locally. Provider confirmation and payout remain separate.");
    }
    // Caller must authorize a live claim before invoking a new commit.
    public static LossCommitResult Commit(LossOrder order, string claimId, string profileId, string slot, Action save,long attackId=0)
    {
        var replay = Replay(order, claimId, profileId, slot);
        if (replay is not null) return replay;
        if(attackId>0&&order.CommitReceipts.Any(x=>x.AttackId==attackId))return new(false,order.Remaining,"This official attack was already counted; no second loss saved.");
        if (string.IsNullOrWhiteSpace(claimId) || slot is not ("A" or "B") ||
            !(slot == "A" ? order.ProfileAId : order.ProfileBId).Equals(profileId, StringComparison.OrdinalIgnoreCase))
            return new(false, order.Remaining, "Invalid worker or claim.");
        if(order.SourceService=="FFScouter"&&attackId<=0)return new(false,order.Remaining,"FFScouter deliveries require an official attack receipt.");
        if(order.SourceService=="FFScouter"&&attackId>0&&order.RemoteAttackIds.Contains(attackId)){
            var known=new LossCommitReceipt{ClaimId=claimId,ProfileId=profileId,Slot=slot,AttackId=attackId};order.CommitReceipts.Add(known);
            bool batchAdded=order.LocalBatchBaseline.HasValue&&order.LocalBatchClaims.Add(claimId);
            try{save();}catch{order.CommitReceipts.Remove(known);if(batchAdded)order.LocalBatchClaims.Remove(claimId);throw;}
            return new(true,order.Remaining,"Provider already counted this verified attack; receipt linked without adding a delivery.",true);
        }
        if (order.IsClosed || order.Remaining <= 0)
            return new(false, order.Remaining, "Requested total was already reached; no loss counted.");
        var previous = (order.CompletedA, order.CompletedB, order.NextA, order.NextB, order.Status);
        var receipt = new LossCommitReceipt { AttackId=attackId, ClaimId = claimId, ProfileId = profileId, Slot = slot };
        if (slot == "A") { order.CompletedA++; order.NextA = "Auto recovery"; }
        else { order.CompletedB++; order.NextB = "Auto recovery"; }
        order.CommitReceipts.Add(receipt);
        bool batchAddedNew=order.SourceService=="FFScouter"&&order.LocalBatchBaseline.HasValue&&order.LocalBatchClaims.Add(claimId);
        if (!NormalizeCompletion(order) && order.Status != "Paused") order.Status = "Auto cycling";
        try { save(); }
        catch
        {
            (order.CompletedA, order.CompletedB, order.NextA, order.NextB, order.Status) = previous;
            order.CommitReceipts.Remove(receipt);
            if(batchAddedNew)order.LocalBatchClaims.Remove(claimId);
            throw;
        }
        return new(true, order.Remaining, $"Loss saved. {order.Remaining} remaining.");
    }
}
