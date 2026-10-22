using System.Collections;
using System.Net;
using System.Text.Json;

namespace AppMare_Maui;

public class HandleData
{
    public static void AddItemToStorage(string child, object data)
    {
        if (data == null) return;
        var ctx = AppSerializationContext.Current;
        if (ctx != null)
        {
            var ti = ctx.GetTypeInfo(data.GetType());
            if (ti != null) { Preferences.Set(child, JsonSerializer.Serialize(data, ti)); return; }
        }
        Preferences.Set(child, JsonSerializer.Serialize(data));
    }

    private static bool DeleteOrUpdate(object lista, object targetDicta)
    {
        if (lista is List<object> list)
        {
            if (targetDicta is Dictionary<string, object> targetDict)
            {
                var existing = list.OfType<Dictionary<string, object>>().FirstOrDefault(d => DictionariesEqual(d, targetDict));
                if (existing != null)
                {
                    if (existing.ContainsKey("ItemCount"))
                    {
                        double currentCount = double.Parse(existing["ItemCount"].ToString());
                        if (currentCount > 1) existing["ItemCount"] = currentCount - 1;
                        else list.Remove(existing);
                    }
                    else list.Remove(existing);
                }
            }
        }
        return true;
    }

    public static object GetElementt(object obj)
    {
     

   if (obj is JsonElement )
        {
            
        }
        else
        {
  var ctx = AppSerializationContext.Current;
            if (ctx != null)
            {
                var ti = ctx.GetTypeInfo(obj.GetType());
                  var txt =   JsonSerializer.Serialize(obj,ti);
              obj = JsonSerializer.Deserialize<JsonElement>(txt);

            }
            else
            {
                  var txt =   JsonSerializer.Serialize(obj);
              obj = JsonSerializer.Deserialize<JsonElement>(txt);
            }
           
        }
        if (obj is JsonElement root)
            switch (root.ValueKind)
            {
                case JsonValueKind.Object:
                    var dic = new Dictionary<string, object>();
                    foreach (var property in root.EnumerateObject())
                        dic.TryAdd(property.Name, GetElementt(property.Value));
                    return dic;
                case JsonValueKind.Array:
                    List<object> list = new();
                    foreach (var element in root.EnumerateArray()) list.Add(GetElementt(element));
                    return list;
                case JsonValueKind.String: return root.GetString();
                case JsonValueKind.Number: return root.GetDouble();
                case JsonValueKind.True: case JsonValueKind.False: return root.GetBoolean();
                case JsonValueKind.Null: return null;
            }
       
        return obj;
    }

    private static bool AddOrUpdate(object lista, object newDicta)
    {
        if (lista is List<object> list && newDicta is Dictionary<string, object> newDict)
        {
            var existing = list.OfType<Dictionary<string, object>>().FirstOrDefault(d => DictionariesEqual(d, newDict));
            if (existing != null)
            {
                if (existing.ContainsKey("ItemCount"))
                    existing["ItemCount"] = double.Parse(existing["ItemCount"].ToString()) + 1;
                else existing["ItemCount"] = 2.0;
            }
            else { newDict["ItemCount"] = 1.0; list.Add(newDict); }
        }
        return true;
    }

    private static bool DictionariesEqual(Dictionary<string, object> d1, Dictionary<string, object> d2)
    {
        if (d1 == null && d2 == null) return true; if (d1 == null || d2 == null) return false;
        var keys1 = d1.Keys.Where(k => k != "ItemCount").OrderBy(k => k).ToList();
        var keys2 = d2.Keys.Where(k => k != "ItemCount").OrderBy(k => k).ToList();
        if (!keys1.SequenceEqual(keys2)) return false;
        foreach (var key in keys1) { if (!ValuesEqual(d1[key], d2[key])) return false; }
        return true;
    }

    private static bool ValuesEqual(object value1, object value2)
    {
        if (value1 == null && value2 == null) return true; if (value1 == null || value2 == null) return false;
        if (value1 is Dictionary<string, object> dict1 && value2 is Dictionary<string, object> dict2) return DictionariesEqual(dict1, dict2);
        if (value1 is IList list1 && value2 is IList list2) return ListsEqual(list1, list2);
        if (IsNumeric(value1) && IsNumeric(value2)) return Convert.ToDouble(value1) == Convert.ToDouble(value2);
        return value1.Equals(value2);
    }

    private static bool IsNumeric(object value) => value is int or long or float or double or decimal or short or byte or sbyte or uint or ulong or ushort;

    private static bool ListsEqual(IList list1, IList list2)
    {
        if (list1.Count != list2.Count) return false;
        for (var i = 0; i < list1.Count; i++) if (!ValuesEqual(list1[i], list2[i])) return false;
        return true;
    }

    public async static void AddToListInStorage(string child, object data)
    {
        var dat = Preferences.Get(child, string.Empty);
        if (dat != string.Empty)
        {
            var obj = JsonDocument.Parse(dat).RootElement;
            var element = GetElementt(obj);
            var item = GetElementt(data);
            AddOrUpdate(element, item);
            var ctx = AppSerializationContext.Current;
            if (ctx != null)
            {
                var ti = ctx.GetTypeInfo(element.GetType());
                if (ti != null) { Preferences.Set(child, JsonSerializer.Serialize(element, ti)); return; }
            }
            Preferences.Set(child, JsonSerializer.Serialize(element));
        }
        else
        {
            var ctx = AppSerializationContext.Current;
            if (ctx != null)
            {
                var ti = ctx.GetTypeInfo(typeof(List<object>));
                if (ti != null) { Preferences.Set(child, JsonSerializer.Serialize(new List<object> { data }, ti)); return; }
            }
            Preferences.Set(child, JsonSerializer.Serialize(new List<object> { data }));
        }
    }

    public async static void RemoveFromListInStorage(string child, object data)
    {
        var dat = Preferences.Get(child, string.Empty);
        if (dat != string.Empty)
        {
            var obj = JsonDocument.Parse(dat).RootElement;
            var element = GetElementt(obj);
            var item = GetElementt(data);
            DeleteOrUpdate(element, item);
            var ctx = AppSerializationContext.Current;
            if (ctx != null)
            {
                var ti = ctx.GetTypeInfo(element.GetType());
                if (ti != null) { Preferences.Set(child, JsonSerializer.Serialize(element, ti)); return; }
            }
            Preferences.Set(child, JsonSerializer.Serialize(element));
        }
    }

    public static class GetElement<T>
    {
        public static T GetItem(string child)
        {
            var val = Preferences.Get(child, string.Empty);
            if (val != string.Empty)
            {
                var ctx = AppSerializationContext.Current;
                if (ctx != null)
                {
                    var ti = ctx.GetTypeInfo(typeof(T));
                    if (ti != null) return (T)JsonSerializer.Deserialize(val, ti);
                }
                return JsonSerializer.Deserialize<T>(val);
            }
            return default;
        }
    }
}
