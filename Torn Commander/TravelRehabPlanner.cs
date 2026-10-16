namespace TornCommander;

public static class TravelRehabPlanner
{
    public static int FlightMinutes(string ticket,bool book)=> (ticket,book) switch {
        ("Airstrip",false)=>116,("Airstrip",true)=>87,("Private",false)=>83,("Private",true)=>62,
        ("Business",false)=>50,("Business",true)=>38,(_,true)=>125,_=>166
    };
    public static TravelItineraryPlan Build(IEnumerable<TravelForecastItem> items,AppSettings settings,
        DateTime readyUtc,DateTime now,DateTime? endUtc,bool rehabFirst,int stayMinutes,TravelSituation? actual=null)
    {
        if(!rehabFirst)return TravelItineraryEngine.Build(items,settings,readyUtc,now,endUtc);
        var end=endUtc??readyUtc.AddHours(24);
        int flight=FlightMinutes(settings.TravelFlightType,settings.TravelMailingBook);
        var depart=readyUtc;
        var arrival=depart.AddMinutes(flight);
        bool alreadySwiss=actual!=null&&(actual.State is "outbound" or "abroad")&&
            (actual.Destination.Equals("Switzerland",StringComparison.OrdinalIgnoreCase)||actual.Destination.Equals("swi",StringComparison.OrdinalIgnoreCase));
        if(!alreadySwiss&&(readyUtc<now.AddMinutes(-2)||readyUtc>now.AddHours(48)))return new(){StartUtc=readyUtc,EndUtc=end,Note="The departure time must be now or within the next 48 hours."};
        if(alreadySwiss){
            depart=actual!.DepartUtc??now;
            arrival=actual.State=="outbound"?actual.ArrivalUtc??DateTime.MaxValue:now;
            if(actual.FlightMinutes is >0 and <=720)flight=actual.FlightMinutes.Value;
        }
        if(arrival==DateTime.MaxValue)return new(){StartUtc=now,EndUtc=end,Note="Switzerland landing time is unknown. Sync travel before planning rehab."};
        var back=arrival.AddMinutes(Math.Clamp(stayMinutes,1,120)+flight);
        if(back>end)return new(){StartUtc=readyUtc,EndUtc=end,Note="The rehab trip will not fit before this plan ends. Choose a later planning window."};
        var plan=TravelItineraryEngine.Build(items,settings,back.AddMinutes(Math.Clamp(settings.TravelTransferMinutes,0,120)),now,end);
        plan.StartUtc=alreadySwiss?now:readyUtc;
        foreach(var leg in plan.Legs)leg.Number++;
        plan.Legs.Insert(0,new(){Number=1,Kind="rehab",Country="Switzerland",Item="Rehabilitation",
            DepartUtc=depart,ArriveUtc=arrival,ReturnUtc=back,Availability=alreadySwiss?"Your Switzerland trip — rehab before returning":"Priority rehab stop — fees not included",
            PlanningConfidence=0});
        plan.Note="Rehab first: allow "+Math.Clamp(stayMinutes,1,120)+" minutes in Switzerland. Rehab fees and any airfare are excluded; bring enough cash. This plans the stop but does not fly or pay for treatment. Mark Rehab done after your visit. "+plan.Note;
        return plan;
    }
}
