using System.Globalization;
using System.Text.Json;
namespace TornCommander;
internal interface IForeignStockProvider
{
    string Id {get;}
    Task<IReadOnlyList<ItemObservation>> FetchAsync(CancellationToken ct=default);
}
internal sealed class ForeignStockProvider : IForeignStockProvider
{
    private static readonly HttpClient Http=new(new HttpClientHandler{AllowAutoRedirect=false}){Timeout=TimeSpan.FromSeconds(25)};
    private readonly string? _clientKey;
    public string Id {get;}
    public ForeignStockProvider(string id,string? clientKey=null){if(id is not ("YATA" or "Torn Intel"))throw new ArgumentException("Unknown provider");Id=id;_clientKey=clientKey;}
    public async Task<IReadOnlyList<ItemObservation>> FetchAsync(CancellationToken ct=default)
    {
        using var request=new HttpRequestMessage(HttpMethod.Get,Id=="YATA"?"https://yata.yt/api/v1/travel/export/":"https://torn-intel.com/api/v1/public/foreign-stock");
        request.Headers.UserAgent.ParseAdd("TornCommander/"+(typeof(ForeignStockProvider).Assembly.GetName().Version?.ToString(3)??"unknown"));
        if(Id=="Torn Intel"){if(string.IsNullOrWhiteSpace(_clientKey))throw new ProviderFailure("Approval required",null);request.Headers.Add("X-Torn-Intel-Key",_clientKey);}
        using var response=await Http.SendAsync(request,ct);
        if((int)response.StatusCode is 401 or 403)throw new ProviderFailure("Approval required",null);
        if((int)response.StatusCode==429){var delay=response.Headers.RetryAfter?.Delta??(response.Headers.RetryAfter?.Date-DateTimeOffset.UtcNow);throw new ProviderFailure("Rate limited",DateTime.UtcNow.Add(delay is {TotalSeconds:>0}?delay.Value:TimeSpan.FromMinutes(5)));}
        if(!response.IsSuccessStatusCode)throw new ProviderFailure("Provider unavailable",null);
        if(response.Content.Headers.ContentLength is > 5000000)throw new InvalidDataException("Stock response too large");
        return Parse(await response.Content.ReadAsStringAsync(ct),Id,DateTime.UtcNow);
    }
    public static string Country(string code)=>code.ToLowerInvariant() switch{"mex"=>"Mexico","cay"=>"Cayman Islands","can"=>"Canada","haw"=>"Hawaii","uni" or "uk"=>"United Kingdom","arg"=>"Argentina","swi"=>"Switzerland","jap"=>"Japan","chi"=>"China","uae"=>"United Arab Emirates","sou"=>"South Africa",_=>code};
    public static IReadOnlyList<ItemObservation> Parse(string json,string source,DateTime now)
    {
        if(json.Length>5000000)throw new InvalidDataException("Stock response too large");
        using var doc=JsonDocument.Parse(json);var root=doc.RootElement;var result=new List<ItemObservation>();
        if(source=="YATA"){
            if(!root.TryGetProperty("stocks",out var countries)||countries.ValueKind!=JsonValueKind.Object)throw new InvalidDataException("Missing country stocks");
            foreach(var c in countries.EnumerateObject()){if(!c.Value.TryGetProperty("update",out var time)||!time.TryGetInt64(out var unix)||unix<1500000000||unix>2200000000)continue;var observed=DateTimeOffset.FromUnixTimeSeconds(unix).UtcDateTime;if(!c.Value.TryGetProperty("stocks",out var items)||items.ValueKind!=JsonValueKind.Array)continue;foreach(var item in items.EnumerateArray())Add(item,Country(c.Name),observed,"id","name","cost",null);}
        }else{
            if(!root.TryGetProperty("countries",out var countries)||countries.ValueKind!=JsonValueKind.Array)throw new InvalidDataException("Missing country stocks");
            foreach(var c in countries.EnumerateArray()){if(!c.TryGetProperty("updatedAt",out var time)||!DateTimeOffset.TryParse(time.GetString(),CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal,out var date))continue;var place=Country(c.GetProperty("country").GetString()??"");if(!c.TryGetProperty("items",out var items)||items.ValueKind!=JsonValueKind.Array)continue;foreach(var item in items.EnumerateArray())Add(item,place,date.UtcDateTime,"itemId","itemName","cost","marketValue");}
        }
        if(result.Count==0)throw new InvalidDataException("No timestamped stock observations");return result;
        void Add(JsonElement item,string country,DateTime observed,string idField,string nameField,string costField,string? marketField){
            if(!item.TryGetProperty(idField,out var i)||!i.TryGetInt32(out var id)||id<=0||!item.TryGetProperty("quantity",out var q)||!q.TryGetInt32(out var qty)||qty<0||!item.TryGetProperty(costField,out var cost)||!cost.TryGetInt64(out var buy)||buy<0||observed>now.AddMinutes(2)||observed<now.AddDays(-366))return;
            string name=item.TryGetProperty(nameField,out var n)?n.GetString()??"":"";long? market=null;if(marketField!=null&&item.TryGetProperty(marketField,out var m)&&m.TryGetInt64(out var price)&&price>0)market=price;
            if(name.Length>0&&country.Length>0)result.Add(new(source,country,id,name,observed,now,qty,buy,market));
        }
    }
}
internal sealed class ProviderFailure : Exception
{
    public DateTime? RetryUtc {get;}
    public ProviderFailure(string status,DateTime? retry):base(status){RetryUtc=retry;}
}
