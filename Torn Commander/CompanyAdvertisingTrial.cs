namespace TornCommander;

internal sealed class CompanyAdTrial
{
    public long CompanyId { get; set; }
    public string StartDay { get; set; } = "";
    public long Budget { get; set; }
    public long AllowedDailyLoss { get; set; }
    public int Days { get; set; } = 7;
}
internal static class CompanyAdvertisingTrial
{
    public static object Report(CompanyJournal journal,DateTime now)
    {
        var trial=journal.AdTrial;
        if(trial==null||trial.CompanyId!=journal.ActiveCompanyId)return new {active=false};
        if(!DateOnly.TryParseExact(trial.StartDay,"yyyy-MM-dd",System.Globalization.CultureInfo.InvariantCulture,System.Globalization.DateTimeStyles.None,out var start))
            return new {active=false};
        var today=DateOnly.FromDateTime(now);
        var rows=journal.Days.Where(x=>x.CompanyId==trial.CompanyId&&x.FromApi&&x.DailyIncome.HasValue&&x.DailyAdvertising.HasValue)
            .Select(x=>new {row=x,day=DateOnly.TryParse(x.DayUtc,out var d)?d:DateOnly.MinValue})
            .Where(x=>x.day<today).GroupBy(x=>x.day).Select(g=>g.First()).ToList();
        var prior=rows.Where(x=>x.day>=start.AddDays(-7)&&x.day<start).OrderByDescending(x=>x.day).ToList();
        long? baselineBudget=prior.FirstOrDefault()?.row.DailyAdvertising;
        var baseline=prior.Where(x=>x.row.DailyAdvertising==baselineBudget).Select(x=>x.row).ToList();
        var test=rows.Where(x=>x.day>=start&&x.day<start.AddDays(trial.Days)&&x.row.DailyAdvertising==trial.Budget).Select(x=>x.row).ToList();
        decimal? Mean(List<CompanyObservation> list,Func<CompanyObservation,long?> field)=>list.Count>0&&list.All(x=>field(x).HasValue)
            ?list.Average(x=>(decimal)field(x)!.Value):null;
        decimal? revenueBefore=Mean(baseline,x=>x.DailyIncome),revenueAfter=Mean(test,x=>x.DailyIncome);
        decimal? adsBefore=Mean(baseline,x=>x.DailyAdvertising),adsAfter=Mean(test,x=>x.DailyAdvertising);
        decimal? extraSales=revenueAfter-revenueBefore,extraAds=adsAfter-adsBefore;
        var both=baseline.Concat(test).ToList();
        bool staffChanged=both.Any(x=>!x.Employees.HasValue)||both.Select(x=>x.Employees).Distinct().Count()>1;
        bool starsChanged=both.Any(x=>!x.Rating.HasValue)||both.Select(x=>x.Rating).Distinct().Count()>1;
        bool enough=baseline.Count>=3&&test.Count>=3;
        decimal? knownLoss=test.Count>0?test.Average(x=>(decimal)Math.Max(0,(x.DailyAdvertising??0)+(x.DailyPayroll??0)+(x.DailyStockCost??0)-x.DailyIncome!.Value)):null;
        decimal? peakKnownLoss=test.Count>0?test.Max(x=>(decimal)Math.Max(0,(x.DailyAdvertising??0)+(x.DailyPayroll??0)+(x.DailyStockCost??0)-x.DailyIncome!.Value)):null;
        bool costsComplete=test.Count>0&&test.All(x=>x.DailyPayroll.HasValue&&x.DailyStockCost.HasValue);

        var state=!enough?"COLLECTING OBSERVATIONS":extraSales>0&&extraSales-extraAds>0?"MORE SALES, BETTER AFTER ADS":
            extraSales>0?"GROWTH AT AN EXTRA COST":"NO OBSERVED SALES LIFT";
        string note=!enough?"Aim for at least 3 recorded days at each budget; 7 is a better initial comparison.":
            extraSales>0&&extraSales-extraAds<=0?"Sales rose, but the extra advertising cost more than the extra revenue. Decide whether that growth is worth the subsidy.":
            extraSales>0?"Observed revenue growth exceeded extra advertising spend. Missing supplies/payroll and unrelated changes can still affect actual profit.":
            "The recorded test has not shown a sales increase over the baseline. Review the higher spend before keeping it.";
        if(peakKnownLoss>trial.AllowedDailyLoss)note+=" At least one recorded day exceeds your chosen daily loss allowance.";
        if(staffChanged||starsChanged)note+=" Staffing or star counts changed / are missing, which weakens the comparison.";
        return new {active=true,start=trial.StartDay,end=start.AddDays(trial.Days-1).ToString("yyyy-MM-dd"),
            budget=trial.Budget,allowedDailyLoss=trial.AllowedDailyLoss,baselineDays=baseline.Count,testDays=test.Count,
            baselineBudget,revenueBefore,revenueAfter,customersBefore=Mean(baseline,x=>x.DailyCustomers),customersAfter=Mean(test,x=>x.DailyCustomers),
            knownLoss,peakKnownLoss,costsComplete,overAllowance=peakKnownLoss>trial.AllowedDailyLoss,
            extraSales,extraAds,afterAdsChange=extraSales-extraAds,
            extraRevenuePerAdDollar=extraAds>0?extraSales/extraAds:null,state,note,
            disclaimer="Observed daily snapshots, not causal proof or audited profit. Today is excluded. Keep prices and positions steady; record actual advertising costs. A trial does not change Torn's ad budget."};
    }
}
