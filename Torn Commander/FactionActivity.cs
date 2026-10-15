namespace TornCommander;

internal sealed record FactionActivityHour(long FactionId, long MemberId, string Name, DateTime Hour, int Online, int Idle, int Offline);
internal static class FactionActivity
{
    public static void Record(FactionJournal j, FactionSnapshot s, DateTime now)
    {
        if(!s.MembersLoaded||s.Count!=s.Members.Count) return;
        var bucket=new DateTime(now.Year,now.Month,now.Day,now.Hour,now.Minute/5*5,0,DateTimeKind.Utc);
        if(j.ActivityLastFaction==s.Id&&j.ActivityLastBucket>=bucket)return;
        var hour=new DateTime(now.Year,now.Month,now.Day,now.Hour,0,0,DateTimeKind.Utc);
        j.Activity=j.Activity.Where(x=>x.Hour>=now.AddDays(-30)&&x.Hour<=now).ToList();
        foreach(var m in s.Members) {
            var state=m.Activity.ToLowerInvariant();
            if(state is not ("online" or "idle" or "offline"))continue;
            var previous=j.Activity.FirstOrDefault(x=>x.FactionId==s.Id&&x.MemberId==m.Id&&x.Hour==hour)
                ??new(s.Id,m.Id,m.Name,hour,0,0,0);
            j.Activity.RemoveAll(x=>x.FactionId==s.Id&&x.MemberId==m.Id&&x.Hour==hour);
            j.Activity.Add(previous with {Name=m.Name,Online=previous.Online+(state=="online"?1:0),
                Idle=previous.Idle+(state=="idle"?1:0),Offline=previous.Offline+(state=="offline"?1:0)});
        }
        j.ActivityLastBucket=bucket;j.ActivityLastFaction=s.Id;
    }
    public static object Report(FactionJournal j,DateTime now)
    {
        var rows=j.Activity.Where(x=>x.FactionId==j.Snapshot?.Id&&x.Hour>=now.AddDays(-30)&&x.Hour<=now).ToList();
        return new { enabled=j.ActivityEnabled,
            hours=rows.Select(x=>x.Hour).Distinct().Count(),
            members=rows.GroupBy(x=>x.MemberId).Select(g=>new {id=g.Key,name=g.OrderByDescending(x=>x.Hour).First().Name,
                online=g.Sum(x=>x.Online),idle=g.Sum(x=>x.Idle),offline=g.Sum(x=>x.Offline),
                activePercent=Math.Round(100.0*g.Sum(x=>x.Online+x.Idle)/Math.Max(1,g.Sum(x=>x.Online+x.Idle+x.Offline)),1)}).OrderByDescending(x=>x.activePercent).ToList(),
            heatmap=Enumerable.Range(0,7).SelectMany(day=>Enumerable.Range(0,24).Select(hour=>{
                var group=rows.Where(x=>(int)x.Hour.DayOfWeek==day&&x.Hour.Hour==hour).ToList();
                int count=group.Sum(x=>x.Online+x.Idle+x.Offline);
                return new {day,hour,samples=count,activePercent=count==0?(double?)null:Math.Round(100.0*group.Sum(x=>x.Online+x.Idle)/count,1)};
            })).ToList()
        };
    }
}
