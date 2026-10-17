using System.Text.Json;
namespace TornCommander;

public sealed partial class MainForm
{
    private List<TradeEntry> _visibleTradeEntries = new();
    private void RecordCompletedTrade(JsonElement request)
    {
        if(request.GetProperty("profileId").GetString()!=CurrentProfileId)throw new InvalidDataException("Profile changed.");
        if(!Guid.TryParse(request.GetProperty("receipt").GetString(),out var receipt))throw new InvalidDataException("Invalid entry receipt.");
        string receiptId=receipt.ToString();
        if(_trades.Any(x=>x.ProfileId==CurrentProfileId&&x.Id==receiptId)){UpdateModernDashboard();return;}
        string item=request.GetProperty("item").GetString()?.Trim()??"";
        if(item.Length<1||item.Length>120)throw new InvalidDataException("Enter an item name, up to 120 characters.");
        int quantity=request.GetProperty("quantity").GetInt32();
        if(quantity<1||quantity>1000000)throw new InvalidDataException("Quantity must be 1–1,000,000.");
        decimal Read(string field){
            if(!request.GetProperty(field).TryGetDecimal(out var value)||value<0||value>1000000000000m)
                throw new InvalidDataException("Enter nonnegative buy, sell and actual fee amounts.");
            return value;
        }
        var trade=new TradeEntry {Id=receiptId,ProfileId=CurrentProfileId,Date=DateTime.Now,Item=item,Quantity=quantity,
            BuyEach=Read("buy"),SellEach=Read("sell"),SellingFees=Read("fees"),Notes="Completed sale recorded manually; fees supplied by user."};
        var next=_trades.Append(trade).ToList();
        Storage.SaveAtomic(Storage.TradesPath,next);_trades=next;
        RebindTrades();FlashStatus("Completed sale saved. After-fee margin: "+trade.ProfitAfterFees?.ToString("C0"));UpdateModernDashboard();
    }
    private void ExportTradeResults()
    {
        SaveCsvReport("Commander_CompletedTrades_"+DateTime.UtcNow.ToString("yyyyMMdd"),
            new[]{ReportCsv.Row("Date","Item","Quantity","Buy each","Sell each","Gross margin","Actual selling fees","Net after fees")}
            .Concat(_trades.Where(x=>x.ProfileId==CurrentProfileId).OrderBy(x=>x.Date)
                .Select(x=>ReportCsv.Row(x.Date.ToString("O"),x.Item,x.Quantity,x.BuyEach,x.SellEach,x.Profit,x.SellingFees,x.ProfitAfterFees))));
    }

}
