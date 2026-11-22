using System.Globalization;

namespace DynamicIsland;

internal static class WeatherWebData
{
    internal sealed record HourSlot(DateTime At,int? Degrees,Sky Sky,bool Night,double? Wind,double? Direction,string? Event=null);
    internal static string Id(WeatherPlace? place)=>place is null?"auto":FormattableString.Invariant($"{place.Latitude:F5},{place.Longitude:F5}");
    internal static string Condition(Sky sky)=>sky switch { Sky.Clear=>"Ясно",Sky.Cloudy=>"Облачно",Sky.Overcast=>"Пасмурно",Sky.Fog=>"Туман",Sky.Rain=>"Дождь",Sky.Snow=>"Снег",_=>"Гроза" };
    internal static string Icon(Sky sky,bool night)=>sky switch
    { Sky.Clear=>night?"clear-night":"clear-day",Sky.Cloudy=>night?"cloudy-night":"cloudy-day",Sky.Overcast=>"overcast",Sky.Fog=>night?"fog-night":"fog-day",Sky.Rain=>"rain",Sky.Snow=>"snow",_=>"storm" };
    internal static HourSlot[] SlotsFor(WeatherService.Report report)
    {
        DateTime at=report.At==default?DateTime.Today:report.At;
        var slots=new List<HourSlot> { new(at,report.Degrees,report.Sky,report.Night,report.Wind,null) };
        DateTime previous=at;
        foreach(var hour in report.Hours.Take(24))
        {
            DateTime time=hour.Time==default?previous.Date.AddHours(hour.At):hour.Time;
            if(hour.Time==default)while(time<=previous)time=time.AddDays(1);
            if(time<=at)continue;
            slots.Add(new(time,hour.Degrees,hour.Sky,hour.Night,hour.Wind,hour.Direction));previous=time;
        }
        DateTime end=slots[^1].At;
        foreach(var day in report.Days)
        {
            if(day.Sunrise is { } rise&&rise>at&&rise<=end)slots.Add(new(rise,null,Sky.Clear,false,null,null,"Рассвет"));
            if(day.Sunset is { } set&&set>at&&set<=end)slots.Add(new(set,null,Sky.Clear,true,null,null,"Закат"));
        }
        return slots.OrderBy(slot=>slot.At).ToArray();
    }
    internal static double TemperatureAt(HourSlot[] slots,HourSlot slot)
    {
        if(slot.Degrees is { } value)return value;
        var before=slots.LastOrDefault(sample=>sample.At<slot.At&&sample.Degrees.HasValue);
        var after=slots.FirstOrDefault(sample=>sample.At>slot.At&&sample.Degrees.HasValue)??before;
        before??=after;
        if(before is null||after is null)return 0;
        double span=(after.At-before.At).TotalSeconds;
        return before.Degrees!.Value+(after.Degrees!.Value-before.Degrees.Value)*(span>0?(slot.At-before.At).TotalSeconds/span:0);
    }
}