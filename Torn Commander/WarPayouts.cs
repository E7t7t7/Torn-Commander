using System.Text.Json;
namespace TornCommander;

internal sealed record WarParticipant(long Id,string Name,int Attacks,decimal Score);
internal sealed class WarPayoutJournal
{
    public long WarId { get; set; }
    public long FactionId { get; set; }
    public long End { get; set; }
    public List<WarParticipant> Members { get; set; } = new();
    public long Budget { get; set; }
    public int AttackPercent { get; set; } = 50;
    public int MinimumAttacks { get; set; }
    public long? Cap { get; set; }
    public DateTime ScannedUtc { get; set; }
}
internal sealed record WarPayment(long Id,string Name,int Attacks,decimal Score,long Amount,bool Eligible);
internal static class WarPayouts
{
    public static WarPayoutJournal Read(string json,long warId,long factionId)
    {
        using var doc=JsonDocument.Parse(json);
        var root=doc.RootElement.GetProperty("rankedwarreport");
        if(root.GetProperty("id").GetInt64()!=warId)throw new InvalidDataException("Wrong war report returned.");
        var sides=root.GetProperty("factions").EnumerateArray().Where(x=>x.GetProperty("id").GetInt64()==factionId).ToList();
        if(sides.Count!=1)throw new InvalidDataException("This report does not include your scanned faction.");
        var j=new WarPayoutJournal {WarId=warId,FactionId=factionId,End=root.GetProperty("end").GetInt64(),ScannedUtc=DateTime.UtcNow};
        foreach(var m in sides[0].GetProperty("members").EnumerateArray()){
            var row=new WarParticipant(m.GetProperty("id").GetInt64(),m.GetProperty("name").GetString()??"",m.GetProperty("attacks").GetInt32(),m.GetProperty("score").GetDecimal());
            if(row.Id<=0||row.Attacks<0||row.Score<0||j.Members.Any(x=>x.Id==row.Id))throw new InvalidDataException("Invalid war participant.");
            j.Members.Add(row);
        }
        if(j.Members.Count>200)throw new InvalidDataException("Report exceeds faction member limit.");
        return j;
    }
    public static List<WarPayment> Calculate(WarPayoutJournal j)
    {
        if(j.Budget<0||j.Budget>1000000000000L||j.AttackPercent<0||j.AttackPercent>100||j.MinimumAttacks<0||j.Cap<0)
            throw new InvalidDataException("Invalid payout settings.");
        var eligible=j.Members.Where(x=>x.Attacks>=j.MinimumAttacks).ToList();
        decimal attacks=eligible.Sum(x=>(decimal)x.Attacks),score=eligible.Sum(x=>x.Score);
        if(j.Budget>0&&eligible.Count>0&&((j.AttackPercent>0&&attacks==0)||(j.AttackPercent<100&&score==0)))
            throw new InvalidDataException("The selected split has no qualifying attacks or score. Change the split or minimum.");
        var weights=eligible.ToDictionary(x=>x.Id,x=>
            (attacks>0?x.Attacks/attacks*j.AttackPercent:0)+(score>0?x.Score/score*(100-j.AttackPercent):0));
        var exact=j.Members.ToDictionary(x=>x.Id,x=>0m);
        var pending=eligible.Where(x=>weights[x.Id]>0).ToList();
        decimal remaining=j.Budget,cap=j.Cap??j.Budget;
        while(pending.Count>0&&remaining>0){
            decimal total=pending.Sum(x=>weights[x.Id]);
            var capped=pending.Where(x=>remaining*weights[x.Id]/total>=cap).ToList();
            if(capped.Count>0){
                foreach(var x in capped){exact[x.Id]=cap;remaining-=cap;pending.Remove(x);}
            } else {
                foreach(var x in pending)exact[x.Id]=remaining*weights[x.Id]/total;
                remaining=0;break;
            }
        }
        var amounts=exact.ToDictionary(x=>x.Key,x=>(long)decimal.Floor(x.Value));
        long allocated=j.Budget-(long)remaining;
        long remainder=allocated-amounts.Values.Sum();
        foreach(var id in exact.OrderByDescending(x=>x.Value-decimal.Floor(x.Value)).ThenBy(x=>x.Key).Select(x=>x.Key))
            if(remainder>0&&amounts[id]<cap){amounts[id]++;remainder--;}
        return j.Members.Select(x=>new WarPayment(x.Id,x.Name,x.Attacks,x.Score,amounts[x.Id],x.Attacks>=j.MinimumAttacks))
            .OrderByDescending(x=>x.Amount).ThenBy(x=>x.Id).ToList();
    }
}
internal static class ReportCsv
{
    public static string Cell(object? value)
    {
        var text=Convert.ToString(value,System.Globalization.CultureInfo.InvariantCulture)??"";
        if(value is string&&text.Length>0&&"=+-@\t\r\n".Contains(text.TrimStart().FirstOrDefault()))text="'"+text;
        return "\""+text.Replace("\"","\"\"")+"\"";
    }
    public static string Row(params object?[] values)=>string.Join(",",values.Select(Cell));
}
