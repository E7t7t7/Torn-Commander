using System.Text.Json;
using System.Text.RegularExpressions;
namespace TornCommander;

public sealed class SmartMedicalSettings
{
    public bool Enabled { get; set; } = true;
    public bool Automatic { get; set; } // Explicit opt-in; never inherited from the old FAK loop.
    public bool AutoDetect { get; set; } = true;
    public bool AutoResume { get; set; }
    public bool BloodBags { get; set; } // Only universal O-negative, with verified IV eligibility.
    public double ManualBonusPercent { get; set; }
    public int ManualMaximumMinutes { get; set; } = 360;
    public int ReserveMinutes { get; set; }
    public Dictionary<int,bool> Permissions { get; set; } = new(){{68,true},{67,true},{66,true},{739,false}};
    public Dictionary<int,int> Reserves { get; set; } = new();
    public Dictionary<int,long> Prices { get; set; } = new();
    public void Validate(){
        if(!double.IsFinite(ManualBonusPercent)||ManualBonusPercent is <0 or >50||ManualMaximumMinutes is <360 or >540||ReserveMinutes<0||ReserveMinutes>539)throw new InvalidDataException("Invalid medical bonus, capacity or cooldown reserve.");
        if(Reserves.Any(x=>x.Value is <0 or >100000000)||Prices.Any(x=>x.Value is <0 or >1000000000)||Permissions.Keys.Concat(Reserves.Keys).Concat(Prices.Keys).Any(x=>!SmartMedicalOptimizer.AllowedId(x)))throw new InvalidDataException("Only the fixed medical allowlist may be configured. Drugs and faction supplies are prohibited.");
    }
}
internal sealed record MedicalItem(int Id,string Name,int HospitalSeconds,int CooldownSeconds,long BasePrice);
internal sealed record MedicalObservation(string ProfileId,int PlayerId,DateTime ObservedUtc,DateTime HospitalUntilUtc,int CooldownSeconds,int MaximumSeconds,double BonusPercent,bool IvVerified,string Verification,Dictionary<int,int> Inventory,int Life=0,int MaximumLife=0);
internal sealed record MedicalPlan(string State,string Reason,int[] Items,int CooldownSeconds,int HospitalSeconds,int FakBaselineSeconds,DateTime? AvailableUtc=null);
internal static class SmartMedicalOptimizer
{
    // Deliberately closed: no caller-supplied item definitions, drug IDs or armory mode.
    internal static readonly MedicalItem[] Catalog={new(68,"Small First Aid Kit",1200,600,7500),new(67,"First Aid Kit",2400,900,15000),new(66,"Morphine",4200,1200,20000),new(739,"Blood Bag : O-",7200,1800,30000)};
    public static bool AllowedId(int id)=>Catalog.Any(x=>x.Id==id);
    public static MedicalItem Item(int id)=>Catalog.Single(x=>x.Id==id);
    private sealed record Choice(int[] Items,long Expense,double Scarcity);
    private static bool Better(Choice a,Choice b)=>a.Items.Length<b.Items.Length||(a.Items.Length==b.Items.Length&&(a.Expense<b.Expense||(a.Expense==b.Expense&&a.Scarcity<b.Scarcity)));
    public static MedicalPlan Plan(MedicalObservation o,SmartMedicalSettings settings,DateTime now){
        settings.Validate();
        MedicalPlan Stop(string state,string why,DateTime? at=null)=>new(state,why,Array.Empty<int>(),0,0,0,at);
        if(now-o.ObservedUtc>TimeSpan.FromSeconds(45)||o.ObservedUtc>now.AddSeconds(2))return Stop("Unverified","Medical state is stale; refresh before acting.");
        if(o.CooldownSeconds<0||o.MaximumSeconds is <21600 or >32400||!double.IsFinite(o.BonusPercent)||o.BonusPercent is <0 or >50||o.Inventory.Any(x=>x.Value<0))return Stop("Unverified","Invalid medical state.");
        int hospital=(int)Math.Ceiling((o.HospitalUntilUtc-now).TotalSeconds);
        if(hospital<=0)return Stop("Recovered","No remaining hospitalization; no item is needed.");
        if(hospital>86400)return Stop("Unverified","Hospital duration exceeds supported loss recovery; review manually.");
        double unit=600*(1+o.BonusPercent/100);int target=(int)Math.Ceiling(hospital/unit);
        int cap=o.MaximumSeconds-settings.ReserveMinutes*60;
        if(cap<=0)return Stop("Paused","Cooldown reserve leaves no medical capacity.");
        if(o.CooldownSeconds>=cap)return Stop("Cooldown","Medical cooldown/reserve prevents another item. Drugs remain disabled.",now.AddSeconds(o.CooldownSeconds-cap+1));
        var available=Catalog.Where(x=>settings.Permissions.GetValueOrDefault(x.Id)&& (x.Id!=739||(settings.BloodBags&&o.IvVerified))).Select(x=>(Item:x,Count:Math.Max(0,o.Inventory.GetValueOrDefault(x.Id)-settings.Reserves.GetValueOrDefault(x.Id)))).Where(x=>x.Count>0).ToList();
        if(available.Count==0)return Stop("Supplies","No permitted personal medical supplies above reserves. Drugs remain disabled.");
        int room=cap-o.CooldownSeconds;
        int maxCost=settings.ReserveMinutes>0?room:room-1+available.Max(x=>x.Item.CooldownSeconds);
        // Bounded knapsack using binary inventory bundles. Healing is in exact base
        // ten-minute units; bonuses scale all supported items equally. Keep the
        // largest item cost so the final item can legitimately overshoot the cap.
        var dp=new Dictionary<(int Cost,int Heal,int Last),Choice>{{(0,0,0),new(Array.Empty<int>(),0,0)}};
        foreach(var entry in available){
            // Extra copies beyond a full recovery cannot improve any objective.
            // Bound work by hospitalization, not the player's stockpile or cooldown capacity.
            int useful=(target+entry.Item.HospitalSeconds/600-1)/(entry.Item.HospitalSeconds/600);
            int remaining=Math.Min(entry.Count,Math.Min(useful,maxCost/entry.Item.CooldownSeconds)),bundle=1;
            while(remaining>0){int take=Math.Min(bundle,remaining);remaining-=take;bundle*=2;
                int cost=entry.Item.CooldownSeconds*take,heal=entry.Item.HospitalSeconds/600*take;
                foreach(var pair in dp.ToArray()){
                    int c=pair.Key.Cost+cost;if(c>maxCost)continue;
                    var key=(Cost:c,Heal:Math.Min(target,pair.Key.Heal+heal),Last:Math.Max(pair.Key.Last,entry.Item.CooldownSeconds));
                    var choice=new Choice(pair.Value.Items.Concat(Enumerable.Repeat(entry.Item.Id,take)).ToArray(),pair.Value.Expense+settings.Prices.GetValueOrDefault(entry.Item.Id,entry.Item.BasePrice)*take,pair.Value.Scarcity+(double)take/entry.Count);
                    if(!dp.TryGetValue(key,out var prior)||Better(choice,prior))dp[key]=choice;
                }
            }
        }
        var feasible=dp.Where(x=>x.Key.Heal>=target&&(settings.ReserveMinutes>0?x.Key.Cost<=room:x.Key.Cost-x.Key.Last<room)).OrderBy(x=>x.Key.Cost).ThenBy(x=>x.Value.Items.Length).ThenBy(x=>x.Value.Expense).ThenBy(x=>x.Value.Scarcity).FirstOrDefault();
        if(feasible.Value==null){
            if(o.CooldownSeconds>0){
                var later=Plan(o with{CooldownSeconds=0},settings,now);
                if(later.State=="Ready"){
                    int needed=settings.ReserveMinutes>0?later.CooldownSeconds:later.CooldownSeconds-later.Items.Max(x=>Item(x).CooldownSeconds)+1;
                    return Stop("Cooldown","A complete medical recovery needs more cooldown capacity. Waiting preserves supplies and the configured reserve; drugs remain disabled.",now.AddSeconds(Math.Max(1,needed-room)));
                }
            }
            return Stop("Supplies","No complete recovery fits permitted supplies. No partial or drug fallback was selected.");
        }
        // Put a largest-cooldown item last, making every preceding use eligible.
        var sequence=feasible.Value.Items.OrderBy(x=>Item(x).CooldownSeconds).ToArray();
        int baseline=(int)Math.Ceiling(hospital/(2400*(1+o.BonusPercent/100)))*900;
        return new("Ready","Minimum medical cooldown; ties use fewer items, then configured cost and plentiful supplies.",sequence,feasible.Key.Cost,hospital,baseline);
    }
    internal static bool VerifyUnused(MedicalObservation before,MedicalObservation after,int itemId,DateTime now){
        if(!AllowedId(itemId)||before.ProfileId!=after.ProfileId||before.PlayerId!=after.PlayerId||after.ObservedUtc<before.ObservedUtc||now-after.ObservedUtc>TimeSpan.FromSeconds(45)||after.ObservedUtc>now.AddSeconds(2)||now-before.ObservedUtc>TimeSpan.FromSeconds(90))return false;
        if(!before.Inventory.ContainsKey(itemId)||!after.Inventory.ContainsKey(itemId)||before.Inventory[itemId]!=after.Inventory[itemId])return false;
        double elapsed=(after.ObservedUtc-before.ObservedUtc).TotalSeconds;
        return Math.Abs(after.CooldownSeconds-Math.Max(0,before.CooldownSeconds-elapsed))<=5&&after.HospitalUntilUtc==before.HospitalUntilUtc;
    }
    internal static bool VerifyUse(MedicalObservation before,MedicalObservation after,int itemId,DateTime now){
        if(!AllowedId(itemId)||before.ProfileId!=after.ProfileId||before.PlayerId!=after.PlayerId||after.ObservedUtc<before.ObservedUtc||now-after.ObservedUtc>TimeSpan.FromSeconds(45)||after.ObservedUtc>now.AddSeconds(2)||now-before.ObservedUtc>TimeSpan.FromSeconds(180))return false;
        var item=Item(itemId);if(after.Inventory.GetValueOrDefault(itemId)!=before.Inventory.GetValueOrDefault(itemId)-1)return false;
        double elapsed=(after.ObservedUtc-before.ObservedUtc).TotalSeconds;
        double delta=after.CooldownSeconds-Math.Max(0,before.CooldownSeconds-elapsed);
        bool reduced=after.HospitalUntilUtc<=now||before.HospitalUntilUtc-after.HospitalUntilUtc>=TimeSpan.FromSeconds(item.HospitalSeconds*(1+before.BonusPercent/100)-5);
        bool restored=true;
        if(before.MaximumLife>0){double percent=itemId==68?.05:itemId==67?.10:itemId==66?.15:.30;int expected=Math.Min(before.MaximumLife,before.Life+(int)Math.Floor(before.MaximumLife*percent*(1+before.BonusPercent/100)));restored=after.MaximumLife==before.MaximumLife&&after.Life>=expected-1;}
        return Math.Abs(delta-item.CooldownSeconds)<=45&&reduced&&restored;
    }
    internal static (double Bonus,int Maximum,bool Iv,string Status) Modifiers(string json,SmartMedicalSettings settings){
        if(!settings.AutoDetect)return(settings.ManualBonusPercent,settings.ManualMaximumMinutes*60,false,"Manual override — user supplied, not API verified; blood eligibility still requires API verification.");
        using var d=JsonDocument.Parse(json);var perks=d.RootElement.GetProperty("perks");double bonus=0;int capacity=21600;bool iv=false,unknown=false;
        foreach(var category in perks.EnumerateObject())foreach(var e in category.Value.EnumerateArray()){
            var text=e.GetString()??"";
            if(Regex.IsMatch(text,@"withdraw and deliver blood|use (?:filled )?blood bags",RegexOptions.IgnoreCase))iv=true;
            if(Regex.IsMatch(text,@"medical item effectiveness",RegexOptions.IgnoreCase)){
                var match=Regex.Match(text,@"(\d+(?:\.\d+)?)\s*%",RegexOptions.IgnoreCase);
                if(match.Success)bonus+=double.Parse(match.Groups[1].Value,System.Globalization.CultureInfo.InvariantCulture);else unknown=true;
            }
            if(Regex.IsMatch(text,@"medical.*cooldown|cooldown.*medical",RegexOptions.IgnoreCase)){
                var hours=Regex.Match(text,@"(\d+(?:\.\d+)?)\s*(?:hours?|hrs?)\b",RegexOptions.IgnoreCase);
                var minutes=Regex.Match(text,@"(\d+)\s*(?:minutes?|mins?)\b",RegexOptions.IgnoreCase);
                if(hours.Success||minutes.Success){var seconds=(hours.Success?double.Parse(hours.Groups[1].Value,System.Globalization.CultureInfo.InvariantCulture)*3600:0)+(minutes.Success?int.Parse(minutes.Groups[1].Value)*60:0);capacity=21600+(int)seconds;}else unknown=true;
            }
        }
        if(unknown||bonus>50||capacity>32400)throw new InvalidDataException("Unrecognized medical modifier. Use the clearly marked manual override after checking Torn's perks.");
        return(bonus,capacity,iv,"Detected from user/perks; base capacity plus reported extension. Unknown blood types are excluded; only universal O-negative is supported.");
    }
}
