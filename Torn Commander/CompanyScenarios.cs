namespace TornCommander;
internal sealed record CompanyFinance(long CompanyId,string Day,long? Income,long? Advertising,long? Payroll,long? Supplies,int? Staff,int? Stars,long? Funds);
internal sealed record CompanyScenario(long? Revenue,long? Advertising,long? Payroll,long? Supplies,long? Net,long? ExtraRevenueNeeded,double? RunwayDays,string Note);
internal static class CompanyScenarios
{
    public static CompanyScenario Compare(CompanyFinance current,long? ads,long? payroll,long? supplies,double revenueChangePercent)
    {
        if(ads<0||payroll<0||supplies<0||!double.IsFinite(revenueChangePercent)||revenueChangePercent< -100||revenueChangePercent>1000)throw new InvalidDataException("Invalid financial scenario.");
        ads??=current.Advertising;payroll??=current.Payroll;supplies??=current.Supplies;
        long? revenue=current.Income.HasValue?(long)Math.Round(current.Income.Value*(decimal)(1+revenueChangePercent/100)):null;
        long? net=revenue.HasValue&&ads.HasValue&&payroll.HasValue&&supplies.HasValue?revenue-ads-payroll-supplies:null;
        long? extra=current.Income.HasValue&&ads.HasValue&&payroll.HasValue&&supplies.HasValue?Math.Max(0,ads.Value+payroll.Value+supplies.Value-current.Income.Value):null;
        double? runway=net is <0&&current.Funds.HasValue?Math.Round(current.Funds.Value/(double)-net.Value,1):null;
        return new(revenue,ads,payroll,supplies,net,extra,runway,"What-if calculation using your revenue assumption; not a sales forecast. Missing costs remain unknown. Runway assumes this daily loss continues and excludes unrecorded expenses.");
    }
    public static object AdvertisingCohorts(IEnumerable<CompanyFinance> days)
    {
        var comparable=days.Where(x=>string.CompareOrdinal(x.Day,DateTime.UtcNow.ToString("yyyy-MM-dd"))<0&&x.Income.HasValue&&x.Advertising.HasValue&&x.Staff.HasValue&&x.Stars.HasValue).GroupBy(x=>(x.CompanyId,x.Day)).Select(g=>g.Last());
        return comparable.GroupBy(x=>(x.CompanyId,x.Advertising,x.Staff,x.Stars)).Select(g=>new{budget=g.Key.Advertising,staff=g.Key.Staff,stars=g.Key.Stars,days=g.Count(),averageIncome=Math.Round(g.Average(x=>(decimal)x.Income!.Value)),averageAfterAds=Math.Round(g.Average(x=>(decimal)(x.Income!.Value-x.Advertising!.Value))),eligible=g.Count()>=3}).OrderByDescending(x=>x.averageAfterAds).ToList();
    }
}
